using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 拼音/首字母搜索的本地小索引：只缓存 npm 热度前 3000 个 DSH 插件（约 12 页），
/// 24 小时一次。查询词本身是拼音或首字母、且 npm 返回结果里一个都没命中时才用它。
/// </summary>
public static class MarketPinyinIndex
{
    const int Pages = 12;
    const int PageSize = 250;
    const int MaxEntries = 3000;
    const int TtlHours = 24;

    public sealed class Entry
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Repository { get; set; } = "";
        public string Homepage { get; set; } = "";
        public double Popularity { get; set; }
        public string Pinyin { get; set; } = "";
        public string Initials { get; set; } = "";
    }

    sealed class IndexFile
    {
        public DateTimeOffset At { get; set; }
        public List<Entry> Entries { get; set; } = [];
    }

    static readonly object Gate = new();
    static List<Entry>? _entries;
    static DateTimeOffset _at;

    static string CachePath(Launcher launcher) =>
        Path.Combine(launcher.Root, "library", "cache", "market", "pinyin-index.json");

    static bool Fresh => _entries is not null &&
                         (DateTimeOffset.UtcNow - _at).TotalHours < TtlHours;

    static Task<bool>? _building;
    static int _warmStarted;

    /// <summary>商店打开后在后台预建索引，用户第一次输拼音时就不用等。</summary>
    public static void Prewarm(Launcher launcher)
    {
        if (Interlocked.Exchange(ref _warmStarted, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await EnsureAsync(launcher, null, CancellationToken.None).ConfigureAwait(false); }
            catch { /* 后台预热，失败不影响搜索 */ }
        });
    }

    /// <summary>确保索引可用；首次会抓 12 页 npm 搜索结果（几秒），之后 24h 内直接读缓存。
    /// 并发调用共享同一次构建。</summary>
    public static Task<bool> EnsureAsync(Launcher launcher, IProgress<string>? progress,
        CancellationToken ct)
    {
        lock (Gate)
        {
            if (Fresh) return Task.FromResult(true);
            if (_building is not null) return _building.WaitAsync(ct);
        }
        var task = BuildAsync(launcher, progress, ct);
        lock (Gate) _building = task;
        return AwaitAndClear(task);

        static async Task<bool> AwaitAndClear(Task<bool> task)
        {
            try { return await task.ConfigureAwait(false); }
            finally { lock (Gate) _building = null; }
        }
    }

    static async Task<bool> BuildAsync(Launcher launcher, IProgress<string>? progress,
        CancellationToken ct)
    {

        var file = CachePath(launcher);
        try
        {
            if (File.Exists(file) && File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddHours(-TtlHours))
            {
                var cached = JsonSerializer.Deserialize<IndexFile>(await File.ReadAllTextAsync(file, ct));
                if (cached is { Entries.Count: > 0 })
                {
                    lock (Gate)
                    {
                        _entries = cached.Entries;
                        _at = cached.At;
                    }
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 缓存坏了就重建
        }

        var built = new List<Entry>();
        try
        {
            for (var page = 0; page < Pages; page++)
            {
                ct.ThrowIfCancellationRequested();
                var from = page * PageSize;
                progress?.Report($"正在建拼音索引 {built.Count}/{MaxEntries}…");
                MarketPage pageData;
                try
                {
                    pageData = await Market.Search("keywords:dsh-plugin", "", from, ct, PageSize);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    pageData = await Market.Search("keywords:dsh-plugin", "", from, ct, PageSize);
                }
                if (pageData.Rows.Count == 0) break;
                foreach (var row in pageData.Rows)
                {
                    var text = row.Name + " " + row.DescriptionText + " " +
                               string.Join(" ", row.Keywords);
                    var (full, initials) = PinyinSearch.Convert(text);
                    built.Add(new Entry
                    {
                        Name = row.Name,
                        Description = row.DescriptionText.Length > 220
                            ? row.DescriptionText[..220]
                            : row.DescriptionText,
                        Version = row.Version,
                        Publisher = row.Publisher,
                        Repository = row.Repository,
                        Homepage = row.Homepage,
                        Popularity = row.Popularity,
                        Pinyin = full,
                        Initials = initials,
                    });
                }
                if (!pageData.HasNext || built.Count >= MaxEntries) break;
            }
            if (built.Count == 0) return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress?.Report("拼音索引失败：" + ex.GetType().Name + " " + ex.Message);
            return false;
        }

        lock (Gate)
        {
            _entries = built;
            _at = DateTimeOffset.UtcNow;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file,
                JsonSerializer.Serialize(new IndexFile { At = DateTimeOffset.UtcNow, Entries = built }), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进缓存也不影响本次搜索
        }
        return true;
    }

    /// <summary>拼音/首字母匹配，按命中强度 + npm 热度排序；最多返回 12 个。</summary>
    public static List<MarketRow> Lookup(string query, int max = 12)
    {
        List<Entry>? entries;
        lock (Gate) entries = _entries;
        return entries is null ? [] : Rank(entries, query, max);
    }

    /// <summary>对给定条目做拼音/首字母匹配；拆出来便于单测。</summary>
    public static List<MarketRow> Rank(IEnumerable<Entry> entries, string query, int max = 12)
    {
        var q = new string(query.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        if (q.Length < 2) return [];

        var scored = new List<(Entry Entry, int Score)>();
        foreach (var entry in entries)
        {
            var score = 0;
            if (entry.Pinyin.Contains(q, StringComparison.Ordinal)) score += 3;
            else if (q.Length <= 6 && entry.Initials.Contains(q, StringComparison.Ordinal)) score += 2;
            if (score == 0) continue;
            scored.Add((entry, score));
        }
        if (scored.Count == 0) return [];

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Popularity)
            .Take(max)
            .Select(x => new MarketRow
            {
                Name = x.Entry.Name,
                Description = x.Entry.Description,
                Version = x.Entry.Version,
                Publisher = x.Entry.Publisher,
                Repository = x.Entry.Repository,
                Homepage = x.Entry.Homepage,
                Popularity = x.Entry.Popularity,
            })
            .ToList();
    }
}
