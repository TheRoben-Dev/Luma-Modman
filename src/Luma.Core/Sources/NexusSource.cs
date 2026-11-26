using System.Text.Json;
using Luma.Core.Games;

namespace Luma.Core.Sources;

/// <summary>
/// Nexus Mods. Search, details and file lists use the public v2 GraphQL API (no key). Downloads use
/// the v1 API with the player's personal API key: Premium members get a link directly; everyone
/// else presses "Mod Manager Download" on the website, which hands Luma an nxm:// link carrying a
/// one-time key (see <see cref="NxmLink"/>). Luma never scrapes the site or skips its download page.
/// </summary>
public sealed class NexusSource(HttpGate http, Func<string?> apiKey, string appVersion, string graphql = "https://api.nexusmods.com/v2/graphql", string v1 = "https://api.nexusmods.com/v1") : IModSource
{
    public string Id => "nexus";
    public string Name => "Nexus Mods";

    /// <summary>Browsing works without a key; downloading needs one.</summary>
    public bool Ready => true;

    public bool HasKey => !string.IsNullOrWhiteSpace(apiKey());

    public bool Carries(GameProfile game) => game.Sites.Nexus != null;

    private static string Domain(GameProfile game) => game.Sites.Nexus ?? throw new InvalidOperationException($"{game.Name} isn't on Nexus Mods.");

