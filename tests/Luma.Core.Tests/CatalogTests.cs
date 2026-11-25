using System.Text.Json;
using Luma.Core.Games;
using Xunit;

namespace Luma.Core.Tests;

public class CatalogTests
{
    private static readonly GameCatalog Catalog = GameCatalog.Load();

    [Fact]
    public void EveryBuiltInProfileIsValid() => Assert.Empty(Catalog.Problems);

    [Fact]
    public void AtLeastAHundredGamesAndSomeAreMarkedNotModdable()
    {
        Assert.True(Catalog.Games.Count >= 100, $"only {Catalog.Games.Count} profiles");
        Assert.Contains(Catalog.Games, g => !g.Moddable);
        Assert.All(Catalog.Games.Where(g => !g.Moddable), g => Assert.False(string.IsNullOrWhiteSpace(g.NotModdable)));
    }

    [Fact]
    public void SteamIdsAreUnique()
    {
        var dupes = Catalog.Games.SelectMany(g => g.Steam.Select(id => (id, g.Id))).GroupBy(x => x.id).Where(x => x.Count() > 1);
        Assert.Empty(dupes);
    }

    [Fact]
    public void LookupsWork()
    {
        Assert.Equal("skyrimspecialedition", Catalog.BySteamId(489830)?.Id);
        Assert.Equal("BepInEx 5", Catalog.Loaders[Catalog.ById("lethalcompany")!.Loader!].Name);
    }

    [Fact]
    public void UserProfilesAddAndOverrideAndBadOnesAreReported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "luma-cat-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "mygame.json"), JsonSerializer.Serialize(new GameProfile
            {
                Name = "My Game", Steam = [999999999], Sites = new SiteIds { Nexus = "mygame" },
            }, GameProfile.Json));
            File.WriteAllText(Path.Combine(dir, "valheim.json"), """{ "id": "valheim", "name": "Valheim (mine)", "steam": [892970], "sites": { "thunderstore": "valheim" } }""");
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");
            File.WriteAllText(Path.Combine(dir, "badloader.json"), """{ "name": "X", "steam": [1], "loader": "nope", "sites": { "nexus": "x" } }""");
            var c = GameCatalog.Load(dir);
            Assert.Equal("mygame", c.BySteamId(999999999)?.Id);
            Assert.Equal("Valheim (mine)", c.ById("valheim")?.Name);
            Assert.Equal(2, c.Problems.Count);
            Assert.Contains(c.Problems, p => p.Contains("unknown loader"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
