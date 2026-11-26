using Luma.Host.Updates;
using Luma.App.Screens;
using Luma.App.Ui;
using Luma.Host;
using Godot;

namespace Luma.App;

/// <summary>
/// Root node: chrome (header, hint bar, toast), the screen stack and input routing.
/// All non-UI work goes through <see cref="AppHost"/>, the same code the headless CLI uses.
/// </summary>
public partial class Main : Control
{
    /// <summary>Every tab works on the game picked in the header (switched with View / Select).</summary>
    public static readonly string[] TabNames = ["Mods", "Browse", "Downloads", "Settings"];

    private static readonly Func<Screen>[] TabFactories =
    [
        () => new ModsScreen(), () => new BrowseScreen(), () => new DownloadsScreen(), () => new SettingsScreen(),
    ];

    private readonly List<Screen> _stack = new();
    private Control _content = null!;
    private Control _overlay = null!;
    private Backdrop _backdrop = null!;
    private Header _header = null!;
    private Control _bgLayer = null!;
    private VBoxContainer _root = null!;
    private FocusHighlight _highlight = null!;
    private HintBar _hints = null!;
    private Toast _toast = null!;
    private int _tab;
    private bool _appFocused = true;

    public static Main Instance { get; private set; } = null!;

    /// <summary>Null only if startup failed (shown on screen).</summary>
    public AppHost? Host { get; private set; }

    /// <summary>
    /// True while a game with mods linked in runs: the app ignores controller input so game presses
    /// don't leak in, and the animated background stops.
    /// </summary>
    public bool GameRunning
    {
        get => _gameRunning;
        set
        {
            if (_gameRunning != value)
            {
                AppLog.Info(value ? "game running" : "game ended");
            }
            _gameRunning = value;
            if (_highlight != null)
            {
                _highlight.Suspended = value;
            }
            PauseBackground();
        }
    }

    private bool _gameRunning;
    private AnimatedBackground? _animBg;
    private Scanlines? _crt;

