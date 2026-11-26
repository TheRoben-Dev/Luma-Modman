using Luma.App.Screens;
using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Luma.Host;
using Godot;

namespace Luma.App.Desktop;

/// <summary>Menu bar and toolbar. Menus are filled each time they open, so they always match the game and its mods.</summary>
public partial class DesktopShell
{
    private Button _deployButton = null!;
    private Button _steamButton = null!;
    private Button _pluginsButton = null!;
    private Button _updatesButton = null!;
    private Button _addFileButton = null!;
    private OptionButton _profiles = null!;
    private Button _deleteProfile = null!;
    private Button _newProfile = null!;
    private bool _fillingProfiles;

    private sealed record MenuItem(string Label, Action? Run, string Shortcut = "", bool Enabled = true, bool? Checked = null);

    private static readonly MenuItem Separator = new("-", null);

    private Control BuildMenus()
    {
        var bar = new MenuBar { Flat = true, PreferGlobalMenu = false };
        AddMenu(bar, "File", FileMenu);
        AddMenu(bar, "Game", GameMenu);
        AddMenu(bar, "Mods", ModsMenu);
        AddMenu(bar, "View", ViewMenu);
        AddMenu(bar, "Help", HelpMenu);
        return bar;
    }

    private static void AddMenu(MenuBar bar, string title, Func<IEnumerable<MenuItem>> items)
    {
        var menu = new PopupMenu { Name = title };
        var actions = new List<Action?>();
        menu.AboutToPopup += () =>
        {
            menu.Clear();
            actions.Clear();
            foreach (var item in items())
            {
                if (item == Separator)
                {
                    menu.AddSeparator();
                    actions.Add(null);
                    continue;
                }
                var label = item.Shortcut.Length > 0 ? item.Label : item.Label;
                if (item.Checked is { } on)
                {
                    menu.AddCheckItem(label, actions.Count);
                    menu.SetItemChecked(menu.ItemCount - 1, on);
                }
                else
                {
                    menu.AddItem(label, actions.Count);
                }
                if (item.Shortcut.Length > 0)
                {
                    // Shown on the right like a desktop menu; the keys themselves are handled by OnShortcut.
                    menu.SetItemShortcut(menu.ItemCount - 1, new Shortcut { Events = [ShortcutEvent(item.Shortcut)] }, global: false);
                    menu.SetItemShortcutDisabled(menu.ItemCount - 1, true);
                }
                menu.SetItemDisabled(menu.ItemCount - 1, !item.Enabled);
                actions.Add(item.Run);
            }
        };
        menu.IdPressed += id =>
        {
            if (id >= 0 && id < actions.Count && actions[(int)id] is { } run)
            {
                run();
            }
        };
        bar.AddChild(menu);
    }

    /// <summary>"Ctrl+O" → the key event shown next to the menu item.</summary>
    private static InputEventKey ShortcutEvent(string text)
    {
        var parts = text.Split('+');
        var key = parts[^1] switch
        {
            "Up" => Key.Up,
            "Down" => Key.Down,
            "Del" => Key.Delete,
            "F5" => Key.F5,
            var k => (Key)k[0],
        };
        return new InputEventKey { Keycode = key, CtrlPressed = parts.Contains("Ctrl") };
    }

    private IEnumerable<MenuItem> FileMenu()
    {
        var game = Host.ActiveGame;
        var moddable = game?.Profile.Moddable == true;
        yield return new MenuItem("Add mod from file…", AddModFromFile, "Ctrl+O", moddable);
        if (game != null && moddable)
        {
            foreach (var link in game.Profile.Links.Where(l => l.ImportTo != null))
            {
                var l = link;
                yield return new MenuItem($"Add {l.Item}s from {l.Name}…", () => App.Push(new ImportPage(game, l)));
            }
        }
        yield return Separator;
        yield return new MenuItem("Import modpack…", () => App.Push(new ModPackPage(game, export: false)), "Ctrl+I");
        yield return new MenuItem("Export modpack…", ExportModpack, "Ctrl+E", moddable && Host.Mods.Store(game!).State.Order.Count > 0);
        yield return Separator;
        yield return new MenuItem("Open mods folder", () => OpenFolder(game != null && moddable ? Host.Mods.Store(game).Root : Host.Paths.ModsDir));
        yield return new MenuItem("Open downloads folder", () => OpenFolder(Host.Paths.DownloadsDir));
        yield return new MenuItem("Open settings folder", () => OpenFolder(Host.Paths.Config));
        yield return Separator;
        yield return new MenuItem("Quit", () => GetTree().Quit(), "Ctrl+Q");
    }

