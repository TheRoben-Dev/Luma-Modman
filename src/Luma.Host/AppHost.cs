using System.Text.Json;
using System.Text.Json.Serialization;
using Luma.Core;
using Luma.Host.Updates;

namespace Luma.Host;

/// <summary>Luma's own app settings.</summary>
public sealed record AppSettings
{
    /// <summary>UI theme id (see the app's theme list); unknown ids fall back to "dark".</summary>
    public string Theme { get; init; } = "dark";

    /// <summary>Accent colour per theme id (a swatch id from the app's list); missing = the theme's own.</summary>
    public Dictionary<string, string> Accents { get; init; } = new();

    /// <summary>Turns off UI animations.</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>CRT scanlines over the UI in any theme.</summary>
    public bool Crt { get; init; }

    /// <summary>CRT effect strength when <see cref="Crt"/> is on: 1 subtle, 2 medium, 3 strong.</summary>
    public int CrtLevel { get; init; } = 2;

    /// <summary>The theme's slow animated background behind the menus.</summary>
    public bool AnimatedBackground { get; init; } = true;

    /// <summary>Menu sounds, 0 (off) to 100.</summary>
    public int SoundVolume { get; init; } = 50;

    /// <summary>A light rumble on the pad for menu moves, presses and errors.</summary>
    public bool Haptics { get; init; } = true;

    /// <summary>Check GitHub Releases for a new Luma version at startup.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Last app version whose "What's new" was shown; null on a fresh install.</summary>
    public string? LastSeenVersion { get; init; }

    /// <summary>The game every tab works on ("steam:489830"); null = the first moddable one found.</summary>
    public string? ActiveGame { get; init; }
}

/// <summary>
/// Composition root shared by the Godot UI and the headless CLI (`luma`), so a game started from
/// Steam with Luma's launch wrapper goes through exactly the same code as the UI.
/// </summary>
public sealed class AppHost
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AppPaths Paths { get; }
    public AppSettings Settings { get; private set; }
    public AppUpdater Updater { get; }
    public GameLibrary Games { get; }
    public CredentialStore Keys { get; }

    private readonly Lazy<ModSites> _sites;
    private readonly Lazy<DownloadQueue> _downloads;

    /// <summary>The mod sites (created on first use).</summary>
    public ModSites Sites => _sites.Value;

    /// <summary>The download queue (created on first use; the CLI never starts it).</summary>
    public DownloadQueue Downloads => _downloads.Value;

    /// <summary>Installed mods and deploying them, for every game.</summary>
    public ModManager Mods { get; }

    public AppHost(AppPaths? paths = null, HttpClient? http = null)
    {
        Paths = paths ?? new AppPaths();
        Directory.CreateDirectory(Paths.Config);
        Directory.CreateDirectory(Paths.Data);
        Settings = LoadJson<AppSettings>(Paths.SettingsFile) ?? new AppSettings();
        Updater = new AppUpdater(http);
        Games = new GameLibrary(Paths);
        Keys = new CredentialStore(Path.Combine(Paths.Config, "credentials.json"));
        _sites = new Lazy<ModSites>(() => new ModSites(Keys));
        _downloads = new Lazy<DownloadQueue>(() => new DownloadQueue(Paths));
        Mods = new ModManager(Paths, Games.Catalog);
    }

    /// <summary>The app's pid file, so `luma nxm` knows whether to start the app.</summary>
    public string PidFile => Path.Combine(Paths.Runtime, "luma-app.pid");

    /// <summary>The game the player is working on: the one they picked, else the first moddable one found.</summary>
    public Luma.Core.Detection.DetectedGame? ActiveGame =>
        Games.Find(Settings.ActiveGame) ?? Games.Games.FirstOrDefault(g => g.Profile.Moddable) ?? Games.Games.FirstOrDefault();

    public void SetActiveGame(string key) => SaveSettings(Settings with { ActiveGame = key });

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        SaveJson(Paths.SettingsFile, settings);
    }

    internal static T? LoadJson<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }

    private bool _autoInstall;

    /// <summary>Installs each finished download for its game (the app turns this on at start).</summary>
    public void StartAutoInstall()
    {
        if (_autoInstall)
        {
            return;
        }
        _autoInstall = true;
        Downloads.Changed += item =>
        {
            if (item is { State: DownloadState.Done, Installed: false, InstallError: null, Path: not null })
            {
                Task.Run(() => InstallDownload(item));
            }
        };
    }

    private readonly object _installLock = new();

    /// <summary>Installs a finished download for the game it was fetched for.</summary>
    public void InstallDownload(DownloadItem item)
    {
        lock (_installLock)
        {
            if (item.Installed || item.Path == null)
            {
                return;
            }
            var game = Games.Find(item.Game);
            if (game == null)
            {
                item.InstallError = "Downloaded. Luma hasn't found the game it's for on this device, so it wasn't installed.";
                Downloads.Update(item);
                return;
            }
            try
            {
                Mods.Install(game, item.Path, new Luma.Core.Mods.InstalledMod
                {
                    Id = $"{item.Site}:{item.ModId}",
                    Site = item.Site,
                    ModId = item.ModId,
                    Name = item.ModName,
                    Author = item.Author,
                    Version = item.Version,
                    FileId = item.FileId,
                    PageUrl = item.PageUrl,
                    Dependencies = item.Dependencies,
                });
                item.Installed = true;
                item.InstallError = null;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            {
                item.InstallError = e.Message;
                AppLog.Warn($"install failed: {item.ModName}: {e.Message}");
            }
            Downloads.Update(item);
        }
    }
}
