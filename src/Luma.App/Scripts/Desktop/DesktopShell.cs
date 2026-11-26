using Luma.App.Screens;
using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Host;
using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// The desktop layout: a traditional mod manager window. Menu bar and toolbar on top, installed
/// games on the left, Mods / Browse / Downloads / Settings in the middle, the app log below and
/// a status bar at the bottom. Pages the controller layout pushes (questions, progress, setup)
/// open here as dialog windows. It sits at the bottom of Main's screen stack, so all shared code
/// (Main.Push, Busy, Confirm, SetActiveGame...) works the same in both layouts.
/// </summary>
public partial class DesktopShell : Screen
{
    private TabContainer _tabs = null!;
    private DesktopMods _mods = null!;
    private DesktopBrowse _browse = null!;
    private DesktopDownloads _downloads = null!;
    private DesktopSettings _settings = null!;
    private ItemList _games = null!;
    private VBoxContainer _gameInfo = null!;
    private Label _status = null!;
    private Label _statusRight = null!;
    private RichTextLabel _log = null!;
    private Control _logPane = null!;
    private PanelContainer _notice = null!;
    private Label _noticeText = null!;
    private Tween? _noticeTween;
    private bool _fillingGames;
    private readonly Dictionary<string, Texture2D?> _covers = new();

    /// <summary>Settings category to open at start (after a restart that applied a theme).</summary>
    public string? StartIn { get; init; }

    public override void _EnterTree()
    {
        Host.Mods.Changed += OnModsChanged;
        Host.Downloads.Changed += OnDownloadChanged;
    }

    public override void _ExitTree()
    {
        Host.Mods.Changed -= OnModsChanged;
        Host.Downloads.Changed -= OnDownloadChanged;
    }

    private void OnModsChanged(string key) => Callable.From(() =>
    {
        if (IsInstanceValid(this) && IsInsideTree() && Host.ActiveGame?.Key == key)
        {
            RefreshGame();
        }
    }).CallDeferred();

    private void OnDownloadChanged(DownloadItem item) => Callable.From(() =>
    {
        if (IsInstanceValid(this) && IsInsideTree())
        {
            _downloads.OnChanged(item);
            RefreshStatus();
        }
    }).CallDeferred();

    protected override void Build()
    {
        var bg = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(Margin(BuildMenus(), 4, 2));
        root.AddChild(new HSeparator());
        root.AddChild(Margin(BuildToolbar(), 6, 4));
        root.AddChild(new HSeparator());

        var main = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(Margin(main, 6, 6, expand: true));
        main.AddChild(BuildGamesPane());
        var vsplit = new VSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddChild(vsplit);
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, DragToRearrangeEnabled = false };
        vsplit.AddChild(_tabs);
        _mods = new DesktopMods { Name = "Mods" };
        _browse = new DesktopBrowse { Name = "Browse" };
        _downloads = new DesktopDownloads { Name = "Downloads" };
        _settings = new DesktopSettings { Name = "Settings", StartIn = StartIn };
        foreach (var v in new Control[] { _mods, _browse, _downloads, _settings })
        {
            _tabs.AddChild(Margin(v, 8, 8, name: v.Name));
        }
        _tabs.TabChanged += i =>
        {
            AppLog.Info($"tab -> {Main.TabNames[i]}");
            if (i == 1)
            {
                _browse.Shown();
            }
            else if (i == 3)
            {
                _settings.Refresh();
            }
        };
        _logPane = BuildLogPane();
        vsplit.AddChild(_logPane);
        vsplit.SplitOffsets = [-40];

        root.AddChild(new HSeparator());
        root.AddChild(Margin(BuildStatusBar(), 8, 3));

