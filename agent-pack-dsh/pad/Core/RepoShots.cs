using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// 插件仓库（GitHub）里的图片：从 README 提取图片链接，下载到本地缓存，给详情页当预览图。
/// 只认仓库自己 README 里的图，过滤 shields.io / badge 之类的徽章。
/// </summary>
public static class RepoShots
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    static readonly string[] ReadmeNames = ["README.md", "readme.md", "Readme.md", "README.markdown", "README.MD"];
    static readonly string[] GoodExts = [".png", ".jpg", ".jpeg", ".webp", ".gif"];
    const int MaxShots = 6;

    static readonly Regex MdImg = new(@"!\[[^\]]*\]\(\s*([^)\s]+)(?:\s+""[^""]*"")?\s*\)", RegexOptions.Compiled);
    static readonly Regex HtmlImg = new(@"<img[^>]+src\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string CacheDir(string root, string spec) =>
        Path.Combine(root, "library", "plugin-shots", LaunchPolicy.BundleBareName(spec).Replace('/', '_'));

    /// <summary>已经下载过的预览图（按文件名排序）。</summary>
    public static IReadOnlyList<string> Cached(string root, string spec)
    {
        try
        {
            var dir = CacheDir(root, spec);
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateFiles(dir)
                .Where(f => GoodExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(MaxShots)
                .ToList();
        }
        catch { return []; }
    }

    public static async Task<IReadOnlyList<string>> EnsureAsync(
        string root, string spec, string? repository, CancellationToken ct = default)
    {
        var cached = Cached(root, spec);
        if (cached.Count > 0) return cached;

        var repo = LaunchPolicy.GithubRepo(repository);
        if (repo is null) return [];

        var dir = CacheDir(root, spec);
        try { Directory.CreateDirectory(dir); } catch { return []; }

        string? readme = null;
        var rawBase = "";
        foreach (var name in ReadmeNames)
        {
            var url = $"https://raw.githubusercontent.com/{repo}/HEAD/{name}";
            var text = await TryGetString(url, ct);
            if (text is not null)
            {
                readme = text;
                rawBase = $"https://raw.githubusercontent.com/{repo}/HEAD/";
                break;
            }
        }
        if (readme is null) return [];

        var urls = new List<string>();
        foreach (Match m in MdImg.Matches(readme)) Add(m.Groups[1].Value);
        foreach (Match m in HtmlImg.Matches(readme)) Add(m.Groups[1].Value);
        if (urls.Count == 0) return [];

        var saved = new List<string>();
        foreach (var url in urls)
        {
            if (saved.Count >= MaxShots) break;
            var ext = Path.GetExtension(url.Split('?')[0]).ToLowerInvariant();
            if (!GoodExts.Contains(ext)) continue;
            var local = Path.Combine(dir, $"shot-{saved.Count + 1:00}{ext}");
            if (await TryDownload(url, local, ct)) saved.Add(local);
        }
        return saved;

        void Add(string raw)
        {
            var s = raw.Trim().Trim('<', '>');
            if (s.Length == 0) return;
            if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (rawBase.Length == 0) return;
                s = rawBase + s.TrimStart('/');
            }
            if (s.Contains("shields.io", StringComparison.OrdinalIgnoreCase)) return;
            if (s.Contains("badge", StringComparison.OrdinalIgnoreCase)) return;
            if (urls.Exists(u => u.Equals(s, StringComparison.OrdinalIgnoreCase))) return;
            urls.Add(s);
        }
    }

    static async Task<string?> TryGetString(string url, CancellationToken ct)
    {
        try
        {
            using var r = await Http.GetAsync(url, ct);
            if (!r.IsSuccessStatusCode) return null;
            return await r.Content.ReadAsStringAsync(ct);
        }
        catch { return null; }
    }

    static async Task<bool> TryDownload(string url, string path, CancellationToken ct)
    {
        try
        {
            using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!r.IsSuccessStatusCode) return false;
            if (r.Content.Headers.ContentLength is > 6_000_000) return false;
            await using var fs = File.Create(path);
            await r.Content.CopyToAsync(fs, ct);
            return new FileInfo(path).Length > 0;
        }
        catch
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            return false;
        }
    }
}
