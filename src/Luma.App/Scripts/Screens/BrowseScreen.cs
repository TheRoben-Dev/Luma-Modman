using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>The mod sites that carry the chosen game. Each opens its own search (next update).</summary>
public partial class BrowseScreen : Screen
{
    /// <summary>Site name (as <see cref="Luma.Core.Games.SiteIds.Names"/> gives it), what it covers and what it needs.</summary>
    public static readonly (string Name, string About, string Needs)[] Sites =
    [
        ("Thunderstore", "Community mods, mostly for Unity games with BepInEx.", "No account needed"),
        ("mod.io", "Mods published through the game's own mod.io page.", "Free mod.io account"),
        ("CurseForge", "Mods for many PC games.", "Free CurseForge API key"),
        ("Nexus Mods", "The biggest mod site.", "Nexus account; Premium for one-tap downloads"),
    ];

    private Control? _first;

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Switch, "Switch game")];

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        var game = Host.ActiveGame;
        col.AddChild(UiTheme.Label(game == null ? "Mod sites" : $"Mods for {game.Title}", UiTheme.TitleSize));
        var items = new List<Control>();
        if (game is { Profile.Moddable: false })
        {
            col.AddChild(UiTheme.Label($"{game.Title} can't be modded safely: {game.Profile.NotModdable}", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            items.Add(Add(col, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
        }
        else
        {
            col.AddChild(UiTheme.Label(game == null
                    ? "Pick a game first: Luma shows the sites that carry it."
                    : "Pick a site to search it. Luma follows each site's rules and credits the authors.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            var list = Column(8);
            col.AddChild(Scroller(list));
            var carried = game?.Profile.Sites.Names().ToHashSet();
            foreach (var site in Sites.Where(s => carried == null || carried.Contains(s.Name)))
            {
                var s = site;
                var row = new ListRow(s.Name, s.About, s.Needs, UiTheme.Dim, null,
                    () => App.Toast($"{s.Name} search arrives in the next update."), $"{s.Name}  ·  {s.About}");
                list.AddChild(row);
                items.Add(row);
            }
            if (game == null)
            {
                items.Add(Add(list, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
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
