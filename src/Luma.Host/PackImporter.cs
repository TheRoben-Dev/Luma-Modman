using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Luma.Core.Sources;
using Luma.Host.Updates;

namespace Luma.Host;

/// <summary>What happened to one mod of an imported pack.</summary>
public enum PackModOutcome
{
    /// <summary>Installed from the archive inside the pack.</summary>
    Installed,

    /// <summary>Already installed on this device: that copy is used (its version may differ).</summary>
    AlreadyInstalled,

    /// <summary>Queued in Downloads; it joins the pack's profile when it arrives.</summary>
    Downloading,

    /// <summary>The player has to fetch it (Nexus free account, a site not set up, a hand-added file).</summary>
    Manual,

    /// <summary>The pack's archive didn't install.</summary>
    Failed,
}

public sealed record PackModResult(PackMod Mod, PackModOutcome Outcome, string Note = "");

public sealed record PackImportResult(string Profile, PackManifest Pack, IReadOnlyList<PackModResult> Mods)
{
    public int Count(PackModOutcome o) => Mods.Count(m => m.Outcome == o);

    /// <summary>"12 installed, 3 downloading, 1 to get by hand".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            void Add(PackModOutcome o, string text)
            {
                if (Count(o) > 0)
                {
                    parts.Add($"{Count(o)} {text}");
                }
            }
            Add(PackModOutcome.Installed, "installed from the pack");
            Add(PackModOutcome.AlreadyInstalled, "already installed");
            Add(PackModOutcome.Downloading, "downloading");
            Add(PackModOutcome.Manual, "to get by hand");
            Add(PackModOutcome.Failed, "failed");
            return parts.Count == 0 ? "no mods" : string.Join(", ", parts);
        }
    }
}

/// <summary>
/// Exports a game's active profile as a modpack, and imports one into a new profile: bundled
/// archives are installed straight from the pack, the others are queued from their sites (as each
/// player's own download, under that site's terms), and what can't be fetched automatically is
/// listed with its page so the player can get it.
/// </summary>
public static class PackImporter
{
    public static PackExport Export(AppHost host, DetectedGame game, string name, bool full, string file) =>
        ModPack.Export(host.Mods.Store(game), game.Profile, name, full, file, "Luma " + AppUpdater.DisplayVersion(AppUpdater.CurrentAppVersion()));

    /// <summary>A profile name that isn't taken yet: "Pack", "Pack (2)", …</summary>
    public static string FreeProfileName(ModStore store, string wanted)
    {
        var name = wanted.Trim().Length > 0 ? wanted.Trim() : "Modpack";
        var n = 2;
        var candidate = name;
        while (store.State.Profiles.ContainsKey(candidate))
        {
            candidate = $"{name} ({n++})";
        }
        return candidate;
    }

    /// <summary>
    /// Imports <paramref name="file"/> for <paramref name="game"/> into a new profile (made active).
    /// <paramref name="progress"/> gets the name of each mod as it's handled.
    /// </summary>
    public static PackImportResult Import(AppHost host, DetectedGame game, string file, string? profile = null, Action<string, double>? progress = null, bool queueDownloads = true)
    {
        var pack = ModPack.Read(file);
        if (!string.Equals(pack.Game, game.Profile.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"This modpack is for {(pack.GameName.Length > 0 ? pack.GameName : pack.Game)}, not {game.Title}.");
        }
        if (!game.Profile.Moddable)
        {
            throw new InvalidOperationException($"{game.Title} can't be modded safely: {game.Profile.NotModdable}");
        }
        var store = host.Mods.Store(game);
        var name = FreeProfileName(store, profile ?? pack.Name);
        // The profile first, in the pack's order: mods installed or downloaded below take their place in it.
        store.SetProfile(name, pack.Mods.Select(m => new ProfileEntry(m.Id, m.Enabled)));
        foreach (var g in pack.Winners.GroupBy(w => w.Value, w => w.Key))
        {
            store.SetWinner(g, g.Key);
        }
        foreach (var plugin in pack.PluginsOff)
        {
            store.SetPluginEnabled(plugin, false);
        }
        var results = new List<PackModResult>();
        var unpack = Path.Combine(host.Paths.DownloadsDir, "modpacks", ModStore.Safe(Path.GetFileNameWithoutExtension(file)));
        for (var i = 0; i < pack.Mods.Count; i++)
        {
            var mod = pack.Mods[i];
            progress?.Invoke(mod.Name, (double)i / Math.Max(1, pack.Mods.Count));
            results.Add(ImportOne(host, game, file, mod, name, unpack, queueDownloads));
        }
        progress?.Invoke("", 1);
        host.Mods.NotifyChanged(game);
        AppLog.Info($"imported modpack {Path.GetFileName(file)} into {game.Key} profile \"{name}\": {string.Join(", ", results.GroupBy(r => r.Outcome).Select(g => $"{g.Key} {g.Count()}"))}");
        return new PackImportResult(name, pack, results);
    }