        _notice = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Modulate = Colors.Transparent };
        _notice.SetAnchorsPreset(LayoutPreset.BottomRight);
        _notice.GrowHorizontal = GrowDirection.Begin;
        _notice.GrowVertical = GrowDirection.Begin;
        _notice.OffsetRight = -14;
        _notice.OffsetBottom = -36;
        _noticeText = DesktopTheme.Label("", wrap: true);
        _noticeText.CustomMinimumSize = new Vector2(320, 0);
        _notice.AddChild(_noticeText);
        AddChild(_notice);

        FillGames();
        RefreshGame();
        if (StartIn != null)
        {
            SelectTab("Settings");
        }
    }

    public override void FocusDefault() => _mods.FocusDefault();

    // ---- for the smoke test ----

    internal DesktopMods ModsView => _mods;
    internal DesktopBrowse BrowseView => _browse;
    internal DesktopDownloads DownloadsView => _downloads;
    internal DesktopSettings SettingsView => _settings;

    /// <summary>Clicks a game in the games list, as a user would.</summary>
    internal bool PickGame(string title)
    {
        for (var i = 0; i < _games.ItemCount; i++)
        {
            if (_games.GetItemText(i).StartsWith(title, StringComparison.Ordinal))
            {
                _games.Select(i);
                _games.EmitSignal(ItemList.SignalName.ItemSelected, i);
                return true;
            }
        }
        return false;
    }

    internal int GameCount => _games.ItemCount;

    // ---- layout helpers ----

    internal static MarginContainer Margin(Control c, int x, int y, bool expand = false, string? name = null)
    {
        var m = new MarginContainer();
        if (name != null)
        {
            m.Name = name;
        }
        m.AddThemeConstantOverride("margin_left", x);
        m.AddThemeConstantOverride("margin_right", x);
        m.AddThemeConstantOverride("margin_top", y);
        m.AddThemeConstantOverride("margin_bottom", y);
        if (expand)
        {
            m.SizeFlagsVertical = SizeFlags.ExpandFill;
        }
        m.AddChild(c);
        return m;
    }

    internal static Control Spacer() => new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };

    // ---- tabs ----

    public string CurrentTab => Main.TabNames[_tabs.CurrentTab];

    public void SelectTab(string name)
    {
        var i = Array.IndexOf(Main.TabNames, name);
        if (i >= 0)
        {
            _tabs.CurrentTab = i;
        }
    }

    public void SetBadge(string tab, bool on)
    {
        var i = Array.IndexOf(Main.TabNames, tab);
        if (i < 0 || _tabs == null)
        {
            return;
        }
        var busy = Host.Downloads.Items.Count(d => d.State is DownloadState.Queued or DownloadState.Downloading);
        _tabs.SetTabTitle(i, on && busy > 0 ? $"{tab} ({busy})" : tab);
    }

    // ---- games pane ----

    private Control BuildGamesPane()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(270, 0) };
        col.AddThemeConstantOverride("separation", 6);
        var head = new HBoxContainer();
        head.AddChild(DesktopTheme.Label("GAMES", DesktopTheme.Dim, DesktopTheme.SmallSize));
        head.AddChild(Spacer());
        head.AddChild(DesktopTheme.Button("Scan", App.ScanGames, "Look for installed games again (all launchers).", "ToolButton"));
        col.AddChild(head);
        _games = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FixedIconSize = new Vector2I(24, 36),
            IconMode = ItemList.IconModeEnum.Left,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = false,
            TooltipText = "Installed games",
            SameColumnWidth = true,
        };
        _games.ItemSelected += i =>
        {
            if (!_fillingGames && _games.GetItemMetadata((int)i).AsString() is { Length: > 0 } key && key != Host.ActiveGame?.Key)
            {
                App.SetActiveGame(key);
            }
        };
        col.AddChild(_games);
        var info = new PanelContainer();
        info.AddThemeStyleboxOverride("panel", DesktopTheme.Frame(10));
        _gameInfo = new VBoxContainer();
        _gameInfo.AddThemeConstantOverride("separation", 4);
        info.AddChild(_gameInfo);
        col.AddChild(info);
        return col;
    }

    /// <summary>A small cover for the games list; a plain tile with the game's colour when there's no art.</summary>
    private Texture2D? Cover(string? path, string title)
    {
        if (path == null || !File.Exists(path))
        {
            var img = Image.CreateEmpty(48, 72, false, Image.Format.Rgba8);
            img.Fill(UiTheme.TileColor(title));
            return ImageTexture.CreateFromImage(img);
        }
        if (_covers.TryGetValue(path, out var t))
        {
            return t;
        }
        Texture2D? tex = null;
        if (File.Exists(path))
        {
            var img = new Image();
            if (img.Load(path) == Error.Ok)
            {
                img.Resize(48, 72, Image.Interpolation.Lanczos);
                tex = ImageTexture.CreateFromImage(img);
            }
        }
        _covers[path] = tex;
        return tex;
    }

    private void FillGames()
    {
        _fillingGames = true;
        _games.Clear();
        var active = Host.ActiveGame?.Key;
        foreach (var g in Host.Games.Games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase))
        {
            var i = _games.AddItem(g.Title, Cover(g.Install.Cover, g.Title));
            _games.SetItemMetadata(i, g.Key);
            var (status, _) = GameText.Status(g, Host.Games.Catalog);
            _games.SetItemTooltip(i, $"{g.Title}\n{GameText.Source(g)}\n{status}");
            if (!g.Profile.Moddable)
            {
                _games.SetItemCustomFgColor(i, DesktopTheme.Dim);
            }
            if (g.Key == active)
            {
                _games.Select(i);
            }
        }
        if (_games.ItemCount == 0)
        {
            var i = _games.AddItem(App.Scanning ? "Looking for games…" : "No supported games found", null, false);
            _games.SetItemMetadata(i, "");
            _games.SetItemDisabled(i, true);
        }
        _fillingGames = false;
    }

    private void FillGameInfo(DetectedGame? game)
    {
        foreach (var c in _gameInfo.GetChildren())
        {
            c.QueueFree();
        }
        if (game == null)
        {
            _gameInfo.AddChild(DesktopTheme.Label($"Luma supports {Host.Games.Catalog.Games.Count} games and looked in:", DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true));
            foreach (var l in GameLibrary.Locations)
            {
                _gameInfo.AddChild(DesktopTheme.Label("•  " + l, DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true));
            }
            _gameInfo.AddChild(DesktopTheme.Button("Supported games…", () => App.Push(new SupportedGamesPage()), "Every game Luma can mod, and the ones it can't."));
            return;
        }
        _gameInfo.AddChild(DesktopTheme.Label(game.Title, null, DesktopTheme.FontSize + 1, wrap: true));
        var (status, color) = GameText.Status(game, Host.Games.Catalog);
        _gameInfo.AddChild(DesktopTheme.Label($"{GameText.Source(game)}  ·  {status}", Tint(color), DesktopTheme.SmallSize, wrap: true));
        var p = game.Profile;
        if (!p.Moddable)
        {
            Fact("Runs", GameText.Runs(game));
            return;
        }
        Fact("Loader", GameText.Loader(p, Host.Games.Catalog));
        Fact("Mods go to", p.ModsPathHere.Length == 0 ? "The game folder" : p.ModsPathHere.Replace("{documents}", "Documents").Replace("{appdata}", "AppData/Roaming")
            .Replace("{localappdata}", "AppData/Local").Replace("{mygames}", "Documents/My Games").Replace("{xdgdata}", "~/.local/share").Replace("{xdgconfig}", "~/.config"));
        Fact("Runs", GameText.Runs(game));
        Fact("Mod sites", string.Join(", ", p.Sites.Names()));
        foreach (var q in p.Quirks)
        {
            _gameInfo.AddChild(DesktopTheme.Label("•  " + q, DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true));
        }
    }

    private void Fact(string label, string value)
    {
        var row = new HBoxContainer();
        var l = DesktopTheme.Label(label, DesktopTheme.Dim, DesktopTheme.SmallSize);
        l.CustomMinimumSize = new Vector2(78, 0);
        row.AddChild(l);
        var v = DesktopTheme.Label(value, null, DesktopTheme.SmallSize, wrap: true);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(v);
        _gameInfo.AddChild(row);
    }

    /// <summary>Maps the controller theme's status colours onto the desktop palette.</summary>
    internal static Color Tint(Color c) =>
        c == UiTheme.Good ? DesktopTheme.Good : c == UiTheme.Warn ? DesktopTheme.Warn : c == UiTheme.Bad ? DesktopTheme.Bad
        : c == UiTheme.Dim ? DesktopTheme.Dim : c == UiTheme.AccentText || c == UiTheme.Accent ? DesktopTheme.Accent : DesktopTheme.Text;

    /// <summary>A scan finished: the list of games may have changed.</summary>
    public void GamesScanned()
    {
        FillGames();
        RefreshGame();
    }

    /// <summary>The active game changed (or its mods did): refresh everything that shows it.</summary>
    public void RefreshGame()
    {
        var game = Host.ActiveGame;
        _fillingGames = true;
        for (var i = 0; i < _games.ItemCount; i++)
        {
            if (_games.GetItemMetadata(i).AsString() == game?.Key)
            {
                _games.Select(i);
            }
        }
        _fillingGames = false;
        FillGameInfo(game);
        RefreshToolbar();
        _mods.Refresh();
        _browse.GameChanged();
        RefreshStatus();
    }

    // ---- status bar, notices and the log ----

    private Control BuildStatusBar()
    {
        var row = new HBoxContainer();
        _status = DesktopTheme.Label("Ready", DesktopTheme.Dim, DesktopTheme.SmallSize);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.ClipText = true;
        row.AddChild(_status);
        _statusRight = DesktopTheme.Label("", DesktopTheme.Dim, DesktopTheme.SmallSize);
        row.AddChild(_statusRight);
        return row;
    }

    public void RefreshStatus()
    {
        if (_statusRight == null)
        {
            return;
        }
        var parts = new List<string>();
        if (Host.ActiveGame is { Profile.Moddable: true } game)
        {
            var state = Host.Mods.Store(game).State;
            parts.Add($"{game.Title}");
            parts.Add($"profile {state.Active}");
            parts.Add($"{state.Enabled().Count()} of {state.Mods.Count} mods on");
            parts.Add(Host.Mods.IsDeployed(game) ? "mods are in the game" : "game folder untouched");
        }
        var busy = Host.Downloads.Items.Count(d => d.State is DownloadState.Queued or DownloadState.Downloading);
        parts.Add(busy > 0 ? $"{busy} downloading" : "no downloads running");
        _statusRight.Text = string.Join("   |   ", parts);
    }

    /// <summary>Main's Toast / Success / Error in this layout: the status bar, a short notice and the log.</summary>
    public void Notify(string text, Color color)
    {
        if (_status == null)
        {
            return;
        }
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
        _noticeText.Text = text;
        _notice.AddThemeStyleboxOverride("panel", NoticeBox(color));
        _noticeTween?.Kill();
        _notice.Modulate = Colors.White;
        _noticeTween = _notice.CreateTween();
        _noticeTween.TweenInterval(color == DesktopTheme.Bad ? 6 : 3.5);
        _noticeTween.TweenProperty(_notice, "modulate:a", 0f, 0.4);
    }

    private static StyleBoxFlat NoticeBox(Color color)
    {
        var b = DesktopTheme.Frame(10);
        b.BgColor = DesktopTheme.Header;
        b.BorderWidthLeft = 4;
        b.BorderColor = color == DesktopTheme.Text ? DesktopTheme.Accent : color;
        b.ShadowColor = new Color(0, 0, 0, 0.35f);
        b.ShadowSize = 8;
        return b;
    }

    private long _logOffset;
    private double _logClock;

    private Control BuildLogPane()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(0, 120) };
        col.AddThemeConstantOverride("separation", 2);
        var head = new HBoxContainer();
        head.AddChild(DesktopTheme.Label("LOG", DesktopTheme.Dim, DesktopTheme.SmallSize));
        head.AddChild(Spacer());
        head.AddChild(DesktopTheme.Button("Open log file", OpenLogFile, AppLog.FilePath ?? "", "ToolButton"));
        head.AddChild(DesktopTheme.Button("Clear", () => _log.Clear(), "Clears this view; the log file keeps everything.", "ToolButton"));
        col.AddChild(head);
        var frame = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", DesktopTheme.Frame(6));
        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.Click,
            TooltipText = "Log lines",
        };
        _log.AddThemeFontOverride("normal_font", DesktopTheme.Mono);
        _log.AddThemeFontSizeOverride("normal_font_size", DesktopTheme.SmallSize);
        frame.AddChild(_log);
        col.AddChild(frame);
        ReadLog(initial: true);
        return col;
    }

    internal static void OpenLogFile()
    {
        if (AppLog.FilePath is { } p)
        {
            OS.ShellOpen(p);
        }
    }

    /// <summary>LogPage in this layout: the log pane is always there; this makes sure it is big enough to read.</summary>
    public void ShowLog()
    {
        if (_logPane.GetParent() is VSplitContainer split && _logPane.Size.Y < 200)
        {
            split.SplitOffsets = [(int)(split.Size.Y * 0.45f)];
        }
        _log.GrabFocus();
    }

    public override void _Process(double delta)
    {
        _logClock += delta;
        if (_logClock >= 0.5)
        {
            _logClock = 0;
            ReadLog(initial: false);
        }
    }

    private void ReadLog(bool initial)
    {
        if (AppLog.FilePath is not { } path || !File.Exists(path))
        {
            return;
        }
        try
        {
            using var s = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            if (s.Length < _logOffset)
            {
                _logOffset = 0;
            }
            if (initial)
            {
                _logOffset = Math.Max(0, s.Length - 16_000);
            }
            if (s.Length == _logOffset)
            {
                return;
            }
            s.Seek(_logOffset, SeekOrigin.Begin);
            using var r = new StreamReader(s);
            var text = r.ReadToEnd();
            var end = text.LastIndexOf('\n');
            if (end < 0)
            {
                return;
            }
            _logOffset += System.Text.Encoding.UTF8.GetByteCount(text[..(end + 1)]);
            var lines = text[..end].Split('\n');
            foreach (var l in initial ? lines.Skip(1) : lines)
            {
                var color = l.Contains(" ERROR ") ? DesktopTheme.Bad : l.Contains(" WARN ") ? DesktopTheme.Warn : DesktopTheme.Dim;
                _log.PushColor(color);
                _log.AddText(l.TrimEnd('\r'));
                _log.Pop();
                _log.Newline();
            }
        }
        catch (IOException)
        {
        }
    }

    // ---- shortcuts ----

    public override bool OnShortcut(InputEventKey key)
    {
        if (key.Echo && key.Keycode is not (Key.Up or Key.Down))
        {
            return false;
        }
        switch (key.Keycode)
        {
            case Key.F:
                SelectTab("Browse");
                _browse.FocusSearch();
                return true;
            case Key.Up or Key.Down when CurrentTab == "Mods":
                return _mods.MoveSelected(key.Keycode == Key.Up ? -1 : +1);
            case Key.O:
                AddModFromFile();
                return true;
            case Key.Q:
                GetTree().Quit();
                return true;
            case Key.I:
                App.Push(new ModPackPage(Host.ActiveGame, export: false));
                return true;
            case Key.E:
                ExportModpack();
                return true;
            case Key.R:
                App.ScanGames();
                return true;
            case Key.U:
                if (Host.ActiveGame is { Profile.Moddable: true } g)
                {
                    ModActions.CheckUpdates(g);
                }
                return true;
        }
        return false;
    }

    internal void ExportModpack()
    {
        if (Host.ActiveGame is { Profile.Moddable: true } game && Host.Mods.Store(game).State.Order.Count > 0)
        {
            App.Push(new ModPackPage(game, export: true));
        }
        else
        {
            App.Error("Install some mods for this game first: a modpack holds a profile's mods.");
        }
    }

    internal void AddModFromFile()
    {
        if (Host.ActiveGame is { Profile.Moddable: true } game)
        {
            App.Push(new AddFilePage(game));
        }
        else
        {
            App.Error("Pick a game that can be modded first.");
        }
    }
}
