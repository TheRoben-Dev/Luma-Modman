using System.Text.Json.Serialization;
using Luma.Core;
using Luma.Core.Sources;

namespace Luma.Host;

public enum DownloadState
{
    Queued,
    Downloading,
    Done,
    Failed,
    Cancelled,
}

/// <summary>One file being fetched (or fetched) from a mod site.</summary>
public sealed class DownloadItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Site { get; init; } = "";
    public string Game { get; init; } = "";
    public string ModId { get; init; } = "";
    public string ModName { get; init; } = "";
    public string Author { get; init; } = "";

    /// <summary>Mods this one needs (installed alongside when the site says so).</summary>
    public List<ModDependency> Dependencies { get; init; } = new();

    /// <summary>Set once the finished file has been installed for its game.</summary>
    public bool Installed { get; set; }

    /// <summary>Why installing failed, when it did.</summary>
    public string? InstallError { get; set; }
    public string FileId { get; init; } = "";
    public string Version { get; init; } = "";
    public string FileName { get; set; } = "";
    public string? PageUrl { get; init; }

    /// <summary>A modpack's profile: the mod joins only this profile (it's already in its order).</summary>
    public string? Profile { get; init; }

    /// <summary>FOMOD options to install with (a modpack's); null asks the player when the mod has an installer.</summary>
    public List<Luma.Core.Mods.FomodChoice>? Fomod { get; set; }

    /// <summary>The download is a mod with a FOMOD installer whose options haven't been picked yet.</summary>
    public bool NeedsOptions { get; set; }
    public DateTime Added { get; init; } = DateTime.UtcNow;
    public DownloadState State { get; set; }
    public long Bytes { get; set; }
    public long Total { get; set; }
    public string? Error { get; set; }

    /// <summary>The finished file on disk.</summary>
    public string? Path { get; set; }

    /// <summary>Resolved link (not kept: site links expire).</summary>
    [JsonIgnore]
    public DownloadTicket? Ticket { get; set; }

    [JsonIgnore]
    public double Progress => Total > 0 ? Math.Clamp((double)Bytes / Total, 0, 1) : -1;
}

/// <summary>
/// Downloads files one at a time into ~/.local/share/luma/downloads/&lt;site&gt;/ (a .part file,
/// renamed when complete). The list survives restarts; finished files are kept so a mod can be
/// reinstalled or rolled back without going online.
/// </summary>
public sealed class DownloadQueue
{
    private readonly AppPaths _paths;
    private readonly HttpClient _http;
    private readonly List<DownloadItem> _items;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _signal = new(0);
    private CancellationTokenSource? _current;
    private string? _currentId;

    /// <summary>Raised from a background thread; UI code must hop to its own thread.</summary>
    public event Action<DownloadItem>? Changed;

    public DownloadQueue(AppPaths paths, HttpClient? http = null)
    {
        _paths = paths;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _items = AppHost.LoadJson<List<DownloadItem>>(File) ?? new List<DownloadItem>();
        foreach (var i in _items.Where(i => i.State is DownloadState.Downloading or DownloadState.Queued))
        {
            i.State = DownloadState.Failed;  // links expire; the player retries
            i.Error = "Stopped when Luma closed. Retry to download it again.";
        }
        _ = Task.Run(Worker);
    }

    private string File => System.IO.Path.Combine(_paths.Data, "downloads.json");

