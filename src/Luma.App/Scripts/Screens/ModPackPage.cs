using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Modpacks in the controller layout: export the active profile (a download list, or full with
/// the mods that may be passed on inside), or pick a .lumapack from Downloads, the Desktop or the
/// modpacks folder to import. The desktop layout shows the same through file dialogs; both run
/// <see cref="PackActions"/>.
/// </summary>
public partial class ModPackPage : Screen
{
    private readonly DetectedGame? _game;
    private Control? _first;
    private bool _full;

    internal DetectedGame? Game => _game;
    internal bool Export { get; }

    public ModPackPage(DetectedGame? game, bool export)
    {
        _game = game;
        Export = export;
    }

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        if (Export)
        {
            BuildExport(col);
        }
        else
        {
            BuildImport(col);
        }
    }

    private void BuildExport(VBoxContainer col)
    {
        var game = _game!;
        var state = Host.Mods.Store(game).State;
        col.AddChild(UiTheme.Label($"Export {state.Active} as a modpack", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label(
            $"A .lumapack file with {game.Title}'s mods in this profile's order. It imports on Linux, SteamOS and Windows. It's saved to {PackActions.Folder.Replace(Host.Paths.Home, "~")}.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var items = new List<Control>();
        var list = Column(6);
        var kind = new Stepper("Contents", [("list", "Download list"), ("full", "Full (mods inside)")], _full ? "full" : "list",
            "A download list is small: everyone downloads each mod from its site. Full puts the mods that may be passed on inside the file.");
        kind.Changed += v =>
        {
            _full = v == "full";
            Fill(list);
        };
        col.AddChild(kind);
        items.Add(kind);
        var go = new ActionButton("Export", () => PackActions.Export(game, _full, null), "Saves the modpack file.");
        col.AddChild(go);
        items.Add(go);
        col.AddChild(Section("Mods in the pack"));
        col.AddChild(Scroller(list));
        Fill(list);
        Focus.Column(items);
        _first = kind;
    }

    private void Fill(VBoxContainer list)
    {
        foreach (var c in list.GetChildren())
        {
            c.QueueFree();
        }
        foreach (var (mod, enabled, bundled) in PackActions.Contents(_game!, _full))
        {
            var row = new HBoxContainer();
            var name = UiTheme.Label(mod.Name + (enabled ? "" : "  (off)"), UiTheme.FontSize, enabled ? null : UiTheme.Dim);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(UiTheme.Label(bundled ? "Inside the pack" : _full ? "Download (site's rules)" : "Download", UiTheme.SmallSize + 1, bundled ? UiTheme.Good : UiTheme.Dim));
            list.AddChild(row);
        }
    }

    private void BuildImport(VBoxContainer col)
    {
        col.AddChild(UiTheme.Label("Import a modpack", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label("Pick a .lumapack. Its mods go into a new profile of the game it's for: mods inside it install straight away, the others download from their sites.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        col.AddChild(Section("Modpacks in Downloads, on the Desktop and in Luma Modpacks"));
        var list = Column(8);
        col.AddChild(Scroller(list));
        var items = new List<Control>();
        foreach (var f in PackActions.Candidates())
        {
            var file = f;
            var info = new FileInfo(file);
            var row = new ListRow(info.Name, $"{ModDetailsPage.Bytes(info.Length)}  ·  {info.LastWriteTime:d MMM HH:mm}", "Import", UiTheme.AccentText, null,
                () => PackActions.Import(file, () => App.Close(this)), $"Import {info.Name}.");
            list.AddChild(row);
            items.Add(row);
        }
        if (items.Count == 0)
        {
            list.AddChild(UiTheme.Label("No .lumapack files there yet. Copy one into Downloads or onto the Desktop.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            var back = new ActionButton("Back", () => App.Pop());
            col.AddChild(back);
            items.Add(back);
        }
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}

/// <summary>Modpack export and import, shared by both layouts.</summary>
public static class PackActions
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;

    /// <summary>Where exported packs go: Documents/Luma Modpacks (the home folder's, without a Documents folder).</summary>
    public static string Folder
    {
        get
        {
            // On Linux .NET's "MyDocuments" is the home folder itself.
            var docs = Luma.Core.Platform.IsWindows
                ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments)
                : Path.Combine(Host.Paths.Home, "Documents");
            return Path.Combine(docs.Length > 0 && Directory.Exists(docs) ? docs : Host.Paths.Home, "Luma Modpacks");
        }
    }

    /// <summary>The active profile's mods, in order, and whether each would go inside a full pack.</summary>
    public static IEnumerable<(InstalledMod Mod, bool Enabled, bool Bundled)> Contents(DetectedGame game, bool full)
    {
        var state = Host.Mods.Store(game).State;
        foreach (var e in state.Order)
        {
            if (state.Find(e.Id) is { } m)
            {
                yield return (m, e.Enabled, full && ModPack.CanBundle(m) && m.Archive != null && File.Exists(m.Archive));
            }
        }
    }

    public static string DefaultFile(DetectedGame game, bool full) =>
        Path.Combine(Folder, ModPack.FileName(game.Title, Host.Mods.Store(game).State.Active, full));

    /// <summary>.lumapack files in Downloads, on the Desktop and in the modpacks folder, newest first.</summary>
    public static IEnumerable<string> Candidates() =>
        new[] { Path.Combine(Host.Paths.Home, "Downloads"), Path.Combine(Host.Paths.Home, "Desktop"), Folder }
            .Where(Directory.Exists).Distinct()
            .SelectMany(d => Directory.EnumerateFiles(d, "*" + ModPack.Extension))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(40);

    /// <summary>Writes the pack (to <paramref name="file"/>, or the default name in <see cref="Folder"/>).</summary>
    public static void Export(DetectedGame game, bool full, string? file, Action? done = null)
    {
        var state = Host.Mods.Store(game).State;
        if (state.Order.Count == 0)
        {
            App.Error($"The {state.Active} profile has no mods to export.");
            return;
        }
        var path = file ?? DefaultFile(game, full);
        App.Busy("Writing the modpack…", (_, _) => Task.Run(() => PackImporter.Export(Host, game, state.Active, full, path)), r =>
        {
            done?.Invoke();
            var where = Path.GetFileName(r.File);
            var msg = full
                ? $"Saved {where}: {r.Bundled} mod{(r.Bundled == 1 ? "" : "s")} inside, {r.Listed} to download."
                : $"Saved {where}: a list of {r.Listed} mod{(r.Listed == 1 ? "" : "s")} to download.";
            if (full && r.NotBundled.Count > 0)
            {
                msg += $"\n\nNot put inside, because their sites or authors don't allow passing files on: {string.Join(", ", r.NotBundled.Take(6))}{(r.NotBundled.Count > 6 ? "…" : "")}. Whoever imports the pack downloads those from the site.";
            }
            AppLog.Info($"exported modpack {r.File} ({r.Bundled} bundled, {r.Listed} listed)");
            App.Push(new ChoicePage(msg, ("Open the folder", () => OS.ShellOpen(Path.GetDirectoryName(r.File)!)), ("Done", () => { })));
        }, cancellable: false);
    }

    /// <summary>Imports a pack for the game it names (which has to be installed here).</summary>
    public static void Import(string file, Action? done = null)
    {
        PackManifest pack;
        try
        {
            pack = ModPack.Read(file);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            App.Error(e.Message);
            return;
        }
        var game = Host.Games.Games.FirstOrDefault(g => g.Profile.Id == pack.Game && g == Host.ActiveGame)
            ?? Host.Games.Games.FirstOrDefault(g => g.Profile.Id == pack.Game);
        if (game == null)
        {
            App.Error($"This modpack is for {(pack.GameName.Length > 0 ? pack.GameName : pack.Game)}, which Luma hasn't found on this device.");
            return;
        }
        var bundled = pack.Mods.Count(m => m.File != null);
        App.Confirm($"Import \"{pack.Name}\" for {game.Title}? Its {pack.Mods.Count} mods go into a new profile{(bundled > 0 ? $"; {bundled} are inside the pack" : "")}, and the others download from their sites. Your other profiles don't change.",
            "Import", () => App.Busy($"Importing {pack.Name}…", (progress, _) => Task.Run(() =>
                PackImporter.Import(Host, game, file, null, (_, p) => progress.Report(p))), r =>
            {
                done?.Invoke();
                if (Host.ActiveGame != game)
                {
                    App.SetActiveGame(game.Key);
                }
                Host.Mods.NotifyChanged(game);
                ShowResult(r);
            }, cancellable: false));
    }

    /// <summary>What the import did, with a way to each mod the player has to fetch.</summary>
    private static void ShowResult(PackImportResult r)
    {
        var msg = $"\"{r.Profile}\" is your active profile now: {r.Summary}.";
        var manual = r.Mods.Where(m => m.Outcome is PackModOutcome.Manual or PackModOutcome.Failed).ToList();
        var differ = r.Mods.Where(m => m.Outcome == PackModOutcome.AlreadyInstalled && m.Note.Length > 0).ToList();
        if (differ.Count > 0)
        {
            msg += $"\n\nAlready installed in another version (yours is kept): {string.Join(", ", differ.Take(4).Select(m => $"{m.Mod.Name} ({m.Note})"))}{(differ.Count > 4 ? "…" : "")}.";
        }
        if (manual.Count > 0)
        {
            msg += "\n\nTo get by hand (they take their place in the order when they arrive):\n" +
                string.Join("\n", manual.Take(6).Select(m => $"• {m.Mod.Name}: {m.Note}")) + (manual.Count > 6 ? $"\n…and {manual.Count - 6} more." : "");
        }
        var options = manual.Where(m => m.Mod.PageUrl != null).Take(5)
            .Select(m => ($"Open {m.Mod.Name}'s page", (Action)(() => ModActions.OpenUrl(m.Mod.PageUrl))))
            .Append(("Done", () => { }))
            .ToArray();
        App.Push(new ChoicePage(msg, options));
    }
}