    private IEnumerable<MenuItem> GameMenu()
    {
        var game = Host.ActiveGame;
        var moddable = game?.Profile.Moddable == true;
        yield return new MenuItem("Scan for games", App.ScanGames, "Ctrl+R", !App.Scanning);
        yield return new MenuItem($"Supported games ({Host.Games.Catalog.Games.Count})…", () => App.Push(new SupportedGamesPage()));
        yield return Separator;
        var deployed = game != null && moddable && Host.Mods.IsDeployed(game);
        yield return new MenuItem(deployed ? "Take the mods out of the game" : "Put the mods in the game now", () => ToggleDeploy(), "", moddable && game != null);
        yield return new MenuItem("Start with mods from Steam…", () => App.Push(new SteamLaunchPage(game!)), "",
            moddable && game!.Install.Source == GameSource.Steam);
        yield return Separator;
        yield return new MenuItem("Open game folder", () => OpenFolder(game?.GameDir), "", game != null);
    }

    private IEnumerable<MenuItem> ModsMenu()
    {
        var game = Host.ActiveGame;
        if (game is not { Profile.Moddable: true })
        {
            yield return new MenuItem("Pick a game that can be modded", null, "", false);
            yield break;
        }
        var store = Host.Mods.Store(game);
        yield return new MenuItem("Check for updates", () => ModActions.CheckUpdates(game), "Ctrl+U", store.State.Mods.Any(m => m.Site is "thunderstore" or "nexus" or "modio" or "curseforge"));
        var missing = store.MissingDependencies().GroupBy(m => m.Missing.Id).Select(g => g.First().Missing).ToList();
        yield return new MenuItem(missing.Count > 0 ? $"Get {missing.Count} missing needed mod{(missing.Count == 1 ? "" : "s")}" : "No needed mods missing",
            () => ModsScreen.GetMissing(game, missing), "", missing.Count > 0);
        if (ModActions.MissingLoader(game) is { } loader)
        {
            yield return new MenuItem($"Install {loader.Name}", () => ModActions.InstallLoader(game, loader));
        }
        yield return Separator;
        yield return new MenuItem("Move selected mod up", () => _mods.MoveSelected(-1), "Ctrl+Up", _mods.HasSelection);
        yield return new MenuItem("Move selected mod down", () => _mods.MoveSelected(+1), "Ctrl+Down", _mods.HasSelection);
        if (game.Profile.Plugins != null)
        {
            yield return new MenuItem("Plugins…", ShowPlugins, "Ctrl+P");
        }
        yield return Separator;
        yield return new MenuItem("New profile…", NewProfile);
        yield return new MenuItem($"Delete profile \"{store.State.Active}\"…", DeleteProfile, "", store.State.Active != GameMods.DefaultProfile);
    }

    private IEnumerable<MenuItem> ViewMenu()
    {
        for (var i = 0; i < Main.TabNames.Length; i++)
        {
            var name = Main.TabNames[i];
            yield return new MenuItem(name, () => SelectTab(name), $"Ctrl+{i + 1}", true, CurrentTab == name);
        }
        yield return Separator;
        yield return new MenuItem("App log", ShowLog);
        yield return Separator;
        yield return new MenuItem("Switch to the controller layout…", () => App.ApplyLayout(UiLayout.Controller));
    }

