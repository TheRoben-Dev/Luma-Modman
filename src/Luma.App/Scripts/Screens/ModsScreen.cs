using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// The Mods tab: the chosen game's setup on the left (cover, where it's installed, its loader and
/// where mods go) and its mods on the right. Luma doesn't start games: mods are linked in when the
/// game starts from Steam or the launcher, and taken out when it closes.
/// </summary>
public partial class ModsScreen : Screen, TouchScroll.IReorder
{
    private Control? _first;

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Secondary, "On / off"), (Pad.Switch, "Switch game")];

    protected override void Build()
    {
        _modRow = null;
        var game = Host.ActiveGame;
        if (game == null)
        {
            BuildNoGames();
            return;
        }
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 36);
        AddChild(row);
        row.AddChild(GamePanel(game));

        var right = Column(10);
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        row.AddChild(right);
        var items = new List<Control>();
        if (!game.Profile.Moddable)
        {
            right.AddChild(UiTheme.Label("Not moddable", UiTheme.TitleSize, UiTheme.Bad));
            right.AddChild(UiTheme.Label(game.Profile.NotModdable!, UiTheme.FontSize - 1, UiTheme.Text, wrap: true));
            right.AddChild(UiTheme.Label("Luma won't install mods for it, to keep your account safe. Pick another game to work on.",
                UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            items.Add(Add(right, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
        }
        else
        {
            BuildMods(game, right, items);
        }
        Focus.Column(items);
        Anim.Stagger(items);
        _first = _modRow != null && IsInstanceValid(_modRow) ? _modRow : items.FirstOrDefault();
    }

    private void BuildMods(DetectedGame game, VBoxContainer right, List<Control> items)
    {
        var mods = Host.Mods;
        var store = mods.Store(game);
        var state = store.State;
        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 14);
        right.AddChild(title);
        title.AddChild(UiTheme.Label("Mods", UiTheme.TitleSize));
        var on = state.Enabled().Count();
        if (state.Mods.Count > 0)
        {
            var count = UiTheme.Label($"{on} of {state.Mods.Count} on", UiTheme.FontSize, UiTheme.Dim);
            count.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            title.AddChild(count);
        }
        var deployed = mods.IsDeployed(game);
        var viaSteam = game.Install.LaunchOptions?.Contains(" run ", StringComparison.Ordinal) == true && game.Install.LaunchOptions.Contains("luma", StringComparison.OrdinalIgnoreCase);
        right.AddChild(UiTheme.Label(
            deployed ? "Mods are in the game now. Take them out to play without them."
            : viaSteam ? "Mods are linked in when the game starts from Steam and taken out when it closes."
            : state.Mods.Count == 0 ? $"No mods for {game.Title} yet. Find some in Browse: they're kept in Luma's folder, not the game's."
            : "Set up starting from Steam once, or put the mods in now.",
            UiTheme.SmallSize + 1, deployed ? UiTheme.Good : UiTheme.Dim, wrap: true));

        var scrollCol = Column(8);
        right.AddChild(Scroller(scrollCol));
        items.Add(Add(scrollCol, new ActionButton("Browse mods", () => App.SwitchTab("Browse"), $"Search mod sites for {game.Title} mods.")));
        var pending = state.Order.Count(e => state.Find(e.Id) == null);
        if (pending > 0)
        {
            var row = new ListRow($"{pending} mod{(pending == 1 ? "" : "s")} of this profile not installed yet", "From a modpack: downloading, or to get by hand. They take their place in the order when they arrive.",
                "Forget them", UiTheme.Warn, null, () => ForgetPending(game), "Takes the mods that never arrived out of this profile.");
            scrollCol.AddChild(row);
            items.Add(row);
        }
        // A Thunderstore mod that needs BepInExPack gets the loader through "needed mods" instead.
        var packNeeded = store.MissingDependencies().Any(m => m.Missing.Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase));
        if (!packNeeded && ModActions.MissingLoader(game) is { } loader)
        {
            var row = new ListRow($"Install {loader.Name}", loader.About, loader.AutoInstall ? "Get it" : "Website", game.Profile.LoaderOptional ? UiTheme.Dim : UiTheme.Warn, null,
                () => ModActions.InstallLoader(game, loader), $"{loader.Name}: {(game.Profile.LoaderOptional ? "some mods need it." : "mods for this game need it.")}");
            scrollCol.AddChild(row);
            items.Add(row);
        }
        items.Add(Add(scrollCol, new ActionButton("Import a modpack", () => App.Push(new ModPackPage(game, export: false)),
            "Installs a .lumapack into a new profile.")));
        if (state.Mods.Count > 0)
        {
            var profiles = state.Profiles.Keys.OrderBy(k => k == GameMods.DefaultProfile ? "" : k).Select(k => (k, k)).Append(("+", "New profile…"));
            var profile = new Stepper("Profile", profiles, state.Active, "Profiles are separate sets of mods and orders for the same game.");
            profile.Changed += v =>
            {
                if (v == "+")
                {
                    App.Push(new TextEntryPage("New profile", "", name =>
                    {
                        if (name.Length > 0)
                        {
                            App.Guard(() => store.UseProfile(name));
                        }
                        Rebuild();
                    }, "A copy of the current profile, to change without losing it."));
                    return;
                }
                App.Guard(() => store.UseProfile(v));
                CallDeferred(MethodName.Rebuild);
            };
            items.Add(Add(scrollCol, profile));
            if (state.Active != GameMods.DefaultProfile)
            {
                var name = state.Active;
                items.Add(Add(scrollCol, new ActionButton($"Delete the {name} profile", () => App.Confirm(
                    $"Delete the {name} profile? Its mods stay installed; only this set and order goes.", "Delete", () =>
                    {
                        App.Guard(() => store.DeleteProfile(name));
                        Host.Mods.NotifyChanged(game);
                    }), "Goes back to the Default profile.")));
            }
            var plan = Try<DeployPlan?>(() => mods.Plan(game), null, "the load order", scrollCol);
            if (plan is { Conflicts.Count: > 0 })
            {
                var n = plan.Conflicts.Count;
                var picked = plan.Conflicts.Count(c => c.Picked);
                items.Add(Add(scrollCol, new ActionButton($"Conflicts ({n} file{(n == 1 ? "" : "s")})", () => App.Push(new ConflictsPage(game)),
                    $"Files in more than one mod, and which mod's copy goes in{(picked > 0 ? $" ({picked} picked by you)" : "")}.")));
            }
            if (game.Profile.Plugins != null)
            {
                items.Add(Add(scrollCol, new ActionButton("Plugins", () => App.Push(new PluginsPage(game)),
                    "The plugin load order Luma writes to plugins.txt, with each plugin on or off.")));
            }
            items.Add(Add(scrollCol, new ActionButton("Export this profile as a modpack", () => App.Push(new ModPackPage(game, export: true)),
                "A file others can import on Linux, SteamOS or Windows: a download list, or with the mods inside.")));
            if (deployed)
            {
                items.Add(Add(scrollCol, new ActionButton("Take the mods out of the game", () => Run(() => mods.Undeploy(game), "Mods taken out."),
                    "Puts the game folder back as it was.")));
            }
            else
            {
                items.Add(Add(scrollCol, new ActionButton("Put the mods in the game now", () => Deploy(game),
                    "For starting the game from another launcher. They stay until you take them out.")));
            }
            if (game.Install.Source == GameSource.Steam && !viaSteam)
            {
                items.Add(Add(scrollCol, new ActionButton("Start with mods from Steam", () => App.Push(new SteamLaunchPage(game)),
                    "One launch option makes Steam link the mods in at every start.")));
            }
            var made = mods.OverwriteFiles(game).Count;
            if (made > 0)
            {
                var row = new ListRow($"Overwrite  ·  {made} file{(made == 1 ? "" : "s")} the game made", "Kept out of the game folder and put in last every time. Make a mod of them, or delete them.",
                    "Open", UiTheme.Accent, null, () => App.Push(new OverwritePage(game)), OverwritePage.About);
                scrollCol.AddChild(row);
                items.Add(row);
            }
            var missing = store.MissingDependencies().GroupBy(m => m.Missing.Id).Select(g => g.First()).ToList();
            if (missing.Count > 0)
            {
                var names = string.Join(", ", missing.Take(3).Select(m => m.Missing.Name)) + (missing.Count > 3 ? "…" : "");
                var fix = new ListRow($"{missing.Count} needed mod{(missing.Count == 1 ? "" : "s")} missing", names, "Get them", UiTheme.Warn, null,
                    () => GetMissing(game, missing.Select(m => m.Missing).ToList()), "Mods you turned on need these to work.");
                scrollCol.AddChild(fix);
                items.Add(fix);
            }
            var losers = plan?.Conflicts.SelectMany(c => c.Losers).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()) ?? new();

            scrollCol.AddChild(Section($"Load order  ·  {state.Active}"));
            // Find and sort, like the desktop table's filter box and column headers.
            var find = new ListRow(_filter.Length == 0 ? "Find a mod" : $"Showing mods with \"{_filter}\"", _filter.Length == 0 ? "Show only mods whose name has some text." : "A changes the text; clear it to see every mod.",
                _filter.Length == 0 ? "Find" : "Change", UiTheme.Dim, null, () => App.Push(new TextEntryPage("Find a mod", _filter, text =>
                {
                    _filter = text.Trim();
                    Rebuild();
                }, "Part of a mod's name. Leave it empty to see every mod.")), "Filters the load order by name.");
            scrollCol.AddChild(find);
            items.Add(find);
            var sort = new Stepper("Sort by", SortOptions, _sort, "Sorting only changes what you see; the load order stays as it is. Mods can be moved only while sorted by load order.");
            sort.Changed += v =>
            {
                _sort = v;
                CallDeferred(MethodName.Rebuild);
            };
            items.Add(Add(scrollCol, sort));
            var canMove = _sort == "order" && _filter.Length == 0;
            var winners = plan?.Conflicts.GroupBy(c => c.Winner).ToDictionary(g => g.Key, g => g.Count()) ?? new();
            foreach (var entry in Sorted(state, losers, winners))
            {
                var mod = state.Find(entry.Id)!;
                var detail = string.Join("  ·  ", new[] { mod.Version, SiteName(mod.Site), mod.UpdateVersion != null ? $"update {mod.UpdateVersion}" : "",
                    losers.TryGetValue(mod.Id, out var n) ? $"{n} file{(n == 1 ? "" : "s")} replaced by other mods" : "" }.Where(x => x.Length > 0));
                var id = mod.Id;
                var row = new ListRow(mod.Name, detail, entry.Enabled ? "On" : "Off", entry.Enabled ? UiTheme.Good : UiTheme.Dim, null,
                    () =>
                    {
                        _focusMod = id;
                        App.Push(new InstalledModPage(game, id));
                    }, $"{mod.Name}: A for options, Y turns it {(entry.Enabled ? "off" : "on")}. On the touchscreen, hold it to drag it.");
                row.SetMeta("mod", id);
                if (canMove)
                {
                    TouchScroll.AllowReorder(row, this);
                }
                if (id == _focusMod)
                {
                    _modRow = row;
                }
                scrollCol.AddChild(row);
                items.Add(row);
            }
        }
        if (state.Mods.Any(m => m.Site is "thunderstore" or "nexus" or "modio" or "curseforge"))
        {
            items.Add(Add(scrollCol, new ActionButton("Check for updates", () => ModActions.CheckUpdates(game), "Asks the mod sites for newer versions.")));
            if (ModActions.Updatable(game).Count is > 0 and var updates)
            {
                items.Add(Add(scrollCol, new ActionButton($"Update all ({updates})", () => ModActions.UpdateAll(game),
                    "Downloads the newest version of every mod that has one; the old versions stay for rollback.")));
            }
        }
        items.Add(Add(scrollCol, new ActionButton("Add a mod from a file", () => App.Push(new AddFilePage(game)),
            "Install a mod you downloaded yourself, such as a script extender.")));
        foreach (var link in game.Profile.Links.Where(l => l.ImportTo != null))
        {
            var l = link;
            items.Add(Add(scrollCol, new ActionButton($"Add {l.Item}s from {l.Name}", () => App.Push(new ImportPage(game, l)),
                $"Install {l.Item}s you downloaded from {l.Name}.")));
        }
        items.Add(Add(scrollCol, new ActionButton("Open a folder", () => App.Push(new ChoicePage($"Open one of {game.Title}'s folders in the file manager.",
            ("Game folder", () => Luma.App.Desktop.DesktopShell.OpenFolder(game.GameDir)),
            ("Mods folder (Luma's copy of the mods)", () => Luma.App.Desktop.DesktopShell.OpenFolder(store.Root)),
            ("Overwrite folder", () => Luma.App.Desktop.DesktopShell.OpenFolder(store.OverwriteDir)),
            ("Cancel", () => { }))), "The game's folder, its mods in Luma's folder, or Overwrite.")));
        items.Add(Add(scrollCol, new ActionButton("Switch game", App.OpenGamePicker, "Pick the game to work on.")));
        if (game.Profile.Quirks.Count > 0)
        {
            scrollCol.AddChild(Section("Good to know"));
            foreach (var q in game.Profile.Quirks)
            {
                scrollCol.AddChild(UiTheme.Label("•  " + q, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
            }
        }
    }

    public static string SiteName(string id) => id switch
    {
        "thunderstore" => "Thunderstore",
        "nexus" => "Nexus Mods",
        "modio" => "mod.io",
        "curseforge" => "CurseForge",
        "local" => "Added by hand",
        "loader" => "Mod loader",
        _ => id,
    };

    private void Deploy(DetectedGame game) => App.Busy("Putting mods in the game…", (_, _) => Task.FromResult(Host.Mods.Deploy(game)), m =>
    {
        App.Success(m.Files.Count == 1 ? "1 mod file is in the game." : $"{m.Files.Count} mod files are in the game.");
        Rebuild();
    }, cancellable: false);

    private void Run(Action act, string done)
    {
        if (App.Guard(act))
        {
            App.Success(done);
        }
        Rebuild();
    }

    /// <summary>Downloads missing dependencies from Thunderstore; other sites open the mod's page.</summary>
    internal static void GetMissing(DetectedGame game, List<Luma.Core.Sources.ModDependency> missing)
    {
        var ts = missing.Where(d => d.Site == "thunderstore").ToList();
        foreach (var d in ts)
        {
            ModActions.DownloadLatest(Host.Sites.Thunderstore, game, d.Id, d.Name);
        }
        var other = missing.Except(ts).FirstOrDefault();
        if (other != null)
        {
            App.Toast($"Find {other.Name} on {SiteName(other.Site)} in Browse.");
        }
    }

    internal static readonly (string, string)[] SortOptions =
        [("order", "Load order"), ("name", "Name"), ("site", "Site"), ("version", "Version"), ("conflicts", "Conflicts"), ("update", "Updates first"), ("off", "Off first")];

    /// <summary>The name filter and sort of the load order list (kept while the app runs).</summary>
    internal static string _filter = "";
    internal static string _sort = "order";

    private static IEnumerable<ProfileEntry> Sorted(GameMods state, Dictionary<string, int> losers, Dictionary<string, int> winners)
    {
        var rows = state.Order.Where(e => state.Find(e.Id) is { } m && (_filter.Length == 0 || m.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)));
        InstalledMod M(ProfileEntry e) => state.Find(e.Id)!;
        return _sort switch
        {
            "name" => rows.OrderBy(e => M(e).Name, StringComparer.OrdinalIgnoreCase),
            "site" => rows.OrderBy(e => SiteName(M(e).Site)).ThenBy(e => M(e).Name, StringComparer.OrdinalIgnoreCase),
            "version" => rows.OrderBy(e => M(e).Version, StringComparer.OrdinalIgnoreCase),
            "conflicts" => rows.OrderByDescending(e => losers.GetValueOrDefault(e.Id) + winners.GetValueOrDefault(e.Id)),
            "update" => rows.OrderBy(e => M(e).UpdateVersion == null ? 1 : 0),
            "off" => rows.OrderBy(e => e.Enabled ? 1 : 0),
            _ => rows,
        };
    }

    /// <summary>Y turns the focused mod on or off.</summary>
    public override bool OnShortcut(InputEventKey key)
    {
        if (_sort != "order" || _filter.Length > 0)
        {
            return false;
        }
        if (!key.CtrlPressed || key.Keycode is not (Key.Up or Key.Down) || Host.ActiveGame is not { } game
            || GetViewport().GuiGetFocusOwner() is not { } f || !f.HasMeta("mod"))
        {
            return false;
        }
        var id = (string)f.GetMeta("mod");
        App.Guard(() => Host.Mods.Store(game).Move(id, key.Keycode == Key.Up ? -1 : +1));
        _focusMod = id;
        Rebuild();
        _modRow?.GrabFocus();
        return true;
    }

    public override void OnAction(string action)
    {
        if (action != Pad.Secondary || Host.ActiveGame is not { } game || GetViewport().GuiGetFocusOwner() is not { } f || !f.HasMeta("mod"))
        {
            return;
        }
        var id = (string)f.GetMeta("mod");
        var store = Host.Mods.Store(game);
        App.Guard(() => store.SetEnabled(id, !store.State.IsEnabled(id)));
        _focusMod = id;
        Rebuild();
        _modRow?.GrabFocus();
    }

    // Touch: hold a mod row still to pick it up, drag it up or down, lift to drop it there.
    public void BeginReorder(Control row)
    {
        row.Modulate = new Color(1.25f, 1.2f, 1.45f);
        row.GrabFocus();
        Feedback.Play(Cue.Confirm);
    }

    public void MoveReorder(Control row, Vector2 at)
    {
        var parent = row.GetParent();
        foreach (var other in parent.GetChildren().OfType<Control>())
        {
            if (other != row && other.HasMeta("mod") && other.GetGlobalRect().HasPoint(new Vector2(other.GetGlobalRect().GetCenter().X, at.Y)))
            {
                parent.MoveChild(row, other.GetIndex());
                Feedback.Play(Cue.Move);
                return;
            }
        }
    }

    public void DropReorder(Control row)
    {
        row.Modulate = Colors.White;
        if (Host.ActiveGame is not { } game || !row.HasMeta("mod"))
        {
            return;
        }
        var id = (string)row.GetMeta("mod");
        var shown = row.GetParent().GetChildren().OfType<Control>().Where(c => c.HasMeta("mod")).Select(c => (string)c.GetMeta("mod")).ToList();
        var store = Host.Mods.Store(game);
        var order = store.State.Order;
        var from = order.FindIndex(e => e.Id == id);
        var p = shown.IndexOf(id);
        if (from < 0 || p < 0 || shown.Count < 2)
        {
            return;
        }
        // Right after the row now above it, or (at the top) where the row now below it was.
        int to;
        if (p > 0)
        {
            var prev = order.FindIndex(e => e.Id == shown[p - 1]);
            to = prev < from ? prev + 1 : prev;
        }
        else
        {
            var next = order.FindIndex(e => e.Id == shown[1]);
            to = next < from ? next : next - 1;
        }
        _focusMod = id;
        if (to != from)
        {
            App.Guard(() => store.Move(id, to - from));
            Host.Mods.NotifyChanged(game);
            Feedback.Play(Cue.Toggle);
        }
        Rebuild();
        _modRow?.GrabFocus();
    }

    /// <summary>The mod row to come back to after a rebuild (rows above it can appear or go).</summary>
    private string? _focusMod;
    private Control? _modRow;

    public override void _EnterTree() => Host.Mods.Changed += OnModsChanged;

    public override void _ExitTree() => Host.Mods.Changed -= OnModsChanged;

    private void OnModsChanged(string key) => Callable.From(() =>
    {
        if (IsInstanceValid(this) && IsInsideTree() && Host.ActiveGame?.Key == key)
        {
            Rebuild();
        }
    }).CallDeferred();

    private static T Add<T>(Control parent, T c) where T : Control
    {
        parent.AddChild(c);
        return c;
    }

    /// <summary>Cover, title, launcher and the facts Luma works from.</summary>
    private Control GamePanel(DetectedGame game)
    {
        var col = Column(8);
        col.SizeFlagsHorizontal = SizeFlags.Fill;
        col.CustomMinimumSize = new Vector2(330, 0);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        col.AddChild(top);
        top.AddChild(new CoverArt(game.Title, game.Install.Cover, new Vector2(120, 180), 10));
        var names = Column(4);
        names.Alignment = BoxContainer.AlignmentMode.End;
        top.AddChild(names);
        names.AddChild(UiTheme.Label(game.Title, UiTheme.FontSize + 2, UiTheme.Text, wrap: true));
        names.AddChild(UiTheme.Label(GameText.Source(game), UiTheme.SmallSize, UiTheme.Dim));
        var (status, color) = GameText.Status(game, Host.Games.Catalog);
        names.AddChild(UiTheme.Label(status, UiTheme.SmallSize, color));

        var p = game.Profile;
        col.AddChild(Section("Setup"));
        if (!p.Moddable)
        {
            Fact(col, "Runs", GameText.Runs(game));
            return col;
        }
        Fact(col, "Loader", GameText.Loader(p, Host.Games.Catalog));
        Fact(col, "Mods go to", p.ModsPathHere.Length == 0 ? "The game folder" : p.ModsPathHere.Replace("{documents}", "Documents").Replace("{appdata}", "AppData/Roaming")
            .Replace("{localappdata}", "AppData/Local").Replace("{mygames}", "Documents/My Games").Replace("{xdgdata}", "~/.local/share").Replace("{xdgconfig}", "~/.config"));
        Fact(col, "Runs", GameText.Runs(game));
        Fact(col, "Mod sites", string.Join(", ", p.Sites.Names()));
        return col;
    }

    private static void Fact(Control col, string label, string value)
    {
        var row = Column(0);
        row.AddChild(UiTheme.Label(label, UiTheme.SmallSize - 1, UiTheme.Dim));
        row.AddChild(UiTheme.Label(value, UiTheme.SmallSize + 1, UiTheme.Text, wrap: true));
        col.AddChild(row);
    }

    private void BuildNoGames()
    {
        var scanning = App.Scanning;
        var items = EmptyState.Build(this, scanning ? "Looking for games…" : "No supported games found",
            (scanning ? "Checking your launchers for games Luma knows how to mod." : $"Luma supports {Host.Games.Catalog.Games.Count} games and looked in:")
            + "\n\n" + string.Join("\n", Luma.Host.GameLibrary.Locations.Select(l => "•  " + l)),
            ("Scan again", App.ScanGames, "Look for installed games again."),
            ("Supported games", () => App.Push(new SupportedGamesPage()), "Every game Luma can mod, and the ones it can't."));
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();

    /// <summary>Takes profile entries whose mod never arrived (a modpack's) out of the active profile.</summary>
    internal static void ForgetPending(DetectedGame game)
    {
        var store = Main.Instance.Host!.Mods.Store(game);
        Main.Instance.Confirm("Take the mods that aren't installed out of this profile? If one is still downloading, it joins every profile when it arrives.", "Forget them", () =>
        {
            Main.Instance.Guard(store.ForgetMissing);
            Main.Instance.Host!.Mods.NotifyChanged(game);
        });
    }
}
