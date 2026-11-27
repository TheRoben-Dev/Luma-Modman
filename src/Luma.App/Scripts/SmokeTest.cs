using Luma.App.Desktop;
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
    private readonly Queue<(string Name, Action Act, double Wait, string? Expect, string? Tab, Func<string?>? Check)> _steps = new();
    private double _wait = 1.0;
    private int _shot;
    private int _failures;

    public SmokeTest(string outDir)
    {
        _outDir = outDir;
        Directory.CreateDirectory(outDir);
        if (UiLayout.Desktop)
        {
            DesktopSteps();
            return;
        }
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
        // Bethesda plugins: a mod with a master and a plugin shows up in the Plugins page, and A turns one off.
        Step("skyrim-plugin-mod", InstallPluginMod, wait: 1.2, tab: "Mods");
        Step("plugins-open", () =>
        {
            FindFocusable(GetTree().Root, "The plugin load order")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 1.0, expect: "Smoke Master.esm");
        Step("plugins-toggle", () => Press(Pad.Accept), wait: 0.8, expect: "Smoke Master.esm", check: () =>
            Main.Instance.Host!.Mods.Store(Main.Instance.Host.ActiveGame!).State.IsPluginOn("Smoke Master.esm") ? "A didn't turn the plugin off" : null);
        Step("plugins-back", () => Press(Pad.Back), wait: 0.8, tab: "Mods");
        // FOMOD installer: pick CBBE (which shows a second step), pick an extra, install.
        bool FomodOpen() => All(GetTree().Root).OfType<Luma.App.Screens.FomodPage>().Any(p => p.IsVisibleInTree());
        Step("fomod-open", () => Luma.App.Screens.FomodSession.InstallFile(Main.Instance.Host!.ActiveGame!, FomodZip()), wait: 2.0,
            expect: "Vanilla: A to unpick", check: () => FomodOpen() ? null : "the installer didn't open");
        Step("fomod-pick", () =>
        {
            Press("ui_down");
            Press(Pad.Accept);
        }, wait: 0.8, expect: "CBBE: A to unpick");
        Step("fomod-next", () =>
        {
            FindFocusable(GetTree().Root, "On to the next step")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 0.8, expect: "Physics: A to pick");
        Step("fomod-extra", () => Press(Pad.Accept), wait: 0.6, expect: "Physics: A to unpick");
        Step("fomod-install", () =>
        {
            FindFocusable(GetTree().Root, "Installs the mod with these options")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 2.5, tab: "Mods", check: () => FomodInstalled("cbbe", physics: true));
        // Conflicts: a later mod replaces Smoke Armor's mesh; picking Smoke Armor makes its copy go in instead.
        Step("conflict-mod", InstallConflictMod, wait: 1.2, tab: "Mods");
        Step("conflicts-open", () =>
        {
            FindFocusable(GetTree().Root, "Files in more than one mod")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 1.0, expect: "meshes/armor.nif: A picks");
        Step("conflicts-pick", () => Press(Pad.Accept), wait: 0.8, expect: "Smoke Armor");
        Step("conflicts-choose", () => Press(Pad.Accept), wait: 0.8, expect: "meshes/armor.nif: A picks", check: () => ArmorWinner("Smoke Armor"));
        Step("conflicts-back", () => Press(Pad.Back), wait: 0.8, tab: "Mods");
        // Overwrite: a file the game makes while mods are in is kept out of the game folder and listed.
        Step("overwrite-made", GameMakesAFile, wait: 1.2, tab: "Mods");
        Step("overwrite-open", () =>
        {
            FindFocusable(GetTree().Root, "Files the game or a tool made")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 1.0, expect: "Make a mod from these files", check: () => All(GetTree().Root).OfType<Luma.App.Screens.OverwritePage>().Any(p => p.IsVisibleInTree()) ? null : "Overwrite didn't open");
        Step("overwrite-back", () => Press(Pad.Back), wait: 0.8, tab: "Mods");
        // Touch reorder: hold a mod row still to pick it up, drag it above the one before it, lift.
        Luma.App.Ui.ListRow? ModRow(string name) =>
            All(GetTree().Root).OfType<Luma.App.Ui.ListRow>().FirstOrDefault(r => r.IsVisibleInTree() && r.HasMeta("mod") && r.TooltipText.StartsWith(name + ":", StringComparison.Ordinal));
        Step("touch-reorder-focus", () => ModRow("Smoke Retexture")?.GrabFocus(), wait: 0.8, expect: "Smoke Retexture", check: () =>
            All(GetTree().Root).OfType<Luma.App.Screens.ModsScreen>().FirstOrDefault(m => m.IsVisibleInTree()) is { } page && page.Size.Y > GetViewport().GetVisibleRect().Size.Y
                ? $"the Mods page is taller than the window ({page.Size.Y}), so it can't scroll" : null);
        Step("touch-hold", () =>
        {
            if (ModRow("Smoke Retexture") is { } r)
            {
                var at = r.GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                Mouse(at, true);
            }
        }, wait: 1.0, check: () => Luma.App.Ui.TouchScroll.Reordering ? null : "holding the row didn't pick it up");
        Step("touch-reorder", () =>
        {
            if (ModRow("Smoke Armor") is { } target)
            {
                var at = target.GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
                Mouse(at, false);
            }
        }, wait: 1.0, tab: "Mods", expect: "Smoke Retexture", check: () =>
        {
            var order = Main.Instance.Host!.Mods.Store(Main.Instance.Host.ActiveGame!).State.Order.Select(e => e.Id).ToList();
            return order.IndexOf("local:smoke-retexture") < order.IndexOf("local:Smoke_Armor") ? null : "dragging didn't move Smoke Retexture above Smoke Armor: " + string.Join(", ", order);
        });
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
        Step("settings-sites", () => Press("ui_down"), expect: "Mod sites");
        Step("sites-pane", () => Press(Pad.Accept), expect: "Not set up yet");
        Step("sites-back", () => Press(Pad.Back), expect: "Mod sites");
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
        Step("add-file", () =>
        {
            FindFocusable(GetTree().Root, "Install a mod you downloaded yourself")?.GrabFocus();
            Press(Pad.Accept);
        }, expect: "Back");
        Step("add-file-back", () => Press(Pad.Back), expect: "Add a mod from a file");
        // Modpacks: export this profile (full), then import it again into a new profile.
        bool PackPage() => All(GetTree().Root).OfType<Luma.App.Screens.ModPackPage>().Any(p => p.IsVisibleInTree());
        string Packs() => Luma.App.Screens.PackActions.Folder;
        Step("modpack-export", () =>
        {
            FindFocusable(GetTree().Root, "A file others can import")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 1.0, check: () => PackPage() ? null : "the export page didn't open");
        Step("modpack-export-full", () => Press("ui_right"), check: () =>
            All(GetTree().Root).OfType<Label>().Any(l => l.Text == "Inside the pack" && l.IsVisibleInTree()) ? null : "no mod marked as inside the pack");
        Step("modpack-export-run", () =>
        {
            FindFocusable(GetTree().Root, "Saves the modpack file")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 2.0, expect: "Open the folder", check: () =>
            Directory.Exists(Packs()) && Directory.EnumerateFiles(Packs(), "*.lumapack").Any() ? null : "no modpack file was written");
        Step("modpack-export-done", () =>
        {
            Press(Pad.Back);
            Press(Pad.Back);
        }, wait: 0.8, expect: "Export this profile", tab: "Mods");
        Step("modpack-import", () =>
        {
            FindFocusable(GetTree().Root, "Installs a .lumapack")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 1.0, expect: "Import Lethal Company - Default (full).lumapack");
        Step("modpack-import-confirm", () => Press(Pad.Accept), wait: 0.8, expect: "Cancel");
        Step("modpack-import-run", () =>
        {
            FindButton("Import")?.GrabFocus();
            Press(Pad.Accept);
        }, wait: 2.5, check: () =>
            Main.Instance.Host!.Mods.Store(Main.Instance.Host.ActiveGame!).State.Active == "Default (2)" ? null : "the pack's profile isn't active");
        Step("modpack-import-done", () => Press(Pad.Back), wait: 1.0, tab: "Mods");
        // Layout setting: switching to the desktop layout asks before restarting; Cancel keeps this one.
        Step("settings-tab", () => Key(Godot.Key.Key4, ctrl: true), tab: "Settings");
        Step("settings-layout", () =>
        {
            FindFocusable(GetTree().Root, "Controller or desktop layout")?.GrabFocus();
            Press(Pad.Accept);
        }, expect: "Controller: full screen");
        Step("settings-layout-desktop", () =>
        {
            Press("ui_right");  // Automatic -> Controller: no restart needed
            Press("ui_right");  // -> Desktop: asks first
        }, expect: "Cancel");
        Step("settings-layout-cancel", () => Press(Pad.Accept), expect: "Controller: full screen", check: () =>
            Main.Instance.Host!.Settings.Layout == "desktop" ? null : "the layout setting wasn't saved");
    }

    /// <summary>
    /// The desktop layout (`--desktop`): its own window with menus, tables and dialogs, driven with
    /// the mouse and keyboard, through the same flows as the controller run: pick a game, search
    /// and download a mod, turn it off and on, reorder, profiles, deploy, settings and dialogs.
    /// </summary>
    private void DesktopSteps()
    {
        static DesktopShell Shell() => Main.Instance.GetChildren().OfType<DesktopShell>().First();
        static Luma.Host.AppHost Host() => Main.Instance.Host!;
        string? NoController()
        {
            var bad = All(GetTree().Root).FirstOrDefault(n => n is ButtonGlyph or HintBar or Header or FocusHighlight);
            return bad == null ? null : $"controller chrome in the desktop layout: {bad.GetType().Name} at {bad.GetPath()}";
        }
        string? DialogOpen(string title) => All(GetTree().Root).OfType<Window>().Any(w => w != GetTree().Root && w.Visible && w.Title.StartsWith(title, StringComparison.Ordinal))
            ? null : $"no '{title}' dialog open";
        string? NoDialogs() => All(GetTree().Root).OfType<Window>().FirstOrDefault(w => w != GetTree().Root && w is not PopupMenu && w.Visible) is { } w ? $"dialog still open: {w.Title}" : null;
        static string? Mod(Func<Luma.Core.Mods.GameMods, string?> check) =>
            Host().ActiveGame is { } g ? check(Host().Mods.Store(g).State) : "no active game";
        static string? MoreCompany(Luma.Core.Mods.GameMods s) => s.Mods.FirstOrDefault(m => m.Name.StartsWith("MoreCompany", StringComparison.Ordinal))?.Id;

        Step("desktop-start", () => { }, wait: 3.0, tab: "Mods", check: () =>
            NoController() ?? (Shell().GameCount >= 4 ? null : $"only {Shell().GameCount} games listed"));
        Step("desktop-pick-lethal", () => Shell().PickGame("Lethal Company"), wait: 1.5, tab: "Mods", check: () =>
            Host().ActiveGame?.Title == "Lethal Company" ? null : "Lethal Company isn't the active game");
        Step("desktop-browse", () => Key(Godot.Key.Key2, ctrl: true), wait: 3.0, tab: "Browse", check: () =>
            Shell().BrowseView.ResultCount > 0 ? null : "no search results");
        Step("desktop-search", () =>
        {
            Key(Godot.Key.F, ctrl: true);
            TypeText("more");
            Key(Godot.Key.Enter);
        }, wait: 2.5, tab: "Browse", check: () => Shell().BrowseView.ResultCount > 0 ? null : "no results for 'more'");
        Step("desktop-mod-page", () => Shell().BrowseView.SelectResult(0), wait: 2.5, check: () =>
            Shell().BrowseView.DownloadButton != null ? null : "the mod page has no Download button");
        Step("desktop-download", () => Shell().BrowseView.DownloadButton?.EmitSignal(BaseButton.SignalName.Pressed), wait: 3.5, check: () =>
            Host().Downloads.Items.FirstOrDefault() is { State: Luma.Host.DownloadState.Done, Installed: true } ? null
                : "the download didn't finish and install: " + Host().Downloads.Items.FirstOrDefault()?.Error);
        Step("desktop-downloads", () =>
        {
            Key(Godot.Key.Key3, ctrl: true);
            Shell().DownloadsView.SelectFirst();
        }, wait: 1.2, tab: "Downloads");
        Step("desktop-download-menu", () => Shell().DownloadsView.OpenMenu(), wait: 0.8, check: () => Shell().DownloadsView.MenuOpen ? null : "no download menu");
        Step("desktop-download-menu-close", () => Key(Godot.Key.Escape), wait: 0.6, check: () => Shell().DownloadsView.MenuOpen ? "the menu stayed open" : null);
        Step("desktop-mods", () =>
        {
            Key(Godot.Key.Key1, ctrl: true);
            Shell().ModsView.SelectByName("MoreCompany");
        }, wait: 1.5, tab: "Mods", check: () => Shell().ModsView.RowCount >= 1 ? null : "the mod table is empty");
        Step("desktop-right-click", () =>
        {
            if (Shell().ModsView.RowCenter("MoreCompany") is { } at)
            {
                RightClickAt(at);
            }
        }, wait: 0.8, check: () => Shell().ModsView.MenuOpen ? null : "right-click didn't open the mod's menu");
        Step("desktop-right-click-close", () => Key(Godot.Key.Escape), wait: 0.6);
        Step("desktop-toggle-off", () =>
        {
            Shell().ModsView.SelectByName("MoreCompany");
            Key(Godot.Key.Space);
        }, wait: 1.0, check: () => Mod(s => MoreCompany(s) is { } id && !s.IsEnabled(id) ? null : "Space didn't turn MoreCompany off"));
        Step("desktop-toggle-on", () =>
        {
            Shell().ModsView.SelectByName("MoreCompany");
            Key(Godot.Key.Space);
        }, wait: 1.0, check: () => Mod(s => MoreCompany(s) is { } id && s.IsEnabled(id) ? null : "Space didn't turn MoreCompany on"));
        var before = -1;
        Step("desktop-ctrl-down", () =>
        {
            Shell().ModsView.SelectByName("MoreCompany");
            before = Host().Mods.Store(Host().ActiveGame!).State.Order.FindIndex(e => e.Id.Contains("MoreCompany", StringComparison.Ordinal));
            Key(Godot.Key.Up, ctrl: true);
        }, wait: 1.0, check: () => Mod(s =>
        {
            var now = s.Order.FindIndex(e => e.Id.Contains("MoreCompany", StringComparison.Ordinal));
            return before <= 0 || now == before - 1 ? null : $"Ctrl+Up moved it from {before} to {now}";
        }));
        Step("desktop-deploy", () => FindButton("Put mods in game")?.EmitSignal(BaseButton.SignalName.Pressed), wait: 2.0, check: () =>
        {
            var game = Host().ActiveGame!;
            var plugin = Directory.Exists(Path.Combine(game.GameDir, "BepInEx", "plugins"))
                ? Directory.EnumerateFiles(Path.Combine(game.GameDir, "BepInEx", "plugins"), "*.dll", SearchOption.AllDirectories).FirstOrDefault() : null;
            return plugin != null && new FileInfo(plugin).LinkTarget != null ? null : "the mod wasn't linked into the game folder";
        });
        Step("desktop-undeploy", () => FindButton("Take mods out of game")?.EmitSignal(BaseButton.SignalName.Pressed), wait: 1.5, check: () =>
            Directory.Exists(Path.Combine(Host().ActiveGame!.GameDir, "BepInEx")) ? "mod files were left in the game folder" : null);
        Step("desktop-new-profile", () => FindButton("New…")?.EmitSignal(BaseButton.SignalName.Pressed), wait: 1.0, check: () => DialogOpen("New profile"));
        Step("desktop-new-profile-typed", () =>
        {
            TypeText("Testing");
            Key(Godot.Key.Enter);
        }, wait: 1.2, check: () => NoDialogs() ?? Mod(s => s.Active == "Testing" ? null : $"active profile is {s.Active}"));
        Step("desktop-delete-profile", () => FindButton("Delete")?.EmitSignal(BaseButton.SignalName.Pressed), wait: 1.0, check: () => DialogOpen("Please confirm"));
        Step("desktop-delete-profile-yes", () => FindButton("Delete", inDialog: true)?.EmitSignal(BaseButton.SignalName.Pressed), wait: 1.2, check: () =>
            NoDialogs() ?? Mod(s => s.Profiles.ContainsKey("Testing") ? "the profile is still there" : null));
        Step("desktop-file-menu", () => ClickAt(new Vector2(22, 14)), wait: 0.8, check: () =>
            All(GetTree().Root).OfType<PopupMenu>().Any(p => p.Visible && p.Name == "File") ? null : "the File menu didn't open");
        Step("desktop-file-menu-close", () => Key(Godot.Key.Escape), wait: 0.6);
        Step("desktop-add-file", () => Key(Godot.Key.O, ctrl: true), wait: 1.2, check: () => DialogOpen("Add a mod from a file"));
        Step("desktop-add-file-cancel", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        var pack = "";
        void PickFile(string path)
        {
            pack = path;
            All(GetTree().Root).OfType<FileDialog>().LastOrDefault(f => f.Visible)?.EmitSignal(FileDialog.SignalName.FileSelected, path);
        }
        Step("desktop-export-pack", () => Key(Godot.Key.E, ctrl: true), wait: 1.2, check: () => DialogOpen("Export Default as a modpack"));
        Step("desktop-export-pack-save", () => PickFile(Path.Combine(Luma.App.Screens.PackActions.Folder, "Smoke pack.lumapack")), wait: 2.0, check: () =>
            File.Exists(pack) ? DialogOpen("") : "no modpack file was written");
        Step("desktop-export-pack-done", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-import-pack", () => Key(Godot.Key.I, ctrl: true), wait: 1.2, check: () => DialogOpen("Import a modpack"));
        Step("desktop-import-pack-pick", () => PickFile(pack), wait: 1.0, check: () => DialogOpen("Please confirm"));
        Step("desktop-import-pack-yes", () => FindButton("Import", inDialog: true)?.EmitSignal(BaseButton.SignalName.Pressed), wait: 2.5, check: () =>
            Mod(s => s.Active == "Default (2)" ? null : $"active profile is {s.Active}"));
        Step("desktop-import-pack-done", () => Key(Godot.Key.Escape), wait: 1.0, check: NoDialogs);
        Step("desktop-steam", () => Main.Instance.Push(new Luma.App.Screens.SteamLaunchPage(Host().ActiveGame!)), wait: 1.0, check: () => DialogOpen("Start with mods from Steam"));
        Step("desktop-steam-close", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-settings", () => Key(Godot.Key.Key4, ctrl: true), wait: 1.0, tab: "Settings");
        foreach (var cat in new[] { "Mod sites", "Appearance", "Sound & feel", "Layout", "Updates", "About" })
        {
            var c = cat;
            Step("desktop-settings-" + c.Split(' ')[0].ToLowerInvariant(), () => Shell().SettingsView.Open(c), wait: 0.8, tab: "Settings",
                check: () => Shell().SettingsView.Current == c ? null : $"settings show {Shell().SettingsView.Current}");
        }
        Step("desktop-site-setup", () => Main.Instance.Push(new Luma.App.Screens.SiteSetupPage(Host().Sites.Nexus)), wait: 1.0, check: () => DialogOpen("Set up Nexus Mods"));
        Step("desktop-site-setup-close", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-changelog", () => Main.Instance.ShowChangelog(), wait: 1.0, check: () => DialogOpen("Changelog"));
        Step("desktop-changelog-close", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-supported-games", () => Main.Instance.Push(new Luma.App.Screens.SupportedGamesPage()), wait: 1.2, check: () => DialogOpen("Supported games"));
        Step("desktop-supported-games-close", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-layout-switch", () => Main.Instance.ApplyLayout("controller"), wait: 1.0, check: () => DialogOpen("Please confirm"));
        Step("desktop-layout-switch-cancel", () => FindButton("Cancel", inDialog: true)?.EmitSignal(BaseButton.SignalName.Pressed), wait: 0.8, check: NoDialogs);
        Step("desktop-skyrim", () =>
        {
            Main.Instance.Host!.SaveSettings(Main.Instance.Host.Settings with { Layout = "auto" });
            Shell().PickGame("The Elder Scrolls V");
        }, wait: 1.5, tab: "Mods", check: () => Host().ActiveGame?.Title.StartsWith("The Elder Scrolls V", StringComparison.Ordinal) == true ? null : "Skyrim isn't active");
        Step("desktop-skyrim-plugin-mod", InstallPluginMod, wait: 1.2, tab: "Mods");
        Step("desktop-plugins", () => Key(Godot.Key.P, ctrl: true), wait: 1.2, check: () =>
            DialogOpen("Plugins") ?? (All(GetTree().Root).OfType<Tree>().Any(t => t.TooltipText == "Plugins" && t.GetRoot()?.GetChildCount() == 2) ? null : "the plugin table doesn't list the mod's two plugins"));
        Step("desktop-plugins-close", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-fomod", () => Luma.App.Screens.FomodSession.InstallFile(Host().ActiveGame!, FomodZip()), wait: 2.0, check: () => DialogOpen("Install options"));
        Step("desktop-fomod-pick", () =>
            All(GetTree().Root).OfType<CheckBox>().FirstOrDefault(c => c.Text.StartsWith("CBBE", StringComparison.Ordinal) && c.IsVisibleInTree())?.EmitSignal(BaseButton.SignalName.Pressed),
            wait: 0.8, check: () => FindButton("Next", inDialog: true) != null ? null : "no Next button after picking CBBE");
        Step("desktop-fomod-next", () => FindButton("Next", inDialog: true)?.EmitSignal(BaseButton.SignalName.Pressed), wait: 0.8, check: () =>
            All(GetTree().Root).OfType<CheckBox>().Any(c => c.Text.StartsWith("Physics", StringComparison.Ordinal) && c.IsVisibleInTree()) ? null : "the second step didn't show");
        Step("desktop-fomod-install", () => FindButton("Install", inDialog: true)?.EmitSignal(BaseButton.SignalName.Pressed), wait: 2.5, check: () =>
            NoDialogs() ?? FomodInstalled("cbbe", physics: false));
        Step("desktop-conflict-mod", InstallConflictMod, wait: 1.2, tab: "Mods");
        Tree? ConflictTree() => All(GetTree().Root).OfType<Tree>().FirstOrDefault(t => t.TooltipText == "Conflicts" && t.IsVisibleInTree());
        Step("desktop-conflicts", () => Key(Godot.Key.K, ctrl: true), wait: 1.2, check: () =>
            DialogOpen("Conflicts") ?? (ConflictTree()?.GetRoot()?.GetChildCount() == 1 ? null : "the conflict table doesn't list the one shared file"));
        Step("desktop-conflicts-pick", () =>
        {
            // Picks "Smoke Armor" (option 1; 0 is the load order) in the row's drop-down.
            if (ConflictTree() is { } t && t.GetRoot()?.GetChild(0) is { } it)
            {
                it.Select(1);
                it.SetRange(1, 1);
                t.EmitSignal(Tree.SignalName.ItemEdited);
            }
        }, wait: 0.8, check: () => ArmorWinner("Smoke Armor"));
        Step("desktop-conflicts-close", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-overwrite-made", GameMakesAFile, wait: 1.2, tab: "Mods");
        Step("desktop-overwrite", () => FindButton("Overwrite…")?.EmitSignal(BaseButton.SignalName.Pressed), wait: 1.0, check: () =>
            DialogOpen("Overwrite") ?? (All(GetTree().Root).OfType<ItemList>().Any(l => l.TooltipText == "Files in Overwrite" && l.ItemCount == 1) ? null : "Overwrite doesn't list the file"));
        Step("desktop-overwrite-close", () => Key(Godot.Key.Escape), wait: 0.8, check: NoDialogs);
        Step("desktop-browse-nexus", () => Key(Godot.Key.Key2, ctrl: true), wait: 2.0, tab: "Browse");
        Step("desktop-not-moddable", () => Shell().PickGame("Marvel Rivals"), wait: 1.5, tab: "Mods", check: NoController);
    }

    /// <summary>A mod archive with a FOMOD installer: a body choice (CBBE adds a step with an extra) and a patch for CBBE.</summary>
    private static string FomodZip()
    {
        var host = Main.Instance.Host!;
        var dir = host.Paths.Data;  // not Downloads: the add-file page lists that
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "Smoke Armor.zip");
        if (File.Exists(zip))
        {
            return zip;
        }
        const string xml = """
            <config><moduleName>Smoke Armor</moduleName>
              <requiredInstallFiles><folder source="Core" destination="" /></requiredInstallFiles>
              <installSteps order="Explicit">
                <installStep name="Body"><optionalFileGroups><group name="Body type" type="SelectExactlyOne"><plugins order="Explicit">
                  <plugin name="Vanilla"><description>Fits the default body.</description><files><folder source="Bodies\Vanilla" destination="meshes" /></files>
                    <conditionFlags><flag name="body">vanilla</flag></conditionFlags><typeDescriptor><type name="Recommended" /></typeDescriptor></plugin>
                  <plugin name="CBBE"><description>For CBBE bodies.</description><files><folder source="Bodies\CBBE" destination="meshes" /></files>
                    <conditionFlags><flag name="body">cbbe</flag></conditionFlags><typeDescriptor><type name="Optional" /></typeDescriptor></plugin>
                </plugins></group></optionalFileGroups></installStep>
                <installStep name="CBBE extras"><visible><flagDependency flag="body" value="cbbe" /></visible>
                  <optionalFileGroups><group name="Extras" type="SelectAny"><plugins>
                    <plugin name="Physics"><description>Bouncy.</description><files><file source="Extras\physics.esp" destination="Smoke Physics.esp" /></files>
                      <typeDescriptor><type name="Optional" /></typeDescriptor></plugin>
                  </plugins></group></optionalFileGroups></installStep>
              </installSteps>
            </config>
            """;
        using var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            using var w = new StreamWriter(z.CreateEntry(name).Open());
            w.Write(text);
        }
        Add("fomod/ModuleConfig.xml", xml);
        Add("Core/SmokeArmor.esp", "esp");
        Add("Bodies/Vanilla/armor.nif", "vanilla");
        Add("Bodies/CBBE/armor.nif", "cbbe");
        Add("Extras/Physics.esp", "physics");
        return zip;
    }

    private static string? FomodInstalled(string body, bool physics)
    {
        var host = Main.Instance.Host!;
        var store = host.Mods.Store(host.ActiveGame!);
        if (store.State.Mods.FirstOrDefault(m => m.Name == "Smoke Armor") is not { } mod)
        {
            return "Smoke Armor wasn't installed";
        }
        var nif = Path.Combine(store.ModDir(mod.Id), "mods", "meshes", "armor.nif");
        if (!File.Exists(nif) || File.ReadAllText(nif) != body)
        {
            return $"the {body} body wasn't installed";
        }
        return File.Exists(Path.Combine(store.ModDir(mod.Id), "mods", "Smoke Physics.esp")) == physics ? null : "the extra was installed wrongly";
    }

    /// <summary>A mod that replaces Smoke Armor's mesh, installed after it (so it wins by load order).</summary>
    private static void InstallConflictMod()
    {
        var host = Main.Instance.Host!;
        var game = host.ActiveGame!;
        var zip = Path.Combine(host.Paths.Data, "smoke-retexture.zip");
        if (!File.Exists(zip))
        {
            using var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create);
            // Two top folders: one alone would be taken for a wrapper folder and dropped.
            using (var w = new StreamWriter(z.CreateEntry("meshes/armor.nif").Open()))
            {
                w.Write("retexture");
            }
            z.CreateEntry("textures/armor.dds");
        }
        host.Mods.Install(game, zip, new Luma.Core.Mods.InstalledMod { Id = "local:smoke-retexture", Site = "local", Name = "Smoke Retexture" });
        host.Mods.NotifyChanged(game);
    }

    /// <summary>Puts the mods in, has "the game" write a file into Data, and takes them out again: the file goes to Overwrite.</summary>
    private static void GameMakesAFile()
    {
        var host = Main.Instance.Host!;
        var game = host.ActiveGame!;
        host.Mods.Deploy(game);
        File.WriteAllText(Path.Combine(host.Mods.ModsDir(game)!, "smoke-output.txt"), "made by the game");
        host.Mods.Undeploy(game);
    }

    /// <summary>Null when the mod named <paramref name="mod"/> is picked to win meshes/armor.nif.</summary>
    private static string? ArmorWinner(string mod)
    {
        var host = Main.Instance.Host!;
        var game = host.ActiveGame!;
        var c = host.Mods.Plan(game).Conflicts.FirstOrDefault(x => x.Key == "mods/meshes/armor.nif");
        if (c == null)
        {
            return "no conflict over meshes/armor.nif";
        }
        var name = host.Mods.Store(game).State.Find(c.Winner)?.Name;
        return c.Picked && name == mod ? null : $"{name} wins armor.nif (picked: {c.Picked}), not {mod}";
    }

    /// <summary>Installs a small mod with a master and a plugin for the active (Bethesda) game.</summary>
    private static void InstallPluginMod()
    {
        var host = Main.Instance.Host!;
        var game = host.ActiveGame!;
        var zip = Path.Combine(host.Paths.Data, "smoke-plugins.zip");
        using (var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            z.CreateEntry("Smoke Master.esm");
            z.CreateEntry("Smoke Patch.esp");
        }
        host.Mods.Install(game, zip, new Luma.Core.Mods.InstalledMod { Id = "local:smoke-plugins", Site = "local", Name = "Smoke plugins" });
        host.Mods.NotifyChanged(game);
    }

    private static IEnumerable<Node> All(Node root)
    {
        foreach (var c in root.GetChildren(true))
        {
            yield return c;
            foreach (var d in All(c))
            {
                yield return d;
            }
        }
    }

    /// <summary>A visible, enabled button by its text: in the open dialog, or in the main window.</summary>
    private Button? FindButton(string text, bool inDialog = false)
    {
        var root = inDialog ? All(GetTree().Root).OfType<Window>().LastOrDefault(w => w != GetTree().Root && w.Visible && w is not PopupMenu) as Node : GetTree().Root;
        return root == null ? null : All(root).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree() && !b.Disabled);
    }

    private static void ClickAt(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.FlushBufferedEvents();
        Mouse(at, true);
        Mouse(at, false);
        Input.FlushBufferedEvents();
    }

    private static void RightClickAt(Vector2 at)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.FlushBufferedEvents();
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, GlobalPosition = at });
        Input.FlushBufferedEvents();
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

    private void Step(string name, Action act, double wait = 1.0, string? expect = null, string? tab = null, Func<string?>? check = null) =>
        _steps.Enqueue((name, act, wait, expect, tab, check));

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

    private static void Key(Key k, bool shift = false, bool ctrl = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, CtrlPressed = ctrl, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, ShiftPressed = shift, CtrlPressed = ctrl, Pressed = false });
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
        var (name, act, wait, expect, tab, check) = _steps.Dequeue();
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
        GetTree().CreateTimer(wait - 0.15).Timeout += () => Capture(name, expect, tab, check);
    }

    private void Capture(string name, string? expect, string? tab, Func<string?>? check)
    {
        string? problem;
        try
        {
            problem = check?.Invoke();
        }
        catch (Exception e)
        {
            problem = e.ToString();
        }
        if (problem != null)
        {
            _failures++;
            GD.PushError($"smoke: step {name} failed: {problem}");
        }
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
