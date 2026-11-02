using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>One row of a growth board. <see cref="Growth"/> is the period delta
/// (downloads or new stars), <see cref="Total"/> is the period volume.</summary>
public sealed class MetricRow
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Description { get; set; } = "";
    public string Repository { get; set; } = "";
    public long Total { get; set; }
    public long Growth { get; set; }

    [JsonIgnore] public string Title => Name;
    [JsonIgnore] public string GrowthText => (Growth >= 0 ? "+" : "") + Growth.ToString("N0");
    [JsonIgnore] public string TotalText => Total.ToString("N0");
    [JsonIgnore] public string VersionText => string.IsNullOrWhiteSpace(Version) ? "" : "v" + Version;
}

public sealed class Leaderboard
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public List<MetricRow> Rows { get; set; } = [];
}

public sealed class Leaderboards
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string Note { get; set; } = "";
    public List<Leaderboard> Boards { get; set; } = [];

    [JsonIgnore] public bool FromCache { get; set; }
    [JsonIgnore] public string GeneratedText => GeneratedAt.ToLocalTime().ToString("HH:mm");
}

/// <summary>
/// Download / star growth boards. The store itself is npm search; growth needs two
/// historical series:
///   downloads: npm's range endpoint (one request per candidate, daily buckets)
///   stars: GitHub's privacy-safe stargazers/history endpoint (weekly buckets, newest first)
/// Results are cached on disk; the boards are best-effort, so a rate-limited source just
/// drops rows instead of failing the page.
/// </summary>
public static class MarketMetrics
{
    public const int CandidateCount = 30;
    /// <summary>Daily history kept for the month-over-month windows.</summary>
    public const int HistoryDays = 62;

    const double CacheHours = 6;

    static readonly HttpClient DownloadsHttp = new() { Timeout = TimeSpan.FromSeconds(25) };

    static readonly HttpClient GithubHttp = CreateGithub();

