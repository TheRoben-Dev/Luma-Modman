using System.Text.RegularExpressions;
using Luma.Core.Detection;

namespace Luma.Core.Mods;

/// <summary>
/// A program run with the mods in the game (MO2's "executables"): xEdit, LOOT, BodySlide,
/// Nemesis, Pandora, DynDOLOD. <see cref="Exe"/> may use {game} (the game folder) and {data}
/// (the mods folder), so a tool that is itself a mod (BodySlide in Data/CalienteTools) works.
/// <see cref="Args"/> may use {game}, {data} and {gamename}; paths become Windows paths
/// (Z:\...) when the tool runs under Proton.
/// </summary>
public sealed record ToolDef(string Name, string Exe, string Args = "");

/// <summary>A tool Luma knows how to set up: its program names and the arguments it needs.</summary>
public sealed record KnownTool(string Name, string[] Programs, string Args = "", string About = "");

public static class Tools
{
    /// <summary>Tools Luma recognises by program name, with the arguments that point them at the game.</summary>
    public static readonly KnownTool[] Known =
    [
        new("xEdit", ["SSEEdit64.exe", "SSEEdit.exe", "FO4Edit64.exe", "FO4Edit.exe", "TES5Edit64.exe", "TES5Edit.exe", "FNVEdit.exe", "FO3Edit.exe", "TES4Edit.exe", "SF1Edit64.exe", "xEdit64.exe", "xEdit.exe"],
            "-D:\"{data}\"", "Cleans plugins and makes patches. Its output goes to Overwrite."),
        new("LOOT", ["LOOT.exe"], "--game=\"{gamename}\" --game-path=\"{game}\"", "Sorts plugins by its community rules. Luma reads the order back from plugins.txt."),
        new("BodySlide", ["BodySlide x64.exe", "BodySlide.exe"], "", "Builds body and outfit meshes. Its output goes to Overwrite."),
        new("Outfit Studio", ["OutfitStudio x64.exe", "OutfitStudio.exe"]),
        new("Nemesis", ["Nemesis Unlimited Behavior Engine.exe"], "", "Generates animation behaviours. Its output goes to Overwrite."),
        new("Pandora", ["Pandora Behaviour Engine+.exe", "Pandora Behaviour Engine.exe"], "", "Generates animation behaviours. Its output goes to Overwrite."),
        new("FNIS", ["GenerateFNISforUsers.exe"]),
        new("DynDOLOD", ["DynDOLODx64.exe", "DynDOLOD.exe"]),
        new("TexGen", ["TexGenx64.exe", "TexGen.exe"]),
        new("Wrye Bash", ["Wrye Bash.exe"]),
        new("Synthesis", ["Synthesis.exe"]),
        new("Creation Kit", ["CreationKit.exe"]),
    ];

    /// <summary>The name LOOT knows a game by (its --game argument).</summary>
    public static string LootName(string profileId, string title) => profileId switch
    {
        "skyrimspecialedition" => "Skyrim Special Edition",
        "skyrim" => "Skyrim",
        "enderalspecialedition" => "Enderal Special Edition",
        "fallout4" => "Fallout4",
        "newvegas" => "FalloutNV",
        "fallout3" => "Fallout3",
        "oblivion" => "Oblivion",
        "starfield" => "Starfield",
        _ => title,
    };

    public static KnownTool? Recognise(string exe) =>
        Known.FirstOrDefault(k => k.Programs.Any(p => p.Equals(Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Known tools found in the game's mods (as they'll be in the game folder once the mods are
    /// in), the game folder itself, and unpacked in Downloads or on the Desktop.
    /// </summary>
    public static List<ToolDef> Find(ModStore store, string gameDir, string? modsDir, IEnumerable<string> extraDirs)
    {
        var found = new List<ToolDef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string exePath, string stored)
        {
            if (Recognise(exePath) is { } k && seen.Add(stored))
            {
                found.Add(new ToolDef(k.Name, stored, k.Args));
            }
        }
        foreach (var mod in store.State.Mods)
        {
            foreach (var (sub, token) in new[] { ("game", "{game}"), ("mods", "{data}") })
            {
                var dir = Path.Combine(store.ModDir(mod.Id), sub);
                foreach (var f in SafeExes(dir, 6))
                {
                    Add(f, token + "/" + Path.GetRelativePath(dir, f).Replace('\\', '/'));
                }
            }
        }
        foreach (var f in SafeExes(gameDir, 4))
        {
            Add(f, "{game}/" + Path.GetRelativePath(gameDir, f).Replace('\\', '/'));
        }
        foreach (var dir in extraDirs)
        {
            foreach (var f in SafeExes(dir, 3))
            {
                Add(f, f);
            }
        }
        return found;
    }

    private static IEnumerable<string> SafeExes(string dir, int depth)
    {
        if (!Directory.Exists(dir))
        {
            return [];
        }
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = depth, IgnoreInaccessible = true })
                .Where(f => Recognise(f) != null).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Fills in {game} and {data} in a tool's program path.</summary>
    public static string ExePath(ToolDef tool, string gameDir, string? modsDir) =>
        Path.GetFullPath(Expand(tool.Exe, gameDir, modsDir, "", null));

    /// <summary>Fills in {game}, {data} and {gamename}; with <paramref name="toWindows"/>, paths become Wine paths.</summary>
    public static string Expand(string text, string gameDir, string? modsDir, string gameName, Func<string, string>? toWindows)
    {
        string P(string p) => toWindows?.Invoke(p) ?? p;
        return Regex.Replace(text, @"\{(game|data|gamename)\}", m => m.Groups[1].Value switch
        {
            "game" => P(gameDir),
            "data" => P(modsDir ?? gameDir),
            _ => gameName,
        });
    }

    /// <summary>A Linux path as Wine sees it: Z:\home\deck\... (Z: is the root of the file system in every prefix).</summary>
    public static string WinePath(string path) => "Z:" + path.Replace('/', '\\');

    /// <summary>Splits an argument line the way a shell would for plain and "quoted" arguments.</summary>
    public static List<string> SplitArgs(string args)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(args, "(?:[^\\s\"]+|\"[^\"]*\")+"))
        {
            list.Add(m.Value.Replace("\"", ""));
        }
        return list;
    }
}

