using System.Collections.Concurrent;
using System.Text.Json;
using Luma.Core.Games;

namespace Luma.Core.Sources;

/// <summary>
/// CurseForge for Studios API v1 with the player's API key (x-api-key). Respects each author's
/// choice: when a mod doesn't allow third-party distribution, Luma opens its page instead.
/// </summary>
public sealed class CurseForgeSource(HttpGate http, Func<string?> apiKey, string baseUrl = "https://api.curseforge.com") : IModSource
{
    private readonly ConcurrentDictionary<string, long> _gameIds = new();

    public string Id => "curseforge";
    public string Name => "CurseForge";
    public bool Ready => !string.IsNullOrWhiteSpace(apiKey());

    public bool Carries(GameProfile game) => game.Sites.Curseforge != null;

    private IEnumerable<KeyValuePair<string, string>> Headers()
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new SetupRequiredException(Name);
        }
        yield return new("x-api-key", key.Trim());
    }

    private async Task<long> GameId(GameProfile game, CancellationToken ct)
    {
        var slug = game.Sites.Curseforge ?? throw new InvalidOperationException($"{game.Name} isn't on CurseForge.");
        if (_gameIds.TryGetValue(slug, out var id))
        {
            return id;
        }
        for (var index = 0; index < 2000; index += 50)
        {
            using var doc = await http.GetJsonAsync($"{baseUrl}/v1/games?index={index}&pageSize=50", Headers(), ct: ct);
            var page = doc.RootElement.Arr("data").ToList();
            foreach (var g in page)
            {
                _gameIds[g.Str("slug")] = g.Long("id");
            }
            if (_gameIds.TryGetValue(slug, out id))
            {
                return id;
            }
            if (page.Count < 50)
            {
                break;
            }
        }
        throw new KeyNotFoundException($"CurseForge has no game \"{slug}\".");
    }

    public async Task<IReadOnlyList<ModCategory>> CategoriesAsync(GameProfile game, CancellationToken ct = default)
    {
        var id = await GameId(game, ct);
        using var doc = await http.GetJsonAsync($"{baseUrl}/v1/categories?gameId={id}", Headers(), ct: ct);
        return doc.RootElement.Arr("data").Where(c => !c.Bool("isClass")).Select(c => new ModCategory(c.Long("id").ToString(), c.Str("name")))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<ModSummary>> SearchAsync(GameProfile game, ModQuery q, CancellationToken ct = default)
    {
        var id = await GameId(game, ct);
        // sortField: 2 popularity, 3 last updated, 11 released date.
        var sort = q.Sort switch { ModSort.New => 11, ModSort.Updated => 3, _ => 2 };
        var url = $"{baseUrl}/v1/mods/search?gameId={id}&sortField={sort}&sortOrder=desc&index={q.Page * ModQuery.PageSize}&pageSize={ModQuery.PageSize}";
        if (q.Text.Length > 0)
        {
            url += "&searchFilter=" + Uri.EscapeDataString(q.Text);
        }
        if (q.Category != null)
        {
            url += "&categoryId=" + Uri.EscapeDataString(q.Category);
        }
        using var doc = await http.GetJsonAsync(url, Headers(), ct: ct);
        return doc.RootElement.Arr("data").Select(Summary).ToList();
    }

    private ModSummary Summary(JsonElement m) => new()
    {
        Site = Id,
        Id = m.Long("id").ToString(),
        Name = m.Str("name"),
        Author = string.Join(", ", m.Arr("authors").Select(a => a.Str("name")).Where(n => n.Length > 0)),
        Summary = m.Str("summary"),
        IconUrl = m.Obj("logo").StrOrNull("thumbnailUrl"),
        Downloads = m.Long("downloadCount"),
        Likes = m.Long("thumbsUpCount"),
        Updated = m.Date("dateModified"),
        Created = m.Date("dateReleased") ?? m.Date("dateCreated"),
        Categories = m.Arr("categories").Select(c => c.Str("name")).ToList(),
        PageUrl = m.Obj("links").StrOrNull("websiteUrl"),
    };

    public async Task<ModDetails> DetailsAsync(GameProfile game, string modId, CancellationToken ct = default)
    {
        var mod = Uri.EscapeDataString(modId);
        using var doc = await http.GetJsonAsync($"{baseUrl}/v1/mods/{mod}", Headers(), ct: ct);
        var m = doc.RootElement.Obj("data");
        var description = "";
        try
        {
            using var d = await http.GetJsonAsync($"{baseUrl}/v1/mods/{mod}/description", Headers(), ct: ct);
            description = d.RootElement.Str("data");
        }
        catch (HttpRequestException)
        {
        }
        using var files = await http.GetJsonAsync($"{baseUrl}/v1/mods/{mod}/files?pageSize=20", Headers(), ct: ct);
        var allowed = !m.TryGetProperty("allowModDistribution", out var a) || a.ValueKind != JsonValueKind.False;
        var list = files.RootElement.Arr("data").Select(f => new ModFile
        {
            Id = f.Long("id").ToString(),
            Name = f.Str("displayName"),
            Version = f.Str("displayName"),
            Size = f.Long("fileLength"),
            Date = f.Date("fileDate"),
            FileName = f.Str("fileName"),
            Kind = f.Long("releaseType") switch { 1 => "Release", 2 => "Beta", 3 => "Alpha", _ => "" },
            DownloadUrl = allowed ? f.StrOrNull("downloadUrl") : null,
            // relationType 3 = required dependency.
            Dependencies = f.Arr("dependencies").Where(x => x.Long("relationType") == 3)
                .Select(x => new ModDependency(Id, x.Long("modId").ToString(), $"mod {x.Long("modId")}")).ToList(),
        }).OrderByDescending(f => f.Date).ToList();
        return new ModDetails
        {
            Mod = Summary(m),
            Description = Text.Plain(description.Length > 0 ? description : m.Str("summary")),
            Screenshots = m.Arr("screenshots").Select(s => s.Str("url")).Where(u => u.Length > 0).ToList(),
            Files = list,
            Dependencies = list.FirstOrDefault()?.Dependencies ?? [],
        };
    }

    public async Task<DownloadTicket> DownloadAsync(GameProfile game, string modId, ModFile file, CancellationToken ct = default)
    {
        var url = file.DownloadUrl;
        if (url == null)
        {
            try
            {
                using var doc = await http.GetJsonAsync($"{baseUrl}/v1/mods/{Uri.EscapeDataString(modId)}/files/{Uri.EscapeDataString(file.Id)}/download-url", Headers(), cache: false, ct: ct);
                url = doc.RootElement.StrOrNull("data");
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
            }
        }
        if (string.IsNullOrEmpty(url))
        {
            throw new DownloadNotAllowedException("The author only allows downloads from CurseForge's own site or app.", null);
        }
        return new DownloadTicket(url, Text.SafeFileName(file.FileName.Length > 0 ? file.FileName : $"curseforge-{modId}-{file.Id}.zip"), file.Size);
    }

    public async Task ValidateAsync(CancellationToken ct = default)
    {
        using var _ = await http.GetJsonAsync($"{baseUrl}/v1/games?index=0&pageSize=1", Headers(), cache: false, ct: ct);
    }
}
