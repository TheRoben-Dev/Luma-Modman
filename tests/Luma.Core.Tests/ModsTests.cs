using System.IO.Compression;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Luma.Core.Sources;
using Xunit;

namespace Luma.Core.Tests;

public sealed class ModsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "luma-mods-" + Guid.NewGuid().ToString("N")[..8]);

    public ModsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static GameProfile Profile(string modsPath, string? loader = null) =>
        new() { Id = "g", Name = "Game", Steam = [1], ModsPath = modsPath, Loader = loader, Sites = new SiteIds { Nexus = "g" } };

    private string Zip(string name, params string[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            using var w = new StreamWriter(z.CreateEntry(e).Open());
            w.Write("content of " + e);
        }
        return path;
    }

    private List<string> Layout(string zip, GameProfile p, string folder = "Mod")
    {
        var dest = Path.Combine(_dir, "x-" + Guid.NewGuid().ToString("N")[..6]);
        Archive.Extract(zip, dest);
        return ModLayout.Plan(dest, p, folder).Select(e => $"{e.Root}:{e.Target}").Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void ThunderstorePluginGoesIntoItsOwnFolder()
    {
        var zip = Zip("a.zip", "manifest.json", "icon.png", "README.md", "MoreCompany.dll", "assets/hats.bundle");
        Assert.Equal(["Mods:MoreCompany/MoreCompany.dll", "Mods:MoreCompany/assets/hats.bundle"], Layout(zip, Profile("BepInEx/plugins", "bepinex"), "MoreCompany"));
    }

    [Fact]
    public void BepInExFoldersMapUnderBepInEx()
    {
        var zip = Zip("b.zip", "manifest.json", "plugins/x.dll", "config/x.cfg");
        Assert.Equal(["Game:BepInEx/config/x.cfg", "Game:BepInEx/plugins/x.dll"], Layout(zip, Profile("BepInEx/plugins", "bepinex")));
    }

    [Fact]
    public void LoaderPackUnwrapsToTheGameFolder()
    {
        var zip = Zip("c.zip", "manifest.json", "icon.png", "BepInExPack/winhttp.dll", "BepInExPack/doorstop_config.ini", "BepInExPack/BepInEx/core/BepInEx.dll");
        Assert.Equal(["Game:BepInEx/core/BepInEx.dll", "Game:doorstop_config.ini", "Game:winhttp.dll"], Layout(zip, Profile("BepInEx/plugins", "bepinex")));
    }

    [Fact]
    public void WrapperFolderAndDataFolderForSkyrim()
    {
        var p = Profile("Data");
        Assert.Equal(["Mods:a.esp", "Mods:textures/t.dds"], Layout(Zip("d.zip", "Cool Mod 1.2/a.esp", "Cool Mod 1.2/textures/t.dds"), p));
        Assert.Equal(["Game:Data/a.esp"], Layout(Zip("e.zip", "Data/a.esp"), p));
    }

    [Fact]
    public void UnrealPaksFindTheirFolder()
    {
        var p = Profile("Pagoda/Content/Paks/~mods");
        Assert.Equal(["Mods:x_P.pak"], Layout(Zip("f.zip", "x_P.pak"), p));
        Assert.Equal(["Game:Pagoda/Content/Paks/~mods/x_P.pak"], Layout(Zip("g.zip", "~mods/x_P.pak"), p));
        Assert.Equal(["Game:Pagoda/Content/Paks/LogicMods/y.pak"], Layout(Zip("h.zip", "Paks/LogicMods/y.pak"), p));
    }

    [Fact]
    public void ModsFolderOutsideTheGameIsStripped()
    {
        var p = Profile("{appdata}/Balatro/Mods");
        Assert.Equal(["Mods:Steamodded/main.lua"], Layout(Zip("i.zip", "Mods/Steamodded/main.lua"), p));
    }

    [Fact]
    public void ZipSlipIsRefused()
    {
        var zip = Zip("evil.zip", "../../escape.txt");
        Assert.Throws<InvalidDataException>(() => Archive.Extract(zip, Path.Combine(_dir, "evil")));
    }

    [Fact]
    public void InstallDeployUndeployRoundTrip()
    {
        var game = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(game, "Data"));
        File.WriteAllText(Path.Combine(game, "Data", "shared.txt"), "original");
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var p = Profile("Data");
        store.Install(Zip("m1.zip", "shared.txt", "one.esp"), new InstalledMod { Id = "nexus:1", Name = "One", Version = "1.0" }, p);
        store.Install(Zip("m2.zip", "Shared.txt", "meshes/two.nif"), new InstalledMod { Id = "nexus:2", Name = "Two", Version = "1.0" }, p);

        var plan = Deployer.Plan(store, game, GamePaths.Resolve(game, "Data"));
        var c = Assert.Single(plan.Conflicts);
        Assert.Equal("nexus:2", c.Winner);
        Assert.Equal(["nexus:1"], c.Losers);
        Assert.Equal(3, plan.Files.Count);

        var manifest = Path.Combine(_dir, "deploy.json");
        var m = Deployer.Deploy(plan, "steam:1", manifest, Path.Combine(_dir, "backup"));
        Assert.Equal("content of Shared.txt", File.ReadAllText(Path.Combine(game, "Data", "shared.txt")));
        Assert.True(File.Exists(Path.Combine(game, "Data", "meshes", "two.nif")));
        Assert.Single(m.Backups);

        // Reordering changes the winner on the next deploy.
        store.Move("nexus:1", +1);
        Deployer.Deploy(Deployer.Plan(store, game, GamePaths.Resolve(game, "Data")), "steam:1", manifest, Path.Combine(_dir, "backup"));
        Assert.Equal("content of shared.txt", File.ReadAllText(Path.Combine(game, "Data", "shared.txt")));

        Deployer.Undeploy(manifest);
        Assert.Equal("original", File.ReadAllText(Path.Combine(game, "Data", "shared.txt")));
        Assert.Equal(["shared.txt"], Directory.EnumerateFileSystemEntries(Path.Combine(game, "Data")).Select(Path.GetFileName).ToList());
        Assert.False(File.Exists(manifest));
    }

    [Fact]
    public void FilesTheGameMakesGoToOverwriteAndComeBack()
    {
        var game = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(game, "Data"));
        File.WriteAllText(Path.Combine(game, "Data", "game.esm"), "base");
        File.WriteAllText(Path.Combine(game, "Data", "cfg.ini"), "game cfg");
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var p = Profile("Data");
        store.Install(Zip("m1.zip", "one.esp", "meshes/a.nif", "cfg.ini"), new InstalledMod { Id = "nexus:1", Name = "One" }, p);
        var data = GamePaths.Resolve(game, "Data");
        var manifest = Path.Combine(_dir, "deploy.json");
        Deployer.Deploy(Deployer.Plan(store, game, data), "steam:1", manifest, Path.Combine(_dir, "backup"));

        // While the game runs: a tool writes a patch, a folder of output, a file next to a mod's,
        // and the game saves over a mod's config (replacing the link); saves and the game folder itself are left alone.
        File.WriteAllText(Path.Combine(data, "patch.esp"), "made");
        Directory.CreateDirectory(Path.Combine(data, "output", "deep"));
        File.WriteAllText(Path.Combine(data, "output", "deep", "b.nif"), "made");
        File.WriteAllText(Path.Combine(data, "meshes", "gen.nif"), "made");
        File.Delete(Path.Combine(data, "cfg.ini"));
        File.WriteAllText(Path.Combine(data, "cfg.ini"), "edited");
        File.WriteAllText(Path.Combine(game, "log.txt"), "game root");
        File.WriteAllText(Path.Combine(data, "quick.ess"), "save");

        Deployer.Undeploy(manifest);
        Assert.Equal(["mods/cfg.ini", "mods/meshes/gen.nif", "mods/output/deep/b.nif", "mods/patch.esp"], store.OverwriteFiles());
        Assert.Equal("game cfg", File.ReadAllText(Path.Combine(data, "cfg.ini")));
        Assert.Equal(["cfg.ini", "game.esm", "quick.ess"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName).Order().ToList());
        Assert.True(File.Exists(Path.Combine(game, "log.txt")));

        // Next time Overwrite goes in last, so the edited config wins over the mod's.
        var plan = Deployer.Plan(store, game, data);
        Assert.Contains(plan.Conflicts, c => c.Winner == ModStore.OverwriteId);
        Deployer.Deploy(plan, "steam:1", manifest, Path.Combine(_dir, "backup"));
        Assert.Equal("edited", File.ReadAllText(Path.Combine(data, "cfg.ini")));
        Assert.Equal("made", File.ReadAllText(Path.Combine(data, "output", "deep", "b.nif")));
        Deployer.Undeploy(manifest);
        Assert.Equal(4, store.OverwriteFiles().Count);

        var mod = store.OverwriteToMod("Generated");
        Assert.Empty(store.OverwriteFiles());
        Assert.Equal("local:Generated", mod.Id);
        Assert.True(store.State.IsEnabled(mod.Id));
    }

    [Fact]
    public void AnUnpackedFolderInstallsLikeAnArchive()
    {
        var folder = Path.Combine(_dir, "SkyUI_5.2");
        Directory.CreateDirectory(Path.Combine(folder, "interface"));
        File.WriteAllText(Path.Combine(folder, "SkyUI_SE.esp"), "p");
        File.WriteAllText(Path.Combine(folder, "interface", "skyui.swf"), "s");
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var mod = store.Install(folder, new InstalledMod { Id = "local:SkyUI_5.2", Site = "local", Name = "SkyUI_5.2" }, Profile("Data"));
        Assert.Equal(2, mod.Files);
        Assert.Equal(["mods/interface/skyui.swf", "mods/SkyUI_SE.esp"], store.ModFiles(mod.Id).Select(f => f.Key));
        Assert.True(File.Exists(Path.Combine(folder, "SkyUI_SE.esp")));  // the original folder stays
    }

    [Fact]
    public void NamesNotesCategoriesSeparatorsAndProfileRenames()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var p = Profile("Data");
        store.Install(Zip("a1.zip", "a.esp"), new InstalledMod { Id = "nexus:1", Name = "Site Name", Version = "1" }, p);
        store.Install(Zip("b1.zip", "b.esp"), new InstalledMod { Id = "nexus:2", Name = "Other" }, p);
        store.SetModInfo("nexus:1", name: "My Name", notes: "keep below B", category: "UI");
        // An update keeps the player's name, notes and category.
        store.Install(Zip("a2.zip", "a.esp"), new InstalledMod { Id = "nexus:1", Name = "Site Name v2", Version = "2" }, p);
        var a = store.State.Find("nexus:1")!;
        Assert.Equal(("My Name", "keep below B", "UI", "2"), (a.Name, a.Notes, a.Category, a.Version));
        Assert.Equal(["UI"], store.State.Categories());

        var sep = store.AddSeparator("Interface", "nexus:2");
        Assert.Equal(["nexus:1", sep, "nexus:2"], store.State.Order.Select(e => e.Id));
        Assert.Equal(0, store.State.Pending());
        Assert.Equal(2, store.State.Enabled().Count());
        store.ForgetMissing();
        Assert.Contains(store.State.Order, e => e.Id == sep);
        store.RenameSeparator(sep, "UI mods");
        Assert.Equal("UI mods", store.State.Separators[sep]);

        store.UseProfile("Survival");
        store.SetPluginOrder(["b.esp", "a.esp"]);
        store.RenameProfile("Survival", "Hardcore");
        Assert.Equal("Hardcore", store.State.Active);
        Assert.Equal(["b.esp", "a.esp"], store.State.PluginOrder["Hardcore"]);
        Assert.Throws<InvalidOperationException>(() => store.RenameProfile(GameMods.DefaultProfile, "X"));
        store.RemoveSeparator(sep);
        Assert.True(store.State.Separators.ContainsKey(sep));  // still in the Default profile
        store.UseProfile(GameMods.DefaultProfile);
        store.RemoveSeparator(sep);
        Assert.False(store.State.Separators.ContainsKey(sep));
    }

    [Fact]
    public void ProfilesDependenciesAndRollbackHistory()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var p = Profile("BepInEx/plugins", "bepinex");
        var v1 = Zip("x1.zip", "X.dll");
        store.Install(v1, new InstalledMod
        {
            Id = "thunderstore:a-X", ModId = "a-X", Name = "X", Version = "1.0",
            Dependencies = [new ModDependency("thunderstore", "BepInEx-BepInExPack", "BepInExPack")],
        }, p);
        Assert.Single(store.MissingDependencies());
        store.Install(Zip("x2.zip", "X.dll"), store.State.Find("thunderstore:a-X")! with { Version = "2.0" }, p);
        var mod = store.State.Find("thunderstore:a-X")!;
        Assert.Equal("2.0", mod.Version);
        Assert.Equal(v1, Assert.Single(mod.History).Archive);

        store.UseProfile("Vanilla", copyActive: false);
        Assert.Empty(store.State.Enabled());
        store.UseProfile(GameMods.DefaultProfile);
        Assert.Single(store.State.Enabled());
        store.SetEnabled("thunderstore:a-X", false);
        Assert.Empty(store.MissingDependencies());

        var reloaded = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        Assert.Equal(["Default", "Vanilla"], reloaded.State.Profiles.Keys.Order());
        reloaded.Uninstall("thunderstore:a-X");
        Assert.Empty(reloaded.State.Mods);
        Assert.False(Directory.Exists(reloaded.ModDir("thunderstore:a-X")));
    }

    [LinuxFact]
    public void ModsDirResolvesTokensInsideThePrefix()
    {
        var install = new InstalledGame { InstallDir = "/g", Prefix = "/pfx" };
        var game = new DetectedGame { Profile = Profile("{localappdata}/Pagoda/Saved"), Install = install, GameDir = "/g" };
        Assert.Equal("/pfx/drive_c/users/steamuser/AppData/Local/Pagoda/Saved", GamePaths.ModsDir(game, "/home/x"));
        Assert.Null(GamePaths.ModsDir(game with { Install = install with { Prefix = null } }, "/home/x"));
    }

    [Fact]
    public void LoaderGoesToItsFolder()
    {
        var p = Profile("Pagoda/Content/Paks/~mods", "ue4ss") with { Detect = ["Pagoda/Binaries/Win64/Pagoda-Win64-Shipping.exe"] };
        var prefix = Luma.Host.LoaderInstaller.InstallPath(p, new LoaderInfo { Id = "ue4ss", InstallDir = "{exeDir}" });
        Assert.Equal("Pagoda/Binaries/Win64", prefix);
        var dest = Path.Combine(_dir, "ue4ss");
        Archive.Extract(Zip("UE4SS_v4.0.0.zip", "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/Mods/mods.txt"), dest);
        Assert.Equal(["Game:Pagoda/Binaries/Win64/dwmapi.dll", "Game:Pagoda/Binaries/Win64/ue4ss/Mods/mods.txt", "Game:Pagoda/Binaries/Win64/ue4ss/UE4SS.dll"],
            ModLayout.Plan(dest, p, "ue4ss", prefix).Select(e => $"{e.Root}:{e.Target}").Order(StringComparer.Ordinal).ToList());
        Assert.Equal("", Luma.Host.LoaderInstaller.InstallPath(Profile("BepInEx/plugins", "bepinex"), new LoaderInfo { Id = "bepinex" }));
    }

    [Fact]
    public void ScriptExtenderAddedByHandInstallsAsTheLoader()
    {
        var skse = new LoaderInfo { Id = "skse64", Name = "SKSE64", Launcher = "skse64_loader.exe" };
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:489830");
        var zip = Zip("skse64_2_02_06.zip", "skse64_2_02_06/skse64_loader.exe", "skse64_2_02_06/skse64_1_6_1170.dll",
            "skse64_2_02_06/Data/Scripts/Actor.pex", "skse64_2_02_06/src/skse64/main.cpp", "skse64_2_02_06/skse64_readme.txt");
        var mod = store.Install(zip, new InstalledMod { Id = "local:skse64_2_02_06", Site = "local", Name = "skse64_2_02_06" }, Profile("Data", "skse64"), loader: skse);
        Assert.Equal("loader:skse64", mod.Id);
        Assert.Equal("SKSE64", mod.Name);
        var files = Directory.EnumerateFiles(store.ModDir(mod.Id), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(store.ModDir(mod.Id), f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["game/Data/Scripts/Actor.pex", "game/skse64_1_6_1170.dll", "game/skse64_loader.exe"], files);

        // An ordinary mod is left alone.
        var other = store.Install(Zip("plain.zip", "plain.esp"), new InstalledMod { Id = "local:plain", Site = "local", Name = "plain" }, Profile("Data", "skse64"), loader: skse);
        Assert.Equal("local:plain", other.Id);
    }

    [Fact]
    public void LauncherInASubfolderPutsTheLoaderThere()
    {
        var obse = new LoaderInfo { Id = "obse64", Name = "OBSE64", Launcher = "OblivionRemastered/Binaries/Win64/obse64_loader.exe" };
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:2623190");
        var mod = store.Install(Zip("obse64.zip", "obse64_loader.exe", "obse64_0_411_140.dll"), new InstalledMod { Id = "local:obse64", Site = "local", Name = "obse64" },
            Profile("OblivionRemastered/Content/Dev/ObvData/Data", "obse64"), loader: obse);
        Assert.True(File.Exists(Path.Combine(store.ModDir(mod.Id), "game", "OblivionRemastered", "Binaries", "Win64", "obse64_loader.exe")));
        Assert.Equal("OblivionRemastered/Binaries/Win64", Luma.Host.LoaderInstaller.InstallPath(Profile("x", "obse64"), obse));
    }

    [Fact]
    public void SteamCommandStartsThroughTheLauncher()
    {
        var game = "/home/deck/.steam/steam/steamapps/common/Skyrim Special Edition";
        string[] cmd = ["/home/deck/.steam/steam/ubuntu12_32/reaper", "SteamLaunch", "AppId=489830", "--",
            "/home/deck/.steam/steam/ubuntu12_32/steam-launch-wrapper", "--",
            "/home/deck/.steam/steam/steamapps/common/Proton - Experimental/proton", "waitforexitandrun", game + "/SkyrimSELauncher.exe"];
        var through = Luma.Host.LaunchCommand.ThroughLauncher(cmd, game, "skse64_loader.exe");
        Assert.Equal(Path.Combine(game, "skse64_loader.exe"), through[^1]);
        Assert.Equal(cmd[..^1], through[..^1]);
        // Windows: Steam's command is the game's own program.
        var win = Luma.Host.LaunchCommand.ThroughLauncher([@"C:\Games\Steam\steamapps\common\Skyrim Special Edition\SkyrimSE.exe"], @"C:\Games\Steam\steamapps\common\Skyrim Special Edition", "skse64_loader.exe");
        Assert.EndsWith("skse64_loader.exe", win[0]);
        Assert.StartsWith(@"C:\Games", win[0]);
        // Nothing in the game folder to replace: unchanged.
        string[] other = ["/usr/bin/env", "/opt/other/game.exe"];
        Assert.Same(other, Luma.Host.LaunchCommand.ThroughLauncher(other, game, "skse64_loader.exe"));
    }
}
