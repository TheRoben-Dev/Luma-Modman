using System.Text.Json;
using System.Text.Json.Serialization;

namespace Luma.Core.Games;

/// <summary>Which mod sites carry a game, by each site's own id for it.</summary>
public sealed record SiteIds
{
    /// <summary>Nexus Mods game domain ("skyrimspecialedition").</summary>
    public string? Nexus { get; init; }

    /// <summary>Thunderstore community identifier ("lethal-company").</summary>
    public string? Thunderstore { get; init; }

    /// <summary>mod.io game name_id; resolved by name through the API when unsure.</summary>
    public string? Modio { get; init; }

    /// <summary>CurseForge game slug ("hogwarts-legacy").</summary>
    public string? Curseforge { get; init; }

    public IEnumerable<string> Names()
    {
        if (Thunderstore != null) yield return "Thunderstore";
        if (Modio != null) yield return "mod.io";
        if (Curseforge != null) yield return "CurseForge";
        if (Nexus != null) yield return "Nexus Mods";
    }
}

/// <summary>
/// Everything Luma knows about modding one game, loaded from data/games/&lt;id&gt;.json.
/// Supporting a new game means writing one of these files, not code.
/// </summary>
public sealed record GameProfile
{
    /// <summary>Stable id; the file name without .json.</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Steam app ids (base game and alternative editions).</summary>
    public List<long> Steam { get; init; } = new();

    /// <summary>GOG product ids, for Heroic's GOG installs.</summary>
    public List<string> Gog { get; init; } = new();

    /// <summary>Epic (Legendary) app names, for Heroic's Epic installs.</summary>
    public List<string> Epic { get; init; } = new();

    /// <summary>
    /// Files that identify the game inside an install folder (relative paths, first one is the
    /// main executable). Used for Heroic, Lutris and other non-Steam installs.
    /// </summary>
    public List<string> Detect { get; init; } = new();

    /// <summary>"proton" (Windows build, runs in a Wine prefix) or "native" (Linux build).</summary>
    public string Runtime { get; init; } = "proton";

    /// <summary>
    /// Where mods go. Relative to the game folder, or starting with a location token:
    /// {documents}, {appdata}, {localappdata}, {mygames} (inside the Proton prefix for Windows games),
    /// {xdgdata}, {xdgconfig} (native games).
    /// </summary>
    public string ModsPath { get; init; } = "";

    /// <summary>
    /// Where mods go when Luma runs on Windows, if not <see cref="ModsPath"/>: only needed for games
    /// whose Linux build keeps mods somewhere else ({xdgdata} → "{documents}/My Games/...").
    /// </summary>
    public string? WindowsModsPath { get; init; }

    /// <summary>The mods path for the OS Luma runs on.</summary>
    [JsonIgnore]
    public string ModsPathHere => Platform.IsWindows && WindowsModsPath != null ? WindowsModsPath : ModsPath;

    /// <summary>The mod loader this game needs (id from data/loaders.json), or null when mods load without one.</summary>
    public string? Loader { get; init; }

    /// <summary>True when only some mods need the loader (installed when the first such mod is).</summary>
    public bool LoaderOptional { get; init; }

    public SiteIds Sites { get; init; } = new();

    /// <summary>Extra Wine DLL overrides beyond the loader's own ("dinput8=n,b").</summary>
    public List<string> DllOverrides { get; init; } = new();

    /// <summary>Extra Steam launch options beyond Luma's wrapper (e.g. "-skipintro").</summary>
    public string? LaunchOptions { get; init; }

    /// <summary>Bethesda-style plugin list (plugins.txt) Luma keeps in step with the enabled mods; null for other games.</summary>
    public PluginSettings? Plugins { get; init; }

    /// <summary>INI settings mods need (archive invalidation): written while mods are in, put back after.</summary>
    public List<IniSetting> Ini { get; init; } = new();

    /// <summary>The game's save folder ("{mygames}/Skyrim Special Edition/Saves"): a profile can keep its own saves there while it's in.</summary>
    public string? Saves { get; init; }

    /// <summary>The game's own INI files (settings, prefs): a profile can keep its own copies of them.</summary>
    public List<string> Inis { get; init; } = new();

    /// <summary>Set when the game can't be modded safely (anti-cheat, online-only bans): the reason shown to the player.</summary>
    public string? NotModdable { get; init; }

    /// <summary>Things worth knowing, shown on the game's page.</summary>
    public List<string> Quirks { get; init; } = new();

    /// <summary>
    /// Community sites without an API Luma may use (custom maps, songs): opened in the browser, and
    /// what the player downloads there can be imported into <see cref="GameLink.ImportTo"/>.
    /// </summary>
    public List<GameLink> Links { get; init; } = new();

