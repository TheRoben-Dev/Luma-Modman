using Luma.App.Screens;
using Luma.Host;
using Godot;

namespace Luma.App.Desktop;

/// <summary>
/// The Downloads tab in the desktop layout: every download as a table row (mod, game, site,
/// version, status, progress, size), with the selected row's actions in a toolbar and on
/// right-click: cancel, retry, install, show the file, open the mod's page, delete.
/// </summary>
public partial class DesktopDownloads : VBoxContainer
{
    private static Main App => Main.Instance;
    private static AppHost Host => Main.Instance.Host!;

    private Tree _table = null!;
    private HBoxContainer _actions = null!;
    private Label _info = null!;
    private PopupMenu _menu = null!;
    private readonly List<Action> _menuActions = new();
    private readonly Dictionary<string, TreeItem> _items = new();
    private string? _selected;

    private static readonly string[] Titles = ["Mod", "Game", "Site", "Version", "Status", "Progress", "Size"];

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        _actions = new HBoxContainer();
        _actions.AddThemeConstantOverride("separation", 4);
        AddChild(_actions);
        _table = new Tree
        {
            Columns = Titles.Length,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            AllowRmbSelect = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            TooltipText = "Downloads",
        };
        for (var i = 0; i < Titles.Length; i++)
        {
            _table.SetColumnTitle(i, Titles[i]);
            _table.SetColumnExpand(i, i == 0);
            _table.SetColumnTitleAlignment(i, HorizontalAlignment.Left);
            _table.SetColumnClipContent(i, true);
        }
        _table.SetColumnCustomMinimumWidth(0, 220);
        _table.SetColumnCustomMinimumWidth(1, 160);
        _table.SetColumnCustomMinimumWidth(2, 110);
        _table.SetColumnCustomMinimumWidth(3, 90);
        _table.SetColumnCustomMinimumWidth(4, 120);
        _table.SetColumnCustomMinimumWidth(5, 170);
        _table.SetColumnCustomMinimumWidth(6, 90);
        _table.ItemSelected += () =>
        {
            _selected = _table.GetSelected()?.GetMetadata(0).AsString();
            FillActions();
        };
        _table.ItemMouseSelected += (pos, button) =>
        {
            if (button == (long)MouseButton.Right)
            {
                ShowMenu(_table.GetGlobalRect().Position + pos);
            }
        };
        AddChild(_table);
        var foot = new HBoxContainer();
        _info = DesktopTheme.Label("", DesktopTheme.Dim, DesktopTheme.SmallSize);
        _info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foot.AddChild(_info);
        foot.AddChild(DesktopTheme.Button("Open downloads folder", () => DesktopShell.OpenFolder(Host.Paths.DownloadsDir), Host.Paths.DownloadsDir, "ToolButton"));
        AddChild(foot);
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

    public void Refresh()
    {
        if (_table == null)
        {
            return;
        }
        _table.Clear();
        _items.Clear();
        var root = _table.CreateItem();
        var items = Host.Downloads.Items;
        foreach (var d in items)
        {
            var it = _table.CreateItem(root);
            it.SetMetadata(0, d.Id);
            _items[d.Id] = it;
            Fill(it, d);
            if (d.Id == _selected)
            {
                it.Select(0);
            }
        }
        if (items.Count == 0)
        {
            var it = _table.CreateItem(root);
            it.SetText(0, "Nothing downloaded yet. Mods you download in Browse, and Nexus \"Mod Manager Download\" links, arrive here.");
            it.SetCustomColor(0, DesktopTheme.Dim);
            it.SetSelectable(0, false);
            for (var i = 1; i < Titles.Length; i++)
            {
                it.SetSelectable(i, false);
            }
            it.SetMetadata(0, "");
        }
        if (_selected != null && !_items.ContainsKey(_selected))
        {
            _selected = null;
        }
        _info.Text = $"{items.Count} download{(items.Count == 1 ? "" : "s")}  ·  finished downloads are kept so a mod can be reinstalled or rolled back offline  ·  {Host.Paths.Short(Host.Paths.DownloadsDir)}";
        FillActions();
    }

    /// <summary>One download changed: update its row in place, or rebuild if rows came or went.</summary>
    public void OnChanged(DownloadItem item)
    {
        if (_table == null)
        {
            return;
        }
        var present = Host.Downloads.Items.Any(i => i.Id == item.Id);
        if (present && _items.TryGetValue(item.Id, out var it))
        {
            Fill(it, item);
            if (item.Id == _selected)
            {
                FillActions();
            }
        }
        else
        {
            Refresh();
        }
    }

    private static void Fill(TreeItem it, DownloadItem d)
    {
        it.SetText(0, d.ModName);
        it.SetTooltipText(0, Help(d));
        it.SetText(1, Host.Games.Games.FirstOrDefault(g => g.Key == d.Game)?.Title ?? (d.Game.Length > 0 ? d.Game : "—"));
        it.SetCustomColor(1, DesktopTheme.Dim);
        it.SetText(2, SiteName(d.Site));
        it.SetCustomColor(2, DesktopTheme.Dim);
        it.SetText(3, d.Version);
        var (status, color) = Status(d);
        it.SetText(4, status);
        it.SetCustomColor(4, color);
        it.SetTooltipText(4, Help(d));
        it.SetText(5, d.State == DownloadState.Downloading ? Bar(d.Progress) : d.State == DownloadState.Done ? Bar(1) : "");
        it.SetCustomColor(5, d.State == DownloadState.Downloading ? DesktopTheme.Accent : DesktopTheme.Dim);
        it.SetText(6, d.Total > 0 ? ModDetailsPage.Bytes(d.Total) : d.Bytes > 0 ? ModDetailsPage.Bytes(d.Bytes) : "");
        it.SetTextAlignment(6, HorizontalAlignment.Right);
    }

