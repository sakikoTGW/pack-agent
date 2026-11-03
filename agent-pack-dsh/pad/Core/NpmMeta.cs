using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 一个 npm 包的预检元数据：版本 + 与 DSH 兼容门相关的两个字段。
/// <see cref="Found"/>=false 表示 registry 404（包已下架）；<see cref="Resolved"/>=false
/// 表示网络/超时/未知错误（预检标 unknown，不阻塞安装）。
/// </summary>
public sealed class NpmPackageMeta
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    /// <summary>package.json <c>engines.dsh</c>；没有为 null。</summary>
    public string? EnginesDsh { get; init; }
    /// <summary>peerDependencies 原文（键大小写不敏感）。</summary>
    public Dictionary<string, string> PeerDependencies { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Found { get; init; } = true;
    public bool Resolved { get; init; } = true;

    public static NpmPackageMeta Missing(string name) => new() { Name = name, Found = false };
    public static NpmPackageMeta Unknown(string name, string? version = null) =>
        new() { Name = name, Version = version ?? "", Resolved = false };
}

/// <summary>
/// 预检的 npm 元数据源。单元测试注入假实现（绝不在单测里打网络）；CLI/UI 用
/// <see cref="NpmMetaSource.Shared"/>（进程内缓存 + 并发上限 + 15s 超时）。
/// </summary>
public interface INpmMetaSource
{
    /// <summary>
    /// 解析一个包：<paramref name="version"/> 为空 → latest；给了精确版本 → 那个版本
    /// （预检包钉版本时用它判断兼容性）。网络失败也不抛，返回 Resolved=false。
    /// </summary>
    Task<NpmPackageMeta> Get(string packageName, string? version, CancellationToken ct);
}

/// <summary>
/// 真 npm registry 元数据源（<c>npm view &lt;pkg&gt; version engines peerDependencies --json</c>）。
/// 并发 ≤4、单次 15s 超时、进程内缓存（同一包同一版本只查一次）。
/// </summary>
public sealed class NpmMetaSource : INpmMetaSource
{
    public static NpmMetaSource Shared { get; } = new();

    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly ConcurrentDictionary<string, Lazy<Task<NpmPackageMeta>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim _gate = new(4, 4);
    readonly Func<string, string?, CancellationToken, Task<NpmPackageMeta>>? _fetch;
    readonly string _cwd;

    /// <summary>CLI/UI 用：真 npm。缓存跨调用保留。</summary>
    public NpmMetaSource() : this(Path.GetTempPath()) { }

    /// <summary>测试/嵌入用：可以换一个取数函数（cwd 只影响子进程工作目录）。</summary>
    public NpmMetaSource(string cwd,
        Func<string, string?, CancellationToken, Task<NpmPackageMeta>>? fetch = null)
    {
        _cwd = string.IsNullOrWhiteSpace(cwd) ? Path.GetTempPath() : cwd;
        _fetch = fetch;
    }

    public async Task<NpmPackageMeta> Get(string packageName, string? version, CancellationToken ct)
    {
        var name = (packageName ?? "").Trim();
        if (name.Length == 0) return NpmPackageMeta.Unknown("");
        var ver = (version ?? "").Trim();
        var key = ver.Length == 0 ? name : $"{name}@{ver}";
        var lazy = _cache.GetOrAdd(key, _ => new Lazy<Task<NpmPackageMeta>>(
            () => FetchCachedAsync(name, ver), LazyThreadSafetyMode.ExecutionAndPublication));
        return await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
    }

