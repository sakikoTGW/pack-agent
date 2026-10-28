using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 手写的 Electron <c>asar</c> 读取器（不引用 @electron/asar，也不 shell out）。
///
/// 布局（本机两个真实安装实测过）：
/// <code>
/// 0..4    UInt32LE  = 4                       pickle 头，不校验
/// 4..8    UInt32LE  = headerSize = jsonLen + 8
/// 8..12   UInt32LE  = jsonLen + 4
/// 12..16  UInt32LE  = headerJsonLen
/// 16..    UTF-8 JSON 树：{ "files": { name: node } }
///         node = { "files": {..} } | { "size", "offset": "&lt;数字字符串&gt;", "unpacked"?, "link"?, "integrity"? }
/// dataBase = 8 + UInt32LE(bytes[4]) = 16 + headerJsonLen
/// </code>
///
/// 约束：只读、无写盘、无 shell；桌面端正在跑时 asar 会被占用，所以一律用
/// <c>FileShare.ReadWrite | FileShare.Delete</c> 打开；任何异常都算“读不到”，绝不向上抛。
/// 头解析结果按 (完整路径, 文件大小, LastWriteTimeUtc.Ticks) 缓存，最多 <see cref="CacheCap"/> 份。
/// </summary>
public sealed class Asar
{
    /// <summary>安全上限：headerJsonLen 最多 64MB。真实 app.asar 约 5MB。</summary>
    const long MaxHeaderBytes = 64L * 1024 * 1024;

    /// <summary>安全上限：单个 entry 转文本最多 64MB。</summary>
    const long MaxEntryBytes = 64L * 1024 * 1024;

    /// <summary>link 最多跟这么多跳，防止环。</summary>
    const int MaxLinkHops = 8;

    /// <summary>静态缓存上限。</summary>
    const int CacheCap = 8;

    /// <summary>一次 Find 最多看这么多树节点，防止病态归档卡死 worker。</summary>
    const int MaxWalkNodes = 400_000;

    static readonly object Gate = new();
    static readonly List<(CacheKey Key, Header Header)> Cached = [];

    readonly Node _root;
    readonly long _dataBase;
    readonly long _fileLength;

    /// <summary>归档的完整路径。</summary>
    public string ArchivePath { get; }

    Asar(string archivePath, Header header)
    {
        ArchivePath = archivePath;
        _root = header.Root;
        _dataBase = header.DataBase;
        _fileLength = header.FileLength;
    }

