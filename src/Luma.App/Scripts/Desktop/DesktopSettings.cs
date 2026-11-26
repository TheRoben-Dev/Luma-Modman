using Luma.App.Screens;
using Luma.App.Ui;
using Luma.Host;
using Luma.Host.Updates;
using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// The Settings tab in the desktop layout: categories on the left, a form on the right. Same
/// settings as the controller layout, with ordinary checkboxes and drop-downs.
/// </summary>
public partial class DesktopSettings : HSplitContainer
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;

    public static readonly string[] Shortcuts =
    [
        "Ctrl+1 … Ctrl+4: Mods, Browse, Downloads, Settings",
        "Ctrl+F: search mods (Browse)",
        "Ctrl+O: add a mod from a file",
        "Ctrl+I / Ctrl+E: import or export a modpack",
        "Ctrl+U: check for mod updates",
        "Ctrl+R: scan for games",
        "Ctrl+Up / Ctrl+Down: move the selected mod in the load order",
        "Space or double-click: turn the selected mod on or off",
        "Delete: uninstall the selected mod",
        "Right-click a mod or download: its options",
        "Esc: close a dialog",
        "Ctrl+Q: quit",
    ];

    private static readonly (string Name, string Summary)[] Categories =
    [
        ("Games", "Finding installed games, and the supported list."),
        ("Mod sites", "Your keys for Nexus Mods, CurseForge and mod.io."),
        ("Appearance", "Theme and accent colour."),
        ("Sound & feel", "Menu sounds and controller rumble."),
        ("Layout", "Controller or desktop layout, and keyboard shortcuts."),
        ("Updates", "Version, changelog and updates."),
        ("About", "Where Luma keeps its files, and the app log."),
    ];

    private ItemList _list = null!;
    private VBoxContainer _page = null!;
    private string? _pendingTheme;
    private string? _pendingAccent;

    public string? StartIn { get; init; }

    public override void _Ready()
    {
        _list = new ItemList { CustomMinimumSize = new Vector2(190, 0), TooltipText = "Settings categories" };
        foreach (var (name, summary) in Categories)
        {
            var i = _list.AddItem(name);
            _list.SetItemTooltip(i, summary);
        }
        _list.ItemSelected += _ => Refresh();
        AddChild(_list);
        var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", DesktopTheme.Frame(16));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        frame.AddChild(scroll);
        _page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _page.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_page);
        AddChild(frame);
        var start = Array.FindIndex(Categories, c => string.Equals(c.Name, StartIn, StringComparison.OrdinalIgnoreCase));
        _list.Select(Math.Max(0, start));
        Refresh();
    }

    public string Current => _list.GetSelectedItems() is { Length: > 0 } s ? Categories[s[0]].Name : Categories[0].Name;

    public void Open(string category)
    {
        var i = Array.FindIndex(Categories, c => c.Name == category);
        if (i >= 0)
        {
            _list.Select(i);
            Refresh();
        }
    }

    public void Refresh()
    {
        if (_page == null)
        {
            return;
        }
        foreach (var c in _page.GetChildren())
        {
            c.QueueFree();
        }
        var name = Current;
        var summary = Categories.First(c => c.Name == name).Summary;
        _page.AddChild(DesktopTheme.Label(name, null, DesktopTheme.FontSize + 6));
        _page.AddChild(DesktopTheme.Label(summary, DesktopTheme.Dim, wrap: true));
        _page.AddChild(new HSeparator());
        switch (name)
        {
            case "Games":
                Games();
                break;
            case "Mod sites":
                Sites();
                break;
            case "Appearance":
                Appearance();
                break;
            case "Sound & feel":
                Sound();
                break;
            case "Layout":
                Layout();
                break;
            case "Updates":
                Updates();
                break;
            case "About":
                About();
                break;
        }
    }

    // ---- form helpers ----

    private void Text(string text, Color? color = null) => _page.AddChild(DesktopTheme.Label(text, color ?? DesktopTheme.Dim, wrap: true));

    private void Heading(string text)
    {
        var l = DesktopTheme.Label(text.ToUpperInvariant(), DesktopTheme.Dim, DesktopTheme.SmallSize);
        l.CustomMinimumSize = new Vector2(0, 28);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        _page.AddChild(l);
    }

    private Button Act(string text, Action run, string tip = "", bool primary = false)
    {
        var b = DesktopTheme.Button(text, run, tip, primary ? "PrimaryButton" : "");
        b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _page.AddChild(b);
        return b;
    }

    /// <summary>A labelled row: name on the left, the control, and help text under it.</summary>
    private void Row(string label, Control control, string help = "")
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var l = DesktopTheme.Label(label);
        l.CustomMinimumSize = new Vector2(170, 0);
        row.AddChild(l);
        control.SizeFlagsHorizontal = control is CheckBox ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin;
        if (control is OptionButton)
        {
            control.CustomMinimumSize = new Vector2(240, 0);
        }
        control.TooltipText = help;
        row.AddChild(control);
        _page.AddChild(row);
        if (help.Length > 0)
        {
            var h = DesktopTheme.Label(help, DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true);
            var m = DesktopShell.Margin(h, 0, 0);
            m.AddThemeConstantOverride("margin_left", 182);
            _page.AddChild(m);
        }
    }

    private static OptionButton Options(IEnumerable<(string Id, string Label)> items, string selected, Action<string> changed)
    {
        var o = new OptionButton();
        var list = items.ToList();
        foreach (var (id, label) in list)
        {
            o.AddItem(label);
            o.SetItemMetadata(o.ItemCount - 1, id);
            if (id == selected)
            {
                o.Select(o.ItemCount - 1);
            }
        }
        o.ItemSelected += i => changed(o.GetItemMetadata((int)i).AsString());
        return o;
    }

    private static CheckBox Check(string text, bool on, Action<bool> changed)
    {
        var c = new CheckBox { Text = text, ButtonPressed = on };
        c.Toggled += v => changed(v);
        return c;
    }

    // ---- categories ----

    private void Games()
    {
        var lib = Host.Games;
        var when = lib.ScannedAt is { } t ? t.ToLocalTime().ToString("g") : "never";
        Text($"{lib.Games.Count} supported game{(lib.Games.Count == 1 ? "" : "s")} installed. Last scan: {when}.", DesktopTheme.Text);
        Act("Scan again", App.ScanGames, "Look for installed games again: Steam (every library, SD cards too), Heroic and Lutris, native and Flatpak.");
        Act($"Supported games ({lib.Catalog.Games.Count})…", () => App.Push(new SupportedGamesPage()), "Every game Luma can mod, and the ones it can't.");
        Heading("Your own game profiles");
        var dir = Path.Combine(Host.Paths.Config, "games");
        Text($"Add or fix a game without waiting for an update: put a profile (.json, same format as Luma's own) in {dir}. Yours win over the built-in ones.");
        Act("Open the folder", () => DesktopShell.OpenFolder(dir));
        foreach (var p in lib.Catalog.Problems)
        {
            Text("Skipped profile: " + p, DesktopTheme.Warn);
        }
    }

    private void Sites()
    {
        Text("Thunderstore needs no account. The other sites ask each player for their own free key; it stays on this device and is only sent to that site.");
        foreach (var site in Host.Sites.All.Where(SiteKeys.NeedsKey))
        {
            var s = site;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var name = DesktopTheme.Label(s.Name);
            name.CustomMinimumSize = new Vector2(170, 0);
            row.AddChild(name);
            var status = DesktopTheme.Label(SiteKeys.Status(s), SiteKeys.CurrentKey(s) == null ? DesktopTheme.Dim : DesktopTheme.Good);
            status.CustomMinimumSize = new Vector2(260, 0);
            row.AddChild(status);
            row.AddChild(DesktopTheme.Button(SiteKeys.CurrentKey(s) == null ? "Set up…" : "Change…", () => App.Push(new SiteSetupPage(s))));
            _page.AddChild(row);
        }
        Heading("Nexus download links");
        Text(NxmHandler.IsRegistered(Host.Paths)
            ? "\"Mod Manager Download\" buttons on Nexus Mods open in Luma."
            : "Let the website's \"Mod Manager Download\" buttons send files to Luma.");
        Act(NxmHandler.IsRegistered(Host.Paths) ? "Set up download links again" : "Handle Nexus download links", () =>
        {
            SiteKeys.SetUpNxmLinks();
            Refresh();
        }, "Makes Luma the browser's handler for nxm:// links.");
    }

    private void Appearance()
    {
        var active = UiTheme.Name;
        _pendingTheme ??= active;
        var themeId = _pendingTheme;
        _pendingAccent ??= Host.Settings.Accents.GetValueOrDefault(themeId) ?? "";
        Row("Theme", Options(UiTheme.Themes.Select(t => (t.Id, t.Label + (t.Id == active ? "  (current)" : ""))), themeId, v =>
        {
            _pendingTheme = v;
            _pendingAccent = Host.Settings.Accents.GetValueOrDefault(v) ?? "";
            Refresh();
        }), (UiTheme.Themes.FirstOrDefault(t => t.Id == themeId)?.Blurb ?? "") + " In this layout the theme sets light or dark and the accent colour.");
        var pocket = themeId == "pocket";
        Row(pocket ? "Screen colour" : "Accent colour", Options(
            new[] { ("", pocket ? "Classic green" : "Theme's own") }.Concat(UiTheme.SwatchesFor(UiTheme.Themes.First(t => t.Id == themeId))), _pendingAccent, v =>
            {
                _pendingAccent = v;
                Refresh();
            }), "The highlight colour for this theme. Each theme remembers its own.");
        var changed = themeId != active || _pendingAccent != (Host.Settings.Accents.GetValueOrDefault(themeId) ?? "");
        if (changed)
        {
            var label = UiTheme.Themes.First(t => t.Id == themeId).Label;
            Act($"Restart to apply {label}", () =>
            {
                var accents = new Dictionary<string, string>(Host.Settings.Accents);
                if (_pendingAccent is { Length: > 0 } a)
                {
                    accents[themeId] = a;
                }
                else
                {
                    accents.Remove(themeId);
                }
                Host.SaveSettings(Host.Settings with { Theme = themeId, Accents = accents });
                App.RestartApp("--open=appearance");
            }, "Saves the theme and restarts the app. You come back to this page.", primary: true);
        }
        Heading("Controller layout");
        Text("These change the full-screen controller layout's look.");
        Row("Background", Check("Animated background", Host.Settings.AnimatedBackground, on =>
        {
            Host.SaveSettings(Host.Settings with { AnimatedBackground = on });
            App.RefreshBackground();
        }), "A slow animation behind the menus that matches the theme. It stops while a game runs.");
        Row("CRT effect", Options([("off", "Off"), ("1", "Subtle"), ("2", "Medium"), ("3", "Strong")], Host.Settings.Crt ? Host.Settings.CrtLevel.ToString() : "off", v =>
        {
            Host.SaveSettings(v == "off" ? Host.Settings with { Crt = false } : Host.Settings with { Crt = true, CrtLevel = int.Parse(v) });
            App.RefreshBackground();
        }), "Makes the whole screen look like an old TV.");
        Row("Animations", Check("Sliding focus, smooth scrolling and page transitions", !Host.Settings.ReduceMotion, on =>
        {
            Host.SaveSettings(Host.Settings with { ReduceMotion = !on });
            Anim.Enabled = on;
        }));
    }

    private void Sound()
    {
        Text("Each theme has its own set of menu sounds. They play in the controller layout; in games, only the game's own sound and rumble play.");
        var levels = new List<(string, string)> { ("0", "Off"), ("25", "Quiet"), ("50", "Medium"), ("75", "Loud"), ("100", "Full") };
        var current = Host.Settings.SoundVolume;
        var nearest = levels.OrderBy(l => Math.Abs(int.Parse(l.Item1) - current)).First().Item1;
        Row("Menu sounds", Options(levels, nearest, v =>
        {
            Host.SaveSettings(Host.Settings with { SoundVolume = int.Parse(v) });
            App.ApplyFeedback(Host.Settings.SoundVolume, Host.Settings.Haptics);
        }), "Short sounds when you move, press, go back or switch tabs.");
        Row("Rumble", Check("Controller rumble", Host.Settings.Haptics, on =>
        {
            Host.SaveSettings(Host.Settings with { Haptics = on });
            App.ApplyFeedback(Host.Settings.SoundVolume, on);
        }), "A light buzz for menu moves, presses and errors.");
    }

    private void Layout()
    {
        var current = Host.Settings.Layout;
        Row("Layout", Options(UiLayout.Choices.Select(c => (c.Id, c.Label)), UiLayout.Choices.Any(c => c.Id == current) ? current : UiLayout.Auto, v =>
        {
            App.ApplyLayout(v);
            Callable.From(Refresh).CallDeferred();
        }), UiLayout.Describe(current) + " Switching to the other layout restarts Luma.");
        Heading("Keyboard and mouse");
        foreach (var line in Shortcuts)
        {
            Text("•  " + line, DesktopTheme.Text);
        }
    }

    private void Updates()
    {
        var updater = Host.Updater;
        _page.AddChild(DesktopTheme.Label($"Luma {AppUpdater.DisplayVersion(updater.CurrentVersion)}", null, DesktopTheme.FontSize + 2));
        Act("Changelog…", App.ShowChangelog, "What changed in each version of Luma.");
        if (updater.CanSelfUpdate)
        {
            Act("Check for updates", () => App.CheckForAppUpdate(silent: false), "Looks for a newer Luma on GitHub. Updates install in place and keep your mods, profiles and settings.");
            Row("At startup", Check("Check for updates when Luma starts", Host.Settings.CheckForUpdates, on => Host.SaveSettings(Host.Settings with { CheckForUpdates = on })),
                "Asks when a new version is out. Never interrupts a game.");
            if (updater.CanRollback)
            {
                var prev = updater.PreviousVersion is { } pv ? $" ({pv})" : "";
                Act($"Roll back to previous version{prev}…", () => App.Confirm($"Go back to the previous Luma{prev}? The app restarts.", "Roll back", App.RollbackApp));
            }
            var dir = updater.InstallDir;
            bool added;
            try
            {
                added = SteamSetup.IsAdded(Host.Paths.Home, dir);
            }
            catch (Exception)
            {
                added = false;
            }
            Act(added ? "Add Luma to Steam again (refresh art)" : "Add Luma to Steam", () => SettingsScreen.AddToSteam(dir),
                $"A Steam shortcut with Luma's own art, so it opens from {GameText.SteamTvMode}.");
        }
        else
        {
            Text("This copy can't update itself (development build or package). Use the installer to update.");
        }
        Text("Luma installs mods only from the sites you connect, never games, DRM bypasses or pirated content.");
    }

    private void About()
    {
        Text("Luma keeps mods, downloads and backups in its own folders. Game folders only see mods while a game runs, through links Luma puts in at launch and takes out afterwards, so every change can be undone.");
        foreach (var (label, path) in new[]
                 {
                     ("Mods", Host.Paths.ModsDir),
                     ("Downloads", Host.Paths.DownloadsDir),
                     ("Settings", Host.Paths.Config),
                     ("App log", AppLog.FilePath ?? "(not open)"),
                 })
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var l = DesktopTheme.Label(label);
            l.CustomMinimumSize = new Vector2(170, 0);
            row.AddChild(l);
            var p = new LineEdit { Text = path, Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(p);
            var target = label == "App log" ? Path.GetDirectoryName(path) : path;
            row.AddChild(DesktopTheme.Button("Open", () => DesktopShell.OpenFolder(target)));
            _page.AddChild(row);
        }
        Act("Show the app log", () => App.Push(new LogPage()), "The app's own log: navigation, downloads, installs and errors.");
    }
}
