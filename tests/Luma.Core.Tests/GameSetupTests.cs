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
}
