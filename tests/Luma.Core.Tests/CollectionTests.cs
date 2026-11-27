using System.Text.Json;
using Luma.Core.Games;
using Luma.Core.Sources;
using Xunit;

namespace Luma.Core.Tests;

public sealed class CollectionTests
{
    [Theory]
    [InlineData("https://www.nexusmods.com/games/skyrimspecialedition/collections/qdurkx", "skyrimspecialedition", "qdurkx", null)]
    [InlineData("https://next.nexusmods.com/skyrimspecialedition/collections/qdurkx?tab=mods", "skyrimspecialedition", "qdurkx", null)]
    [InlineData("nexusmods.com/games/fallout4/collections/AbC123/revisions/7/mods", "fallout4", "abc123", 7)]
    [InlineData("nxm://skyrimspecialedition/collections/qdurkx/revisions/42", "skyrimspecialedition", "qdurkx", 42)]
    [InlineData("qdurkx", null, "qdurkx", null)]
    public void CollectionLinksAreRead(string link, string? domain, string slug, int? revision) =>
        Assert.Equal(new NexusCollectionRef(domain, slug, revision), NexusCollectionRef.TryParse(link));

    [Theory]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/266")]
    [InlineData("https://example.com/collections/qdurkx")]
    [InlineData("nxm://skyrimspecialedition/mods/266/files/1?key=a")]
    [InlineData("")]
    public void OtherLinksAreNotCollections(string link) => Assert.Null(NexusCollectionRef.TryParse(link));

    [Fact]
    public void ACollectionBecomesAPackDownloadList()
    {
        const string json = """
            { "data": { "collectionRevision": {
              "revisionNumber": 12,
              "collection": { "name": "Lite Overhaul", "slug": "qdurkx", "user": { "name": "Curator" }, "game": { "domainName": "skyrimspecialedition" } },
              "modFiles": [
                { "fileId": 111, "optional": false, "file": { "fileId": 111, "name": "SkyUI 5.2", "version": "5.2", "uri": "SkyUI_5_2_SE.7z", "mod": { "modId": 12604, "name": "SkyUI", "author": "SkyUI Team" } } },
                { "fileId": "222", "optional": true, "file": { "fileId": "222", "name": "Extra", "version": "1", "uri": "extra.zip", "mod": { "modId": "5", "name": "Extra Stuff", "author": "Someone" } } },
                { "fileId": 333, "optional": false, "file": { "fileId": 333, "name": "SkyUI patch", "version": "1", "uri": "p.zip", "mod": { "modId": 12604, "name": "SkyUI", "author": "SkyUI Team" } } }
              ] } } }
            """;
        using var doc = JsonDocument.Parse(json);
        var c = NexusCollection.Parse(doc.RootElement);
        Assert.Equal(("Lite Overhaul", 12, "Curator", "skyrimspecialedition", 3), (c.Name, c.Revision, c.Author, c.Domain, c.Mods.Count));
        var game = new GameProfile { Id = "skyrimspecialedition", Name = "Skyrim SE" };
        var required = c.ToPack(game, optional: false);
        Assert.Equal(["nexus:12604"], required.Mods.Select(m => m.Id));  // one entry per mod
        Assert.Equal("111", required.Mods[0].FileId);
        Assert.Contains("file_id=111", required.Mods[0].PageUrl);
        Assert.Equal(["nexus:12604", "nexus:5"], c.ToPack(game, optional: true).Mods.Select(m => m.Id));
        Assert.Equal("skyrimspecialedition", required.Game);
    }

    [Fact]
    public void AMissingCollectionSaysSo()
    {
        using var doc = JsonDocument.Parse("""{ "data": { "collectionRevision": null } }""");
        Assert.Throws<KeyNotFoundException>(() => NexusCollection.Parse(doc.RootElement));
    }
}
