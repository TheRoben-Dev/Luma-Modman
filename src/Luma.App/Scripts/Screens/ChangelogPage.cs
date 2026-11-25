using Luma.App.Ui;
using Luma.Host.Updates;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Release notes as a scrolling list of version cards: "What's new" after an update (only the
/// versions since the last one seen) or the full history from Settings → Updates.
/// </summary>
public partial class ChangelogPage : Screen
{
    private readonly string _title;
    private readonly IReadOnlyList<ChangelogEntry> _entries;
    private readonly bool _whatsNew;
    private Control? _first;

    public ChangelogPage(string title, IReadOnlyList<ChangelogEntry> entries, bool whatsNew = false)
    {
        _title = title;
        _entries = entries;
        _whatsNew = whatsNew;
    }

    protected override void Build()
    {
        var col = Column(12);
        AddChild(Scroller(col));
        col.AddChild(UiTheme.Label(_title, UiTheme.TitleSize, wrap: true));
        var items = new List<Control>();
        if (_entries.Count == 0)
        {
            col.AddChild(UiTheme.Label("No changelog in this build.", UiTheme.SmallSize + 1, UiTheme.Dim));
        }
        foreach (var e in _entries)
        {
            var card = new PanelContainer { FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 0, 12));
            var pad = new MarginContainer();
            foreach (var side in new[] { "left", "right", "top", "bottom" })
            {
                pad.AddThemeConstantOverride("margin_" + side, 16);
            }
            card.AddChild(pad);
            var body = Column(6);
            pad.AddChild(body);
            var current = e.Version.Major == Host.Updater.CurrentVersion.Major && e.Version.Minor == Host.Updater.CurrentVersion.Minor &&
                          Math.Max(0, e.Version.Build) == Math.Max(0, Host.Updater.CurrentVersion.Build);
            body.AddChild(UiTheme.Label(current ? $"{e.Display}  ·  this version" : e.Display, UiTheme.FontSize + 4, current ? UiTheme.AccentText : UiTheme.Text));
            foreach (var item in e.Items)
            {
                body.AddChild(UiTheme.Label("• " + item, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            col.AddChild(card);
            items.Add(card);
        }
        var done = new ActionButton(_whatsNew ? "Continue" : "Back", () => App.Pop());
        col.AddChild(done);
        items.Add(done);
        Focus.Column(items);
        Anim.Stagger(items, 0.03);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
