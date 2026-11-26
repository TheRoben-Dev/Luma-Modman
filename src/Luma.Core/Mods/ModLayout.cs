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

    /// <summary>Files that describe the package rather than being part of the mod.</summary>
    public static bool IsDoc(string name)
    {
        var n = name.ToLowerInvariant();
        return n is "manifest.json" or "icon.png" or "thunderstore.toml" || n.StartsWith("readme") || n.StartsWith("changelog") ||
               n.StartsWith("license") || n.EndsWith(".md");
    }

    /// <param name="dir">The unpacked archive.</param>
    /// <param name="modFolder">A folder name for the mod, used where mods sit in folders of their own (BepInEx plugins).</param>
    public static List<LayoutEntry> Plan(string dir, GameProfile profile, string modFolder)
    {
        var root = dir;
        var segs = profile.ModsPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var outside = profile.ModsPath.StartsWith('{');
        var inGame = outside ? [] : segs;
        var markers = new HashSet<string>(segs.Where(s => !s.StartsWith('{')).Select(s => s.ToLowerInvariant())) { "bepinex" };

        // Unwrap single wrapper folders ("MyMod-1.2/...") that aren't one of the game's folders.
        while (true)
        {
            var dirs = Directory.GetDirectories(root);
            var files = Directory.GetFiles(root).Where(f => !IsDoc(Path.GetFileName(f))).ToList();
            if (dirs.Length == 1 && files.Count == 0 && !markers.Contains(Path.GetFileName(dirs[0]).ToLowerInvariant()))
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
        if (lower.Any(t => RootFiles.Contains(t) || t.EndsWith(".exe")))
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
            if (profile.Loader is "bepinex" or "bepinex-il2cpp" && inGame.Length > 0 && inGame[^1].Equals("plugins", StringComparison.OrdinalIgnoreCase))
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
