using System.IO.Compression;
using System.Text;
using Luma.Core.Games;
using Luma.Core.Mods;
using Luma.Host;
using Xunit;

namespace Luma.Core.Tests;

public sealed class LoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "luma-loaders-" + Guid.NewGuid().ToString("N")[..8]);

    public LoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly GameCatalog Catalog = GameCatalog.Load();

    private static LoaderInfo L(string id) => Catalog.Loaders[id];

    private static GameProfile G(string id) => Catalog.ById(id)!;

    private string Unpacked(params string[] files)
    {
        var d = Path.Combine(_dir, "u-" + Guid.NewGuid().ToString("N")[..6]);
        foreach (var f in files)
        {
            var p = Path.Combine(d, f);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, f);
        }
        return d;
    }

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

    private static List<string> Targets(string dir, GameProfile p, LoaderInfo l)
    {
        var (root, prefix) = LoaderRules.Locate(dir, p, l);
        return ModLayout.Fixed(root, prefix).Select(e => e.Target).Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void EveryLoaderAndGameReferenceIsKnown()
    {
        foreach (var l in Catalog.Loaders.Values)
        {
            Assert.All(l.Requires, r => Assert.True(Catalog.Loaders.ContainsKey(r), $"{l.Id} requires unknown {r}"));
            if (l.Source.StartsWith("page:", StringComparison.Ordinal))
            {
                Assert.NotNull(l.Asset);
            }
        }
        Assert.Equal(["cet", "red4ext", "redscript", "archivexl", "tweakxl", "codeware"], G("cyberpunk2077").AllLoaders);
        Assert.True(L("skse64").AutoInstall);
        Assert.False(L("obse64").AutoInstall);
    }

    [Fact]
    public void CyberEngineTweaksKeepsItsLayoutFromTheGameFolder()
    {
        var dir = Unpacked("bin/x64/version.dll", "bin/x64/global.ini", "bin/x64/plugins/cyber_engine_tweaks.asi", "bin/x64/plugins/cyber_engine_tweaks/fonts/a.ttf");
        Assert.Equal(["bin/x64/global.ini", "bin/x64/plugins/cyber_engine_tweaks.asi", "bin/x64/plugins/cyber_engine_tweaks/fonts/a.ttf", "bin/x64/version.dll"],
            Targets(dir, G("cyberpunk2077"), L("cet")));
    }

    [Fact]
    public void ArchiveXlPackedFromItsOwnFolderGoesUnderRed4ext()
    {
        var flat = Unpacked("ArchiveXL.dll", "Bundle/x.archive");
        Assert.Equal(["red4ext/plugins/ArchiveXL/ArchiveXL.dll", "red4ext/plugins/ArchiveXL/Bundle/x.archive"], Targets(flat, G("cyberpunk2077"), L("archivexl")));
        var full = Unpacked("red4ext/plugins/ArchiveXL/ArchiveXL.dll", "r6/scripts/ArchiveXL/a.reds");
        Assert.Equal(["r6/scripts/ArchiveXL/a.reds", "red4ext/plugins/ArchiveXL/ArchiveXL.dll"], Targets(full, G("cyberpunk2077"), L("archivexl")));
    }

    [Fact]
    public void ScriptExtenderUnwrapsItsVersionFolder()
    {
        var dir = Unpacked("skse64_2_02_06/skse64_loader.exe", "skse64_2_02_06/skse64_1_6_1170.dll", "skse64_2_02_06/Data/Scripts/a.pex", "skse64_2_02_06/readme.txt");
        Assert.Contains("skse64_loader.exe", Targets(dir, G("skyrimspecialedition"), L("skse64")));
        Assert.Contains("Data/Scripts/a.pex", Targets(dir, G("skyrimspecialedition"), L("skse64")));
    }

    [Fact]
    public void Ue4ssGoesNextToTheGamesProgram()
    {
        var dir = Unpacked("dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/Mods/mods.txt");
        Assert.Equal(["Pal/Binaries/Win64/dwmapi.dll", "Pal/Binaries/Win64/ue4ss/Mods/mods.txt", "Pal/Binaries/Win64/ue4ss/UE4SS.dll"], Targets(dir, G("palworld"), L("ue4ss")));
    }

    [Fact]
    public void ModEngine2LaunchesWithTheGamesArguments()
    {
        var er = G("eldenring");
        Assert.Equal("Game/modengine2_launcher.exe", LoaderRules.Launcher(er, L("modengine2")));
        Assert.Equal(["-t", "er", "-c", "config_eldenring.toml"], LoaderRules.LaunchArgs(er, L("modengine2")));
        var cmd = LaunchCommand.ThroughLauncher(["/proton", "waitforexitandrun", "/g/ELDEN RING/Game/start_protected_game.exe"], "/g/ELDEN RING", "Game/modengine2_launcher.exe",
            LoaderRules.LaunchArgs(er, L("modengine2")), native: false);
        Assert.Equal(["/proton", "waitforexitandrun", Path.Combine("/g/ELDEN RING", "Game/modengine2_launcher.exe"), "-t", "er", "-c", "config_eldenring.toml"], cmd);
    }

    [Fact]
    public void ModEngine2ConfigPointsAtTheModsFolder()
    {
        var mod = Unpacked("game/Game/config_eldenring.toml");
        File.WriteAllText(Path.Combine(mod, "game/Game/config_eldenring.toml"), "mods = [\n    { enabled = true, name = \"default\", path = \"mod\" }\n]\n");
        LoaderInstaller.AfterInstall(G("eldenring"), L("modengine2"), mod);
        Assert.Contains("path = \"mods\"", File.ReadAllText(Path.Combine(mod, "game/Game/config_eldenring.toml")));
    }

    [Fact]
    public void NativeLauncherReplacesTheLinuxProgram()
    {
        var cmd = LaunchCommand.ThroughLauncher(["/steam/reaper", "--", "/g/Stardew Valley/StardewValley"], "/g/Stardew Valley", "StardewModdingAPI", [], native: true);
        Assert.Equal(Path.Combine("/g/Stardew Valley", "StardewModdingAPI"), cmd[^1]);
    }

    [Fact]
    public void ModFilesShowWhichLoadersTheyNeed()
    {
        var cp = Catalog.Loaders.Values.Where(l => G("cyberpunk2077").AllLoaders.Contains(l.Id)).ToList();
        Assert.Equal(["cet"], LoaderRules.Needs(cp, ["bin/x64/plugins/cyber_engine_tweaks/mods/Foo/init.lua"]));
        Assert.Equal(["redscript"], LoaderRules.Needs(cp, ["r6/scripts/Foo/Foo.reds"]));
        Assert.Equal(["archivexl", "tweakxl"], LoaderRules.Needs(cp, ["archive/pc/mod/foo.xl", "r6/tweaks/foo.yaml"]));
        Assert.Empty(LoaderRules.Needs(cp, ["archive/pc/mod/foo.archive"]));
        Assert.True(LoaderRules.NeededBy(L("skse64"), ["SKSE/Plugins/po3_Tweaks.dll"]));
        Assert.True(LoaderRules.NeededBy(L("ue4ss"), ["Pal/Binaries/Win64/ue4ss/Mods/Foo/Scripts/main.lua"]));
        Assert.False(LoaderRules.NeededBy(L("ue4ss"), ["Pal/Content/Paks/~mods/Foo_P.pak"]));
    }

    [Fact]
    public void RequirementNamesMatchLoaders()
    {
        Assert.True(LoaderRules.IsDependency(L("cet"), "Cyber Engine Tweaks"));
        Assert.True(LoaderRules.IsDependency(L("red4ext"), "RED4ext"));
        Assert.True(LoaderRules.IsDependency(L("skse64"), "SKSE64"));
        Assert.True(LoaderRules.IsDependency(L("smapi"), "SMAPI - Stardew Modding API"));
        Assert.False(LoaderRules.IsDependency(L("cet"), "Cyberpunk 2077 HD Reworked"));
        Assert.False(LoaderRules.IsDependency(L("archivexl"), "Virtual Atelier"));
    }

    [Fact]
    public void NeededLoadersComeFirst()
    {
        Assert.Equal(["red4ext", "archivexl", "tweakxl"], LoaderRules.WithRequired(["archivexl", "tweakxl"], Catalog.Loaders));
    }

    [Fact]
    public void ReleaseFilePicking()
    {
        Assert.Equal("cet_1.35.0.zip", LoaderRules.PickAsset(["cet_1.35.0.zip", "cet_1.35.0_pdb.zip"], L("cet").Asset));
        Assert.Equal("red4ext_1.27.0.zip", LoaderRules.PickAsset(["red4ext_1.27.0_pdbs.zip", "red4ext_1.27.0.zip"], L("red4ext").Asset));
        Assert.Equal("RE4.zip", LoaderRules.PickAsset(["RE2.zip", "RE4.zip", "RE4_TDB.zip"], G("residentevil42023").LoaderAsset));
        // Without a pattern: the Windows archive, not symbols, sources or other systems.
        Assert.Equal("xNVSE_6.3.9.7z", LoaderRules.PickAsset(["Source code.zip", "xNVSE_6.3.9_pdb.7z", "xNVSE_6.3.9.7z"], null));
        Assert.Equal("BLSE-win-x64.zip", LoaderRules.PickAsset(["BLSE-linux.zip", "BLSE-win-x64.zip", "checksums.txt"], null));
        Assert.Null(LoaderRules.PickAsset(["notes.txt"], null));
    }

    [Fact]
    public void DownloadPageLinks()
    {
        const string html = """
            <a href="beta/skse64_2_02_06.7z">Current Anniversary Edition build</a>
            <a href="beta/skse64_2_00_20.7z">Special Edition 1.5.97</a>
            <a href="beta/skse_1_07_03.7z">Skyrim (2011)</a>
            """;
        Assert.Equal("https://skse.silverlock.org/beta/skse64_2_02_06.7z", LoaderRules.PickLink(html, "https://skse.silverlock.org", L("skse64").Asset!));
        Assert.Equal("https://skse.silverlock.org/beta/skse_1_07_03.7z", LoaderRules.PickLink(html, "https://skse.silverlock.org", L("skse").Asset!));
        Assert.Equal("2.02.06", LoaderRules.VersionFrom("skse64_2_02_06.7z"));
        Assert.Equal("1.35.0", LoaderRules.VersionFrom("cet_1.35.0.zip"));
    }

    [Fact]
    public void HandAddedScriptExtenderInstallsAsTheLoader()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "g");
        var p = G("fallout4");
        var zip = Zip("f4se_0_07_02.7z.zip", "f4se_0_07_02/f4se_loader.exe", "f4se_0_07_02/f4se_1_10_984.dll", "f4se_0_07_02/src/f4se/a.cpp", "f4se_0_07_02/readme.txt");
        var mod = store.Install(zip, new InstalledMod { Id = "local:f4se", Site = "local", Name = "f4se" }, p, null, [L("f4se")]);
        Assert.Equal("loader:f4se", mod.Id);
        Assert.True(File.Exists(Path.Combine(store.ModDir(mod.Id), "game", "f4se_loader.exe")));
        Assert.False(Directory.Exists(Path.Combine(store.ModDir(mod.Id), "game", "src")));
    }

    [Fact]
    public void InstalledModsRememberWhatTheyNeed()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "g");
        var p = G("cyberpunk2077");
        var loaders = p.AllLoaders.Select(L).ToList();
        var mod = store.Install(Zip("m.zip", "archive/pc/mod/car.archive", "archive/pc/mod/car.xl", "r6/scripts/car.reds"), new InstalledMod { Id = "nexus:1", Name = "Car" }, p, null, loaders);
        Assert.Equal(["redscript", "archivexl"], mod.Needs);
        Assert.Equal(new HashSet<string> { "redscript", "archivexl" }, store.NeededLoaders(loaders));
        store.SetEnabled("nexus:1", false);
        Assert.Empty(store.NeededLoaders(loaders));
    }

    [Fact]
    public void KnownFoldersAreNotUnwrapped()
    {
        string Plan(GameProfile p, params string[] files) => string.Join(",", ModLayout.Plan(Unpacked(files), p, "M").Select(e => $"{e.Root}:{e.Target}").Order(StringComparer.Ordinal));
        Assert.Equal("Mods:SKSE/Plugins/po3_Tweaks.dll", Plan(G("skyrimspecialedition"), "SKSE/Plugins/po3_Tweaks.dll"));
        Assert.Equal("Mods:meshes/armor/a.nif", Plan(G("skyrimspecialedition"), "Cool Armor 1.0/meshes/armor/a.nif"));
        Assert.Equal("Mods:bin/x64/plugins/cyber_engine_tweaks/mods/Foo/init.lua", Plan(G("cyberpunk2077"), "bin/x64/plugins/cyber_engine_tweaks/mods/Foo/init.lua"));
    }

    [Fact]
    public void ThunderstorePackStandsInForBepInEx()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "lc");
        var p = G("lethalcompany");
        var game = new Luma.Core.Detection.DetectedGame { Profile = p, Install = new Luma.Core.Detection.InstalledGame { InstallDir = _dir }, GameDir = _dir };
        store.Install(Zip("mc.zip", "manifest.json", "BepInEx/plugins/MoreCompany.dll"), new InstalledMod
        {
            Id = "thunderstore:notnotnotswipez-MoreCompany", Site = "thunderstore", Name = "MoreCompany",
            Dependencies = [new Luma.Core.Sources.ModDependency("thunderstore", "BepInEx-BepInExPack", "BepInExPack", "5.4.2100")],
        }, p, null, [L("bepinex")]);
        Assert.Empty(LoaderInstaller.Missing(game, Catalog, store));
        store.Install(Zip("x.zip", "manifest.json", "X.dll"), new InstalledMod { Id = "thunderstore:a-X", Site = "thunderstore", Name = "X" }, p, null, [L("bepinex")]);
        Assert.Empty(LoaderInstaller.Missing(game, Catalog, store));
    }

    [Fact]
    public void FlatArchivesFollowTheGamesFileRules()
    {
        var p = G("cyberpunk2077");
        var dir = Unpacked("car.archive", "car.xl", "readme.txt");
        Assert.Equal(["Game:archive/pc/mod/car.archive", "Game:archive/pc/mod/car.xl"],
            ModLayout.Plan(dir, p, "Car").Select(e => $"{e.Root}:{e.Target}").Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Ue4ssLuaModsGoInUe4ssMods()
    {
        var p = G("palworld");
        var dir = Unpacked("Fast Travel-123-1-0/FastTravel/Scripts/main.lua", "Fast Travel-123-1-0/FastTravel/config.lua");
        Assert.Equal(["Game:Pal/Binaries/Win64/ue4ss/Mods/FastTravel/Scripts/main.lua", "Game:Pal/Binaries/Win64/ue4ss/Mods/FastTravel/config.lua", "Game:Pal/Binaries/Win64/ue4ss/Mods/FastTravel/enabled.txt"],
            ModLayout.Plan(dir, p, "Fast Travel").Select(e => $"{e.Root}:{e.Target}").Order(StringComparer.Ordinal).ToList());
        var bare = Unpacked("Scripts/main.lua");
        Assert.Contains("Game:Pal/Binaries/Win64/ue4ss/Mods/FastTravel/Scripts/main.lua", ModLayout.Plan(bare, p, "FastTravel").Select(e => $"{e.Root}:{e.Target}"));
        var pak = Unpacked("Foo_P.pak");
        Assert.Equal(["Mods:Foo_P.pak"], ModLayout.Plan(pak, p, "Foo").Select(e => $"{e.Root}:{e.Target}").ToList());
    }

    [Fact]
    public void WitcherModsKeepTheirModFolder()
    {
        var p = G("witcher3");
        string Plan(string dir, string name) => string.Join(",", ModLayout.Plan(dir, p, name).Select(e => $"{e.Root}:{e.Target}").Order(StringComparer.Ordinal));
        Assert.Equal("Mods:modFoo/content/scripts/a.ws", Plan(Unpacked("Foo-12-1-0/modFoo/content/scripts/a.ws"), "Foo"));
        Assert.Equal("Mods:modFoo/content/scripts/a.ws", Plan(Unpacked("content/scripts/a.ws"), "Foo"));
        Assert.Equal("Game:Mods/modFoo/content/a.bundle,Game:dlc/dlcFoo/content/a.bundle", Plan(Unpacked("Mods/modFoo/content/a.bundle", "dlc/dlcFoo/content/a.bundle"), "Foo"));
    }

    [Fact]
    public void WitcherScriptClashes()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "w3");
        var p = G("witcher3");
        store.Install(Zip("a.zip", "modA/content/scripts/game/player/r4Player.ws"), new InstalledMod { Id = "nexus:1", Name = "A" }, p);
        store.Install(Zip("b.zip", "modB/content/scripts/game/player/r4Player.ws", "modB/content/scripts/local/b.ws"), new InstalledMod { Id = "nexus:2", Name = "B" }, p);
        var clashes = store.ScriptClashes(p.ScriptMerge!);
        Assert.Equal(["game/player/r4Player.ws"], clashes.Keys.ToList());
        Assert.Equal(["A", "B"], clashes["game/player/r4Player.ws"]);
        store.Install(Zip("m.zip", "mod0000_MergedFiles/content/scripts/game/player/r4Player.ws"), new InstalledMod { Id = "local:merged", Site = "local", Name = "Merged" }, p);
        Assert.Empty(store.ScriptClashes(p.ScriptMerge!));
    }

    private static byte[] Lz4Literal(byte[] data)
    {
        var o = new List<byte>();
        var n = data.Length;
        o.Add((byte)((n >= 15 ? 15 : n) << 4));
        if (n >= 15)
        {
            var rest = n - 15;
            while (rest >= 255)
            {
                o.Add(255);
                rest -= 255;
            }
            o.Add((byte)rest);
        }
        o.AddRange(data);
        return o.ToArray();
    }

    private string Pak(string name, string metaXml)
    {
        var meta = Encoding.UTF8.GetBytes(metaXml);
        var path = Path.Combine(_dir, name);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(Encoding.ASCII.GetBytes("LSPK"));
        w.Write(18u);
        var listOffsetPos = fs.Position;
        w.Write(0ul);
        w.Write(0u);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write(new byte[16]);
        w.Write((ushort)1);
        var dataOffset = fs.Position;
        w.Write(meta);
        var entry = new byte[272];
        Encoding.UTF8.GetBytes("Mods/Cool_Mod/meta.lsx").CopyTo(entry, 0);
        BitConverter.GetBytes((uint)dataOffset).CopyTo(entry, 256);
        entry[263] = 0;  // stored
        BitConverter.GetBytes(meta.Length).CopyTo(entry, 264);
        BitConverter.GetBytes(meta.Length).CopyTo(entry, 268);
        var list = Lz4Literal(entry);
        var listOffset = fs.Position;
        w.Write(1);
        w.Write(list.Length);
        w.Write(list);
        fs.Position = listOffsetPos;
        w.Write((ulong)listOffset);
        return path;
    }

    private const string Meta = """
        <?xml version="1.0" encoding="UTF-8"?>
        <save><region id="Config"><node id="root"><children>
          <node id="ModuleInfo">
            <attribute id="Folder" type="LSString" value="Cool_Mod"/>
            <attribute id="Name" type="LSString" value="Cool Mod"/>
            <attribute id="UUID" type="FixedString" value="11111111-2222-3333-4444-555555555555"/>
            <attribute id="Version64" type="int64" value="36028797018963969"/>
          </node>
        </children></node></region></save>
        """;

    [Fact]
    public void PakMetaIsRead()
    {
        var m = Larian.Module(Pak("Cool.pak", Meta));
        Assert.NotNull(m);
        Assert.Equal("11111111-2222-3333-4444-555555555555", m!.Uuid);
        Assert.Equal("Cool_Mod", m.Folder);
        Assert.Equal("36028797018963969", m.Version64);
    }

    [Fact]
    public void InfoJsonWinsOverThePak()
    {
        var info = """{"Mods":[{"Author":"x","Name":"Other","Folder":"Other","Version":"36028797018963970","UUID":"aaaaaaaa-0000-0000-0000-000000000000"}],"MD5":"abc"}""";
        var m = Larian.FromInfo(info, "Other");
        Assert.Equal(new LarianModule("aaaaaaaa-0000-0000-0000-000000000000", "Other", "Other", "36028797018963970", "abc"), m);
    }

    [Fact]
    public void ModSettingsKeepTheGameAndAddModsInOrder()
    {
        var a = new LarianModule("aaaaaaaa-0000-0000-0000-000000000000", "A", "A");
        var b = new LarianModule("bbbbbbbb-0000-0000-0000-000000000000", "B", "B");
        var first = Larian.ModSettings(null, [a, b], new HashSet<string> { a.Uuid, b.Uuid });
        Assert.True(first.IndexOf("GustavX", StringComparison.Ordinal) < first.IndexOf("aaaaaaaa", StringComparison.Ordinal));
        Assert.True(first.IndexOf("aaaaaaaa", StringComparison.Ordinal) < first.IndexOf("bbbbbbbb", StringComparison.Ordinal));
        // Reordered and B turned off: the game's module and a mod from the in-game manager stay.
        var again = Larian.ModSettings(first, [a], new HashSet<string> { a.Uuid });
        Assert.Contains("GustavX", again);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(again, "aaaaaaaa"));
        Assert.Contains("bbbbbbbb", again);  // not Luma's any more this time: left as the player has it
    }

    [Fact]
    public void Lz4Copies()
    {
        // "abcabcabc": literals "abc", then a match of 6 at offset 3.
        var block = new byte[] { (3 << 4) | 2, (byte)'a', (byte)'b', (byte)'c', 3, 0 };
        Assert.Equal("abcabcabc", Encoding.ASCII.GetString(Larian.Lz4Block(block, 9)));
    }
}
