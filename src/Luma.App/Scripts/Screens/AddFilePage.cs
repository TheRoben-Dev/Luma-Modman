using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Installs a mod archive downloaded by hand (from a site with no app downloads, or a script
/// extender such as SKSE64): pick it from Downloads or the Desktop and it joins the load order.
/// A script extender is recognised by its loader program and installed as the game's loader.
/// "Browse folders" walks any folder (home, SD cards and USB drives, Windows drives) for archives,
/// like the desktop layout's file picker.
/// </summary>
public partial class AddFilePage : Screen
{
    private readonly DetectedGame _game;
    private Control? _first;

    internal DetectedGame Game => _game;

    /// <summary>The folder being browsed; null shows the recent files and the places to start from.</summary>
    private string? _dir;

    /// <summary>Pick a folder (an unpacked mod) instead of an archive; the desktop opens a folder picker for it.</summary>
    internal bool Folder { get; }

    public AddFilePage(DetectedGame game, bool folder = false)
    {
        _game = game;
        Folder = folder;
    }

    /// <summary>Why a folder can't be installed as a mod (a whole drive or home folder, or far too big), or null.</summary>
    internal static string? FolderProblem(string home, string dir)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        if (Path.GetPathRoot(full) is { } root && string.Equals(Path.TrimEndingDirectorySeparator(root), full, StringComparison.OrdinalIgnoreCase)
            || Places(home).Any(p => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.Path)), full, StringComparison.Ordinal)))
        {
            var shown = Path.GetFileName(full) is { Length: > 0 } n ? n : full;
            return $"{shown} isn't a mod: pick the folder the mod's files are in.";
        }
        try
        {
            var n = Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Take(50001).Count();
            return n == 0 ? "That folder is empty." : n > 50000 ? "That folder holds more than 50,000 files: pick the mod's own folder." : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "Luma can't read that folder: " + e.Message;
        }
    }

    /// <summary>Installs a folder (an unpacked mod), after checking it's a sensible one.</summary>
    internal static void InstallFolder(DetectedGame game, string dir, Action<InstalledMod>? done = null)
    {
        var app = Main.Instance;
        if (FolderProblem(app.Host!.Paths.Home, dir) is { } problem)
        {
            app.Error(problem);
            return;
        }
        FomodSession.InstallFile(game, Path.TrimEndingDirectorySeparator(dir), done);
    }

    /// <summary>Where browsing can start: home, Downloads, Desktop, SD cards and USB drives (Linux), drives (Windows).</summary>
    internal static List<(string Name, string Path)> Places(string home)
    {
        var list = new List<(string, string)>();
        foreach (var (name, dir) in new[] { ("Downloads", Path.Combine(home, "Downloads")), ("Desktop", Path.Combine(home, "Desktop")), ("Home folder", home) })
        {
            if (Directory.Exists(dir))
            {
                list.Add((name, dir));
            }
        }
        if (Luma.Core.Platform.IsWindows)
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
            {
                list.Add(($"Drive {d.Name.TrimEnd('\\')}" + (d.VolumeLabel.Length > 0 ? $" ({d.VolumeLabel})" : ""), d.RootDirectory.FullName));
            }
        }
        else
        {
            // SteamOS and Bazzite mount SD cards and USB drives under /run/media (per user, or directly).
            foreach (var media in new[] { "/run/media/" + System.Environment.UserName, "/run/media" })
            {
                if (!Directory.Exists(media))
                {
                    continue;
                }
                foreach (var d in SafeDirs(media).Where(d => Path.GetFileName(d) != System.Environment.UserName))
                {
                    list.Add(($"Drive {Path.GetFileName(d)}", d));
                }
            }
        }
        return list;
    }

    /// <summary>Visible subfolders, or none when the folder can't be read.</summary>
    internal static List<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir).Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    /// <summary>Mod archives in a folder, newest first, or none when it can't be read.</summary>
    internal static List<string> SafeArchives(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir).Where(Archive.IsArchive).OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

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
            : "Pick a .zip, .7z or .rar you downloaded, or a folder with an unpacked mod. Luma keeps a copy, so it can be reinstalled later.";
        col.AddChild(UiTheme.Label(hint, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var list = Column(8);
        var items = new List<Control>();
        if (_dir != null)
        {
            BuildFolder(col, list, items, _dir);
        }
        else
        {
            col.AddChild(Section("Recent files in Downloads and on the Desktop"));
            col.AddChild(Scroller(list));
            foreach (var f in Candidates())
            {
                items.Add(FileRow(list, f));
            }
            if (items.Count == 0)
            {
                list.AddChild(UiTheme.Label("No .zip, .7z or .rar files in Downloads or on the Desktop yet. Browse a folder below.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
            list.AddChild(Section("Browse a folder"));
            foreach (var (name, dir) in Places(Host.Paths.Home))
            {
                var d = dir;
                var row = new ListRow(name, Host.Paths.Short(dir), "Open", UiTheme.Dim, null, () => Open(d), $"Look for mod files in {dir}.");
                list.AddChild(row);
                items.Add(row);
            }
        }
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    private ListRow FileRow(Control list, string file)
    {
        var info = new FileInfo(file);
        var row = new ListRow(info.Name, $"{ModDetailsPage.Bytes(info.Length)}  ·  {info.LastWriteTime:d MMM HH:mm}", "Add", UiTheme.AccentText, null,
            () => Install(file), $"Install {info.Name} for {_game.Title}.");
        list.AddChild(row);
        return row;
    }

    private void BuildFolder(VBoxContainer col, VBoxContainer list, List<Control> items, string dir)
    {
        col.AddChild(Section(Host.Paths.Short(dir)));
        var up = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(dir));
        var back = new ActionButton(up != null ? "Up one folder" : "Back to the start", () => Open(up), "B goes back to where you started.");
        col.AddChild(back);
        items.Add(back);
        if (FolderProblem(Host.Paths.Home, dir) == null)
        {
            var whole = new ActionButton($"Install this folder as a mod", () =>
            {
                App.Pop();
                InstallFolder(_game, dir);
            }, $"Installs {Path.GetFileName(Path.TrimEndingDirectorySeparator(dir))} as it is: for a mod you unpacked yourself.");
            col.AddChild(whole);
            items.Add(whole);
        }
        col.AddChild(Scroller(list));
        foreach (var f in SafeArchives(dir))
        {
            items.Add(FileRow(list, f));
        }
        foreach (var d in SafeDirs(dir))
        {
            var sub = d;
            var row = new ListRow(Path.GetFileName(d) + "/", "Folder", "Open", UiTheme.Dim, null, () => Open(sub), $"Open {Path.GetFileName(d)}.");
            list.AddChild(row);
            items.Add(row);
        }
        if (items.Count <= 2 && !SafeArchives(dir).Any() && !SafeDirs(dir).Any())
        {
            list.AddChild(UiTheme.Label("No mod archives or folders here.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
    }

    private void Open(string? dir)
    {
        _dir = dir;
        Rebuild();
    }

    public override bool OnBack()
    {
        if (_dir == null)
        {
            return false;
        }
        Open(null);
        return true;
    }

    private void Install(string file)
    {
        App.Pop();
        FomodSession.InstallFile(_game, file);
    }

    /// <summary>Copies the archive into Luma's downloads (for reinstall and rollback).</summary>
    public static string Keep(Luma.Host.AppHost host, DetectedGame game, string file)
    {
        file = Path.TrimEndingDirectorySeparator(file);
        var kept = Path.Combine(host.Paths.DownloadsDir, "local", ModStore.Safe(game.Key), Path.GetFileName(file));
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(kept), StringComparison.Ordinal))
        {
            return kept;
        }
        if (Directory.Exists(file))
        {
            // An unpacked mod: a copy of the folder, so the original can be moved or deleted.
            if (Directory.Exists(kept))
            {
                Directory.Delete(kept, true);
            }
            ProfileFiles.CopyDir(file, kept);
        }
        else
        {
            File.Copy(file, kept, overwrite: true);
        }
        return kept;
    }

    /// <summary>What a mod added from a file is called in the load order.</summary>
    public static InstalledMod LocalMod(string file)
    {
        file = Path.TrimEndingDirectorySeparator(file);
        var name = Directory.Exists(file) ? Path.GetFileName(file) : Path.GetFileNameWithoutExtension(file);
        return new InstalledMod { Id = "local:" + ModStore.Safe(name), Site = "local", Name = name };
    }

    /// <summary>Copies the archive into Luma's downloads and installs it (throws <see cref="FomodNeededException"/> for installers).</summary>
    public static InstalledMod InstallFile(Luma.Host.AppHost host, DetectedGame game, string file) =>
        host.Mods.Install(game, Keep(host, game, file), LocalMod(file));

    public override void FocusDefault() => _first?.GrabFocus();
}
