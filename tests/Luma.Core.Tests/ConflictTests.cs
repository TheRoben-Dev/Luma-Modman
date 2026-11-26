using System.IO.Compression;
using Luma.Core.Games;
using Luma.Core.Mods;
using Xunit;

namespace Luma.Core.Tests;

public sealed class ConflictTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "luma-conflict-" + Guid.NewGuid().ToString("N")[..8]);

    public ConflictTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Zip(string name, params string[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            using var w = new StreamWriter(z.CreateEntry(e).Open());
            w.Write(name + ":" + e);
        }
        return path;
    }

    [Fact]
    public void PickedWinnerBeatsTheLoadOrderPerFileAndProfile()
    {
        var store = new ModStore(Path.Combine(_dir, "store"), "steam:1");
        var p = new GameProfile { Id = "g", Name = "G", Steam = [1], ModsPath = "Data", Sites = new SiteIds { Nexus = "g" } };
        store.Install(Zip("a.zip", "meshes/x.nif", "textures/t.dds"), new InstalledMod { Id = "nexus:1", Name = "A" }, p);
        store.Install(Zip("b.zip", "Meshes/X.nif", "textures/t.dds", "b.esp"), new InstalledMod { Id = "nexus:2", Name = "B" }, p);
        var game = Path.Combine(_dir, "game");
        var data = Path.Combine(game, "Data");
        Directory.CreateDirectory(data);

        var plan = Deployer.Plan(store, game, data);
        Assert.Equal(2, plan.Conflicts.Count);
        Assert.All(plan.Conflicts, c => Assert.Equal("nexus:2", c.Winner));
        Assert.All(plan.Conflicts, c => Assert.False(c.Picked));
        var nif = plan.Conflicts.Single(c => c.Key.EndsWith(".nif", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["nexus:1", "nexus:2"], nif.Providers!);

        store.SetWinner([nif.Key], "nexus:1");
        plan = Deployer.Plan(store, game, data);
        var picked = plan.Conflicts.Single(c => c.Key == nif.Key);
        Assert.True(picked.Picked);
        Assert.Equal("nexus:1", picked.Winner);
        Assert.Equal(["nexus:2"], picked.Losers);
        Assert.Equal("a.zip:meshes/x.nif", File.ReadAllText(plan.Files[picked.Target].Source));
        Assert.Equal("nexus:2", plan.Conflicts.Single(c => c.Key != nif.Key).Winner);

        // Another profile follows its own load order; a winner that's turned off stops counting.
        store.SetProfile("Other", [new ProfileEntry("nexus:1"), new ProfileEntry("nexus:2")]);
        Assert.All(Deployer.Plan(store, game, data).Conflicts, c => Assert.Equal("nexus:2", c.Winner));
        store.UseProfile(GameMods.DefaultProfile);
        store.SetEnabled("nexus:1", false);
        Assert.Empty(Deployer.Plan(store, game, data).Conflicts);

        store.SetEnabled("nexus:1", true);
        store.SetWinner([nif.Key], null);
        Assert.All(Deployer.Plan(store, game, data).Conflicts, c => Assert.Equal("nexus:2", c.Winner));
    }
}
