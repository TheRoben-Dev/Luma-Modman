using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// A Bethesda game's plugin list as Luma writes it to plugins.txt when the mods go in: masters
/// first, then the enabled mods' plugins in mod order. Plugins of Luma's mods can be turned off
/// one by one; the game's other plugins (Creations, hand-installed) keep the state they had.
/// The desktop layout shows the same list as a dialog with checkboxes.
/// </summary>
public partial class PluginsPage : Screen
{
    private readonly DetectedGame _game;
    private Control? _first;
    private string? _focus;

    internal DetectedGame Game => _game;

    public PluginsPage(DetectedGame game) => _game = game;

    /// <summary>The list, or a reason it can't be worked out.</summary>
    internal static (List<PluginEntry> List, string? Problem) Load(DetectedGame game)
    {
        try
        {
            return (Main.Instance.Host!.Mods.Plugins(game) ?? [], null);
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            return ([], e.Message);
        }
    }

    internal static string ModName(DetectedGame game, string? id) =>
        id == null ? "Not from Luma" : Main.Instance.Host!.Mods.Store(game).State.Find(id)?.Name ?? id;

    internal static void Toggle(DetectedGame game, PluginEntry p)
    {
        Main.Instance.Host!.Mods.Store(game).SetPluginEnabled(p.Name, !p.Enabled);
        Main.Instance.Host.Mods.NotifyChanged(game);
        if (Main.Instance.Host.Mods.IsDeployed(game))
        {
            Main.Instance.Toast("Takes effect the next time the mods go in (or the game starts from Steam).");
        }
    }

    protected override void Build()
    {
        _first = null;
        var col = Column(10);
        AddChild(col);
        col.AddChild(UiTheme.Label("Plugins", UiTheme.TitleSize));
        var (list, problem) = Load(_game);
        col.AddChild(UiTheme.Label(problem ?? $"{list.Count(p => p.Enabled)} of {list.Count} on. Luma writes this order to plugins.txt when the mods go in: masters first, then your mods' plugins in mod order. A for on or off.",
            UiTheme.SmallSize + 1, problem != null ? UiTheme.Warn : UiTheme.Dim, wrap: true));
        var rows = Column(8);
        col.AddChild(Scroller(rows));
        var items = new List<Control>();
        var i = 0;
        foreach (var p in list)
        {
            var plugin = p;
            var detail = $"{++i}  ·  {(p.Master ? "master" : "plugin")}  ·  {ModName(_game, p.ModId)}";
            var row = new ListRow(p.Name, detail, p.Enabled ? "On" : "Off", p.Enabled ? UiTheme.Good : UiTheme.Dim, null,
                p.ModId == null ? null : () =>
                {
                    _focus = plugin.Name;
                    Toggle(_game, plugin);
                    Rebuild();
                },
                p.ModId == null ? $"{p.Name}: not from a Luma mod, so it keeps the state it has in the game." : $"{p.Name}: A turns it {(p.Enabled ? "off" : "on")}.");
            rows.AddChild(row);
            items.Add(row);
            if (p.Name == _focus)
            {
                _first = row;
            }
        }
        if (items.Count == 0)
        {
            rows.AddChild(UiTheme.Label(problem == null ? "No plugins yet: install mods that bring .esp, .esm or .esl files." : "", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            var back = new ActionButton("Back", () => App.Pop());
            col.AddChild(back);
            items.Add(back);
        }
        Focus.Column(items);
        _first ??= items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
