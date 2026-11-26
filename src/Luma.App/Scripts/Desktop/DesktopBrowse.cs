using Luma.App.Screens;
using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Sources;
using Luma.Host;
using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// The Browse tab in the desktop layout: site, search box, sort and category along the top,
/// results as a table, and the selected mod's page (files, what it needs, description and
/// pictures) on the right. Sites that need the player's key show their setup first.
/// </summary>
public partial class DesktopBrowse : VBoxContainer
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;

    private OptionButton _site = null!;
    private LineEdit _search = null!;
    private OptionButton _sort = null!;
    private OptionButton _category = null!;
    private Button _account = null!;
    private Label _status = null!;
    private Tree _results = null!;
    private Button _more = null!;
    private VBoxContainer _detail = null!;
    private ScrollContainer _detailScroll = null!;
    private Label _credit = null!;
    private HBoxContainer _links = null!;
    private Control _main = null!;
    private VBoxContainer _setup = null!;

    private List<IModSource> _sites = new();
    private IModSource? _current;
    private DetectedGame? _game;
    private ModQuery _query = new();
    private readonly List<ModSummary> _rows = new();
    private IReadOnlyList<ModCategory> _categories = [];
    private int _searchId;
    private int _detailId;
    private bool _loading;
    private bool _dirty = true;
    private ModSummary? _shown;
    private ModDetails? _details;
    private bool _fillingResults;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        AddChild(bar);
        bar.AddChild(DesktopTheme.Label("Site", DesktopTheme.Dim, DesktopTheme.SmallSize));
        _site = new OptionButton { CustomMinimumSize = new Vector2(150, 0), TooltipText = "The mod site to search." };
        _site.ItemSelected += i => UseSite(_sites[(int)i]);
        bar.AddChild(_site);
        _search = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true, TooltipText = "Search by name (Ctrl+F). Enter searches." };
        _search.TextSubmitted += _ => Search();
        bar.AddChild(_search);
        bar.AddChild(DesktopTheme.Button("Search", Search, "Search the site."));
        bar.AddChild(DesktopTheme.Label("Sort", DesktopTheme.Dim, DesktopTheme.SmallSize));
        _sort = new OptionButton { TooltipText = "Order the results." };
        foreach (var s in new[] { "Popular", "Updated", "New" })
        {
            _sort.AddItem(s);
        }
        _sort.ItemSelected += i =>
        {
            _query = _query with { Sort = Enum.Parse<ModSort>(_sort.GetItemText((int)i)), Page = 0 };
            Search();
        };
        bar.AddChild(_sort);
        bar.AddChild(DesktopTheme.Label("Category", DesktopTheme.Dim, DesktopTheme.SmallSize));
        _category = new OptionButton { CustomMinimumSize = new Vector2(150, 0), TooltipText = "Show one kind of mod." };
        _category.ItemSelected += i =>
        {
            var id = _category.GetItemMetadata((int)i).AsString();
            _query = _query with { Category = id.Length == 0 ? null : id, Page = 0 };
            Search();
        };
        bar.AddChild(_category);
        _account = DesktopTheme.Button("Account…", () =>
        {
            if (_current != null)
            {
                App.Push(new SiteSetupPage(_current));
            }
        }, "Your key for this site.");
        bar.AddChild(_account);

        _status = DesktopTheme.Label("", DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true);
        AddChild(_status);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _main = split;
        AddChild(split);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", 4);
        split.AddChild(left);
        _results = new Tree
        {
            Columns = 4,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TooltipText = "Search results",
        };
        string[] titles = ["Name", "Author", "Downloads", "Updated"];
        for (var i = 0; i < titles.Length; i++)
        {
            _results.SetColumnTitle(i, titles[i]);
            _results.SetColumnExpand(i, i == 0);
            _results.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            _results.SetColumnClipContent(i, true);
        }
        _results.SetColumnCustomMinimumWidth(0, 240);
        _results.SetColumnCustomMinimumWidth(1, 140);
        _results.SetColumnCustomMinimumWidth(2, 90);
        _results.SetColumnCustomMinimumWidth(3, 100);
        _results.ItemSelected += () =>
        {
            if (!_fillingResults && _results.GetSelected()?.GetMetadata(0).AsInt32() is { } i && i >= 0 && i < _rows.Count)
            {
                ShowMod(_current!, _rows[i]);
            }
        };
        _results.ItemActivated += DownloadMain;
        left.AddChild(_results);
        var foot = new HBoxContainer();
        _more = DesktopTheme.Button("Load more", () =>
        {
            if (!_loading)
            {
                _query = _query with { Page = _query.Page + 1 };
                Load();
            }
        }, "Show the next results.");
        foot.AddChild(_more);
        foot.AddChild(DesktopShell.Spacer());
        _credit = DesktopTheme.Label("", DesktopTheme.Dim, DesktopTheme.SmallSize);
        foot.AddChild(_credit);
        left.AddChild(foot);

        var side = new PanelContainer { CustomMinimumSize = new Vector2(380, 0) };
        side.AddThemeStyleboxOverride("panel", DesktopTheme.Frame(12));
        _detailScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(_detailScroll);
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 6);
        _detailScroll.AddChild(_detail);
        split.AddChild(side);

        _setup = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false, Alignment = AlignmentMode.Center };
        _setup.AddThemeConstantOverride("separation", 10);
        AddChild(_setup);

        _links = new HBoxContainer();
        _links.AddThemeConstantOverride("separation", 10);
        AddChild(_links);
        GameChanged();
    }

    /// <summary>Searches when the tab is first shown for a game (not before: the sites are online).</summary>
    public void Shown()
    {
        if (_dirty)
        {
            _dirty = false;
            Search();
        }
    }

    public void FocusSearch()
    {
        _search.GrabFocus();
        _search.SelectAll();
    }

    /// <summary>The active game (or a site key) changed: fills the site list again.</summary>
    public void GameChanged()
    {
        if (_site == null)
        {
            return;
        }
        var game = Host.ActiveGame;
        var sameGame = game?.Key == _game?.Key;
        _game = game;
        _site.Clear();
        foreach (var c in _links.GetChildren())
        {
            c.QueueFree();
        }
        _sites = game is { Profile.Moddable: true } ? Host.Sites.For(game.Profile).ToList() : [];
        if (game == null || !game.Profile.Moddable)
        {
            _main.Visible = false;
            _setup.Visible = true;
            ClearSetup();
            _setup.AddChild(Centered(DesktopTheme.Label(game == null ? "Pick a game first: Luma shows the sites that carry it." : $"{game.Title} can't be modded safely: {game.Profile.NotModdable}", DesktopTheme.Dim, wrap: true)));
            _status.Text = "";
            return;
        }
        foreach (var s in _sites)
        {
            var label = s.Name + (s.Ready ? "" : "  (set up)");
            _site.AddItem(label);
        }
        if (game.Profile.Links.Count > 0)
        {
            _links.AddChild(DesktopTheme.Label("Community sites:", DesktopTheme.Dim, DesktopTheme.SmallSize));
            foreach (var l in game.Profile.Links)
            {
                var link = l;
                var b = new LinkButton { Text = link.Name, TooltipText = $"{link.About}\n{link.Url}", Underline = LinkButton.UnderlineMode.OnHover };
                b.AddThemeColorOverride("font_color", DesktopTheme.Accent);
                b.Pressed += () => ModActions.OpenUrl(link.Url);
                _links.AddChild(b);
            }
        }
        var keep = sameGame && _current != null ? _sites.FindIndex(s => s.Id == _current.Id) : -1;
        var pick = keep >= 0 ? keep : Math.Max(0, _sites.FindIndex(s => s.Ready));
        if (_sites.Count > 0)
        {
            _site.Select(pick);
            UseSite(_sites[pick], keepResults: keep >= 0 && _current!.Ready);
        }
    }

    private static CenterContainer Centered(Control c)
    {
        var center = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        c.CustomMinimumSize = new Vector2(520, 0);
        center.AddChild(c);
        return center;
    }

    private void ClearSetup()
    {
        foreach (var c in _setup.GetChildren())
        {
            c.QueueFree();
        }
    }

    private void UseSite(IModSource site, bool keepResults = false)
    {
        var changed = site != _current;
        _current = site;
        _account.Visible = SiteKeys.NeedsKey(site);
        _search.PlaceholderText = $"Search {site.Name} for {_game?.Title} mods";
        _credit.Text = site.Id switch
        {
            "thunderstore" => "Results from Thunderstore (thunderstore.io). Mods belong to their authors.",
            "nexus" => "Results from Nexus Mods (nexusmods.com). Mods belong to their authors.",
            "modio" => "Results from mod.io. Mods belong to their authors.",
            "curseforge" => "Results from CurseForge. Mods belong to their authors.",
            _ => "",
        };
        if (!site.Ready)
        {
            ShowSetup(site);
            return;
        }
        _main.Visible = true;
        _setup.Visible = false;
        if (keepResults && !changed)
        {
            return;
        }
        _query = new ModQuery { Sort = _query.Sort, Text = _search.Text.Trim() };
        _categories = [];
        FillCategories();
        LoadCategories(site);
        ShowMod(site, null);
        _dirty = true;
        if (IsVisibleInTree())
        {
            _dirty = false;
            Search();
        }
    }

    private void ShowSetup(IModSource site)
    {
        _main.Visible = false;
        _setup.Visible = true;
        ClearSetup();
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        col.AddChild(DesktopTheme.Label($"{site.Name} needs your own free key", null, DesktopTheme.FontSize + 4));
        col.AddChild(DesktopTheme.Label(SiteKeys.For(site).Why, DesktopTheme.Dim, wrap: true));
        var b = DesktopTheme.Button($"Set up {site.Name}…", () => App.Push(new SiteSetupPage(site)), "Takes a minute: copy your key from the site and paste it here.", "PrimaryButton");
        b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        col.AddChild(b);
        _setup.AddChild(Centered(col));
        _status.Text = "";
    }

    private void FillCategories()
    {
        _category.Clear();
        _category.AddItem("All");
        _category.SetItemMetadata(0, "");
        foreach (var c in _categories)
        {
            _category.AddItem(c.Name);
            _category.SetItemMetadata(_category.ItemCount - 1, c.Id);
            if (c.Id == _query.Category)
            {
                _category.Select(_category.ItemCount - 1);
            }
        }
    }

    private async void LoadCategories(IModSource site)
    {
        if (_game == null)
        {
            return;
        }
        try
        {
            var cats = await site.CategoriesAsync(_game.Profile);
            if (IsInstanceValid(this) && site == _current && cats.Count > 0)
            {
                _categories = cats;
                FillCategories();
            }
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            // Categories are optional; search still works.
        }
    }

    private void Search()
    {
        if (_current == null || !_current.Ready)
        {
            return;
        }
        _query = _query with { Text = _search.Text.Trim(), Page = 0 };
        _rows.Clear();
        Load();
    }

    private async void Load()
    {
        if (_current is not { } site || _game is not { } game)
        {
            return;
        }
        var id = ++_searchId;
        _loading = true;
        _more.Disabled = true;
        _status.Text = _query.Page == 0 ? $"Searching {site.Name}…" : "Loading more…";
        _status.AddThemeColorOverride("font_color", DesktopTheme.Dim);
        if (_query.Page == 0)
        {
            _results.Clear();
        }
        try
        {
            var page = await site.SearchAsync(game.Profile, _query);
            if (!IsInstanceValid(this) || id != _searchId)
            {
                return;
            }
            // Adult-only mods are left out, as in the controller layout.
            _rows.AddRange(page.Where(m => !m.Adult && _rows.All(r => r.Id != m.Id)));
            _more.Disabled = page.Count < ModQuery.PageSize;
            _status.Text = _rows.Count == 0
                ? (_query.Text.Length > 0 ? $"Nothing on {site.Name} matches \"{_query.Text}\"." : $"{site.Name} has no mods for {game.Title} in this category.")
                : $"{_rows.Count} mod{(_rows.Count == 1 ? "" : "s")} on {site.Name}{(_query.Text.Length > 0 ? $" matching \"{_query.Text}\"" : "")}. Double-click a row to download it.";
            FillResults();
        }
        catch (SetupRequiredException)
        {
            if (IsInstanceValid(this) && id == _searchId)
            {
                ShowSetup(site);
            }
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            if (IsInstanceValid(this) && id == _searchId)
            {
                _status.Text = e.Message;
                _status.AddThemeColorOverride("font_color", DesktopTheme.Warn);
                FillResults();
            }
        }
        finally
        {
            if (id == _searchId)
            {
                _loading = false;
            }
        }
    }

    private void FillResults()
    {
        var selected = _shown?.Id;
        _fillingResults = true;
        _results.Clear();
        var root = _results.CreateItem();
        for (var i = 0; i < _rows.Count; i++)
        {
            var m = _rows[i];
            var it = _results.CreateItem(root);
            it.SetMetadata(0, i);
            it.SetText(0, m.Name);
            it.SetTooltipText(0, m.Summary.Length > 0 ? $"{m.Name}\n{m.Summary}" : m.Name);
            it.SetText(1, m.Author);
            it.SetText(2, m.Downloads > 0 ? SiteBrowsePage.Count(m.Downloads) : m.Likes > 0 ? $"♥ {SiteBrowsePage.Count(m.Likes)}" : "");
            it.SetTextAlignment(2, HorizontalAlignment.Right);
            it.SetText(3, m.Updated is { } u ? u.ToLocalTime().ToString("d MMM yyyy") : "");
            it.SetCustomColor(1, DesktopTheme.Dim);
            it.SetCustomColor(3, DesktopTheme.Dim);
            if (m.Id == selected)
            {
                it.Select(0);
            }
        }
        _fillingResults = false;
    }

    /// <summary>Shows a mod's page on the right (null clears it).</summary>
    public async void ShowMod(IModSource site, ModSummary? mod)
    {
        _shown = mod;
        _details = null;
        var id = ++_detailId;
        foreach (var c in _detail.GetChildren())
        {
            c.QueueFree();
        }
        if (mod == null || _game is not { } game)
        {
            _detail.AddChild(DesktopTheme.Label("Select a mod to see its page.", DesktopTheme.Dim, wrap: true));
            return;
        }
        if (site != _current && _sites.IndexOf(site) is var idx and >= 0)
        {
            _site.Select(idx);
            UseSite(site, keepResults: true);
        }
        FillDetail(site, mod, null, null);
        try
        {
            var d = await site.DetailsAsync(game.Profile, mod.Id);
            if (IsInstanceValid(this) && id == _detailId)
            {
                _details = d;
                FillDetail(site, d.Mod, d, null);
            }
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            if (IsInstanceValid(this) && id == _detailId)
            {
                FillDetail(site, mod, null, e.Message);
            }
        }
    }

    private void FillDetail(IModSource site, ModSummary mod, ModDetails? d, string? error)
    {
        foreach (var c in _detail.GetChildren())
        {
            c.QueueFree();
        }
        var game = _game!;
        _detail.AddChild(new RemoteImage(mod.IconUrl, mod.Name, new Vector2(356, 200), 6));
        _detail.AddChild(DesktopTheme.Label(mod.Name, null, DesktopTheme.FontSize + 4, wrap: true));
        var by = string.Join("  ", new[] { mod.Author.Length > 0 ? "by " + mod.Author : "", "on " + site.Name }.Where(s => s.Length > 0));
        _detail.AddChild(DesktopTheme.Label(by, DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true));
        var stats = new List<string>();
        if (mod.Downloads > 0)
        {
            stats.Add($"{SiteBrowsePage.Count(mod.Downloads)} downloads");
        }
        if (mod.Likes > 0)
        {
            stats.Add($"{SiteBrowsePage.Count(mod.Likes)} likes");
        }
        if (mod.Updated is { } u)
        {
            stats.Add("updated " + u.ToLocalTime().ToString("d MMM yyyy"));
        }
        if (stats.Count > 0)
        {
            _detail.AddChild(DesktopTheme.Label(string.Join("  ·  ", stats), DesktopTheme.Dim, DesktopTheme.SmallSize, wrap: true));
        }
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _detail.AddChild(buttons);
        if (d == null)
        {
            _detail.AddChild(DesktopTheme.Label(error ?? "Loading…", error == null ? DesktopTheme.Dim : DesktopTheme.Warn, wrap: true));
            if (error != null)
            {
                buttons.AddChild(DesktopTheme.Button("Try again", () => ShowMod(site, mod)));
            }
        }
        var main = d?.Files.FirstOrDefault(f => f.Kind is "" or "Main") ?? d?.Files.FirstOrDefault();
        if (d != null && main != null)
        {
            var label = main.Version.Length > 0 ? $"Download {main.Version}" : "Download";
            if (main.Size > 0)
            {
                label += $"  ·  {ModDetailsPage.Bytes(main.Size)}";
            }
            buttons.AddChild(DesktopTheme.Button(label, () => ModActions.Download(site, game, d.Mod, main, d.Dependencies), $"Download {main.Name}.", "PrimaryButton"));
        }
        if (mod.PageUrl != null)
        {
            buttons.AddChild(DesktopTheme.Button("Open website", () => ModActions.OpenUrl(mod.PageUrl), "The mod's page, with its full description and comments."));
        }
        if (d == null)
        {
            return;
        }
        if (d.Files.Count > 1)
        {
            _detail.AddChild(Title("FILES"));
            var files = new Tree { Columns = 4, HideRoot = true, ColumnTitlesVisible = true, SelectMode = Tree.SelectModeEnum.Row, CustomMinimumSize = new Vector2(0, Math.Min(220, 34 + 26 * Math.Min(d.Files.Count, 12))), TooltipText = "Files" };
            string[] t = ["File", "Version", "Size", "Kind"];
            for (var i = 0; i < t.Length; i++)
            {
                files.SetColumnTitle(i, t[i]);
                files.SetColumnExpand(i, i == 0);
                files.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
                files.SetColumnClipContent(i, true);
            }
            files.SetColumnCustomMinimumWidth(1, 70);
            files.SetColumnCustomMinimumWidth(2, 70);
            files.SetColumnCustomMinimumWidth(3, 70);
            var root = files.CreateItem();
            for (var i = 0; i < Math.Min(d.Files.Count, 40); i++)
            {
                var f = d.Files[i];
                var it = files.CreateItem(root);
                it.SetMetadata(0, i);
                it.SetText(0, f.Name);
                it.SetTooltipText(0, f.FileName);
                it.SetText(1, f.Version);
                it.SetText(2, f.Size > 0 ? ModDetailsPage.Bytes(f.Size) : "");
                it.SetText(3, f.Kind);
            }
            void DownloadSelected()
            {
                if (files.GetSelected()?.GetMetadata(0).AsInt32() is { } i)
                {
                    ModActions.Download(site, game, d.Mod, d.Files[i], d.Dependencies);
                }
            }
            files.ItemActivated += DownloadSelected;
            _detail.AddChild(files);
            var pick = DesktopTheme.Button("Download selected file", DownloadSelected, "Optional files, updates and older versions.");
            pick.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            _detail.AddChild(pick);
        }
        if (d.Dependencies.Count > 0)
        {
            _detail.AddChild(Title("NEEDS"));
            foreach (var dep in d.Dependencies)
            {
                var x = dep;
                var b = new LinkButton { Text = dep.Name + (dep.Version != null ? $"  ({dep.Version} or newer)" : ""), Underline = LinkButton.UnderlineMode.OnHover, TooltipText = $"{mod.Name} needs {dep.Name}." };
                b.AddThemeColorOverride("font_color", DesktopTheme.Accent);
                b.Pressed += () =>
                {
                    if (x.Site != site.Id)
                    {
                        App.Toast($"{x.Name} comes from another site.");
                        return;
                    }
                    ShowMod(site, new ModSummary { Site = x.Site, Id = x.Id, Name = x.Name });
                };
                _detail.AddChild(b);
            }
        }
        _detail.AddChild(Title("ABOUT"));
        var about = new RichTextLabel { FitContent = true, SelectionEnabled = true, BbcodeEnabled = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        about.AddText(d.Description.Length > 0 ? d.Description : mod.Summary);
        _detail.AddChild(about);
        foreach (var shot in d.Screenshots.Take(4))
        {
            _detail.AddChild(new RemoteImage(shot, mod.Name, new Vector2(356, 200), 6));
        }
        _detailScroll.ScrollVertical = 0;
    }

    private static Label Title(string text)
    {
        var l = DesktopTheme.Label(text, DesktopTheme.Dim, DesktopTheme.SmallSize);
        l.CustomMinimumSize = new Vector2(0, 26);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }

    /// <summary>Double-click on a result: the newest main file.</summary>
    private void DownloadMain()
    {
        if (_current is { } site && _game is { } game && _results.GetSelected()?.GetMetadata(0).AsInt32() is { } i && i >= 0 && i < _rows.Count)
        {
            ModActions.DownloadLatest(site, game, _rows[i].Id, _rows[i].Name);
        }
    }

    /// <summary>The Download button of the mod shown (smoke test).</summary>
    public Button? DownloadButton => _detail.GetChildren().OfType<HBoxContainer>().SelectMany(h => h.GetChildren().OfType<Button>()).FirstOrDefault(b => b.Text.StartsWith("Download", StringComparison.Ordinal));

    public int ResultCount => _rows.Count;

    /// <summary>Selects a result row (smoke test).</summary>
    public void SelectResult(int index)
    {
        var item = _results.GetRoot()?.GetChild(index);
        if (item != null)
        {
            item.Select(0);
            _results.ScrollToItem(item);
        }
    }
}
