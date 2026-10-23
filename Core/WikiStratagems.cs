using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SCS.Core;

/// <summary>
/// Pulls stratagem codes from the community wiki (helldivers.wiki.gg) through its MediaWiki API, so new
/// stratagems and code changes show up without rebuilding SCS. Results are cached next to the exe and
/// merged over the built-in list by <see cref="Stratagems.ApplyWiki"/>.
///
/// The wiki is a volunteer-run site, so this is deliberately polite: an identifying User-Agent, maxlag,
/// a pause between requests, a hard cap on requests per update, and at most one automatic update per day.
/// Approach inspired by goncaloestrelado/HelldiversMacro.
/// </summary>
public static class WikiStratagems
{
    public sealed record Entry(string Title, string Code, string Category);

    public sealed class CacheFile
    {
        public DateTime FetchedUtc { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }

    private const string Api = "https://helldivers.wiki.gg/api.php";
    private const string UserAgent = "SCS/2.0 (Stratagem Control System; https://github.com/switchmaxfx/SCS-2.0)";
    private const int BatchSize = 50;
    private const int MaxRequests = 30;
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) };
    public static readonly TimeSpan AutoUpdateInterval = TimeSpan.FromHours(24);

    public static string CachePath => Path.Combine(AppContext.BaseDirectory, "stratagem_cache.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ------------------------------------------------------------------ cache

    public static CacheFile? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var c = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(CachePath), JsonOpts);
            return c is { Entries.Count: > 0 } ? c : null;
        }
        catch { return null; } // a corrupt cache just means "use the built-in list"
    }

    public static void SaveCache(CacheFile c)
    {
        var tmp = CachePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(c, JsonOpts));
        File.Move(tmp, CachePath, overwrite: true);
    }

    public static bool IsDue(CacheFile? c) => c == null || DateTime.UtcNow - c.FetchedUtc > AutoUpdateInterval;

    // ------------------------------------------------------------------ fetch

    /// <summary>Downloads every page in Category:Stratagems and parses out the codes. Throws on failure.</summary>
    public static async Task<CacheFile> FetchAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        var query = new Dictionary<string, string>
        {
            ["action"] = "query",
            ["generator"] = "categorymembers",
            ["gcmtitle"] = "Category:Stratagems",
            ["gcmlimit"] = BatchSize.ToString(),
            ["gcmtype"] = "page",
            ["prop"] = "revisions",
            ["rvprop"] = "content",
            ["rvslots"] = "main",
            ["format"] = "json",
            ["maxlag"] = "5",
        };

        var pages = new Dictionary<string, string>(); // title -> wikitext
        for (int request = 1; request <= MaxRequests; request++)
        {
            progress?.Report($"Contacting wiki (request {request})...");
            using var doc = await GetJsonAsync(http, query, ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("query", out var q) && q.TryGetProperty("pages", out var pageMap))
            {
                foreach (var page in pageMap.EnumerateObject())
                {
                    var p = page.Value;
                    var title = p.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    if (title.Length == 0 || IsExcluded(title)) continue;
                    var text = RevisionText(p);
                    if (text != null) pages[title] = text;
                }
            }

            if (!root.TryGetProperty("continue", out var cont)) break;
            foreach (var kv in cont.EnumerateObject())
                query[kv.Name] = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString()! : kv.Value.GetRawText();
            await Task.Delay(RequestDelay, ct);
        }

        var entries = new List<Entry>();
        foreach (var (title, text) in pages)
        {
            var e = ParsePage(title, text);
            if (e != null) entries.Add(e);
        }
        if (entries.Count == 0)
            throw new InvalidOperationException("The wiki answered, but no stratagem codes could be read from it. The saved list was kept.");

        progress?.Report($"Read {entries.Count} stratagems from the wiki.");
        return new CacheFile { FetchedUtc = DateTime.UtcNow, Entries = entries.OrderBy(e => e.Title).ToList() };
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient http, Dictionary<string, string> query, CancellationToken ct)
    {
        var url = Api + "?" + string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        for (int attempt = 0; ; attempt++)
        {
            HttpResponseMessage resp;
            try { resp = await http.GetAsync(url, ct); }
            catch (HttpRequestException) when (attempt < RetryDelays.Length) { await Task.Delay(RetryDelays[attempt], ct); continue; }

            using (resp)
            {
                var code = (int)resp.StatusCode;
                if (code is 429 or 502 or 503 or 504 && attempt < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[attempt], ct);
                    continue;
                }
                resp.EnsureSuccessStatusCode();
                var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                // maxlag: the wiki's servers are busy, back off and try again.
                if (doc.RootElement.TryGetProperty("error", out var err)
                    && err.TryGetProperty("code", out var ec) && ec.GetString() == "maxlag"
                    && attempt < RetryDelays.Length)
                {
                    doc.Dispose();
                    await Task.Delay(RetryDelays[attempt], ct);
                    continue;
                }
                return doc;
            }
        }
    }

    private static string? RevisionText(JsonElement page)
    {
        if (!page.TryGetProperty("revisions", out var revs) || revs.GetArrayLength() == 0) return null;
        var rev = revs[0];
        if (rev.TryGetProperty("slots", out var slots) && slots.TryGetProperty("main", out var main))
        {
            if (main.TryGetProperty("*", out var star)) return star.GetString();
            if (main.TryGetProperty("content", out var content)) return content.GetString();
        }
        return rev.TryGetProperty("*", out var legacy) ? legacy.GetString() : null;
    }

    // ------------------------------------------------------------------ parsing

    private static readonly Regex CodeTemplate = new(@"\{\{\s*Stratagem[ _]?code\s*\|([^}]+)\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TypeField = new(@"\|\s*stratagem_type\s*=\s*([^\n|}]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LangSubpage = new(@"/[a-z]{2}(?:-[a-z]{2})?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal static bool IsExcluded(string title)
    {
        if (title.Contains("(disambiguation)") || title.StartsWith("Template:") || title.StartsWith("Community:")) return true;
        var lower = title.ToLowerInvariant();
        if (lower.Contains("april fools") || lower.Contains("aprilfools")) return true;
        if (LangSubpage.IsMatch(title)) return true; // translated copies like "Reinforce/de"
        return title is "Stratagems" or "Equipment Traits" or "Reinforcement Pods";
    }

    /// <summary>"{{Stratagem code|down|left|up}}" -> "DLU". Null if the page has no code.</summary>
    internal static string? ParseCode(string wikitext)
    {
        var m = CodeTemplate.Match(wikitext);
        if (!m.Success) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var part in m.Groups[1].Value.Split('|'))
        {
            char? c = part.Trim().ToLowerInvariant() switch
            {
                "up" or "u" => 'U',
                "down" or "d" => 'D',
                "left" or "l" => 'L',
                "right" or "r" => 'R',
                _ => null
            };
            if (c != null) sb.Append(c.Value);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    internal static string? ParseType(string wikitext)
    {
        var m = TypeField.Match(wikitext);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>Wiki stratagem_type -> SCS category. Pages without a type are mission names etc., not stratagems.</summary>
    internal static string? MapCategory(string? wikiType) => wikiType?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "support weapon" => "Support Weapon",
        "orbital" => "Orbital",
        "eagle" => "Eagle",
        "sentry" => "Sentry",
        "emplacement" or "mines" or "mine" => "Emplacement",
        "backpack" => "Backpack",
        "vehicle" or "exosuit" => "Vehicle",
        _ => "Mission", // Ship, Objective, Other, Mission...
    };

    internal static Entry? ParsePage(string title, string wikitext)
    {
        var code = ParseCode(wikitext);
        var cat = MapCategory(ParseType(wikitext));
        if (code == null || cat == null || code.Length > 12) return null;
        return new Entry(title.Trim(), code, cat);
    }
}
