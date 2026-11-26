using System.Text.Json;
using Luma.Core.Games;

namespace Luma.Core.Sources;

/// <summary>
/// Thunderstore: public, no account. Search uses the community listing API the site itself uses
/// (/api/cyberstorm/listing), details and the readme come from the experimental package API.
/// Package ids are "Namespace-Name".
/// </summary>
public sealed class ThunderstoreSource(HttpGate http, string baseUrl = "https://thunderstore.io") : IModSource
{
    public string Id => "thunderstore";
    public string Name => "Thunderstore";
    public bool Ready => true;

    public bool Carries(GameProfile game) => game.Sites.Thunderstore != null;

    private string Community(GameProfile game) => Uri.EscapeDataString(game.Sites.Thunderstore ?? throw new InvalidOperationException($"{game.Name} isn't on Thunderstore."));

    public async Task<IReadOnlyList<ModCategory>> CategoriesAsync(GameProfile game, CancellationToken ct = default)
    {
        using var doc = await http.GetJsonAsync($"{baseUrl}/api/cyberstorm/community/{Community(game)}/filters/", ct: ct);
        return doc.RootElement.Arr("package_categories").Select(c => new ModCategory(c.Str("id"), c.Str("name"))).Where(c => c.Id.Length > 0).ToList();
    }

    public async Task<IReadOnlyList<ModSummary>> SearchAsync(GameProfile game, ModQuery q, CancellationToken ct = default)
    {
        var ordering = q.Sort switch { ModSort.New => "newest", ModSort.Updated => "last-updated", _ => "most-downloaded" };
        var url = $"{baseUrl}/api/cyberstorm/listing/{Community(game)}/?ordering={ordering}&page={q.Page + 1}&page_size={ModQuery.PageSize}";
        if (q.Text.Length > 0)
        {
            url += "&q=" + Uri.EscapeDataString(q.Text);
        }
        if (q.Category != null)
        {
            url += "&included_categories=" + Uri.EscapeDataString(q.Category);
        }
        using var doc = await http.GetJsonAsync(url, ct: ct);
        return doc.RootElement.Arr("results").Select(r => Summary(r, game)).ToList();
    }

    private ModSummary Summary(JsonElement r, GameProfile game)
    {
        var ns = r.Str("namespace");
        var name = r.Str("name");
        return new ModSummary
        {
            Site = Id,
            Id = $"{ns}-{name}",
            Name = name.Replace('_', ' '),
            Author = ns,
            Summary = r.Str("description"),
            IconUrl = r.StrOrNull("icon_url"),
            Downloads = r.Long("download_count"),
            Likes = r.Long("rating_count"),
            Updated = r.Date("last_updated"),
            Created = r.Date("datetime_created"),
            Categories = r.Arr("categories").Select(c => c.Str("name")).ToList(),
            Adult = r.Bool("is_nsfw"),
            PageUrl = $"{baseUrl}/c/{game.Sites.Thunderstore}/p/{ns}/{name}/",
        };
    }

    public static (string Namespace, string Name) Split(string id)
    {
        var dash = id.IndexOf('-');
        return dash > 0 ? (id[..dash], id[(dash + 1)..]) : throw new ArgumentException($"Not a Thunderstore package id: {id}");
    }

    /// <summary>"Owner-Name-1.2.3" → dependency on Owner-Name at 1.2.3.</summary>
    public static ModDependency ParseDependency(string s)
    {
        var parts = s.Split('-');
        return parts.Length >= 3
            ? new ModDependency("thunderstore", $"{parts[0]}-{string.Join('-', parts[1..^1])}", string.Join(' ', parts[1..^1]).Replace('_', ' '), parts[^1])
            : new ModDependency("thunderstore", s, s);
    }

    public async Task<ModDetails> DetailsAsync(GameProfile game, string modId, CancellationToken ct = default)
    {
        var (ns, name) = Split(modId);
        using var doc = await http.GetJsonAsync($"{baseUrl}/api/experimental/package/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/", ct: ct);
        var r = doc.RootElement;
        var latest = r.Obj("latest");
        var version = latest.Str("version_number");
        var deps = latest.Arr("dependencies").Select(d => ParseDependency(d.GetString() ?? "")).ToList();
        var listing = r.Arr("community_listings").FirstOrDefault(l => l.Str("community") == game.Sites.Thunderstore);
        var summary = new ModSummary
        {
            Site = Id,
            Id = modId,
            Name = name.Replace('_', ' '),
            Author = r.Str("owner"),
            Summary = latest.Str("description"),
            IconUrl = latest.StrOrNull("icon"),
            Downloads = latest.Long("downloads"),
            Updated = r.Date("date_updated"),
            Created = r.Date("date_created"),
            Categories = listing.Arr("categories").Select(c => c.GetString() ?? "").ToList(),
            Adult = listing.Bool("has_nsfw_content"),
            PageUrl = r.StrOrNull("package_url"),
        };
        var readme = "";
        try
        {
            using var rd = await http.GetJsonAsync($"{baseUrl}/api/experimental/package/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}/readme/", ct: ct);
            readme = rd.RootElement.Str("markdown");
        }
        catch (HttpRequestException)
        {
            // Some packages have no readme; the summary is enough.
        }
        return new ModDetails
        {
            Mod = summary,
            Description = Text.Plain(readme.Length > 0 ? readme : summary.Summary),
            Files =
            [
                new ModFile
                {
                    Id = version,
                    Name = $"{summary.Name} {version}",
                    Version = version,
                    Date = latest.Date("date_created"),
                    FileName = $"{ns}-{name}-{version}.zip",
                    DownloadUrl = latest.StrOrNull("download_url"),
                    Dependencies = deps,
                },
            ],
            Dependencies = deps,
        };
    }

    public Task<DownloadTicket> DownloadAsync(GameProfile game, string modId, ModFile file, CancellationToken ct = default)
    {
        var (ns, name) = Split(modId);
        var url = file.DownloadUrl ?? $"{baseUrl}/package/download/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(file.Version)}/";
        return Task.FromResult(new DownloadTicket(url, Text.SafeFileName($"{ns}-{name}-{file.Version}.zip"), file.Size));
    }
}