    static HttpClient CreateGithub()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "pack-agent-for-DSH");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
        return http;
    }

    public static string CachePath(Launcher launcher) =>
        Path.Combine(launcher.Root, "library", "cache", "market", "leaderboards.json");

    /// <summary>Cached boards when fresh, otherwise a fresh build. A failed refresh keeps
    /// the previous file visible rather than blanking the page.</summary>
    public static async Task<Leaderboards> GetAsync(Launcher launcher, bool force,
        IProgress<string>? log, CancellationToken ct)
    {
        var path = CachePath(launcher);
        var cached = ReadCache(path);
        if (!force && cached is not null
            && (DateTimeOffset.UtcNow - cached.GeneratedAt).TotalHours < CacheHours)
        {
            cached.FromCache = true;
            return cached;
        }

        try
        {
            var fresh = await BuildAsync(launcher, log, ct);
            WriteCache(path, fresh);
            return fresh;
        }
        catch when (cached is not null)
        {
            cached.FromCache = true;
            cached.Note = "刷新失败，显示缓存";
            return cached;
        }
    }

    public static void ClearCache(Launcher launcher)
    {
        try { File.Delete(CachePath(launcher)); } catch { /* optional */ }
    }

    static async Task<Leaderboards> BuildAsync(Launcher launcher, IProgress<string>? log, CancellationToken ct)
    {
        var candidates = new List<MarketRow>();
        try
        {
            var page = await Market.Search("keywords:dsh-plugin", "", 0, ct, 100);
            candidates = page.Rows
                .Where(r => r.Name.Length > 0)
                .OrderByDescending(r => r.Popularity)
                .Take(CandidateCount)
                .ToList();
        }
        catch (Exception ex) when (ex is PadError)
        {
            throw;
        }
        log?.Report($"排行榜：候选 {candidates.Count} 个");

        var downloads = new ConcurrentDictionary<string, Dictionary<DateOnly, long>>(StringComparer.Ordinal);
        using (var gate = new SemaphoreSlim(6))
        {
            await Task.WhenAll(candidates.Select(async row =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var url = $"https://api.npmjs.org/downloads/range/{WindowStart():yyyy-MM-dd}:{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}/{row.Name}";
                    var body = await DownloadsHttp.GetStringAsync(url, ct);
                    using var doc = JsonDocument.Parse(body);
                    downloads[row.Name] = NpmRangeDays(doc.RootElement);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
                {
                    // One missing package must not blank the board.
                }
                finally { gate.Release(); }
            }));
        }

        var stars = new ConcurrentDictionary<string, Dictionary<DateOnly, long>>(StringComparer.Ordinal);
        var starCacheDir = Path.Combine(launcher.Root, "library", "cache", "market", "stars");
        var token = Environment.GetEnvironmentVariable("PAD_GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        using (var gate = new SemaphoreSlim(2))
        {
            await Task.WhenAll(candidates.Select(async row =>
            {
                if (ParseRepoSlug(row.Repository) is not { } slug) return;
                // 12h per-repo cache keeps repeat runs inside GitHub's anonymous 60/h budget.
                if (TryReadStarCache(starCacheDir, slug, out var cached))
                {
                    stars[row.Name] = cached;
                    return;
                }
                await gate.WaitAsync(ct);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        $"https://api.github.com/repos/{slug}/stargazers/history?per_page=10");
                    if (!string.IsNullOrWhiteSpace(token))
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                    using var response = await GithubHttp.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(body);
                    var days = StarHistoryDays(doc.RootElement);
                    stars[row.Name] = days;
                    WriteStarCache(starCacheDir, slug, days);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
                {
                    // Rate-limited GitHub just trims the star board.
                }
                finally { gate.Release(); }
            }));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        List<MetricRow> Board(Func<MarketRow, Dictionary<DateOnly, long>?> series, int days, DateOnly? end)
        {
            var rows = new List<MetricRow>();
            foreach (var candidate in candidates)
            {
                var daily = series(candidate);
                if (daily is null || daily.Count == 0) continue;
                var effectiveEnd = end;
                if (effectiveEnd is null)
                {
                    // npm's daily buckets lag a couple of days (trailing zeros); anchor the
                    // window on the last day that actually has data.
                    effectiveEnd = daily.Where(kv => kv.Value > 0)
                        .Select(kv => kv.Key)
                        .DefaultIfEmpty(daily.Keys.Max())
                        .Max();
                }
                var (growth, total) = WindowGrowth(daily, days, effectiveEnd);
                rows.Add(new MetricRow
                {
                    Name = candidate.Name,
                    Version = candidate.Version,
                    Publisher = candidate.Publisher,
                    Description = candidate.Description,
                    Repository = candidate.Repository,
                    Growth = growth,
                    Total = total,
                });
            }
            var ranked = rows.OrderByDescending(r => r.Growth).ThenBy(r => r.Name, StringComparer.Ordinal).Take(12).ToList();
            for (var i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
            return ranked;
        }

        var boards = new List<Leaderboard>
        {
            new() { Key = "stars-day", Label = "Star日增", Rows = Board(r => stars.TryGetValue(r.Name, out var v) ? v : null, 1, today) },
            new() { Key = "stars-week", Label = "Star周增", Rows = Board(r => stars.TryGetValue(r.Name, out var v) ? v : null, 7, today) },
            new() { Key = "stars-month", Label = "Star月增", Rows = Board(r => stars.TryGetValue(r.Name, out var v) ? v : null, 30, today) },
            new() { Key = "downloads-day", Label = "下载日增", Rows = Board(r => downloads.TryGetValue(r.Name, out var v) ? v : null, 1, null) },
            new() { Key = "downloads-week", Label = "下载周增", Rows = Board(r => downloads.TryGetValue(r.Name, out var v) ? v : null, 7, null) },
            new() { Key = "downloads-month", Label = "下载月增", Rows = Board(r => downloads.TryGetValue(r.Name, out var v) ? v : null, 30, null) },
        };
        log?.Report($"排行榜：下载 {downloads.Count} / Star {stars.Count}");
        return new Leaderboards
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Note = $"候选 {candidates.Count}（npm 热度）· 下载 {downloads.Count} · Star {stars.Count}",
            Boards = boards,
        };
    }

    static DateOnly WindowStart() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-HistoryDays);

    /// <summary>Sum the last <paramref name="days"/> days and the same window before it.</summary>
    public static (long Growth, long Total) WindowGrowth(IReadOnlyDictionary<DateOnly, long> daily, int days, DateOnly? end = null)
    {
        if (days <= 0 || daily.Count == 0) return (0, 0);
        var last = end ?? daily.Keys.Max();
        long current = 0, previous = 0;
        for (var i = 0; i < days; i++)
            if (daily.TryGetValue(last.AddDays(-i), out var v)) current += v;
        var previousEnd = last.AddDays(-days);
        for (var i = 0; i < days; i++)
            if (daily.TryGetValue(previousEnd.AddDays(-i), out var v)) previous += v;
        return (current - previous, current);
    }

    /// <summary>npm downloads range → day → downloads.</summary>
    public static Dictionary<DateOnly, long> NpmRangeDays(JsonElement root)
    {
        var map = new Dictionary<DateOnly, long>();
        if (!root.TryGetProperty("downloads", out var arr) || arr.ValueKind != JsonValueKind.Array) return map;
        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty("day", out var dayEl) || dayEl.ValueKind != JsonValueKind.String) continue;
            if (!DateOnly.TryParseExact(dayEl.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day)) continue;
            var count = item.TryGetProperty("downloads", out var c) && c.TryGetInt64(out var n) ? n : 0;
            map[day] = count;
        }
        return map;
    }

    /// <summary>GitHub stargazers/history (weekly buckets, newest first, days starts Sunday)
    /// → day → new stars.</summary>
    public static Dictionary<DateOnly, long> StarHistoryDays(JsonElement root)
    {
        var map = new Dictionary<DateOnly, long>();
        if (root.ValueKind != JsonValueKind.Array) return map;
        foreach (var week in root.EnumerateArray())
        {
            if (!week.TryGetProperty("week", out var w) || !w.TryGetInt64(out var unix)) continue;
            if (!week.TryGetProperty("days", out var days) || days.ValueKind != JsonValueKind.Array) continue;
            var start = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime);
            var i = 0;
            foreach (var cell in days.EnumerateArray())
            {
                var count = cell.TryGetInt64(out var n) ? n : 0;
                map[start.AddDays(i)] = count;
                i++;
            }
        }
        return map;
    }

    /// <summary>GitHub / npm repository URL or slug → <c>owner/repo</c>.</summary>
    public static string? ParseRepoSlug(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;
        var s = repository.Trim();
        if (s.StartsWith("github:", StringComparison.OrdinalIgnoreCase)) s = s["github:".Length..];
        s = s.Replace("git+", "", StringComparison.OrdinalIgnoreCase);
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            try { s = new Uri(s).AbsolutePath.Trim('/'); } catch { return null; }
        }
        if (s.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)) s = s["github.com/".Length..];
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : null;
    }

    sealed class StarCache
    {
        public DateTimeOffset At { get; set; }
        public Dictionary<string, long> Days { get; set; } = [];
    }

    static string StarCacheFile(string dir, string slug) =>
        Path.Combine(dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slug))).ToLowerInvariant() + ".json");

    static bool TryReadStarCache(string dir, string slug, out Dictionary<DateOnly, long> days)
    {
        days = null!;
        try
        {
            var file = StarCacheFile(dir, slug);
            if (!File.Exists(file)) return false;
            var cached = JsonSerializer.Deserialize<StarCache>(File.ReadAllText(file));
            if (cached is null || (DateTimeOffset.UtcNow - cached.At).TotalHours >= 12) return false;
            var map = new Dictionary<DateOnly, long>();
            foreach (var (key, value) in cached.Days)
                if (DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var day))
                    map[day] = value;
            if (map.Count == 0) return false;
            days = map;
            return true;
        }
        catch { return false; }
    }

    static void WriteStarCache(string dir, string slug, Dictionary<DateOnly, long> days)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var cached = new StarCache
            {
                At = DateTimeOffset.UtcNow,
                Days = days.ToDictionary(kv => kv.Key.ToString("yyyy-MM-dd"), kv => kv.Value),
            };
            var file = StarCacheFile(dir, slug);
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(cached));
            File.Move(file + ".tmp", file, overwrite: true);
        }
        catch { /* best-effort */ }
    }

    static Leaderboards? ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<Leaderboards>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    static void WriteCache(string path, Leaderboards boards)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(boards));
        }
        catch { /* cache is best-effort */ }
    }
}
