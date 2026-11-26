using Luma.Core.Detection;
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
    public static void Download(IModSource site, DetectedGame game, ModSummary mod, ModFile file)
    {
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
            FileId = file.Id,
            Version = file.Version,
            FileName = file.FileName,
            PageUrl = mod.PageUrl,
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

    /// <summary>The `luma` helper ships next to the app executable.</summary>
    public static string CliPath() => Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "luma");

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
}
