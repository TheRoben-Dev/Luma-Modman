using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Tools run with the mods in, like MO2's executables: xEdit, LOOT, BodySlide, Nemesis, Pandora…
/// Luma puts the mods in, starts the tool (on Linux in the game's Proton prefix) and takes the
/// mods out when it closes; what the tool wrote goes to Overwrite, and LOOT's sort becomes the
/// plugin order. Tools found in the mods, the game folder or Downloads can be added with one press.
/// The desktop layout shows the same in its Tools menu and a dialog.
/// </summary>
public partial class ToolsPage : Screen
{
    private readonly DetectedGame _game;
    private Control? _first;

    internal DetectedGame Game => _game;

    public ToolsPage(DetectedGame game) => _game = game;

    internal const string About = "Programs that work on the game's files: xEdit, LOOT, BodySlide, Nemesis, Pandora and the like. Luma puts the mods in, starts the tool, and takes the mods out when you close it. What the tool writes goes to Overwrite; LOOT's sort becomes your plugin order.";

    /// <summary>Runs a tool with the mods in, showing a busy page until it's closed.</summary>
    internal static void Run(DetectedGame game, ToolDef tool)
    {
        var app = Main.Instance;
        var host = app.Host!;
        app.Busy($"{tool.Name} is running. Close it to take the mods out again.", (_, _) => Task.Run(() => ToolRunner.Run(host, game, tool)), code =>
        {
            host.Mods.NotifyChanged(game);
            var made = host.Mods.OverwriteFiles(game).Count;
            app.Success($"{tool.Name} closed{(code != 0 ? $" (exit code {code})" : "")}. Mods taken out{(made > 0 ? $"; {made} file{(made == 1 ? "" : "s")} in Overwrite" : "")}.");
        }, cancellable: false);
    }

    /// <summary>Adds a program picked by hand: a known tool gets its usual arguments.</summary>
    internal static void AddProgram(DetectedGame game, string exe)
    {
        var host = Main.Instance.Host!;
        var known = Tools.Recognise(exe);
        var name = known?.Name ?? Path.GetFileNameWithoutExtension(exe);
        var stored = exe;
        // A program inside the game folder is kept relative to it, so it survives the game moving.
        if (exe.StartsWith(game.GameDir.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, Luma.Core.Platform.PathComparison))
        {
            stored = "{game}/" + Path.GetRelativePath(game.GameDir, exe).Replace('\\', '/');
        }
        var store = host.Mods.Store(game);
        while (store.State.Tools.Any(t => t.Name == name))
        {
            name += " (2)";
        }
        store.SetTool(new ToolDef(name, stored, known?.Args ?? ""));
        host.Mods.NotifyChanged(game);
        Main.Instance.Success($"Added {name}.");
    }

    internal static void PickProgram(DetectedGame game, Action? done = null) =>
        Main.Instance.Push(new PickFilePage("Pick the tool's program", Luma.Core.Platform.IsWindows ? ["*.exe", "*.bat"] : ["*.exe", "*.bat", "*.sh", "*.AppImage"], exe =>
        {
            AddProgram(game, exe);
            done?.Invoke();
        }));

    internal static void EditArgs(DetectedGame game, ToolDef tool, Action? done = null) =>
        Main.Instance.Push(new TextEntryPage($"Arguments for {tool.Name}", tool.Args, text =>
        {
            Main.Instance.Host!.Mods.Store(game).SetTool(tool with { Args = text.Trim() });
            Main.Instance.Host.Mods.NotifyChanged(game);
            done?.Invoke();
        }, "{game} is the game folder, {data} the mods folder (Data), {gamename} the game's name for LOOT. Paths become Windows paths under Proton."));

    internal static void Remove(DetectedGame game, ToolDef tool, Action? done = null) =>
        Main.Instance.Confirm($"Remove {tool.Name} from the tools? The program itself stays where it is.", "Remove", () =>
        {
            Main.Instance.Host!.Mods.Store(game).RemoveTool(tool.Name);
            Main.Instance.Host.Mods.NotifyChanged(game);
            done?.Invoke();
        });

    internal static string Where(DetectedGame game, ToolDef tool) =>
        tool.Exe.StartsWith("{data}/", StringComparison.Ordinal) ? "in the mods: " + tool.Exe[7..]
        : tool.Exe.StartsWith("{game}/", StringComparison.Ordinal) ? "game folder: " + tool.Exe[7..]
        : Main.Instance.Host!.Paths.Short(tool.Exe);

    protected override void Build()
    {
        _first = null;
        var col = Column(10);
        AddChild(col);
        col.AddChild(UiTheme.Label($"Tools  ·  {_game.Title}", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label(About, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var rows = Column(8);
        col.AddChild(Scroller(rows));
        var items = new List<Control>();
        var store = Host.Mods.Store(_game);
        foreach (var t in store.State.Tools)
        {
            var tool = t;
            var row = new ListRow(t.Name, Where(_game, t) + (t.Args.Length > 0 ? "  ·  " + t.Args : ""), "Run", UiTheme.AccentText, null, () =>
                App.Push(new ChoicePage($"{tool.Name}\n{Where(_game, tool)}",
                    ("Run it with the mods in", () => Run(_game, tool)),
                    ("Change its arguments", () => EditArgs(_game, tool, Rebuild)),
                    ("Remove it", () => Remove(_game, tool, Rebuild)),
                    ("Cancel", () => { }))), $"{t.Name}: A to run it, change its arguments or remove it.");
            rows.AddChild(row);
            items.Add(row);
        }
        if (store.State.Tools.Count == 0)
        {
            rows.AddChild(UiTheme.Label("No tools yet. Add one found below, or pick a program.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        var found = Host.Mods.FindTools(_game);
        if (found.Count > 0)
        {
            rows.AddChild(Section("Found on this device"));
            foreach (var f in found)
            {
                var tool = f;
                var row = new ListRow(f.Name, Where(_game, f), "Add", UiTheme.Dim, null, () =>
                {
                    store.SetTool(tool);
                    Host.Mods.NotifyChanged(_game);
                    Rebuild();
                }, $"Adds {f.Name} with the arguments it needs.");
                rows.AddChild(row);
                items.Add(row);
            }
        }
        var pick = new ActionButton("Add a program…", () => PickProgram(_game, Rebuild), "Pick any program (xEdit you unpacked, LOOT, a tool from a mod).");
        rows.AddChild(pick);
        items.Add(pick);
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
