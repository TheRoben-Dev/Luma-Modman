namespace Luma.Core.Detection;

/// <summary>
/// Reads Steam's own files (never runs Steam): every library from libraryfolders.vdf, including
/// SD cards and extra drives, the installed apps' manifests, Proton prefixes (compatdata),
/// the compatibility tool and launch options per game, and the cached library art.
/// Covers the native Steam install and the Flatpak one.
/// </summary>
public sealed class SteamScanner
{
    private readonly string _home;

    public SteamScanner(string home) => _home = home;

    /// <summary>Steam root folders that exist (native first, then Flatpak), de-duplicated through symlinks.</summary>
    public IReadOnlyList<(string Root, string Packaging)> Roots()
    {
        var candidates = new (string, string)[]
        {
            (Path.Combine(_home, ".local/share/Steam"), "native"),
            (Path.Combine(_home, ".steam/steam"), "native"),
            (Path.Combine(_home, ".steam/root"), "native"),
            (Path.Combine(_home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"), "flatpak"),
            (Path.Combine(_home, ".var/app/com.valvesoftware.Steam/data/Steam"), "flatpak"),
        };
        var seen = new HashSet<string>();
        var roots = new List<(string, string)>();
        foreach (var (dir, kind) in candidates)
        {
            if (!Directory.Exists(Path.Combine(dir, "steamapps")))
            {
                continue;
            }
            if (seen.Add(PathUtil.RealPath(dir)))
            {
                roots.Add((dir, kind));
            }
        }
        return roots;
    }

    /// <summary>Library folders listed by a Steam root (the root itself is always one).</summary>
    public static IReadOnlyList<string> Libraries(string root)
    {
        var libs = new List<string> { root };
        var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file))
        {
            var kv = KeyValues.ParseFile(file);
            var folders = kv.Map("libraryfolders") ?? kv.Map("LibraryFolders") ?? new KeyValues();
            foreach (var key in folders.Keys)
            {
                // New format: "0" { "path" "/run/media/mmcblk0p1" ... }; old format: "1" "/path".
                var path = folders.Map(key)?.Str("path") ?? (int.TryParse(key, out _) ? folders.Str(key) : null);
                if (!string.IsNullOrEmpty(path))
                {
                    libs.Add(path);
                }
            }
        }
        var seen = new HashSet<string>();
        return libs.Where(l => Directory.Exists(Path.Combine(l, "steamapps")) && seen.Add(PathUtil.RealPath(l))).ToList();
    }

    public IReadOnlyList<InstalledGame> Scan(CancellationToken ct = default)
    {
        var games = new List<InstalledGame>();
        var seenApps = new HashSet<string>();
        foreach (var (root, packaging) in Roots())
        {
            var tools = CompatTools(root);
            var options = LaunchOptionsByApp(root);
            var libs = Libraries(root);
            foreach (var lib in libs)
            {
                ct.ThrowIfCancellationRequested();
                var apps = Path.Combine(lib, "steamapps");
                IEnumerable<string> manifests;
                try
                {
                    manifests = Directory.EnumerateFiles(apps, "appmanifest_*.acf").ToList();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;  // an unmounted SD card or a drive we can't read
                }
                foreach (var manifest in manifests)
                {
                    InstalledGame? g;
                    try
                    {
                        g = ReadManifest(manifest, lib, libs, root);
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        continue;
                    }
                    if (g == null || !seenApps.Add(g.SourceId))
                    {
                        continue;
                    }
                    games.Add(g with
                    {
                        CompatTool = tools.GetValueOrDefault(g.SourceId),
                        LaunchOptions = options.GetValueOrDefault(g.SourceId),
                        Packaging = packaging,
                    });
                }
            }
        }
        return games;
    }

    private static InstalledGame? ReadManifest(string manifest, string lib, IReadOnlyList<string> libs, string root)
    {
        var state = KeyValues.ParseFile(manifest).Map("AppState");
        var appId = state?.Str("appid");
        var installDir = state?.Str("installdir");
        if (state == null || string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDir))
        {
            return null;
        }
        var dir = Path.Combine(lib, "steamapps", "common", installDir);
        if (!Directory.Exists(dir))
        {
            return null;  // manifest left over from an uninstall, or still downloading
        }
        return new InstalledGame
        {
            Source = GameSource.Steam,
            SourceId = appId,
            Title = state.Str("name") ?? installDir,
            InstallDir = dir,
            Library = lib,
            Prefix = FindPrefix(appId, lib, libs),
            Cover = FindCover(root, appId),
        };
    }

    /// <summary>
    /// The Proton prefix: steamapps/compatdata/&lt;appid&gt;/pfx in the game's own library, or in any
    /// other library (Steam keeps it on the main drive in some setups). Null before the first launch.
    /// </summary>
    public static string? FindPrefix(string appId, string lib, IReadOnlyList<string> libs)
    {
        foreach (var l in libs.Prepend(lib))
        {
            var pfx = Path.Combine(l, "steamapps", "compatdata", appId, "pfx");
            if (Directory.Exists(pfx))
            {
                return pfx;
            }
        }
        return null;
    }

    /// <summary>Portrait art Steam has cached (old flat layout and the newer per-app folders).</summary>
    public static string? FindCover(string root, string appId)
    {
        var cache = Path.Combine(root, "appcache", "librarycache");
        string[] candidates =
        [
            Path.Combine(cache, appId, "library_600x900.jpg"),
            Path.Combine(cache, appId, "library_600x900_2x.jpg"),
            Path.Combine(cache, $"{appId}_library_600x900.jpg"),
            Path.Combine(cache, $"{appId}_library_600x900_2x.jpg"),
        ];
        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null)
        {
            return found;
        }
        // Newer clients put the art in a hashed subfolder: librarycache/<appid>/<hash>/library_600x900.jpg
        var dir = Path.Combine(cache, appId);
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "library_600x900*.jpg", SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Per-app compatibility tool from config/config.vdf (CompatToolMapping).</summary>
    public static Dictionary<string, string> CompatTools(string root)
    {
        var map = new Dictionary<string, string>();
        var file = Path.Combine(root, "config", "config.vdf");
        try
        {
            if (File.Exists(file) &&
                KeyValues.ParseFile(file).Path("InstallConfigStore", "Software", "Valve", "Steam", "CompatToolMapping") is { } tools)
            {
                foreach (var (app, entry) in tools.Maps())
                {
                    if (entry.Str("name") is { Length: > 0 } name)
                    {
                        map[app] = name;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        return map;
    }

    /// <summary>Launch options per app from the most recently used account's localconfig.vdf.</summary>
    public static Dictionary<string, string> LaunchOptionsByApp(string root)
    {
        var map = new Dictionary<string, string>();
        if (LocalConfig(root) is not { } file)
        {
            return map;
        }
        try
        {
            var apps = KeyValues.ParseFile(file).Path("UserLocalConfigStore", "Software", "Valve", "Steam", "apps");
            foreach (var (app, entry) in apps?.Maps() ?? [])
            {
                if (entry.Str("LaunchOptions") is { Length: > 0 } opts)
                {
                    map[app] = opts;
                }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
        return map;
    }

    /// <summary>The localconfig.vdf of the account used last (newest file), or null.</summary>
    public static string? LocalConfig(string root)
    {
        var userdata = Path.Combine(root, "userdata");
        if (!Directory.Exists(userdata))
        {
            return null;
        }
        return Directory.EnumerateDirectories(userdata)
            .Where(d => Path.GetFileName(d) != "0" && Path.GetFileName(d) != "anonymous")
            .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
