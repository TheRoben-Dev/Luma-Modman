using Luma.Core.Mods;
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
        var deps = needs ?? file.Dependencies;
        if (MissingLoader(game) is { AutoInstall: true, } loader && !game.Profile.LoaderOptional && !deps.Any(d => d.Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase)))
        {
            InstallLoader(game, loader);
        }
        // Loaders the mod lists as requirements ("Cyber Engine Tweaks", "RED4ext") come along too.
        if (Host.Settings.AutoLoaders)
        {
            foreach (var l in LoaderInstaller.MissingFor(game, Host.Games.Catalog, Host.Mods.Store(game), deps).Where(l => l.AutoInstall))
            {
                if (Host.QueueLoader(game, l) is { } item)
                {
                    App.Success($"{mod.Name} needs {l.Name}: downloading it too");
                }
            }
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
            links = NxmHandler.Take(Host.Paths, out var collections);
            foreach (var c in collections)
            {
                CollectionActions.Open(c);
            }
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
            if (mod.Site == "loader")
            {
                // Loaders: their newest official release.
                if (Host.Games.Catalog.Loaders.TryGetValue(mod.ModId, out var l) && l.AutoInstall)
                {
                    try
                    {
                        var (_, version, _) = await LoaderInstaller.LatestAsync(Host.Sites.Http, l, game.Profile);
                        var newer = version.Length > 0 && mod.Version.Length > 0 && !SameVersion(version, mod.Version) ? version : null;
                        found += newer != null ? 1 : 0;
                        if (store.State.Find(mod.Id) is { } cur && cur.UpdateVersion != newer)
                        {
                            store.Save(store.State with { Mods = store.State.Mods.Select(m => m.Id == mod.Id ? m with { UpdateVersion = newer } : m).ToList() });
                        }
                    }
                    catch (Exception e) when (Main.IsUserFacing(e))
                    {
                        failed++;
                    }
                }
                continue;
            }
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

    /// <summary>"v1.2.0" and "1.2" are the same version.</summary>
    public static bool SameVersion(string a, string b)
    {
        static string N(string v)
        {
            var parts = System.Text.RegularExpressions.Regex.Replace(v, "^[^0-9]*", "").Split('.', '_', '-').Select(p => p.TrimStart('0') is { Length: > 0 } t ? t : "0").ToList();
            while (parts.Count > 1 && parts[^1] == "0")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            return string.Join('.', parts);
        }
        return N(a) == N(b);
    }

    /// <summary>Mods that have a newer version waiting (found by Check for updates).</summary>
    public static List<InstalledMod> Updatable(DetectedGame game) =>
        Host.Mods.Store(game).State.Mods.Where(m => m.UpdateVersion != null && m.ModId.Length > 0).ToList();

    /// <summary>
    /// Queues the newest version of every mod that has an update. Mods a site lets download only
    /// from its website (free Nexus accounts, some CurseForge authors) are counted and left for
    /// the player to update one by one.
    /// </summary>
    public static async void UpdateAll(DetectedGame game)
    {
        var mods = Updatable(game);
        if (mods.Count == 0)
        {
            App.Toast("No updates waiting. Check for updates first.");
            return;
        }
        int queued = 0, manual = 0, failed = 0;
        App.Toast($"Updating {mods.Count} mod{(mods.Count == 1 ? "" : "s")}…");
        foreach (var mod in mods)
        {
            if (mod.Site == "loader")
            {
                if (Host.Games.Catalog.Loaders.TryGetValue(mod.ModId, out var l) && Host.QueueLoader(game, l) != null)
                {
                    queued++;
                }
                continue;
            }
            var site = Host.Sites.All.FirstOrDefault(s => s.Id == mod.Site);
            if (site == null || !site.Ready || !site.Carries(game.Profile))
            {
                failed++;
                continue;
            }
            if (site is NexusSource && !Host.Keys.Current.NexusPremium)
            {
                manual++;
                continue;
            }
            try
            {
                var d = await site.DetailsAsync(game.Profile, mod.ModId);
                var file = d.Files.FirstOrDefault(f => f.Kind is "" or "Main") ?? d.Files.FirstOrDefault();
                if (file == null)
                {
                    failed++;
                }
                else if (file.DownloadUrl == null && site.Id == "curseforge")
                {
                    manual++;
                }
                else
                {
                    Download(site, game, d.Mod, file, d.Dependencies);
                    queued++;
                }
            }
            catch (Exception e) when (Main.IsUserFacing(e))
            {
                AppLog.Warn($"update all: {mod.Name}: {e.Message}");
                failed++;
            }
        }
        var parts = new List<string>();
        if (queued > 0)
        {
            parts.Add($"{queued} update{(queued == 1 ? "" : "s")} downloading");
        }
        if (manual > 0)
        {
            parts.Add($"{manual} only from {(manual == 1 ? "its" : "their")} website (use Update on {(manual == 1 ? "it" : "each")})");
        }
        if (failed > 0)
        {
            parts.Add($"{failed} couldn't be fetched");
        }
        App.Toast(string.Join("; ", parts) + ".");
    }

    /// <summary>The game's loader when it isn't installed yet and Luma can fetch it.</summary>
    public static LoaderInfo? MissingLoader(DetectedGame game)
    {
        var p = game.Profile;
        return p.Loader != null && Host.Games.Catalog.Loaders.TryGetValue(p.Loader, out var l) && !LoaderInstaller.Installed(game, l, Host.Mods.Store(game).State) ? l : null;
    }

    /// <summary>Queues the loader's newest official release (it installs like a mod), or explains where to get it.</summary>
    public static void InstallLoader(DetectedGame game, LoaderInfo l)
    {
        if (!l.AutoInstall)
        {
            var then = l.Launcher != null ? $" Luma then starts {game.Title} through it." : "";
            App.Push(new ChoicePage($"{l.Name} is installed from its own website. Download it there (the 7z or zip archive), then choose \"Add a mod from a file\".{then}",
                ("Open the website", () => OpenUrl(LoaderPage(l))), ("Add a mod from a file", () => App.Push(new AddFilePage(game))), ("Cancel", () => { })));
            return;
        }
        // Loaders it needs come first (RED4ext before ArchiveXL).
        var store = Host.Mods.Store(game);
        foreach (var need in LoaderRules.WithRequired([l.Id], Host.Games.Catalog.Loaders).Select(id => Host.Games.Catalog.Loaders[id]))
        {
            if (need.Id == l.Id || !LoaderInstaller.Installed(game, need, store.State))
            {
                if (Host.QueueLoader(game, need) is { } item)
                {
                    App.Success($"Downloading {item.ModName}");
                }
            }
        }
    }

    /// <summary>A banner line when mods change the same scripts (The Witcher 3), or null.</summary>
    public static string? ScriptClashText(DetectedGame game)
    {
        if (game.Profile.ScriptMerge is not { } sm)
        {
            return null;
        }
        var clashes = Host.Mods.Store(game).ScriptClashes(sm);
        if (clashes.Count == 0)
        {
            return null;
        }
        var mods = clashes.Values.SelectMany(v => v).Distinct().ToList();
        var names = string.Join(", ", mods.Take(3)) + (mods.Count > 3 ? $" and {mods.Count - 3} more" : "");
        return $"{clashes.Count} script{(clashes.Count == 1 ? " is" : "s are")} changed by more than one mod ({names}). Only one change is used until you merge them with {sm.Tool}.";
    }

    /// <summary>Runs the game's script merge tool when it's under Tools, else opens its page.</summary>
    public static void MergeScripts(DetectedGame game)
    {
        var sm = game.Profile.ScriptMerge!;
        var tool = Host.Mods.Store(game).State.Tools.FirstOrDefault(t => t.Name.Contains("Merger", StringComparison.OrdinalIgnoreCase) || t.Exe.Contains("ScriptMerger", StringComparison.OrdinalIgnoreCase));
        if (tool != null)
        {
            ToolsPage.Run(game, tool);
            return;
        }
        App.Push(new ChoicePage($"{sm.Tool} merges script mods so they work together. Download it from its page, unpack it, then add WitcherScriptMerger.exe under Tools: Luma runs it with your mods in.",
            ("Open its page", () => OpenUrl(sm.Url)), ("Tools", () => App.Push(new ToolsPage(game))), ("Cancel", () => { })));
    }

    /// <summary>"Better Vehicles needs RED4ext and ArchiveXL." for a banner.</summary>
    public static string MissingLoadersText(DetectedGame game, IReadOnlyList<LoaderStatus> missing)
    {
        var names = missing.Select(s => s.Loader.Name).ToList();
        var list = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
        var who = missing.SelectMany(s => s.NeededBy).Distinct().ToList();
        var subject = who.Count switch
        {
            0 => $"Mods for {game.Title}",
            1 => who[0],
            _ => $"{who[0]} and {who.Count - 1} more mod{(who.Count == 2 ? "" : "s")}",
        };
        var verb = who.Count == 1 ? "needs" : "need";
        var how = missing.All(s => s.Loader.AutoInstall) ? "Luma can install " + (names.Count == 1 ? "it" : "them") + " now." : "Some come from their own website.";
        return $"{subject} {verb} {list}. {how}";
    }

    /// <summary>Installs every loader something needs that isn't there yet; ones from websites are listed for the player.</summary>
    public static void InstallMissingLoaders(DetectedGame game)
    {
        var missing = Host.Mods.LoaderStatus(game).Where(s => s.Missing).Select(s => s.Loader).ToList();
        var queued = Host.QueueMissingLoaders(game);
        var manual = missing.Where(l => !l.AutoInstall).ToList();
        if (manual.Count > 0)
        {
            InstallLoader(game, manual[0]);
        }
        else if (queued.Count > 0)
        {
            App.Success($"Downloading {string.Join(", ", queued.Select(l => l.Name))}");
        }
    }

    /// <summary>A loader's download page: GitHub releases, its Nexus page or its homepage.</summary>
    public static string? LoaderPage(LoaderInfo l) => AppHost.LoaderPage(l);
}
