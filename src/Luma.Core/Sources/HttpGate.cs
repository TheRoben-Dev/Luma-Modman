using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Luma.Core.Sources;

/// <summary>
/// Every request to a mod site goes through here: one identifying User-Agent, a minimum gap between
/// requests per host (the sites' rate limits), a short in-memory cache for repeated reads, and
/// clear errors (with the site's own message) instead of raw HTTP failures.
/// </summary>
public sealed class HttpGate
{
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, DateTime> _last = new();
    private readonly ConcurrentDictionary<string, (DateTime At, string Body)> _cache = new();
    private readonly Func<DateTime> _now;

    /// <summary>Minimum time between two requests to the same host.</summary>
    public TimeSpan MinGap { get; init; } = TimeSpan.FromMilliseconds(350);

    /// <summary>How long a GET stays cached.</summary>
    public TimeSpan CacheFor { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Last rate-limit headers seen per host ("x-rl-hourly-remaining" etc.), for the log.</summary>
    public ConcurrentDictionary<string, string> Limits { get; } = new();

    public static string UserAgent { get; set; } = "Luma/0.0 (+https://github.com/TheRoben-Dev/Luma-Modman)";

    public HttpGate(HttpClient? http = null, Func<DateTime>? now = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _now = now ?? (() => DateTime.UtcNow);
    }

    public HttpClient Client => _http;

    public async Task<string> GetStringAsync(string url, IEnumerable<KeyValuePair<string, string>>? headers = null, bool cache = true, CancellationToken ct = default)
    {
        var key = url + "|" + string.Join(",", headers?.Select(h => h.Key) ?? []);
        if (cache && _cache.TryGetValue(key, out var hit) && _now() - hit.At < CacheFor)
        {
            return hit.Body;
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var body = await SendAsync(req, headers, ct);
        if (cache)
        {
            _cache[key] = (_now(), body);
        }
        return body;
    }

    public async Task<string> PostJsonAsync(string url, string json, IEnumerable<KeyValuePair<string, string>>? headers = null, CancellationToken ct = default)
    {
        var key = "POST " + url + "|" + json;
        if (_cache.TryGetValue(key, out var hit) && _now() - hit.At < CacheFor)
        {
            return hit.Body;
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        var body = await SendAsync(req, headers, ct);
        _cache[key] = (_now(), body);
        return body;
    }

    public async Task<JsonDocument> GetJsonAsync(string url, IEnumerable<KeyValuePair<string, string>>? headers = null, bool cache = true, CancellationToken ct = default)
    {
        var body = await GetStringAsync(url, headers, cache, ct);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{new Uri(url).Host} sent something Luma couldn't read: {e.Message}");
        }
    }

    private async Task<string> SendAsync(HttpRequestMessage req, IEnumerable<KeyValuePair<string, string>>? headers, CancellationToken ct)
    {
        var host = req.RequestUri!.Host;
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (k, v) in headers ?? [])
        {
            req.Headers.TryAddWithoutValidation(k, v);
        }
        var gate = _locks.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_last.TryGetValue(host, out var last))
            {
                var wait = last + MinGap - _now();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }
            }
            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            finally
            {
                _last[host] = _now();
            }
            using (resp)
            {
                foreach (var h in resp.Headers.Where(h => h.Key.StartsWith("x-rl-", StringComparison.OrdinalIgnoreCase) || h.Key.StartsWith("x-ratelimit", StringComparison.OrdinalIgnoreCase)))
                {
                    Limits[host + " " + h.Key.ToLowerInvariant()] = string.Join(",", h.Value);
                }
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (resp.IsSuccessStatusCode)
                {
                    return body;
                }
                throw new HttpRequestException(Explain(host, resp.StatusCode, body), null, resp.StatusCode);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A sentence for the player, with the site's own message when it sent one.</summary>
    internal static string Explain(string host, HttpStatusCode code, string body)
    {
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Object)
            {
                message = r.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()
                    : r.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var em) ? em.GetString()
                    : r.TryGetProperty("error", out var es) && es.ValueKind == JsonValueKind.String ? es.GetString()
                    : null;
            }
        }
        catch (JsonException)
        {
        }
        var what = code switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "didn't accept the key or sign-in",
            HttpStatusCode.NotFound => "doesn't have that",
            HttpStatusCode.TooManyRequests => "says Luma asked too often; try again in a little while",
            >= HttpStatusCode.InternalServerError => "is having trouble right now",
            _ => $"answered {(int)code}",
        };
        return $"{host} {what}" + (string.IsNullOrWhiteSpace(message) ? "." : $": {message}");
    }
}
