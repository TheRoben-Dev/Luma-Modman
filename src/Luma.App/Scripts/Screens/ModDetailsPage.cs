using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Sources;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// One mod: picture, author and numbers on the left with Download, other files and the website;
/// the description, screenshots and what it needs on the right.
/// </summary>
public partial class ModDetailsPage : Screen
{
    private readonly IModSource _site;
    private readonly DetectedGame _game;
    private readonly ModSummary _mod;
    private ModDetails? _details;
    private string? _error;
    private bool _requested;
    private Control? _first;

    public ModDetailsPage(IModSource site, DetectedGame game, ModSummary mod)
    {
        _site = site;
        _game = game;
        _mod = mod;
    }

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Secondary, "Open website")];

    protected override void Build()
    {
        var mod = _details?.Mod ?? _mod;
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 34);
        AddChild(row);

        var left = Column(8);
        left.SizeFlagsHorizontal = SizeFlags.Fill;
        left.CustomMinimumSize = new Vector2(340, 0);
        row.AddChild(left);
        left.AddChild(new RemoteImage(mod.IconUrl, mod.Name, new Vector2(340, 190), 14));
        left.AddChild(UiTheme.Label(mod.Name, UiTheme.FontSize + 4, UiTheme.Text, wrap: true));
        var by = new List<string>();
        if (mod.Author.Length > 0)
        {
            by.Add("by " + mod.Author);
        }
        by.Add("on " + _site.Name);
        left.AddChild(UiTheme.Label(string.Join("  ", by), UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var stats = new List<string>();
        if (mod.Downloads > 0)
        {
            stats.Add($"↓ {SiteBrowsePage.Count(mod.Downloads)} downloads");
        }
        if (mod.Likes > 0)
        {
            stats.Add($"♥ {SiteBrowsePage.Count(mod.Likes)}");
        }
        if (mod.Updated is { } u)
        {
            stats.Add("updated " + u.ToLocalTime().ToString("d MMM yyyy"));
        }
        if (stats.Count > 0)
        {
            left.AddChild(UiTheme.Label(string.Join("  ·  ", stats), UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        }
        var buttons = new List<Control>();
        var main = _details?.Files.FirstOrDefault(f => f.Kind is "" or "Main") ?? _details?.Files.FirstOrDefault();
        if (_details != null)
        {
            if (main != null)
            {
                var label = main.Version.Length > 0 ? $"Download {main.Version}" : "Download";
                if (main.Size > 0)
                {
                    label += $"  ·  {Bytes(main.Size)}";
                }
                buttons.Add(Add(left, new ActionButton(label, () => ModActions.Download(_site, _game, mod, main), $"Download {main.Name}.")));
            }
            if (_details.Files.Count > 1)
            {
                buttons.Add(Add(left, new ActionButton($"Other files ({_details.Files.Count})", ChooseFile, "Optional files, updates and older versions.")));
            }
        }
        if (mod.PageUrl != null)
        {
            buttons.Add(Add(left, new ActionButton("Open website", () => ModActions.OpenUrl(mod.PageUrl), "The mod's page, with its full description and comments.")));
        }
        Focus.Column(buttons);

        var right = Column(10);
        right.SizeFlagsVertical = SizeFlags.ExpandFill;
        row.AddChild(Scroller(right));
        var cards = new List<Control>();
        if (_details == null)
        {
            right.AddChild(UiTheme.Label(_error ?? "Loading…", UiTheme.SmallSize + 1, _error == null ? UiTheme.Dim : UiTheme.Warn, wrap: true));
            if (_error != null)
            {
                var again = new ActionButton("Try again", () =>
                {
                    _error = null;
                    _requested = false;
                    Rebuild();
                });
                right.AddChild(again);
                cards.Add(again);
            }
        }
        else
        {
            if (_details.Dependencies.Count > 0)
            {
                right.AddChild(Section("Needs"));
                foreach (var d in _details.Dependencies)
                {
                    var dep = d;
                    var r = new ListRow(dep.Name, dep.Version != null ? $"version {dep.Version} or newer" : "", null, null, null, () => OpenDependency(dep),
                        $"{mod.Name} needs {dep.Name}.");
                    right.AddChild(r);
                    cards.Add(r);
                }
            }
            right.AddChild(Section("About"));
            var text = _details.Description.Length > 0 ? _details.Description : mod.Summary;
            foreach (var chunk in Chunks(text))
            {
                var card = Card(UiTheme.Label(chunk, UiTheme.SmallSize + 1, UiTheme.Text, wrap: true));
                right.AddChild(card);
                cards.Add(card);
            }
            if (_details.Screenshots.Count > 0)
            {
                right.AddChild(Section("Pictures"));
                foreach (var shot in _details.Screenshots.Take(8))
                {
                    var card = Card(new RemoteImage(shot, mod.Name, new Vector2(0, 300), 10) { SizeFlagsHorizontal = SizeFlags.ExpandFill });
                    right.AddChild(card);
                    cards.Add(card);
                }
            }
        }
        if (cards.Count > 0)
        {
            Focus.Column(cards, left: buttons.FirstOrDefault());
            foreach (var b in buttons)
            {
                b.FocusNeighborRight = cards[0].GetPath();
            }
        }
        _first = buttons.FirstOrDefault() ?? cards.FirstOrDefault();
        if (!_requested)
        {
            _requested = true;
            Load();
        }
    }

    private static PanelContainer Card(Control content)
    {
        var card = new PanelContainer { FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var box = UiTheme.Box(UiTheme.Panel, 0, 12);
        box.ContentMarginLeft = box.ContentMarginRight = 16;
        box.ContentMarginTop = box.ContentMarginBottom = 12;
        card.AddThemeStyleboxOverride("panel", box);
        card.AddChild(content);
        return card;
    }

    /// <summary>Paragraphs grouped into cards of readable size, so the pad scrolls through them.</summary>
    private static IEnumerable<string> Chunks(string text)
    {
        var current = "";
        foreach (var para in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + para.Length > 700)
            {
                yield return current;
                current = "";
            }
            current = current.Length == 0 ? para : current + "\n\n" + para;
        }
        if (current.Length > 0)
        {
            yield return current;
        }
    }

    private async void Load()
    {
        try
        {
            var d = await _site.DetailsAsync(_game.Profile, _mod.Id);
            if (!IsInstanceValid(this))
            {
                return;
            }
            _details = d;
        }
        catch (Exception e) when (Main.IsUserFacing(e))
        {
            if (!IsInstanceValid(this))
            {
                return;
            }
            _error = e.Message;
        }
        Rebuild();
    }

    private void ChooseFile()
    {
        if (_details == null)
        {
            return;
        }
        var mod = _details.Mod;
        var options = _details.Files.Take(12).Select(f =>
        {
            var file = f;
            var label = string.Join("  ·  ", new[] { f.Kind, f.Name, f.Version, f.Size > 0 ? Bytes(f.Size) : "" }.Where(s => s.Length > 0).Distinct());
            return (label, (Action)(() => ModActions.Download(_site, _game, mod, file)));
        }).Append(("Cancel", () => { })).ToArray();
        App.Push(new ChoicePage($"Which file of {mod.Name}?", options));
    }

    private void OpenDependency(ModDependency dep)
    {
        if (dep.Site != _site.Id)
        {
            App.Toast($"{dep.Name} comes from another site.");
            return;
        }
        App.Push(new ModDetailsPage(_site, _game, new ModSummary { Site = dep.Site, Id = dep.Id, Name = dep.Name }));
    }

    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    private static T Add<T>(Control parent, T c) where T : Control
    {
        parent.AddChild(c);
        return c;
    }

    public override void OnAction(string action)
    {
        if (action == Pad.Secondary)
        {
            ModActions.OpenUrl((_details?.Mod ?? _mod).PageUrl);
        }
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