    private static PackModResult ImportOne(AppHost host, DetectedGame game, string file, PackMod mod, string profile, string unpack, bool queueDownloads)
    {
        var store = host.Mods.Store(game);
        if (store.State.Find(mod.Id) is { } have)
        {
            var same = have.FileId.Length > 0 ? have.FileId == mod.FileId : have.Version == mod.Version;
            return new PackModResult(mod, PackModOutcome.AlreadyInstalled, same ? "" : $"you have {Show(have.Version)}, the pack has {Show(mod.Version)}");
        }
        if (mod.File != null)
        {
            try
            {
                var archive = ModPack.ExtractMod(file, mod, unpack);
                host.Mods.Install(game, archive, ToInstalled(mod), onlyProfile: profile);
                return new PackModResult(mod, PackModOutcome.Installed);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or FomodNeededException)
            {
                AppLog.Warn($"modpack: {mod.Name}: {e.Message}");
                return new PackModResult(mod, PackModOutcome.Failed, e.Message);
            }
        }
        var site = host.Sites.All.FirstOrDefault(s => s.Id == mod.Site);
        if (site == null || mod.ModId.Length == 0)
        {
            var why = mod.Site switch
            {
                "local" => "added by hand by the pack's author; ask them for the file",
                "loader" => "get it from its website, then add it with \"Add a mod from a file\"",
                _ => $"Luma doesn't know the site \"{mod.Site}\"",
            };
            return new PackModResult(mod, PackModOutcome.Manual, why);
        }
        if (!site.Carries(game.Profile))
        {
            return new PackModResult(mod, PackModOutcome.Manual, $"{site.Name} doesn't list {game.Title} for Luma");
        }
        if (!site.Ready)
        {
            return new PackModResult(mod, PackModOutcome.Manual, $"set up {site.Name} in Settings → Mod sites, then import again");
        }
        if (site is NexusSource && !host.Keys.Current.NexusPremium)
        {
            return new PackModResult(mod, PackModOutcome.Manual, "Nexus Mods lets free accounts download only from its website: press \"Mod Manager Download\" there");
        }
        if (!queueDownloads)
        {
            return new PackModResult(mod, PackModOutcome.Manual, $"download from {site.Name}");
        }
        host.Downloads.Add(new DownloadItem
        {
            Site = site.Id,
            Game = game.Key,
            ModId = mod.ModId,
            ModName = mod.Name,
            Author = mod.Author,
            FileId = mod.FileId,
            Version = mod.Version,
            PageUrl = mod.PageUrl,
            Dependencies = mod.Dependencies,
            Profile = profile,
            Fomod = mod.Fomod,
        }, ct => Resolve(site, game.Profile, mod, ct));
        return new PackModResult(mod, PackModOutcome.Downloading);
    }

    /// <summary>The pack's exact file when the site still has it, otherwise the newest main file.</summary>
    private static async Task<DownloadTicket> Resolve(IModSource site, GameProfile game, PackMod mod, CancellationToken ct)
    {
        var details = await site.DetailsAsync(game, mod.ModId, ct);
        var f = details.Files.FirstOrDefault(x => x.Id == mod.FileId)
            ?? details.Files.FirstOrDefault(x => x.Kind is "" or "Main") ?? details.Files.FirstOrDefault()
            ?? throw new InvalidOperationException($"{site.Name} has no file to download for {mod.Name}.");
        if (f.DownloadUrl == null && site.Id == "curseforge")
        {
            throw new InvalidOperationException($"The author of {mod.Name} only allows downloads from CurseForge's own website or app.");
        }
        return await site.DownloadAsync(game, mod.ModId, f, ct);
    }

    private static string Show(string v) => v.Length > 0 ? v : "another version";

    private static InstalledMod ToInstalled(PackMod m) => new()
    {
        Id = m.Id,
        Site = m.Site,
        ModId = m.ModId,
        Name = m.Name,
        Author = m.Author,
        Version = m.Version,
        FileId = m.FileId,
        PageUrl = m.PageUrl,
        Dependencies = m.Dependencies,
        Fomod = m.Fomod,
    };
}
