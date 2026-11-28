using System.Text.RegularExpressions;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Luma.Core.Sources;

namespace Luma.Host;

/// <summary>Where one of a game's loaders stands, for the Mod loaders page and the banners.</summary>
/// <param name="Mod">The installed loader when Luma installed it (version, updates); null when it isn't, or sits in the game folder already.</param>
/// <param name="NeededBy">Enabled mods whose files or requirements show they need it.</param>
/// <param name="Required">The game's mods need it in general (its main, non-optional loader), or another needed loader needs it.</param>
public sealed record LoaderStatus(LoaderInfo Loader, bool Installed, InstalledMod? Mod, IReadOnlyList<string> NeededBy, bool Required)
{
    /// <summary>Not installed, and something needs it.</summary>
    public bool Missing => !Installed && (Required || NeededBy.Count > 0);
}

/// <summary>
/// Mod loaders and script extenders (BepInEx, UE4SS, SKSE64, RED4ext...), from their official GitHub
/// releases or download pages. A loader is installed like a mod ("loader:&lt;id&gt;"), so it is
/// linked in with the rest and comes out with them.
/// </summary>
public static class LoaderInstaller
{
    public static string ModId(LoaderInfo l) => "loader:" + l.Id;

    /// <summary>Where the loader's anchor file goes, relative to the game folder.</summary>
    public static string InstallPath(GameProfile p, LoaderInfo? l = null) =>
        l == null ? "" : LoaderRules.InstallDir(p, l);

    /// <summary>The game's loaders, main one first.</summary>
    public static List<LoaderInfo> Loaders(GameProfile p, GameCatalog catalog) =>
        p.AllLoaders.Select(id => catalog.Loaders.GetValueOrDefault(id)).OfType<LoaderInfo>().ToList();