    private IEnumerable<MenuItem> HelpMenu()
    {
        yield return new MenuItem("Changelog", App.ShowChangelog);
        yield return new MenuItem("Open the app log file", OpenLogFile);
        yield return new MenuItem("Check for Luma updates", () => App.CheckForAppUpdate(silent: false), "", Host.Updater.CanSelfUpdate);
        yield return Separator;
        yield return new MenuItem("Keyboard shortcuts", () => App.Push(new ChoicePage(string.Join("\n", DesktopSettings.Shortcuts), ("Close", () => { }))));
        yield return new MenuItem($"About Luma {Luma.Host.Updates.AppUpdater.DisplayVersion(Host.Updater.CurrentVersion)}", () => App.Push(new ChoicePage(
            $"Luma {Luma.Host.Updates.AppUpdater.DisplayVersion(Host.Updater.CurrentVersion)}\n\nA mod manager for SteamOS, Bazzite and Windows. Mods come only from the sites you connect, never games, DRM bypasses or pirated content.",
            ("Close", () => { }))));
    }

    internal static void OpenFolder(string? path)
    {
        if (path == null)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        if (OS.ShellOpen(path) != Error.Ok)
        {
            Main.Instance.Toast($"Couldn't open {path}");
        }
    }

    // ---- toolbar ----

