using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 整合包自己的图标（D105 UX）。只读包文件本身：
/// <list type="bullet">
/// <item><c>.pack.zip</c> / <c>.pinst.zip</c>：从 zip 中央目录找根（或唯一一级目录）里的
/// <c>icon.* / logo.* / pack.png</c>，把字节落到
/// <c>&lt;launcherRoot&gt;/library/icons/packs/&lt;sha16&gt;.&lt;ext&gt;</c> 复用缓存；
/// svg 用 <see cref="BundleIcon.TryRasterizeSvg"/> 光栅化成 png（STA）。</item>
/// <item><c>.pack.json</c>：先看同目录候选图标；再看 pack.json 的 <c>icon</c>/<c>logo</c>
/// 相对路径（非 svg 直接返回源文件；svg 同样光栅化缓存）。</item>
/// </list>
/// 不写实例、不联网；坏包/坏 zip 一律返回 null，不抛。
/// </summary>
public static class PackIcon
{
    /// <summary>候选名按优先级：icon.* 先于 logo.*；同优先级 root 先于唯一一级目录。</summary>
    static readonly string[] Names =
    [
        "icon.png", "icon.jpg", "icon.jpeg", "icon.svg", "icon.ico",
        "logo.png", "logo.jpg", "logo.svg",
        "pack.png",
    ];