    /// <summary>Background motion stops while a game runs or the window is in the background.</summary>
    private void PauseBackground()
    {
        var paused = _gameRunning || !_appFocused;
        if (_animBg != null && IsInstanceValid(_animBg))
        {
            _animBg.Paused = paused;
        }
        if (_crt != null && IsInstanceValid(_crt))
        {
            _crt.Paused = paused;
        }
    }

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Hidden;  // never drawn; clicks and touch still work
        Instance = this;
        Pad.Setup();
        Pad.FamilyChanged += UpdateChrome;
        AppHost? host = null;
        Exception? startupError = null;
        try
        {
            host = new AppHost();
        }
        catch (Exception ex)
        {
            startupError = ex;
        }
        if (host != null)
        {
            AppLog.Open(Path.Combine(host.Paths.Data, "logs"));
        }
        if (startupError != null)
        {
            AppLog.Error("startup: " + startupError);
        }
        var startTheme = host?.Settings.Theme ?? "dark";
        AppLog.Info($"theme {startTheme}, window {DisplayServer.WindowGetSize()}, pads: {string.Join(", ", Input.GetConnectedJoypads().Select(Input.GetJoyName))}");
        UiTheme.Use(startTheme, host?.Settings.Accents.GetValueOrDefault(startTheme));
        Anim.Enabled = !(host?.Settings.ReduceMotion ?? false);
        Theme = UiTheme.Build();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (OS.GetCmdlineUserArgs().Contains("--windowed"))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }

        _bgLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _bgLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_bgLayer);
        Host = host;
        BuildBackground();

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 40);
        }
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 16);
        margin.AddChild(root);
        _root = root;
        _header = NewHeader();
        root.AddChild(_header);
        _content = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_content);
        _hints = new HintBar();
        root.AddChild(_hints);
        // Overlay layer (cards) above header and content; the hint bar stays visible below it.
        _overlay = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _overlay.OffsetBottom = -56;
        AddChild(_overlay);
        _backdrop = new Backdrop();
        _backdrop.Clicked += () =>
        {
            if (_stack.Count > 1 && _stack[^1].IsOverlay && !GameRunning)
            {
                Pop();
            }
        };
        _overlay.AddChild(_backdrop);
        _highlight = new FocusHighlight();
        AddChild(_highlight);
        _toast = new Toast();
        AddChild(_toast);

        // Rows with an expanding explanation show it in place; the hint bar would only repeat it.
        GetViewport().GuiFocusChanged += c => _hints.SetHelp(c.HasMeta(Stepper.ExpandsMeta) ? "" : c.TooltipText);
        // A tick when the player moves focus (not when a page puts focus somewhere itself).
        GetViewport().GuiFocusChanged += _ =>
        {
            if (Time.GetTicksMsec() - _navAtMs < 250)
            {
                Feedback.Play(Cue.Move);
            }
        };

        _feedback = new Feedback(UiTheme.Name)
        {
            Volume = host?.Settings.SoundVolume ?? 50,
            Haptics = host?.Settings.Haptics ?? true,
        };
        AddChild(_feedback);
        if (startupError is { } e)
        {
            GD.PushError(e.ToString());
            var msg = UiTheme.Label($"Luma could not start:\n\n{e.Message}", wrap: true);
            msg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _content.AddChild(msg);
            return;
        }
        if (OS.GetCmdlineUserArgs().Contains("--open=appearance"))
        {
            // Back from a restart that applied a theme: Settings → Appearance, on the theme row.
            _tab = Array.IndexOf(TabNames, "Settings");
            var settings = new SettingsScreen { StartIn = "Appearance" };
            _stack.Add(settings);
            _content.AddChild(settings);
            UpdateChrome();
        }
        else
        {
            SwitchTab(0);
        }
        // The cached list shows at once; a fresh scan replaces it in the background.
        ScanGames(quiet: true);
        StartNxmInbox();

        var args = OS.GetCmdlineUserArgs();
        var smoke = Array.IndexOf(args, "--smoke");
        var sounds = Array.IndexOf(args, "--render-sounds");
        if (sounds >= 0 && sounds + 1 < args.Length)
        {
            // Development tool: every theme's sounds as .wav files, to listen to without the app.
            Feedback.WriteAll(args[sounds + 1]);
            GetTree().Quit();
        }
        else if (smoke >= 0 && smoke + 1 < args.Length)
        {
            AddChild(new SmokeTest(args[smoke + 1]));
        }
        else if (!ShowWhatsNew() && Host is { Settings.CheckForUpdates: true, Updater.CanSelfUpdate: true })
        {
            CheckForAppUpdate(silent: true);
        }
    }

    private Header NewHeader()
    {
        var h = new Header(TabNames);
        h.TabClicked += i =>
        {
            if (_stack.Count == 1 && !GameRunning)
            {
                SwitchTab(i);
            }
        };
        h.GameClicked += () =>
        {
            if (_stack.Count == 1 && !GameRunning)
            {
                OpenGamePicker();
            }
        };
        var g = Host?.ActiveGame;
        h.SetGame(g?.Title, g?.Install.Cover);
        return h;
    }

    /// <summary>Background colour, accent glow, the theme's animated background and decoration (dot grid, LCD grid), and the CRT effect.</summary>
    private void BuildBackground()
    {
        foreach (var c in _bgLayer.GetChildren())
        {
            c.QueueFree();
        }
        var bg = new ColorRect { Color = UiTheme.Bg, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _bgLayer.AddChild(bg);
        if (UiTheme.Spec.Glow > 0)
        {
            // Soft accent glow from the top-left corner so the background isn't flat.
            var glow = new TextureRect
            {
                Texture = new GradientTexture2D
                {
                    Gradient = new Gradient
                    {
                        Offsets = [0f, 1f],
                        Colors = [UiTheme.Accent with { A = UiTheme.Spec.Glow }, UiTheme.Accent with { A = 0f }],
                    },
                    Fill = GradientTexture2D.FillEnum.Radial,
                    FillFrom = new Vector2(0.1f, 0f),
                    FillTo = new Vector2(0.85f, 0.9f),
                    Width = 256,
                    Height = 256,
                },
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            glow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _bgLayer.AddChild(glow);
        }
        _animBg = null;
        _crt = null;
        if (Host?.Settings.AnimatedBackground != false)
        {
            _animBg = new AnimatedBackground(UiTheme.Spec);
            _bgLayer.AddChild(_animBg);
        }
        switch (UiTheme.Spec.Decor)
        {
            case Decor.DotGrid:
                _bgLayer.AddChild(new DotGrid());
                break;
            case Decor.PixelGrid:
                _bgLayer.AddChild(new PixelGrid());
                break;
        }
        // Scanlines sit above everything (ZIndex), from the theme or the CRT setting.
        if (UiTheme.Spec.Decor == Decor.Scanlines || Host?.Settings.Crt == true)
        {
            _crt = new Scanlines(Host?.Settings.Crt == true ? Host.Settings.CrtLevel : 0);
            _bgLayer.AddChild(_crt);
        }
        PauseBackground();
    }

    /// <summary>
    /// Used by the UI smoke test only: players change theme through Settings, which restarts the
    /// app. Switches theme in place: same tab, same page stack, same focus and scroll position.
    /// The old frame is kept as a picture on top and faded out, so the change is a quick crossfade.
    /// </summary>
    public void ApplyTheme(string name)
    {
        AppLog.Info($"theme -> {name} (stack: {StackNames()})");
        TextureRect? snap = null;
        if (Anim.Enabled)
        {
            var img = GetViewport().GetTexture().GetImage();
            snap = new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(img),
                MouseFilter = MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ZIndex = 200,
            };
            snap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        UiTheme.Use(name, Host?.Settings.Accents.GetValueOrDefault(name));
        Theme = UiTheme.Build();
        BuildBackground();
        var header = NewHeader();
        _root.AddChild(header);
        _root.MoveChild(header, _header.GetIndex());
        _header.QueueFree();
        _header = header;
        var hints = new HintBar();
        _root.AddChild(hints);
        _root.MoveChild(hints, _hints.GetIndex());
        _hints.QueueFree();
        _hints = hints;
        _toast.Restyle();
        _highlight.Restyle();
        for (var i = 0; i < _stack.Count; i++)
        {
            _stack[i].Rebuild();
            if (i < _stack.Count - 1 && _stack[i + 1].IsOverlay)
            {
                SetFocusable(_stack[i], false);
            }
        }
        UpdateChrome();
        if (GetViewport().GuiGetFocusOwner() is { } f)
        {
            _hints.SetHelp(f.HasMeta(Stepper.ExpandsMeta) ? "" : f.TooltipText);
        }
        AppLog.Info($"theme applied, focus: {Describe(GetViewport().GuiGetFocusOwner())}");
        if (snap != null)
        {
            AddChild(snap);
            var t = snap.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            t.TweenProperty(snap, "modulate:a", 0f, Anim.Slow + 0.08);
            t.TweenCallback(Callable.From(snap.QueueFree));
        }
    }

    /// <summary>Re-applies the background decoration (CRT setting changed).</summary>
    public void RefreshBackground() => BuildBackground();

    /// <summary>Shows or hides the badge dot on a root tab (e.g. Downloads while something downloads).</summary>
    public void SetTabBadge(string tab, bool on) => _header.SetBadge(Array.IndexOf(TabNames, tab), on);

    /// <summary>
    /// After an update, shows the changes since the last version the user saw, once. A fresh install
    /// just records the version. Returns true if the page was shown.
    /// </summary>
    private bool ShowWhatsNew()
    {
        if (Host == null)
        {
            return false;
        }
        var current = Host.Updater.CurrentVersion;
        var display = AppUpdater.DisplayVersion(current);
        var seen = AppUpdater.ParseVersion(Host.Settings.LastSeenVersion);
        if (Host.Settings.LastSeenVersion != display)
        {
            Host.SaveSettings(Host.Settings with { LastSeenVersion = display });
        }
        if (seen == null)
        {
            return false;
        }
        var news = Changelog.Between(Changelog.Bundled, seen, current);
        if (news.Count == 0)
        {
            return false;
        }
        Push(new ChangelogPage($"What's new in Luma {display}", news, whatsNew: true));
        return true;
    }

    /// <summary>Full release history (Settings → Updates).</summary>
    public void ShowChangelog() => Push(new ChangelogPage("Changelog", Changelog.Bundled));

    /// <summary>Asks GitHub for a newer Luma. Silent: no message when up to date or offline.</summary>
    public async void CheckForAppUpdate(bool silent)
    {
        if (Host == null)
        {
            return;
        }
        var updater = Host.Updater;
        AppRelease? release;
        try
        {
            release = await Task.Run(() => updater.CheckAsync());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!silent)
            {
                Error("Couldn't reach GitHub to check for updates.");
            }
            return;
        }
        var current = AppUpdater.DisplayVersion(updater.CurrentVersion);
        if (release == null)
        {
            if (!silent)
            {
                Success($"Luma {current} is the latest version");
            }
            return;
        }
        // A startup check never interrupts a running game or a page the user is on.
        if (silent && (GameRunning || _stack.Count != 1))
        {
            return;
        }
        var next = AppUpdater.DisplayVersion(release.Version);
        if (!updater.CanSelfUpdate)
        {
            Toast($"Luma {next} is available. Re-run the installer to update this copy.");
            return;
        }
        var whatsNew = Changelog.ItemsFromReleaseNotes(release.Notes);
        var notes = whatsNew.Count > 0 ? "\n\nWhat's new:\n" + string.Join("\n", whatsNew.Take(8).Select(i => "• " + i)) : "";
        Push(new ChoicePage($"Luma {next} is available (you have {current}).{notes}\n\nThe update takes a few seconds. Your mods, profiles and settings are not touched, and the current version is kept so you can roll back in Settings.",
            ("Update and restart", () => InstallAppUpdate(release)),
            ("Later", () => { })));
    }

    private void InstallAppUpdate(AppRelease release)
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        var updater = Host.Updater;
        Busy($"Updating to {AppUpdater.DisplayVersion(release.Version)}…", (p, ct) => updater.InstallAsync(release, p, ct), Restart);
    }

    public void RollbackApp()
    {
        if (Host == null || GameRunning)
        {
            return;
        }
        Guard(() => Restart(Host.Updater.Rollback()));
    }

    /// <summary>
    /// Restarts the app with the same arguments (plus <paramref name="extra"/>), e.g. to apply a
    /// theme. Godot starts the new instance as this one exits, so it works from source and exported.
    /// </summary>
    public void RestartApp(params string[] extra)
    {
        AppLog.Info($"restart ({string.Join(' ', extra)})");
        var user = OS.GetCmdlineUserArgs().Where(a => !a.StartsWith("--open=", StringComparison.Ordinal)).Concat(extra).ToList();
        var args = OS.GetCmdlineArgs().ToList();
        if (user.Count > 0)
        {
            args.Add("--");
            args.AddRange(user);
        }
        OS.SetRestartOnExit(true, args.ToArray());
        GetTree().Quit();
    }

    private void Restart(string exe)
    {
        try
        {
            AppUpdater.Relaunch(exe, OS.GetCmdlineUserArgs());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Error("Updated. Please start Luma again to finish.");
            return;
        }
        GetTree().Quit();
    }

    /// <summary>
    /// Quitting: release Godot objects that C# statics keep alive, while .NET is still running.
    /// Left for engine shutdown they can abort the process ("script_bindings is not empty").
    /// </summary>
    public override void _ExitTree()
    {
        try
        {
            if (Host != null && File.Exists(Host.PidFile))
            {
                File.Delete(Host.PidFile);
            }
        }
        catch (IOException)
        {
        }
        Theme = null;
        Luma.App.Ui.Poly.Ps1.ReleaseShared();
        CoverArt.ReleaseShared();
        UiTheme.ReleaseFonts();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            _appFocused = false;
            AppLog.Info("window lost focus");
            Pad.ReleaseAll();
            PauseBackground();
        }
        else if (what == NotificationApplicationFocusIn)
        {
            _appFocused = true;
            AppLog.Info("window focused");
            Input.MouseMode = Input.MouseModeEnum.Hidden;
            PauseBackground();
        }
    }

    private ulong _navAtMs;
    private Feedback? _feedback;

    /// <summary>Applies the sound and rumble settings right away (Settings → Sound & feel).</summary>
    public void ApplyFeedback(int volume, bool haptics)
    {
        if (_feedback != null)
        {
            _feedback.Volume = volume;
            _feedback.Haptics = haptics;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!GameRunning && e is InputEventAction or InputEventKey && e.IsPressed() && !e.IsEcho())
        {
            if (e.IsAction("ui_up") || e.IsAction("ui_down") || e.IsAction("ui_left") || e.IsAction("ui_right"))
            {
                _navAtMs = Time.GetTicksMsec();
            }
            else if (e.IsAction(Pad.Accept) && GetViewport().GuiGetFocusOwner() is BaseButton and not Stepper)
            {
                Feedback.Play(Cue.Confirm);
            }
        }
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                AppLog.Info($"pointer {(mb.Pressed ? "down" : "up")} at {mb.Position.Round()} over {Describe(GetViewport().GuiGetHoveredControl())}, focus {Describe(GetViewport().GuiGetFocusOwner())}");
                break;
            case InputEventJoypadButton { Pressed: true } jb when jb.ButtonIndex is JoyButton.A or JoyButton.B:
                AppLog.Info($"pad {jb.ButtonIndex} (game running {GameRunning}, app focused {_appFocused}), focus {Describe(GetViewport().GuiGetFocusOwner())}");
                break;
        }
        if (!GameRunning && TouchScroll.Handle(GetViewport(), e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        var joy = e is InputEventJoypadButton or InputEventJoypadMotion;
        if (joy && (GameRunning || !_appFocused))
        {
            // Godot receives joypad input even when unfocused; never react to presses meant for the game.
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Pad.Translate(e))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        PollNxmInbox(delta);
        if (!GameRunning && _appFocused)
        {
            TouchScroll.Process(delta);
            Pad.Process(delta);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_stack.Count == 0 || GameRunning)
        {
            return;
        }
        var top = _stack[^1];
        if (e.IsActionPressed(Pad.Back))
        {
            var handled = top.OnBack();
            if (!handled && _stack.Count > 1)
            {
                Pop();
                handled = true;
            }
            if (handled)
            {
                Feedback.Play(Cue.Back);
            }
        }
        else if (e.IsActionPressed(Pad.TabPrev) && _stack.Count == 1)
        {
            SwitchTab((_tab + TabNames.Length - 1) % TabNames.Length);
        }
        else if (e.IsActionPressed(Pad.TabNext) && _stack.Count == 1)
        {
            SwitchTab((_tab + 1) % TabNames.Length);
        }
        else if (e.IsActionPressed(Pad.Switch) && _stack.Count == 1)
        {
            OpenGamePicker();
        }
        else if (e.IsActionPressed(Pad.Secondary) || e.IsActionPressed(Pad.Tertiary))
        {
            top.OnAction(e.IsActionPressed(Pad.Secondary) ? Pad.Secondary : Pad.Tertiary);
        }
        else
        {
            return;
        }
        GetViewport().SetInputAsHandled();
    }

    // ---- navigation ----

    private string StackNames() => string.Join(" > ", _stack.Select(x => x.GetType().Name));

    private static string Describe(Control? c) =>
        c == null ? "none" : $"{c.GetType().Name} '{(c is Button b ? b.Text : c.TooltipText).Split('\n')[0]}'";

    // ---- games ----

    /// <summary>True while a background scan for installed games runs.</summary>
    public bool Scanning { get; private set; }

    public void OpenGamePicker()
    {
        if (Host != null && Top is not GamePickerPage)
        {
            Push(new GamePickerPage());
        }
    }

    /// <summary>Makes <paramref name="key"/> the game every tab works on, and goes back to its Mods tab.</summary>
    public void SetActiveGame(string key)
    {
        if (Host == null)
        {
            return;
        }
        Host.SetActiveGame(key);
        AppLog.Info($"active game -> {key}");
        GameChanged();
        while (_stack.Count > 1)
        {
            Pop();
        }
        SwitchTab(0);
        Success($"Working on {Host.ActiveGame?.Title}");
    }

    /// <summary>Looks for installed games again (all launchers) in the background, then refreshes what's on screen.</summary>
    public void ScanGames() => ScanGames(quiet: false);

    private async void ScanGames(bool quiet)
    {
        if (Host == null || Scanning)
        {
            return;
        }
        Scanning = true;
        var before = Host.Games.Games.Count;
        try
        {
            await Task.Run(() => Host.Games.Scan());
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            AppLog.Error("scan: " + e);
            if (!quiet)
            {
                Error("Couldn't finish looking for games: " + e.Message);
            }
        }
        finally
        {
            Scanning = false;
        }
        Host.Games.NotifyChanged();
        GameChanged();
        foreach (var s in _stack)
        {
            if (s is ModsScreen or BrowseScreen or GamePickerPage or SupportedGamesPage)
            {
                s.Rebuild();
            }
        }
        var count = Host.Games.Games.Count;
        if (!quiet)
        {
            Success(count == 0 ? "No supported games found" : $"Found {count} supported game{(count == 1 ? "" : "s")}");
        }
        else if (count != before)
        {
            AppLog.Info($"scan changed the list: {before} -> {count}");
        }
    }

    /// <summary>Header shows the game the tabs work on.</summary>
    private void GameChanged()
    {
        var g = Host?.ActiveGame;
        _header.SetGame(g?.Title, g?.Install.Cover);
    }

    /// <summary>The root tab shown now ("Mods", "Browse"...).</summary>
    public string CurrentTab => TabNames[_tab];

    public void SwitchTab(string name)
    {
        var i = Array.IndexOf(TabNames, name);
        if (i >= 0 && _stack.Count == 1)
        {
            SwitchTab(i);
        }
    }

    private void SwitchTab(int index)
    {
        AppLog.Info($"tab -> {TabNames[index]}");
        if (index != _tab)
        {
            Feedback.Play(Cue.Tab);
        }
        var dir = index == _tab ? 0 : (index - _tab + TabNames.Length) % TabNames.Length == 1 ? 1 : -1;
        _tab = index;
        foreach (var s in _stack)
        {
            if (dir != 0 && s.Visible && !s.IsOverlay)
            {
                SetFocusable(s, false);
                Anim.Exit(s, -dir);
            }
            else
            {
                s.QueueFree();
            }
        }
        _stack.Clear();
        var screen = TabFactories[index]();
        _stack.Add(screen);
        _content.AddChild(screen);
        if (dir != 0)
        {
            Anim.Enter(screen, dir);
        }
        else
        {
            Anim.FadeIn(screen);
        }
        UpdateChrome();
    }

    private bool OverlayOpen => _stack.Any(x => x.IsOverlay);

    public void Push(Screen screen)
    {
        AppLog.Info($"push {screen.GetType().Name} over {StackNames()}");
        var top = _stack.Count > 0 ? _stack[^1] : null;
        top?.Remember();
        if (screen.IsOverlay)
        {
            // The page behind stays visible (dimmed and blurred) but can't take focus.
            if (top != null)
            {
                SetFocusable(top, false);
            }
            _stack.Add(screen);
            _overlay.AddChild(screen);
            _backdrop.FadeTo(true);
            screen.AnimateOpen();
        }
        else
        {
            if (top != null)
            {
                top.Visible = false;
            }
            _stack.Add(screen);
            // Pages opened from a card (pickers, confirmations) sit above the backdrop too.
            (OverlayOpen ? _overlay : _content).AddChild(screen);
            Anim.Enter(screen, 1);
        }
        UpdateChrome();
    }

    public void Pop()
    {
        AppLog.Info($"pop ({StackNames()})");
        if (_stack.Count <= 1)
        {
            return;
        }
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        var prev = _stack[^1];
        if (top.IsOverlay)
        {
            top.AnimateClose(top.QueueFree);
            SetFocusable(prev, true);
            if (!OverlayOpen)
            {
                _backdrop.FadeTo(false);
            }
        }
        else
        {
            SetFocusable(top, false);
            Anim.Exit(top, 1);
            prev.Visible = true;
            Anim.Enter(prev, -1);
        }
        prev.Restore();
        UpdateChrome();
    }

    /// <summary>Turns keyboard/pad focus off (and back on) for everything in a page behind an overlay.</summary>
    private static void SetFocusable(Node root, bool on)
    {
        foreach (var n in root.GetChildren())
        {
            if (n is Control c)
            {
                if (!on && c.FocusMode != FocusModeEnum.None)
                {
                    c.SetMeta("ed_focus", (int)c.FocusMode);
                    c.FocusMode = FocusModeEnum.None;
                }
                else if (on && c.HasMeta("ed_focus"))
                {
                    c.FocusMode = (FocusModeEnum)c.GetMeta("ed_focus").AsInt32();
                    c.RemoveMeta("ed_focus");
                }
            }
            SetFocusable(n, on);
        }
    }

    /// <summary>Pops back to the given screen if it is still on the stack.</summary>
    public void PopTo(Screen screen)
    {
        while (_stack.Count > 1 && _stack[^1] != screen)
        {
            Pop();
        }
    }

    public Screen Top => _stack[^1];

    public void UpdateChrome()
    {
        _header.SetTab(_tab, _stack.Count == 1);
        if (_stack.Count == 0)
        {
            return;
        }
        var hints = new List<(string, string)>();
        hints.AddRange(_stack[^1].Hints.Where(h => h.Action != Pad.Switch || _stack.Count == 1));
        hints.Add((Pad.Accept, "Select"));
        if (_stack.Count > 1)
        {
            hints.Add((Pad.Back, "Back"));
        }
        _hints.SetHints(hints);
    }

    // ---- feedback ----

    public void Toast(string text) => _toast.Show(text);

    public void Error(string text)
    {
        AppLog.Warn("shown error: " + text);
        Feedback.Play(Cue.Error);
        _toast.Show(text, UiTheme.Bad);
    }

    public void Success(string text)
    {
        Feedback.Play(Cue.Success);
        _toast.Show(text, UiTheme.Good);
    }

    /// <summary>Runs an action and shows expected failures (I/O, bad files, network) as a toast.</summary>
    public bool Guard(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            Error(e.Message);
            return false;
        }
    }

    public static bool IsUserFacing(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
            or InvalidDataException or HttpRequestException or KeyNotFoundException or TaskCanceledException;

    public void Confirm(string message, string yes, Action onYes) => Push(new ConfirmPage(message, yes, onYes));

    /// <summary>Runs background work behind a busy page. Progress is 0..1 or negative for indeterminate.</summary>
    public async void Busy<T>(string title, Func<IProgress<double>, CancellationToken, Task<T>> work, Action<T> done, bool cancellable = true)
    {
        using var cts = new CancellationTokenSource();
        var page = new BusyPage(title, cancellable ? cts : null);
        Push(page);
        var progress = new Progress<double>(page.SetProgress);  // created on the main thread: reports marshal back
        try
        {
            var result = await Task.Run(() => work(progress, cts.Token), cts.Token);
            PopTo(page);
            Pop();
            done(result);
        }
        catch (Exception e) when (IsUserFacing(e) || e is OperationCanceledException)
        {
            PopTo(page);
            Pop();
            if (e is not OperationCanceledException)
            {
                Error(e.Message);
            }
        }
    }

    // ---- Nexus download links and the downloads badge ----

    private double _inboxClock;

    /// <summary>Writes the pid file `luma nxm` checks, and picks up links waiting from before the app started.</summary>
    private void StartNxmInbox()
    {
        if (Host == null)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Host.PidFile)!);
            File.WriteAllText(Host.PidFile, OS.GetProcessId().ToString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("pid file: " + e.Message);
        }
        Host.Downloads.Changed += _ => Callable.From(UpdateDownloadsBadge).CallDeferred();
        Host.StartAutoInstall();
        UpdateDownloadsBadge();
    }

    private void UpdateDownloadsBadge()
    {
        if (Host != null)
        {
            SetTabBadge("Downloads", Host.Downloads.Busy);
        }
    }

    /// <summary>Called every frame; checks the inbox about once a second.</summary>
    private void PollNxmInbox(double delta)
    {
        _inboxClock += delta;
        if (_inboxClock < 1 || Host == null || GameRunning)
        {
            return;
        }
        _inboxClock = 0;
        ModActions.TakeNxmLinks();
    }
}
