using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Adds files downloaded by hand from a community site with no app access (DiscoMaps maps for
/// Dead as Disco): pick an archive from Downloads and Luma unpacks it where the game looks.
/// </summary>
public partial class ImportPage : Screen
{
    private readonly DetectedGame _game;
    private readonly GameLink _link;
    private Control? _first;

    public ImportPage(DetectedGame game, GameLink link)
    {
        _game = game;
        _link = link;
    }

    private string? Target => GamePaths.ResolvePath(_game, _link.ImportTo!, Host.Paths.Home);

    private IEnumerable<string> Candidates()
    {
        var dirs = new[] { "Downloads", "Desktop" }.Select(d => Path.Combine(Host.Paths.Home, d)).Where(Directory.Exists);
        return dirs.SelectMany(d => Directory.EnumerateFiles(d))
            .Where(f => Archive.IsArchive(f) && Path.GetExtension(f).ToLowerInvariant() is ".zip" or ".7z" or ".rar")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(40);
    }

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        col.AddChild(UiTheme.Label($"Add {_link.Item}s from {_link.Name}", UiTheme.TitleSize));
        var target = Target;
        col.AddChild(UiTheme.Label(
            $"{_link.Name} has no way for apps to download, so get {_link.Item}s on the website (signed in), then pick the file here. Luma unpacks it where {_game.Title} finds it.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var items = new List<Control>
        {
            Add(col, new ActionButton($"Open {_link.Name}", () => ModActions.OpenUrl(_link.Url), _link.Url)),
        };
        if (target == null)
        {
            col.AddChild(UiTheme.Label(Luma.Core.Platform.IsWindows
                ? $"Luma doesn't know where {_game.Title} keeps its {_link.Item}s on Windows."
                : $"Start {_game.Title} once from Steam first: its {_link.Item}s go inside the folder Proton creates then.", UiTheme.SmallSize + 1, UiTheme.Warn, wrap: true));
        }
        else
        {
            var count = Directory.Exists(target) ? Directory.GetDirectories(target).Length : 0;
            col.AddChild(UiTheme.Label($"{count} {_link.Item}{(count == 1 ? "" : "s")} added so far.", UiTheme.SmallSize, UiTheme.Dim));
            col.AddChild(Section("Files in Downloads"));
            var list = Column(8);
            col.AddChild(Scroller(list));
            foreach (var f in Candidates())
            {
                var file = f;
                var info = new FileInfo(file);
                var row = new ListRow(info.Name, $"{ModDetailsPage.Bytes(info.Length)}  ·  {info.LastWriteTime:d MMM HH:mm}", "Add", UiTheme.AccentText, null,
                    () => Import(file, target), $"Add {info.Name}.");
                list.AddChild(row);
                items.Add(row);
            }
            if (items.Count == 1)
            {
                list.AddChild(UiTheme.Label("No .zip, .7z or .rar files in Downloads or on the Desktop yet.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
        }
        Focus.Column(items);
        _first = items.Count > 1 ? items[1] : items[0];
    }

    private void Import(string file, string target) => App.Busy($"Adding {Path.GetFileName(file)}…", (_, _) => Task.Run(() => Unpack(file, target)), name =>
    {
        App.Success($"Added \"{name}\".");
        Rebuild();
    }, cancellable: false);

    /// <summary>Unpacks into a temp folder, unwraps single wrapper folders, then moves the item in as one folder.</summary>
    private static string Unpack(string file, string target)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "luma-import-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Archive.Extract(file, tmp);
            var root = tmp;
            while (Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root) is { Length: 1 } one)
            {
                root = one[0];
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw new InvalidDataException($"{Path.GetFileName(file)} is empty.");
            }
            var name = ModStore.Safe(root == tmp ? Path.GetFileNameWithoutExtension(file) : Path.GetFileName(root));
            var dest = Path.Combine(target, name);
            Directory.CreateDirectory(target);
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest, true);
            }
            CopyDir(root, dest);
            return name;
        }
        finally
        {
            if (Directory.Exists(tmp))
            {
                Directory.Delete(tmp, true);
            }
        }
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(dest, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to, overwrite: true);
        }
    }

    private static T Add<T>(Control parent, T c) where T : Control
    {
        parent.AddChild(c);
        return c;
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
