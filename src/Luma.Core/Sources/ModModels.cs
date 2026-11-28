namespace Luma.Core.Sources;

/// <summary>How search results are ordered. Every site supports these three.</summary>
public enum ModSort
{
    Popular,
    Updated,
    New,
}

/// <summary>A search on one site, for one game.</summary>
public sealed record ModQuery
{
    public string Text { get; init; } = "";

    /// <summary>A category id from <see cref="IModSource.CategoriesAsync"/>, or null for all.</summary>
    public string? Category { get; init; }

    public ModSort Sort { get; init; } = ModSort.Popular;

    /// <summary>0-based page.</summary>
    public int Page { get; init; }

    public const int PageSize = 20;
}

public sealed record ModCategory(string Id, string Name);

/// <summary>One mod in a result list.</summary>
public sealed record ModSummary
{
    /// <summary>Site id ("thunderstore", "modio", "curseforge", "nexus").</summary>
    public required string Site { get; init; }

    /// <summary>The site's id for the mod (Thunderstore: "Owner-Name").</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
    public string Author { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? IconUrl { get; init; }
    public long Downloads { get; init; }

    /// <summary>Endorsements, likes or rating score, whatever the site counts.</summary>
    public long Likes { get; init; }

    public DateTime? Updated { get; init; }
    public DateTime? Created { get; init; }
    public IReadOnlyList<string> Categories { get; init; } = [];
    public bool Adult { get; init; }

    /// <summary>The mod's page on the site, for attribution and "open on the website".</summary>
    public string? PageUrl { get; init; }
}

/// <summary>A required mod, by the site's id.</summary>
public sealed record ModDependency(string Site, string Id, string Name, string? Version = null);

/// <summary>A downloadable file or version of a mod.</summary>
public sealed record ModFile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public long Size { get; init; }
    public DateTime? Date { get; init; }
    public string FileName { get; init; } = "";

    /// <summary>"Main", "Optional", "Update", "Old" (Nexus categories); empty when the site has none.</summary>
    public string Kind { get; init; } = "";

    /// <summary>Direct link when the site gives one up front (Thunderstore, mod.io, CurseForge).</summary>
    public string? DownloadUrl { get; init; }

    public IReadOnlyList<ModDependency> Dependencies { get; init; } = [];
}

public sealed record ModDetails
{
    public required ModSummary Mod { get; init; }

    /// <summary>The description as plain text (HTML, BBCode and Markdown are flattened).</summary>
    public string Description { get; init; } = "";

    public IReadOnlyList<string> Screenshots { get; init; } = [];

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ModFile> Files { get; init; } = [];

    public IReadOnlyList<ModDependency> Dependencies { get; init; } = [];
}

/// <summary>What to download: a URL the site handed out, and the file name to keep it under.</summary>
/// <param name="Referer">Sent with the download, for sites that only hand out files to visitors of their page.</param>
public sealed record DownloadTicket(string Url, string FileName, long Size = 0, string? Referer = null);

/// <summary>The site won't let Luma download this file (author opted out, Premium needed...): send the player to the site.</summary>
public sealed class DownloadNotAllowedException(string message, string? pageUrl) : InvalidOperationException(message)
{
    public string? PageUrl { get; } = pageUrl;
}

/// <summary>The site needs setup (API key, sign-in) before it can be used.</summary>
public sealed class SetupRequiredException(string site) : InvalidOperationException($"{site} needs to be set up first.")
{
    public string Site { get; } = site;
}
