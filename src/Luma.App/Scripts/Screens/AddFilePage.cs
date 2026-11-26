using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Installs a mod archive downloaded by hand (from a site with no app downloads, or a script
/// extender such as SKSE64): pick it from Downloads or the Desktop and it joins the load order.
/// A script extender is recognised by its loader program and installed as the game's loader.
/// </summary>
public partial class AddFilePage : Screen
{
    private readonly DetectedGame _game;
    private Control? _first;

    public AddFilePage(DetectedGame game) => _game = game;

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
        col.AddChild(UiTheme.Label("Add a mod from a file", UiTheme.TitleSize));
        var loader = Host.Mods.Loader(_game);
        var hint = loader?.Launcher != null
            ? $"Pick a .zip, .7z or .rar you downloaded. {loader.Name} goes in this way too: Luma then starts {_game.Title} through it."
            : "Pick a .zip, .7z or .rar you downloaded. Luma keeps a copy, so it can be reinstalled later.";
        col.AddChild(UiTheme.Label(hint, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        col.AddChild(Section("Files in Downloads and on the Desktop"));
        var list = Column(8);
        col.AddChild(Scroller(list));
        var items = new List<Control>();
        foreach (var f in Candidates())
        {
            var file = f;
            var info = new FileInfo(file);
            var row = new ListRow(info.Name, $"{ModDetailsPage.Bytes(info.Length)}  ·  {info.LastWriteTime:d MMM HH:mm}", "Add", UiTheme.AccentText, null,
                () => Install(file), $"Install {info.Name} for {_game.Title}.");
            list.AddChild(row);
            items.Add(row);
        }
        if (items.Count == 0)
        {
            list.AddChild(UiTheme.Label("No .zip, .7z or .rar files in Downloads or on the Desktop yet.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            var back = new ActionButton("Back", () => App.Pop());
            col.AddChild(back);
            items.Add(back);
        }
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    private void Install(string file) => App.Busy($"Installing {Path.GetFileName(file)}…", (_, _) => Task.Run(() => InstallFile(Host, _game, file)), mod =>
    {
        App.Success(mod.Site == "loader" ? $"{mod.Name} is installed. The game starts through it from now on." : $"Installed {mod.Name}.");
        App.Pop();
    }, cancellable: false);

    /// <summary>Copies the archive into Luma's downloads (for reinstall and rollback) and installs it.</summary>
    public static InstalledMod InstallFile(Luma.Host.AppHost host, DetectedGame game, string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var kept = Path.Combine(host.Paths.DownloadsDir, "local", ModStore.Safe(game.Key), Path.GetFileName(file));
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.Copy(file, kept, overwrite: true);
        return host.Mods.Install(game, kept, new InstalledMod { Id = "local:" + ModStore.Safe(name), Site = "local", Name = name });
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
