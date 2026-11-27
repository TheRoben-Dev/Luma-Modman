using Luma.App.Ui;
using Luma.Core.Detection;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// A mod's files, one folder at a time, like MO2's file tree: hide files or whole folders so they
/// stay out of the game (an optional texture set, a readme, a plugin you don't want). The desktop
/// layout shows the same as a tree in a dialog.
/// </summary>
public partial class ModFilesPage : Screen
{
    private readonly DetectedGame _game;
    private readonly string _id;
    private readonly string _prefix;
    private string? _focusKey;

    internal DetectedGame Game => _game;
    internal string Id => _id;

    /// <param name="prefix">The folder shown: "" for the top, else a key ending in "/" ("mods/textures/").</param>
    public ModFilesPage(DetectedGame game, string id, string prefix = "")
    {
        _game = game;
        _id = id;
        _prefix = prefix;
    }

    internal const string About = "Hidden files stay out of the game when the mods go in; the mod itself keeps them. Hide an optional texture set, a readme or a plugin you don't want.";

    /// <summary>One entry of a folder: a sub-folder (Key ends in "/") or a file.</summary>
    internal sealed record Entry(string Name, string Key, bool Dir, int Files, long Size);

    /// <summary>"Mods folder" and "Game folder" for the two roots, else the folder's own name.</summary>
    internal static string RootName(string key) => key switch
    {
        "mods/" => "Mods folder",
        "game/" => "Game folder",
        _ => key.TrimEnd('/').Split('/')[^1],
    };

    /// <summary>The folders and files directly in <paramref name="prefix"/>, folders first.</summary>
    internal static List<Entry> Entries(IEnumerable<(string Key, long Size)> files, string prefix)
    {
        var dirs = new Dictionary<string, (int Files, long Size)>(StringComparer.OrdinalIgnoreCase);
        var list = new List<Entry>();
        foreach (var (key, size) in files)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var rest = key[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                list.Add(new Entry(rest, key, false, 1, size));
                continue;
            }
            var dir = prefix + rest[..(slash + 1)];
            dirs[dir] = dirs.TryGetValue(dir, out var d) ? (d.Files + 1, d.Size + size) : (1, size);
        }
        return dirs.Select(d => new Entry(RootName(d.Key), d.Key, true, d.Value.Files, d.Value.Size))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Where to start: the mods folder when the mod has nothing for the game folder.</summary>
    internal static string Start(IEnumerable<(string Key, long Size)> files) =>
        files.All(f => f.Key.StartsWith("mods/", StringComparison.Ordinal)) ? "mods/" : "";

    internal static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    /// <summary>Hides or shows a file or folder; tells the player when a folder above it is hidden.</summary>
    internal static void Toggle(DetectedGame game, string id, string key, Action? done = null)
    {
        var app = Main.Instance;
        var mod = app.Host!.Mods.Store(game).State.Find(id);
        if (mod == null)
        {
            return;
        }
        var hidden = mod.IsHidden(key);
        if (hidden && !mod.Hidden.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            app.Toast("It's hidden with a folder it's in. Show that folder to bring it back.");
            return;
        }
        if (app.Guard(() => app.Host.Mods.SetHidden(game, id, [key], !hidden)) && app.Host.Mods.IsDeployed(game))
        {
            app.Toast("The mods are in the game now: this takes effect the next time they go in.");
        }
        done?.Invoke();
    }

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        var store = Host.Mods.Store(_game);
        var mod = store.State.Find(_id);
        var files = Host.Mods.ModFiles(_game, _id);
        var where = _prefix.Length == 0 ? "" : "  ·  " + string.Join(" / ", _prefix.TrimEnd('/').Split('/').Select((p, i) => i == 0 ? RootName(p + "/") : p));
        col.AddChild(UiTheme.Label($"{mod?.Name ?? _id}{where}", UiTheme.TitleSize));
        var hiddenCount = mod == null ? 0 : files.Count(f => mod.IsHidden(f.Key));
        col.AddChild(UiTheme.Label($"{files.Count} file{(files.Count == 1 ? "" : "s")}{(hiddenCount > 0 ? $", {hiddenCount} hidden" : "")}. {About}", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var items = new List<Control>();
        Control? focus = null;
        if (_prefix.Length > 0 && mod != null && _prefix.Count(c => c == '/') > 1)
        {
            var inHidden = mod.IsHidden(_prefix);
            var self = new ActionButton(inHidden ? "Show this whole folder" : "Hide this whole folder", () => Toggle(_game, _id, _prefix, Rebuild),
                inHidden ? "Puts the folder back into the game with the mod." : "Keeps everything in this folder out of the game.");
            col.AddChild(self);
            items.Add(self);
        }
        var rows = Column(6);
        col.AddChild(Scroller(rows));
        var entries = Entries(files, _prefix);
        foreach (var e in entries.Take(500))
        {
            var hidden = mod?.IsHidden(e.Key) == true;
            var key = e.Key;
            var row = e.Dir
                ? new ListRow(e.Name + "/", $"{e.Files} file{(e.Files == 1 ? "" : "s")}  ·  {Bytes(e.Size)}", hidden ? "Hidden" : null, UiTheme.Warn, null,
                    () => App.Push(new ModFilesPage(_game, _id, key)), "Opens the folder.")
                : new ListRow(e.Name, Bytes(e.Size), hidden ? "Hidden" : "In", hidden ? UiTheme.Warn : UiTheme.Dim, null, () =>
                {
                    _focusKey = key;
                    Toggle(_game, _id, key, Rebuild);
                }, hidden ? "Press to put it back in the game." : "Press to hide it (keep it out of the game).");
            rows.AddChild(row);
            items.Add(row);
            if (key == _focusKey)
            {
                focus = row;
            }
        }
        if (entries.Count > 500)
        {
            rows.AddChild(UiTheme.Label($"…and {entries.Count - 500} more. Hide the folder as a whole, or use the desktop layout's file tree.", UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        }
        var back = new ActionButton("Back", () => App.Pop());
        col.AddChild(back);
        items.Add(back);
        Focus.Column(items);
        _first = focus ?? items.FirstOrDefault();
    }

    private Control? _first;

    public override void FocusDefault() => _first?.GrabFocus();
}