    public IReadOnlyList<DownloadItem> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.OrderByDescending(i => i.Added).ToList();
            }
        }
    }

    public bool Busy => Items.Any(i => i.State is DownloadState.Queued or DownloadState.Downloading);

    /// <summary>Resolves the link (on the worker) and downloads. <paramref name="resolve"/> asks the site for a URL.</summary>
    public DownloadItem Add(DownloadItem item, Func<CancellationToken, Task<DownloadTicket>> resolve)
    {
        item.State = DownloadState.Queued;
        lock (_lock)
        {
            _items.Add(item);
            _resolvers[item.Id] = resolve;
        }
        Save();
        Changed?.Invoke(item);
        _signal.Release();
        return item;
    }

    private readonly Dictionary<string, Func<CancellationToken, Task<DownloadTicket>>> _resolvers = new();

    public bool CanRetry(DownloadItem item)
    {
        lock (_lock)
        {
            return item.State is DownloadState.Failed or DownloadState.Cancelled && _resolvers.ContainsKey(item.Id);
        }
    }

    public void Retry(DownloadItem item)
    {
        if (!CanRetry(item))
        {
            throw new InvalidOperationException("Open the mod again to download it: the site's link has expired.");
        }
        item.State = DownloadState.Queued;
        item.Error = null;
        item.Bytes = 0;
        Save();
        Changed?.Invoke(item);
        _signal.Release();
    }

    public void Cancel(DownloadItem item)
    {
        if (item.Id == _currentId)
        {
            _current?.Cancel();
        }
        else if (item.State == DownloadState.Queued)
        {
            item.State = DownloadState.Cancelled;
            Save();
            Changed?.Invoke(item);
        }
    }

    /// <summary>Removes the entry and its file.</summary>
    public void Remove(DownloadItem item)
    {
        Cancel(item);
        lock (_lock)
        {
            _items.Remove(item);
            _resolvers.Remove(item.Id);
        }
        if (item.Path != null && System.IO.File.Exists(item.Path))
        {
            System.IO.File.Delete(item.Path);
        }
        Save();
        Changed?.Invoke(item);
    }

    private async Task Worker()
    {
        while (true)
        {
            await _signal.WaitAsync();
            DownloadItem? next;
            Func<CancellationToken, Task<DownloadTicket>>? resolve;
            lock (_lock)
            {
                next = _items.Where(i => i.State == DownloadState.Queued).OrderBy(i => i.Added).FirstOrDefault();
                resolve = next != null ? _resolvers.GetValueOrDefault(next.Id) : null;
            }
            if (next == null || resolve == null)
            {
                continue;
            }
            await Run(next, resolve);
            lock (_lock)
            {
                if (_items.Any(i => i.State == DownloadState.Queued))
                {
                    _signal.Release();
                }
            }
        }
    }

    private async Task Run(DownloadItem item, Func<CancellationToken, Task<DownloadTicket>> resolve)
    {
        using var cts = new CancellationTokenSource();
        _current = cts;
        _currentId = item.Id;
        item.State = DownloadState.Downloading;
        item.Bytes = 0;
        Changed?.Invoke(item);
        var dir = System.IO.Path.Combine(_paths.DownloadsDir, item.Site);
        string? part = null;
        try
        {
            var ticket = await resolve(cts.Token);
            item.Ticket = ticket;
            item.FileName = Text.SafeFileName(ticket.FileName);
            Directory.CreateDirectory(dir);
            var target = System.IO.Path.Combine(dir, item.FileName);
            part = target + ".part";
            using var req = new HttpRequestMessage(HttpMethod.Get, ticket.Url);
            req.Headers.TryAddWithoutValidation("User-Agent", HttpGate.UserAgent);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{new Uri(ticket.Url).Host} answered {(int)resp.StatusCode} for the download.");
            }
            item.Total = resp.Content.Headers.ContentLength ?? ticket.Size;
            await using (var src = await resp.Content.ReadAsStreamAsync(cts.Token))
            await using (var dst = System.IO.File.Create(part))
            {
                var buf = new byte[1 << 16];
                var lastReport = DateTime.UtcNow;
                int n;
                while ((n = await src.ReadAsync(buf, cts.Token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), cts.Token);
                    item.Bytes += n;
                    if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(150))
                    {
                        lastReport = DateTime.UtcNow;
                        Changed?.Invoke(item);
                    }
                }
            }
            System.IO.File.Move(part, target, overwrite: true);
            item.Path = target;
            item.Total = item.Bytes;
            item.State = DownloadState.Done;
            AppLog.Info($"download done: {item.Site} {item.ModName} -> {target} ({item.Bytes} bytes)");
        }
        catch (OperationCanceledException)
        {
            item.State = DownloadState.Cancelled;
        }
        catch (DownloadNotAllowedException e)
        {
            item.State = DownloadState.Failed;
            item.Error = e.Message;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            item.State = DownloadState.Failed;
            item.Error = e.Message;
            AppLog.Warn($"download failed: {item.Site} {item.ModName}: {e.Message}");
        }
        finally
        {
            _current = null;
            _currentId = null;
            if (item.State != DownloadState.Done && part != null && System.IO.File.Exists(part))
            {
                try
                {
                    System.IO.File.Delete(part);
                }
                catch (IOException)
                {
                }
            }
        }
        Save();
        Changed?.Invoke(item);
    }

    /// <summary>Records install results (called by the installer after a finished download).</summary>
    public void Update(DownloadItem item)
    {
        Save();
        Changed?.Invoke(item);
    }

    private void Save()
    {
        List<DownloadItem> copy;
        lock (_lock)
        {
            copy = _items.ToList();
        }
        try
        {
            AppHost.SaveJson(File, copy);
        }
        catch (IOException e)
        {
            AppLog.Warn("downloads list: " + e.Message);
        }
    }
}
