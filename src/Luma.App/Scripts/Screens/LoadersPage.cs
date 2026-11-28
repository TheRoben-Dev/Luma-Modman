using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>
/// The game's mod loaders and script extenders (BepInEx, UE4SS, SKSE64, RED4ext, ArchiveXL...): whether
/// each is installed, its version, which mods need it, and Install / Update / Remove. Luma installs
/// the ones mods need by itself; this page is where to see and manage them.
/// The desktop layout shows the same as a dialog (Mods → Mod loaders…).
/// </summary>
public partial class LoadersPage : Screen
{
    private readonly DetectedGame _game;
    private Control? _first;

    internal DetectedGame Game => _game;

    public LoadersPage(DetectedGame game) => _game = game;

    internal const string About = "Mod loaders and script extenders let mods hook into the game. Luma installs the ones your mods need by itself, from their official releases, and starts the game through them.";

    /// <summary>The state of one loader in a word, its colour, and a line about it.</summary>
    internal static (string State, Color Color, string Detail) Describe(LoaderStatus s)
    {
        var l = s.Loader;
        var needed = s.NeededBy.Count switch
        {
            0 => "",
            1 => $"Needed by {s.NeededBy[0]}.",
            var n => $"Needed by {s.NeededBy[0]} and {n - 1} more.",
        };
        if (s.Installed)
        {
            var v = s.Mod is { Version.Length: > 0 } m ? $"Version {m.Version}." : s.Mod == null ? "Already in the game folder." : "";
            return s.Mod?.UpdateVersion is { } up
                ? ($"Update {up}", UiTheme.Accent, $"{l.About} {v} {needed}".Trim())
                : ("Installed", UiTheme.Good, $"{l.About} {v} {needed}".Trim());
        }
        if (s.Missing)
        {
            return (l.AutoInstall ? "Needed" : "Get it", UiTheme.Warn, $"{l.About} {(needed.Length > 0 ? needed : "Mods for this game need it.")}");
        }
        return ("Not needed yet", UiTheme.Dim, $"{l.About} Luma installs it when a mod needs it.");
    }

    /// <summary>The actions for one loader, for a choice menu or buttons.</summary>
    internal static List<(string Label, Action Run)> Actions(DetectedGame game, LoaderStatus s, Action? done = null)
    {
        var app = Main.Instance;
        var l = s.Loader;
        var list = new List<(string, Action)>();
        if (!s.Installed)
        {
            list.Add((l.AutoInstall ? $"Install {l.Name}" : $"Get {l.Name} from its website", () => { ModActions.InstallLoader(game, l); done?.Invoke(); }));
        }
        else if (s.Mod != null && l.AutoInstall)
        {
            list.Add((s.Mod.UpdateVersion != null ? $"Update to {s.Mod.UpdateVersion}" : "Reinstall the newest version", () => { ModActions.InstallLoader(game, l); done?.Invoke(); }));
        }
        if (ModActions.LoaderPage(l) is { } page)
        {
            list.Add(("Open its website", () => ModActions.OpenUrl(page)));
        }
        if (s.Mod != null)
        {
            list.Add(("Remove it", () => app.Confirm($"Remove {l.Name}? Mods that need it stop working until it's back.", "Remove", () =>
            {
                if (app.Guard(() => app.Host!.Mods.Uninstall(game, s.Mod.Id)))
                {
                    app.Success($"Removed {l.Name}.");
                    done?.Invoke();
                }
            })));
        }
        return list;
    }

    protected override void Build()
    {
        var col = Column(10);
        AddChild(col);
        var all = Host.Mods.LoaderStatus(_game);
        col.AddChild(UiTheme.Label($"Mod loaders  ·  {_game.Title}", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label(About, UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        var items = new List<Control>();
        var missing = all.Where(s => s.Missing).ToList();
        if (missing.Count > 0)
        {
            var b = new ActionButton($"Install what's needed ({string.Join(", ", missing.Select(s => s.Loader.Name))})", () => { ModActions.InstallMissingLoaders(_game); Rebuild(); },
                "Fetches every loader your mods need that isn't installed yet.");
            col.AddChild(b);
            items.Add(b);
        }
        var auto = Toggle.Create("Install loaders by themselves", Host.Settings.AutoLoaders, v => Host.SaveSettings(Host.Settings with { AutoLoaders = v }),
            "When a mod needs a loader Luma can fetch, it's installed with the mod.");
        col.AddChild(auto);
        items.Add(auto);
        var rows = Column(6);
        col.AddChild(Scroller(rows));
        foreach (var s in all)
        {
            var (state, color, detail) = Describe(s);
            var st = s;
            var row = new ListRow(s.Loader.Name, detail, state, color, null, () =>
            {
                var acts = Actions(_game, st, Rebuild);
                App.Push(new ChoicePage($"{st.Loader.Name}: {st.Loader.About}", acts.Select(a => (a.Label, a.Run)).Append(("Back", () => { })).ToArray()));
            }, detail);
            rows.AddChild(row);
            items.Add(row);
        }
        if (all.Count == 0)
        {
            rows.AddChild(UiTheme.Label($"{_game.Title}'s mods load without a separate loader.", UiTheme.FontSize, UiTheme.Dim, wrap: true));
            var back = new ActionButton("Back", () => App.Pop());
            col.AddChild(back);
            items.Add(back);
        }
        Focus.Column(items);
        _first = items.FirstOrDefault();
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
