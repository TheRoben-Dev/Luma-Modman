using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>Explains the one Steam launch option that makes Steam start the game through Luma.</summary>
public partial class SteamLaunchPage : Screen
{
    private readonly DetectedGame _game;
    private Control? _first;

    internal DetectedGame Game => _game;

    public SteamLaunchPage(DetectedGame game) => _game = game;

    protected override void Build()
    {
        var option = ModManager.LaunchOption(ModActions.CliPath());
        var col = Column(12);
        AddChild(Scroller(col));
        col.AddChild(UiTheme.Label("Start with mods from Steam", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label(
            $"With this launch option, starting {_game.Title} from Steam ({GameText.SteamTvMode} too) links your mods in first and takes them out when the game closes. The game folder stays as Steam installed it, and a problem with mods never stops the game from starting.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var box = new PanelContainer();
        var bs = UiTheme.Box(UiTheme.PanelHi, 1, 10, UiTheme.Line);
        bs.ContentMarginLeft = bs.ContentMarginRight = 16;
        bs.ContentMarginTop = bs.ContentMarginBottom = 12;
        box.AddThemeStyleboxOverride("panel", bs);
        box.AddChild(UiTheme.Label(option, UiTheme.SmallSize + 1, UiTheme.AccentText, wrap: true));
        col.AddChild(box);
        col.AddChild(Section("Steps"));
        string[] steps =
        [
            "Copy the launch option below.",
            $"In Steam, open {_game.Title}'s Properties (the gear, or the menu button in {GameText.SteamTvMode}).",
            "Under General → Launch options, paste it. If something is already there, put it in front of %command% instead of replacing it.",
            "Start the game as usual.",
        ];
        for (var i = 0; i < steps.Length; i++)
        {
            col.AddChild(UiTheme.Label($"{i + 1}.  {steps[i]}", UiTheme.SmallSize + 1, UiTheme.Text, wrap: true));
        }
        if (_game.Install.LaunchOptions is { Length: > 0 } current)
        {
            col.AddChild(UiTheme.Label($"Current launch options: {current}", UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        }
        var items = new List<Control>
        {
            Add(col, new ActionButton("Copy the launch option", () =>
            {
                DisplayServer.ClipboardSet(option);
                App.Success("Copied. Paste it in Steam's launch options.");
            }, option)),
            Add(col, new ActionButton("I've set it: check again", () =>
            {
                App.ScanGames();
                App.Pop();
            }, "Reads Steam's settings again.")),
            Add(col, new ActionButton("Back", () => App.Pop())),
        };
        Focus.Column(items);
        _first = items[0];
    }

    private static T Add<T>(Control parent, T c) where T : Control
    {
        parent.AddChild(c);
        return c;
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
