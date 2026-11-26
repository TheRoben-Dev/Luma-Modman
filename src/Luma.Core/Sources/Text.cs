using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Luma.Core.Sources;

/// <summary>Helpers shared by the site clients: flattening descriptions, reading JSON leniently.</summary>
public static partial class Text
{
    /// <summary>HTML, BBCode or Markdown description → readable plain text with paragraphs and bullets.</summary>
    public static string Plain(string? s, int max = 6000)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }
        var t = s.Replace("\r\n", "\n");
        t = BlockTags().Replace(t, "\n");
        t = ListItem().Replace(t, "\n•  ");
        t = BbList().Replace(t, "\n•  ");
        t = Tags().Replace(t, "");
        // Markdown images and links before BBCode, which would eat "[text]".
        t = MdImage().Replace(t, "");
        t = MdLink().Replace(t, "$1");
        t = BbTags().Replace(t, "");
        t = WebUtility.HtmlDecode(t);
        // Markdown: headings, emphasis, list markers.
        t = MdHeading().Replace(t, "");
        t = MdBullet().Replace(t, "•  ");
        t = t.Replace("**", "").Replace("__", "").Replace("`", "");
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var raw in t.Split('\n'))
        {
            var line = Spaces().Replace(raw, " ").Trim();
            if (line.Length == 0)
            {
                if (++blank == 1 && sb.Length > 0)
                {
                    sb.Append('\n');
                }
                continue;
            }
            blank = 0;
            if (line.StartsWith('•'))
            {
                line = "•  " + line[1..].TrimStart();
            }
            sb.Append(line).Append('\n');
        }
        var result = sb.ToString().Trim();
        return result.Length > max ? result[..max].TrimEnd() + "…" : result;
    }

    public static string Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static string? StrOrNull(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static long Long(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetInt64(out var l) ? l : (long)v.GetDouble(),
                JsonValueKind.String => long.TryParse(v.GetString(), out var p) ? p : 0,
                _ => 0,
            }
            : 0;

    public static bool Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    /// <summary>ISO date string or Unix seconds.</summary>
    public static DateTime? Date(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var secs) && secs > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
        }
        return v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;
    }

    /// <summary>A safe file name (no paths, no control characters).</summary>
    public static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars().Concat(['\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var s = new string(name.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        return s.Length == 0 ? "download" : s.Length > 150 ? s[..150] : s;
    }

    [GeneratedRegex(@"<\s*(br|/p|p|/div|div|/h\d|h\d|/ul|ul|/ol|ol|hr)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<\s*li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"\s*(\[list[^\]]*\]\s*)?\[\*\]")]
    private static partial Regex BbList();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\[/?[a-zA-Z]+(=[^\]]*)?\]")]
    private static partial Regex BbTags();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex MdImage();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MdLink();

    [GeneratedRegex(@"(?m)^[ \t]{0,3}#{1,6}[ \t]*")]
    private static partial Regex MdHeading();

    [GeneratedRegex(@"(?m)^[ \t]*[-*+][ \t]+")]
    private static partial Regex MdBullet();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();
}
