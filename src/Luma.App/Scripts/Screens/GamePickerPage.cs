using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>Switches the game every tab works on. Lists the supported games found on this machine.</summary>
public partial class GamePickerPage : Screen
{
    private Control? _first;

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Secondary, "Scan again")];

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        col.AddChild(UiTheme.Label("Pick a game", UiTheme.TitleSize));
        var games = Host.Games.Games;
        col.AddChild(UiTheme.Label(games.Count == 0
                ? "No supported games found yet."
                : $"{games.Count} supported game{(games.Count == 1 ? "" : "s")} installed. Mods, Browse and Downloads work on the one you pick.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var list = Column(8);
        col.AddChild(Scroller(list));
        var items = new List<Control>();
        var active = Host.ActiveGame?.Key;
        foreach (var g in games)
        {
            var game = g;
            var (status, color) = GameText.Status(game, Host.Games.Catalog);
            var row = new ListRow(game.Title, GameText.Source(game) + (game.Key == active ? "  ·  working on now" : ""), status, color,
                new CoverArt(game.Title, game.Install.Cover, new Vector2(44, 66), 5),
                () => App.SetActiveGame(game.Key),
                game.Profile.Moddable ? $"{game.Title}  ·  {GameText.Source(game)}" : $"{game.Title}: {game.Profile.NotModdable}");
            list.AddChild(row);
            items.Add(row);
        }
        var scan = new ActionButton("Scan again", App.ScanGames, "Look for installed games again (Steam, Heroic, Lutris).");
        var all = new ActionButton($"Supported games ({Host.Games.Catalog.Games.Count})", () => App.Push(new SupportedGamesPage()),
            "Every game Luma can mod, and the ones it can't.");
        list.AddChild(scan);
        list.AddChild(all);
        items.Add(scan);
        items.Add(all);
        Focus.Column(items);
        Anim.Stagger(items);
        _first = items.FirstOrDefault(i => i is ListRow r && r.TooltipText.StartsWith(Host.ActiveGame?.Title ?? "\0", StringComparison.Ordinal)) ?? items[0];
    }

    public override void OnAction(string action)
    {
        if (action == Pad.Secondary)
        {
            App.ScanGames();
        }
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
