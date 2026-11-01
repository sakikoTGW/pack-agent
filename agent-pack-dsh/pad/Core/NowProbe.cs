using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// D72：直接读 DSH 的会话投影缓存（<c>&lt;home&gt;/storages/session_projcache/sessions/*.json</c>），
/// 为「现在」页和悬停预览拿到比 <see cref="Dialogs"/> 更细的现场：
/// 标题、cwd、preset、回合/步数、openStep（在跑）、userQuestions（等你输入）、
/// pendingCalls（工具调用）、turnOutline.draft（最近一轮输出）。
/// 投影里每行是 <c>{ver,seq,val}</c> 包装，这里统一拆 val。只读，不写 DSH 任何文件。
/// </summary>
public sealed record NowSession(
    string Id,
    string Title,
    string Cwd,
    string Preset,
    string Model,
    DateTimeOffset? LastAt,
    int Turns,
    int Steps,
    bool Open,
    bool Waiting,
    int Pending,
    string Draft,
    string? Tool);

public static class NowProbe
{
    public static List<NowSession> Read(string home, int limit = 64)
    {
        var list = new List<NowSession>();
        var dir = Path.Combine(home, "storages", "session_projcache", "sessions");
        if (!Directory.Exists(dir)) return list;
        try
        {
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.json")
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .Take(limit))
            {
                var one = ReadOne(file);
                if (one is not null) list.Add(one);
            }
        }
        catch { /* 读不到就当没有 */ }
        return list;
    }

    static NowSession? ReadOne(FileInfo file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file.FullName));
            if (!doc.RootElement.TryGetProperty("record", out var rec)) return null;
            if (!rec.TryGetProperty("rows", out var rows)) return null;

            var id = Path.GetFileNameWithoutExtension(file.Name);
            var cwd = Str(rec, "identity", "cwd") ?? "";
            var title = StrVal(rows, "title") ?? "";
            var preset = StrVal(rows, "agentPreset") ?? "";
            var model = Str(Maybe(rows, "modelSelection"), "lastUsed", "model") ?? "";

            var stats = Val(rows, "sessionStats");
            var turns = Int(stats, "turns");
            var steps = Int(stats, "steps");
            var open = Obj(stats, "openStep");
            var pending = CountArr(stats, "pendingCalls");
            var tool = FirstName(Maybe(stats, "pendingCalls"));

            var q = Val(rows, "userQuestions");
            var activeAsk = CountArr(Maybe(q, "questions"), "active");

            var outline = Val(rows, "turnOutline");
            var draft = Str(outline, "draft") ?? "";
            var ctx = Val(rows, "timeContext");
            var lastMs = Long(ctx, "lastMessageTime");
            DateTimeOffset? at = lastMs is { } ms && ms > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : file.LastWriteTimeUtc;
            if (string.IsNullOrWhiteSpace(title))
                title = string.IsNullOrWhiteSpace(cwd) ? "（新会话）" : Path.GetFileName(cwd.TrimEnd('\\', '/'));
            return new NowSession(id, title, cwd, preset, model, at, turns, steps,
                open, activeAsk > 0 || pending > 0, pending, Short(draft, 320), tool);
        }
        catch { return null; }
    }

    /// <summary>rows 里的一行可能是直值，也可能是 {ver,seq,val}；统一取 val。</summary>
    static JsonElement? Val(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("val", out var inner)) return inner;
        return v;
    }

    static JsonElement? Maybe(JsonElement? el, string key) =>
        el is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(key, out var v) ? v : null;

    static string? StrVal(JsonElement rows, string key)
    {
        var v = Val(rows, key);
        return v is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
    }

    static string? Str(JsonElement? el, params string[] path)
    {
        var cur = el;
        foreach (var key in path)
        {
            if (cur is not { ValueKind: JsonValueKind.Object } o || !o.TryGetProperty(key, out var next)) return null;
            cur = next;
        }
        return cur is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
    }

    static bool Obj(JsonElement? el, string key) =>
        el is { ValueKind: JsonValueKind.Object } o
        && o.TryGetProperty(key, out var v)
        && v.ValueKind is JsonValueKind.Object or JsonValueKind.Array;

    static int Int(JsonElement? el, string key)
    {
        if (el is not { ValueKind: JsonValueKind.Object } o) return 0;
        if (!o.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number) return 0;
        return v.TryGetInt32(out var n) ? n : 0;
    }

    static long? Long(JsonElement? el, string key)
    {
        if (el is not { ValueKind: JsonValueKind.Object } o) return null;
        if (!o.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        return v.TryGetInt64(out var n) ? n : null;
    }

    static int CountArr(JsonElement? el, string? key = null)
    {
        if (el is null) return 0;
        var cur = el.Value;
        if (key is not null)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(key, out var next)) return 0;
            cur = next;
        }
        return cur.ValueKind switch
        {
            JsonValueKind.Array => cur.GetArrayLength(),
            JsonValueKind.String => cur.GetString()?.Length > 0 ? 1 : 0,
            JsonValueKind.Object => cur.EnumerateObject().Count(),
            _ => 0,
        };
    }

    static string? FirstName(JsonElement? el)
    {
        if (el is not { ValueKind: JsonValueKind.Array } arr) return null;
        foreach (var call in arr.EnumerateArray())
        {
            foreach (var key in new[] { "name", "toolName", "tool" })
            {
                if (call.ValueKind == JsonValueKind.Object
                    && call.TryGetProperty(key, out var nm)
                    && nm.ValueKind == JsonValueKind.String)
                    return nm.GetString();
            }
        }
        return null;
    }

    static string Short(string text, int max)
    {
        var t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }
}
