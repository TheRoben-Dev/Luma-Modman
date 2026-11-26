using System.Text.Json;

namespace Luma.Core.Detection;

/// <summary>
/// Heroic Games Launcher installs (Epic through Legendary, GOG, Amazon through Nile), from Heroic's
/// own JSON files, native or Flatpak (or %APPDATA%\heroic on Windows). The Wine prefix comes from each game's Heroic settings.
/// </summary>
public sealed class HeroicScanner
{
    private readonly string _home;

    public HeroicScanner(string home) => _home = home;

    public IEnumerable<(string Dir, string Packaging)> ConfigDirs()
    {
        if (Platform.IsWindows)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic");
            if (Directory.Exists(dir))
            {
                yield return (dir, "native");
            }
            yield break;
        }
        foreach (var (dir, kind) in new[]
        {
            (Path.Combine(_home, ".config/heroic"), "native"),
            (Path.Combine(_home, ".var/app/com.heroicgameslauncher.hgl/config/heroic"), "flatpak"),
        })
        {
            if (Directory.Exists(dir))
            {
                yield return (dir, kind);
            }
        }
    }

    public IReadOnlyList<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        foreach (var (dir, kind) in ConfigDirs())
        {
            games.AddRange(Safe(() => Legendary(dir)).Select(g => g with { Packaging = kind }));
            games.AddRange(Safe(() => Gog(dir)).Select(g => g with { Packaging = kind }));
            games.AddRange(Safe(() => Nile(dir)).Select(g => g with { Packaging = kind }));
        }
        return games.Where(g => Directory.Exists(g.InstallDir)).ToList();
    }

    private static IEnumerable<InstalledGame> Safe(Func<IEnumerable<InstalledGame>> read)
    {
        try
        {
            return read().ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }

    private static IEnumerable<InstalledGame> Legendary(string heroic)
    {
        var file = Path.Combine(heroic, "legendaryConfig", "legendary", "installed.json");
        if (!File.Exists(file))
        {
            yield break;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var app = p.Value;
            var path = Str(app, "install_path");
            if (path == null)
            {
                continue;
            }
            yield return new InstalledGame
            {
                Source = GameSource.Epic,
                SourceId = Str(app, "app_name") ?? p.Name,
                Title = Str(app, "title") ?? Path.GetFileName(path),
                InstallDir = path,
                Prefix = PrefixFor(heroic, Str(app, "app_name") ?? p.Name, Str(app, "platform")),
            };
        }
    }

    private static IEnumerable<InstalledGame> Gog(string heroic)
    {
        var file = Path.Combine(heroic, "gog_store", "installed.json");
        if (!File.Exists(file))
        {
            yield break;
        }
        var titles = GogTitles(heroic);
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (!doc.RootElement.TryGetProperty("installed", out var list))
        {
            yield break;
        }
        foreach (var app in list.EnumerateArray())
        {
            var id = Str(app, "appName");
            var path = Str(app, "install_path");
            if (id == null || path == null)
            {
                continue;
            }
            yield return new InstalledGame
            {
                Source = GameSource.Gog,
                SourceId = id,
                Title = titles.GetValueOrDefault(id) ?? Path.GetFileName(path.TrimEnd('/')),
                InstallDir = path,
                Prefix = PrefixFor(heroic, id, Str(app, "platform")),
            };
        }
    }

    /// <summary>GOG titles from Heroic's library cache (the installed list has none).</summary>
    private static Dictionary<string, string> GogTitles(string heroic)
    {
        var map = new Dictionary<string, string>();
        foreach (var file in new[] { Path.Combine(heroic, "store_cache", "gog_library.json"), Path.Combine(heroic, "gog_store", "library.json") })
        {
            if (!File.Exists(file))
            {
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("games", out var games))
                {
                    foreach (var g in games.EnumerateArray())
                    {
                        if (Str(g, "app_name") is { } id && Str(g, "title") is { } t)
                        {
                            map.TryAdd(id, t);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
        return map;
    }

    private static IEnumerable<InstalledGame> Nile(string heroic)
    {
        var file = Path.Combine(heroic, "nile_config", "nile", "installed.json");
        if (!File.Exists(file))
        {
            yield break;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (var app in doc.RootElement.EnumerateArray())
        {
            var id = Str(app, "id");
            var path = Str(app, "path");
            if (id == null || path == null)
            {
                continue;
            }
            yield return new InstalledGame
            {
                Source = GameSource.Amazon,
                SourceId = id,
                Title = Path.GetFileName(path.TrimEnd('/')),
                InstallDir = path,
                Prefix = PrefixFor(heroic, id, "windows"),
            };
        }
    }

    /// <summary>winePrefix from GamesConfig/&lt;app&gt;.json; null for native Linux builds or when not set.</summary>
    private static string? PrefixFor(string heroic, string appName, string? platform)
    {
        if (platform is "linux" || Platform.IsWindows)
        {
            return null;
        }
        var file = Path.Combine(heroic, "GamesConfig", appName + ".json");
        if (!File.Exists(file))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty(appName, out var cfg) && Str(cfg, "winePrefix") is { Length: > 0 } pfx)
            {
                // Heroic's Proton prefixes keep drive_c under pfx/, Wine prefixes directly.
                return Directory.Exists(Path.Combine(pfx, "pfx", "drive_c")) ? Path.Combine(pfx, "pfx") : pfx;
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
