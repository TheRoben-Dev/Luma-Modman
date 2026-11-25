using Luma.App.Ui;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>The app log, newest lines first, coloured by level. Scrolls with the d-pad or a finger.</summary>
public partial class LogPage : Screen
{
    private const int MaxLines = 400;
    private Control? _first;

    protected override void Build()
    {
        var col = Column(4);
        AddChild(Scroller(col));
        col.AddChild(UiTheme.Label("App log", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label(AppLog.FilePath ?? "The log isn't open.", UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        var lines = ReadTail();
        var rows = new List<Control>();
        if (lines.Count == 0)
        {
            col.AddChild(UiTheme.Label("Nothing logged yet.", UiTheme.SmallSize + 1, UiTheme.Dim));
        }
        // Lines are grouped in blocks of ten so the d-pad moves a screenful at a time, not one line.
        foreach (var chunk in lines.Chunk(10))
        {
            var block = new PanelContainer { FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "Log lines" };
            block.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 0, 8));
            var body = Column(2);
            block.AddChild(body);
            foreach (var l in chunk)
            {
                var color = l.Contains(" ERROR ") ? UiTheme.Bad : l.Contains(" WARN ") ? UiTheme.Warn : UiTheme.Text;
                body.AddChild(UiTheme.Label(l, UiTheme.SmallSize, color, wrap: true));
            }
            col.AddChild(block);
            rows.Add(block);
        }
        var back = new ActionButton("Back", () => App.Pop());
        col.AddChild(back);
        rows.Add(back);
        Focus.Column(rows);
        _first = rows[0];
    }

    private static List<string> ReadTail()
    {
        try
        {
            if (AppLog.FilePath is not { } path || !File.Exists(path))
            {
                return [];
            }
            using var s = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(s);
            var all = r.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return all.TakeLast(MaxLines).Reverse().ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
