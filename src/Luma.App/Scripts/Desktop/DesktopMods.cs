using Luma.App.Screens;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Luma.Host;
using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// The Mods tab in the desktop layout: the load order as a table (on/off checkbox, name, version,
/// source, conflicts, update) with sortable columns, drag-and-drop reordering and a right-click
/// menu, warnings above it (missing loader, missing needed mods, mods in the game) and the
/// selected mod's details and actions on the right.
/// </summary>
public partial class DesktopMods : VBoxContainer
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;

    private const int ColOrder = 0, ColOn = 1, ColName = 2, ColVersion = 3, ColSource = 4, ColConflicts = 5, ColUpdate = 6;
    private static readonly string[] Titles = ["#", "On", "Name", "Version", "Source", "Conflicts", "Update"];

    private VBoxContainer _banners = null!;
    private ModTable _table = null!;
    private VBoxContainer _detail = null!;
    private Label _footer = null!;
    private Control _body = null!;
    private Label _empty = null!;
    private Button _emptyButton = null!;
    private PopupMenu _menu = null!;
    private readonly List<Action> _menuActions = new();
    private string? _selected;
    private int _sortCol = ColOrder;
    private bool _sortAsc = true;
    private bool _filling;
    private string _filter = "";

    public bool HasSelection => _selected != null && Game != null;

    public string Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            Refresh();
        }
    }

    private static DetectedGame? Game => Host.ActiveGame is { Profile.Moddable: true } g ? g : null;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        _banners = new VBoxContainer();
        _banners.AddThemeConstantOverride("separation", 4);
        AddChild(_banners);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body = split;
        AddChild(split);
        _table = new ModTable
        {
            Columns = Titles.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            AllowRmbSelect = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TooltipText = "Load order",
            FocusMode = FocusModeEnum.All,
        };
        for (var i = 0; i < Titles.Length; i++)
        {
            _table.SetColumnTitle(i, Titles[i]);
            _table.SetColumnExpand(i, i == ColName);
            _table.SetColumnClipContent(i, true);
            _table.SetColumnTitleAlignment(i, i is ColOrder or ColOn ? HorizontalAlignment.Center : HorizontalAlignment.Left);
        }
        _table.SetColumnCustomMinimumWidth(ColOrder, 44);
        _table.SetColumnCustomMinimumWidth(ColOn, 44);
        _table.SetColumnCustomMinimumWidth(ColName, 220);
        _table.SetColumnCustomMinimumWidth(ColVersion, 100);
        _table.SetColumnCustomMinimumWidth(ColSource, 120);
        _table.SetColumnCustomMinimumWidth(ColConflicts, 150);
        _table.SetColumnCustomMinimumWidth(ColUpdate, 100);
        _table.CanReorder = () => _sortCol == ColOrder && _sortAsc && _filter.Length == 0;
        _table.Dropped += MoveTo;
        _table.ItemSelected += () =>
        {
            if (!_filling)
            {
                _selected = _table.GetSelected()?.GetMetadata(0).AsString();
                FillDetail();
            }
        };
        _table.ItemEdited += OnEdited;
        _table.ItemMouseSelected += (pos, button) =>
        {
            if (button == (long)MouseButton.Right)
            {
                ShowMenu(_table.GetGlobalRect().Position + pos);
            }
        };
        _table.ItemActivated += () => Toggle(_selected);
        _table.ColumnTitleClicked += (col, button) =>
        {
            if (button != (long)MouseButton.Left)
            {
                return;
            }
            if (_sortCol == col)
            {
                _sortAsc = !_sortAsc;
            }
            else
            {
                _sortCol = (int)col;
                _sortAsc = true;
            }
            Refresh();
        };
        _table.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Echo: false } k && !k.CtrlPressed)
            {
                if (k.Keycode == Key.Space)
                {
                    Toggle(_selected);
                    _table.AcceptEvent();
                }
                else if (k.Keycode == Key.Delete)
                {
                    Uninstall(_selected);
                    _table.AcceptEvent();
                }
                else if (k.Keycode == Key.Menu)
                {
                    ShowMenu();
                    _table.AcceptEvent();
                }
            }
        };
        split.AddChild(_table);

        var side = new PanelContainer { CustomMinimumSize = new Vector2(320, 0) };
        side.AddThemeStyleboxOverride("panel", DesktopTheme.Frame(12));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(scroll);
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_detail);
        split.AddChild(side);

        _footer = DesktopTheme.Label("", DesktopTheme.Dim, DesktopTheme.SmallSize);
        AddChild(_footer);

        var emptyBox = new VBoxContainer { Alignment = AlignmentMode.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        _empty = DesktopTheme.Label("", DesktopTheme.Dim, wrap: true);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        emptyBox.AddChild(_empty);
        _emptyButton = new Button { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        emptyBox.AddChild(_emptyButton);
        emptyBox.Name = "Empty";
        AddChild(emptyBox);

        _menu = new PopupMenu();
        _menu.IdPressed += id =>
        {
            if (id >= 0 && id < _menuActions.Count)
            {
                _menuActions[(int)id]();
            }
        };
        AddChild(_menu);
        Refresh();
    }

    public void FocusDefault() => _table?.GrabFocus();

    private Action? _emptyAction;

    private void ShowEmpty(string text, string? button, Action? action)
    {
        _body.Visible = false;
        _footer.Visible = false;
        var box = (Control)_empty.GetParent();
        box.Visible = true;
        _empty.Text = text;
        _emptyButton.Visible = button != null;
        _emptyButton.Text = button ?? "";
        if (_emptyAction != null)
        {
            _emptyButton.Pressed -= _emptyAction;
        }
        _emptyAction = action;
        if (action != null)
        {
            _emptyButton.Pressed += action;
        }
    }

    /// <summary>Rebuilds the table from the store, keeping the selection and scroll position.</summary>
    public void Refresh()
    {
        if (_table == null)
        {
            return;
        }
        foreach (var c in _banners.GetChildren())
        {
            c.QueueFree();
        }
        var game = Host.ActiveGame;
        if (game == null)
        {
            ShowEmpty(App.Scanning ? "Looking for games…" : "No supported games found. Install one from Steam, Heroic or Lutris, then scan again.", "Scan again", App.ScanGames);
            return;
        }
        if (!game.Profile.Moddable)
        {
            ShowEmpty($"{game.Title} can't be modded safely.\n\n{game.Profile.NotModdable}\n\nLuma won't install mods for it, to keep your account safe.", null, null);
            return;
        }
        ((Control)_empty.GetParent()).Visible = false;
        _body.Visible = true;
        _footer.Visible = true;

        var mods = Host.Mods;
        var store = mods.Store(game);
        var state = store.State;
        Banners(game, store);

        DeployPlan? plan = null;
        try
        {
            plan = mods.Plan(game);
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            Banner($"Couldn't work out the load order: {e.Message}", DesktopTheme.Warn, null, null);
        }
        var losers = plan?.Conflicts.SelectMany(c => c.Losers).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count()) ?? new();
        var winners = plan?.Conflicts.GroupBy(c => c.Winner).ToDictionary(g => g.Key, g => g.Count()) ?? new();

        _filling = true;
        var scroll = _table.GetScroll();
        _table.Clear();
        var root = _table.CreateItem();
        var rows = state.Order.Select((e, i) => (Entry: e, Index: i, Mod: state.Find(e.Id))).Where(r => r.Mod != null).ToList();
        if (_filter.Length > 0)
        {
            rows = rows.Where(r => r.Mod!.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        IEnumerable<(ProfileEntry Entry, int Index, InstalledMod? Mod)> sorted = _sortCol switch
        {
            ColName => rows.OrderBy(r => r.Mod!.Name, StringComparer.OrdinalIgnoreCase),
            ColVersion => rows.OrderBy(r => r.Mod!.Version, StringComparer.OrdinalIgnoreCase),
            ColSource => rows.OrderBy(r => ModsScreen.SiteName(r.Mod!.Site)),
            ColConflicts => rows.OrderBy(r => losers.GetValueOrDefault(r.Mod!.Id) + winners.GetValueOrDefault(r.Mod!.Id)),
            ColUpdate => rows.OrderBy(r => r.Mod!.UpdateVersion == null ? 1 : 0),
            ColOn => rows.OrderBy(r => r.Entry.Enabled ? 1 : 0),
            _ => rows,
        };
        if (!_sortAsc)
        {
            sorted = sorted.Reverse();
        }
        TreeItem? select = null;
        foreach (var (entry, index, mod) in sorted)
        {
            var m = mod!;
            var it = _table.CreateItem(root);
            it.SetMetadata(0, m.Id);
            it.SetMetadata(1, index);
            it.SetText(ColOrder, (index + 1).ToString());
            it.SetTextAlignment(ColOrder, HorizontalAlignment.Center);
            it.SetCustomColor(ColOrder, DesktopTheme.Dim);
            it.SetCellMode(ColOn, TreeItem.TreeCellMode.Check);
            it.SetEditable(ColOn, true);
            it.SetChecked(ColOn, entry.Enabled);
            it.SetTooltipText(ColOn, entry.Enabled ? "On: goes into the game" : "Off: stays installed, left out of the game");
            it.SetText(ColName, m.Name);
            it.SetTooltipText(ColName, $"{m.Name}{(m.Author.Length > 0 ? " by " + m.Author : "")}");
            if (!entry.Enabled)
            {
                it.SetCustomColor(ColName, DesktopTheme.Dim);
            }
            it.SetText(ColVersion, m.Version);
            it.SetText(ColSource, ModsScreen.SiteName(m.Site));
            var lost = losers.GetValueOrDefault(m.Id);
            var won = winners.GetValueOrDefault(m.Id);
            if (lost + won > 0)
            {
                it.SetText(ColConflicts, string.Join(", ", new[] { won > 0 ? $"wins {won}" : "", lost > 0 ? $"loses {lost}" : "" }.Where(s => s.Length > 0)));
                it.SetCustomColor(ColConflicts, lost > 0 ? DesktopTheme.Warn : DesktopTheme.Good);
                it.SetTooltipText(ColConflicts, $"{(won > 0 ? $"Its copy wins for {won} file{(won == 1 ? "" : "s")} other mods also have. " : "")}{(lost > 0 ? $"{lost} of its file{(lost == 1 ? " is" : "s are")} replaced by other mods' copies. " : "")}Right-click → Conflicts… to pick winners.");
            }
            if (m.UpdateVersion != null)
            {
                it.SetText(ColUpdate, m.UpdateVersion);
                it.SetCustomColor(ColUpdate, DesktopTheme.Accent);
                it.SetTooltipText(ColUpdate, $"Version {m.UpdateVersion} is available.");
            }
            if (m.Id == _selected)
            {
                select = it;
            }
        }
        for (var i = 0; i < Titles.Length; i++)
        {
            _table.SetColumnTitle(i, Titles[i] + (i == _sortCol ? (_sortAsc ? "  ▲" : "  ▼") : ""));
        }
        if (select != null)
        {
            select.Select(ColName);
        }
        else
        {
            _selected = null;
        }
        _filling = false;
        Callable.From(() =>
        {
            if (IsInstanceValid(_table))
            {
                var bar = _table.GetChildren(true).OfType<VScrollBar>().FirstOrDefault();
                if (bar != null)
                {
                    bar.Value = scroll.Y;
                }
            }
        }).CallDeferred();

        var on = state.Enabled().Count();
        _footer.Text = state.Mods.Count == 0
            ? $"No mods for {game.Title} yet. Find some in Browse, or add a file you downloaded (File → Add mod from file)."
            : $"{on} of {state.Mods.Count} mods on  ·  profile {state.Active}  ·  mods lower in the list load later and win when two change the same file. Drag rows to reorder, right-click for options.";
        FillDetail();
    }

    private void Banners(DetectedGame game, ModStore store)
    {
        var mods = Host.Mods;
        if (mods.IsDeployed(game))
        {
            Banner("Mods are in the game folder now. Take them out to play without them.", DesktopTheme.Good, "Take mods out", () =>
            {
                if (App.Guard(() => mods.Undeploy(game)))
                {
                    App.Success("Mods taken out.");
                }
                mods.NotifyChanged(game);
            });
        }
        var packNeeded = store.MissingDependencies().Any(m => m.Missing.Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase));
        if (!packNeeded && ModActions.MissingLoader(game) is { } loader)
        {
            Banner($"{loader.Name} isn't installed. {loader.About}", game.Profile.LoaderOptional ? DesktopTheme.Dim : DesktopTheme.Warn,
                loader.AutoInstall ? $"Install {loader.Name}" : "Get it…", () => ModActions.InstallLoader(game, loader));
        }
        var pending = store.State.Order.Count(e => store.State.Find(e.Id) == null);
        if (pending > 0)
        {
            Banner($"{pending} mod{(pending == 1 ? "" : "s")} of this profile {(pending == 1 ? "isn't" : "aren't")} installed yet (from a modpack: downloading, or to get by hand). They take their place in the order when they arrive.",
                DesktopTheme.Warn, "Forget them", () => ModsScreen.ForgetPending(game));
        }
        var made = mods.OverwriteFiles(game).Count;
        if (made > 0)
        {
            Banner($"Overwrite: {made} file{(made == 1 ? "" : "s")} the game or a tool made while the mods were in. They're kept out of the game folder and go in last, after every mod.",
                DesktopTheme.Accent, "Overwrite…", () => App.Push(new OverwritePage(game)));
        }
        var missing = store.MissingDependencies().GroupBy(m => m.Missing.Id).Select(g => g.First()).ToList();
        if (missing.Count > 0)
        {
            var names = string.Join(", ", missing.Take(4).Select(m => m.Missing.Name)) + (missing.Count > 4 ? "…" : "");
            Banner($"{missing.Count} needed mod{(missing.Count == 1 ? " is" : "s are")} missing: {names}", DesktopTheme.Warn, "Get them",
                () => ModsScreen.GetMissing(game, missing.Select(m => m.Missing).ToList()));
        }
    }

    private void Banner(string text, Color color, string? button, Action? action)
    {
        var panel = new PanelContainer();
        var box = DesktopTheme.Frame(8);
        box.BorderWidthLeft = 4;
        box.BorderColor = color;
        panel.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var l = DesktopTheme.Label(text, null, 0, wrap: true);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        l.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(l);
        if (button != null && action != null)
        {
            row.AddChild(DesktopTheme.Button(button, action));
        }
        panel.AddChild(row);
        _banners.AddChild(panel);
    }

    // ---- selection and details ----

    public void Select(string id)
    {
        _selected = id;
        Refresh();
        _table.GrabFocus();
    }

    private void FillDetail()
    {
        foreach (var c in _detail.GetChildren())
        {
            c.QueueFree();
        }
        if (Game is not { } game)
        {
            return;
        }
        var store = Host.Mods.Store(game);
        var mod = _selected == null ? null : store.State.Find(_selected);
        if (mod == null)
        {
            _detail.AddChild(DesktopTheme.Label("Select a mod to see its details and options.", DesktopTheme.Dim, wrap: true));
            return;
        }
        var order = store.State.Order;
        var index = order.FindIndex(e => e.Id == mod.Id);
        var on = store.State.IsEnabled(mod.Id);
        var title = DesktopTheme.Label(mod.Name, null, DesktopTheme.FontSize + 4, wrap: true);
        _detail.AddChild(title);
        foreach (var (k, v) in new[]
                 {
                     ("Version", mod.Version),
                     ("Author", mod.Author),
                     ("Source", ModsScreen.SiteName(mod.Site)),
                     ("Files", mod.Files.ToString()),
                     ("Position", $"{index + 1} of {order.Count} in {store.State.Active}"),
                     ("Installed", mod.InstalledAt.ToLocalTime().ToString("d MMM yyyy HH:mm")),
                     ("Status", on ? "On" : "Off"),
                 })
        {
            if (v.Length == 0)
            {
                continue;
            }
            var row = new HBoxContainer();
            var kl = DesktopTheme.Label(k, DesktopTheme.Dim, DesktopTheme.SmallSize);
            kl.CustomMinimumSize = new Vector2(76, 0);
            row.AddChild(kl);
            var vl = DesktopTheme.Label(v, k == "Status" ? (on ? DesktopTheme.Good : DesktopTheme.Dim) : null, DesktopTheme.SmallSize, wrap: true);
            vl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(vl);
            _detail.AddChild(row);
        }
        if (mod.Dependencies.Count > 0)
        {
            _detail.AddChild(DesktopTheme.Label("Needs " + string.Join(", ", mod.Dependencies.Select(d => d.Name)), DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true));
        }
        _detail.AddChild(new HSeparator());
        foreach (var (label, action, tip) in Actions(game, mod, index, order.Count, on))
        {
            var b = DesktopTheme.Button(label, action, tip, label.StartsWith("Update to", StringComparison.Ordinal) ? "PrimaryButton" : "");
            b.Alignment = HorizontalAlignment.Left;
            _detail.AddChild(b);
        }
    }

    /// <summary>What can be done with one installed mod: the same list as the controller layout's mod page.</summary>
    private List<(string Label, Action Run, string Tip)> Actions(DetectedGame game, InstalledMod mod, int index, int count, bool on)
    {
        var store = Host.Mods.Store(game);
        var id = mod.Id;
        var list = new List<(string, Action, string)>
        {
            (on ? "Turn off" : "Turn on", () => Toggle(id), on ? "Keeps it installed but leaves it out of the game." : "Includes it the next time mods go into the game."),
        };
        if (index > 0)
        {
            list.Add(("Move up (loads earlier)", () => Move(id, -1), "Mods lower in the list win when two change the same file."));
            list.Add(("Move to the top", () => Move(id, -index), "Loads first."));
        }
        if (index < count - 1)
        {
            list.Add(("Move down (loads later, wins conflicts)", () => Move(id, +1), "Mods lower in the list win when two change the same file."));
            list.Add(("Move to the bottom", () => Move(id, count - 1 - index), "Loads last and wins every conflict."));
        }
        var site = Host.Sites.All.FirstOrDefault(s => s.Id == mod.Site);
        if (site != null && mod.ModId.Length > 0)
        {
            list.Add((mod.UpdateVersion != null ? $"Update to {mod.UpdateVersion}" : "Download the newest version",
                () => ModActions.DownloadLatest(site, game, mod.ModId, mod.Name), "Downloads it and installs it over this version; this one stays for rollback."));
        }
        if (ConflictsPage.Load(game, mod.Id).List.Count is > 0 and var conflicts)
        {
            list.Add(($"Conflicts ({conflicts} file{(conflicts == 1 ? "" : "s")})…", () => App.Push(new ConflictsPage(game, mod.Id)), "Files this mod shares with other mods, and which copy goes in."));
        }
        if (mod.Fomod != null)
        {
            list.Add(("Change install options…", () => FomodSession.Change(game, mod), "Opens the mod's installer again and reinstalls it with what you pick."));
        }
        foreach (var h in mod.History.Where(h => File.Exists(h.Archive)))
        {
            var v = h;
            list.Add(($"Roll back to {(v.Version.Length > 0 ? v.Version : "the previous version")}",
                () => App.Busy("Rolling back…", (_, _) => Task.FromResult(Host.Mods.Rollback(game, id, v)), _ =>
                {
                    App.Success($"{mod.Name} is back to {v.Version}.");
                    Host.Mods.NotifyChanged(game);
                }, cancellable: false), $"Reinstalls the download from {v.InstalledAt.ToLocalTime():d MMM}."));
        }
        if (mod.PageUrl != null)
        {
            list.Add(("Open website", () => ModActions.OpenUrl(mod.PageUrl), mod.PageUrl));
        }
        list.Add(("Open mod folder", () => DesktopShell.OpenFolder(store.ModDir(id)), "The mod's files in Luma's mod folder."));
        list.Add(("Uninstall…", () => Uninstall(id), "Removes it from Luma's mod folder. Its download is kept."));
        return list;
    }

    private void ShowMenu(Vector2? at = null)
    {
        if (Game is not { } game || _selected == null || Host.Mods.Store(game).State.Find(_selected) is not { } mod)
        {
            return;
        }
        var state = Host.Mods.Store(game).State;
        var index = state.Order.FindIndex(e => e.Id == mod.Id);
        _menu.Clear();
        _menuActions.Clear();
        var group = "";
        foreach (var (label, run, _) in Actions(game, mod, index, state.Order.Count, state.IsEnabled(mod.Id)))
        {
            var g = label.StartsWith("Open", StringComparison.Ordinal) ? "open" : label == "Uninstall…" ? "remove" : label.StartsWith("Move", StringComparison.Ordinal) ? "move" : "main";
            var newGroup = g != group && _menuActions.Count > 0;
            group = g;
            if (newGroup)
            {
                _menu.AddSeparator();
                _menuActions.Add(() => { });
            }
            _menu.AddItem(label, _menuActions.Count);
            _menuActions.Add(run);
        }
        PopupAtMouse(_menu, _table, at);
    }

    internal int RowCount => _table.GetRoot()?.GetChildCount() ?? 0;

    private TreeItem? Row(string name)
    {
        for (var it = _table.GetRoot()?.GetFirstChild(); it != null; it = it.GetNext())
        {
            if (it.GetText(ColName).StartsWith(name, StringComparison.Ordinal))
            {
                return it;
            }
        }
        return null;
    }

    /// <summary>Selects a row by mod name (smoke test).</summary>
    internal bool SelectByName(string name)
    {
        if (Row(name) is not { } it)
        {
            return false;
        }
        it.Select(ColName);
        _table.GrabFocus();
        return true;
    }

    /// <summary>Centre of a row in viewport coordinates (smoke test clicks).</summary>
    internal Vector2? RowCenter(string name) => Row(name) is { } it ? _table.GetGlobalRect().Position + _table.GetItemAreaRect(it).GetCenter() : null;

    /// <summary>Opens a context menu at the mouse (or at the selected row, from the keyboard).</summary>
    internal static void PopupAtMouse(PopupMenu menu, Tree table, Vector2? clicked = null)
    {
        var at = clicked ?? table.GetViewport().GetMousePosition();
        if (!table.GetGlobalRect().HasPoint(at) && table.GetSelected() is { } sel)
        {
            at = table.GetGlobalRect().Position + table.GetItemAreaRect(sel).GetCenter();
        }
        menu.ResetSize();
        menu.Popup(new Rect2I((Vector2I)at, new Vector2I(Math.Max(1, (int)menu.GetContentsMinimumSize().X), 1)));
    }

    /// <summary>True when a menu with the mod's options is open (smoke test).</summary>
    public bool MenuOpen => _menu.Visible;

    // ---- changes ----

    private void Changed(DetectedGame game) => Host.Mods.NotifyChanged(game);

    private void OnEdited()
    {
        if (_filling || Game is not { } game || _table.GetEdited() is not { } it || _table.GetEditedColumn() != ColOn)
        {
            return;
        }
        var id = it.GetMetadata(0).AsString();
        var on = it.IsChecked(ColOn);
        _selected = id;
        App.Guard(() => Host.Mods.Store(game).SetEnabled(id, on));
        Changed(game);
    }

    public void Toggle(string? id)
    {
        if (id == null || Game is not { } game)
        {
            return;
        }
        var store = Host.Mods.Store(game);
        _selected = id;
        App.Guard(() => store.SetEnabled(id, !store.State.IsEnabled(id)));
        Changed(game);
    }

    private void Move(string id, int delta)
    {
        if (Game is not { } game)
        {
            return;
        }
        _selected = id;
        App.Guard(() => Host.Mods.Store(game).Move(id, delta));
        Changed(game);
    }

    /// <summary>Ctrl+Up / Ctrl+Down on the selected mod.</summary>
    public bool MoveSelected(int delta)
    {
        if (_selected == null || Game == null)
        {
            return false;
        }
        Move(_selected, delta);
        return true;
    }

    private void MoveTo(string id, int index)
    {
        if (Game is not { } game)
        {
            return;
        }
        var order = Host.Mods.Store(game).State.Order;
        var from = order.FindIndex(e => e.Id == id);
        if (from < 0)
        {
            return;
        }
        var to = index > from ? index - 1 : index;
        if (to != from)
        {
            Move(id, to - from);
        }
    }

    private void Uninstall(string? id)
    {
        if (id == null || Game is not { } game || Host.Mods.Store(game).State.Find(id) is not { } mod)
        {
            return;
        }
        App.Confirm($"Uninstall {mod.Name}? Its download is kept, so it can be installed again.", "Uninstall", () =>
        {
            if (App.Guard(() => Host.Mods.Uninstall(game, id)))
            {
                App.Success($"{mod.Name} uninstalled.");
            }
            _selected = null;
            Changed(game);
        });
    }
}
