using System.Net;
using System.Text;
using Luma.Core;
using Luma.Core.Games;
using Luma.Core.Sources;
using Luma.Host;
using Xunit;

namespace Luma.Core.Tests;

public class SourcesTests
{
    /// <summary>Answers by URL prefix and records every request.</summary>
    private sealed class Fake(Func<HttpRequestMessage, (HttpStatusCode, string)?> route) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Seen { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            var (code, body) = route(request) ?? (HttpStatusCode.NotFound, "{\"message\":\"no route\"}");
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (HttpGate Gate, Fake Fake) Gate(Func<HttpRequestMessage, (HttpStatusCode, string)?> route)
    {
        var fake = new Fake(route);
        return (new HttpGate(new HttpClient(fake)) { MinGap = TimeSpan.Zero }, fake);
    }

    private static GameProfile Game(string? ts = null, string? nexus = null, string? modio = null, string? cf = null) =>
        new() { Id = "g", Name = "Game", Steam = [1], Sites = new SiteIds { Thunderstore = ts, Nexus = nexus, Modio = modio, Curseforge = cf } };

    // Trimmed from real responses (2026-11-26).
    private const string TsListing = """
        {"count":40003,"next":null,"previous":null,"results":[{"categories":[{"id":"682","name":"BepInEx","slug":"bepinex"}],
        "community_identifier":"lethal-company","description":"BepInEx pack for Mono Unity games.","download_count":34514741,
        "icon_url":"https://ccdn.thunderstore.io/live/repository/icons/BepInEx-BepInExPack-5.4.2305.png","is_deprecated":false,"is_nsfw":false,
        "is_pinned":true,"last_updated":"2026-03-17T05:05:21.490966Z","name":"BepInExPack","namespace":"BepInEx","rating_count":1054,"size":669238,
        "datetime_created":"2023-11-16T14:32:29.588783Z"}]}
        """;

    private const string TsPackage = """
        {"namespace":"notnotnotswipez","name":"MoreCompany","full_name":"notnotnotswipez-MoreCompany","owner":"notnotnotswipez",
        "package_url":"https://thunderstore.io/package/notnotnotswipez/MoreCompany/","date_created":"2023-11-16T04:46:27.899881Z",
        "date_updated":"2026-04-25T01:28:42.076106Z","latest":{"version_number":"1.14.0","description":"A stable lobby player count expansion mod.",
        "icon":"https://ccdn.thunderstore.io/x.png","dependencies":["BepInEx-BepInExPack-5.4.2100"],
        "download_url":"https://thunderstore.io/package/download/notnotnotswipez/MoreCompany/1.14.0/","downloads":1117467,
        "date_created":"2026-04-25T01:28:40.943994Z"},"community_listings":[{"has_nsfw_content":false,"categories":["Mods","BepInEx"],"community":"lethal-company"}]}
        """;

    [Fact]
    public async Task ThunderstoreSearchAndDetails()
    {
        var (gate, fake) = Gate(r => r.RequestUri!.AbsolutePath switch
        {
            "/api/cyberstorm/listing/lethal-company/" => (HttpStatusCode.OK, TsListing),
            "/api/experimental/package/notnotnotswipez/MoreCompany/" => (HttpStatusCode.OK, TsPackage),
            "/api/experimental/package/notnotnotswipez/MoreCompany/1.14.0/readme/" => (HttpStatusCode.OK, "{\"markdown\":\"# MoreCompany\\n\\n* More **players**\\n* [Cosmetics](https://x)\"}"),
            _ => null,
        });
        var ts = new ThunderstoreSource(gate);
        var game = Game(ts: "lethal-company");
        var results = await ts.SearchAsync(game, new ModQuery { Text = "bep inex", Sort = ModSort.Updated, Category = "682", Page = 1 });
        var mod = Assert.Single(results);
        Assert.Equal("BepInEx-BepInExPack", mod.Id);
        Assert.Equal(34514741, mod.Downloads);
        Assert.Equal(["BepInEx"], mod.Categories);
        var q = fake.Seen[0].RequestUri!.Query;
        Assert.Contains("ordering=last-updated", q);
        Assert.Contains("page=2", q);
        Assert.Contains("q=bep%20inex", q);
        Assert.Contains("included_categories=682", q);

        var d = await ts.DetailsAsync(game, "notnotnotswipez-MoreCompany");
        Assert.Equal("MoreCompany\n\n•  More players\n•  Cosmetics", d.Description);
        var dep = Assert.Single(d.Dependencies);
        Assert.Equal(("BepInEx-BepInExPack", "5.4.2100"), (dep.Id, dep.Version));
        var file = Assert.Single(d.Files);
        var ticket = await ts.DownloadAsync(game, d.Mod.Id, file);
        Assert.Equal("https://thunderstore.io/package/download/notnotnotswipez/MoreCompany/1.14.0/", ticket.Url);
        Assert.Equal("notnotnotswipez-MoreCompany-1.14.0.zip", ticket.FileName);
        Assert.Contains(fake.Seen, r => r.Headers.UserAgent.ToString().StartsWith("Luma/", StringComparison.Ordinal));
    }

