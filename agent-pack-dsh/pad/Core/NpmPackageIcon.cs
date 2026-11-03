using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pad.Core;

/// <summary>
/// npm 官方包图标（package.json 的 `icon` 字段，相对包内路径）。搜索接口不返回它，
/// 所以按包名查一次 `/{name}/latest`（6 小时内缓存），再用 jsDelivr 取原图：
/// SVG 走 BundleIcon 的光栅化，PNG/JPG/GIF 直接缓存成文件。
/// </summary>
public static class NpmPackageIcon
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string, (string Version, string Icon, DateTimeOffset At)> Cache = new(StringComparer.OrdinalIgnoreCase);

    static NpmPackageIcon()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "pack-agent-for-DSH");
    }

    /// <summary>解析 `/{name}/latest` 响应里的 version + icon；供单测。</summary>
    public static (string Version, string Icon)? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            var icon = root.TryGetProperty("icon", out var i) ? i.GetString() ?? "" : "";
            return icon.Length == 0 ? null : (version, icon);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>包内相对路径 → jsDelivr URL；scoped 包名原样拼在 @version 前。</summary>
    public static string BuildUrl(string name, string version, string iconPath)
    {
        var path = iconPath.Trim().Replace('\\', '/').TrimStart('/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var ver = version.Length > 0 ? "@" + Uri.EscapeDataString(version) : "";
        return $"https://cdn.jsdelivr.net/npm/{name}{ver}/{encoded}";
    }

    /// <summary>返回本地缓存文件路径；没有自定义 icon、取图失败或格式不支持时返回 null。</summary>
    public static async Task<string?> EnsureAsync(string launcherRoot, string name, string knownVersion,
        CancellationToken ct)
    {
        var (version, icon) = await InfoAsync(name, knownVersion, ct);
        if (icon.Length == 0) return null;

        var dir = BundleIcon.CacheDir(launcherRoot, name);
        try { Directory.CreateDirectory(dir); } catch { return null; }

        var ext = Path.GetExtension(icon).ToLowerInvariant();
        if (ext is not (".svg" or ".png" or ".jpg" or ".jpeg" or ".gif")) return null;
        var dest = Path.Combine(dir, ext == ".svg" ? "icon-npm.png" : "icon-npm" + ext);
        if (File.Exists(dest)) return dest;

        try
        {
            var bytes = await Http.GetByteArrayAsync(BuildUrl(name, version, icon), ct);
            ct.ThrowIfCancellationRequested();
            if (ext == ".svg")
            {
                var svg = Encoding.UTF8.GetString(bytes);
                return BundleIcon.TryRasterizeSvg(svg, dest) ? dest : null;
            }
            await File.WriteAllBytesAsync(dest + ".tmp", bytes, ct);
            File.Move(dest + ".tmp", dest, overwrite: true);
            return dest;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static async Task<(string Version, string Icon)> InfoAsync(string name, string knownVersion,
        CancellationToken ct)
    {
        if (Cache.TryGetValue(name, out var hit) &&
            (DateTimeOffset.UtcNow - hit.At).TotalHours < 6)
            return (hit.Version, hit.Icon);
        try
        {
            var json = await Http.GetStringAsync(
                $"https://registry.npmjs.org/{Uri.EscapeDataString(name).Replace("%40", "@")}/latest", ct);
            if (ParseLatest(json) is not { } parsed) return ("", "");
            Cache[name] = (parsed.Version, parsed.Icon, DateTimeOffset.UtcNow);
            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ("", "");
        }
    }
}
