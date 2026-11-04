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
/// 所以按包名查一次 `/{name}/latest`；元数据与图标文件都落盘缓存，第二次打开页面
/// 直接命中本地文件，不再请求 registry。
/// </summary>
public static class NpmPackageIcon
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    static readonly object Gate = new();
    static readonly Dictionary<string, Meta> Memory = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> LoadedRoots = new(StringComparer.OrdinalIgnoreCase);
    static DateTimeOffset _lastSave = DateTimeOffset.MinValue;

    sealed class Meta
    {
        public string Version { get; set; } = "";
        public string Icon { get; set; } = "";
        public DateTimeOffset At { get; set; }
    }

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
        // 已有图标文件：直接返回，完全不走网络。
        if (FindCached(launcherRoot, name) is { } cached) return cached;

        var (version, icon) = await InfoAsync(launcherRoot, name, ct);
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

    static string? FindCached(string launcherRoot, string name)
    {
        try
        {
            var dir = BundleIcon.CacheDir(launcherRoot, name);
            if (!Directory.Exists(dir)) return null;
            foreach (var file in Directory.EnumerateFiles(dir, "icon-npm.*"))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".gif")
                    return file;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    static async Task<(string Version, string Icon)> InfoAsync(string launcherRoot, string name,
        CancellationToken ct)
    {
        LoadMeta(launcherRoot);
        var key = launcherRoot + "|" + name;
        lock (Gate)
        {
            if (Memory.TryGetValue(key, out var hit) &&
                (DateTimeOffset.UtcNow - hit.At).TotalHours < 24)
                return (hit.Version, hit.Icon);
        }
        try
        {
            var json = await Http.GetStringAsync(
                $"https://registry.npmjs.org/{Uri.EscapeDataString(name).Replace("%40", "@")}/latest", ct);
            if (ParseLatest(json) is not { } parsed) return ("", "");
            lock (Gate)
            {
                Memory[key] = new Meta
                {
                    Version = parsed.Version,
                    Icon = parsed.Icon,
                    At = DateTimeOffset.UtcNow,
                };
            }
            SaveMeta(launcherRoot);
            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ("", "");
        }
    }

    static string MetaPath(string launcherRoot) =>
        Path.Combine(launcherRoot, "library", "cache", "market", "npm-icons.json");

    static void LoadMeta(string launcherRoot)
    {
        lock (Gate)
        {
            if (!LoadedRoots.Add(launcherRoot)) return;
        }
        try
        {
            var file = MetaPath(launcherRoot);
            if (!File.Exists(file)) return;
            var items = JsonSerializer.Deserialize<Dictionary<string, Meta>>(File.ReadAllText(file));
            if (items is null) return;
            lock (Gate)
            {
                foreach (var (name, meta) in items)
                    Memory[launcherRoot + "|" + name] = meta;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    static void SaveMeta(string launcherRoot)
    {
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow - _lastSave < TimeSpan.FromSeconds(1)) return;
            _lastSave = DateTimeOffset.UtcNow;
        }
        try
        {
            var prefix = launcherRoot + "|";
            Dictionary<string, Meta> snapshot;
            lock (Gate)
            {
                snapshot = Memory
                    .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key[prefix.Length..], kv => kv.Value,
                        StringComparer.OrdinalIgnoreCase);
            }
            var file = MetaPath(launcherRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
