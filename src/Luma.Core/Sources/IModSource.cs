using Luma.Core.Games;

namespace Luma.Core.Sources;

/// <summary>One mod site. Implementations are stateless apart from the credential and the HTTP gate.</summary>
public interface IModSource
{
    /// <summary>"thunderstore", "modio", "curseforge", "nexus".</summary>
    string Id { get; }

    /// <summary>Shown to players ("Nexus Mods").</summary>
    string Name { get; }

    /// <summary>True when the site can be used now (no setup needed, or the key is set).</summary>
    bool Ready { get; }

    /// <summary>True when the site carries <paramref name="game"/> (the profile names its id there).</summary>
    bool Carries(GameProfile game);

    Task<IReadOnlyList<ModCategory>> CategoriesAsync(GameProfile game, CancellationToken ct = default);

    Task<IReadOnlyList<ModSummary>> SearchAsync(GameProfile game, ModQuery query, CancellationToken ct = default);

    Task<ModDetails> DetailsAsync(GameProfile game, string modId, CancellationToken ct = default);

    /// <summary>A download URL for one file. Throws <see cref="DownloadNotAllowedException"/> when the site doesn't allow it here.</summary>
    Task<DownloadTicket> DownloadAsync(GameProfile game, string modId, ModFile file, CancellationToken ct = default);
}
