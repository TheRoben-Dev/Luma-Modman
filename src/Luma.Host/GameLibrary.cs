using Luma.Core;
using Luma.Core.Detection;
using Luma.Core.Games;

namespace Luma.Host;

/// <summary>
/// The games Luma can work on: the support profiles, the installs found on this machine, and the
/// game the player is working on now (every tab acts on that one). The last scan is cached so the
/// app opens instantly; a rescan runs in the background and replaces it.
/// </summary>
public sealed class GameLibrary
{
    private readonly AppPaths _paths;
    private readonly GameDetector _detector;

    public GameCatalog Catalog { get; }

    /// <summary>Supported games found on this machine, by title. Includes not-moddable ones (shown with the reason).</summary>
    public IReadOnlyList<DetectedGame> Games { get; private set; } = [];

    /// <summary>When the list was last scanned (UTC), or null if never.</summary>
    public DateTime? ScannedAt { get; private set; }

    public event Action? Changed;

    public GameLibrary(AppPaths paths)
    {
        _paths = paths;
        Catalog = GameCatalog.Load(Path.Combine(paths.Config, "games"));
        foreach (var p in Catalog.Problems)
        {
            AppLog.Warn("game profile: " + p);
        }
        _detector = new GameDetector(Catalog, paths.Home);
        if (AppHost.LoadJson<LibraryCache>(paths.LibraryCacheFile) is { } cache)
        {
            Games = _detector.Match(cache.Installs);
            ScannedAt = cache.ScannedAt;
        }
    }

    /// <summary>Scans every launcher (Steam incl. SD cards, Heroic, Lutris; native and Flatpak). Safe to call off the main thread.</summary>
    public IReadOnlyList<DetectedGame> Scan(CancellationToken ct = default)
    {
        var found = _detector.Scan(ct);
        var installs = _detector.LastInstalls;
        AppLog.Info($"scan: {installs.Count} installs, {found.Count} supported ({string.Join(", ", found.Select(f => f.Profile.Id))})");
        ScannedAt = DateTime.UtcNow;
        try
        {
            AppHost.SaveJson(_paths.LibraryCacheFile, new LibraryCache { ScannedAt = ScannedAt.Value, Installs = installs.ToList() });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("library cache: " + e.Message);
        }
        Games = found;
        return found;
    }

    /// <summary>Call on the UI thread after a background <see cref="Scan"/> to tell listeners.</summary>
    public void NotifyChanged() => Changed?.Invoke();

    public DetectedGame? Find(string? key) => key == null ? null : Games.FirstOrDefault(g => g.Key == key);

    /// <summary>Where Luma looks, for the "no games found" page.</summary>
    public static readonly string[] Locations =
    [
        "Steam: every library in libraryfolders.vdf, including SD cards and extra drives",
        "Steam Flatpak: ~/.var/app/com.valvesoftware.Steam",
        "Heroic (Epic, GOG, Amazon): ~/.config/heroic and the Flatpak",
        "Lutris: ~/.config/lutris/games and the Flatpak",
    ];

    private sealed record LibraryCache
    {
        public DateTime ScannedAt { get; init; }
        public List<InstalledGame> Installs { get; init; } = new();
    }
}
