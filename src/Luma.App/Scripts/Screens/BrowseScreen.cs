using Luma.App.Ui;
using Luma.Core.Sources;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// The mod sites that carry the chosen game. A site that needs the player's key opens its guided
/// setup first; the others open straight into search. Community sites a game has (map sites and
/// the like, with no app access) are listed below and open in the browser.
/// </summary>
public partial class BrowseScreen : Screen
{
    /// <summary>What each site covers, by site id.</summary>
    private static readonly Dictionary<string, string> About = new()
    {
        ["thunderstore"] = "Community mods, mostly for Unity games with BepInEx. No account needed.",
        ["modio"] = "Mods published through the game's mod.io page. Needs a free mod.io key.",
        ["curseforge"] = "Mods for many PC games. Needs a free CurseForge key.",
        ["nexus"] = "The biggest mod site. Browse freely; downloads need a Nexus account.",
    };

    private Control? _first;

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Switch, "Switch game")];

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        var game = Host.ActiveGame;
        col.AddChild(UiTheme.Label(game == null ? "Mod sites" : $"Mods for {game.Title}", UiTheme.TitleSize));
        var items = new List<Control>();
        if (game == null)
        {
            col.AddChild(UiTheme.Label("Pick a game first: Luma shows the sites that carry it.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            items.Add(Add(col, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
        }
        else if (!game.Profile.Moddable)
        {
            col.AddChild(UiTheme.Label($"{game.Title} can't be modded safely: {game.Profile.NotModdable}", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            items.Add(Add(col, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
        }
        else
        {
            col.AddChild(UiTheme.Label("Pick a site to search it. Luma follows each site's rules and credits the authors.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            var list = Column(8);
            col.AddChild(Scroller(list));
            foreach (var s in Host.Sites.For(game.Profile))
            {
                var site = s;
                var ready = site.Ready;
                var (status, color) = site switch
                {
                    NexusSource n => n.HasKey ? (Host.Keys.Current.NexusPremium ? "Premium" : "Signed in", UiTheme.Good) : ("Browse only", UiTheme.Dim),
                    _ when site.Id == "thunderstore" => ("Ready", UiTheme.Good),
                    _ => ready ? ("Ready", UiTheme.Good) : ("Set up", UiTheme.Warn),
                };
                var row = new ListRow(site.Name, About.GetValueOrDefault(site.Id, ""), status, color, null,
                    () => App.Push(ready ? new SiteBrowsePage(site, game) : new SiteSetupPage(site)), $"{site.Name}  ·  {About.GetValueOrDefault(site.Id, "")}");
                list.AddChild(row);
                items.Add(row);
            }
            if (game.Profile.Links.Count > 0)
            {
                list.AddChild(Section("Community sites"));
                foreach (var l in game.Profile.Links)
                {
                    var link = l;
                    var row = new ListRow(link.Name, link.About, "Opens in browser", UiTheme.Dim, null, () => ModActions.OpenUrl(link.Url),
                        $"{link.Name}: {link.About}");
                    list.AddChild(row);
                    items.Add(row);
                }
            }
        }
        Focus.Column(items);
        Anim.Stagger(items);
        _first = items.FirstOrDefault();
    }

    private static T Add<T>(Control parent, T c) where T : Control
    {
        parent.AddChild(c);
        return c;
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
