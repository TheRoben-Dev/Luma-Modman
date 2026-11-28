using Luma.Core.Games;

namespace Luma.Core.Mods;

/// <summary>Which folder a mod file is placed relative to.</summary>
public enum DeployRoot
{
    /// <summary>The game's install folder.</summary>
    Game,

    /// <summary>The profile's mods folder (which may be outside the game folder).</summary>
    Mods,
}

public sealed record LayoutEntry(string Source, DeployRoot Root, string Target);

/// <summary>
/// Works out where the files of an unpacked mod go. Archives come in many shapes: wrapped in a
/// folder, laid out from the game folder ("BepInEx/plugins/x.dll", "Data/x.esp"), from the mods
/// folder ("plugins/x.dll", "~mods/x.pak"), or just the files. The profile's modsPath tells which
/// folder names to look for.
/// </summary>
public static class ModLayout
{
    private static readonly string[] RootFiles =
        ["winhttp.dll", "version.dll", "dinput8.dll", "dxgi.dll", "d3d9.dll", "d3d11.dll", "dsound.dll", "doorstop_config.ini", "xinput1_3.dll"];

    /// <summary>
    /// Folders a game or loader reads by name: an archive holding just one of them ("SKSE/Plugins/x.dll",
    /// "meshes/...") is laid out from it, never unwrapped into it.
    /// </summary>
    private static readonly HashSet<string> KnownFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "skse", "f4se", "sfse", "nvse", "obse", "fose", "mwse", "meshes", "textures", "scripts", "interface", "sound", "music", "seq",
        "strings", "shadersfx", "lodsettings", "materials", "grass", "video", "calientetools", "netscriptframework",
        "red4ext", "r6", "archive", "engine", "bin", "mods", "reframework", "natives", "ue4ss", "lml", "nativepc", "bepinex",
    };

    /// <summary>Files that describe the package rather than being part of the mod.</summary>
    public static bool IsDoc(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "manifest.json" or "icon.png" or "thunderstore.toml" || n.StartsWith("readme") || n.StartsWith("changelog") ||
               n.StartsWith("license") || n.EndsWith(".md");
    }

    /// <param name="dir">The unpacked archive.</param>
    /// <param name="modFolder">A folder name for the mod, used where mods sit in folders of their own (BepInEx plugins).</param>
    /// <param name="gamePrefix">Put everything under this folder of the game (loaders), skipping detection.</param>
    /// <summary>Every file under <paramref name="root"/>, in the game folder under <paramref name="prefix"/> (a loader's files, as laid out).</summary>
    public static List<LayoutEntry> Fixed(string root, string prefix)
    {
        prefix = prefix.Trim('/');
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (f, rel: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(x => !x.rel.StartsWith(".smapi-", StringComparison.Ordinal))
            .Select(x => new LayoutEntry(x.f, DeployRoot.Game, prefix.Length == 0 ? x.rel : prefix + "/" + x.rel))
            .ToList();
    }

    /// <param name="loaders">The game's loaders (ids): UE4SS Lua and C++ mods go in ue4ss/Mods when the game uses UE4SS.</param>
    public static List<LayoutEntry> Plan(string dir, GameProfile profile, string modFolder, string? gamePrefix = null, IReadOnlyCollection<string>? loaders = null)
    {
        if (gamePrefix == null && (loaders ?? profile.AllLoaders.ToList()).Contains("ue4ss") && Ue4ssMods(dir, profile, modFolder) is { } ue)
        {
            return ue;
        }
        var root = dir;
        var segs = profile.ModsPathHere.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var outside = profile.ModsPathHere.StartsWith('{');
        var inGame = outside ? [] : segs;
        var markers = new HashSet<string>(segs.Where(s => !s.StartsWith('{')).Select(s => s.ToLowerInvariant())) { "bepinex" };

        // Unwrap single wrapper folders ("MyMod-1.2/...") that aren't one of the game's folders.
        var marker = profile.ModFolderMarker;
        while (true)
        {
            if (marker != null && (Directory.Exists(Path.Combine(root, marker)) || Directory.GetDirectories(root).Any(d => Directory.Exists(Path.Combine(d, marker)))))
            {
                break;  // at the mod folder ("modX/content") or the folder holding mod folders
            }
            var dirs = Directory.GetDirectories(root);
            var files = Directory.GetFiles(root).Where(f => !IsDoc(Path.GetFileName(f))).ToList();
            if (dirs.Length == 1 && files.Count == 0 && !markers.Contains(Path.GetFileName(dirs[0]).ToLowerInvariant()) && !KnownFolders.Contains(Path.GetFileName(dirs[0])))
            {
                root = dirs[0];
                continue;
            }
            break;
        }

        var top = Directory.EnumerateFileSystemEntries(root)
            .Select(Path.GetFileName)
            .Where(n => n != null && !IsDoc(n))
            .Select(n => n!)
            .ToList();
        var lower = top.Select(t => t.ToLowerInvariant()).ToHashSet();

        DeployRoot target;
        var prefix = "";
        if (gamePrefix == null && marker != null && Directory.Exists(Path.Combine(root, marker)))
        {
            // The mod's files without their folder ("content/scripts/..."): they get one.
            var name = Folder(modFolder);
            prefix = name.StartsWith("mod", StringComparison.OrdinalIgnoreCase) ? name : "mod" + name;
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => (f, rel: Path.GetRelativePath(root, f).Replace('\\', '/')))
                .Where(x => x.rel.Contains('/') || !IsDoc(x.rel))
                .Select(x => new LayoutEntry(x.f, DeployRoot.Mods, prefix + "/" + x.rel)).ToList();
        }
        if (gamePrefix == null && profile.FileRules.Count > 0 && Flat(root, profile) is { } flat)
        {
            return flat;
        }
        if (gamePrefix != null)
        {
            target = DeployRoot.Game;
            prefix = gamePrefix.Trim('/');
        }
        else if (lower.Any(t => RootFiles.Contains(t) || t.EndsWith(".exe")))
        {
            target = DeployRoot.Game;  // loader or game files: laid out from the game folder
        }
        else if (Matches(inGame, lower) is { } k)
        {
            target = DeployRoot.Game;
            prefix = string.Join('/', inGame[..k]);
        }
        else if (outside && segs.Length > 1 && lower.Contains(segs[^1].ToLowerInvariant()))
        {
            // "Mods/x" for a mods folder outside the game: strip the folder name itself.
            root = Path.Combine(root, top.First(t => t.Equals(segs[^1], StringComparison.OrdinalIgnoreCase)));
            target = DeployRoot.Mods;
        }
        else
        {
            target = DeployRoot.Mods;
            if (profile.AllLoaders.Any(l => l is "bepinex" or "bepinex-il2cpp") && inGame.Length > 0 && inGame[^1].Equals("plugins", StringComparison.OrdinalIgnoreCase))
            {
                prefix = Folder(modFolder);  // BepInEx plugins each get a folder
            }
        }

        var list = new List<LayoutEntry>();
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (!rel.Contains('/') && IsDoc(rel))
            {
                continue;
            }
            list.Add(new LayoutEntry(f, target, prefix.Length == 0 ? rel : prefix + "/" + rel));
        }
        return list;
    }

    /// <summary>
    /// A UE4SS mod ("MyMod/Scripts/main.lua", "MyMod/dlls/main.dll", optionally under "Mods/" or "ue4ss/Mods/")
    /// that isn't laid out from the game folder: goes to &lt;exe folder&gt;/ue4ss/Mods/&lt;MyMod&gt;, with
    /// enabled.txt so UE4SS loads it. Null when the archive isn't one (pak mods, full layouts).
    /// </summary>
    private static List<LayoutEntry>? Ue4ssMods(string root, GameProfile profile, string modFolder)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        static bool IsMain(string f) => f.EndsWith("scripts/main.lua", StringComparison.OrdinalIgnoreCase) || f.EndsWith("dlls/main.dll", StringComparison.OrdinalIgnoreCase);
        var mains = files.Where(IsMain).ToList();
        if (mains.Count == 0 || files.Any(f => f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        var exeDir = LoaderRules.ExeDir(profile);
        if (exeDir.Length > 0 && files.Any(f => f.StartsWith(exeDir.Split('/')[0] + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return null;  // laid out from the game folder already
        }
        var modsRoot = (exeDir.Length > 0 ? exeDir + "/" : "") + "ue4ss/Mods";
        var list = new List<LayoutEntry>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            // The mod's own folder is the one holding Scripts/ or dlls/; anything above it ("Mods/", "ue4ss/Mods/") is dropped.
            var main = mains.FirstOrDefault(m => f.StartsWith(ModRoot(m), StringComparison.OrdinalIgnoreCase));
            if (main == null)
            {
                continue;
            }
            var modRoot = ModRoot(main);
            var parent = modRoot.TrimEnd('/');
            var name = parent.Length == 0 ? Folder(modFolder) : parent[(parent.LastIndexOf('/') + 1)..];
            var cut = parent.Length == 0 ? 0 : parent.Length - name.Length;
            var target = modsRoot + "/" + (parent.Length == 0 ? name + "/" + f : f[cut..]);
            list.Add(new LayoutEntry(Path.Combine(root, f), DeployRoot.Game, target));
            folders.Add(name);
        }
        foreach (var name in folders)
        {
            var key = modsRoot + "/" + name + "/enabled.txt";
            if (!list.Any(e => e.Target.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                var marker = Path.Combine(root, ".luma-" + name + "-enabled.txt");
                File.WriteAllText(marker, "");
                list.Add(new LayoutEntry(marker, DeployRoot.Game, key));
            }
        }
        return list;
    }

    /// <summary>"A/B/Scripts/main.lua" → "A/B/"; "Scripts/main.lua" → "".</summary>
    private static string ModRoot(string main)
    {
        var parts = main.Split('/');
        return parts.Length <= 2 ? "" : string.Join('/', parts[..^2]) + "/";
    }

    /// <summary>A flat archive (just files) whose files the profile's file rules place by extension; null when it isn't flat or no rule fits.</summary>
    private static List<LayoutEntry>? Flat(string root, GameProfile profile)
    {
        var files = Directory.GetFiles(root).Where(f => !IsDoc(Path.GetFileName(f))).ToList();
        if (Directory.GetDirectories(root).Length > 0 || files.Count == 0)
        {
            return null;
        }
        var list = new List<LayoutEntry>();
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (!profile.FileRules.TryGetValue(ext, out var dir))
            {
                return null;
            }
            list.Add(new LayoutEntry(f, DeployRoot.Game, dir.Trim('/') + "/" + Path.GetFileName(f)));
        }
        return list;
    }

    /// <summary>The deepest index k where a top-level entry is the game's folder segs[k]; the files go under segs[..k].</summary>
    private static int? Matches(string[] segs, HashSet<string> top)
    {
        for (var k = segs.Length - 1; k >= 0; k--)
        {
            if (top.Contains(segs[k].ToLowerInvariant()))
            {
                return k;
            }
        }
        return null;
    }

    private static string Folder(string name)
    {
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray()).Trim('.', '_');
        return safe.Length == 0 ? "mod" : safe;
    }
}
