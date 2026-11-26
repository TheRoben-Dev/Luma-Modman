using Luma.Core.Detection;
using Luma.Core.Games;
using Xunit;

namespace Luma.Core.Tests;

public class DetectionTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "luma-det-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Dir(string relative) => Directory.CreateDirectory(Path.Combine(_home, relative)).FullName;

    [Fact]
    public void KeyValuesParsesNestingCommentsEscapesAndCase()
    {
        var kv = KeyValues.Parse("""
            // comment
            "Root"
            {
                "name"  "Say \"hi\""   [$WIN32]
                "Inner" { "Key" "v" }
                bare value
            }
            """);
        var root = kv.Map("root")!;
        Assert.Equal("Say \"hi\"", root.Str("NAME"));
        Assert.Equal("v", kv.Path("Root", "inner")!.Str("key"));
        Assert.Equal("value", root.Str("bare"));
        Assert.Throws<InvalidDataException>(() => KeyValues.Parse("\"a\" { \"b\" \"c\""));
    }

    /// <summary>A Steam Deck layout: internal library plus an SD card, a Proton prefix, launch options and art.</summary>
    private (string Root, string Sd) FakeSteam()
    {
        var root = Path.Combine(_home, ".local/share/Steam");
        var sd = Dir("media/sdcard");
        Write(".local/share/Steam/steamapps/libraryfolders.vdf", $$"""
            "libraryfolders"
            {
                "0" { "path" "{{root}}" "apps" { "489830" "1" } }
                "1" { "path" "{{sd}}" }
                "2" { "path" "/nonexistent/drive" }
            }
            """);
        Write(".local/share/Steam/steamapps/appmanifest_489830.acf", """
            "AppState" { "appid" "489830" "name" "The Elder Scrolls V: Skyrim Special Edition" "installdir" "Skyrim Special Edition" }
            """);
        Write(".local/share/Steam/steamapps/common/Skyrim Special Edition/SkyrimSE.exe", "");
        Dir(".local/share/Steam/steamapps/compatdata/489830/pfx/drive_c");
        Write("media/sdcard/steamapps/appmanifest_1966720.acf", """
            "AppState" { "appid" "1966720" "name" "Lethal Company" "installdir" "Lethal Company" }
            """);
        Write("media/sdcard/steamapps/common/Lethal Company/Lethal Company.exe", "");
        // Uninstalled leftover: manifest without a folder.
        Write("media/sdcard/steamapps/appmanifest_292030.acf", """
            "AppState" { "appid" "292030" "name" "The Witcher 3" "installdir" "The Witcher 3" }
            """);
        // An unsupported game.
        Write(".local/share/Steam/steamapps/appmanifest_12345.acf", """
            "AppState" { "appid" "12345" "name" "Some Puzzle Game" "installdir" "Puzzle" }
            """);
        Dir(".local/share/Steam/steamapps/common/Puzzle");
        Write(".local/share/Steam/config/config.vdf", """
            "InstallConfigStore" { "Software" { "Valve" { "Steam" { "CompatToolMapping" {
                "1966720" { "name" "GE-Proton9-20" "config" "" "priority" "250" }
            } } } } }
            """);
        Write(".local/share/Steam/userdata/123/config/localconfig.vdf", """
            "UserLocalConfigStore" { "Software" { "Valve" { "Steam" { "apps" {
                "489830" { "LaunchOptions" "PROTON_LOG=1 %command%" }
            } } } } }
            """);
        Write(".local/share/Steam/appcache/librarycache/489830/abc123/library_600x900.jpg", "jpg");
        // ~/.steam/steam is a symlink to the same root on real systems.
        Directory.CreateDirectory(Path.Combine(_home, ".steam"));
        Directory.CreateSymbolicLink(Path.Combine(_home, ".steam/steam"), root);
        return (root, sd);
    }

    [LinuxFact]
    public void SteamScanFindsAllLibrariesPrefixesOptionsAndArt()
    {
        var (root, sd) = FakeSteam();
        var scanner = new SteamScanner(_home);
        Assert.Single(scanner.Roots());
        Assert.Equal([root, sd], SteamScanner.Libraries(root));

        var games = scanner.Scan().ToDictionary(g => g.SourceId);
        Assert.Equal(["12345", "1966720", "489830"], games.Keys.Order());
        var skyrim = games["489830"];
        Assert.Equal(Path.Combine(root, "steamapps/compatdata/489830/pfx"), skyrim.Prefix);
        Assert.Equal("PROTON_LOG=1 %command%", skyrim.LaunchOptions);
        Assert.EndsWith("library_600x900.jpg", skyrim.Cover);
        var lethal = games["1966720"];
        Assert.Equal(sd, lethal.Library);
        Assert.Equal("GE-Proton9-20", lethal.CompatTool);
        Assert.Null(lethal.Prefix);  // never launched yet
    }

    [LinuxFact]
    public void FlatpakSteamIsFound()
    {
        Write(".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/appmanifest_413150.acf",
            "\"AppState\" { \"appid\" \"413150\" \"name\" \"Stardew Valley\" \"installdir\" \"Stardew Valley\" }");
        Dir(".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley");
        var g = Assert.Single(new SteamScanner(_home).Scan());
        Assert.Equal("flatpak", g.Packaging);
    }

    [LinuxFact]
    public void HeroicEpicAndGogInstallsWithPrefixes()
    {
        var game = Dir("Games/Heroic/Cyberpunk 2077");
        Write("Games/Heroic/Cyberpunk 2077/bin/x64/Cyberpunk2077.exe", "");
        var pfx = Dir("Games/Heroic/Prefixes/cp/pfx/drive_c");
        Write(".config/heroic/legendaryConfig/legendary/installed.json", $$"""
            { "Ginger": { "app_name": "Ginger", "title": "Cyberpunk 2077", "install_path": "{{game}}", "platform": "Windows" } }
            """);
        Write(".config/heroic/GamesConfig/Ginger.json", $$"""
            { "Ginger": { "winePrefix": "{{Path.Combine(_home, "Games/Heroic/Prefixes/cp")}}" } }
            """);
        var gog = Dir("Games/Heroic/Stardew Valley");
        Write(".config/heroic/gog_store/installed.json", $$"""
            { "installed": [ { "appName": "1453375253", "install_path": "{{gog}}", "platform": "linux" } ] }
            """);
        var installs = new HeroicScanner(_home).Scan();
        var epic = Assert.Single(installs, i => i.Source == GameSource.Epic);
        Assert.Equal("Cyberpunk 2077", epic.Title);
        Assert.Equal(Path.GetDirectoryName(pfx), epic.Prefix);
        var g = Assert.Single(installs, i => i.Source == GameSource.Gog);
        Assert.Null(g.Prefix);  // a native Linux build has no prefix

        // Epic ids aren't in the profile, so the detect file finds it.
        var detector = new GameDetector(GameCatalog.Load(), _home);
        var d = Assert.Single(detector.Match(installs), x => x.Install.Source == GameSource.Epic);
        Assert.Equal("cyberpunk2077", d.Profile.Id);
    }

    [LinuxFact]
    public void LutrisGameFoundFromExeDeepInTheFolder()
    {
        Write("Games/skyrim/drive_c/Games/Skyrim/SkyrimSE.exe", "");
        Write(".config/lutris/games/skyrim-special-edition-1700000000.yml", $$"""
            game:
              exe: {{Path.Combine(_home, "Games/skyrim/drive_c/Games/Skyrim/SkyrimSE.exe")}}
              prefix: {{Path.Combine(_home, "Games/skyrim")}}
              args: ''
            system:
              env:
                exe: not-this-one
            wine:
              version: lutris-GE
            """);
        var install = Assert.Single(new LutrisScanner(_home).Scan());
        Assert.Equal("skyrim-special-edition", install.SourceId);
        Assert.Equal("Skyrim Special Edition", install.Title);
        Assert.Equal(Path.Combine(_home, "Games/skyrim"), install.Prefix);
        var d = new GameDetector(GameCatalog.Load(), _home).Match(install);
        Assert.Equal("skyrimspecialedition", d?.Profile.Id);
    }

    [LinuxFact]
    public void DetectorMatchesSteamByAppIdAndSkipsUnsupported()
    {
        FakeSteam();
        var detector = new GameDetector(GameCatalog.Load(), _home);
        var found = detector.Scan();
        Assert.Equal(["lethalcompany", "skyrimspecialedition"], found.Select(f => f.Profile.Id).Order());
        Assert.Equal(3, detector.LastInstalls.Count);
        Assert.All(found, f => Assert.StartsWith("steam:", f.Key));
    }

    [Fact]
    public void CaseInsensitivePathLookup()
    {
        Write("g/Bin/X64/Game.EXE", "");
        Assert.NotNull(PathUtil.FindCaseInsensitive(Path.Combine(_home, "g"), "bin/x64/game.exe"));
        Assert.Null(PathUtil.FindCaseInsensitive(Path.Combine(_home, "g"), "bin/x64/other.exe"));
    }
}

