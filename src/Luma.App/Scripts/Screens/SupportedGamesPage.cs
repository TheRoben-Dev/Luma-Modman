using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>Every game Luma has a profile for, installed ones first, with the reason for the ones it won't mod.</summary>
public partial class SupportedGamesPage : Screen
{
    private Control? _first;

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        var catalog = Host.Games.Catalog;
        col.AddChild(UiTheme.Label("Supported games", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label($"{catalog.Games.Count(g => g.Moddable)} games Luma can mod, and {catalog.Games.Count(g => !g.Moddable)} it won't because of anti-cheat or online bans. "
                                   + "Install one from Steam, Heroic or Lutris and Luma finds it.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var list = Column(6);
        col.AddChild(Scroller(list));
        var installed = Host.Games.Games.Select(g => g.Profile.Id).ToHashSet();
        var items = new List<Control>();
        foreach (var g in catalog.Games.OrderBy(g => installed.Contains(g.Id) ? 0 : 1).ThenBy(g => g.Moddable ? 0 : 1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var p = g;
            var detail = p.Moddable
                ? string.Join("  ·  ", new[] { p.Loader is { } l && catalog.Loaders.TryGetValue(l, out var li) ? li.Name : null, string.Join(", ", p.Sites.Names()) }.Where(s => !string.IsNullOrEmpty(s)))
                : p.NotModdable!;
            var (status, color) = installed.Contains(p.Id) ? ("Installed", UiTheme.Good) : p.Moddable ? ((string?)null, UiTheme.Dim) : ("Not moddable", UiTheme.Bad);
            var row = new ListRow(p.Name, detail, status, color, null, () =>
            {
                if (!p.Moddable)
                {
                    App.Toast(p.NotModdable!);
                }
                else if (p.Quirks.Count > 0)
                {
                    App.Toast(p.Quirks[0]);
                }
            }, $"{p.Name}  ·  {detail}");
            list.AddChild(row);
            items.Add(row);
        }
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
