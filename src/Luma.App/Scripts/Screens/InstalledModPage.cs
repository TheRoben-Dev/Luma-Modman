using Luma.App.Ui;
using Luma.Core.Detection;
using Luma.Core.Mods;
using Godot;

namespace Luma.App.Screens;

/// <summary>One installed mod: turn it on or off, move it in the load order, update, roll back or remove it.</summary>
public partial class InstalledModPage : Screen
{
    private readonly DetectedGame _game;
    private readonly string _id;
    private Control? _first;

    internal DetectedGame Game => _game;
    internal string Id => _id;

    public InstalledModPage(DetectedGame game, string id)
    {
        _game = game;
        _id = id;
    }

    protected override void Build()
    {
        var store = Host.Mods.Store(_game);
        var mod = store.State.Find(_id);
        var center = new CenterContainer();
        AddChild(center);
        var col = Column(10);
        col.CustomMinimumSize = new Vector2(680, 0);
        center.AddChild(col);
        if (mod == null)
        {
            col.AddChild(UiTheme.Label("This mod isn't installed any more.", UiTheme.FontSize));
            var back = Add(col, new ActionButton("Back", () => App.Pop()));
            _first = back;
            return;
        }
        var order = store.State.Order;
        var index = order.FindIndex(e => e.Id == _id);
        var on = store.State.IsEnabled(_id);
        col.AddChild(UiTheme.Label(mod.Name, UiTheme.TitleSize, UiTheme.Text, wrap: true));
        var facts = new[]
        {
            mod.Version.Length > 0 ? "version " + mod.Version : "",
            mod.Author.Length > 0 ? "by " + mod.Author : "",
            ModsScreen.SiteName(mod.Site),
            $"{mod.Files} file{(mod.Files == 1 ? "" : "s")}",
            $"#{index + 1} of {order.Count} in {store.State.Active}",
        };
        col.AddChild(UiTheme.Label(string.Join("  ·  ", facts.Where(f => f.Length > 0)), UiTheme.SmallSize + 1, UiTheme.Dim, wrap: true));
        if (mod.Dependencies.Count > 0)
        {
            col.AddChild(UiTheme.Label("Needs " + string.Join(", ", mod.Dependencies.Select(d => d.Name)), UiTheme.SmallSize, UiTheme.Dim, wrap: true));
        }
        var items = new List<Control>
        {
            Add(col, new ActionButton(on ? "Turn off" : "Turn on", () => Do(() => store.SetEnabled(_id, !on)),
                on ? "Keeps it installed but leaves it out of the game." : "Includes it the next time mods go into the game.")),
        };
        if (index > 0)
        {
            items.Add(Add(col, new ActionButton("Move up (loads earlier)", () => Do(() => store.Move(_id, -1)), "Mods lower in the list win when two change the same file.")));
        }
        if (index < order.Count - 1)
        {
            items.Add(Add(col, new ActionButton("Move down (loads later, wins conflicts)", () => Do(() => store.Move(_id, +1)), "Mods lower in the list win when two change the same file.")));
        }
        var site = Host.Sites.All.FirstOrDefault(s => s.Id == mod.Site);
        if (site != null && mod.ModId.Length > 0)
        {
            items.Add(Add(col, new ActionButton(mod.UpdateVersion != null ? $"Update to {mod.UpdateVersion}" : "Download the newest version",
                () => ModActions.DownloadLatest(site, _game, mod.ModId, mod.Name), "Downloads it and installs it over this version; this one stays for rollback.")));
        }
        foreach (var h in mod.History.Where(h => File.Exists(h.Archive)))
        {
            var v = h;
            items.Add(Add(col, new ActionButton($"Roll back to {(v.Version.Length > 0 ? v.Version : "the previous version")}",
                () => App.Busy("Rolling back…", (_, _) => Task.FromResult(Host.Mods.Rollback(_game, _id, v)), _ =>
                {
                    App.Success($"{mod.Name} is back to {v.Version}.");
                    Rebuild();
                }, cancellable: false), $"Reinstalls the download from {v.InstalledAt.ToLocalTime():d MMM}.")));
        }
        if (mod.PageUrl != null)
        {
            items.Add(Add(col, new ActionButton("Open website", () => ModActions.OpenUrl(mod.PageUrl), mod.PageUrl)));
        }
        items.Add(Add(col, new ActionButton("Uninstall", () => App.Confirm($"Uninstall {mod.Name}? Its download is kept, so it can be installed again.", "Uninstall", () =>
        {
            if (App.Guard(() => Host.Mods.Uninstall(_game, _id)))
            {
                App.Pop();
                App.Success($"{mod.Name} uninstalled.");
            }
        }), "Removes it from Luma's mod folder.")));
        items.Add(Add(col, new ActionButton("Back", () => App.Pop())));
        Focus.Column(items);
        _first = items[0];
    }

    private void Do(Action act)
    {
        App.Guard(act);
        Host.Mods.NotifyChanged(_game);
        Rebuild();
    }

    private static T Add<T>(Control parent, T c) where T : Control
    {
        parent.AddChild(c);
        return c;
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
