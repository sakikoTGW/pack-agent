using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pad.Core;

/// <summary>
/// The mark a combination package publishes. TUI's is
/// <c>docs/assets/logo.svg</c> in <c>ccch1mneyyy/dsh-TUI</c>; the npm tarball
/// usually drops <c>docs/</c>, so the second look is jsDelivr of that file.
/// <c>dsh-plugin.json</c> v0.15 has no icon field.
/// </summary>
public static class BundleIcon
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public static event Action? Changed;

    static BundleIcon()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "pack-agent-for-DSH");
    }

    static void Notify()
    {
        try { Changed?.Invoke(); }
        catch { }
    }

    public static string CacheDir(string launcherRoot, string spec)
    {
        var safe = LaunchPolicy.BundleBareName(spec).Replace('/', '_').Replace('@', '_');
        return Path.Combine(launcherRoot, "library", "plugins-meta", safe);
    }

    public static string? Resolve(string launcherRoot, string packageDir, string spec)
    {
        foreach (var name in LaunchPolicy.BundleIconFiles)
        {
            var p = Path.Combine(packageDir, name.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p)) continue;
            // 官方插件图标是 SVG：光栅到 library 缓存再返回，别把 png 写进包目录。
            return p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? RasterizeToCache(launcherRoot, spec, p) ?? p
                : p;
        }
        if (ResolvePackageIcon(launcherRoot, packageDir, spec) is { } npmIcon) return npmIcon;
        var cache = CacheDir(launcherRoot, spec);
        foreach (var name in new[] { "icon.png", "logo.svg", "logo.png" })
        {
            var p = Path.Combine(cache, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static string? RasterizeToCache(string launcherRoot, string spec, string svgPath)
    {
        try
        {
            var cache = CacheDir(launcherRoot, spec);
            Directory.CreateDirectory(cache);
            var png = Path.Combine(cache, "icon.png");
            if (File.Exists(png) &&
                File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(svgPath))
                return png;
            if (!TryRasterizeSvg(File.ReadAllText(svgPath), png, 128)) return null;
            Notify();
            return png;
        }
        catch { return null; }
    }

    /// <summary>
    /// 由 DSH 发行号自带、但包里没有 logo 的插件（base / web-app 之类），用 DSH 自己的品牌图标兜底。
    /// 判断靠“包是从发行号的 pnpm 仓库解析到的”，不靠包名前缀。
    /// </summary>
    public static string? SeedDshBrand(string launcherRoot, string spec) => null;   // CLI 不读 WPF 资源

    /// <summary>没有本地 package.json 时，按 GitHub 仓库（npm 元数据里的 repository）取插件自己的图标。</summary>
    public static async Task<string?> EnsureFromRepo(string launcherRoot, string spec, string? repo,
        CancellationToken ct)
    {
        if (Resolve(launcherRoot, "", spec) is { } have) return have;
        var cache = CacheDir(launcherRoot, spec);
        Directory.CreateDirectory(cache);
        foreach (var url in LaunchPolicy.BundleIconUrls(repo, null))
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(cache,
                url.Contains(".svg", StringComparison.OrdinalIgnoreCase) ? "logo.svg" : "icon.png");
            try
            {
                using var res = await Http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) continue;
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 40 || bytes.Length > 400_000) continue;
                await File.WriteAllBytesAsync(dest, bytes, ct);
                Notify();
                return dest;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* next url */ }
        }
        return null;
    }

    public static string? First(string launcherRoot, IEnumerable<DshProfile> profiles)
    {
        string? fallback = null;
        foreach (var p in profiles)
            foreach (var spec in p.Bundles)
            {
                var dir = LaunchPolicy.BundlePackageDir(p.Dir, spec);
                var icon = Resolve(launcherRoot, dir, spec);
                if (icon is null) continue;
                if (spec.Contains("dsh-tui", StringComparison.OrdinalIgnoreCase)) return icon;
                fallback ??= icon;
            }
        return fallback;
    }

    public static async Task Ensure(string launcherRoot, string packageDir, string spec,
        CancellationToken ct)
    {
        if (Resolve(launcherRoot, packageDir, spec) is not null) return;
        var repo = ReadRepository(Path.Combine(packageDir, "package.json"));
        var ver = ReadVersion(Path.Combine(packageDir, "package.json"));

        // 本地包没带 icon：先从 npm 官方 package icon 取一次，再退回仓库 logo。
        if (await NpmPackageIcon.EnsureAsync(launcherRoot, LaunchPolicy.BundleBareName(spec),
                ver ?? "", ct) is not null)
        {
            Notify();
            return;
        }
        var cache = CacheDir(launcherRoot, spec);
        Directory.CreateDirectory(cache);
        foreach (var url in LaunchPolicy.BundleIconUrls(repo, ver))
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(url);
            if (string.IsNullOrEmpty(ext)) continue;
            var dest = Path.Combine(cache, ext.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                ? "logo.svg" : "icon.png");
            try
            {
                using var res = await Http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) continue;
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 40 || bytes.Length > 400_000) continue;
                await File.WriteAllBytesAsync(dest, bytes, ct);
                Notify();
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* next url */ }
        }
    }

    /// <summary>已安装包自带的 npm 官方 icon：读 package.json 的 icon，本地文件优先，SVG 光栅化。</summary>
    static string? ResolvePackageIcon(string launcherRoot, string packageDir, string spec)
    {
        if (string.IsNullOrWhiteSpace(packageDir)) return null;
        var pkg = Path.Combine(packageDir, "package.json");
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            var icon = doc.RootElement.TryGetProperty("icon", out var value) ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(icon)) return null;
            var relative = icon.Trim().Replace('\\', '/').TrimStart('/');
            while (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
            var local = Path.Combine(packageDir,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(local)) return null;

            if (!Path.GetExtension(local).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                return local;

            var cache = CacheDir(launcherRoot, spec);
            Directory.CreateDirectory(cache);
            var png = Path.Combine(cache, "icon-npm.png");
            if (File.Exists(png)) return png;
            return TryRasterizeSvg(File.ReadAllText(local), png) ? png : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string? ReadRepository(string pkg)
    {
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            var root = doc.RootElement;
            if (root.TryGetProperty("repository", out var repo))
            {
                if (repo.ValueKind == JsonValueKind.String) return repo.GetString();
                if (repo.ValueKind == JsonValueKind.Object
                    && repo.TryGetProperty("url", out var u))
                    return u.GetString();
            }
        }
        catch { }
        return null;
    }

    static string? ReadVersion(string pkg)
    {
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>CLI 不做 SVG 光栅化（那要用 WPF Imaging）；PNG 图标照常。</summary>
    public static string LastRasterizeError { get; private set; } = "CLI 不支持 SVG 光栅化";

    public static bool TryRasterizeSvg(string svg, string pngPath, int size = 64) => false;
}