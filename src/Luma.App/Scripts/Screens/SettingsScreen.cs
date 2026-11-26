using Luma.Host;
using Luma.Host.Updates;
using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>Settings in two panes (see <see cref="TwoPaneScreen"/>).</summary>
public partial class SettingsScreen : TwoPaneScreen
{
    private static readonly (string Name, string Summary)[] CategoryList =
    [
        ("Games", "Finding installed games, and the supported list."),
        ("Mod sites", "Your keys for Nexus Mods, CurseForge and mod.io."),
        ("Appearance", "Theme, accent colour, CRT effect and animations."),
        ("Sound & feel", "Menu sounds and controller rumble."),
        ("Layout", "Controller or desktop layout, and keyboard shortcuts."),
        ("Updates", "Version, changelog and updates."),
        ("About", "Where Luma keeps its files, and the app log."),
    ];

    protected override string Heading => "Settings";

    protected override IReadOnlyList<(string Name, string Summary)> Categories => CategoryList;

    protected override void BuildCategory(string name, VBoxContainer col, Action<Control> Add)
    {
        switch (name)
        {
            case "Games":
                BuildGames(col, Add);
                break;
            case "Mod sites":
                BuildSites(col, Add);
                break;
            case "Appearance":
                BuildAppearance(col, Add);
                break;
            case "Sound & feel":
                BuildFeedback(col, Add);
                break;
            case "Layout":
                BuildLayout(col, Add);
                break;
            case "Updates":
                BuildUpdates(col, Add);
                break;
            case "About":
                BuildAbout(col, Add);
                break;
        }
    }

    private string? _pendingTheme;
    private string? _pendingBase;
    private Dictionary<string, string>? _pendingAccents;

