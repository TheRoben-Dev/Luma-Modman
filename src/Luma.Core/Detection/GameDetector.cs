using Luma.Core.Games;

namespace Luma.Core.Detection;

/// <summary>An installed game Luma supports (or knows it can't mod), ready for the Library.</summary>
public sealed record DetectedGame
{
    public required GameProfile Profile { get; init; }
    public required InstalledGame Install { get; init; }

    /// <summary>The game's root folder (where the profile's relative paths start).</summary>
    public required string GameDir { get; init; }

    /// <summary>"steam:489830", "epic:Fortnite", "lutris:skyrim"... unique per install.</summary>
    public string Key => $"{Install.Source.ToString().ToLowerInvariant()}:{Install.SourceId}";

    public string Title => Profile.Name;
}

/// <summary>
/// Scans every launcher (Steam, Heroic, Lutris on Linux; Steam, Heroic, Epic, GOG on Windows) and keeps the installs that match a support profile: Steam by app id,
/// everything else by the profile's detect files in the game folder (or a few folders above it,
/// since Lutris points at the executable, which can sit in bin/x64).
/// </summary>
public sealed class GameDetector
{
    private readonly GameCatalog _catalog;
    private readonly string _home;

    public GameDetector(GameCatalog catalog, string home)
    {
        _catalog = catalog;
        _home = home;
    }

    /// <summary>All launchers' installs, including unsupported ones (for diagnostics).</summary>
    public IReadOnlyList<InstalledGame> LastInstalls { get; private set; } = [];

    public IReadOnlyList<DetectedGame> Scan(CancellationToken ct = default)
    {
        var installs = new List<InstalledGame>();
        installs.AddRange(new SteamScanner(_home).Scan(ct));
        ct.ThrowIfCancellationRequested();
        installs.AddRange(new HeroicScanner(_home).Scan());
        if (OperatingSystem.IsWindows())
        {
            installs.AddRange(WindowsLaunchers.Epic());
            installs.AddRange(WindowsLaunchers.Gog());
        }
        else
        {
            installs.AddRange(new LutrisScanner(_home).Scan());
        }
        LastInstalls = installs;
        return Match(installs);
    }

    public IReadOnlyList<DetectedGame> Match(IEnumerable<InstalledGame> installs)
    {
        var found = new List<DetectedGame>();
        var seen = new HashSet<string>();
        foreach (var install in installs)
        {
            if (Match(install) is { } d && seen.Add(d.Key))
            {
                found.Add(d);
            }
        }
        return found.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public DetectedGame? Match(InstalledGame install)
    {
        GameProfile? profile = null;
        string? root = install.InstallDir;
        switch (install.Source)
        {
            case GameSource.Steam when long.TryParse(install.SourceId, out var appId):
                profile = _catalog.BySteamId(appId);
                break;
            case GameSource.Gog:
                profile = _catalog.Games.FirstOrDefault(g => g.Gog.Contains(install.SourceId));
                break;
            case GameSource.Epic:
                profile = _catalog.Games.FirstOrDefault(g => g.Epic.Contains(install.SourceId, StringComparer.OrdinalIgnoreCase));
                break;
        }
        if (profile == null)
        {
            // Every detect file must be there; the most specific profile wins (Enderal and Skyrim
            // share SkyrimSE.exe, but only Enderal has its own master file).
            var levels = install.Source == GameSource.Steam ? 0 : 3;  // Steam folders are the game root already
            var best = _catalog.Games
                .Where(g => g.Detect.Count > 0)
                .Select(g => (Game: g, Root: FindRoot(install.InstallDir, g.Detect[0], levels)))
                .Where(x => x.Root != null && x.Game.Detect.Skip(1).All(f => PathUtil.FindCaseInsensitive(x.Root!, f) != null))
                .OrderByDescending(x => x.Game.Detect.Count)
                .FirstOrDefault();
            if (best.Game != null)
            {
                profile = best.Game;
                root = best.Root!;
            }
        }
        return profile == null ? null : new DetectedGame { Profile = profile, Install = install, GameDir = root };
    }

    /// <summary>The folder at or up to <paramref name="levelsUp"/> levels above <paramref name="dir"/> that holds <paramref name="relative"/> (any case).</summary>
    public static string? FindRoot(string dir, string relative, int levelsUp = 3)
    {
        var cur = dir;
        for (var i = 0; i <= levelsUp && !string.IsNullOrEmpty(cur); i++)
        {
            if (PathUtil.FindCaseInsensitive(cur, relative) != null)
            {
                return cur;
            }
            cur = Path.GetDirectoryName(cur);
        }
        return null;
    }
}