    /// <summary>A text progress bar for the table cell.</summary>
    private static string Bar(double p)
    {
        if (p < 0)
        {
            return "▒▒▒▒▒▒▒▒▒▒▒▒";
        }
        var n = (int)Math.Round(p * 12);
        return new string('█', n) + new string('░', 12 - n) + $"  {p * 100:0}%";
    }

    private static string SiteName(string id) => id switch
    {
        "thunderstore" => "Thunderstore",
        "nexus" => "Nexus Mods",
        "modio" => "mod.io",
        "curseforge" => "CurseForge",
        "loader" => "GitHub",
        _ => id,
    };

    private static (string, Color) Status(DownloadItem i) => i.State switch
    {
        DownloadState.Queued => ("Waiting", DesktopTheme.Dim),
        DownloadState.Downloading => ("Downloading", DesktopTheme.Accent),
        DownloadState.Done when i.Installed => ("Installed", DesktopTheme.Good),
        DownloadState.Done when i.NeedsOptions => ("Needs options", DesktopTheme.Warn),
        DownloadState.Done when i.InstallError != null => ("Not installed", DesktopTheme.Warn),
        DownloadState.Done => ("Installing…", DesktopTheme.Accent),
        DownloadState.Failed => ("Failed", DesktopTheme.Bad),
        DownloadState.Cancelled => ("Cancelled", DesktopTheme.Dim),
        _ => ("", DesktopTheme.Dim),
    };

    private static string Help(DownloadItem i) =>
        i.State == DownloadState.Failed && i.Error != null ? i.Error : i.InstallError ?? $"{i.ModName} from {SiteName(i.Site)}.";

    private DownloadItem? Selected => _selected == null ? null : Host.Downloads.Items.FirstOrDefault(i => i.Id == _selected);

    /// <summary>The same options as the controller layout's download page.</summary>
    private static List<(string Label, Action Run)> Options(DownloadItem item)
    {
        var q = Host.Downloads;
        var options = new List<(string, Action)>();
        switch (item.State)
        {
            case DownloadState.Queued or DownloadState.Downloading:
                options.Add(("Cancel download", () => q.Cancel(item)));
                break;
            case DownloadState.Failed or DownloadState.Cancelled:
                if (q.CanRetry(item))
                {
                    options.Add(("Try again", () => App.Guard(() => q.Retry(item))));
                }
                break;
            case DownloadState.Done when item.Path != null:
                if (item.NeedsOptions && !item.Installed)
                {
                    options.Add(("Choose install options…", () => FomodSession.ForDownload(item)));
                }
                else if (!item.Installed)
                {
                    options.Add(("Install", () =>
                    {
                        item.InstallError = null;
                        Task.Run(() => Host.InstallDownload(item));
                    }));
                }
                options.Add(("Show file", () => OS.ShellOpen(Path.GetDirectoryName(item.Path)!)));
                break;
        }
        if (item.PageUrl != null)
        {
            options.Add(("Open mod website", () => ModActions.OpenUrl(item.PageUrl)));
        }
        options.Add((item.State == DownloadState.Done ? "Delete file…" : "Remove from list", () =>
        {
            if (item.State == DownloadState.Done)
            {
                App.Confirm($"Delete the downloaded file of {item.ModName}? Installed copies are not affected.", "Delete", () => App.Guard(() => q.Remove(item)));
            }
            else
            {
                App.Guard(() => q.Remove(item));
            }
        }));
        return options;
    }

    private void FillActions()
    {
        foreach (var c in _actions.GetChildren())
        {
            c.QueueFree();
        }
        if (Selected is { } item)
        {
            foreach (var (label, run) in Options(item))
            {
                _actions.AddChild(DesktopTheme.Button(label, run, "", "ToolButton"));
            }
            var problem = item.State == DownloadState.Failed ? item.Error : item.InstallError;
            if (problem != null)
            {
                var l = DesktopTheme.Label(problem, DesktopTheme.Warn, DesktopTheme.SmallSize);
                l.ClipText = true;
                l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                _actions.AddChild(l);
            }
        }
        else
        {
            _actions.AddChild(DesktopTheme.Label("Select a download for its options.", DesktopTheme.Dim, DesktopTheme.SmallSize));
        }
        _actions.AddChild(DesktopShell.Spacer());
        _actions.AddChild(DesktopTheme.Button("Clear failed", ClearFailed, "Removes failed and cancelled downloads from the list.", "ToolButton"));
    }

    private void ShowMenu(Vector2? at = null)
    {
        if (Selected is not { } item)
        {
            return;
        }
        _menu.Clear();
        _menuActions.Clear();
        foreach (var (label, run) in Options(item))
        {
            _menu.AddItem(label, _menuActions.Count);
            _menuActions.Add(run);
        }
        DesktopMods.PopupAtMouse(_menu, _table, at);
    }

    private static void ClearFailed()
    {
        var q = Host.Downloads;
        var gone = q.Items.Where(i => i.State is DownloadState.Failed or DownloadState.Cancelled).ToList();
        foreach (var i in gone)
        {
            q.Remove(i);
        }
        App.Toast(gone.Count == 0 ? "Nothing to clear: finished files stay for rollback." : $"Cleared {gone.Count} from the list.");
    }

    /// <summary>Selects the first download (smoke test).</summary>
    public bool SelectFirst()
    {
        var first = _table.GetRoot()?.GetFirstChild();
        if (first == null || first.GetMetadata(0).AsString().Length == 0)
        {
            return false;
        }
        first.Select(0);
        return true;
    }

    public bool MenuOpen => _menu.Visible;

    public void OpenMenu() => ShowMenu();
}
