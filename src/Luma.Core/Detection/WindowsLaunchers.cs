using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace Luma.Core.Detection;

/// <summary>
/// Games installed by the official Windows launchers: the Epic Games Launcher (its manifest
/// folder in ProgramData) and GOG Galaxy (or GOG's offline installers, which register the game
/// the same way). Heroic on Windows is read by <see cref="HeroicScanner"/>.
/// </summary>
public static class WindowsLaunchers
{
    public const string Packaging = "launcher";

    /// <summary>Epic Games Launcher installs, from its *.item manifests.</summary>
    /// <param name="manifests">The manifest folder; null = %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests.</param>
    public static IReadOnlyList<InstalledGame> Epic(string? manifests = null)
    {
        manifests ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        var games = new List<InstalledGame>();
        if (!Directory.Exists(manifests))
        {
            return games;
        }
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(manifests, "*.item").ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return games;
        }
        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                var app = Str(r, "AppName");
                var dir = Str(r, "InstallLocation");
                // DLC and add-ons have their own manifest pointing at the main game: skip them.
                var isAddon = Str(r, "MainGameAppName") is { Length: > 0 } main && !string.Equals(main, app, StringComparison.OrdinalIgnoreCase);
                if (app == null || dir == null || isAddon || !Directory.Exists(dir))
                {
                    continue;
                }
                games.Add(new InstalledGame
                {
                    Source = GameSource.Epic,
                    SourceId = app,
                    Title = Str(r, "DisplayName") ?? Path.GetFileName(dir.TrimEnd('\\', '/')),
                    InstallDir = dir,
                    Packaging = Packaging,
                });
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }
        return games;
    }

    /// <summary>GOG games registered under HKLM\SOFTWARE\WOW6432Node\GOG.com\Games.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<InstalledGame> Gog()
    {
        var games = new List<InstalledGame>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games") ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GOG.com\Games");
            if (root == null)
            {
                return games;
            }
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                var id = k?.GetValue("gameID") as string ?? name;
                var dir = k?.GetValue("path") as string ?? k?.GetValue("PATH") as string;
                // DLC entries point at their base game through dependsOn.
                if (k == null || dir == null || k.GetValue("dependsOn") is string { Length: > 0 } || !Directory.Exists(dir))
                {
                    continue;
                }
                games.Add(new InstalledGame
                {
                    Source = GameSource.Gog,
                    SourceId = id,
                    Title = k.GetValue("gameName") as string ?? Path.GetFileName(dir.TrimEnd('\\', '/')),
                    InstallDir = dir,
                    Packaging = Packaging,
                });
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
        return games;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
