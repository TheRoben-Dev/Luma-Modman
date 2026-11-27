using System.IO.Compression;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Xunit;

namespace Luma.Core.Tests;

public sealed class GameSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "luma-setup-" + Guid.NewGuid().ToString("N")[..8]);

    public GameSetupTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

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

    private static readonly PluginSettings Sse = new() { File = "{localappdata}/Skyrim Special Edition/plugins.txt", Style = "asterisk", Base = ["Skyrim.esm", "Update.esm"] };

    [Fact]
    public void BuiltInBethesdaProfilesHavePluginLists()
    {
        var c = GameCatalog.Load();
        var sse = c.Games.Single(g => g.Id == "skyrimspecialedition");
        Assert.Equal("asterisk", sse.Plugins!.Style);
        Assert.DoesNotContain(sse.Quirks, q => q.Contains("doesn't turn plugins"));
        var fo4 = c.Games.Single(g => g.Id == "fallout4");
        Assert.Contains(fo4.Ini, i => i.Key == "bInvalidateOlderFiles" && i.Value == "1" && i.File.EndsWith("Fallout4Custom.ini"));
        Assert.True(c.Games.Single(g => g.Id == "newvegas").Plugins!.Timestamps);
    }

    [Fact]
    public void LoadOrderKeepsForeignPluginsAndPutsMastersFirst()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var p = new GameProfile { Id = "g", Name = "G", Steam = [1], ModsPath = "Data", Sites = new SiteIds { Nexus = "g" } };
        store.Install(Zip("a.zip", "A.esp", "textures/a.dds"), new InstalledMod { Id = "nexus:1", Name = "A" }, p);
        store.Install(Zip("b.zip", "B.esm", "B - Patch.esp"), new InstalledMod { Id = "nexus:2", Name = "B" }, p);
        store.Install(Zip("c.zip", "C.esp"), new InstalledMod { Id = "nexus:3", Name = "C" }, p);
        store.SetEnabled("nexus:3", false);
        store.SetPluginEnabled("B - Patch.esp", false);

        var game = Path.Combine(_dir, "game");
        var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);
        var plan = Deployer.Plan(store, game, data);
        var current = new[] { "# comment", "*Skyrim.esm", "*ccBGSSSE001-Fish.esm", "Hand.esp", "*A.esp" };
        var list = GameSetup.Plugins(plan, data, store.State, Sse, current);

        Assert.Equal(["ccBGSSSE001-Fish.esm", "B.esm", "Hand.esp", "A.esp", "B - Patch.esp"], list.Select(x => x.Name));
        Assert.Null(list[0].ModId);
        Assert.Equal("nexus:2", list[1].ModId);
        Assert.False(list.Single(x => x.Name == "Hand.esp").Enabled);   // the player's own choice is kept
        Assert.False(list.Single(x => x.Name == "B - Patch.esp").Enabled);
        Assert.DoesNotContain(list, x => x.Name == "C.esp");             // its mod is off

        var txt = GameSetup.PluginsTxt(list, Sse).Split("\r\n").Where(l => !l.StartsWith('#') && l.Length > 0).ToList();
        Assert.Equal(["*ccBGSSSE001-Fish.esm", "*B.esm", "Hand.esp", "*A.esp", "B - Patch.esp"], txt);

        var plain = new PluginSettings { File = Sse.File, Style = "plain", Base = ["Skyrim.esm"] };
        var plainTxt = GameSetup.PluginsTxt(list, plain, ["Skyrim.esm"]).Split("\r\n").Where(l => !l.StartsWith('#') && l.Length > 0);
        Assert.Equal(["Skyrim.esm", "ccBGSSSE001-Fish.esm", "B.esm", "A.esp"], plainTxt);
    }

    [Fact]
    public void PluginHeadersGiveFlagsAndMasters()
    {
        var esp = Path.Combine(_dir, "Flagged.esp");
        File.WriteAllBytes(esp, PluginFile.Make(PluginFile.MasterFlag | 0x200, "Skyrim.esm", "Base Mod.esm"));
        var h = PluginFile.Read(esp, 0x200);
        Assert.True(h.Master);
        Assert.True(h.Light);
        Assert.Equal(["Skyrim.esm", "Base Mod.esm"], h.Masters);
        Assert.False(PluginFile.Read(esp, 0).Light);  // a game without light plugins
        // Starfield's light flag is 0x100; an empty or foreign file is judged by its name.
        File.WriteAllBytes(esp, PluginFile.Make(0x100));
        Assert.True(PluginFile.Read(esp, 0x100).Light);
        File.WriteAllText(Path.Combine(_dir, "x.esl"), "not a plugin");
        Assert.Equal(new PluginHeader(true, true, []), PluginFile.Read(Path.Combine(_dir, "x.esl"), 0x200) with { Masters = [] });
        Assert.Equal(PluginHeader.None.Master, PluginFile.Read(Path.Combine(_dir, "missing.esp"), 0x200).Master);
    }

    [Fact]
    public void SortPutsPluginsAfterTheirMastersAndProblemsAreFound()
    {
        PluginEntry P(string name, bool master = false, bool on = true, params string[] needs) => new(name, "local:x", on, master, false, needs);
        var list = new List<PluginEntry>
        {
            P("Lib.esm", master: true, needs: "Core.esm"),
            P("Core.esm", master: true),
            P("Patch.esp", needs: ["Main.esp", "Lib.esm"]),
            P("Main.esp"),
            P("Other.esp"),
            P("Broken.esp", needs: "Gone.esp"),
        };
        var problems = GameSetup.Problems(list, Sse);
        Assert.Contains(problems, p => p.Plugin == "Lib.esm" && p.Text.Contains("loads before Core.esm"));
        Assert.Contains(problems, p => p.Plugin == "Patch.esp" && p.Text.Contains("Main.esp"));
        Assert.Contains(problems, p => p.Plugin == "Broken.esp" && p.Text.Contains("needs Gone.esp"));

        var sorted = GameSetup.SortPlugins(list);
        Assert.Equal(["Core.esm", "Lib.esm", "Main.esp", "Patch.esp", "Other.esp", "Broken.esp"], sorted.Select(p => p.Name));
        Assert.Equal(["Broken.esp"], GameSetup.Problems(sorted, Sse).Select(p => p.Plugin));

        // A saved order wins; a plugin new since stays after the one it followed.
        var applied = GameSetup.ApplyOrder(list, ["Other.esp", "Main.esp"]);
        Assert.Equal(["Patch.esp", "Other.esp", "Broken.esp", "Main.esp"], applied.Where(p => !p.Master).Select(p => p.Name));

        var many = Enumerable.Range(0, 260).Select(i => P($"p{i}.esp")).ToList();
        Assert.Contains(GameSetup.Problems(many, Sse), p => p.Plugin == null && p.Text.Contains("more than the 254"));
    }

    [Fact]
    public void IniValuesAreSetInTheirSection()
    {
        var ini = "[General]\r\nsLanguage=EN\r\n\r\n[Archive]\r\nbInvalidateOlderFiles=0\r\n\r\n[Display]\r\niSize W=1280\r\n";
        var a = GameSetup.SetIni(ini, "Archive", "bInvalidateOlderFiles", "1");
        Assert.Contains("[Archive]\r\nbInvalidateOlderFiles=1\r\n", a);
        var b = GameSetup.SetIni(a, "archive", "sResourceDataDirsFinal", "");
        Assert.Contains("bInvalidateOlderFiles=1\r\nsResourceDataDirsFinal=\r\n\r\n[Display]", b);
        Assert.Equal("[Archive]\r\nbInvalidateOlderFiles=1\r\n", GameSetup.SetIni("", "Archive", "bInvalidateOlderFiles", "1"));
        Assert.Contains("iSize W=1280", b);
    }

    [LinuxFact]
    public void DeployWritesPluginsAndIniAndUndeployPutsThemBack()
    {
        var prefix = Path.Combine(_dir, "pfx");
        var user = Path.Combine(prefix, "drive_c", "users", "steamuser");
        var plugins = Path.Combine(user, "AppData", "Local", "Game", "plugins.txt");
        var custom = Path.Combine(user, "Documents", "My Games", "Game", "GameCustom.ini");
        var mainIni = Path.Combine(user, "Documents", "My Games", "Game", "Game.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(plugins)!);
        File.WriteAllText(plugins, "*Mine.esp\r\n");
        var profile = new GameProfile
        {
            Id = "g", Name = "G", Steam = [1], ModsPath = "Data", Sites = new SiteIds { Nexus = "g" },
            Plugins = new PluginSettings { File = "{localappdata}/Game/plugins.txt", Style = "asterisk" },
            Ini = [new IniSetting("{mygames}/Game/GameCustom.ini", "Archive", "bInvalidateOlderFiles", "1"), new IniSetting("{mygames}/Game/Game.ini", "Archive", "bInvalidateOlderFiles", "1")],
        };
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, "Data"));
        var game = new DetectedGame { Profile = profile, Install = new InstalledGame { Source = GameSource.Steam, SourceId = "1", Prefix = prefix }, GameDir = gameDir };
        var store = new ModStore(Path.Combine(_dir, "store"), game.Key);
        store.Install(Zip("x.zip", "X.esp"), new InstalledMod { Id = "nexus:9", Name = "X" }, profile);

        var manifest = Path.Combine(_dir, "deploy.json");
        var backups = Path.Combine(_dir, "backup");
        var data = Path.Combine(gameDir, "Data");
        var plan = Deployer.Plan(store, gameDir, data);
        var m = Deployer.Deploy(plan, game.Key, manifest, backups);
        var written = GameSetup.Apply(game, _dir, plan, data, store.State, m, backups);
        Deployer.Write(manifest, m);

        Assert.Equal(["Mine.esp", "X.esp"], written.Select(p => p.Name));
        Assert.Contains("*X.esp", File.ReadAllText(plugins));
        Assert.Contains("*Mine.esp", File.ReadAllText(plugins));
        Assert.Equal("[Archive]\r\nbInvalidateOlderFiles=1\r\n", File.ReadAllText(custom));
        Assert.False(File.Exists(mainIni));  // the game makes its main INI; Luma doesn't create it

        Deployer.Undeploy(manifest);
        Assert.Equal("*Mine.esp\r\n", File.ReadAllText(plugins));
        Assert.False(File.Exists(custom));
        Assert.False(File.Exists(Path.Combine(data, "X.esp")));
    }

    [LinuxFact]
    public void ProfilesKeepTheirOwnSavesAndIniFiles()
    {
        var prefix = Path.Combine(_dir, "pfx");
        var games = Path.Combine(prefix, "drive_c", "users", "steamuser", "Documents", "My Games", "Game");
        var saves = Path.Combine(games, "Saves");
        var ini = Path.Combine(games, "GamePrefs.ini");
        var custom = Path.Combine(games, "GameCustom.ini");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, "global.ess"), "global");
        File.WriteAllText(ini, "[Display]\r\niSize=1\r\n");
        var profile = new GameProfile
        {
            Id = "g", Name = "G", Steam = [1], ModsPath = "Data", Sites = new SiteIds { Nexus = "g" },
            Ini = [new IniSetting("{mygames}/Game/GameCustom.ini", "Archive", "bInvalidateOlderFiles", "1")],
            Saves = "{mygames}/Game/Saves", Inis = ["{mygames}/Game/GamePrefs.ini", "{mygames}/Game/GameCustom.ini"],
        };
        Assert.Empty(profile.Validate(new HashSet<string>()));
        var gameDir = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(gameDir, "Data"));
        var game = new DetectedGame { Profile = profile, Install = new InstalledGame { Source = GameSource.Steam, SourceId = "1", Prefix = prefix }, GameDir = gameDir };
        var store = new ModStore(Path.Combine(_dir, "store"), game.Key);
        store.Install(Zip("x.zip", "X.esp", "textures/a.dds", "textures/b.dds", "readme.txt"), new InstalledMod { Id = "nexus:9", Name = "X" }, profile);
        store.SetHidden("nexus:9", ["mods/textures/", "mods/readme.txt"], true);
        store.SetHidden("nexus:9", ["mods/textures/a.dds"], true);  // already hidden with its folder
        Assert.Equal(["mods/readme.txt", "mods/textures/"], store.State.Find("nexus:9")!.Hidden);
        store.UseProfile("Run 2");
        var dir = store.ProfileDir("Run 2");
        ProfileFiles.Seed(game, _dir, dir, saves: true, copySaves: false);
        ProfileFiles.Seed(game, _dir, dir, saves: false, copySaves: false);
        store.SetLocal(true, true);
        store.SetLocal(false, true);

        var manifest = Path.Combine(_dir, "deploy.json");
        var backups = Path.Combine(_dir, "backup");
        var data = Path.Combine(gameDir, "Data");
        var plan = Deployer.Plan(store, gameDir, data);
        Assert.Equal(["X.esp"], plan.Files.Keys.Select(Path.GetFileName));  // hidden files stay out
        var m = Deployer.Deploy(plan, game.Key, manifest, backups);
        ProfileFiles.Place(game, _dir, store.State, dir, m, backups);
        GameSetup.Apply(game, _dir, plan, data, store.State, m, backups);
        Deployer.Write(manifest, m);

        Assert.False(File.Exists(Path.Combine(saves, "global.ess")));  // the profile starts with no saves
        File.WriteAllText(Path.Combine(saves, "run2.ess"), "mine");
        File.WriteAllText(ini, "[Display]\r\niSize=2\r\n");  // the game changes its settings
        Assert.Contains("bInvalidateOlderFiles=1", File.ReadAllText(custom));

        Deployer.Undeploy(manifest);
        Assert.Equal("global", File.ReadAllText(Path.Combine(saves, "global.ess")));
        Assert.False(File.Exists(Path.Combine(saves, "run2.ess")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(dir, "saves", "run2.ess")));
        Assert.Equal("[Display]\r\niSize=1\r\n", File.ReadAllText(ini));
        Assert.Contains("iSize=2", File.ReadAllText(Path.Combine(dir, "inis", "GamePrefs.ini")));
        Assert.False(File.Exists(custom));
        Assert.False(Directory.Exists(saves + ProfileFiles.AsideSuffix));

        // The next deploy brings the profile's save and settings back.
        m = Deployer.Deploy(Deployer.Plan(store, gameDir, data), game.Key, manifest, backups);
        ProfileFiles.Place(game, _dir, store.State, dir, m, backups);
        Deployer.Write(manifest, m);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(saves, "run2.ess")));
        Assert.Contains("iSize=2", File.ReadAllText(ini));
        Deployer.Undeploy(manifest);
        store.RenameProfile("Run 2", "Run Two");
        Assert.True(File.Exists(Path.Combine(store.ProfileDir("Run Two"), "saves", "run2.ess")));
        Assert.Equal(["Run Two"], store.State.LocalSaves);
    }
}
