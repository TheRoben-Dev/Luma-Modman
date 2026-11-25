namespace Luma.Core.Detection;

/// <summary>
/// Lutris games from their per-game YAML files (games/&lt;slug&gt;-&lt;id&gt;.yml), native or Flatpak.
/// Only the few keys Luma needs are read (game: exe, prefix, working_dir), so no YAML library.
/// </summary>
public sealed class LutrisScanner
{
    private readonly string _home;

    public LutrisScanner(string home) => _home = home;

    public IReadOnlyList<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        foreach (var (dir, kind) in new[]
        {
            (Path.Combine(_home, ".config/lutris/games"), "native"),
            (Path.Combine(_home, ".local/share/lutris/games"), "native"),
            (Path.Combine(_home, ".var/app/net.lutris.Lutris/config/lutris/games"), "flatpak"),
            (Path.Combine(_home, ".var/app/net.lutris.Lutris/data/lutris/games"), "flatpak"),
        })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(dir, "*.yml"))
            {
                try
                {
                    if (Read(file) is { } g)
                    {
                        games.Add(g with { Packaging = kind });
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return games;
    }

    public static InstalledGame? Read(string file)
    {
        var game = GameSection(File.ReadAllLines(file));
        var exe = game.GetValueOrDefault("exe");
        if (string.IsNullOrEmpty(exe))
        {
            return null;
        }
        var prefix = game.GetValueOrDefault("prefix");
        // A relative exe is relative to the prefix (Wine games) or the working dir.
        if (!Path.IsPathRooted(exe))
        {
            var baseDir = game.GetValueOrDefault("working_dir") ?? prefix;
            if (baseDir == null)
            {
                return null;
            }
            exe = Path.Combine(baseDir, exe);
        }
        var dir = Path.GetDirectoryName(exe);
        if (dir == null || !Directory.Exists(dir))
        {
            return null;
        }
        var slug = Path.GetFileNameWithoutExtension(file);
        var dash = slug.LastIndexOf('-');
        if (dash > 0 && slug[(dash + 1)..].All(char.IsAsciiDigit))
        {
            slug = slug[..dash];
        }
        return new InstalledGame
        {
            Source = GameSource.Lutris,
            SourceId = slug,
            Title = string.Join(' ', slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..])),
            InstallDir = dir,
            Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
        };
    }

    /// <summary>The scalar keys directly under the top-level "game:" key.</summary>
    internal static Dictionary<string, string> GameSection(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>();
        var inGame = false;
        var indent = -1;
        foreach (var raw in lines)
        {
            if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#'))
            {
                continue;
            }
            var lead = raw.Length - raw.TrimStart().Length;
            if (lead == 0)
            {
                inGame = raw.TrimEnd() == "game:";
                indent = -1;
                continue;
            }
            if (!inGame)
            {
                continue;
            }
            if (indent < 0)
            {
                indent = lead;
            }
            if (lead != indent)
            {
                continue;  // nested deeper (lists, maps)
            }
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }
            var value = line[(colon + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '\'' && value[^1] == '\'' || value[0] == '"' && value[^1] == '"'))
            {
                value = value[1..^1];
            }
            if (value.Length > 0)
            {
                map[line[..colon].Trim()] = value;
            }
        }
        return map;
    }
}