    /// <summary>True when the loader is installed through Luma or already sits in the game folder.</summary>
    public static bool Installed(DetectedGame game, LoaderInfo l, GameMods mods)
    {
        if (mods.Mods.Any(m => m.Id == ModId(l) || (l.Id is "bepinex" or "bepinex-il2cpp" && m.Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }
        if (LoaderRules.Launcher(game.Profile, l) is { } launcher && File.Exists(Path.Combine(game.GameDir, launcher)))
        {
            return true;
        }
        foreach (var d in l.Detect)
        {
            var path = Path.Combine(game.GameDir, LoaderRules.Resolve(d, game.Profile));
            if (File.Exists(path) || Directory.Exists(path))
            {
                return true;
            }
        }
        return l.Id switch
        {
            "bepinex" or "bepinex-il2cpp" => Directory.Exists(Path.Combine(game.GameDir, "BepInEx", "core")),
            "melonloader" => Directory.Exists(Path.Combine(game.GameDir, "MelonLoader")),
            _ => false,
        };
    }

    /// <summary>
    /// Every loader of the game with what needs it: the main loader, loaders the enabled mods' files
    /// or listed requirements point to, and the loaders those need in turn.
    /// </summary>
    public static List<LoaderStatus> Status(DetectedGame game, GameCatalog catalog, ModStore store)
    {
        var p = game.Profile;
        var loaders = Loaders(p, catalog);
        var byFiles = store.NeededLoaders(loaders);
        var on = store.State.Order.Where(e => e.Enabled).Select(e => e.Id).ToHashSet();
        var enabled = store.State.Mods.Where(m => on.Contains(m.Id) && m.Site != "loader").ToList();
        var neededBy = new Dictionary<string, List<string>>();
        foreach (var l in loaders)
        {
            var names = enabled.Where(m => (m.Needs?.Contains(l.Id) ?? false) || m.Dependencies.Any(d => LoaderRules.IsDependency(l, d.Name))).Select(m => m.Name).ToList();
            neededBy[l.Id] = names;
        }
        // Thunderstore mods bring BepInEx as their BepInExPack dependency: that one comes as a mod instead.
        var packComing = enabled.Any(m => m.Dependencies.Any(d => d.Name.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase) || d.Id.Contains("BepInExPack", StringComparison.OrdinalIgnoreCase)));
        if (packComing)
        {
            neededBy["bepinex"] = [];
            neededBy["bepinex-il2cpp"] = [];
        }
        var wanted = loaders.Where(l => (l.Id == p.Loader && !p.LoaderOptional && enabled.Count > 0 && !(packComing && l.Id is "bepinex" or "bepinex-il2cpp")) || neededBy[l.Id].Count > 0)
            .Select(l => l.Id);
        var required = LoaderRules.WithRequired(wanted, catalog.Loaders).ToHashSet();
        var all = loaders.Select(l => l.Id).Concat(required).Distinct().ToList();
        return all.Select(id => catalog.Loaders[id]).Select(l => new LoaderStatus(l, Installed(game, l, store.State), store.State.Find(ModId(l)),
            neededBy.GetValueOrDefault(l.Id) ?? [], required.Contains(l.Id))).ToList();
    }

    /// <summary>The loaders something needs that aren't installed, in install order (RED4ext before ArchiveXL).</summary>
    public static List<LoaderStatus> Missing(DetectedGame game, GameCatalog catalog, ModStore store) =>
        Status(game, catalog, store).Where(s => s.Missing).ToList();

    /// <summary>The loaders a mod's listed requirements name ("Cyber Engine Tweaks"), with the loaders those need, that aren't installed yet.</summary>
    public static List<LoaderInfo> MissingFor(DetectedGame game, GameCatalog catalog, ModStore store, IEnumerable<ModDependency> needs)
    {
        var loaders = Loaders(game.Profile, catalog);
        var named = loaders.Where(l => needs.Any(d => LoaderRules.IsDependency(l, d.Name))).Select(l => l.Id);
        return LoaderRules.WithRequired(named, catalog.Loaders).Select(id => catalog.Loaders[id]).Where(l => !Installed(game, l, store.State)).ToList();
    }

    /// <summary>The newest release's file for this loader (for this game: REFramework has one per game), its version and page.</summary>
    public static async Task<(DownloadTicket Ticket, string Version, string Page)> LatestAsync(HttpGate http, LoaderInfo l, GameProfile? game = null, CancellationToken ct = default)
    {
        if (!l.AutoInstall)
        {
            throw new InvalidOperationException($"{l.Name} has to be installed from its website.");
        }
        var pattern = game?.Loader == l.Id && game.LoaderAsset != null ? game.LoaderAsset : l.Asset;
        if (l.Source.StartsWith("page:", StringComparison.Ordinal))
        {
            var page = l.Source["page:".Length..];
            var html = await http.GetStringAsync(page, cache: false, ct: ct);
            var link = LoaderRules.PickLink(html, page, pattern!)
                ?? throw new InvalidDataException($"Luma couldn't find the {l.Name} download on its website. Get it from {page} and add it with \"Add a mod from a file\".");
            var file = Uri.UnescapeDataString(new Uri(link).Segments[^1]);
            return (new DownloadTicket(link, Text.SafeFileName(file), 0, page), LoaderRules.VersionFrom(file), page);
        }
        var repo = l.Source["github:".Length..];
        using var doc = await http.GetJsonAsync($"https://api.github.com/repos/{repo}/releases/latest", ct: ct);
        var root = doc.RootElement;
        var tag = root.Str("tag_name");
        var assets = root.Arr("assets").Select(a => (Name: a.Str("name"), Url: a.Str("browser_download_url"), Size: a.Long("size"))).ToList();
        var names = assets.Select(a => a.Name).ToList();
        // A renamed release file falls back to picking the Windows archive by name, except where each game has its own file (REFramework).
        var pick = LoaderRules.PickAsset(names, pattern) ?? (pattern == l.Asset ? LoaderRules.PickAsset(names, null) : null);
        if (pick != null)
        {
            var a = assets.First(x => x.Name == pick);
            var version = Regex.Replace(tag, "^[^0-9]*", "");
            return (new DownloadTicket(a.Url, Text.SafeFileName(a.Name), a.Size), version.Length > 0 ? version : LoaderRules.VersionFrom(a.Name), root.Str("html_url"));
        }
        throw new InvalidDataException($"The newest {l.Name} release ({tag}) has no file Luma recognises. Install it from {root.Str("html_url")}.");
    }

    /// <summary>
    /// After a loader is installed: Mod Engine 2 loads mods from the folder its config names, so the
    /// config is pointed at the game's mods folder.
    /// </summary>
    public static void AfterInstall(GameProfile p, LoaderInfo l, string modDir)
    {
        if (l.Id != "modengine2")
        {
            return;
        }
        var gameDir = Path.Combine(modDir, "game");
        if (!Directory.Exists(gameDir))
        {
            return;
        }
        var install = LoaderRules.InstallDir(p, l);
        var rel = Path.GetRelativePath(install.Length == 0 ? "." : install, p.ModsPathHere.Length == 0 ? "." : p.ModsPathHere).Replace('\\', '/');
        foreach (var toml in Directory.EnumerateFiles(gameDir, "config_*.toml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(toml);
            var fixedText = Regex.Replace(text, "(path\\s*=\\s*\")mod(\")", m => m.Groups[1].Value + rel + m.Groups[2].Value);
            if (fixedText != text)
            {
                File.WriteAllText(toml, fixedText);
            }
        }
    }
}
