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

    public SiteSetupPage(IModSource site) => _site = site;

    internal IModSource Site => _site;

    private string? CurrentKey => SiteKeys.CurrentKey(_site);

    protected override void Build()
    {
        var col = Column(12);
        AddChild(Scroller(col));
        col.AddChild(UiTheme.Label($"Set up {_site.Name}", UiTheme.TitleSize));
        var g = SiteKeys.For(_site);
        col.AddChild(UiTheme.Label(g.Why, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));

        var status = SiteKeys.Status(_site);
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
            items.Add(Add(col, new ActionButton("Forget the key", () => App.Confirm($"Remove your {_site.Name} key from Luma?", "Forget it", () =>
                {
                    SiteKeys.Forget(_site);
                    Rebuild();
                }),
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
                    SiteKeys.SetUpNxmLinks();
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
        if (await SiteKeys.Check(_site, key) && IsInstanceValid(this))
        {
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
