using Luma.App.Ui;
using Luma.Core.Detection;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// The game's Overwrite folder: files the game or a tool made while the mods were in (configs,
/// generated patches, tool output). Luma keeps them here instead of leaving them in the game
/// folder, and puts them in last at every start. They can become a mod of their own, or be deleted.
/// The desktop layout shows the same as a dialog (Mods → Overwrite…).
/// </summary>
public partial class OverwritePage : Screen
{
    private readonly DetectedGame _game;

    internal DetectedGame Game => _game;

    public OverwritePage(DetectedGame game) => _game = game;

    /// <summary>"meshes/a.nif" (mods folder) or "game folder: BepInEx/config/x.cfg".</summary>
    internal static string Show(string file) =>
        file.StartsWith("mods/", StringComparison.Ordinal) ? file[5..] : file.StartsWith("game/", StringComparison.Ordinal) ? "game folder: " + file[5..] : file;

    internal const string About = "Files the game or a tool made while the mods were in. Luma keeps them here, out of the game folder, and puts them in last every time, so they win over every mod.";

    /// <summary>Asks for a name and makes a mod of the files.</summary>
    internal static void MakeMod(DetectedGame game, Action? done = null)
    {
        var app = Main.Instance;
        app.Push(new TextEntryPage("Name the new mod", "Generated files", name =>
        {
            if (app.Guard(() => app.Host!.Mods.OverwriteToMod(game, name)))
            {
                app.Success($"Made the mod {name.Trim()}. It's at the end of the load order.");
                done?.Invoke();
            }
        }));
    }

    internal static void Clear(DetectedGame game, Action? done = null)
    {
        var app = Main.Instance;
        var n = app.Host!.Mods.OverwriteFiles(game).Count;
        app.Confirm($"Delete the {n} file{(n == 1 ? "" : "s")} in Overwrite? The game makes them again if it needs them.", "Delete", () =>
        {
            if (app.Guard(() => app.Host.Mods.ClearOverwrite(game)))
            {
                app.Success("Overwrite is empty.");
                done?.Invoke();
            }
        });
    }

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        var files = Host.Mods.OverwriteFiles(_game);
        col.AddChild(UiTheme.Label($"Overwrite  ·  {files.Count} file{(files.Count == 1 ? "" : "s")}", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label(About, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var items = new List<Control>();
        if (files.Count > 0)
        {
            var make = new ActionButton("Make a mod from these files", () => MakeMod(_game, Rebuild), "Moves them into a new mod you can turn on and off and order like any other.");
            var clear = new ActionButton("Delete these files", () => Clear(_game, Rebuild), "Removes them for good.");
            col.AddChild(make);
            col.AddChild(clear);
            items.Add(make);
            items.Add(clear);
        }
        var rows = Column(6);
        col.AddChild(Scroller(rows));
        foreach (var f in files.Take(500))
        {
            var row = new ListRow(Show(f), "", null, null, null, null, f);
            rows.AddChild(row);
            items.Add(row);
        }
        if (files.Count == 0)
        {
            var back = new ActionButton("Back", () => App.Pop());
            col.AddChild(back);
            items.Add(back);
        }
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    private Control? _first;

    public override void FocusDefault() => _first?.GrabFocus();
}
