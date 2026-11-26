using System.IO.Compression;
using System.Text.Json;
using Luma.Core.Detection;
using Luma.Core.Games;
using Luma.Core.Mods;
using Luma.Host;
using Xunit;

namespace Luma.Core.Tests;

public sealed class ModPackTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "luma-pack-" + Guid.NewGuid().ToString("N")[..8]);

    public ModPackTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private static readonly GameProfile Game = new() { Id = "g", Name = "Game", Steam = [1], ModsPath = "BepInEx/plugins", Sites = new SiteIds { Nexus = "g" } };

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

    /// <summary>A store with a Thunderstore mod, a hand-added one (off) and a Nexus one, in that order.</summary>
    private ModStore Store(string root)
    {
        var store = new ModStore(root, "steam:1");
        store.Install(Zip("ts.zip", "A.dll"), new InstalledMod { Id = "thunderstore:a-A", Site = "thunderstore", ModId = "a-A", FileId = "a-A-1.0.0", Name = "A", Version = "1.0.0" }, Game);
        store.Install(Zip("local.zip", "L.dll"), new InstalledMod { Id = "local:L", Site = "local", Name = "L" }, Game);
        store.Install(Zip("nx.zip", "N.dll"), new InstalledMod { Id = "nexus:7", Site = "nexus", ModId = "7", FileId = "70", Name = "N", Version = "2", PageUrl = "https://www.nexusmods.com/g/mods/7" }, Game);
        store.SetEnabled("local:L", false);
        return store;
    }

    [Fact]
    public void FullPackBundlesOnlyWhatMayBePassedOn()
    {
        var store = Store(Path.Combine(_dir, "store"));
        var file = Path.Combine(_dir, "out", "p.lumapack");
        var r = ModPack.Export(store, Game, "My pack", full: true, file, "Luma test");
        Assert.Equal(2, r.Bundled);
        Assert.Equal(1, r.Listed);
        Assert.Equal(["N"], r.NotBundled);

        var m = ModPack.Read(file);
        Assert.Equal(PackManifest.CurrentFormat, m.Format);
        Assert.Equal("g", m.Game);
        Assert.True(m.Full);
        Assert.Equal(["thunderstore:a-A", "local:L", "nexus:7"], m.Mods.Select(x => x.Id));
        Assert.False(m.Mods[1].Enabled);
        Assert.Null(m.Mods[2].File);
        Assert.All(m.Mods.Take(2), x => Assert.StartsWith("mods/", x.File));
        Assert.DoesNotContain('\\', m.Mods[0].File!);

        var list = ModPack.Export(store, Game, "List", full: false, Path.Combine(_dir, "l.lumapack"), "Luma test");
        Assert.Equal(0, list.Bundled);
        Assert.All(ModPack.Read(list.File).Mods, x => Assert.Null(x.File));
    }

    [Fact]
    public void ReadRefusesNewerFormatsAndPathsOutsideThePack()
    {
        string Pack(string name, object manifest, params string[] extra)
        {
            var path = Path.Combine(_dir, name);
            using var z = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var e in extra)
            {
                z.CreateEntry(e);
            }
            using var w = new StreamWriter(z.CreateEntry("pack.json").Open());
            w.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return path;
        }
        Assert.Throws<InvalidDataException>(() => ModPack.Read(Pack("new.lumapack", new { format = 99, game = "g", mods = Array.Empty<object>() })));
        Assert.Throws<InvalidDataException>(() => ModPack.Read(Pack("evil.lumapack",
            new { format = 1, game = "g", mods = new[] { new { id = "local:x", name = "x", file = "mods/../../x.zip" } } }, "mods/../../x.zip")));
        Assert.Throws<InvalidDataException>(() => ModPack.Read(Pack("nogame.lumapack", new { format = 1, mods = Array.Empty<object>() })));
        Assert.Throws<InvalidDataException>(() => ModPack.Read(Zip("plain.zip", "readme.txt")));
    }

    [Fact]
    public void ImportMakesANewProfileInThePacksOrder()
    {
        var pack = ModPack.Export(Store(Path.Combine(_dir, "author")), Game, "Friends", full: true, Path.Combine(_dir, "f.lumapack"), "Luma test").File;

        var home = Path.Combine(_dir, "home");
        var host = new AppHost(new AppPaths(v => v switch { "HOME" => home, _ => null }, windows: false));
        var game = new DetectedGame { Profile = Game, Install = new InstalledGame { Source = GameSource.Steam, SourceId = "1" }, GameDir = Path.Combine(_dir, "game") };
        var store = host.Mods.Store(game);
        // The player already has A, in another version: theirs is kept.
        host.Mods.Install(game, Zip("a09.zip", "A.dll"), new InstalledMod { Id = "thunderstore:a-A", Site = "thunderstore", ModId = "a-A", FileId = "a-A-0.9.0", Name = "A", Version = "0.9.0" });

        var r = PackImporter.Import(host, game, pack);
        Assert.Equal("Friends", r.Profile);
        Assert.Equal("Friends", store.State.Active);
        Assert.Equal(PackModOutcome.AlreadyInstalled, r.Mods[0].Outcome);
        Assert.Contains("0.9.0", r.Mods[0].Note);
        Assert.Equal(PackModOutcome.Installed, r.Mods[1].Outcome);
        Assert.Equal(PackModOutcome.Manual, r.Mods[2].Outcome); // Nexus isn't set up here
        Assert.Equal(["thunderstore:a-A", "local:L", "nexus:7"], store.State.Order.Select(e => e.Id));
        Assert.False(store.State.Order[1].Enabled);
        Assert.Equal("0.9.0", store.State.Find("thunderstore:a-A")!.Version);

        // The pack's mods joined only its profile.
        Assert.Equal(["thunderstore:a-A"], store.State.Profiles[GameMods.DefaultProfile].Select(e => e.Id));

        // A second import gets its own name; the mod that never arrived can be forgotten.
        Assert.Equal("Friends (2)", PackImporter.Import(host, game, pack).Profile);
        store.ForgetMissing();
        Assert.Equal(["thunderstore:a-A", "local:L"], store.State.Order.Select(e => e.Id));

        var other = Game with { Id = "other", Name = "Other" };
        Assert.Throws<InvalidOperationException>(() => PackImporter.Import(host, game with { Profile = other }, pack));
    }
}