    [JsonIgnore]
    public bool Moddable => NotModdable == null;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>Problems that make a profile unusable; empty when it's fine.</summary>
    public IReadOnlyList<string> Validate(IReadOnlySet<string> loaderIds)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id) || Id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
        {
            errors.Add("id must be lowercase letters, digits and dashes");
        }
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("name is missing");
        }
        if (Runtime is not ("proton" or "native"))
        {
            errors.Add("runtime must be proton or native");
        }
        if (Steam.Count == 0 && Detect.Count == 0)
        {
            errors.Add("needs steam ids or detect files to be found");
        }
        if (Moddable)
        {
            if (Loader != null && !loaderIds.Contains(Loader))
            {
                errors.Add($"unknown loader '{Loader}'");
            }
            if (!Sites.Names().Any())
            {
                errors.Add("a moddable game needs at least one mod site");
            }
            if (ModsPath.StartsWith('/') || ModsPath.Contains(".."))
            {
                errors.Add("modsPath must be relative or start with a location token");
            }
            if (ModsPath.StartsWith('{') && !ModsTokens.Any(t => ModsPath.StartsWith(t, StringComparison.Ordinal)))
            {
                errors.Add($"unknown location token in modsPath '{ModsPath}'");
            }
            if (Plugins is { } pl && (!pl.File.StartsWith('{') || pl.Style is not ("asterisk" or "plain")))
            {
                errors.Add("plugins.file must start with a location token and plugins.style must be asterisk or plain");
            }
            if (Ini.Any(i => !i.File.StartsWith('{') || i.Section.Length == 0 || i.Key.Length == 0))
            {
                errors.Add("each ini setting needs a file with a location token, a section and a key");
            }
            if ((Saves is { } sv && !sv.StartsWith('{')) || Inis.Any(i => !i.StartsWith('{')))
            {
                errors.Add("saves and inis must start with a location token");
            }
            if (WindowsModsPath is { } w && (w.StartsWith('/') || w.Contains("..") || (w.StartsWith('{') && !WindowsTokens.Any(t => w.StartsWith(t, StringComparison.Ordinal)))))
            {
                errors.Add($"windowsModsPath must be relative or start with a Windows location token: '{w}'");
            }
        }
        return errors;
    }

    /// <summary>Tokens that mean something on Windows (the {xdg...} ones don't).</summary>
    public static readonly string[] WindowsTokens = ["{documents}", "{appdata}", "{localappdata}", "{mygames}"];

    public static readonly string[] ModsTokens = ["{documents}", "{appdata}", "{localappdata}", "{mygames}", "{xdgdata}", "{xdgconfig}"];
}

/// <summary>Where a Bethesda game keeps its plugin list, and how it reads it.</summary>
public sealed record PluginSettings
{
    /// <summary>plugins.txt, with a location token ("{localappdata}/Skyrim Special Edition/plugins.txt").</summary>
    public string File { get; init; } = "";

    /// <summary>
    /// "asterisk": every plugin listed in load order, "*" before the active ones (Skyrim SE,
    /// Fallout 4, Starfield). "plain": only active plugins (Skyrim 2011, Oblivion, Fallout 3/NV).
    /// </summary>
    public string Style { get; init; } = "asterisk";

    /// <summary>Also write loadorder.txt next to plugins.txt (Skyrim 2011).</summary>
    public bool LoadOrderFile { get; init; }

    /// <summary>The game orders plugins by file date (Oblivion, Fallout 3, New Vegas).</summary>
    public bool Timestamps { get; init; }

    /// <summary>The game's own plugins, always loaded first and never written by Luma.</summary>
    public List<string> Base { get; init; } = new();

    /// <summary>The header flag that marks a light plugin (ESL): 0x200 in Skyrim SE and Fallout 4, 0x100 in Starfield; 0 where there are none.</summary>
    public uint? LightFlag { get; init; }

    /// <summary>The light flag in effect: <see cref="LightFlag"/>, else 0x200 for the "asterisk" games and none for the older ones.</summary>
    public uint Light => LightFlag ?? (Style == "asterisk" ? Mods.PluginFile.DefaultLightFlag : 0u);

    /// <summary>How many full (not light) plugins the game can load, the game's own included.</summary>
    public int MaxPlugins { get; init; } = 254;
}

/// <summary>One INI value: [Section] Key=Value in File (a location token path).</summary>
public sealed record IniSetting(string File, string Section, string Key, string Value);

/// <summary>A community site for a game, used through the browser (see <see cref="GameProfile.Links"/>).</summary>
public sealed record GameLink
{
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string About { get; init; } = "";

    /// <summary>Where imported files go (same tokens as modsPath); null when the site is only a link.</summary>
    public string? ImportTo { get; init; }

    /// <summary>What one item is called ("map").</summary>
    public string Item { get; init; } = "item";
}

/// <summary>A mod loader or script extender Luma can install (data/loaders.json).</summary>
public sealed record LoaderInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Where Luma gets it: "github:owner/repo", "nexus:domain/modId", "thunderstore:team/package" or a homepage URL.</summary>
    public string Source { get; init; } = "";

    /// <summary>Wine DLL overrides it needs under Proton ("winhttp=n,b").</summary>
    public List<string> DllOverrides { get; init; } = new();

    /// <summary>What it is, in one line, for the game page.</summary>
    public string About { get; init; } = "";

    /// <summary>For GitHub sources: a regular expression picking the release file to install (Windows build, for Proton).</summary>
    public string? Asset { get; init; }

    /// <summary>
    /// The program that starts the game with the loader (script extenders: "skse64_loader.exe"),
    /// relative to the game folder. When it's in the game, `luma run` starts it instead of the game.
    /// </summary>
    public string? Launcher { get; init; }

    /// <summary>The loader's files go in the launcher's folder ("" for the game folder).</summary>
    public string LauncherDir => Launcher == null ? "" : Path.GetDirectoryName(Launcher.Replace('\\', '/'))?.Replace('\\', '/') ?? "";

    /// <summary>True when Luma can download and install it by itself (a GitHub release with a known file).</summary>
    public bool AutoInstall => Source.StartsWith("github:", StringComparison.Ordinal) && Asset != null;
}
