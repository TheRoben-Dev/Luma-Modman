using System.Text.Json;
using Luma.Core.Games;
using Luma.Core.Mods;

namespace Luma.Core.Sources;

/// <summary>
/// Which Nexus Mods collection (and revision) a link names: a collection page
/// (nexusmods.com/games/skyrimspecialedition/collections/abc123, next.nexusmods.com/…, an older
/// nexusmods.com/skyrimspecialedition/collections/abc123), an nxm://…/collections/abc123/revisions/4
/// link from the website's "Add to Vortex/mod manager" button, or the bare slug.
/// </summary>
public sealed record NexusCollectionRef(string? Domain, string Slug, int? Revision)
{
    public static NexusCollectionRef? TryParse(string? text)
    {
        var s = text?.Trim() ?? "";
        if (s.Length == 0)
        {
            return null;
        }
        if (!s.Contains('/') && s.Length is >= 4 and <= 12 && s.All(char.IsAsciiLetterOrDigit))
        {
            return new NexusCollectionRef(null, s.ToLowerInvariant(), null);
        }
        if (!s.Contains("://", StringComparison.Ordinal))
        {
            s = "https://" + s;
        }
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            return null;
        }
        var nxm = uri.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase);
        if (!nxm && !uri.Host.EndsWith("nexusmods.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        // nxm://domain/collections/slug/revisions/n puts the game in the host part.
        var parts = (nxm ? new[] { uri.Host } : []).Concat(uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)).ToList();
        if (parts.FirstOrDefault() == "games")
        {
            parts.RemoveAt(0);
        }
        var at = parts.FindIndex(p => p.Equals("collections", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= parts.Count || !parts[at + 1].All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }
        var domain = at > 0 ? parts[at - 1].ToLowerInvariant() : null;
        int? revision = at + 3 < parts.Count && parts[at + 2].Equals("revisions", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[at + 3], out var r) ? r : null;
        return new NexusCollectionRef(domain, parts[at + 1].ToLowerInvariant(), revision);
    }

    public string PageUrl => Domain != null
        ? $"https://www.nexusmods.com/games/{Domain}/collections/{Slug}" + (Revision is { } r ? $"/revisions/{r}" : "")
        : $"https://www.nexusmods.com/collections/{Slug}";
}

/// <summary>One mod file of a collection.</summary>
public sealed record NexusCollectionMod(string ModId, string FileId, string Name, string FileName, string Version, string Author, bool Optional);

/// <summary>A collection revision: its name and the exact mod files it lists.</summary>
public sealed record NexusCollection(string Name, string Slug, int Revision, string Author, string Domain, IReadOnlyList<NexusCollectionMod> Mods)
{
    public const string Query = """
        query($slug: String!, $rev: Int) {
          collectionRevision(slug: $slug, revision: $rev, viewAdultContent: true) {
            revisionNumber
            collection { name slug user { name } game { domainName } }
            modFiles { fileId optional file { fileId name version uri mod { modId name author } } }
          }
        }
        """;

    /// <summary>Reads the answer to <see cref="Query"/>.</summary>
    public static NexusCollection Parse(JsonElement root)
    {
        var rev = root.Obj("data").Obj("collectionRevision");
        if (rev.ValueKind != JsonValueKind.Object)
        {
            throw new KeyNotFoundException("Nexus Mods doesn't have that collection (it may be hidden, removed, or the link is wrong).");
        }
        var c = rev.Obj("collection");
        var mods = rev.Arr("modFiles").Select(f =>
        {
            var file = f.Obj("file");
            var mod = file.Obj("mod");
            var fileId = file.Long("fileId") is > 0 and var id ? id : f.Long("fileId");
            return new NexusCollectionMod(mod.Long("modId").ToString(), fileId.ToString(), mod.Str("name") is { Length: > 0 } n ? n : file.Str("name"),
                file.Str("uri"), file.Str("version"), mod.Str("author"), f.Bool("optional"));
        }).Where(m => m.ModId != "0").ToList();
        return new NexusCollection(c.Str("name"), c.Str("slug"), (int)rev.Long("revisionNumber"), c.Obj("user").Str("name"), c.Obj("game").Str("domainName"), mods);
    }

    /// <summary>
    /// The collection as a modpack's download list, so it imports like one (a new profile; each mod
    /// downloads, or for free accounts waits for its "Mod Manager Download" press).
    /// </summary>
    public PackManifest ToPack(GameProfile game, bool optional) => new()
    {
        Name = $"{Name} (collection)",
        CreatedBy = Author,
        Game = game.Id,
        GameName = game.Name,
        Mods = Mods.Where(m => optional || !m.Optional).GroupBy(m => m.ModId).Select(g => g.First()).Select(m => new PackMod
        {
            Id = "nexus:" + m.ModId,
            Site = "nexus",
            ModId = m.ModId,
            FileId = m.FileId,
            Name = m.Name,
            Author = m.Author,
            Version = m.Version,
            // The file's own download button (nmm=1 opens the "Mod Manager Download" prompt straight away).
            PageUrl = $"https://www.nexusmods.com/{Domain}/mods/{m.ModId}?tab=files&file_id={m.FileId}&nmm=1",
        }).ToList(),
    };
}
