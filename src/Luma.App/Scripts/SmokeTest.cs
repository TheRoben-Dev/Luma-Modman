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
        Step("library", () => SetAnimatedBackground(false), expect: "Browse mods");
        // Real joypad and keyboard events (not actions), through Pad's translation layer.
        Step("joy-dpad-down", () => { Joy(JoyButton.DpadDown, true); Joy(JoyButton.DpadDown, false); }, expect: "Settings");
        Step("joy-dpad-up", () => { Joy(JoyButton.DpadUp, true); Joy(JoyButton.DpadUp, false); }, expect: "Browse mods");
        // Bumpers switch tabs; on Browse the site tiles are a row of cartridges.
        Step("joy-rb-browse", () => Tap(JoyButton.RightShoulder), expect: "Thunderstore");
        Step("joy-dpad-right", () => Joy(JoyButton.DpadRight, true), wait: 0.25, expect: "mod.io");
        Step("joy-dpad-release", () => Joy(JoyButton.DpadRight, false), expect: "mod.io");
        Step("joy-stick-right", () => Stick(1f), wait: 0.25, expect: "CurseForge");
        Step("joy-stick-center", () => Stick(0f), expect: "CurseForge");
        // Holding a direction auto-repeats after a short delay (and stops at the end of the row).
        Step("joy-dpad-hold-left", () => Joy(JoyButton.DpadLeft, true), wait: 0.9, expect: "Thunderstore");
        Step("joy-dpad-hold-release", () => Joy(JoyButton.DpadLeft, false), expect: "Thunderstore");
        Step("joy-a-site", () => Tap(JoyButton.A), expect: "Thunderstore");
        // A finger tap on another tile (as gamescope delivers it: move there, press, release) selects and presses it.
        Step("touch-tap-site", () => TouchTap(FindFocusable(GetTree().Root, "Nexus Mods")), wait: 1.2, expect: "Nexus Mods");
        Step("joy-rb-installed", () => Tap(JoyButton.RightShoulder), expect: "Browse mods", tab: "Installed");
        Step("joy-rb-downloads", () => Tap(JoyButton.RightShoulder), expect: "Browse mods", tab: "Downloads");
        Step("joy-rb-settings", () => Tap(JoyButton.RightShoulder), expect: "Appearance");
        Step("key-tab-wraps", () => Key(Godot.Key.Tab), expect: "Browse mods");
        Step("key-shift-tab", () => Key(Godot.Key.Tab, shift: true), expect: "Appearance");
        Step("joy-lb-downloads", () => Tap(JoyButton.LeftShoulder), expect: "Browse mods", tab: "Downloads");
        Step("joy-lb-installed", () => Tap(JoyButton.LeftShoulder), expect: "Browse mods", tab: "Installed");
        // A on an empty tab's button jumps to that tab.
        Step("installed-browse", () => Press(Pad.Accept), expect: "Thunderstore");
        Step("settings", () =>
        {
            Tap(JoyButton.RightShoulder);
            Tap(JoyButton.RightShoulder);
            Tap(JoyButton.RightShoulder);
        }, expect: "Appearance");
        // Two panes: moving down the categories swaps the right pane in place.
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
            Step($"theme-{id}-browse", () =>
            {
                SetTheme(id);
                Tap(JoyButton.RightShoulder);  // Settings -> Library (wraps)
                Tap(JoyButton.RightShoulder);  // -> Browse
            }, wait: 1.2, expect: "Thunderstore");
            Step($"theme-{id}-tile", () => Press("ui_right"), wait: 0.8, expect: "mod.io");
            Step($"theme-{id}-tap", () => TouchTap(FindFocusable(GetTree().Root, "CurseForge")), wait: 0.8, expect: "CurseForge");
            Step($"theme-{id}-settings", () =>
            {
                Tap(JoyButton.LeftShoulder);  // -> Library
                Tap(JoyButton.LeftShoulder);  // -> Settings (wraps)
            }, wait: 1.0, expect: "Appearance");
        }
        Step("theme-restore", () => SetTheme("dark"), wait: 1.0);
        Step("changelog", () =>
        {
            FindFocusable(GetTree().Root, "Version, changelog")?.GrabFocus();
            Press(Pad.Accept);  // into Updates: first row is Changelog
            Press(Pad.Accept);
        });
        Step("changelog-back", () => Press(Pad.Back), expect: "Changelog");
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
    }

    private static void Stick(float x) =>
        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = x });

    private static void Key(Key k, bool shift = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, Pressed = false });
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