    private IEnumerable<KeyValuePair<string, string>> KeyHeaders()
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new SetupRequiredException(Name);
        }
        yield return new("apikey", key.Trim());
        yield return new("Application-Name", "Luma");
        yield return new("Application-Version", appVersion);
    }

    private async Task<JsonDocument> Query(string query, object variables, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { query, variables });
        var text = await http.PostJsonAsync(graphql, body, [new("Application-Name", "Luma"), new("Application-Version", appVersion)], ct);
        var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            var msg = errors[0].Str("message");
            doc.Dispose();
            throw new InvalidDataException($"Nexus Mods couldn't answer: {msg}");
        }
        return doc;
    }

    private const string ModFields = "modId name summary author uploader { name } pictureUrl thumbnailUrl endorsements downloads updatedAt createdAt adultContent modCategory { name }";

    /// <summary>Nexus categories aren't in the public API without a key; the categories seen in results are offered instead.</summary>
    public Task<IReadOnlyList<ModCategory>> CategoriesAsync(GameProfile game, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModCategory>>(_seenCategories.TryGetValue(Domain(game), out var c) ? c.Order().Select(n => new ModCategory(n, n)).ToList() : []);

    private readonly Dictionary<string, SortedSet<string>> _seenCategories = new();

    public async Task<IReadOnlyList<ModSummary>> SearchAsync(GameProfile game, ModQuery q, CancellationToken ct = default)
    {
        var domain = Domain(game);
        var filter = new Dictionary<string, object> { ["gameDomainName"] = new[] { new { value = domain } } };
        if (q.Text.Length > 0)
        {
            filter["name"] = new[] { new { value = q.Text, op = "WILDCARD" } };
        }
        if (q.Category != null)
        {
            filter["categoryName"] = new[] { new { value = q.Category } };
        }
        var sortKey = q.Sort switch { ModSort.New => "createdAt", ModSort.Updated => "updatedAt", _ => "endorsements" };
        var sort = new[] { new Dictionary<string, object> { [sortKey] = new { direction = "DESC" } } };
        using var doc = await Query($"query($f: ModsFilter, $s: [ModsSort!], $o: Int, $c: Int) {{ mods(filter: $f, sort: $s, offset: $o, count: $c) {{ nodes {{ {ModFields} }} }} }}",
            new { f = filter, s = sort, o = q.Page * ModQuery.PageSize, c = ModQuery.PageSize }, ct);
        var list = doc.RootElement.Obj("data").Obj("mods").Arr("nodes").Select(n => Summary(n, domain)).ToList();
        var seen = _seenCategories.TryGetValue(domain, out var s) ? s : _seenCategories[domain] = new SortedSet<string>();
        foreach (var c in list.SelectMany(m => m.Categories))
        {
            seen.Add(c);
        }
        return list;
    }

    private ModSummary Summary(JsonElement n, string domain)
    {
        var id = n.Long("modId").ToString();
        var cat = n.Obj("modCategory").Str("name");
        return new ModSummary
        {
            Site = Id,
            Id = id,
            Name = n.Str("name"),
            Author = n.Str("author") is { Length: > 0 } a ? a : n.Obj("uploader").Str("name"),
            Summary = System.Net.WebUtility.HtmlDecode(n.Str("summary")),
            IconUrl = n.StrOrNull("thumbnailUrl") ?? n.StrOrNull("pictureUrl"),
            Downloads = n.Long("downloads"),
            Likes = n.Long("endorsements"),
            Updated = n.Date("updatedAt"),
            Created = n.Date("createdAt"),
            Categories = cat.Length > 0 ? [cat] : [],
            Adult = n.Bool("adultContent"),
            PageUrl = $"https://www.nexusmods.com/{domain}/mods/{id}",
        };
    }

    public async Task<ModDetails> DetailsAsync(GameProfile game, string modId, CancellationToken ct = default)
    {
        var domain = Domain(game);
        var id = int.Parse(modId);
        using var doc = await Query($"query($ids: [CompositeDomainWithIdInput!]!) {{ legacyModsByDomain(ids: $ids) {{ nodes {{ {ModFields} description game {{ id }} }} }} }}",
            new { ids = new[] { new { gameDomain = domain, modId = id } } }, ct);
        var node = doc.RootElement.Obj("data").Obj("legacyModsByDomain").Arr("nodes").FirstOrDefault();
        if (node.ValueKind != JsonValueKind.Object)
        {
            throw new KeyNotFoundException("Nexus Mods doesn't have that mod (it may be hidden or removed).");
        }
        var summary = Summary(node, domain);
        var gameId = node.Obj("game").Long("id");
        using var files = await Query("query($m: ID!, $g: ID!) { modFiles(modId: $m, gameId: $g) { fileId name version sizeInBytes category date uri description } }",
            new { m = id.ToString(), g = gameId.ToString() }, ct);
        var list = files.RootElement.Obj("data").Arr("modFiles")
            .Select(f => new ModFile
            {
                Id = f.Long("fileId").ToString(),
                Name = f.Str("name"),
                Version = f.Str("version"),
                Size = f.Long("sizeInBytes"),
                Date = f.Date("date"),
                FileName = f.Str("uri"),
                Kind = Kind(f.Str("category")),
            })
            .Where(f => f.Kind is not ("Old" or "Removed"))
            .OrderBy(f => f.Kind switch { "Main" => 0, "Update" => 1, "Optional" => 2, _ => 3 })
            .ThenByDescending(f => f.Date)
            .ToList();
        var shots = new List<string>();
        if (summary.IconUrl is { } thumb && node.StrOrNull("pictureUrl") is { } pic)
        {
            shots.Add(pic);
        }
        return new ModDetails { Mod = summary, Description = Text.Plain(node.Str("description")), Files = list, Screenshots = shots };
    }

    private static string Kind(string category) => category.ToUpperInvariant() switch
    {
        "MAIN" => "Main",
        "UPDATE" => "Update",
        "OPTIONAL" => "Optional",
        "MISCELLANEOUS" => "Misc",
        "ARCHIVED" or "OLD_VERSION" => "Old",
        "DELETED" or "REMOVED" => "Removed",
        _ => category,
    };

    /// <summary>Checks the key and says whether the account is Premium.</summary>
    public async Task<(string User, bool Premium)> ValidateAsync(CancellationToken ct = default)
    {
        using var doc = await http.GetJsonAsync($"{v1}/users/validate.json", KeyHeaders(), cache: false, ct: ct);
        return (doc.RootElement.Str("name"), doc.RootElement.Bool("is_premium"));
    }

    /// <summary>Premium: the link straight away. Others: sent to the website's download button (nxm link).</summary>
    public async Task<DownloadTicket> DownloadAsync(GameProfile game, string modId, ModFile file, CancellationToken ct = default) =>
        await DownloadAsync(Domain(game), modId, file.Id, null, null, file.FileName, ct);

    /// <summary>Resolves a download link, with the one-time key and expiry from an nxm link when there is one.</summary>
    public async Task<DownloadTicket> DownloadAsync(string domain, string modId, string fileId, string? key, long? expires, string? fileName, CancellationToken ct = default)
    {
        var url = $"{v1}/games/{Uri.EscapeDataString(domain)}/mods/{Uri.EscapeDataString(modId)}/files/{Uri.EscapeDataString(fileId)}/download_link.json";
        if (key != null && expires != null)
        {
            url += $"?key={Uri.EscapeDataString(key)}&expires={expires}";
        }
        JsonDocument doc;
        try
        {
            doc = await http.GetJsonAsync(url, KeyHeaders(), cache: false, ct: ct);
        }
        catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.Forbidden && key == null)
        {
            throw new DownloadNotAllowedException(
                "Nexus Mods only gives direct downloads to Premium members. Open the mod's Files tab and press \"Mod Manager Download\": the file comes straight to Luma.",
                $"https://www.nexusmods.com/{domain}/mods/{modId}?tab=files&file_id={fileId}");
        }
        using (doc)
        {
            var link = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().FirstOrDefault() : default;
            var uri = link.StrOrNull("URI") ?? throw new InvalidDataException("Nexus Mods didn't send a download link.");
            var name = fileName is { Length: > 0 } ? fileName : Path.GetFileName(new Uri(uri).LocalPath);
            return new DownloadTicket(uri, Text.SafeFileName(name));
        }
    }
}
