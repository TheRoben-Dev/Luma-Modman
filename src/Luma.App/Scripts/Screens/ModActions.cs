using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Sources;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>Starting downloads and handling Nexus links, shared by the mod pages and Main.</summary>
public static class ModActions
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;

    /// <summary>Queues one file of a mod, or explains what the site needs first.</summary>
    public static void Download(IModSource site, DetectedGame game, ModSummary mod, ModFile file, IReadOnlyList<ModDependency>? needs = null)
    {
        // The game's loader comes first (once), unless the mod brings its own (a Thunderstore BepInExPack dependency).
        if (MissingLoader(game) is { AutoInstall: true, } loader && !game.Profile.LoaderOptional && !(needs ?? file.Dependencies).Any(d => d.Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase)))
        {
            InstallLoader(game, loader);
        }
        if (site is NexusSource nexus && !(nexus.HasKey && Host.Keys.Current.NexusPremium))
        {
            NexusManual(nexus, game, mod, file);
            return;
        }
        if (file.DownloadUrl == null && site.Id == "curseforge")
        {
            App.Push(new ChoicePage($"The author of {mod.Name} only allows downloads from CurseForge's own website or app.",
                ("Open on the website", () => OpenUrl(mod.PageUrl)), ("Cancel", () => { })));
            return;
        }
        Queue(new DownloadItem
        {
            Site = site.Id,
            Game = game.Key,
            ModId = mod.Id,
            ModName = mod.Name,
            Author = mod.Author,
            FileId = file.Id,
            Version = file.Version,
            FileName = file.FileName,
            PageUrl = mod.PageUrl,
            Dependencies = (file.Dependencies.Count > 0 ? file.Dependencies : needs ?? []).ToList(),
        }, ct => site.DownloadAsync(game.Profile, mod.Id, file, ct));
    }

    /// <summary>Free Nexus accounts download through the site's "Mod Manager Download" button.</summary>
    private static void NexusManual(NexusSource nexus, DetectedGame game, ModSummary mod, ModFile file)
    {
        if (!nexus.HasKey)
        {
            App.Push(new ChoicePage("Nexus Mods downloads need your Nexus account. Set it up once: it takes a minute.",
                ("Set up Nexus Mods", () => App.Push(new SiteSetupPage(nexus))), ("Cancel", () => { })));
            return;
        }
        var page = $"https://www.nexusmods.com/{game.Profile.Sites.Nexus}/mods/{mod.Id}?tab=files&file_id={file.Id}";
        App.Push(new ChoicePage(
            "Nexus Mods lets apps download directly only for Premium members. Open the file on the website and press \"Mod Manager Download\": it comes straight to Luma's Downloads.",
            ("Open the file page", () =>
            {
                EnsureNxmHandler(quiet: true);
                OpenUrl(page);
            }),
            ("Cancel", () => { })));
    }

    /// <summary>Makes Luma the handler for Nexus "Mod Manager Download" links.</summary>
    public static bool EnsureNxmHandler(bool quiet)
    {
        if (NxmHandler.IsRegistered(Host.Paths))
        {
            return true;
        }
        try
        {
            NxmHandler.Register(Host.Paths, CliPath());
            if (!quiet)
            {
                App.Success("Nexus download links now open in Luma.");
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            App.Error(e.Message);
            return false;
        }
    }

    /// <summary>The `luma` helper (luma-cli.exe on Windows) ships next to the app executable.</summary>
    public static string CliPath() => Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", Luma.Core.Platform.CliBinary);

    /// <summary>Picks up links dropped by `luma nxm` and queues them.</summary>
    public static void TakeNxmLinks()
    {
        List<NxmLink> links;
        try
        {
            links = NxmHandler.Take(Host.Paths);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("nxm inbox: " + e.Message);
            return;
        }
        foreach (var link in links)
        {
            AcceptNxm(link);
        }
    }

    private static void AcceptNxm(NxmLink link)
    {
        if (link.Expired(DateTime.UtcNow))
        {
            App.Error("That Nexus download link has expired. Press \"Mod Manager Download\" on the website again.");
            return;
        }
        var nexus = Host.Sites.Nexus;
        if (!nexus.HasKey)
        {
            App.Push(new ChoicePage("A Nexus Mods download arrived, but Luma needs your Nexus account to fetch it.",
                ("Set up Nexus Mods", () => App.Push(new SiteSetupPage(nexus))), ("Cancel", () => { })));
            return;
        }
        var game = Host.Games.Games.FirstOrDefault(g => string.Equals(g.Profile.Sites.Nexus, link.Domain, StringComparison.OrdinalIgnoreCase));
        Queue(new DownloadItem
        {
            Site = "nexus",
            Game = game?.Key ?? "",
            ModId = link.ModId,
            ModName = game == null ? $"Nexus mod {link.ModId} ({link.Domain})" : $"{game.Title} mod {link.ModId}",
            FileId = link.FileId,
            PageUrl = $"https://www.nexusmods.com/{link.Domain}/mods/{link.ModId}",
        }, ct => nexus.DownloadAsync(link.Domain, link.ModId, link.FileId, link.Key, link.Expires, null, ct));
        if (game == null)
        {
            App.Toast($"Downloading a mod for \"{link.Domain}\", a game Luma hasn't found on this device.");
        }
    }

    private static void Queue(DownloadItem item, Func<CancellationToken, Task<DownloadTicket>> resolve)
    {
        Host.Downloads.Add(item, resolve);
        App.Success($"Downloading {item.ModName}");
    }

    public static void OpenUrl(string? url)
    {
        if (url is not { Length: > 0 } || !(url.StartsWith("https://") || url.StartsWith("http://")))
        {
            return;
        }
        var err = OS.ShellOpen(url);
        App.Toast(err == Error.Ok ? "Opened in your browser." : $"Couldn't open the browser. The address is {url}");
    }

    /// <summary>Downloads the newest main file of a mod (dependencies, updates).</summary>
    public static async void DownloadLatest(IModSource site, DetectedGame game, string modId, string name)
    {
        try
        {
            var d = await site.DetailsAsync(game.Profile, modId);
            var file = d.Files.FirstOrDefault(f => f.Kind is "" or "Main") ?? d.Files.FirstOrDefault()
                ?? throw new InvalidOperationException($"{site.Name} has no file to download for {name}.");
            Download(site, game, d.Mod, file, d.Dependencies);
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            App.Error(e.Message);
        }
    }

    /// <summary>Asks each mod's site for its newest version and marks the mods that have one.</summary>
    public static async void CheckUpdates(DetectedGame game)
    {
        var store = Host.Mods.Store(game);
        var found = 0;
        var failed = 0;
        App.Toast("Checking for updates…");
        foreach (var mod in store.State.Mods.Where(m => m.ModId.Length > 0).ToList())
        {
            var site = Host.Sites.All.FirstOrDefault(s => s.Id == mod.Site);
            if (site == null || !site.Ready || !site.Carries(game.Profile))
            {
                continue;
            }
            try
            {
                var d = await site.DetailsAsync(game.Profile, mod.ModId);
                var latest = d.Files.FirstOrDefault(f => f.Kind is "" or "Main") ?? d.Files.FirstOrDefault();
                var newer = latest != null && latest.Id != mod.FileId && latest.Version != mod.Version ? latest.Version : null;
                if (newer != null)
                {
                    found++;
                }
                var current = store.State.Find(mod.Id);
                if (current != null && current.UpdateVersion != newer)
                {
                    store.Save(store.State with { Mods = store.State.Mods.Select(m => m.Id == mod.Id ? m with { UpdateVersion = newer } : m).ToList() });
                }
            }
            catch (Exception e) when (Main.IsUserFacing(e))
            {
                failed++;
            }
        }
        Host.Mods.NotifyChanged(game);
        App.Toast(found > 0 ? $"{found} update{(found == 1 ? "" : "s")} available." : failed > 0 ? $"Everything checked is up to date ({failed} couldn't be checked)." : "Everything is up to date.");
    }

    /// <summary>The game's loader when it isn't installed yet and Luma can fetch it.</summary>
    public static LoaderInfo? MissingLoader(DetectedGame game)
    {
        var p = game.Profile;
        return p.Loader != null && Host.Games.Catalog.Loaders.TryGetValue(p.Loader, out var l) && !LoaderInstaller.Installed(game, l, Host.Mods.Store(game).State) ? l : null;
    }

    /// <summary>Queues the loader's newest official release; it installs like a mod.</summary>
    public static void InstallLoader(DetectedGame game, LoaderInfo l)
    {
        if (!l.AutoInstall)
        {
            var then = l.Launcher != null ? $" Luma then starts {game.Title} through it." : "";
            App.Push(new ChoicePage($"{l.Name} is installed from its own website. Download it there (the 7z or zip archive), then choose \"Add a mod from a file\".{then}",
                ("Open the website", () => OpenUrl(LoaderPage(l))), ("Add a mod from a file", () => App.Push(new AddFilePage(game))), ("Cancel", () => { })));
            return;
        }
        if (Host.Downloads.Items.Any(i => i.Site == "loader" && i.ModId == l.Id && i.Game == game.Key && i.State is DownloadState.Queued or DownloadState.Downloading))
        {
            return;
        }
        Queue(new DownloadItem
        {
            Site = "loader",
            Game = game.Key,
            ModId = l.Id,
            ModName = l.Name,
            PageUrl = "https://github.com/" + l.Source["github:".Length..],
        }, async ct => (await LoaderInstaller.LatestAsync(Host.Sites.Http, l, ct)).Ticket);
    }

    /// <summary>A loader's download page: GitHub releases, its Nexus page or its homepage.</summary>
    public static string? LoaderPage(LoaderInfo l)
    {
        var (kind, rest) = l.Source.IndexOf(':') is var i and > 0 && !l.Source.StartsWith("http") ? (l.Source[..i], l.Source[(i + 1)..]) : ("url", l.Source);
        return kind switch
        {
            "github" => $"https://github.com/{rest}/releases/latest",
            "nexus" when rest.Split('/') is [var domain, var id] => $"https://www.nexusmods.com/{domain}/mods/{id}",
            "thunderstore" => $"https://thunderstore.io/package/{rest}/",
            _ => l.Source.StartsWith("http") ? l.Source : null,
        };
    }
}
