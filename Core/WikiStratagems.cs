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
    /// <summary>One wiki stratagem. <see cref="Icon"/> is the wiki's image file name (e.g. "Reinforce_Stratagem_Icon.svg").</summary>
    public sealed record Entry(string Title, string Code, string Category, string? Icon = null);

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
    public static string IconDir => Path.Combine(AppContext.BaseDirectory, "stratagem_icons");
    public const int IconSize = 72;

    /// <summary>Local path for a wiki icon file (keeps its .svg/.png extension). May not exist yet.</summary>
    public static string IconPath(string wikiFile)
    {
        var file = wikiFile.Trim().Replace(' ', '_');
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is not (".svg" or ".png")) ext = ".svg";
        var name = Path.GetFileNameWithoutExtension(file);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return Path.Combine(IconDir, name + ext);
    }

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
        Log($"codes: {entries.Count} stratagems read from {pages.Count} pages");
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
    private static readonly Regex ImageField = new(@"\|\s*(stratagem_image|image)\s*=\s*([^\n|}]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
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

    /// <summary>Icon file from the infobox. Prefers stratagem_image over the generic image field.</summary>
    internal static string? ParseIcon(string wikitext)
    {
        string? fallback = null;
        foreach (Match m in ImageField.Matches(wikitext))
        {
            var file = m.Groups[2].Value.Trim();
            if (file.StartsWith("File:", StringComparison.OrdinalIgnoreCase)) file = file[5..];
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".svg" or ".png" or ".webp")) continue;
            if (m.Groups[1].Value.Equals("stratagem_image", StringComparison.OrdinalIgnoreCase)) return file;
            fallback ??= file;
        }
        return fallback;
    }

    internal static Entry? ParsePage(string title, string wikitext)
    {
        var code = ParseCode(wikitext);
        var cat = MapCategory(ParseType(wikitext));
        if (code == null || cat == null || code.Length > 12) return null;
        return new Entry(title.Trim(), code, cat, ParseIcon(wikitext));
    }

    // ------------------------------------------------------------------ icons

    /// <summary>
    /// Downloads the given entries' icons into <see cref="IconDir"/>, skipping any already on disk.
    /// wiki.gg serves the original SVG files, which SCS draws itself (see <see cref="SvgIcon"/>), so each
    /// icon is one small request (~2 KB). A file is only kept if it is a PNG or an SVG SCS can actually read.
    /// Failures are logged and skipped, not fatal. Returns how many were downloaded.
    /// </summary>
    public static async Task<int> FetchIconsAsync(IEnumerable<Entry> entries, CancellationToken ct = default)
    {
        var files = entries.Select(e => e.Icon).Where(f => !string.IsNullOrWhiteSpace(f) && !File.Exists(IconPath(f!)))
                           .Select(f => f!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) return 0;
        Directory.CreateDirectory(IconDir);

        using var http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        })
        { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        int downloaded = 0, failed = 0;
        foreach (var file in files.Take(200))
        {
            var url = $"https://helldivers.wiki.gg/wiki/Special:FilePath/{Uri.EscapeDataString(file.Replace(' ', '_'))}";
            try
            {
                using var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) { Log($"icon {file}: HTTP {(int)resp.StatusCode} from {resp.RequestMessage?.RequestUri}"); failed++; continue; }
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                var dest = IconPath(file);
                bool ok = dest.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? IsPng(bytes)
                    : bytes.Length < 1_000_000 && SvgIcon.Parse(System.Text.Encoding.UTF8.GetString(bytes)) != null;
                if (!ok)
                {
                    Log($"icon {file}: unreadable ({resp.Content.Headers.ContentType}, {bytes.Length} bytes) from {resp.RequestMessage?.RequestUri}");
                    failed++; continue;
                }
                await File.WriteAllBytesAsync(dest + ".tmp", bytes, ct);
                File.Move(dest + ".tmp", dest, overwrite: true);
                downloaded++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log($"icon {file}: {ex.GetType().Name}: {ex.Message}");
                failed++;
            }
            await Task.Delay(150, ct);
        }
        Log($"icons: {downloaded} downloaded, {failed} failed, {files.Count} requested");
        return downloaded;
    }

    private static bool IsPng(byte[] b) =>
        b.Length > 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';

    /// <summary>Small diagnostics log next to the exe (wiki_sync.log), kept short.</summary>
    public static void Log(string line)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "wiki_sync.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch { }
    }
}
