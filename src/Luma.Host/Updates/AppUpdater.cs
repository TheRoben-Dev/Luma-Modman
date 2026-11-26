using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Luma.Core;

namespace Luma.Host.Updates;

public sealed record AppRelease(Version Version, string Tag, string TarballUrl, string SumsUrl, long Size, string Notes);

/// <summary>A release package for one OS: the asset name and the app folder inside it.</summary>
/// <param name="Zip">A .zip (Windows) rather than a .tar.gz (Linux).</param>
public sealed record UpdatePackage(string Asset, string Folder, string Binary, string Launcher, bool Zip)
{
    public static readonly UpdatePackage Linux = new("Luma-linux-x86_64.tar.gz", "Luma", "Luma.x86_64", "luma", false);
    public static readonly UpdatePackage Windows = new("Luma-windows-x86_64.zip", "Luma", "Luma.exe", "luma-cli.exe", true);

    /// <summary>The package for the OS Luma runs on.</summary>
    public static UpdatePackage Here => Platform.IsWindows ? Windows : Linux;

    public static readonly UpdatePackage[] All = [Linux, Windows];
}

/// <summary>
/// In-app self-update from this project's GitHub Releases (the same assets the installer uses).
/// Download and checksum check happen next to the install folder (never /tmp, which is a small
/// RAM disk on SteamOS/Bazzite), then the install folder is swapped by rename and the previous
/// version is kept as "&lt;install&gt;.previous" for rollback. On Windows, where the folder of a
/// running program can't be renamed, its files are moved one by one instead (running files can). Mods, profiles and settings live
/// elsewhere and are never touched.
/// </summary>
public sealed class AppUpdater
{
    public const string Repo = "TheRoben-Dev/Luma-Modman";
    public const string LatestApi = $"https://api.github.com/repos/{Repo}/releases/latest";

    private static readonly string[] Repos = [Repo];

    /// <summary>The app binary in an install folder, or null.</summary>
    public static string? AppBinary(string dir) =>
        UpdatePackage.All.Select(l => Path.Combine(dir, l.Binary)).FirstOrDefault(File.Exists);

    private readonly HttpClient _http;

    /// <param name="installDir">Folder holding Luma.x86_64 (Luma.exe); null = the running app's folder.</param>
    /// <param name="package">Which release package to use; null = this OS's.</param>
    public AppUpdater(HttpClient? http = null, string? installDir = null, Version? current = null, UpdatePackage? package = null)
    {
        Package = package ?? UpdatePackage.Here;
        _http = http ?? DefaultClient();
        InstallDir = installDir ?? (Environment.ProcessPath is { } exe ? Path.GetDirectoryName(exe)! : AppContext.BaseDirectory);
        CurrentVersion = current ?? CurrentAppVersion();
    }

    public string InstallDir { get; }
    public UpdatePackage Package { get; }
    public Version CurrentVersion { get; }
    public string PreviousDir => Trim(InstallDir) + ".previous";

    private static string Trim(string dir) => dir.TrimEnd('/', '\\');

    /// <summary>
    /// Self-update only works for the portable install (installer or tarball): a folder with the
    /// app binary and the launcher that we can write next to. Not for dev builds or packages.
    /// </summary>
    public bool CanSelfUpdate =>
        File.Exists(Path.Combine(InstallDir, Package.Binary)) && File.Exists(Path.Combine(InstallDir, Package.Launcher)) &&
        Directory.GetParent(Trim(InstallDir)) is { } parent && IsWritable(parent.FullName);

    public bool CanRollback => Directory.Exists(PreviousDir) && AppBinary(PreviousDir) != null;

