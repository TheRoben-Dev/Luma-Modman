using Luma.Core.Sources;
using Luma.Host;

namespace Luma.App.Screens;

/// <summary>
/// API keys for sites that need the player's own key: what each site asks for, checking a key
/// with the site before keeping it, and forgetting it. Shared by the controller page
/// (<see cref="SiteSetupPage"/>) and the desktop layout's setup dialog.
/// </summary>
public static class SiteKeys
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;
    private static bool _checking;

    public sealed record Guide(string KeyPage, string[] Steps, string Why);

    public static Guide For(IModSource site) => site.Id switch
    {
        "nexus" => new Guide("https://www.nexusmods.com/settings/api-keys",
            [
                "Sign in to Nexus Mods (a free account works).",
                "Open the API keys page and copy your Personal API Key (at the bottom of the page).",
                "Paste it here.",
            ],
            "Browsing Nexus works without an account. Downloads need your key: Premium members download right here, free members press \"Mod Manager Download\" on the website and the file comes to Luma."),
        "curseforge" => new Guide("https://console.curseforge.com/",
            [
                "Sign in to the CurseForge for Studios console (free).",
                "Open API keys and copy your key.",
                "Paste it here.",
            ],
            "CurseForge asks every app user for their own free key to search and download."),
        "modio" => new Guide("https://mod.io/me/access",
            [
                "Sign in to mod.io (free).",
                "Under API access, create a key (any name, for example \"Luma\") and copy it.",
                "Paste it here.",
            ],
            "mod.io asks each player to use their own free API key."),
        _ => new Guide("", [], ""),
    };

    /// <summary>Sites that take a key (Thunderstore needs none).</summary>
    public static bool NeedsKey(IModSource site) => site.Id is "nexus" or "curseforge" or "modio";

    public static string? CurrentKey(IModSource site) => site.Id switch
    {
        "nexus" => Host.Keys.Current.Nexus,
        "curseforge" => Host.Keys.Current.CurseForge,
        "modio" => Host.Keys.Current.Modio,
        _ => null,
    };

    public static string Status(IModSource site) => CurrentKey(site) == null ? "Not set up yet." : site.Id == "nexus"
        ? $"Signed in as {Host.Keys.Current.NexusUser ?? "your account"}{(Host.Keys.Current.NexusPremium ? " (Premium)" : "")}."
        : "Your key is saved.";

    /// <summary>Checks the key with the site, then keeps it. The old key stays if the site refuses it.</summary>
    public static async Task<bool> Check(IModSource site, string key)
    {
        if (_checking)
        {
            return false;
        }
        _checking = true;
        var before = Host.Keys.Current;
        App.Toast($"Checking the key with {site.Name}…");
        try
        {
            // The site clients read the stored key, so save it first and put the old one back if it's refused.
            Save(site, key, null, false);
            switch (site)
            {
                case NexusSource n:
                    var (user, premium) = await n.ValidateAsync();
                    Save(site, key, user, premium);
                    App.Success(premium ? $"Signed in as {user} (Premium)." : $"Signed in as {user}.");
                    break;
                case CurseForgeSource c:
                    await c.ValidateAsync();
                    App.Success("CurseForge is ready.");
                    break;
                case ModioSource m:
                    await m.ValidateAsync();
                    App.Success("mod.io is ready.");
                    break;
            }
            if (site.Id == "nexus")
            {
                ModActions.EnsureNxmHandler(quiet: true);
            }
            return true;
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            Host.Keys.Save(before);
            App.Error(e.Message);
            return false;
        }
        finally
        {
            _checking = false;
        }
    }

    public static void Save(IModSource site, string? key, string? user, bool premium)
    {
        var c = Host.Keys.Current;
        c = site.Id switch
        {
            "nexus" => c with { Nexus = key, NexusUser = user, NexusPremium = premium },
            "curseforge" => c with { CurseForge = key },
            "modio" => c with { Modio = key },
            _ => c,
        };
        Host.Keys.Save(c);
    }

    public static void Forget(IModSource site)
    {
        Save(site, null, null, false);
        App.Toast($"{site.Name} key removed.");
    }

    /// <summary>Makes (or makes again) Luma the handler for Nexus "Mod Manager Download" links.</summary>
    public static void SetUpNxmLinks()
    {
        if (!NxmHandler.IsRegistered(Host.Paths))
        {
            ModActions.EnsureNxmHandler(quiet: false);
            return;
        }
        try
        {
            NxmHandler.Register(Host.Paths, ModActions.CliPath());
            App.Success("Nexus download links open in Luma.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            App.Error(e.Message);
        }
    }
}
