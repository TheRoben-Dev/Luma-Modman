using Luma.App.Ui;
using Luma.Host;
using Godot;

namespace Luma.App.Screens;

/// <summary>Running and finished downloads, with progress, retry and the files kept for rollback.</summary>
public partial class DownloadsScreen : Screen
{
    private readonly Dictionary<string, ListRow> _rows = new();
    private Control? _first;

    public override IEnumerable<(string Action, string Label)> Hints => [(Pad.Secondary, "Clear failed")];

    public override void _EnterTree() => Host.Downloads.Changed += OnChangedAnyThread;

    public override void _ExitTree() => Host.Downloads.Changed -= OnChangedAnyThread;

    private void OnChangedAnyThread(DownloadItem item) => Callable.From(() => OnChanged(item)).CallDeferred();

    private void OnChanged(DownloadItem item)
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
        {
            return;
        }
        var present = Host.Downloads.Items.Any(i => i.Id == item.Id);
        if (_rows.TryGetValue(item.Id, out var row) && present && IsInstanceValid(row))
        {
            var (status, color) = Status(item);
            row.SetStatus(status, color);
            row.TooltipText = Help(item);
        }
        else if (present != _rows.ContainsKey(item.Id))
        {
            Rebuild();
        }
    }

    protected override void Build()
    {
        _rows.Clear();
        var items = Host.Downloads.Items;
        if (items.Count == 0)
        {
            var empty = EmptyState.Build(this, "Nothing downloading",
                "Mods you download from the Browse tab, and \"Mod Manager Download\" links from Nexus Mods, arrive here. Finished downloads are kept so a mod can be reinstalled or rolled back without going online.",
                ("Browse mods", () => App.SwitchTab("Browse"), "Find mods to download."));
            _first = empty.FirstOrDefault();
            return;
        }
        var col = Column(10);
        AddChild(col);
        col.AddChild(UiTheme.Label("Downloads", UiTheme.TitleSize));
        col.AddChild(UiTheme.Label("Kept in " + Host.Paths.DownloadsDir.Replace(Host.Paths.Home, "~"), UiTheme.SmallSize, UiTheme.Dim));
        var list = Column(8);
        col.AddChild(Scroller(list));
        var rows = new List<Control>();
        foreach (var it in items)
        {
            var item = it;
            var (status, color) = Status(item);
            var detail = string.Join("  ·  ", new[] { SiteName(item.Site), item.Version, item.FileName }.Where(s => s.Length > 0));
            var row = new ListRow(item.ModName, detail, status, color, null, () => Open(item), Help(item));
            list.AddChild(row);
            rows.Add(row);
            _rows[item.Id] = row;
        }
        Focus.Column(rows);
        Anim.Stagger(rows);
        _first = rows.FirstOrDefault();
    }

    private static string SiteName(string id) => id switch
    {
        "thunderstore" => "Thunderstore",
        "nexus" => "Nexus Mods",
        "modio" => "mod.io",
        "curseforge" => "CurseForge",
        _ => id,
    };

    private static (string, Color) Status(DownloadItem i) => i.State switch
    {
        DownloadState.Queued => ("Waiting", UiTheme.Dim),
        DownloadState.Downloading when i.Progress >= 0 => ($"{i.Progress * 100:0}%  ·  {ModDetailsPage.Bytes(i.Bytes)} of {ModDetailsPage.Bytes(i.Total)}", UiTheme.AccentText),
        DownloadState.Downloading => (i.Bytes > 0 ? ModDetailsPage.Bytes(i.Bytes) : "Starting…", UiTheme.AccentText),
        DownloadState.Done => ("Downloaded" + (i.Total > 0 ? "  ·  " + ModDetailsPage.Bytes(i.Total) : ""), UiTheme.Good),
        DownloadState.Failed => ("Failed", UiTheme.Bad),
        DownloadState.Cancelled => ("Cancelled", UiTheme.Dim),
        _ => ("", UiTheme.Dim),
    };

    private static string Help(DownloadItem i) => i.State == DownloadState.Failed && i.Error != null ? i.Error : $"{i.ModName} from {SiteName(i.Site)}.";

    private void Open(DownloadItem item)
    {
        var q = Host.Downloads;
        var options = new List<(string, Action)>();
        switch (item.State)
        {
            case DownloadState.Queued or DownloadState.Downloading:
                options.Add(("Cancel the download", () => q.Cancel(item)));
                break;
            case DownloadState.Failed or DownloadState.Cancelled:
                if (q.CanRetry(item))
                {
                    options.Add(("Try again", () => App.Guard(() => q.Retry(item))));
                }
                break;
            case DownloadState.Done when item.Path != null:
                options.Add(("Show the file", () => OS.ShellOpen(Path.GetDirectoryName(item.Path)!)));
                break;
        }
        if (item.PageUrl != null)
        {
            options.Add(("Open the mod's website", () => ModActions.OpenUrl(item.PageUrl)));
        }
        options.Add((item.State == DownloadState.Done ? "Delete the file" : "Remove from the list", () =>
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
        options.Add(("Back", () => { }));
        var message = item.State == DownloadState.Failed && item.Error != null ? $"{item.ModName}\n\n{item.Error}" : item.ModName;
        App.Push(new ChoicePage(message, options.ToArray()));
    }

    public override void OnAction(string action)
    {
        if (action != Pad.Secondary)
        {
            return;
        }
        var q = Host.Downloads;
        var gone = q.Items.Where(i => i.State is DownloadState.Failed or DownloadState.Cancelled).ToList();
        foreach (var i in gone)
        {
            q.Remove(i);
        }
        App.Toast(gone.Count == 0 ? "Nothing to clear: finished files stay for rollback." : $"Cleared {gone.Count} from the list.");
    }

    public override void FocusDefault() => _first?.GrabFocus();
}
