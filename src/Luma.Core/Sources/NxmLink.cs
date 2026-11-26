namespace Luma.Core.Sources;

/// <summary>
/// A Nexus Mods "Mod Manager Download" link: nxm://&lt;domain&gt;/mods/&lt;mod&gt;/files/&lt;file&gt;?key=…&amp;expires=…&amp;user_id=…
/// </summary>
public sealed record NxmLink(string Domain, string ModId, string FileId, string? Key, long? Expires, string? UserId)
{
    public static NxmLink? TryParse(string? s)
    {
        if (s == null || !Uri.TryCreate(s.Trim(), UriKind.Absolute, out var uri) || !uri.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 4 || parts[0] != "mods" || parts[2] != "files" || !parts[1].All(char.IsAsciiDigit) || !parts[3].All(char.IsAsciiDigit))
        {
            return null;
        }
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "", StringComparer.OrdinalIgnoreCase);
        return new NxmLink(uri.Host.ToLowerInvariant(), parts[1], parts[3],
            query.GetValueOrDefault("key"),
            long.TryParse(query.GetValueOrDefault("expires"), out var e) ? e : null,
            query.GetValueOrDefault("user_id"));
    }

    /// <summary>True once the one-time key has run out.</summary>
    public bool Expired(DateTime nowUtc) => Expires is { } e && DateTimeOffset.FromUnixTimeSeconds(e).UtcDateTime < nowUtc;
}
