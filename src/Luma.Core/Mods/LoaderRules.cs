using System.Text.RegularExpressions;
using Luma.Core.Games;

namespace Luma.Core.Mods;

/// <summary>
/// The Godot-free rules behind loader support: which loaders a mod needs (by its files or its listed
/// requirements), where a loader's archive starts and where it goes, and which release file to take.
/// </summary>
public static class LoaderRules
{
    /// <summary>The folder of the game's program, relative to the game folder ("Pal/Binaries/Win64"), "" when it's at the top.</summary>
    public static string ExeDir(GameProfile p)
    {
        var exe = p.Detect.FirstOrDefault(d => d.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? p.Detect.FirstOrDefault();
        if (exe == null)
        {
            return "";
        }
        var dir = Path.GetDirectoryName(exe.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
        return dir.Trim('/');
    }

    /// <summary>Fills in "{exeDir}" and tidies slashes.</summary>
    public static string Resolve(string path, GameProfile p)
    {
        var s = path.Replace('\\', '/').Replace("{exeDir}", ExeDir(p));
        return string.Join('/', s.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Where the loader's anchor file goes in this game, relative to the game folder.</summary>
    public static string InstallDir(GameProfile p, LoaderInfo l)
    {
        if (p.Loader == l.Id && p.LoaderDir != null)
        {
            return Resolve(p.LoaderDir, p);
        }
        if (l.InstallDir != null)
        {
            return Resolve(l.InstallDir, p);
        }
        return Resolve(l.LauncherDir, p);
    }

    /// <summary>The launcher's path in this game (follows <see cref="InstallDir"/>), or null.</summary>
    public static string? Launcher(GameProfile p, LoaderInfo l)
    {
        if (l.Launcher == null)
        {
            return null;
        }
        if (l.NativeLauncher != null && p.Runtime == "native" && !Platform.IsWindows)
        {
            return Resolve(l.NativeLauncher, p);
        }
        if (p.Loader == l.Id && p.LoaderDir != null)
        {
            var name = Path.GetFileName(l.Launcher.Replace('\\', '/'));
            var dir = InstallDir(p, l);
            return dir.Length == 0 ? name : dir + "/" + name;
        }
        return Resolve(l.Launcher, p);
    }

    /// <summary>The launcher's arguments for this game: the game's own when it names some, else the loader's.</summary>
    public static IReadOnlyList<string> LaunchArgs(GameProfile p, LoaderInfo l) =>
        p.Loader == l.Id && p.LoaderArgs.Count > 0 ? p.LoaderArgs : l.LaunchArgs;

    /// <summary>True when one of the files (paths relative to the game or mods folder) shows a mod needs the loader.</summary>
    public static bool NeededBy(LoaderInfo l, IEnumerable<string> files)
    {
        if (l.Signatures.Count == 0)
        {
            return false;
        }
        foreach (var f in files)
        {
            var path = "/" + f.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            foreach (var sig in l.Signatures)
            {
                var s = sig.ToLowerInvariant();
                if (s.StartsWith("*."))
                {
                    if (path.EndsWith(s[1..], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                else if (s.EndsWith('/') ? path.Contains(s, StringComparison.Ordinal) : path.EndsWith(s, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>The loaders (of <paramref name="candidates"/>) a mod with these files needs.</summary>
    public static List<string> Needs(IEnumerable<LoaderInfo> candidates, IReadOnlyCollection<string> files) =>
        candidates.Where(l => NeededBy(l, files)).Select(l => l.Id).ToList();

    /// <summary>True when a requirement's name on a mod site means this loader ("Cyber Engine Tweaks", "RED4ext").</summary>
    public static bool IsDependency(LoaderInfo l, string name)
    {
        if (name.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase))
        {
            return false;  // Thunderstore's own BepInEx package: fetched as a mod from there
        }
        var n = Normal(name);
        if (n.Length == 0)
        {
            return false;
        }
        return new[] { l.Name, l.Id }.Concat(l.Aliases).Select(Normal).Any(a => a.Length > 0 && (n == a || (a.Length >= 5 && n.StartsWith(a, StringComparison.Ordinal))));
    }

    private static string Normal(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Adds the loaders that <paramref name="ids"/> need themselves (RED4ext for ArchiveXL), in install order: needed ones first.</summary>
    public static List<string> WithRequired(IEnumerable<string> ids, IReadOnlyDictionary<string, LoaderInfo> loaders)
    {
        var result = new List<string>();
        void Add(string id, int depth)
        {
            if (depth > 8 || result.Contains(id) || !loaders.TryGetValue(id, out var l))
            {
                return;
            }
            foreach (var r in l.Requires)
            {
                Add(r, depth + 1);
            }
            result.Add(id);
        }
        foreach (var id in ids)
        {
            Add(id, 0);
        }
        return result;
    }

    /// <summary>
    /// Where the loader's files start in its unpacked archive and where that folder goes in the game.
    /// The anchor file is looked up; when its folder already ends with the install folder
    /// ("bin/x64/plugins/cyber_engine_tweaks.asi"), the archive is laid out from the game folder.
    /// Without an anchor, a single wrapper folder is unwrapped and the rest goes in the install folder.
    /// </summary>
    public static (string Root, string Prefix) Locate(string dir, GameProfile p, LoaderInfo l)
    {
        var install = InstallDir(p, l);
        if (l.AnchorFile is { } anchor && FindFile(dir, anchor) is { } found)
        {
            var at = Path.GetDirectoryName(found)!;
            var relDir = Path.GetRelativePath(dir, at).Replace('\\', '/');
            relDir = relDir == "." ? "" : relDir;
            var segs = install.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var have = relDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // How much of the install folder the archive already has above the anchor ("x64/plugins" of "bin/x64/plugins").
            for (var k = Math.Min(segs.Length, have.Length); k > 0; k--)
            {
                if (have[^k..].Select(x => x.ToLowerInvariant()).SequenceEqual(segs[^k..].Select(x => x.ToLowerInvariant())))
                {
                    var root = at;
                    for (var i = 0; i < k; i++)
                    {
                        root = Path.GetDirectoryName(root)!;
                    }
                    return (root, string.Join('/', segs[..^k]));
                }
            }
            return (at, install);
        }
        var r = dir;
        while (Directory.GetDirectories(r) is [var only] && Directory.GetFiles(r).All(f => ModLayout.IsDoc(Path.GetFileName(f))))
        {
            r = only;
        }
        return (r, install);
    }

    internal static string? FindFile(string dir, string name) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Count(c => c == '/' || c == '\\'))
            .FirstOrDefault();

    /// <summary>True when an archive a player added by hand is this loader (it holds the loader's own anchor file).</summary>
    public static bool IsLoaderArchive(string dir, LoaderInfo l) =>
        !l.SharedAnchor && l.AnchorFile is { } a && FindFile(dir, a) != null;

    private static readonly Regex Bad = new(@"(pdb|debug|symbol|source|src|linux|macos|osx|darwin|arm64|aarch64|i686|win32|x86(?!_64)|32bit|dev|sdk|headers)", RegexOptions.IgnoreCase);

    /// <summary>
    /// The release file to install: the one <paramref name="pattern"/> matches, else the Windows archive
    /// among them (zip/7z, not debug symbols, sources or other systems). Null when none fits.
    /// </summary>
    public static string? PickAsset(IReadOnlyList<string> names, string? pattern)
    {
        if (pattern != null)
        {
            var re = new Regex(pattern, RegexOptions.IgnoreCase);
            return names.FirstOrDefault(n => re.IsMatch(n));
        }
        var archives = names.Where(n => Archive.Extensions.Any(e => n.EndsWith(e, StringComparison.OrdinalIgnoreCase)) && !Bad.IsMatch(Path.GetFileNameWithoutExtension(n))).ToList();
        if (archives.Count == 0)
        {
            return null;
        }
        return archives
            .OrderByDescending(n => Regex.IsMatch(n, "win|x64|x86_64", RegexOptions.IgnoreCase) ? 1 : 0)
            .ThenBy(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n.Length)
            .First();
    }

    /// <summary>The links on an HTML page (href values, made absolute against <paramref name="page"/>).</summary>
    public static IEnumerable<string> Links(string html, string page)
    {
        var baseUri = new Uri(page);
        foreach (Match m in Regex.Matches(html, "href\\s*=\\s*[\"']([^\"'#]+)[\"']", RegexOptions.IgnoreCase))
        {
            if (Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(m.Groups[1].Value), out var u) && u.Scheme is "http" or "https")
            {
                yield return u.ToString();
            }
        }
    }

    /// <summary>The first link on a page whose file name matches the loader's pattern.</summary>
    public static string? PickLink(string html, string page, string pattern)
    {
        var re = new Regex(pattern, RegexOptions.IgnoreCase);
        return Links(html, page).FirstOrDefault(u => re.IsMatch(Uri.UnescapeDataString(new Uri(u).AbsolutePath)));
    }

    /// <summary>A version from a file name ("skse64_2_02_06.7z" → "2.02.06", "cet_1.35.0.zip" → "1.35.0").</summary>
    public static string VersionFrom(string fileName)
    {
        var m = Regex.Match(Path.GetFileNameWithoutExtension(fileName), @"(?<![A-Za-z\d])(\d+(?:[._]\d+)+)");
        return m.Success ? m.Groups[1].Value.Replace('_', '.') : "";
    }
}
