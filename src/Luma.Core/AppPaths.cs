namespace Luma.Core;

/// <summary>
/// Luma's own directories: XDG on Linux, %LOCALAPPDATA%\Luma and %APPDATA%\Luma on Windows. Mods, downloads and backups live here, never inside a game folder:
/// games only see mods through links that are put in at launch and taken out again afterwards.
/// </summary>
public sealed class AppPaths
{
    public const string Name = "luma";

    public string Data { get; }
    public string Config { get; }
    public string Cache { get; }
    public string Runtime { get; }
    public string Home { get; }

    /// <summary>Folders shown by a shorter name: %LOCALAPPDATA% and %APPDATA% on Windows, then ~ for home.</summary>
    private readonly List<(string Root, string Name)> _short = new();
    private readonly bool _windows;

    /// <param name="env">Environment variables (tests pass their own).</param>
    /// <param name="windows">Windows layout; null = the OS Luma runs on.</param>
    public AppPaths(Func<string, string?>? env = null, bool? windows = null)
    {
        env ??= System.Environment.GetEnvironmentVariable;
        _windows = windows ?? Platform.IsWindows;
        if (_windows)
        {
            Home = NonEmpty(env("USERPROFILE")) ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            var localRoot = NonEmpty(env("LOCALAPPDATA")) ?? Path.Combine(Home, "AppData", "Local");
            var roamingRoot = NonEmpty(env("APPDATA")) ?? Path.Combine(Home, "AppData", "Roaming");
            _short.Add((localRoot, "%LOCALAPPDATA%"));
            _short.Add((roamingRoot, "%APPDATA%"));
            _short.Add((Home, "~"));
            var local = Path.Combine(localRoot, "Luma");
            Data = local;
            Config = Path.Combine(roamingRoot, "Luma");
            Cache = Path.Combine(local, "cache");
            Runtime = Path.Combine(local, "run");
            return;
        }
        Home = env("HOME") ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        _short.Add((Home, "~"));
        Data = Path.Combine(Xdg(env, "XDG_DATA_HOME", Home, ".local/share"), Name);
        Config = Path.Combine(Xdg(env, "XDG_CONFIG_HOME", Home, ".config"), Name);
        Cache = Path.Combine(Xdg(env, "XDG_CACHE_HOME", Home, ".cache"), Name);
        var runtime = env("XDG_RUNTIME_DIR");
        Runtime = string.IsNullOrEmpty(runtime) ? Path.Combine(Path.GetTempPath(), Name + "-" + System.Environment.UserName) : Path.Combine(runtime, Name);
    }

    /// <summary>
    /// A path for showing: "~/.local/share/luma/mods", "%LOCALAPPDATA%\\Luma\\mods". The folder
    /// itself is unchanged; only paths inside home (or AppData on Windows) get shorter.
    /// </summary>
    public string Short(string path)
    {
        foreach (var (root, name) in _short)
        {
            var r = root.TrimEnd('/', '\\');
            if (r.Length > 0 && path.StartsWith(r, _windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && (path.Length == r.Length || path[r.Length] is '/' or '\\'))
            {
                return name + path[r.Length..];
            }
        }
        return path;
    }

    public string SettingsFile => Path.Combine(Config, "settings.json");

    /// <summary>Extracted mods, one folder per game, then per mod.</summary>
    public string ModsDir => Path.Combine(Data, "mods");

    /// <summary>Downloaded archives, kept so a mod can be reinstalled or rolled back offline.</summary>
    public string DownloadsDir => Path.Combine(Data, "downloads");

    /// <summary>Per-game mod profiles (enabled mods, load order).</summary>
    public string ProfilesDir => Path.Combine(Config, "profiles");

    /// <summary>Detected games from the last scan.</summary>
    public string LibraryCacheFile => Path.Combine(Cache, "library.json");

    /// <summary>Cover art and screenshots fetched for the UI.</summary>
    public string ArtCacheDir => Path.Combine(Cache, "art");

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static string Xdg(Func<string, string?> env, string name, string home, string fallback)
    {
        var v = env(name);
        return string.IsNullOrEmpty(v) ? Path.Combine(home, fallback) : v;
    }
}