    /// <summary>
    /// 打开归档并解析头。路径不存在、头损坏、JSON 超限、任何 IO 异常都返回 null。
    /// </summary>
    public static Asar? Open(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath)) return null;
        try
        {
            var full = Path.GetFullPath(archivePath.Trim().Trim('"'));
            var info = new FileInfo(full);
            if (!info.Exists) return null;

            var key = new CacheKey(full, info.Length, info.LastWriteTimeUtc.Ticks);
            var header = CacheGet(key);
            if (header is null)
            {
                header = Load(full);
                if (header is null) return null;
                CachePut(key, header);
            }
            return new Asar(full, header);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 读归档内一个文本 entry。<paramref name="innerPath"/> 用 '/' 分隔（接受 '\'、前导斜杠）。
    /// 不存在、是目录、越界、读失败、有环，一律 null。
    /// </summary>
    public string? ReadText(string innerPath)
    {
        try
        {
            var normalized = Normalize(innerPath);
            if (normalized is null) return null;
            var node = Resolve(normalized);
            if (node is null || node.Files is not null) return null;
            return ReadNodeText(normalized, node, 0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 找出所有以 <paramref name="suffix"/> 结尾的内部路径（'/' 归一化、无前导斜杠、忽略大小写、去重）。
    /// 广度优先，所以浅层匹配先返回。最多 <paramref name="cap"/> 条（cap &lt;= 0 用默认 8）。
    /// </summary>
    public IReadOnlyList<string> Find(string suffix, int cap = 8)
    {
        var results = new List<string>();
        try
        {
            var wanted = Normalize(suffix);
            if (wanted is null) return results;
            var limit = cap <= 0 ? 8 : cap;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(Node Node, string Path, int Hops)>();
            queue.Enqueue((_root, "", 0));
            var scanned = 0;

            while (queue.Count > 0)
            {
                var (node, path, hops) = queue.Dequeue();
                if (++scanned > MaxWalkNodes) break;

                if (node.Files is not null)
                {
                    // 目录型 link：以 link 所在路径为前缀，继续遍历链接目标
                    if (node.Link is { Length: > 0 } dirLink && hops < MaxLinkHops)
                    {
                        var target = ResolveLinkTarget(path, dirLink);
                        if (target.Node is not null && target.Path is not null)
                            queue.Enqueue((target.Node, path, hops + 1));
                    }
                    foreach (var (name, child) in node.Files)
                    {
                        var childPath = path.Length == 0 ? name : path + "/" + name;
                        queue.Enqueue((child, childPath, hops));
                    }
                    continue;
                }

                var current = node;
                if (current.Link is { Length: > 0 } link && hops < MaxLinkHops)
                {
                    var target = ResolveLinkTarget(path, link);
                    if (target.Node is not null && target.Path is not null)
                    {
                        if (target.Node.Files is not null)
                        {
                            queue.Enqueue((target.Node, path, hops + 1));
                            continue;
                        }
                        current = target.Node;
                    }
                }

                if (!Matches(path, wanted) || !seen.Add(path)) continue;
                results.Add(path);
                if (results.Count >= limit) break;
            }
        }
        catch
        {
            // 返回已经找到的部分
        }
        return results;
    }

    /// <summary>
    /// 这个 asar 里是不是捆着一个 DSH 内核（<c>@deepseek-ai/dsh/package.json</c>
    /// 或桌面端宿主 <c>@deepseek-ai/dsh-desktop-host/package.json</c>）。
    /// </summary>
    public static bool LooksLikeDsh(string archivePath)
    {
        try
        {
            var asar = Open(archivePath);
            if (asar is null) return false;
            return asar.Find("/@deepseek-ai/dsh/package.json", 1).Count > 0
                || asar.Find("/@deepseek-ai/dsh-desktop-host/package.json", 1).Count > 0;
        }
        catch
        {
            return false;
        }
    }

    // ---- 解析 -------------------------------------------------------------

    static Header? Load(string full)
    {
        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var fileLength = fs.Length;
            if (fileLength < 16) return null;

            var head = new byte[16];
            if (!ReadExact(fs, head, head.Length)) return null;

            var headerJsonLen = UInt32LE(head, 12);
            if (headerJsonLen == 0 || headerJsonLen > MaxHeaderBytes) return null;
            if (16L + headerJsonLen > fileLength) return null;

            // dataBase 是 entry 数据区起点：8 + headerSize（= u32@4）。
            var dataBase = 8L + UInt32LE(head, 4);
            if (dataBase < 16 || dataBase > fileLength) return null;

            var jsonBytes = new byte[(int)headerJsonLen];
            fs.Position = 16;
            if (!ReadExact(fs, jsonBytes, jsonBytes.Length)) return null;

            using var doc = JsonDocument.Parse(jsonBytes);
            var root = BuildNode(doc.RootElement);
            if (root?.Files is null) return null;
            return new Header(root, dataBase, fileLength);
        }
        catch
        {
            return null;
        }
    }

    static Node? BuildNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        if (element.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Object)
        {
            var dir = new Node { Files = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase) };
            foreach (var child in files.EnumerateObject())
            {
                var built = BuildNode(child.Value);
                if (built is not null) dir.Files[child.Name] = built;
            }
            return dir;
        }

        var node = new Node();
        if (element.TryGetProperty("unpacked", out var unpacked) && unpacked.ValueKind == JsonValueKind.True)
            node.Unpacked = true;
        if (element.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.String)
            node.Link = link.GetString();
        if (element.TryGetProperty("size", out var size) && TryReadLong(size, out var sizeValue))
            node.Size = sizeValue;
        if (element.TryGetProperty("offset", out var offset) && TryReadLong(offset, out var offsetValue))
            node.Offset = offsetValue;
        return node;
    }

    static bool TryReadLong(JsonElement element, out long value)
    {
        value = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetInt64(out value);
            case JsonValueKind.String:
                return long.TryParse(element.GetString(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value);
            default:
                return false;
        }
    }

    // ---- 读取 -------------------------------------------------------------

    Node? Resolve(string normalized)
    {
        var node = _root;
        foreach (var segment in normalized.Split('/'))
        {
            if (node.Files is null) return null;
            if (!node.Files.TryGetValue(segment, out var next)) return null;
            node = next;
        }
        return node;
    }

    string? ReadNodeText(string normalized, Node node, int hops)
    {
        if (node.Link is { Length: > 0 } link)
        {
            if (hops >= MaxLinkHops) return null;
            var target = ResolveLinkTarget(normalized, link);
            if (target.Node is null || target.Path is null) return null;
            if (target.Node.Files is not null) return null;
            return ReadNodeText(target.Path, target.Node, hops + 1);
        }
        if (node.Unpacked) return ReadUnpackedText(normalized);
        return ReadDataText(node);
    }

    /// <summary>link 先按归档根解析（asar 规范），再按所在目录解析（兼容旧打包器）。</summary>
    (Node? Node, string? Path) ResolveLinkTarget(string containingPath, string link)
    {
        var rootRelative = Normalize(link);
        if (rootRelative is not null)
        {
            var node = Resolve(rootRelative);
            if (node is not null) return (node, rootRelative);
        }

        var slash = containingPath.LastIndexOf('/');
        var dir = slash < 0 ? "" : containingPath[..slash];
        var sibling = Normalize(dir.Length == 0 ? link : dir + "/" + link);
        if (sibling is not null)
        {
            var node = Resolve(sibling);
            if (node is not null) return (node, sibling);
        }
        return (null, null);
    }

    string? ReadDataText(Node node)
    {
        if (node.Offset < 0 || node.Offset > _fileLength) return null;
        if (node.Size < 0 || node.Size > MaxEntryBytes) return null;

        var start = _dataBase + node.Offset;
        if (start < _dataBase || start > _fileLength) return null;
        if (node.Size > _fileLength - start) return null;
        if (node.Size == 0) return "";

        var bytes = new byte[(int)node.Size];
        using var fs = new FileStream(ArchivePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        fs.Position = start;
        return ReadExact(fs, bytes, bytes.Length) ? DecodeText(bytes) : null;
    }

    string? ReadUnpackedText(string normalized)
    {
        var path = Path.Combine(ArchivePath + ".unpacked",
            normalized.Replace('/', Path.DirectorySeparatorChar));
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 0 || info.Length > MaxEntryBytes) return null;
        if (info.Length == 0) return "";

        var bytes = new byte[(int)info.Length];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return ReadExact(fs, bytes, bytes.Length) ? DecodeText(bytes) : null;
    }

    // ---- 小工具 ------------------------------------------------------------

    /// <summary>路径归一化：'\' → '/'、去前导斜杠、去空段、拒绝 '.'/'..'。</summary>
    static string? Normalize(string? innerPath)
    {
        if (string.IsNullOrWhiteSpace(innerPath)) return null;
        var text = innerPath.Trim().Replace('\\', '/');
        while (text.StartsWith("./", StringComparison.Ordinal)) text = text[2..];
        text = text.Trim('/');
        if (text.Length == 0) return null;

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        foreach (var part in parts)
            if (part is "." or "..") return null;
        return string.Join('/', parts);
    }

    static bool Matches(string path, string wanted)
    {
        if (path.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return true;
        return path.Length > wanted.Length
            && path.EndsWith("/" + wanted, StringComparison.OrdinalIgnoreCase);
    }

    static uint UInt32LE(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    static bool ReadExact(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    static string DecodeText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    // ---- 缓存 -------------------------------------------------------------

    static Header? CacheGet(CacheKey key)
    {
        lock (Gate)
        {
            foreach (var (cachedKey, header) in Cached)
                if (cachedKey.Equals(key)) return header;
            return null;
        }
    }

    static void CachePut(CacheKey key, Header header)
    {
        lock (Gate)
        {
            for (var i = 0; i < Cached.Count; i++)
            {
                if (!Cached[i].Key.Equals(key)) continue;
                Cached[i] = (key, header);
                return;
            }
            while (Cached.Count >= CacheCap) Cached.RemoveAt(0);
            Cached.Add((key, header));
        }
    }

    // ---- 内部类型 ----------------------------------------------------------

    readonly record struct CacheKey(string ArchivePath, long Size, long ModifiedTicks);

    sealed record Header(Node Root, long DataBase, long FileLength);

    sealed class Node
    {
        /// <summary>目录：子节点；文件：null。</summary>
        public Dictionary<string, Node>? Files { get; init; }

        public long Offset { get; set; }

        public long Size { get; set; }

        public bool Unpacked { get; set; }

        public string? Link { get; set; }
    }
}
