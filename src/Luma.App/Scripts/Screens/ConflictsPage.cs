using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Files that more than one enabled mod provides, and which mod's copy goes in. The mod later in
/// the load order wins unless the player picks another for a file (kept per profile). With a mod
/// given, only that mod's conflicts show, with a way to make it win all of them.
/// The desktop layout shows the same list as a dialog with a winner drop-down per file.
/// </summary>
public partial class ConflictsPage : Screen
{
    private readonly DetectedGame _game;
    private readonly string? _modId;
    private Control? _first;
    private string? _focus;

    internal DetectedGame Game => _game;
    internal string? ModId => _modId;

    public ConflictsPage(DetectedGame game, string? modId = null)
    {
        _game = game;
        _modId = modId;
    }

    /// <summary>The conflicts (of one mod, if given), or a reason they can't be worked out.</summary>
    internal static (List<Conflict> List, string? Problem) Load(DetectedGame game, string? modId)
    {
        try
        {
            var all = Main.Instance.Host!.Mods.Plan(game).Conflicts;
            return (all.Where(c => modId == null || c.Providers?.Contains(modId) == true).ToList(), null);
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            return ([], e.Message);
        }
    }

    /// <summary>How many files of an enabled mod's are also in another enabled mod.</summary>
    internal static int Count(DetectedGame game) => Load(game, null).List.Count;

    /// <summary>"meshes/armor.nif" (mods folder) or "game: bin/x.dll" (game folder).</summary>
    internal static string Show(Conflict c) =>
        c.Key.StartsWith("mods/", StringComparison.Ordinal) ? c.Key[5..] : c.Key.StartsWith("game/", StringComparison.Ordinal) ? "game folder: " + c.Key[5..] : c.Key;

    internal static string ModName(DetectedGame game, string id) => Main.Instance.Host!.Mods.Store(game).State.Find(id)?.Name ?? id;

    /// <summary>The mod the load order alone would pick: the last of the providers.</summary>
    internal static string ByOrder(Conflict c) => c.Providers is { Count: > 0 } p ? p[^1] : c.Winner;

    /// <summary>Makes <paramref name="modId"/> win the files (null: follow the load order again).</summary>
    internal static void Pick(DetectedGame game, IEnumerable<string> keys, string? modId)
    {
        Main.Instance.Host!.Mods.Store(game).SetWinner(keys, modId);
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
        var modName = _modId == null ? null : ModName(_game, _modId);
        col.AddChild(UiTheme.Label(modName == null ? "Conflicts" : $"Conflicts: {modName}", UiTheme.TitleSize));
        var (list, problem) = Load(_game, _modId);
        col.AddChild(UiTheme.Label(problem ?? (list.Count == 0
                ? "No files are in more than one of the mods that are on."
                : $"{list.Count} file{(list.Count == 1 ? " is" : "s are")} in more than one mod. The mod lower in the load order wins, unless you pick another. A picks the winner."),
            UiTheme.SmallSize + 1, problem != null ? UiTheme.Warn : UiTheme.Dim, wrap: true));
        var items = new List<Control>();
        if (_modId != null && list.Count > 0)
        {
            var id = _modId;
            var keys = list.Select(c => c.Key).ToList();
            var win = new ActionButton($"Make {modName} win all of these", () =>
            {
                Pick(_game, keys, id);
                Rebuild();
            }, "Its copy of each of these files goes in, wherever it is in the load order.");
            col.AddChild(win);
            items.Add(win);
            if (list.Any(c => c.Picked))
            {
                var reset = new ActionButton("Follow the load order for all of these", () =>
                {
                    Pick(_game, keys, null);
                    Rebuild();
                }, "Forgets the winners picked for these files.");
                col.AddChild(reset);
                items.Add(reset);
            }
        }
        var rows = Column(8);
        col.AddChild(Scroller(rows));
        foreach (var c in list)
        {
            var conflict = c;
            var others = string.Join(", ", c.Losers.Select(l => ModName(_game, l)));
            var row = new ListRow(Show(c), $"{ModName(_game, c.Winner)} wins  ·  also in {others}", c.Picked ? "Picked" : "Load order", c.Picked ? UiTheme.Accent : UiTheme.Dim, null,
                () =>
                {
                    _focus = conflict.Key;
                    Choose(conflict);
                },
                $"{Show(c)}: A picks which mod's copy goes in.");
            rows.AddChild(row);
            items.Add(row);
            if (c.Key == _focus)
            {
                _first = row;
            }
        }
        if (list.Count == 0)
        {
            var back = new ActionButton("Back", () => App.Pop());
            col.AddChild(back);
            items.Add(back);
        }
        Focus.Column(items);
        _first ??= items.FirstOrDefault();
    }

    private void Choose(Conflict c)
    {
        var options = new List<(string, Action)>();
        foreach (var id in c.Providers ?? [c.Winner])
        {
            var mod = id;
            options.Add(($"{ModName(_game, id)}{(id == c.Winner ? "  (wins now)" : "")}", () =>
            {
                Pick(_game, [c.Key], mod == ByOrder(c) ? null : mod);
                Rebuild();
            }));
        }
        if (c.Picked)
        {
            options.Add(("Follow the load order", () =>
            {
                Pick(_game, [c.Key], null);
                Rebuild();
            }));
        }
        App.Push(new ChoicePage($"Which mod's {Show(c)} goes in?", options.ToArray()));
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