public class WindowsPlatformTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "luma-win-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void AppPathsUseAppDataOnWindows()
    {
        var p = new AppPaths(v => v switch
        {
            "USERPROFILE" => @"C:\Users\me",
            "LOCALAPPDATA" => @"C:\Users\me\AppData\Local",
            "APPDATA" => @"C:\Users\me\AppData\Roaming",
            _ => null,
        }, windows: true);
        Assert.Equal(@"C:\Users\me", p.Home);
        Assert.Equal(Path.Combine(@"C:\Users\me\AppData\Local", "Luma"), p.Data);
        Assert.Equal(Path.Combine(@"C:\Users\me\AppData\Roaming", "Luma"), p.Config);
        Assert.StartsWith(p.Data, p.Cache);
    }

    [Fact]
    public void EpicLauncherManifestsAreRead()
    {
        var game = Directory.CreateDirectory(Path.Combine(_root, "Games", "Hades")).FullName;
        var manifests = Directory.CreateDirectory(Path.Combine(_root, "Manifests")).FullName;
        File.WriteAllText(Path.Combine(manifests, "A.item"), $$"""
            { "AppName": "Min", "DisplayName": "Hades", "InstallLocation": {{System.Text.Json.JsonSerializer.Serialize(game)}}, "MainGameAppName": "Min" }
            """);
        File.WriteAllText(Path.Combine(manifests, "B.item"), $$"""
            { "AppName": "MinDlc", "DisplayName": "Hades DLC", "InstallLocation": {{System.Text.Json.JsonSerializer.Serialize(game)}}, "MainGameAppName": "Min" }
            """);
        File.WriteAllText(Path.Combine(manifests, "C.item"), """{ "AppName": "Gone", "InstallLocation": "/nowhere" }""");
        File.WriteAllText(Path.Combine(manifests, "D.item"), "not json");
        var found = Assert.Single(WindowsLaunchers.Epic(manifests));
        Assert.Equal(GameSource.Epic, found.Source);
        Assert.Equal("Min", found.SourceId);
        Assert.Equal("Hades", found.Title);
        Assert.Equal(WindowsLaunchers.Packaging, found.Packaging);
    }

    [Fact]
    public void WindowsModsPathOverridesLinuxOnlyLocations()
    {
        var catalog = GameCatalog.Load();
        foreach (var g in catalog.Games.Where(g => g.Moddable))
        {
            var path = g.WindowsModsPath ?? g.ModsPath;
            Assert.False(path.StartsWith("{xdg", StringComparison.Ordinal), $"{g.Id} has no Windows mods location ({g.ModsPath})");
        }
        Assert.Empty(catalog.Problems);
    }

    [WindowsFact]
    public void WindowsTokensAreTheUsersOwnFolders()
    {
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Luma.Core.Mods.GamePaths.WindowsFolder("{documents}"));
        Assert.EndsWith("My Games", Luma.Core.Mods.GamePaths.WindowsFolder("{mygames}"));
        Assert.Equal("", Luma.Core.Mods.GamePaths.WindowsFolder("{xdgdata}"));
    }

    [WindowsFact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WindowsRegistryLookupsDontThrow()
    {
        _ = WindowsSteam.Roots();
        _ = WindowsSteam.Running();
        _ = WindowsLaunchers.Gog();
        Assert.False(Platform.ProcessAlive(-1));
        Assert.True(Platform.ProcessAlive(Environment.ProcessId));
    }
}
