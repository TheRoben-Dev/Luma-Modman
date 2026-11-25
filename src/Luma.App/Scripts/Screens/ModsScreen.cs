using Luma.App.Ui;
using Luma.Core.Detection;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// The Mods tab: the chosen game's setup on the left (cover, where it's installed, its loader and
/// where mods go) and its mods on the right. Luma doesn't start games: mods are linked in when the
/// game starts from Steam or the launcher, and taken out when it closes.
/// </summary>
public partial class ModsScreen : Screen
{
    private Control? _first;

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Switch, "Switch game")];

    protected override void Build()
    {
        var game = Host.ActiveGame;
        if (game == null)
        {
            BuildNoGames();
            return;
        }
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 36);
        AddChild(row);
        row.AddChild(GamePanel(game));

        var right = Column(10);
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        row.AddChild(right);
        var items = new List<Control>();
        if (!game.Profile.Moddable)
        {
            right.AddChild(UiTheme.Label("Not moddable", UiTheme.TitleSize, UiTheme.Bad));
            right.AddChild(UiTheme.Label(game.Profile.NotModdable!, UiTheme.FontSize - 1, UiTheme.Text, wrap: true));
            right.AddChild(UiTheme.Label("Luma won't install mods for it, to keep your account safe. Pick another game to work on.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            items.Add(Add(right, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
        }
        else
        {
            right.AddChild(UiTheme.Label("Mods", UiTheme.TitleSize));
            right.AddChild(UiTheme.Label($"No mods for {game.Title} yet. Find some in Browse; they install here, outside the game folder.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            items.Add(Add(right, new ActionButton("Browse mods", () => App.SwitchTab("Browse"), $"Search mod sites for {game.Title} mods.")));
            items.Add(Add(right, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
            if (game.Profile.Quirks.Count > 0)
            {
                right.AddChild(Section("Good to know"));
                foreach (var q in game.Profile.Quirks)
                {
                    right.AddChild(UiTheme.Label("•  " + q, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
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

    /// <summary>Cover, title, launcher and the facts Luma works from.</summary>
    private Control GamePanel(DetectedGame game)
    {
        var col = Column(8);
        col.SizeFlagsHorizontal = SizeFlags.Fill;
        col.CustomMinimumSize = new Vector2(330, 0);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        col.AddChild(top);
        top.AddChild(new CoverArt(game.Title, game.Install.Cover, new Vector2(120, 180), 10));
        var names = Column(4);
        names.Alignment = BoxContainer.AlignmentMode.End;
        top.AddChild(names);
        names.AddChild(UiTheme.Label(game.Title, UiTheme.FontSize + 2, UiTheme.Text, wrap: true));
        names.AddChild(UiTheme.Label(GameText.Source(game), UiTheme.SmallSize, UiTheme.Dim));
        var (status, color) = GameText.Status(game, Host.Games.Catalog);
        names.AddChild(UiTheme.Label(status, UiTheme.SmallSize, color));

        var p = game.Profile;
        col.AddChild(Section("Setup"));
        if (!p.Moddable)
        {
            Fact(col, "Runs", p.Runtime == "native" ? "Native Linux version" : "Proton");
            return col;
        }
        Fact(col, "Loader", GameText.Loader(p, Host.Games.Catalog));
        Fact(col, "Mods go to", p.ModsPath.Length == 0 ? "The game folder" : p.ModsPath.Replace("{documents}", "Documents").Replace("{appdata}", "AppData/Roaming")
            .Replace("{localappdata}", "AppData/Local").Replace("{mygames}", "Documents/My Games").Replace("{xdgdata}", "~/.local/share").Replace("{xdgconfig}", "~/.config"));
        Fact(col, "Runs", p.Runtime == "native" ? "Native Linux version" : game.Install.Prefix == null ? "Proton (prefix created on first start)" : "Proton" + (game.Install.CompatTool is { } t ? $" ({t})" : ""));
        Fact(col, "Mod sites", string.Join(", ", p.Sites.Names()));
        return col;
    }

    private static void Fact(Control col, string label, string value)
    {
        var row = Column(0);
        row.AddChild(UiTheme.Label(label, UiTheme.SmallSize - 1, UiTheme.Dim));
        row.AddChild(UiTheme.Label(value, UiTheme.SmallSize + 1, UiTheme.Text, wrap: true));
        col.AddChild(row);
    }

    private void BuildNoGames()
    {
        var scanning = App.Scanning;
        var items = EmptyState.Build(this, scanning ? "Looking for games…" : "No supported games found",
            (scanning ? "Checking your launchers for games Luma knows how to mod." : $"Luma supports {Host.Games.Catalog.Games.Count} games and looked in:")
            + "\n\n" + string.Join("\n", Luma.Host.GameLibrary.Locations.Select(l => "•  " + l)),
            ("Scan again", App.ScanGames, "Look for installed games again."),
            ("Supported games", () => App.Push(new SupportedGamesPage()), "Every game Luma can mod, and the ones it can't."));
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
