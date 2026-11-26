using Luma.App.Ui;
using Luma.Core.Sources;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Guided setup for a site that needs the player's own API key: open the site's key page in the
/// browser, copy the key, paste it here. Luma checks it with the site before saving it (in a file
/// only the player can read). Nexus also gets the "Mod Manager Download" link handler here.
/// </summary>
public partial class SiteSetupPage : Screen
{
    private readonly IModSource _site;
    private Control? _first;
    private bool _checking;

    public SiteSetupPage(IModSource site) => _site = site;

    private record Guide(string KeyPage, string[] Steps, string Why);

    private Guide For() => _site.Id switch
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

    private string? CurrentKey => _site.Id switch
    {
        "nexus" => Host.Keys.Current.Nexus,
        "curseforge" => Host.Keys.Current.CurseForge,
        "modio" => Host.Keys.Current.Modio,
        _ => null,
    };

    protected override void Build()
    {
        var col = Column(12);
        AddChild(Scroller(col));
        col.AddChild(UiTheme.Label($"Set up {_site.Name}", UiTheme.TitleSize));
        var g = For();
        col.AddChild(UiTheme.Label(g.Why, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));

        var status = CurrentKey == null ? "Not set up yet." : _site.Id == "nexus"
            ? $"Signed in as {Host.Keys.Current.NexusUser ?? "your account"}{(Host.Keys.Current.NexusPremium ? " (Premium)" : "")}."
            : "Your key is saved.";
        col.AddChild(UiTheme.Label(status, UiTheme.FontSize, CurrentKey == null ? UiTheme.Warn : UiTheme.Good));

        col.AddChild(Section("Steps"));
        for (var i = 0; i < g.Steps.Length; i++)
        {
            col.AddChild(UiTheme.Label($"{i + 1}.  {g.Steps[i]}", UiTheme.SmallSize + 1, UiTheme.Text, wrap: true));
        }
        var items = new List<Control>
        {
            Add(col, new ActionButton("Open the key page in your browser", () => ModActions.OpenUrl(g.KeyPage), g.KeyPage)),
            Add(col, new ActionButton(CurrentKey == null ? "Paste or type the key" : "Change the key", EnterKey,
                $"Paste with the Paste key (or Ctrl+V). In {GameText.SteamTvMode}, Steam + X opens Steam's keyboard.")),
        };
        if (CurrentKey != null)
        {
            items.Add(Add(col, new ActionButton("Forget the key", () => App.Confirm($"Remove your {_site.Name} key from Luma?", "Forget it", () => Save(null, null, false)),
                "Removes it from this device.")));
        }
        if (_site.Id == "nexus")
        {
            col.AddChild(Section("Download links"));
            col.AddChild(UiTheme.Label(NxmHandler.IsRegistered(Host.Paths)
                    ? "\"Mod Manager Download\" buttons on Nexus Mods open in Luma."
                    : "Let the website's \"Mod Manager Download\" buttons send files to Luma.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            items.Add(Add(col, new ActionButton(NxmHandler.IsRegistered(Host.Paths) ? "Set up download links again" : "Handle Nexus download links",
                () =>
                {
                    if (NxmHandler.IsRegistered(Host.Paths))
                    {
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
                    else
                    {
                        ModActions.EnsureNxmHandler(quiet: false);
                    }
                    Rebuild();
                }, "Makes Luma the browser's handler for nxm:// links.")));
        }
        col.AddChild(UiTheme.Label("Your key stays on this device and is only sent to " + _site.Name + ".", UiTheme.SmallSize - 1, UiTheme.Dim, wrap: true));
        Focus.Column(items);
        Anim.Stagger(items);
        _first = items[CurrentKey == null ? 0 : Math.Min(1, items.Count - 1)];
    }

    private void EnterKey() => App.Push(new TextEntryPage($"{_site.Name} API key", "", key =>
    {
        if (key.Length > 0)
        {
            Check(key);
        }
    }, "Paste the key you copied.", secret: true));

    private async void Check(string key)
    {
        if (_checking)
        {
            return;
        }
        _checking = true;
        var before = Host.Keys.Current;
        App.Toast($"Checking the key with {_site.Name}…");
        try
        {
            // The site clients read the stored key, so save it first and put the old one back if it's refused.
            Save(key, null, false, quiet: true);
            switch (_site)
            {
                case NexusSource n:
                    var (user, premium) = await n.ValidateAsync();
                    Save(key, user, premium, quiet: true);
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
            if (IsInstanceValid(this))
            {
                if (_site.Id == "nexus")
                {
                    ModActions.EnsureNxmHandler(quiet: true);
                }
                Rebuild();
            }
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            Host.Keys.Save(before);
            App.Error(e.Message);
        }
        finally
        {
            _checking = false;
        }
    }

    private void Save(string? key, string? user, bool premium, bool quiet = false)
    {
        var c = Host.Keys.Current;
        c = _site.Id switch
        {
            "nexus" => c with { Nexus = key, NexusUser = user, NexusPremium = premium },
            "curseforge" => c with { CurseForge = key },
            "modio" => c with { Modio = key },
            _ => c,
        };
        Host.Keys.Save(c);
        if (!quiet)
        {
            App.Toast(key == null ? $"{_site.Name} key removed." : "Saved.");
            Rebuild();
        }
    }

    private static T Add<T>(Control parent, T c) where T : Control
    {
        parent.AddChild(c);
        return c;
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
