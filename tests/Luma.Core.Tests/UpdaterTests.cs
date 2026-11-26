using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Luma.Host.Updates;
using Xunit;

namespace Luma.Core.Tests;

public class UpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "luma-upd-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private const string Base = "https://github.com/TheRoben-Dev/Luma-Modman/releases/download/v0.2.0/";

    private static string ReleaseJson(string tag = "v0.2.0", string host = "github.com", bool prerelease = false) => $$"""
        {"tag_name":"{{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"body":"notes",
         "assets":[{"name":"Luma-linux-x86_64.tar.gz","size":10,"browser_download_url":"https://{{host}}/TheRoben-Dev/Luma-Modman/releases/download/{{tag}}/Luma-linux-x86_64.tar.gz"},
                   {"name":"Luma-windows-x86_64.zip","size":12,"browser_download_url":"https://{{host}}/TheRoben-Dev/Luma-Modman/releases/download/{{tag}}/Luma-windows-x86_64.zip"},
                   {"name":"SHA256SUMS","browser_download_url":"https://{{host}}/TheRoben-Dev/Luma-Modman/releases/download/{{tag}}/SHA256SUMS"}]}
        """;

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.1.1+abc123", "0.1.1")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("nightly", null)]
    public void ParsesVersions(string s, string? expected) =>
        Assert.Equal(expected, AppUpdater.ParseVersion(s) is { } v ? AppUpdater.DisplayVersion(v) : null);

    [Fact]
    public void ParsesReleaseAndRejectsForeignHostsAndPrereleases()
    {
        var r = AppUpdater.ParseRelease(ReleaseJson(), UpdatePackage.Linux);
        Assert.NotNull(r);
        Assert.Equal(new Version(0, 2, 0), r!.Version);
        Assert.Null(AppUpdater.ParseRelease(ReleaseJson(host: "evil.example"), UpdatePackage.Linux));
        Assert.Null(AppUpdater.ParseRelease(ReleaseJson(prerelease: true), UpdatePackage.Linux));
    }

    [Fact]
    public async Task OnlyNewerReleasesAreOffered()
    {
        var http = new HttpClient(new Fake(new() { [AppUpdater.LatestApi] = System.Text.Encoding.UTF8.GetBytes(ReleaseJson()) }));
        Assert.NotNull(await new AppUpdater(http, _root, new Version(0, 1, 1), UpdatePackage.Linux).CheckAsync());
        Assert.Null(await new AppUpdater(http, _root, new Version(0, 2, 0), UpdatePackage.Linux).CheckAsync());
    }

    [Fact]
    public async Task InstallsSwapsKeepsPreviousAndRollsBack()
    {
        var install = Path.Combine(_root, "Applications", "Luma");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "Luma.x86_64"), "old");
        File.WriteAllText(Path.Combine(install, "luma"), "old");

        var tarball = MakeTarball("new");
        var sums = $"{Convert.ToHexString(SHA256.HashData(tarball)).ToLowerInvariant()}  Luma-linux-x86_64.tar.gz\n";
        var http = new HttpClient(new Fake(new()
        {
            [Base + "Luma-linux-x86_64.tar.gz"] = tarball,
            [Base + "SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes(sums),
        }));
        var u = new AppUpdater(http, install, new Version(0, 1, 1), UpdatePackage.Linux);
        Assert.True(u.CanSelfUpdate);
        var release = AppUpdater.ParseRelease(ReleaseJson(), UpdatePackage.Linux)!;

        var exe = await u.InstallAsync(release);
        Assert.Equal("new", File.ReadAllText(exe));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True((File.GetUnixFileMode(exe) & UnixFileMode.UserExecute) != 0);
        }
        Assert.Equal("old", File.ReadAllText(Path.Combine(u.PreviousDir, "Luma.x86_64")));
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(install)!, ".luma-update-*"));

        u.Rollback();
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "Luma.x86_64")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(u.PreviousDir, "Luma.x86_64")));
    }

    [Fact]
    public void PicksThePackageForEachOs()
    {
        Assert.EndsWith("/Luma-linux-x86_64.tar.gz", AppUpdater.ParseRelease(ReleaseJson(), UpdatePackage.Linux)!.TarballUrl);
        var win = AppUpdater.ParseRelease(ReleaseJson(), UpdatePackage.Windows)!;
        Assert.EndsWith("/Luma-windows-x86_64.zip", win.TarballUrl);
        Assert.Equal(12, win.Size);
    }

    [Fact]
    public async Task InstallsTheWindowsZip()
    {
        var install = Path.Combine(_root, "Programs", "Luma");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "Luma.exe"), "old");
        File.WriteAllText(Path.Combine(install, "luma-cli.exe"), "old");
        byte[] zip;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var name in new[] { "Luma/Luma.exe", "Luma/luma-cli.exe", "Luma/data_Luma.App_windows_x86_64/Luma.App.dll" })
                {
                    using var w = new StreamWriter(z.CreateEntry(name).Open());
                    w.Write("new");
                }
            }
            zip = ms.ToArray();
        }
        var sums = $"{Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant()}  Luma-windows-x86_64.zip\n";
        var http = new HttpClient(new Fake(new()
        {
            [Base + "Luma-windows-x86_64.zip"] = zip,
            [Base + "SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes(sums),
        }));
        var u = new AppUpdater(http, install, new Version(0, 1, 1), UpdatePackage.Windows);
        Assert.True(u.CanSelfUpdate);
        var exe = await u.InstallAsync(AppUpdater.ParseRelease(ReleaseJson(), UpdatePackage.Windows)!);
        Assert.Equal(Path.Combine(install, "Luma.exe"), exe);
        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "data_Luma.App_windows_x86_64", "Luma.App.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(u.PreviousDir, "Luma.exe")));
        u.Rollback();
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "Luma.exe")));
        Assert.False(Directory.Exists(Path.Combine(install, "data_Luma.App_windows_x86_64")));
    }

    [Fact]
    public void MoveTreeMovesFilesIntoAnExistingFolder()
    {
        var from = Path.Combine(_root, "from");
        var to = Path.Combine(_root, "to");
        Directory.CreateDirectory(Path.Combine(from, "sub"));
        Directory.CreateDirectory(Path.Combine(to, "sub"));  // left over, empty, from an earlier swap
        File.WriteAllText(Path.Combine(from, "a.exe"), "a");
        File.WriteAllText(Path.Combine(from, "sub", "b.dll"), "b");
        AppUpdater.MoveTree(from, to);
        Assert.Equal("b", File.ReadAllText(Path.Combine(to, "sub", "b.dll")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(to, "a.exe")));
        Assert.False(Directory.Exists(from));
    }

    [Fact]
    public async Task BadChecksumChangesNothing()
    {
        var install = Path.Combine(_root, "Applications", "Luma");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "Luma.x86_64"), "old");
        File.WriteAllText(Path.Combine(install, "luma"), "old");
        var http = new HttpClient(new Fake(new()
        {
            [Base + "Luma-linux-x86_64.tar.gz"] = MakeTarball("new"),
            [Base + "SHA256SUMS"] = System.Text.Encoding.UTF8.GetBytes(new string('0', 64) + "  Luma-linux-x86_64.tar.gz\n"),
        }));
        var u = new AppUpdater(http, install, new Version(0, 1, 1), UpdatePackage.Linux);
        await Assert.ThrowsAsync<InvalidDataException>(() => u.InstallAsync(AppUpdater.ParseRelease(ReleaseJson(), UpdatePackage.Linux)!));
        Assert.Equal("old", File.ReadAllText(Path.Combine(install, "Luma.x86_64")));
        Assert.False(u.CanRollback);
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(install)!, ".luma-update-*"));
    }

    private static byte[] MakeTarball(string content, string folder = "Luma", string binary = "Luma.x86_64", string launcher = "luma")
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gz))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, folder + "/") { Mode = (UnixFileMode)0b111_101_101 });
            foreach (var name in new[] { binary, launcher })
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, folder + "/" + name)
                {
                    Mode = (UnixFileMode)0b111_101_101,
                    DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
                });
            }
        }
        return ms.ToArray();
    }

    private sealed class Fake(Dictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(responses.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