    private void BuildGames(VBoxContainer col, Action<Control> Add)
    {
        var lib = Host.Games;
        var when = lib.ScannedAt is { } t ? t.ToLocalTime().ToString("g") : "never";
        col.AddChild(UiTheme.Label($"{lib.Games.Count} supported game{(lib.Games.Count == 1 ? "" : "s")} installed. Last scan: {when}.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        Add(new ActionButton("Scan again", App.ScanGames, "Look for installed games again: Steam (every library, SD cards too), Heroic and Lutris, native and Flatpak."));
        Add(new ActionButton($"Supported games ({lib.Catalog.Games.Count})", () => App.Push(new SupportedGamesPage()), "Every game Luma can mod, and the ones it can't."));
        col.AddChild(UiTheme.Label("Add or fix a game without waiting for an update: put a profile (.json, same format as Luma's own) in "
                                   + System.IO.Path.Combine(Host.Paths.Config, "games") + ". Yours win over the built-in ones.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        foreach (var p in lib.Catalog.Problems)
        {
            col.AddChild(UiTheme.Label("Skipped profile: " + p, UiTheme.SmallSize, UiTheme.Warn, wrap: true));
        }
    }

    private void BuildSites(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(UiTheme.Label("Thunderstore needs no account. The other sites ask each player for their own free key; it stays on this device and is only sent to that site.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        foreach (var site in Host.Sites.All.Where(SiteKeys.NeedsKey))
        {
            var s = site;
            var ready = SiteKeys.CurrentKey(s) != null;
            Add(new ListRow(s.Name, SiteKeys.Status(s), ready ? "Change" : "Set up", ready ? UiTheme.Good : UiTheme.Warn, null,
                () => App.Push(new SiteSetupPage(s)), $"{s.Name}: {SiteKeys.Status(s)}"));
        }
    }

    private void BuildLayout(VBoxContainer col, Action<Control> Add)
    {
        var current = Host.Settings.Layout;
        var layout = new Stepper("Layout", UiLayout.Choices.Select(c => (c.Id, c.Label)), UiLayout.Choices.Any(c => c.Id == current) ? current : UiLayout.Auto,
            "Controller: full screen, big rows. Desktop: a separate mod manager window with menus, tables and right-click. Changing it restarts Luma.")
        {
            Expands = true,
            DetailFor = UiLayout.Describe,
        };
        layout.Changed += App.ApplyLayout;
        Add(layout);
        col.AddChild(Section("Keyboard and mouse"));
        foreach (var line in new[]
        {
            "Ctrl+1 to Ctrl+4: Mods, Browse, Downloads, Settings.",
            "Ctrl+F: search the mod site you're browsing.",
            "Ctrl+Up / Ctrl+Down: move the selected mod in the load order.",
            "R: turn the selected mod on or off.  G: switch game.  Tab: next tab.  Esc: back.",
        })
        {
            col.AddChild(UiTheme.Label("•  " + line, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
    }

    private void BuildFeedback(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(UiTheme.Label("Each theme has its own set of sounds. In games, only the game's own sound and rumble play.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var levels = new List<(string, string)> { ("0", "Off"), ("25", "Quiet"), ("50", "Medium"), ("75", "Loud"), ("100", "Full") };
        var current = Host.Settings.SoundVolume;
        var nearest = levels.OrderBy(l => Math.Abs(int.Parse(l.Item1) - current)).First().Item1;
        var sound = new Stepper("Menu sounds", levels, nearest, "Short sounds when you move, press, go back or switch tabs.")
        {
            Expands = true,
            DetailFor = v => v == "0" ? "Silent menus." : "Plays through your speakers or headphones at this level.",
        };
        sound.Changed += v =>
        {
            Host.SaveSettings(Host.Settings with { SoundVolume = int.Parse(v) });
            App.ApplyFeedback(Host.Settings.SoundVolume, Host.Settings.Haptics);
            Luma.App.Ui.Feedback.Play(Luma.App.Ui.Cue.Confirm);  // a sample at the new level
        };
        Add(sound);
        Add(Toggle.Create("Controller rumble", Host.Settings.Haptics, on =>
        {
            Host.SaveSettings(Host.Settings with { Haptics = on });
            App.ApplyFeedback(Host.Settings.SoundVolume, on);
            Luma.App.Ui.Feedback.Play(Luma.App.Ui.Cue.Confirm);
        }, "A light buzz in your hands for menu moves, presses and errors. On a Steam Deck it uses the trackpad haptics when Steam Input passes rumble through.",
            "Rumbles on menu actions.", "No rumble in the menus."));
    }

    private void BuildAppearance(VBoxContainer col, Action<Control> Add)
    {
        // Theme and accent are picked here and applied by restarting the app: switching them live
        // left the library unable to open games until the next start.
        var active = UiTheme.Name;
        if (_pendingBase != active)
        {
            _pendingBase = active;
            _pendingTheme = null;
            _pendingAccents = null;
        }
        _pendingTheme ??= active;
        _pendingAccents ??= new Dictionary<string, string>(Host.Settings.Accents);
        var themeId = _pendingTheme;
        var theme = new Stepper("Theme", UiTheme.Themes.Select(t => (t.Id, t.Label + (t.Id == active ? "  (current)" : ""))), themeId,
            "Pick a theme, then choose Restart to apply. The app restarts and comes back here.")
        {
            Expands = true,
            DetailFor = v => UiTheme.Themes.FirstOrDefault(t => t.Id == v)?.Blurb,
        };
        theme.Changed += v =>
        {
            _pendingTheme = v;
            RefreshPane();  // the accent row's choices depend on the theme
        };
        Add(theme);
        var pocket = themeId == "pocket";
        var accent = new Stepper(pocket ? "Screen colour" : "Accent colour",
            [("", pocket ? "Classic green" : "Theme's own"), .. UiTheme.SwatchesFor(UiTheme.Themes.First(t => t.Id == themeId))],
            _pendingAccents.GetValueOrDefault(themeId) ?? "",
            pocket ? "Pocket LCD's four shades, in another screen colour. Saved for this theme." : "The highlight colour for this theme. Each theme remembers its own.")
        {
            Expands = true,
            DetailFor = v => v == ""
                ? (pocket ? "The original pea-green screen." : "The colour this theme was designed with.")
                : (pocket ? "All four shades change together, so it still looks like one screen." : "Focus, selected values and highlights use this colour."),
        };
        accent.Changed += v =>
        {
            if (v == "")
            {
                _pendingAccents.Remove(themeId);
            }
            else
            {
                _pendingAccents[themeId] = v;
            }
            RefreshPane();
        };
        Add(accent);
        var changed = themeId != active ||
                      (_pendingAccents.GetValueOrDefault(themeId) ?? "") != (Host.Settings.Accents.GetValueOrDefault(themeId) ?? "");
        if (changed)
        {
            var label = UiTheme.Themes.First(t => t.Id == themeId).Label;
            Add(new ActionButton($"↻  Restart to apply {label}", () =>
            {
                Host.SaveSettings(Host.Settings with { Theme = themeId, Accents = new Dictionary<string, string>(_pendingAccents) });
                App.RestartApp("--open=appearance");
            }, "Saves the theme and restarts the app. You come back to this page."));
        }
        Add(Toggle.Create("Animated background", Host.Settings.AnimatedBackground, on =>
        {
            Host.SaveSettings(Host.Settings with { AnimatedBackground = on });
            App.RefreshBackground();
        }, "A slow animation behind the menus that matches the theme. It stops while a game runs.",
            "Each theme has its own: drifting lights, a pulsing grid, phosphor rain, pixel clouds, a scrolling floor, an LCD shimmer or spinning shapes.",
            "A plain background. Uses a little less battery."));
        var crt = new Stepper("CRT effect", [("off", "Off"), ("1", "Subtle"), ("2", "Medium"), ("3", "Strong")],
            Host.Settings.Crt ? Host.Settings.CrtLevel.ToString() : "off",
            "Makes the whole screen look like an old TV. Works with every theme.")
        {
            Expands = true,
            DetailFor = v => v switch
            {
                "off" => "No effect. Retro terminal still has its own light scanlines.",
                "1" => "Faint scanlines, a slight wobble and a gentle tube curve.",
                "2" => "Scanlines, a wobbling picture, a rolling band, colour fringes and dark edges.",
                _ => "The full old-TV look. Text gets a little softer.",
            },
        };
        crt.Changed += v =>
        {
            Host.SaveSettings(v == "off" ? Host.Settings with { Crt = false } : Host.Settings with { Crt = true, CrtLevel = int.Parse(v) });
            App.RefreshBackground();
        };
        Add(crt);
        Add(Toggle.Create("Animations", !Host.Settings.ReduceMotion, on =>
        {
            Host.SaveSettings(Host.Settings with { ReduceMotion = !on });
            Anim.Enabled = on;
        }, "Sliding focus, smooth scrolling and page transitions.", null,
            "Everything changes instantly, nothing slides or fades."));
    }

    private void BuildUpdates(VBoxContainer col, Action<Control> Add)
    {
        var updater = Host.Updater;
        col.AddChild(UiTheme.Label($"Luma {AppUpdater.DisplayVersion(updater.CurrentVersion)}", UiTheme.FontSize + 6, UiTheme.Text));
        Add(new ActionButton("Changelog", App.ShowChangelog, "What changed in each version of Luma."));
        if (updater.CanSelfUpdate)
        {
            Add(new ActionButton("Check for updates", () => App.CheckForAppUpdate(silent: false),
                "Looks for a newer Luma on GitHub. Updates install in place and keep your mods, profiles and settings."));
            Add(Toggle.Create("Check at startup", Host.Settings.CheckForUpdates,
                on => Host.SaveSettings(Host.Settings with { CheckForUpdates = on }),
                "Asks when a new version is out. Never interrupts a game."));
            if (updater.CanRollback)
            {
                var prev = updater.PreviousVersion is { } pv ? $" ({pv})" : "";
                Add(new ActionButton($"Roll back to previous version{prev}", () => App.Confirm(
                    $"Go back to the previous Luma{prev}? The app restarts.", "Roll back", App.RollbackApp)));
            }
        }
        if (updater.CanSelfUpdate)
        {
            var dir = updater.InstallDir;
            var added = Try(() => SteamSetup.IsAdded(Host.Paths.Home, dir), false, "Steam's shortcuts", col);
            Add(new ActionButton(added ? "Add Luma to Steam again (refresh art)" : "Add Luma to Steam", () => AddToSteam(dir),
                $"A Steam shortcut with Luma's own art, so it opens from {GameText.SteamTvMode}."));
        }
        else
        {
            col.AddChild(UiTheme.Label("This copy can't update itself (development build or package). Use the installer to update.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        }
        col.AddChild(UiTheme.Label("Luma installs mods only from the sites you connect, never games, DRM bypasses or pirated content.", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
    }

    internal static void AddToSteam(string dir)
    {
        var home = Host.Paths.Home;
        if (!Luma.Core.Steam.SteamShortcuts.SteamRunning(home))
        {
            if (App.Guard(() => SteamSetup.Add(home, dir)))
            {
                App.Success("Luma is in your Steam library.");
            }
            return;
        }
        if (SteamSetup.InGameMode)
        {
            App.Push(new ChoicePage("Steam reads shortcuts only when it starts, and it can't be closed from Game Mode. Switch to Desktop Mode and add Luma from there, or run the installer again.",
                ("OK", () => { })));
            return;
        }
        App.Push(new ChoicePage("Steam reads shortcuts only when it starts. Luma can close Steam, add itself, and start Steam again.",
            ("Close Steam and add Luma", () => App.Busy("Adding Luma to Steam…", (_, _) => Task.Run(() =>
            {
                if (!SteamSetup.CloseSteam(home, TimeSpan.FromSeconds(30)))
                {
                    throw new InvalidOperationException("Steam didn't close. Exit it yourself (Steam → Exit), then try again.");
                }
                var n = SteamSetup.Add(home, dir);
                SteamSetup.StartSteam();
                return n;
            }), _ => App.Success("Luma is in your Steam library."), cancellable: false)),
            ("Cancel", () => { })));
    }

    private void BuildAbout(VBoxContainer col, Action<Control> Add)
    {
        col.AddChild(UiTheme.Label("Luma keeps mods, downloads and backups in its own folders. Game folders only see mods while a game runs, through links Luma puts in at launch and takes out afterwards, so every change can be undone.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        foreach (var (label, path) in new[]
        {
            ("Mods", Host.Paths.ModsDir),
            ("Downloads", Host.Paths.DownloadsDir),
            ("Settings", Host.Paths.Config),
            ("App log", AppLog.FilePath ?? "(not open)"),
        })
        {
            col.AddChild(UiTheme.Label($"{label}:  {path}", UiTheme.SmallSize + 1, UiTheme.Text, wrap: true));
        }
        Add(new ActionButton("Open the app log", () => App.Push(new LogPage()), "The app's own log: navigation, downloads, installs and errors."));
    }
}
