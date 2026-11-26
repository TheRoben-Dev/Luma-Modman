using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Sources;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// Search one mod site for the chosen game: a search box, sort and category pickers along the
/// top, results below (icon, name, author, downloads), and "Load more" at the end. A opens a mod.
/// </summary>
public partial class SiteBrowsePage : Screen
{
    private readonly IModSource _site;
    private readonly DetectedGame _game;
    private ModQuery _query = new();
    private IReadOnlyList<ModCategory> _categories = [];
    private readonly List<ModSummary> _results = new();
    private bool _more;
    private int _searchId;
    private bool _loading;

    private ActionButton? _search;
    private Stepper? _sort;
    private Stepper? _category;
    private HBoxContainer? _toolbar;
    private VBoxContainer? _list;
    private Label? _status;

    public SiteBrowsePage(IModSource site, DetectedGame game)
    {
        _site = site;
        _game = game;
    }

    public override IEnumerable<(string Action, string Label)> Hints =>
        _site.Id == "thunderstore" ? [(Pad.Secondary, "Search")] : [(Pad.Secondary, "Search"), (Pad.Tertiary, "Site setup")];

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 14);
        col.AddChild(title);
        title.AddChild(UiTheme.Label(_site.Name, UiTheme.TitleSize));
        var sub = UiTheme.Label(_game.Title, UiTheme.FontSize, UiTheme.Dim);
        sub.VerticalAlignment = VerticalAlignment.Bottom;
        sub.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        title.AddChild(sub);

        _toolbar = new HBoxContainer();
        _toolbar.AddThemeConstantOverride("separation", 12);
        col.AddChild(_toolbar);
        _search = new ActionButton(SearchLabel(), OpenSearch, "Type what to look for.") { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f };
        _toolbar.AddChild(_search);
        _sort = new Stepper("Sort", [("Popular", "Popular"), ("Updated", "Updated"), ("New", "New")], _query.Sort.ToString(), "Order the results.")
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _sort.Changed += v =>
        {
            _query = _query with { Sort = Enum.Parse<ModSort>(v), Page = 0 };
            Search();
        };
        _toolbar.AddChild(_sort);
        _category = MakeCategoryStepper();
        _toolbar.AddChild(_category);

        _status = UiTheme.Label("", UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true);
        col.AddChild(_status);
        _list = Column(8);
        col.AddChild(Scroller(_list));
        Credit(col);
        if (_results.Count > 0)
        {
            ShowResults();
        }
        else
        {
            Search();
        }
        LoadCategories();
    }

    private Stepper MakeCategoryStepper()
    {
        var options = new List<(string, string)> { ("", "All") };
        options.AddRange(_categories.Select(c => (c.Id, c.Name)));
        var s = new Stepper("Category", options, _query.Category ?? "", "Show one kind of mod.") { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        s.Changed += v =>
        {
            _query = _query with { Category = v.Length == 0 ? null : v, Page = 0 };
            Search();
        };
        return s;
    }

    /// <summary>Each site asks to be credited; this line names where results come from.</summary>
    private void Credit(Control col)
    {
        var text = _site.Id switch
        {
            "thunderstore" => "Results from Thunderstore (thunderstore.io). Mods belong to their authors.",
            "nexus" => "Results from Nexus Mods (nexusmods.com). Mods belong to their authors.",
            "modio" => "Results from mod.io. Mods belong to their authors.",
            "curseforge" => "Results from CurseForge. Mods belong to their authors.",
            _ => "",
        };
        col.AddChild(UiTheme.Label(text, UiTheme.SmallSize - 1, UiTheme.Dim));
    }

    private string SearchLabel() => _query.Text.Length == 0 ? "Search…" : $"Search: {_query.Text}";

    private void OpenSearch() => App.Push(new TextEntryPage($"Search {_site.Name}", _query.Text, text =>
    {
        _query = _query with { Text = text, Page = 0 };
        if (_search != null)
        {
            _search.Text = SearchLabel();
        }
        Search();
    }, $"Search {_game.Title} mods by name."));

    private async void LoadCategories()
    {
        try
        {
            var cats = await _site.CategoriesAsync(_game.Profile);
            if (!IsInstanceValid(this) || cats.Count == 0 || cats.Count == _categories.Count || _toolbar == null || _category == null)
            {
                return;
            }
            _categories = cats;
            var focused = _category.HasFocus();
            var fresh = MakeCategoryStepper();
            var index = _category.GetIndex();
            _category.QueueFree();
            _toolbar.AddChild(fresh);
            _toolbar.MoveChild(fresh, index);
            _category = fresh;
            WireFocus();
            if (focused)
            {
                fresh.GrabFocus();
            }
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            // Categories are optional; search still works.
        }
    }

    private void Search()
    {
        _results.Clear();
        _more = false;
        Load();
    }

    private async void Load()
    {
        if (_list == null || _status == null)
        {
            return;
        }
        var id = ++_searchId;
        _loading = true;
        _status.Text = _query.Page == 0 ? "Searching…" : "Loading more…";
        _status.RemoveThemeColorOverride("font_color");
        if (_query.Page == 0)
        {
            Clear();
        }
        try
        {
            var page = await _site.SearchAsync(_game.Profile, _query);
            if (!IsInstanceValid(this) || id != _searchId)
            {
                return;
            }
            // Adult-only mods are left out of lists shown on a shared screen.
            _results.AddRange(page.Where(m => !m.Adult && _results.All(r => r.Id != m.Id)));
            _more = page.Count >= ModQuery.PageSize;
            ShowResults();
        }
        catch (SetupRequiredException)
        {
            if (IsInstanceValid(this) && id == _searchId)
            {
                App.Pop();
                App.Push(new SiteSetupPage(_site));
            }
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            if (IsInstanceValid(this) && id == _searchId)
            {
                _status.Text = e.Message;
                _status.AddThemeColorOverride("font_color", UiTheme.Warn);
                ShowResults(keepStatus: true);
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

    private void Clear()
    {
        if (_list == null)
        {
            return;
        }
        foreach (var c in _list.GetChildren())
        {
            _list.RemoveChild(c);
            c.QueueFree();
        }
        WireFocus();
    }

    private void ShowResults(bool keepStatus = false)
    {
        if (_list == null || _status == null)
        {
            return;
        }
        var focusIndex = -1;
        var owner = GetViewport()?.GuiGetFocusOwner();
        var existing = _list.GetChildren().OfType<Control>().ToList();
        if (owner != null && existing.Contains(owner))
        {
            focusIndex = existing.IndexOf(owner);
        }
        foreach (var c in existing)
        {
            _list.RemoveChild(c);
            c.QueueFree();
        }
        if (!keepStatus)
        {
            _status.Text = _results.Count == 0
                ? (_query.Text.Length > 0 ? $"Nothing on {_site.Name} matches \"{_query.Text}\"." : $"{_site.Name} has no mods for {_game.Title} in this category.")
                : "";
        }
        var fresh = new List<Control>();
        foreach (var m in _results)
        {
            var mod = m;
            var detail = string.Join("  ·  ", new[] { mod.Author.Length > 0 ? "by " + mod.Author : "", Short(mod.Summary) }.Where(s => s.Length > 0));
            var row = new ListRow(mod.Name, detail, Stats(mod), UiTheme.Dim, new RemoteImage(mod.IconUrl, mod.Name, new Vector2(64, 64), 10),
                () => App.Push(new ModDetailsPage(_site, _game, mod)), mod.Summary);
            _list.AddChild(row);
            fresh.Add(row);
        }
        if (_more)
        {
            var more = new ActionButton("Load more", () =>
            {
                if (!_loading)
                {
                    _query = _query with { Page = _query.Page + 1 };
                    Load();
                }
            }, "Show the next results.");
            _list.AddChild(more);
            fresh.Add(more);
        }
        WireFocus();
        if (owner == _search && fresh.Count > 0 && _query.Page == 0 && !keepStatus)
        {
            // Opened the page or just searched: land on the results.
            fresh[0].GrabFocus();
        }
        else if (focusIndex >= 0 && fresh.Count > 0)
        {
            fresh[Math.Min(focusIndex, fresh.Count - 1)].GrabFocus();
        }
        else if (owner == null || !IsInstanceValid(owner) || !owner.IsVisibleInTree())
        {
            FocusDefault();
        }
    }

    private void WireFocus()
    {
        if (_toolbar == null || _list == null)
        {
            return;
        }
        var tools = _toolbar.GetChildren().OfType<Control>().Where(c => !c.IsQueuedForDeletion()).ToList();
        var rows = _list.GetChildren().OfType<Control>().Where(c => !c.IsQueuedForDeletion()).ToList();
        Focus.Row(tools, below: rows.FirstOrDefault());
        if (rows.Count > 0)
        {
            Focus.Column(rows, above: tools[0]);
        }
    }

    private static string Short(string s)
    {
        s = s.ReplaceLineEndings(" ");
        return s.Length > 90 ? s[..90].TrimEnd() + "…" : s;
    }

    public static string Count(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}k",
        _ => n.ToString(),
    };

    private static string Stats(ModSummary m) => m.Downloads > 0 ? $"↓ {Count(m.Downloads)}" : m.Likes > 0 ? $"♥ {Count(m.Likes)}" : "";

    public override bool OnShortcut(InputEventKey key)
    {
        if (key.CtrlPressed && key.Keycode == Key.F && !key.Echo)
        {
            OpenSearch();
            return true;
        }
        return false;
    }

    public override void OnAction(string action)
    {
        if (action == Pad.Secondary)
        {
            OpenSearch();
        }
        else if (action == Pad.Tertiary && _site.Id != "thunderstore")
        {
            App.Push(new SiteSetupPage(_site));
        }
    }

    public override void FocusDefault()
    {
        if (_list?.GetChildren().OfType<Control>().FirstOrDefault(c => !c.IsQueuedForDeletion()) is { } first)
        {
            first.GrabFocus();
        }
        else
        {
            _search?.GrabFocus();
        }
    }
}
