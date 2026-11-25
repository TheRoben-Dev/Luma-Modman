using Luma.App.Ui;
using Godot;

namespace Luma.App.Screens;

/// <summary>The mod sites Luma can browse, as tiles. Each opens its own browser (search, categories, details).</summary>
public partial class BrowseScreen : Screen
{
    /// <summary>Site id, name, monogram and what it covers.</summary>
    public static readonly (string Id, string Name, string Mono, string About)[] Sites =
    [
        ("thunderstore", "Thunderstore", "TS", "Community mods for Unity and other games, mostly with BepInEx. No account needed."),
        ("modio", "mod.io", "MI", "Mods published through mod.io. Needs a free mod.io account for some games."),
        ("curseforge", "CurseForge", "CF", "Mods for many PC games. Needs a CurseForge API key."),
        ("nexus", "Nexus Mods", "NX", "The biggest mod site. Needs a Nexus account; Premium allows one-tap downloads."),
    ];

    private Control? _first;

    protected override void Build()
    {
        var col = Column(14);
        AddChild(col);
        col.AddChild(UiTheme.Label("Mod sites", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label("Pick a site to search its mods. Luma follows each site's rules and credits its authors.",
            UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        var pad = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
        {
            pad.AddThemeConstantOverride($"margin_{side}", 8);
        }
        pad.AddChild(row);
        col.AddChild(pad);
        var tiles = new List<Control>();
        foreach (var site in Sites)
        {
            var s = site;
            var tile = Tiles.Card(s.Name, s.Mono, null, $"{s.Name}  ·  {s.About}", () =>
                App.Toast($"{s.Name} browsing arrives in the next update."));
            row.AddChild(tile);
            tiles.Add(tile);
        }
        Focus.Row(tiles);
        Anim.Stagger(tiles);
        _first = tiles.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
