using System.Reflection;
using System.Text.RegularExpressions;

namespace Luma.Host.Updates;

/// <summary>One version's player-facing changes.</summary>
public sealed record ChangelogEntry(Version Version, IReadOnlyList<string> Items)
{
    public string Display => AppUpdater.DisplayVersion(Version);
}

/// <summary>
/// CHANGELOG.md (repo root, embedded in this assembly): "## x.y.z" headings with "- " items.
/// The same section opens each GitHub release's notes, so the update prompt can show it too.
/// </summary>
public static partial class Changelog
{
    /// <summary>Marker between the changelog section and the install boilerplate in release notes.</summary>
    public const string NotesSeparator = "\n---\n";

    private static IReadOnlyList<ChangelogEntry>? _bundled;

    /// <summary>The changelog shipped with this build, newest first.</summary>
    public static IReadOnlyList<ChangelogEntry> Bundled => _bundled ??= LoadBundled();

    private static IReadOnlyList<ChangelogEntry> LoadBundled()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md");
        if (s == null)
        {
            return [];
        }
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }

    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var list = new List<ChangelogEntry>();
        Version? current = null;
        var items = new List<string>();
        void Flush()
        {
            if (current != null)
            {
                list.Add(new ChangelogEntry(current, items.ToList()));
            }
            items.Clear();
        }
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            var h = Heading().Match(line);
            if (h.Success)
            {
                Flush();
                current = AppUpdater.ParseVersion(h.Groups[1].Value);
                continue;
            }
            if (current == null)
            {
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                items.Add(Plain(line[2..].Trim()));
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && items.Count > 0 && line.Trim().Length > 0)
            {
                items[^1] += " " + Plain(line.Trim());
            }
        }
        Flush();
        return list.OrderByDescending(e => e.Version).ToList();
    }

    /// <summary>Entries newer than <paramref name="after"/> up to and including <paramref name="upTo"/>.</summary>
    public static IReadOnlyList<ChangelogEntry> Between(IReadOnlyList<ChangelogEntry> all, Version after, Version upTo) =>
        all.Where(e => Norm(e.Version) > Norm(after) && Norm(e.Version) <= Norm(upTo)).ToList();

    /// <summary>The player-facing part of GitHub release notes (before the install boilerplate).</summary>
    public static IReadOnlyList<string> ItemsFromReleaseNotes(string notes)
    {
        var text = notes.Replace("\r", "");
        var cut = text.IndexOf(NotesSeparator, StringComparison.Ordinal);
        if (cut < 0)
        {
            return [];
        }
        return Parse("## 0.0.0\n" + text[..cut]).FirstOrDefault()?.Items ?? [];
    }

    private static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>Drops markdown emphasis and code ticks for plain UI labels.</summary>
    private static string Plain(string s) => Emphasis().Replace(s, "$2").Replace("`", "");

    [GeneratedRegex(@"^##\s+v?(\d+\.\d+(?:\.\d+)?)\b")]
    private static partial Regex Heading();

    [GeneratedRegex(@"(\*\*|__)(\S.*?\S|\S)\1")]
    private static partial Regex Emphasis();
}