    /// <summary>Version of the copy kept for rollback, from its README.txt ("Luma 0.4.0 (Linux x86_64)").</summary>
    public string? PreviousVersion
    {
        get
        {
            try
            {
                var first = File.ReadLines(Path.Combine(PreviousDir, "README.txt")).FirstOrDefault() ?? "";
                var rest = first.StartsWith("Luma ", StringComparison.Ordinal) ? first[5..] : null;
                return ParseVersion(rest) is { } v ? DisplayVersion(v) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public static Version CurrentAppVersion()
    {
        var info = typeof(AppUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ParseVersion(info) ?? new Version(0, 0, 0);
    }

    public static string DisplayVersion(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    /// <summary>"v0.2.0", "0.2.0+abc123" -> 0.2.0. Null if not a version.</summary>
    public static Version? ParseVersion(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        s = s.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['+', '-', ' ']);
        if (cut >= 0)
        {
            s = s[..cut];
        }
        return Version.TryParse(s, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    /// <summary>Returns the latest release if it is newer than the running app, otherwise null.</summary>
    public async Task<AppRelease?> CheckAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(LatestApi, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        resp.EnsureSuccessStatusCode();
        var release = ParseRelease(await resp.Content.ReadAsStringAsync(ct), Package);
        return release != null && release.Version > CurrentVersion ? release : null;
    }

    public static AppRelease? ParseRelease(string json, UpdatePackage? package = null)
    {
        package ??= UpdatePackage.Here;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
        {
            return null;
        }
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version == null || !root.TryGetProperty("assets", out var assets))
        {
            return null;
        }
        string? tarball = null, sums = null;
        long size = 0;
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            var url = a.GetProperty("browser_download_url").GetString();
            if (name == package.Asset)
            {
                tarball = url;
                size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
            }
            else if (name == "SHA256SUMS")
            {
                sums = url;
            }
        }
        if (tarball == null || sums == null || !IsOfficial(tarball) || !IsOfficial(sums))
        {
            return null;
        }
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        return new AppRelease(version, tag, tarball, sums, size, notes);
    }

    /// <summary>Only this repository's release downloads are accepted.</summary>
    public static bool IsOfficial(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "github.com" &&
        Repos.Any(r => u.AbsolutePath.StartsWith($"/{r}/releases/download/", StringComparison.Ordinal));

    /// <summary>
    /// Downloads, verifies and installs the release. Returns the path of the new app binary.
    /// On any failure nothing is changed. The running app keeps working (its files are renamed,
    /// not deleted) until it restarts into the new version.
    /// </summary>
    public async Task<string> InstallAsync(AppRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!CanSelfUpdate)
        {
            throw new InvalidOperationException("This copy of Luma can't update itself. Re-run the installer instead.");
        }
        var parent = Directory.GetParent(Trim(InstallDir))!.FullName;
        var stage = Path.Combine(parent, ".luma-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stage);
        try
        {
            var tarball = Path.Combine(stage, Path.GetFileName(new Uri(release.TarballUrl).AbsolutePath));
            await DownloadAsync(release.TarballUrl, tarball, release.Size, progress, ct);
            var sums = await _http.GetStringAsync(release.SumsUrl, ct);
            Verify(tarball, sums);
            progress?.Report(-1);

            var extracted = Path.Combine(stage, "x");
            Directory.CreateDirectory(extracted);
            if (Package.Zip)
            {
                ZipFile.ExtractToDirectory(tarball, extracted, overwriteFiles: false);
            }
            else
            {
                await using var file = File.OpenRead(tarball);
                await using var gz = new GZipStream(file, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, extracted, overwriteFiles: false, ct);
            }
            if (!File.Exists(Path.Combine(extracted, Package.Folder, Package.Binary)))
            {
                throw new InvalidDataException("The update package is missing the app binary.");
            }
            var newApp = Path.Combine(extracted, Package.Folder);

            // Swap: current -> .previous (replacing an older .previous), new -> current.
            if (Directory.Exists(PreviousDir))
            {
                Directory.Delete(PreviousDir, recursive: true);
            }
            Move(InstallDir, PreviousDir);
            try
            {
                Move(newApp, InstallDir);
            }
            catch
            {
                if (OperatingSystem.IsWindows())
                {
                    MoveTree(InstallDir, Path.Combine(stage, "failed"));
                }
                Move(PreviousDir, InstallDir);
                throw;
            }
            return Path.Combine(InstallDir, Package.Binary);
        }
        finally
        {
            try
            {
                Directory.Delete(stage, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Swaps back to the version kept from the last update.</summary>
    public string Rollback()
    {
        if (!CanRollback)
        {
            throw new InvalidOperationException("There is no previous version to go back to.");
        }
        var temp = Trim(InstallDir) + ".rollback-" + Guid.NewGuid().ToString("N")[..6];
        Move(InstallDir, temp);
        Move(PreviousDir, InstallDir);
        Move(temp, PreviousDir);
        return AppBinary(InstallDir)!;
    }

    /// <summary>Renames a folder; on Windows moves its files into <paramref name="to"/> one by one (see the class notes).</summary>
    private static void Move(string from, string to)
    {
        if (OperatingSystem.IsWindows())
        {
            MoveTree(from, to);
        }
        else
        {
            Directory.Move(from, to);
        }
    }

    /// <summary>
    /// Moves every file under <paramref name="from"/> to the same place under <paramref name="to"/>,
    /// then removes the folders left empty (a folder still in use stays, empty, and is reused).
    /// </summary>
    internal static void MoveTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
        {
            var dest = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Move(file, dest, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(from))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Starts the (new) app binary with the same arguments. The caller then quits.</summary>
    public static void Relaunch(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        Process.Start(psi);
    }

    public static void Verify(string file, string sums)
    {
        var name = Path.GetFileName(file);
        var expected = sums.Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].TrimStart('*').Trim() == name)
            .Select(p => p[0].ToLowerInvariant())
            .FirstOrDefault() ?? throw new InvalidDataException($"No checksum published for {name}.");
        using var f = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
        if (actual != expected)
        {
            throw new InvalidDataException("The download is damaged (checksum mismatch). Nothing was changed.");
        }
    }

    private async Task DownloadAsync(string url, string dest, long expectedSize, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? expectedSize;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buffer = new byte[1 << 16];
        long done = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            done += n;
            if (total > 0)
            {
                progress?.Report((double)done / total);
            }
        }
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".luma-write-" + Guid.NewGuid().ToString("N")[..6]);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static HttpClient DefaultClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Luma", DisplayVersion(CurrentAppVersion())));
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return h;
    }
}