    [Fact]
    public void ThunderstoreDependencyStrings()
    {
        var d = ThunderstoreSource.ParseDependency("Evaisa-LethalLib-0.16.1");
        Assert.Equal(("Evaisa-LethalLib", "0.16.1", "LethalLib"), (d.Id, d.Version, d.Name));
        var multi = ThunderstoreSource.ParseDependency("Owner-Some-Thing-1.0.0");
        Assert.Equal("Owner-Some-Thing", multi.Id);
    }

    private const string NexusSearch = """
        {"data":{"mods":{"totalCount":10664,"nodes":[{"modId":12604,"name":"SkyUI","summary":"Elegant, PC-friendly interface &amp; more.",
        "author":"SkyUI Team","uploader":{"name":"schlangster"},"pictureUrl":"https://staticdelivery.nexusmods.com/mods/1704/images/12604/a.png",
        "thumbnailUrl":"https://staticdelivery.nexusmods.com/mods/1704/images/thumbnails/12604/a.png","endorsements":509839,"downloads":26708998,
        "updatedAt":"2026-05-05T22:41:21Z","createdAt":"2017-10-01T11:55:43Z","adultContent":false,"modCategory":{"name":"User Interface"}}]}}}
        """;

    [Fact]
    public async Task NexusSearchUsesGraphqlAndDownloadNeedsPremiumOrNxmKey()
    {
        string? key = null;
        var (gate, fake) = Gate(r =>
        {
            if (r.RequestUri!.AbsolutePath == "/v2/graphql")
            {
                var body = r.Content!.ReadAsStringAsync().Result;
                Assert.Contains("\"gameDomainName\":[{\"value\":\"skyrimspecialedition\"}]", body);
                Assert.Contains("\"updatedAt\":{\"direction\":\"DESC\"}", body);
                return (HttpStatusCode.OK, NexusSearch);
            }
            if (r.RequestUri.AbsolutePath.EndsWith("download_link.json", StringComparison.Ordinal))
            {
                Assert.Equal("secret", r.Headers.GetValues("apikey").Single());
                return r.RequestUri.Query.Contains("key=abc")
                    ? (HttpStatusCode.OK, "[{\"name\":\"Nexus CDN\",\"short_name\":\"Nexus CDN\",\"URI\":\"https://cf-files.nexusmods.com/x/SkyUI_5_2_SE.7z?md5=1\"}]")
                    : (HttpStatusCode.Forbidden, "{\"message\":\"You don't have permission to get download links from the API without visting nexusmods.com - this is for premium users only.\"}");
            }
            return null;
        });
        var nexus = new NexusSource(gate, () => key, "0.3.0");
        var game = Game(nexus: "skyrimspecialedition");
        var mod = Assert.Single(await nexus.SearchAsync(game, new ModQuery { Sort = ModSort.Updated }));
        Assert.Equal(("12604", "SkyUI Team", 509839L), (mod.Id, mod.Author, mod.Likes));
        Assert.Equal("Elegant, PC-friendly interface & more.", mod.Summary);
        Assert.Equal("https://www.nexusmods.com/skyrimspecialedition/mods/12604", mod.PageUrl);
        Assert.Equal("User Interface", Assert.Single(await nexus.CategoriesAsync(game)).Name);

        var file = new ModFile { Id = "1", Name = "SkyUI", FileName = "SkyUI_5_2_SE.7z" };
        await Assert.ThrowsAsync<SetupRequiredException>(() => nexus.DownloadAsync(game, "12604", file));
        key = "secret";
        var notAllowed = await Assert.ThrowsAsync<DownloadNotAllowedException>(() => nexus.DownloadAsync(game, "12604", file));
        Assert.Contains("file_id=1", notAllowed.PageUrl);
        var ticket = await nexus.DownloadAsync("skyrimspecialedition", "12604", "1", "abc", 1999999999, null);
        Assert.Equal("SkyUI_5_2_SE.7z", ticket.FileName);
        Assert.Contains(fake.Seen, r => r.Headers.TryGetValues("Application-Name", out var v) && v.Single() == "Luma");
    }

