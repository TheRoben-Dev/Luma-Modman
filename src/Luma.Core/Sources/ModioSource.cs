using System.Collections.Concurrent;
using System.Text.Json;
using Luma.Core.Games;

namespace Luma.Core.Sources;

/// <summary>
/// mod.io REST API v1 with the player's own read-only API key (made free at mod.io/me/access).
/// The profile names the game as mod.io shows it; its numeric id is looked up once.
/// </summary>
public sealed class ModioSource(HttpGate http, Func<string?> apiKey, string baseUrl = "https://api.mod.io/v1") : IModSource
{
    private readonly ConcurrentDictionary<string, (long Id, List<ModCategory> Tags)> _games = new();

    public string Id => "modio";
    public string Name => "mod.io";
    public bool Ready => !string.IsNullOrWhiteSpace(apiKey());

    public bool Carries(GameProfile game) => game.Sites.Modio != null;

    private string Key => apiKey() is { Length: > 0 } k ? Uri.EscapeDataString(k.Trim()) : throw new SetupRequiredException(Name);

    private async Task<(long Id, List<ModCategory> Tags)> Game(GameProfile game, CancellationToken ct)
    {
        var name = game.Sites.Modio ?? throw new InvalidOperationException($"{game.Name} isn't on mod.io.");
        if (_games.TryGetValue(name, out var hit))
        {
            return hit;
        }
        using var doc = await http.GetJsonAsync($"{baseUrl}/games?api_key={Key}&_q={Uri.EscapeDataString(name)}&_limit=10", ct: ct);
        var games = doc.RootElement.Arr("data").ToList();
        var g = games.FirstOrDefault(x => x.Str("name").Equals(name, StringComparison.OrdinalIgnoreCase) || x.Str("name_id").Equals(name, StringComparison.OrdinalIgnoreCase));
        if (g.ValueKind != JsonValueKind.Object)
        {
            g = games.FirstOrDefault();
        }
        if (g.ValueKind != JsonValueKind.Object)
        {
            throw new KeyNotFoundException($"mod.io has no game called {name}.");
        }
        var tags = g.Arr("tag_options").SelectMany(o => o.Arr("tags").Select(t => t.GetString() ?? "")).Where(t => t.Length > 0).Distinct()
            .Select(t => new ModCategory(t, t)).ToList();
        return _games[name] = (g.Long("id"), tags);
    }

    public async Task<IReadOnlyList<ModCategory>> CategoriesAsync(GameProfile game, CancellationToken ct = default) => (await Game(game, ct)).Tags;

    public async Task<IReadOnlyList<ModSummary>> SearchAsync(GameProfile game, ModQuery q, CancellationToken ct = default)
    {
        var (id, _) = await Game(game, ct);
        var sort = q.Sort switch { ModSort.New => "-date_added", ModSort.Updated => "-date_updated", _ => "-downloads_total" };
        var url = $"{baseUrl}/games/{id}/mods?api_key={Key}&_sort={sort}&_limit={ModQuery.PageSize}&_offset={q.Page * ModQuery.PageSize}";
        if (q.Text.Length > 0)
        {
            url += "&_q=" + Uri.EscapeDataString(q.Text);
        }
        if (q.Category != null)
        {
            url += "&tags=" + Uri.EscapeDataString(q.Category);
        }
        using var doc = await http.GetJsonAsync(url, ct: ct);
        return doc.RootElement.Arr("data").Select(Summary).ToList();
    }

    private ModSummary Summary(JsonElement m) => new()
    {
        Site = Id,
        Id = m.Long("id").ToString(),
        Name = m.Str("name"),
        Author = m.Obj("submitted_by").Str("username"),
        Summary = m.Str("summary"),
        IconUrl = m.Obj("logo").StrOrNull("thumb_320x180"),
        Downloads = m.Obj("stats").Long("downloads_total"),
        Likes = m.Obj("stats").Long("ratings_positive"),
        Updated = m.Date("date_updated"),
        Created = m.Date("date_added"),
        Categories = m.Arr("tags").Select(t => t.Str("name")).ToList(),
        Adult = m.Long("maturity_option") != 0,
        PageUrl = m.StrOrNull("profile_url"),
    };

    private static ModFile File(JsonElement f) => new()
    {
        Id = f.Long("id").ToString(),
        Name = f.Str("filename"),
        Version = f.Str("version"),
        Size = f.Long("filesize"),
        Date = f.Date("date_added"),
        FileName = f.Str("filename"),
        DownloadUrl = f.Obj("download").StrOrNull("binary_url"),
    };

    public async Task<ModDetails> DetailsAsync(GameProfile game, string modId, CancellationToken ct = default)
    {
        var (id, _) = await Game(game, ct);
        var mod = Uri.EscapeDataString(modId);
        using var doc = await http.GetJsonAsync($"{baseUrl}/games/{id}/mods/{mod}?api_key={Key}", ct: ct);
        var m = doc.RootElement;
        using var files = await http.GetJsonAsync($"{baseUrl}/games/{id}/mods/{mod}/files?api_key={Key}&_sort=-date_added&_limit=20", ct: ct);
        var deps = new List<ModDependency>();
        try
        {
            using var d = await http.GetJsonAsync($"{baseUrl}/games/{id}/mods/{mod}/dependencies?api_key={Key}", ct: ct);
            deps = d.RootElement.Arr("data").Select(x => new ModDependency(Id, x.Long("mod_id").ToString(), x.Str("name") is { Length: > 0 } n ? n : $"mod {x.Long("mod_id")}")).ToList();
        }
        catch (HttpRequestException)
        {
            // Older games don't expose dependencies.
        }
        var list = files.RootElement.Arr("data").Select(File).ToList();
        if (list.Count == 0 && m.Obj("modfile").ValueKind == JsonValueKind.Object)
        {
            list.Add(File(m.Obj("modfile")));
        }
        return new ModDetails
        {
            Mod = Summary(m),
            Description = Text.Plain(m.Str("description") is { Length: > 0 } desc ? desc : m.Str("summary")),
            Screenshots = m.Obj("media").Arr("images").Select(i => i.StrOrNull("thumb_1280x720") ?? i.Str("original")).Where(u => u.Length > 0).ToList(),
            Files = list,
            Dependencies = deps,
        };
    }

    public Task<DownloadTicket> DownloadAsync(GameProfile game, string modId, ModFile file, CancellationToken ct = default) =>
        file.DownloadUrl is { } url
            ? Task.FromResult(new DownloadTicket(url, Text.SafeFileName(file.FileName.Length > 0 ? file.FileName : $"modio-{modId}-{file.Id}.zip"), file.Size))
            : throw new DownloadNotAllowedException("mod.io didn't offer a download for this file.", null);

    /// <summary>Checks the key with a cheap request.</summary>
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        using var _ = await http.GetJsonAsync($"{baseUrl}/games?api_key={Key}&_limit=1", cache: false, ct: ct);
    }
}
