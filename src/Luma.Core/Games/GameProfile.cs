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

    /// <summary>
    /// More loaders and frameworks some of the game's mods use (Cyberpunk 2077: RED4ext, redscript,
    /// ArchiveXL...). Luma installs one when a mod needs it: by the mod's files or its listed requirements.
    /// </summary>
    public List<string> Loaders { get; init; } = new();

    /// <summary>Picks the loader's release file for this game, when the loader ships one per game (REFramework: "^RE4\.zip$").</summary>
    public string? LoaderAsset { get; init; }

    /// <summary>Where the main loader goes for this game, relative to the game folder, when not where the loader says.</summary>
    public string? LoaderDir { get; init; }

    /// <summary>Arguments for the main loader's launcher in this game (Mod Engine 2: "-t", "er", "-c", "config_eldenring.toml").</summary>
    public List<string> LoaderArgs { get; init; } = new();

    /// <summary>
    /// Where loose files of a flat archive go, by extension (Cyberpunk: ".archive" → "archive/pc/mod"),
    /// for mods packed as just their files.
    /// </summary>
    public Dictionary<string, string> FileRules { get; init; } = new();

    /// <summary>
    /// A folder every mod has inside its own folder (The Witcher 3: "content", as in modX/content/scripts):
    /// archives keep their mod folder, and one without gets a folder named after the mod.
    /// </summary>
    public string? ModFolderMarker { get; init; }

    /// <summary>A Larian-style module list (modsettings.lsx) Luma keeps in step with the enabled .pak mods; null for other games.</summary>
    public string? ModSettings { get; init; }

    /// <summary>Mods change the game's scripts and clash when two change the same one (The Witcher 3: Script Merger fixes it).</summary>
    public ScriptMerge? ScriptMerge { get; init; }

    /// <summary>Every loader the game knows: the main one first, then <see cref="Loaders"/>.</summary>
    [JsonIgnore]
    public IEnumerable<string> AllLoaders => (Loader is { } l ? new[] { l } : []).Concat(Loaders).Distinct();

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
            foreach (var l in AllLoaders.Where(l => !loaderIds.Contains(l)))
            {
                errors.Add($"unknown loader '{l}'");
            }
            if (ModSettings is { } ms && !ms.StartsWith('{'))
            {
                errors.Add("modSettings must start with a location token");
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

/// <summary>Scripts that clash between mods and the tool that merges them.</summary>
public sealed record ScriptMerge
{
    /// <summary>The scripts' folder inside each mod ("content/scripts").</summary>
    public string Folder { get; init; } = "";

    /// <summary>Their extension (".ws").</summary>
    public string Extension { get; init; } = "";

    /// <summary>The merge tool's name and page.</summary>
    public string Tool { get; init; } = "";
    public string Url { get; init; } = "";
}

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

    /// <summary>
    /// Where Luma gets it: "github:owner/repo" (newest release), "page:https://..." (the first link on that
    /// page matching <see cref="Asset"/>), "nexus:domain/modId", "thunderstore:team/package" or a homepage URL.
    /// </summary>
    public string Source { get; init; } = "";

    /// <summary>Wine DLL overrides it needs under Proton ("winhttp=n,b").</summary>
    public List<string> DllOverrides { get; init; } = new();

    /// <summary>What it is, in one line, for the game page.</summary>
    public string About { get; init; } = "";

    /// <summary>
    /// A regular expression picking the file to install: a GitHub release asset's name, or a link on a "page:" source.
    /// GitHub sources without one pick the release's Windows archive by name.
    /// </summary>
    public string? Asset { get; init; }

    /// <summary>
    /// The program that starts the game with the loader (script extenders: "skse64_loader.exe"),
    /// relative to the game folder. When it's in the game, `luma run` starts it instead of the game.
    /// </summary>
    public string? Launcher { get; init; }

    /// <summary>The launcher for a game's native Linux build, when it's another file (SMAPI: "StardewModdingAPI").</summary>
    public string? NativeLauncher { get; init; }

    /// <summary>Arguments the launcher needs before the game's own.</summary>
    public List<string> LaunchArgs { get; init; } = new();

    /// <summary>
    /// A file that only the loader has ("RED4ext.dll"): it marks where the loader's files start in its
    /// archive, and an archive added by hand that holds it is installed as the loader.
    /// Defaults to the launcher's file name.
    /// </summary>
    public string? Anchor { get; init; }

    /// <summary>The anchor file is generic ("dinput8.dll"): don't take any archive holding it for this loader.</summary>
    public bool SharedAnchor { get; init; }

    /// <summary>
    /// The folder the anchor file goes in, relative to the game folder ("red4ext"). "{exeDir}" is the
    /// folder of the game's program. Defaults to the launcher's folder, else the game folder.
    /// </summary>
    public string? InstallDir { get; init; }

    /// <summary>Files or folders (relative to the game folder, "{exeDir}" allowed) that show the loader is installed.</summary>
    public List<string> Detect { get; init; } = new();

    /// <summary>Other loaders it needs (ArchiveXL needs RED4ext).</summary>
    public List<string> Requires { get; init; } = new();

    /// <summary>
    /// Signs in a mod's files that it needs this loader: "/red4ext/plugins/" (a folder anywhere in the
    /// path), "*.reds" (an extension) or "/scripts/main.lua" (a path ending).
    /// </summary>
    public List<string> Signatures { get; init; } = new();

    /// <summary>Names mod sites use for it in a mod's requirements ("Cyber Engine Tweaks", "CET").</summary>
    public List<string> Aliases { get; init; } = new();

    /// <summary>How its archive installs: "files" (as laid out) or "smapi" (SMAPI's installer bundle, unpacked for this OS).</summary>
    public string Kind { get; init; } = "files";

    /// <summary>The loader's files go in the launcher's folder ("" for the game folder).</summary>
    public string LauncherDir => Launcher == null ? "" : Path.GetDirectoryName(Launcher.Replace('\\', '/'))?.Replace('\\', '/') ?? "";

    /// <summary>The anchor in effect: <see cref="Anchor"/>, else the launcher's file name.</summary>
    [JsonIgnore]
    public string? AnchorFile => Anchor ?? (Launcher != null ? Path.GetFileName(Launcher.Replace('\\', '/')) : null);

    /// <summary>True when Luma can download and install it by itself (a GitHub release, or a page with a known link).</summary>
    public bool AutoInstall => Source.StartsWith("github:", StringComparison.Ordinal) || (Source.StartsWith("page:", StringComparison.Ordinal) && Asset != null);
}