    async Task<NpmPackageMeta> FetchCachedAsync(string name, string version)
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            return await FetchAsync(name, version, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<NpmPackageMeta> FetchAsync(string name, string version, CancellationToken ct)
    {
        if (_fetch is not null)
        {
            try { return await _fetch(name, version, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return NpmPackageMeta.Unknown(name, version); }
            catch (Exception) { return NpmPackageMeta.Unknown(name, version); }
        }

        var spec = version.Length == 0 ? name : $"{name}@{version}";
        var args = $"view \"{spec}\" version engines peerDependencies --json";
        ProcResult result;
        try
        {
            result = await Proc.Run("npm", args, _cwd, null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return NpmPackageMeta.Unknown(name, version);
        }
        catch (Exception)
        {
            // 没有 npm / 进程起不来：预检不阻塞安装。
            return NpmPackageMeta.Unknown(name, version);
        }
        if (ct.IsCancellationRequested) return NpmPackageMeta.Unknown(name, version);

        var text = result.Tail ?? "";
        var json = ExtractJson(text);
        if (result.ExitCode != 0)
        {
            // npm 404 = 包/版本在 registry 上不存在（已下架）；其它错误 = unknown（可重试）。
            var lower = text.ToLowerInvariant();
            if (lower.Contains("e404") || lower.Contains("404 not found") || lower.Contains("not found :"))
                return NpmPackageMeta.Missing(name).WithVersion(version);
            return NpmPackageMeta.Unknown(name, version);
        }
        if (json is null) return NpmPackageMeta.Unknown(name, version);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            // npm view 只返回一个字段时（例如包没有 engines/peerDependencies），输出是标量
            // `"0.2.2"` 而不是对象；这仍然是一个已知版本、没有兼容要求 → ok。
            if (root.ValueKind == JsonValueKind.String)
                return new NpmPackageMeta { Name = name, Version = root.GetString() ?? version };
            if (root.ValueKind == JsonValueKind.Number)
                return new NpmPackageMeta { Name = name, Version = root.GetRawText() };
            if (root.ValueKind != JsonValueKind.Object) return NpmPackageMeta.Unknown(name, version);

            var resolvedVersion = "";
            if (root.TryGetProperty("version", out var verEl))
                resolvedVersion = verEl.ValueKind == JsonValueKind.String ? verEl.GetString() ?? "" : "";

            string? enginesDsh = null;
            if (root.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object
                && engines.TryGetProperty("dsh", out var dshEl))
            {
                enginesDsh = dshEl.ValueKind == JsonValueKind.String ? dshEl.GetString() : dshEl.GetRawText();
            }

            var peers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("peerDependencies", out var peerEl) && peerEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var peer in peerEl.EnumerateObject())
                {
                    var range = peer.Value.ValueKind switch
                    {
                        JsonValueKind.String => peer.Value.GetString() ?? "",
                        _ => peer.Value.GetRawText(),
                    };
                    peers[peer.Name] = range;
                }
            }

            return new NpmPackageMeta
            {
                Name = name,
                Version = resolvedVersion.Length > 0 ? resolvedVersion : version,
                EnginesDsh = enginesDsh,
                PeerDependencies = peers,
            };
        }
        catch (JsonException)
        {
            return NpmPackageMeta.Unknown(name, version);
        }
    }

    /// <summary>从 npm 输出里抠出 JSON（可能是标量字符串 / 对象；可能有 npm warn 行）。</summary>
    static string? ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        if (IsJson(trimmed)) return trimmed;                       // 无警告行时最常见
        var objStart = text.IndexOf('{', StringComparison.Ordinal);
        if (objStart >= 0)
        {
            var objEnd = text.LastIndexOf('}');
            if (objEnd > objStart && IsJson(text[objStart..(objEnd + 1)]))
                return text[objStart..(objEnd + 1)];
        }
        var strStart = text.IndexOf('"', StringComparison.Ordinal);
        if (strStart >= 0)
        {
            var strEnd = text.LastIndexOf('"');
            if (strEnd > strStart && IsJson(text[strStart..(strEnd + 1)]))
                return text[strStart..(strEnd + 1)];
        }
        return null;
    }

    static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

static class NpmPackageMetaExtensions
{
    public static NpmPackageMeta WithVersion(this NpmPackageMeta meta, string version) => new()
    {
        Name = meta.Name,
        Version = string.IsNullOrWhiteSpace(meta.Version) ? version : meta.Version,
        Found = meta.Found,
        Resolved = meta.Resolved,
    };
}
