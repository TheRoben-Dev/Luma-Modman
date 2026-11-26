using Luma.App.Ui;
using Godot;

namespace Luma.App;

/// <summary>
/// `Luma -- --smoke &lt;dir&gt;`: drives the UI with scripted input, saves a screenshot after
/// each step and quits. Used by CI (under Xvfb) to catch startup and navigation regressions,
/// and to publish screenshots of each build. Exit code 1 if any step throws.
/// </summary>
public partial class SmokeTest : Node
{
    private readonly string _outDir;
    private readonly Queue<(string Name, Action Act, double Wait, string? Expect, string? Tab)> _steps = new();
    private double _wait = 1.0;
    private int _shot;
    private int _failures;

    public SmokeTest(string outDir)
    {
        _outDir = outDir;
        Directory.CreateDirectory(outDir);
        // The animated background costs a lot under software rendering (CI); it would skew the
        // hold-to-repeat timing. It is switched on for the theme screenshots further down.
        // The first scan runs in the background; the run script sets up a fake Steam library.
        Step("mods-after-scan", () => SetAnimatedBackground(false), wait: 2.5, expect: "Browse mods", tab: "Mods");
        // Real joypad and keyboard events (not actions), through Pad's translation layer.
        Step("joy-dpad-down", () => { Joy(JoyButton.DpadDown, true); Joy(JoyButton.DpadDown, false); }, expect: "BepInEx 5");
        Step("joy-dpad-up", () => { Joy(JoyButton.DpadUp, true); Joy(JoyButton.DpadUp, false); }, expect: "Browse mods");
        // View / Select opens the game switcher, focused on the game being worked on.
        Step("joy-view-switcher", () => Tap(JoyButton.Back), expect: "Lethal Company");
        Step("switcher-down", () => Joy(JoyButton.DpadDown, true), wait: 0.25, expect: "Marvel Rivals");
        Step("switcher-release", () => Joy(JoyButton.DpadDown, false), expect: "Marvel Rivals");
        Step("switcher-stick", () => Stick(0f, 1f), wait: 0.25, expect: "Stardew Valley");
        Step("switcher-stick-center", () => Stick(0f, 0f), expect: "Stardew Valley");
        // Holding a direction auto-repeats after a short delay and stops at the end of the list.
        Step("switcher-hold-down", () => Joy(JoyButton.DpadDown, true), wait: 1.2, expect: "Supported games");
        Step("switcher-hold-release", () => Joy(JoyButton.DpadDown, false), expect: "Supported games");
        Step("supported-games", () => Tap(JoyButton.A), expect: "Lethal Company");
        Step("supported-scroll", () =>
        {
            for (var i = 0; i < 6; i++)
            {
                Press("ui_down");
            }
        });
        Step("supported-back", () => Tap(JoyButton.B), expect: "Supported games");
        Step("pick-skyrim", () =>
        {
            FindFocusable(GetTree().Root, "The Elder Scrolls V")?.GrabFocus();
            Tap(JoyButton.A);
        }, wait: 1.2, expect: "Browse mods", tab: "Mods");
        // A game with anti-cheat shows why it can't be modded instead of mod actions.
        Step("pick-not-moddable", () =>
        {
            Key(Godot.Key.G);
            FindFocusable(GetTree().Root, "Marvel Rivals")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 1.2, expect: "Switch game", tab: "Mods");
        // A finger tap on a row in the switcher picks that game.
        Step("switch-button", () => Press(Pad.Accept), expect: "Marvel Rivals");
        Step("touch-pick", () => TouchTap(FindFocusable(GetTree().Root, "The Elder Scrolls V")), wait: 1.4, expect: "Browse mods", tab: "Mods");
        // Bumpers switch tabs; Browse lists the sites that carry the game.
        Step("joy-rb-browse", () => Tap(JoyButton.RightShoulder), expect: "Nexus Mods", tab: "Browse");
        // Nexus search answers with an error from the stand-in site: the page says so and keeps the search box.
        Step("joy-a-site", () => Tap(JoyButton.A), wait: 1.5, expect: "Search");
        Step("site-back", () => Tap(JoyButton.B), expect: "Nexus Mods", tab: "Browse");
        Step("joy-rb-downloads", () => Tap(JoyButton.RightShoulder), expect: "Browse mods", tab: "Downloads");
        Step("joy-rb-settings", () => Tap(JoyButton.RightShoulder), expect: "Games", tab: "Settings");
        Step("key-tab-wraps", () => Key(Godot.Key.Tab), expect: "Browse mods", tab: "Mods");
        Step("key-shift-tab", () => Key(Godot.Key.Tab, shift: true), expect: "Games", tab: "Settings");
        Step("joy-lb-downloads", () => Tap(JoyButton.LeftShoulder), expect: "Browse mods", tab: "Downloads");
        // A on an empty tab's button jumps to that tab.
        Step("downloads-browse", () => Press(Pad.Accept), expect: "Nexus Mods", tab: "Browse");
        Step("settings", () =>
        {
            Tap(JoyButton.RightShoulder);
            Tap(JoyButton.RightShoulder);
        }, expect: "Games", tab: "Settings");
        Step("games-pane", () => Press(Pad.Accept), expect: "Scan again");
        Step("games-rescan", () => Press(Pad.Accept), wait: 1.5, expect: "Scan again");
        Step("games-back", () => Press(Pad.Back), expect: "Games");
        Step("settings-appearance", () => Press("ui_down"), expect: "Appearance");
        Step("settings-sound", () => Press("ui_down"), expect: "Sound & feel");
        Step("sound-pane", () => Press(Pad.Accept), expect: "Short sounds when you move");
        // Inside a pane, left stays in the pane; only B goes back to the categories.
        Step("sound-level", () => Press("ui_right"), expect: "Short sounds when you move");
        Step("sound-back", () => Press(Pad.Back), expect: "Sound & feel");
        // Finger drag over the options pane scrolls it and must not press the row it started on.
        Step("touch-drag-scroll", () =>
        {
            Mouse(new Vector2(800, 600), true);
            for (var y = 580; y >= 260; y -= 40)
            {
                Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(800, y), GlobalPosition = new Vector2(800, y), ButtonMask = MouseButtonMask.Left, Relative = new Vector2(0, -40) });
            }
            Mouse(new Vector2(800, 260), false);
        }, expect: "Sound & feel");
        Step("about", () => FindFocusable(GetTree().Root, "Where Luma keeps its files")?.GrabFocus(), expect: "About");
        Step("about-pane", () => Press(Pad.Accept), expect: "Open the app log");
        Step("log-page", () => Press(Pad.Accept), expect: "Log lines");
        Step("log-scroll", () =>
        {
            for (var i = 0; i < 3; i++)
            {
                Press("ui_down");
            }
        });
        Step("log-back", () => Press(Pad.Back), expect: "Open the app log");
        Step("about-back", () => Press(Pad.Back), expect: "About");
        Step("appearance", () => FindFocusable(GetTree().Root, "Theme, accent colour")?.GrabFocus(), expect: "Appearance");
        Step("theme-focus", () =>
        {
            SetAnimatedBackground(true);
            FindFocusable(GetTree().Root, "Pick a theme, then choose Restart")?.GrabFocus();
        }, expect: "Pick a theme, then choose Restart");
        for (var i = 1; i <= UiTheme.Themes.Count; i++)
        {
            Step($"theme-{i}", () => Press("ui_right"), wait: 0.8, expect: "Pick a theme, then choose Restart");
        }
        // Themes apply by restarting: picking another one offers the restart, picking the current one doesn't.
        Step("theme-pick", () => Press("ui_right"), wait: 0.5, expect: "Pick a theme, then choose Restart");
        Step("theme-restart-offered", () => FindFocusable(GetTree().Root, "Saves the theme and restarts")?.GrabFocus(), expect: "Restart to apply");
        Step("theme-unpick", () =>
        {
            FindFocusable(GetTree().Root, "Pick a theme, then choose Restart")?.GrabFocus();
            Press("ui_left");
        }, wait: 0.5, expect: "Pick a theme, then choose Restart");
        // Accent colour: pick one, then back to the theme's own.
        Step("accent-focus", () => FindFocusable(GetTree().Root, "The highlight colour for this theme")?.GrabFocus(), expect: "The highlight colour for this theme");
        Step("accent-pick", () => Press("ui_right"), wait: 0.8, expect: "The highlight colour for this theme");
        Step("accent-reset", () => Press("ui_left"), wait: 0.8, expect: "The highlight colour for this theme");
        Step("crt-strong", () =>
        {
            FindFocusable(GetTree().Root, "Makes the whole screen")?.GrabFocus();
            for (var i = 0; i < 3; i++)
            {
                Press("ui_right");
            }
        }, expect: "Makes the whole screen");
        Step("crt-off", () => Press("ui_right"), expect: "Makes the whole screen");
        // Every theme, live: Browse tiles and the Settings panes, by pad and by touch.
        foreach (var t in UiTheme.Themes)
        {
            var id = t.Id;
            Step($"theme-{id}-mods", () =>
            {
                SetTheme(id);
                Tap(JoyButton.RightShoulder);  // Settings -> Mods (wraps)
            }, wait: 1.2, expect: "Browse mods", tab: "Mods");
            Step($"theme-{id}-switcher", () => Tap(JoyButton.Back), wait: 1.0, expect: "The Elder Scrolls V");
            Step($"theme-{id}-browse", () =>
            {
                Tap(JoyButton.B);
                Tap(JoyButton.RightShoulder);  // -> Browse
            }, wait: 1.0, expect: "Nexus Mods", tab: "Browse");
            Step($"theme-{id}-settings", () =>
            {
                Tap(JoyButton.LeftShoulder);  // -> Mods
                Tap(JoyButton.LeftShoulder);  // -> Settings (wraps)
            }, wait: 1.0, tab: "Settings");
        }
        Step("theme-restore", () => SetTheme("dark"), wait: 1.0);
        Step("changelog", () =>
        {
            FindFocusable(GetTree().Root, "Version, changelog")?.GrabFocus();
            Press(Pad.Accept);  // into Updates: first row is Changelog
            Press(Pad.Accept);
        });
        Step("changelog-back", () => Press(Pad.Back), expect: "Changelog");
        // Thunderstore through the stand-in site: results with icons, search with the on-screen
        // keyboard (typed with a real keyboard here), a mod's page and a download.
        Step("pick-lethal", () =>
        {
            Key(Godot.Key.G);
            FindFocusable(GetTree().Root, "Lethal Company")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 1.2, expect: "Browse mods", tab: "Mods");
        Step("ts-sites", () => Tap(JoyButton.RightShoulder), expect: "Thunderstore", tab: "Browse");
        Step("ts-results", () => Tap(JoyButton.A), wait: 2.0, expect: "A stable lobby");
        Step("ts-search", () => Press(Pad.Secondary), expect: "q");
        Step("ts-type", () => TypeText("more"), expect: "q");
        Step("ts-search-done", () => Key(Godot.Key.Enter), wait: 2.0, expect: "A stable lobby");
        Step("ts-details", () => Tap(JoyButton.A), wait: 2.0, expect: "Download 1.14.0");
        Step("ts-download", () => Tap(JoyButton.A), wait: 2.0, expect: "Download 1.14.0");
        Step("ts-downloads-tab", () =>
        {
            if (Main.Instance.Host!.Downloads.Items.FirstOrDefault() is not { State: Luma.Host.DownloadState.Done, Path: { } path } || !File.Exists(path))
            {
                throw new InvalidOperationException("the download didn't finish: " + Main.Instance.Host.Downloads.Items.FirstOrDefault()?.Error);
            }
            Tap(JoyButton.B);
            Tap(JoyButton.B);
            Tap(JoyButton.RightShoulder);
        }, wait: 1.2, expect: "MoreCompany from Thunderstore", tab: "Downloads");
        Step("download-options", () => Tap(JoyButton.A), expect: "Show the file");
        Step("download-options-back", () => Tap(JoyButton.B), expect: "MoreCompany from Thunderstore");
        // The finished download was installed for Lethal Company: it's in the Mods tab's load order.
        Step("mods-installed", () =>
        {
            Tap(JoyButton.LeftShoulder);
            Tap(JoyButton.LeftShoulder);
            FindFocusable(GetTree().Root, "MoreCompany:")?.GrabFocus();
        }, wait: 1.2, expect: "MoreCompany: A for options", tab: "Mods");
        Step("mod-toggle-off", () => Press(Pad.Secondary), expect: "MoreCompany: A for options, Y turns it on");
        Step("mod-page", () => Press(Pad.Accept), expect: "Turn on");
        Step("mod-turn-on", () => Press(Pad.Accept), expect: "Turn off");
        Step("mod-page-back", () => Press(Pad.Back), expect: "MoreCompany: A for options, Y turns it off");
        Step("deploy", () => FindFocusable(GetTree().Root, "For starting the game from another launcher")?.GrabFocus(), expect: "Put the mods in the game now");
        Step("deploy-run", () => Press(Pad.Accept), wait: 1.5);
        Step("deployed-check", () =>
        {
            var game = Main.Instance.Host!.ActiveGame!;
            var plugin = Directory.EnumerateFiles(Path.Combine(game.GameDir, "BepInEx", "plugins"), "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (plugin == null || new FileInfo(plugin).LinkTarget == null)
            {
                throw new InvalidOperationException("the mod wasn't linked into the game folder");
            }
            FindFocusable(GetTree().Root, "Puts the game folder back")?.GrabFocus();
        }, expect: "Take the mods out of the game");
        Step("undeploy", () => Press(Pad.Accept), wait: 1.2);
        Step("undeployed-check", () =>
        {
            var game = Main.Instance.Host!.ActiveGame!;
            if (Directory.Exists(Path.Combine(game.GameDir, "BepInEx")))
            {
                throw new InvalidOperationException("mod files were left in the game folder");
            }
        });
    }

    private static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            var k = (Key)char.ToUpperInvariant(ch);
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = ch, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = ch, Pressed = false });
        }
        Input.FlushBufferedEvents();
    }

    private static void SetTheme(string id)
    {
        if (Main.Instance.Host is { } host)
        {
            host.SaveSettings(host.Settings with { Theme = id });
            Main.Instance.ApplyTheme(id);
        }
    }

    private static void SetAnimatedBackground(bool on)
    {
        if (Main.Instance.Host is { } host)
        {
            host.SaveSettings(host.Settings with { AnimatedBackground = on });
            Main.Instance.RefreshBackground();
        }
    }

    private void Step(string name, Action act, double wait = 1.0, string? expect = null, string? tab = null) => _steps.Enqueue((name, act, wait, expect, tab));

    private static Control? FindFocusable(Node root, string tooltip)
    {
        foreach (var c in root.GetChildren())
        {
            if (c is Control { FocusMode: Control.FocusModeEnum.All } ctl && ctl.TooltipText.StartsWith(tooltip, StringComparison.Ordinal) && ctl.IsVisibleInTree())
            {
                return ctl;
            }
            if (FindFocusable(c, tooltip) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static void Joy(JoyButton b, bool pressed) =>
        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = b, Pressed = pressed });

    private static void Mouse(Vector2 at, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at });

    private static void TouchTap(Control? c)
    {
        if (c == null)
        {
            return;
        }
        var at = c.GetGlobalRect().GetCenter();
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Mouse(at, true);
        Mouse(at, false);
    }

    private static void Tap(JoyButton b)
    {
        Joy(b, true);
        Joy(b, false);
        Input.FlushBufferedEvents();
    }

    private static void Stick(float x, float y)
    {
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = x });
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = y });
    }

    private static void Key(Key k, bool shift = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = false });
        Input.FlushBufferedEvents();
    }

    private static void Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        Input.FlushBufferedEvents();
    }

    public override void _Process(double delta)
    {
        _wait -= delta;
        if (_wait > 0)
        {
            return;
        }
        if (_steps.Count == 0)
        {
            GD.Print($"smoke: done, {_shot} screenshots, {_failures} failures");
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        var (name, act, wait, expect, tab) = _steps.Dequeue();
        try
        {
            act();
        }
        catch (Exception e)
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: {e}");
        }
        // Give layout and focus a few frames to settle, then capture.
        _wait = wait;
        GetTree().CreateTimer(wait - 0.15).Timeout += () => Capture(name, expect, tab);
    }

    private void Capture(string name, string? expect, string? tab)
    {
        if (tab != null && Main.Instance.CurrentTab != tab)
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: expected the {tab} tab, got {Main.Instance.CurrentTab}");
        }
        var focus = GetViewport().GuiGetFocusOwner();
        // Tiles draw their title with child labels, so fall back to the tooltip (which has the title).
        var text = focus is Button { Text: { Length: > 0 } t } ? t : focus?.TooltipText ?? "";
        if (expect != null && !text.Contains(expect))
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: expected focus on '{expect}', got '{text.Split('\n')[0]}'");
        }
        GD.Print($"smoke: {name}: focus={(focus == null ? "none" : focus.GetType().Name + " '" + text.Split('\n')[0] + "'")}");
        var img = GetViewport().GetTexture().GetImage();
        img?.SavePng(Path.Combine(_outDir, $"{++_shot:00}-{name}.png"));
    }
}