    [Fact]
    public void NxmLinksParse()
    {
        var l = NxmLink.TryParse("nxm://SkyrimSpecialEdition/mods/12604/files/35407?key=a%2Bb&expires=1700000000&user_id=42");
        Assert.NotNull(l);
        Assert.Equal(("skyrimspecialedition", "12604", "35407", "a+b", 1700000000L, "42"), (l!.Domain, l.ModId, l.FileId, l.Key, l.Expires, l.UserId));
        Assert.True(l.Expired(DateTime.UtcNow));
        Assert.Null(NxmLink.TryParse("https://www.nexusmods.com/skyrimspecialedition/mods/12604"));
        Assert.Null(NxmLink.TryParse("nxm://skyrim/collections/abc/revisions/3"));
        Assert.Null(NxmLink.TryParse("nxm://skyrim/mods/../files/1"));
    }

    [Fact]
    public async Task CurseForgeRespectsAuthorsWhoOptOutOfThirdPartyDownloads()
    {
        var (gate, _) = Gate(r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/mods/7" => (HttpStatusCode.OK, """{"data":{"id":7,"name":"Thing","summary":"s","authors":[{"name":"Ann"}],"allowModDistribution":false,"links":{"websiteUrl":"https://www.curseforge.com/x/thing"},"screenshots":[{"url":"https://img/1.png"}]}}"""),
            "/v1/mods/7/description" => (HttpStatusCode.OK, """{"data":"<p>Hello<br>world</p>"}"""),
            "/v1/mods/7/files" => (HttpStatusCode.OK, """{"data":[{"id":70,"displayName":"Thing 1.0","fileName":"thing.zip","fileLength":10,"fileDate":"2026-01-01T00:00:00Z","downloadUrl":"https://edge/thing.zip","dependencies":[{"modId":8,"relationType":3},{"modId":9,"relationType":2}]}]}"""),
            "/v1/mods/7/files/70/download-url" => (HttpStatusCode.Forbidden, "{}"),
            _ => null,
        });
        var cf = new CurseForgeSource(gate, () => "k");
        var d = await cf.DetailsAsync(Game(cf: "x"), "7");
        Assert.Equal("Hello\nworld", d.Description);
        Assert.Equal(["https://img/1.png"], d.Screenshots);
        var f = Assert.Single(d.Files);
        Assert.Null(f.DownloadUrl);
        Assert.Equal("8", Assert.Single(f.Dependencies).Id);
        await Assert.ThrowsAsync<DownloadNotAllowedException>(() => cf.DownloadAsync(Game(cf: "x"), "7", f));
        Assert.False(new CurseForgeSource(gate, () => null).Ready);
    }

    [Fact]
    public async Task ModioResolvesTheGameByNameOnce()
    {
        var (gate, fake) = Gate(r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/games" => (HttpStatusCode.OK, """{"data":[{"id":5,"name":"Other"},{"id":11,"name":"Deep Rock Galactic","name_id":"drg","tag_options":[{"name":"Type","tags":["Cosmetic","Gameplay"]}]}]}"""),
            "/v1/games/11/mods" => (HttpStatusCode.OK, """{"data":[{"id":99,"name":"Better Lights","summary":"s","submitted_by":{"username":"u"},"logo":{"thumb_320x180":"https://l"},"stats":{"downloads_total":5},"profile_url":"https://mod.io/g/drg/m/x","modfile":{"id":1,"filename":"x.zip","download":{"binary_url":"https://dl/x.zip"}}}]}"""),
            _ => null,
        });
        var modio = new ModioSource(gate, () => "key");
        var game = Game(modio: "Deep Rock Galactic");
        Assert.Equal(["Cosmetic", "Gameplay"], (await modio.CategoriesAsync(game)).Select(c => c.Name));
        var mod = Assert.Single(await modio.SearchAsync(game, new ModQuery { Text = "light", Category = "Cosmetic" }));
        Assert.Equal(("99", 5L), (mod.Id, mod.Downloads));
        Assert.Single(fake.Seen, r => r.RequestUri!.AbsolutePath == "/v1/games");
        Assert.Contains("_sort=-downloads_total", fake.Seen[^1].RequestUri!.Query);
        Assert.Contains("tags=Cosmetic", fake.Seen[^1].RequestUri!.Query);
    }

    [Fact]
    public void ErrorsAreSentencesWithTheSitesMessage()
    {
        Assert.Equal("api.nexusmods.com didn't accept the key or sign-in: Please provide a valid API Key",
            HttpGate.Explain("api.nexusmods.com", HttpStatusCode.Unauthorized, "{\"message\":\"Please provide a valid API Key\"}"));
        Assert.Equal("api.mod.io says Luma asked too often; try again in a little while: Too many",
            HttpGate.Explain("api.mod.io", HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":429,\"message\":\"Too many\"}}"));
        Assert.Equal("x.io is having trouble right now.", HttpGate.Explain("x.io", HttpStatusCode.BadGateway, "<html>"));
    }

    [Fact]
    public async Task GateCachesAndSpacesRequests()
    {
        var (gate, fake) = Gate(_ => (HttpStatusCode.OK, "{}"));
        var slow = new HttpGate(new HttpClient(fake)) { MinGap = TimeSpan.FromMilliseconds(120) };
        await slow.GetStringAsync("https://a.test/1");
        await slow.GetStringAsync("https://a.test/1");
        Assert.Single(fake.Seen);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await slow.GetStringAsync("https://a.test/2");
        Assert.True(sw.ElapsedMilliseconds >= 80, $"second request after {sw.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("<p>One</p><ul><li>A</li><li>B</li></ul>", "One\n\n•  A\n•  B")]
    [InlineData("[b]Bold[/b] [url=https://x]link[/url]\n[list][*]x[/list]", "Bold link\n•  x")]
    [InlineData("Tom &amp; Jerry&nbsp;&lt;3", "Tom & Jerry <3")]
    public void DescriptionsFlattenToPlainText(string input, string expected) => Assert.Equal(expected, Text.Plain(input));

    [Fact]
    public async Task DownloadQueueFetchesIntoTheSiteFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "luma-dl-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var paths = new AppPaths(v => v switch { "HOME" => root, "XDG_DATA_HOME" => Path.Combine(root, "data"), _ => null });
            var (_, fake) = Gate(_ => (HttpStatusCode.OK, "zipbytes"));
            var q = new DownloadQueue(paths, new HttpClient(fake));
            var done = new TaskCompletionSource<DownloadItem>();
            q.Changed += i =>
            {
                if (i.State is DownloadState.Done or DownloadState.Failed)
                {
                    done.TrySetResult(i);
                }
            };
            q.Add(new DownloadItem { Site = "thunderstore", ModName = "X" }, _ => Task.FromResult(new DownloadTicket("https://dl.test/x.zip", "../x.zip")));
            var item = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DownloadState.Done, item.State);
            Assert.Equal(Path.Combine(paths.DownloadsDir, "thunderstore", "_x.zip"), item.Path);
            Assert.Equal("zipbytes", File.ReadAllText(item.Path!));
            Assert.True(File.Exists(Path.Combine(paths.Data, "downloads.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void NxmInboxRoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "luma-nxm-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var paths = new AppPaths(v => v switch { "HOME" => root, _ => null });
            Assert.False(NxmHandler.Drop(paths, "https://example.com"));
            Assert.True(NxmHandler.Drop(paths, "nxm://deadasdisco/mods/188/files/900?key=k&expires=1"));
            var l = Assert.Single(NxmHandler.Take(paths));
            Assert.Equal(("deadasdisco", "188"), (l.Domain, l.ModId));
            Assert.Empty(NxmHandler.Take(paths));
            Assert.Contains("Exec=\"/opt/luma/luma\" nxm %u", NxmHandler.DesktopEntry("/opt/luma/luma"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