/// <summary>Finds the Proton a Steam game uses, to run Windows tools in that game's prefix.</summary>
public static class ProtonFinder
{
    /// <summary>
    /// The `proton` script for <paramref name="compatTool"/> ("proton_9", "proton_experimental",
    /// "GE-Proton9-20"), else the newest Proton installed; null when there is none.
    /// </summary>
    public static string? Find(string? compatTool, IEnumerable<string> steamRoots, IEnumerable<string> libraries, string home)
    {
        var commons = libraries.Concat(steamRoots).Select(l => Path.Combine(l, "steamapps", "common")).Where(Directory.Exists).Distinct().ToList();
        var custom = steamRoots.Select(r => Path.Combine(r, "compatibilitytools.d"))
            .Append(Path.Combine(home, ".steam", "root", "compatibilitytools.d"))
            .Append("/usr/share/steam/compatibilitytools.d").Where(Directory.Exists).Distinct().ToList();
        string? Script(string dir) => File.Exists(Path.Combine(dir, "proton")) ? Path.Combine(dir, "proton") : null;
        if (compatTool is { Length: > 0 } tool && !tool.Equals("steamlinuxruntime", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var name in FolderNames(tool))
            {
                foreach (var dir in commons.Concat(custom))
                {
                    if (Script(Path.Combine(dir, name)) is { } s)
                    {
                        return s;
                    }
                }
            }
            // Custom tools name themselves in compatibilitytool.vdf; the folder is usually the same name.
            foreach (var dir in custom)
            {
                foreach (var sub in SafeDirs(dir))
                {
                    var vdf = Path.Combine(sub, "compatibilitytool.vdf");
                    if (File.Exists(vdf) && File.ReadAllText(vdf).Contains($"\"{tool}\"", StringComparison.OrdinalIgnoreCase) && Script(sub) is { } s)
                    {
                        return s;
                    }
                }
            }
        }
        // The newest official Proton with a version number, then Experimental, then anything.
        var all = commons.SelectMany(SafeDirs).Where(d => Path.GetFileName(d).StartsWith("Proton", StringComparison.OrdinalIgnoreCase) && Script(d) != null).ToList();
        var numbered = all.Select(d => (d, v: Regex.Match(Path.GetFileName(d), @"(\d+)\.(\d+)")))
            .Where(x => x.v.Success).OrderByDescending(x => int.Parse(x.v.Groups[1].Value)).ThenByDescending(x => int.Parse(x.v.Groups[2].Value)).Select(x => x.d);
        var pick = numbered.Concat(all).Concat(custom.SelectMany(SafeDirs).Where(d => Script(d) != null)).FirstOrDefault();
        return pick == null ? null : Script(pick);
    }

    /// <summary>Folder names for a compat tool id: "proton_9" → "Proton 9.0", "proton_513" → "Proton 5.13".</summary>
    public static IEnumerable<string> FolderNames(string tool)
    {
        yield return tool;
        var l = tool.ToLowerInvariant();
        if (l == "proton_experimental")
        {
            yield return "Proton - Experimental";
        }
        else if (l == "proton_hotfix")
        {
            yield return "Proton Hotfix";
        }
        else if (Regex.Match(l, @"^proton_(\d+)$") is { Success: true } m)
        {
            var d = m.Groups[1].Value;
            yield return d.Length == 1 || (d.Length == 2 && d[0] != '5' && d[0] != '6' && d[0] != '4' && d[0] != '3') ? $"Proton {d}.0" : $"Proton {d[0]}.{d[1..]}";
        }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
