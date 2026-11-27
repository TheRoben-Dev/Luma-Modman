using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Luma.Host;

/// <summary>
/// Luma's languages: data/translations/&lt;code&gt;.json, built into the app. Each maps the English
/// text as it appears in the code to the translation; text made with values in it is keyed with
/// {0}, {1}… for the values ("Installed {0}."), and <see cref="Translator"/> matches it either way.
/// tools/i18n/extract.py lists the keys.
/// </summary>
public static class Translations
{
    public sealed record Language(string Code, string Name);

    private sealed record File(string Language, string Name, Dictionary<string, string> Strings);

    private static readonly Assembly Here = typeof(Translations).Assembly;

    private static IEnumerable<string> Resources() =>
        Here.GetManifestResourceNames().Where(n => n.StartsWith("translations/", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal));

    /// <summary>English first, then every built-in language by its own name.</summary>
    public static IReadOnlyList<Language> Available { get; } = new[] { new Language("en", "English") }
        .Concat(Resources().Select(Read).OfType<File>().Select(f => new Language(f.Language, f.Name)).OrderBy(l => l.Name, StringComparer.CurrentCulture))
        .ToList();

    private static File? Read(string resource)
    {
        try
        {
            using var s = Here.GetManifestResourceStream(resource)!;
            var f = JsonSerializer.Deserialize<File>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return f is { Language.Length: > 0 } ? f with { Strings = f.Strings.Where(p => p.Value.Length > 0).ToDictionary(p => p.Key, p => p.Value) } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The language to use for the setting ("" follows the system's, when Luma has it).</summary>
    public static string Pick(string? setting)
    {
        if (setting is { Length: > 0 } s && Available.Any(l => l.Code == s))
        {
            return s;
        }
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (Environment.GetEnvironmentVariable("LANG") is { Length: >= 2 } lang)
        {
            system = lang[..2];
        }
        return Available.Any(l => l.Code == system) ? system : "en";
    }

    /// <summary>The translator for a language; null for English.</summary>
    public static Translator? For(string code) =>
        code == "en" ? null : Resources().Where(r => r == $"translations/{code}.json").Select(Read).OfType<File>().Select(f => new Translator(f.Strings)).FirstOrDefault();
}

/// <summary>Looks text up: exactly, else by the keys with {0}-style values in them (the longest fixed text wins).</summary>
public sealed class Translator
{
    private readonly Dictionary<string, string> _exact;
    private readonly Dictionary<string, string> _upper;
    private readonly List<(string Key, string Value, Regex Pattern, string Longest, int Fixed)> _patterns = new();
    private readonly Dictionary<string, string?> _cache = new();
    private static readonly Regex Hole = new(@"\{(\d+)\}", RegexOptions.Compiled);

    public Translator(Dictionary<string, string> strings)
    {
        _exact = strings;
        // Headings shown in capitals ("KEYBOARD AND MOUSE").
        _upper = new Dictionary<string, string>();
        foreach (var (k, v) in strings)
        {
            _upper.TryAdd(k.ToUpperInvariant(), v.ToUpperInvariant());
        }
        foreach (var (key, value) in strings)
        {
            if (!Hole.IsMatch(key))
            {
                continue;
            }
            var parts = Hole.Split(key);  // text, hole number, text, …
            var rx = "^";
            var literal = new List<string>();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    rx += Regex.Escape(parts[i]);
                    literal.Add(parts[i]);
                }
                else
                {
                    rx += $"(?<h{parts[i]}>.*?)";
                }
            }
            var longest = literal.OrderByDescending(l => l.Length).First();
            var fixedLength = literal.Sum(l => l.Length);
            if (fixedLength < 3)
            {
                continue;  // "{0} {1}": matches anything
            }
            _patterns.Add((key, value, new Regex(rx + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant), longest, fixedLength));
        }
        _patterns.Sort((a, b) => b.Fixed.CompareTo(a.Fixed));
    }

    public int Count => _exact.Count;

    /// <summary>The translation, or null when there is none.</summary>
    public string? Translate(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }
        if (_exact.TryGetValue(text, out var hit))
        {
            return hit;
        }
        lock (_cache)
        {
            if (_cache.TryGetValue(text, out var cached))
            {
                return cached;
            }
        }
        var result = Match(text) ?? Pieces(text);
        lock (_cache)
        {
            if (_cache.Count > 20000)
            {
                _cache.Clear();
            }
            _cache[text] = result;
        }
        return result;
    }

    /// <summary>Text put together from known pieces: several lines, a bullet in front, capitals.</summary>
    private string? Pieces(string text)
    {
        if (text.Contains('\n'))
        {
            var lines = text.Split('\n');
            var done = lines.Select(l => l.Length > 0 ? Match(l) ?? Bullet(l) : null).ToList();
            return done.Any(d => d != null) ? string.Join('\n', lines.Select((l, i) => done[i] ?? l)) : null;
        }
        return Bullet(text);
    }

    private string? Bullet(string text)
    {
        var core = text.TrimStart('•', '·', '-', ' ', '\t').TrimEnd();
        if (core.Length > 0 && core.Length < text.Length && Match(core) is { } t)
        {
            var at = text.IndexOf(core, StringComparison.Ordinal);
            return text[..at] + t + text[(at + core.Length)..];
        }
        return text.Any(char.IsLetter) && text == text.ToUpperInvariant() && _upper.TryGetValue(text, out var up) ? up : null;
    }

    private string? Match(string text)
    {
        if (_exact.TryGetValue(text, out var hit))
        {
            return hit;
        }
        string? result = null;
        foreach (var (_, value, pattern, longest, _) in _patterns)
        {
            if (!text.Contains(longest, StringComparison.Ordinal) || pattern.Match(text) is not { Success: true } m)
            {
                continue;
            }
            result = Hole.Replace(value, h => m.Groups["h" + h.Groups[1].Value] is { Success: true } g
                ? (_exact.TryGetValue(g.Value, out var inner) ? inner : g.Value) : h.Value);
            break;
        }
        return result;
    }
}
