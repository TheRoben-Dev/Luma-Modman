using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Picks a file by browsing folders with the controller: the places to start from (home,
/// Downloads, Desktop, SD cards and drives), then folders and the files that match. The desktop
/// layout shows the system's file picker instead.
/// </summary>
public partial class PickFilePage : Screen
{
    private readonly string _title;
    private readonly Action<string> _picked;
    private string? _dir;
    private Control? _first;

    internal string Title => _title;

    /// <summary>File name patterns for the desktop file picker ("*.exe").</summary>
    internal string[] Patterns { get; }

    internal Action<string> Picked => _picked;

    public PickFilePage(string title, string[] patterns, Action<string> picked)
    {
        _title = title;
        Patterns = patterns;
        _picked = picked;
    }

    private bool Matches(string file) => Patterns.Any(p => p == "*" || file.EndsWith(p.TrimStart('*'), StringComparison.OrdinalIgnoreCase));

    protected override void Build()
    {
        _first = null;
        var col = Column(10);
        AddChild(col);
        col.AddChild(UiTheme.Label(_title, UiTheme.TitleSize));
        var items = new List<Control>();
        var list = Column(8);
        if (_dir == null)
        {
            col.AddChild(Section("Start in"));
            col.AddChild(Scroller(list));
            foreach (var (name, dir) in AddFilePage.Places(Host.Paths.Home))
            {
                var d = dir;
                var row = new ListRow(name, Host.Paths.Short(dir), "Open", UiTheme.Dim, null, () => Open(d), $"Look in {dir}.");
                list.AddChild(row);
                items.Add(row);
            }
        }
        else
        {
            col.AddChild(Section(Host.Paths.Short(_dir)));
            var up = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_dir));
            var back = new ActionButton(up != null ? "Up one folder" : "Back to the start", () => Open(up), "B goes back to where you started.");
            col.AddChild(back);
            items.Add(back);
            col.AddChild(Scroller(list));
            try
            {
                foreach (var f in Directory.EnumerateFiles(_dir).Where(Matches).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    var file = f;
                    var row = new ListRow(Path.GetFileName(f), "", "Pick", UiTheme.AccentText, null, () =>
                    {
                        App.Pop();
                        _picked(file);
                    }, $"Pick {Path.GetFileName(f)}.");
                    list.AddChild(row);
                    items.Add(row);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
            foreach (var d in AddFilePage.SafeDirs(_dir))
            {
                var sub = d;
                var row = new ListRow(Path.GetFileName(d) + "/", "Folder", "Open", UiTheme.Dim, null, () => Open(sub), $"Open {Path.GetFileName(d)}.");
                list.AddChild(row);
                items.Add(row);
            }
            if (items.Count == 1)
            {
                list.AddChild(UiTheme.Label("Nothing to pick here.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
        }
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    private void Open(string? dir)
    {
        _dir = dir;
        Rebuild();
    }

    public override bool OnBack()
    {
        if (_dir == null)
        {
            return false;
        }
        Open(null);
        return true;
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
