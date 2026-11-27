using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// A Bethesda game's plugin list as Luma writes it to plugins.txt when the mods go in: masters
/// first, then the enabled mods' plugins in mod order, or in an order set by hand or by Sort.
/// Plugins of Luma's mods can be turned off one by one; the game's other plugins (Creations,
/// hand-installed) keep the state they had. Missing masters, masters that load too late and the
/// plugin limit are flagged. The desktop layout shows the same list as a dialog with checkboxes.
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

    internal static string Kind(PluginEntry p) => p.Light ? (p.Master ? "light master" : "light") : p.Master ? "master" : "plugin";

    internal static List<PluginProblem> Problems(DetectedGame game)
    {
        try
        {
            return Main.Instance.Host!.Mods.PluginProblems(game);
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            return [];
        }
    }

    internal static bool CustomOrder(DetectedGame game)
    {
        var state = Main.Instance.Host!.Mods.Store(game).State;
        return state.PluginOrder.TryGetValue(state.Active, out var o) && o.Count > 0;
    }

    internal static void Sort(DetectedGame game, Action done)
    {
        if (Main.Instance.Guard(() =>
        {
            var moved = Main.Instance.Host!.Mods.SortPlugins(game);
            Main.Instance.Success(moved == 0 ? "The plugins were already in a working order." : $"Sorted: {moved} plugin{(moved == 1 ? "" : "s")} moved so each loads after its masters.");
        }))
        {
            Changed(game);
            done();
        }
    }

    internal static void ResetOrder(DetectedGame game, Action done)
    {
        Main.Instance.Host!.Mods.ResetPluginOrder(game);
        Changed(game);
        done();
    }

    internal static void Move(DetectedGame game, string name, int delta, Action done)
    {
        if (Main.Instance.Guard(() => Main.Instance.Host!.Mods.MovePlugin(game, name, delta)))
        {
            Changed(game);
            done();
        }
    }

    private static void Changed(DetectedGame game)
    {
        if (Main.Instance.Host!.Mods.IsDeployed(game))
        {
            Main.Instance.Toast("Takes effect the next time the mods go in (or the game starts from Steam).");
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
        var custom = CustomOrder(_game);
        col.AddChild(UiTheme.Label(problem ?? $"{list.Count(p => p.Enabled)} of {list.Count} on. Luma writes this order to plugins.txt when the mods go in: masters first, then {(custom ? "the order you set" : "your mods' plugins in mod order")}. A to turn one on or off or move it.",
            UiTheme.SmallSize + 1, problem != null ? UiTheme.Warn : UiTheme.Dim, wrap: true));
        var items = new List<Control>();
        var problems = Problems(_game);
        foreach (var pr in problems.Take(6))
        {
            col.AddChild(UiTheme.Label("⚠ " + pr.Text, UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
        if (problems.Count > 6)
        {
            col.AddChild(UiTheme.Label($"…and {problems.Count - 6} more.", UiTheme.SmallSize + 1, UiTheme.Warn));
        }
        if (list.Count > 1)
        {
            var sort = new ActionButton("Sort plugins", () => Sort(_game, Rebuild), "Moves plugins so each loads after the masters it needs, changing as little as possible. For LOOT's full rules, add LOOT under Tools.");
            col.AddChild(sort);
            items.Add(sort);
            if (custom)
            {
                var reset = new ActionButton("Follow the mod order again", () => ResetOrder(_game, Rebuild), "Forgets the order you set: plugins load in the order of their mods.");
                col.AddChild(reset);
                items.Add(reset);
            }
        }
        var rows = Column(8);
        col.AddChild(Scroller(rows));
        var bad = new HashSet<string>(problems.Where(x => x.Plugin != null).Select(x => x.Plugin!), StringComparer.OrdinalIgnoreCase);
        var i = 0;
        foreach (var p in list)
        {
            var plugin = p;
            var needs = p.Needs is { Count: > 0 } n ? $"  ·  needs {string.Join(", ", n.Take(3))}{(n.Count > 3 ? "…" : "")}" : "";
            var detail = $"{++i}  ·  {Kind(p)}  ·  {ModName(_game, p.ModId)}{needs}";
            var row = new ListRow(p.Name, detail, bad.Contains(p.Name) ? "Problem" : p.Enabled ? "On" : "Off", bad.Contains(p.Name) ? UiTheme.Warn : p.Enabled ? UiTheme.Good : UiTheme.Dim, null,
                () =>
                {
                    _focus = plugin.Name;
                    Options(plugin);
                },
                p.ModId == null ? $"{p.Name}: not from a Luma mod, so it keeps the state it has in the game. A to move it." : $"{p.Name}: A to turn it {(p.Enabled ? "off" : "on")} or move it.");
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

    private void Options(PluginEntry p)
    {
        var options = new List<(string, Action)>();
        if (p.ModId != null)
        {
            options.Add((p.Enabled ? "Turn it off" : "Turn it on", () =>
            {
                Toggle(_game, p);
                Rebuild();
            }));
        }
        options.Add(("Move up", () => Move(_game, p.Name, -1, Rebuild)));
        options.Add(("Move down", () => Move(_game, p.Name, +1, Rebuild)));
        options.Add(("Move up 10", () => Move(_game, p.Name, -10, Rebuild)));
        options.Add(("Move down 10", () => Move(_game, p.Name, +10, Rebuild)));
        options.Add(("Cancel", () => { }));
        var needs = p.Needs is { Count: > 0 } n ? $"\nNeeds: {string.Join(", ", n)}" : "";
        App.Push(new ChoicePage($"{p.Name} ({Kind(p)}, from {ModName(_game, p.ModId)}){needs}", options.ToArray()));
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
