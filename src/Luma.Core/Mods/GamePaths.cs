using Luma.Core.Detection;

namespace Luma.Core.Mods;

/// <summary>Where a game's mods go on this device: the profile's modsPath with its tokens resolved.</summary>
public static class GamePaths
{
    /// <summary>
    /// The absolute mods folder. Windows locations ({documents}, {appdata}...) are inside the
    /// game's Proton prefix; null when the game needs a prefix that doesn't exist yet (start the
    /// game once). Relative paths are under the game folder, matched case-insensitively.
    /// </summary>
    public static string? ModsDir(DetectedGame game, string home) => ResolvePath(game, game.Profile.ModsPath, home);

    /// <summary>A profile path (relative to the game folder, or starting with a token like {localappdata}) on this device.</summary>
    public static string? ResolvePath(DetectedGame game, string path, string home)
    {
        var p = path.Replace('\\', '/');
        string root;
        string rest;
        if (p.StartsWith('{'))
        {
            var end = p.IndexOf('}');
            var token = p[..(end + 1)];
            rest = p[(end + 1)..].TrimStart('/');
            var user = game.Install.Prefix == null ? null : Path.Combine(game.Install.Prefix, "drive_c", "users", "steamuser");
            root = token switch
            {
                "{xdgdata}" => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share"),
                "{xdgconfig}" => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(home, ".config"),
                "{documents}" when user != null => Path.Combine(user, "Documents"),
                "{mygames}" when user != null => Path.Combine(user, "Documents", "My Games"),
                "{appdata}" when user != null => Path.Combine(user, "AppData", "Roaming"),
                "{localappdata}" when user != null => Path.Combine(user, "AppData", "Local"),
                _ => "",
            };
            if (root.Length == 0)
            {
                return null;
            }
        }
        else
        {
            root = game.GameDir;
            rest = p;
        }
        return rest.Length == 0 ? root : Resolve(root, rest);
    }

    /// <summary>
    /// root/relative, reusing folders that exist with different letter case ("data" → "Data"), so
    /// a mod never creates a second, ignored copy of a game folder on a case-sensitive disk.
    /// </summary>
    public static string Resolve(string root, string relative)
    {
        var cur = root;
        var exists = Directory.Exists(cur);
        foreach (var part in relative.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (exists)
            {
                var exact = Path.Combine(cur, part);
                if (Directory.Exists(exact) || File.Exists(exact))
                {
                    cur = exact;
                    continue;
                }
                var match = Directory.EnumerateFileSystemEntries(cur)
                    .FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    cur = match;
                    continue;
                }
                exists = false;
            }
            cur = Path.Combine(cur, part);
        }
        return cur;
    }
}