    /// <summary>单个图标上限 2MB（压缩炸弹/巨型资源不进 UI）。</summary>
    const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>包图标的本地文件路径（缓存 PNG 或 json 旁边的源文件）；解析不到返回 null。</summary>
    public static string? Resolve(string launcherRoot, string? packPath)
    {
        var path = (packPath ?? "").Trim().Trim('"');
        if (path.Length == 0) return null;
        try
        {
            if (!File.Exists(path)) return null;
            var lower = path.ToLowerInvariant();
            if (lower.EndsWith(".zip")) return FromZip(launcherRoot, path);
            if (lower.EndsWith(".json")) return FromPackJson(launcherRoot, path);
            // 直接给了图片文件也认（调用方兜底）。
            return IsImage(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or JsonException
                                       or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>缓存目录：&lt;root&gt;/library/icons/packs。</summary>
    public static string CacheDir(string launcherRoot) =>
        Path.Combine(launcherRoot, "library", "icons", "packs");

    // ---- zip -------------------------------------------------------------

    static string? FromZip(string launcherRoot, string packPath)
    {
        using var zip = ZipFile.OpenRead(packPath);
        var entry = FindEntry(zip);
        if (entry is null) return null;
        var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
        if (!IsSupportedImage(ext)) return null;
        if (entry.Length <= 0 || entry.Length > MaxBytes) return null;
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Materialize(launcherRoot, packPath, buffer.ToArray(), ext);
    }

    static ZipArchiveEntry? FindEntry(ZipArchive zip)
    {
        var files = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .ToList();
        foreach (var wanted in Names)
            foreach (var entry in files)
                if (string.Equals(Norm(entry.FullName), wanted, StringComparison.OrdinalIgnoreCase))
                    return entry;

        // 只有一个一级目录（常见的 `MyPack/...` 打包方式）时，允许在其中找。
        var tops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in files)
        {
            var name = Norm(entry.FullName);
            var slash = name.IndexOf('/');
            tops.Add(slash > 0 ? name[..slash] : "");
        }
        if (tops.Count != 1 || tops.Contains("")) return null;
        var top = tops.First();
        foreach (var wanted in Names)
            foreach (var entry in files)
                if (string.Equals(Norm(entry.FullName), top + "/" + wanted, StringComparison.OrdinalIgnoreCase))
                    return entry;
        return null;
    }

    static string Norm(string name) => name.Replace('\\', '/').TrimStart('.', '/');

    // ---- pack.json -------------------------------------------------------

    static string? FromPackJson(string launcherRoot, string packPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(packPath));
        if (string.IsNullOrWhiteSpace(dir)) return null;

        // ① 同目录候选图标（用户放包时最常见的形态）。
        foreach (var name in Names)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return CacheOrSource(launcherRoot, packPath, candidate);
        }

        // ② pack.json 里 icon / logo 指向的相对文件（InstIcon 的老逻辑；这里只读不改它）。
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "icon", "logo" })
            {
                if (!doc.RootElement.TryGetProperty(key, out var value)
                    || value.ValueKind != JsonValueKind.String) continue;
                var rel = (value.GetString() ?? "").Trim();
                if (rel.Length == 0) continue;
                var abs = Path.IsPathRooted(rel)
                    ? rel
                    : Path.GetFullPath(Path.Combine(dir, rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(abs)) continue;
                return CacheOrSource(launcherRoot, packPath, abs);
            }
        }
        catch (JsonException) { /* 坏 pack.json 不抛 */ }
        return null;
    }

    // ---- 缓存 -------------------------------------------------------------

    /// <summary>json 包的图标不在内嵌字节里：非 svg 直接返回源文件；svg 光栅化到缓存。</summary>
    static string? CacheOrSource(string launcherRoot, string packPath, string source)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (!IsSupportedImage(ext)) return null;
        try
        {
            if (new FileInfo(source).Length > MaxBytes) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (ext != ".svg") return source;
        var svg = File.ReadAllText(source);
        var png = Path.Combine(CacheDir(launcherRoot), CacheKey(packPath) + ".png");
        Directory.CreateDirectory(CacheDir(launcherRoot));
        if (File.Exists(png) && new FileInfo(png).Length > 0) return png;
        BundleIcon.TryRasterizeSvg(svg, png, 64);
        // 注意：TryRasterizeSvg 在文件流尚未 Dispose 时用 FileInfo.Length 判空，可能返回 false
        // 但 png 实际已写好（已有行为，见 BundleIcon）。这里按落地文件判断，避免白丢图标。
        return File.Exists(png) && new FileInfo(png).Length > 0 ? png : null;
    }

    static string? Materialize(string launcherRoot, string packPath, byte[] bytes, string ext)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) return null;
        if (string.IsNullOrWhiteSpace(launcherRoot)) return null;
        var dir = CacheDir(launcherRoot);
        Directory.CreateDirectory(dir);
        var key = CacheKey(packPath);
        if (ext == ".svg")
        {
            // WPF Image 不能直接吃 svg：光栅化成 png 缓存（STA；调用方在 UI 线程时天然满足）。
            var png = Path.Combine(dir, key + ".png");
            if (File.Exists(png) && new FileInfo(png).Length > 0) return png;
            var svg = Encoding.UTF8.GetString(bytes);
            BundleIcon.TryRasterizeSvg(svg, png, 64);
            return File.Exists(png) && new FileInfo(png).Length > 0 ? png : null;
        }
        var dest = Path.Combine(dir, key + ext);
        if (File.Exists(dest) && new FileInfo(dest).Length == bytes.Length) return dest;
        var tmp = dest + "." + Environment.ProcessId + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, dest, overwrite: true);
        return dest;
    }

    /// <summary>缓存 key：包路径 + mtime + size 的 sha256 前 16 位（包更新会自然换 key）。</summary>
    static string CacheKey(string packPath)
    {
        var info = new FileInfo(packPath);
        var raw = packPath.ToLowerInvariant() + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    static bool IsSupportedImage(string ext) => ext is ".png" or ".jpg" or ".jpeg" or ".svg" or ".ico";

    static bool IsImage(string path) => IsSupportedImage(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>清理 14 天没被碰过的旧缓存（可选；只在写入路径上顺手做，不抛）。</summary>
    public static void TrimCache(string launcherRoot, TimeSpan? olderThan = null)
    {
        try
        {
            var dir = CacheDir(launcherRoot);
            if (!Directory.Exists(dir)) return;
            var limit = DateTime.UtcNow - (olderThan ?? TimeSpan.FromDays(14));
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < limit) File.Delete(file);
                }
                catch { /* 被占用就下次清 */ }
            }
        }
        catch { /* 清缓存失败不影响取图 */ }
    }
}