    private Control BuildToolbar()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        _deployButton = DesktopTheme.Button("Put mods in game", ToggleDeploy, "Links the enabled mods into the game folder now, for starting it from another launcher.", "ToolButton");
        _steamButton = DesktopTheme.Button("Steam launch option", () =>
        {
            if (Host.ActiveGame is { } g)
            {
                App.Push(new SteamLaunchPage(g));
            }
        }, "One launch option makes Steam link the mods in at every start and take them out after.", "ToolButton");
        _updatesButton = DesktopTheme.Button("Check for updates", () =>
        {
            if (Host.ActiveGame is { } g)
            {
                ModActions.CheckUpdates(g);
            }
        }, "Asks the mod sites for newer versions (Ctrl+U).", "ToolButton");
        _addFileButton = DesktopTheme.Button("Add from file…", AddModFromFile, "Install a mod you downloaded yourself, such as a script extender (Ctrl+O).", "ToolButton");
        row.AddChild(_deployButton);
        row.AddChild(_steamButton);
        row.AddChild(new VSeparator());
        row.AddChild(_updatesButton);
        row.AddChild(_addFileButton);
        row.AddChild(DesktopTheme.Button("Browse mods", () => SelectTab("Browse"), "Search the mod sites that carry this game (Ctrl+2).", "ToolButton"));
        _pluginsButton = DesktopTheme.Button("Plugins…", ShowPlugins, "The plugin load order Luma writes to plugins.txt, with each plugin on or off (Ctrl+P).", "ToolButton");
        row.AddChild(_pluginsButton);
        row.AddChild(new VSeparator());
        row.AddChild(DesktopTheme.Label("Profile", DesktopTheme.Dim, DesktopTheme.SmallSize));
        _profiles = new OptionButton { CustomMinimumSize = new Vector2(170, 0), TooltipText = "Profiles are separate sets of mods and orders for the same game." };
        _profiles.ItemSelected += i =>
        {
            if (_fillingProfiles || Host.ActiveGame is not { } g)
            {
                return;
            }
            var name = _profiles.GetItemText((int)i);
            App.Guard(() => Host.Mods.Store(g).UseProfile(name));
            Host.Mods.NotifyChanged(g);
        };
        row.AddChild(_profiles);
        _newProfile = DesktopTheme.Button("New…", NewProfile, "A copy of the current profile, to change without losing it.", "ToolButton");
        _deleteProfile = DesktopTheme.Button("Delete", DeleteProfile, "Deletes this profile. Its mods stay installed.", "ToolButton");
        row.AddChild(_newProfile);
        row.AddChild(_deleteProfile);
        row.AddChild(Spacer());
        row.AddChild(DesktopTheme.Label("Filter", DesktopTheme.Dim, DesktopTheme.SmallSize));
        var filter = new LineEdit { PlaceholderText = "Filter mods by name", CustomMinimumSize = new Vector2(220, 0), ClearButtonEnabled = true, TooltipText = "Shows only the mods whose name contains this." };
        filter.TextChanged += t =>
        {
            _mods.Filter = t;
            SelectTab("Mods");
        };
        row.AddChild(filter);
        return row;
    }

    private void RefreshToolbar()
    {
        var game = Host.ActiveGame;
        var moddable = game?.Profile.Moddable == true;
        foreach (var b in new[] { _deployButton, _steamButton, _updatesButton, _addFileButton, _newProfile, _deleteProfile })
        {
            b.Disabled = !moddable;
        }
        _profiles.Disabled = !moddable;
        _pluginsButton.Visible = moddable && game!.Profile.Plugins != null;
        _fillingProfiles = true;
        _profiles.Clear();
        if (game != null && moddable)
        {
            var state = Host.Mods.Store(game).State;
            var deployed = Host.Mods.IsDeployed(game);
            _deployButton.Text = deployed ? "Take mods out of game" : "Put mods in game";
            _deployButton.TooltipText = deployed ? "Puts the game folder back as it was." : "Links the enabled mods into the game folder now, for starting it from another launcher.";
            _steamButton.Disabled = game.Install.Source != GameSource.Steam;
            _pluginsButton.Visible = game.Profile.Plugins != null;
            _updatesButton.Disabled = !state.Mods.Any(m => m.Site is "thunderstore" or "nexus" or "modio" or "curseforge");
            foreach (var k in state.Profiles.Keys.OrderBy(k => k == GameMods.DefaultProfile ? "" : k))
            {
                _profiles.AddItem(k);
                if (k == state.Active)
                {
                    _profiles.Select(_profiles.ItemCount - 1);
                }
            }
            _deleteProfile.Disabled = state.Active == GameMods.DefaultProfile;
        }
        _fillingProfiles = false;
    }

    internal void ShowPlugins()
    {
        if (Host.ActiveGame is { Profile.Plugins: not null } game)
        {
            App.Push(new PluginsPage(game));
        }
    }

    private void ToggleDeploy()
    {
        if (Host.ActiveGame is not { Profile.Moddable: true } game)
        {
            return;
        }
        if (Host.Mods.IsDeployed(game))
        {
            if (App.Guard(() => Host.Mods.Undeploy(game)))
            {
                App.Success("Mods taken out.");
            }
            Host.Mods.NotifyChanged(game);
            return;
        }
        App.Busy("Putting mods in the game…", (_, _) => Task.FromResult(Host.Mods.Deploy(game)), m =>
        {
            App.Success(m.Files.Count == 1 ? "1 mod file is in the game." : $"{m.Files.Count} mod files are in the game.");
            Host.Mods.NotifyChanged(game);
        }, cancellable: false);
    }

    private void NewProfile()
    {
        if (Host.ActiveGame is not { Profile.Moddable: true } game)
        {
            return;
        }
        App.Push(new TextEntryPage("New profile", "", name =>
        {
            if (name.Length > 0)
            {
                App.Guard(() => Host.Mods.Store(game).UseProfile(name));
                Host.Mods.NotifyChanged(game);
            }
        }, "A copy of the current profile, to change without losing it."));
    }

    private void DeleteProfile()
    {
        if (Host.ActiveGame is not { Profile.Moddable: true } game)
        {
            return;
        }
        var store = Host.Mods.Store(game);
        var name = store.State.Active;
        if (name == GameMods.DefaultProfile)
        {
            return;
        }
        App.Confirm($"Delete the {name} profile? Its mods stay installed; only this set and order goes.", "Delete", () =>
        {
            App.Guard(() => store.DeleteProfile(name));
            Host.Mods.NotifyChanged(game);
        });
    }
}
