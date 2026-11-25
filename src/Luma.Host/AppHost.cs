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

    public AppHost(AppPaths? paths = null, HttpClient? http = null)
    {
        Paths = paths ?? new AppPaths();
        Directory.CreateDirectory(Paths.Config);
        Directory.CreateDirectory(Paths.Data);
        Settings = LoadJson<AppSettings>(Paths.SettingsFile) ?? new AppSettings();
        Updater = new AppUpdater(http);
        Games = new GameLibrary(Paths);
    }

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
}
