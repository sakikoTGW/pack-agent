using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 一条对话（DSH session）在盘上的可读摘要（D55）。
///
/// DSH 把进行中的会话投影写进 <c>&lt;home&gt;/storages/session_projcache/sessions/*.json</c>：
/// 标题、cwd、preset、模型、最后消息时间、上下文水位、回合数，以及 <c>sessionStats.openStep</c>
/// ——那是 DSH 自己"还有一步没走完"的事实。PAD 只读不写（D18：呈现，不判读）。
///
/// 这里**不**代替管理口：真正的"agent 在跑"仍以 DSH 的 <c>session.list</c> 为准；
/// 没有管理口时，openStep / 最后消息时间只能说明"这份会话最近有没有被写过"。
/// </summary>
public sealed record DialogRow(
    string Id,
    string Title,
    string Cwd,
    string Preset,
    string Model,
    DateTimeOffset? LastActivity,
    int ContextTokens,
    int ContextWindow,
    int Turns,
    int PendingCalls,
    bool StepOpen,
    string Subagent,
    bool Blank)
{
    /// <summary>cwd 的末级目录名：对话属于哪个项目，一眼能看出来。</summary>
    public string Workspace
    {
        get
        {
            var full = Cwd.TrimEnd('\\', '/');
            if (full.Length == 0) return "";
            var name = Path.GetFileName(full);
            return name.Length > 0 ? name : full;
        }
    }

    /// <summary>
    /// DSH 自己记着"这一步还没收尾"（<c>sessionStats.openStep</c>）。
    /// 有管理口时以 <c>session.list</c> 为准；没有时这是唯一可复核的"正在干"证据。
    /// </summary>
    public bool Executing => StepOpen || PendingCalls > 0;

    /// <summary>"最后消息"离现在多久：秒 / 分 / 时 / 日期。</summary>
    public string AgeText => Age(LastActivity);

    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (Workspace.Length > 0) parts.Add(Workspace);
            if (Preset.Length > 0) parts.Add("preset " + Preset);
            if (Model.Length > 0) parts.Add(Model);
            if (Turns > 0) parts.Add(Turns + " 回合");
            if (ContextWindow > 0) parts.Add($"{ContextTokens / 1000}k / {ContextWindow / 1000}k 上下文");
            if (Subagent.Length > 0) parts.Add("子 agent：" + Subagent);
            return string.Join(" · ", parts);
        }
    }

    public static string Age(DateTimeOffset? at)
    {
        if (at is null) return "—";
        var d = DateTimeOffset.Now - at.Value;
        if (d.TotalSeconds < 0) return "刚刚";
        if (d.TotalSeconds < 60) return $"{(int)d.TotalSeconds} 秒前";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} 分钟前";
        if (d.TotalHours < 24) return at.Value.ToString("HH:mm");
        return at.Value.ToString("MM-dd HH:mm");
    }
}

/// <summary>读 <c>session_projcache</c>：只读、按落盘时间取最近的 N 条。</summary>
public static class Dialogs
{
    /// <summary>
    /// 这个 home 最近的对话摘要。按投影缓存的文件写入时间取前 <paramref name="limit"/> 个
    /// （每次落盘整份重写，mtime 就是"这份会话最近一次被写"的事实），解析失败就跳过。
    /// </summary>
    public static IReadOnlyList<DialogRow> Read(string home, int limit = 16)
    {
        var rows = new List<DialogRow>();
        if (string.IsNullOrWhiteSpace(home)) return rows;
        var dir = Path.Combine(home, "storages", "session_projcache", "sessions");
        if (!Directory.Exists(dir)) return rows;

        List<FileInfo> files;
        try
        {
            files = new DirectoryInfo(dir)
                .EnumerateFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(Math.Max(1, limit))
                .ToList();
        }
        catch
        {
            return rows;
        }

        foreach (var file in files)
        {
            try
            {
                if (Parse(file, out var row) && row is not null) rows.Add(row);
            }
            catch
            {
                /* 半写坏的缓存不挡整张卡 */
            }
        }
        rows.Sort((a, b) =>
            (b.LastActivity ?? DateTimeOffset.MinValue).CompareTo(a.LastActivity ?? DateTimeOffset.MinValue));
        return rows;
    }

    /// <summary>
    /// 只要"有几条对话、最近一条多会儿写的"：数目录 + 读文件时间，**不解析 JSON**。
    /// 主页要给每个项目一行用量，185 份 1MB 缓存的解析成本不能压在这里（D60）。
    /// </summary>
    public static (int Count, DateTimeOffset? Newest) Summary(string home)
    {
        if (string.IsNullOrWhiteSpace(home)) return (0, null);
        var dir = Path.Combine(home, "storages", "session_projcache", "sessions");
        if (!Directory.Exists(dir)) return (0, null);
        try
        {
            var newest = DateTime.MinValue;
            var count = 0;
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.json"))
            {
                count++;
                if (file.LastWriteTimeUtc > newest) newest = file.LastWriteTimeUtc;
            }
            return (count, count == 0
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(newest, DateTimeKind.Utc)).ToLocalTime());
        }
        catch
        {
            return (0, null);
        }
    }

    static bool Parse(FileInfo file, out DialogRow? row)
    {
        row = null;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(file.FullName));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
        if (!doc.RootElement.TryGetProperty("record", out var rec) || rec.ValueKind != JsonValueKind.Object)
            return false;
        if (!rec.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Object)
            return false;

        var id = Path.GetFileNameWithoutExtension(file.Name);
        if (id.StartsWith("session-", StringComparison.OrdinalIgnoreCase)) id = id["session-".Length..];

        var cwd = "";
        if (rec.TryGetProperty("identity", out var identity) && identity.ValueKind == JsonValueKind.Object)
            cwd = Str(identity, "cwd");

        var title = Str(Val(rows, "title"));
        if (title.Length == 0) title = FirstPrompt(Val(rows, "turnOutline"));
        if (title.Length == 0) title = id.Length > 8 ? id[..8] : id;

        var lastMessage = Long(Val(rows, "timeContext"), "lastMessageTime");
        var lastPrompt = Long(Val(rows, "sessionListMetadata"), "lastPromptAt");
        var at = lastMessage > 0 ? Ms(lastMessage)
            : lastPrompt > 0 ? Ms(lastPrompt)
            : new DateTimeOffset(file.LastWriteTime);

        var preset = Str(Val(rows, "agentPreset"));
        var model = ModelOf(Val(rows, "modelSelection")).Model;
        var contextTokens = (int)Long(Val(rows, "contextPressure"), "surfaceTokens");
        var contextWindow = (int)Long(Val(rows, "contextPressure"), "contextWindow");

        var stats = Val(rows, "sessionStats");
        var turns = (int)Long(stats, "turns");
        var pending = CountProps(stats, "pendingCalls");
        var stepOpen = stats is { } st && st.TryGetProperty("openStep", out var step)
                       && step.ValueKind == JsonValueKind.Object
                       && step.TryGetProperty("step", out _);

        var sub = "";
        if (Val(rows, "subagent") is { } sa
            && sa.TryGetProperty("identity", out var subId)
            && subId.ValueKind == JsonValueKind.Object)
            sub = Str(subId, "label");

        var blank = Bool(Val(rows, "sessionListMetadata"), "blank");

        row = new DialogRow(id, title, cwd, preset, model, at,
            contextTokens, contextWindow, turns, pending, stepOpen, sub, blank);
        return true;
    }

    static DateTimeOffset Ms(long unixMs)
    {
        try { return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime(); }
        catch { return DateTimeOffset.Now; }
    }

    /// <summary>标题为空时用第一轮提示词的第一行当标题（截断）。</summary>
    static string FirstPrompt(JsonElement? outline)
    {
        if (outline is not { } o || !o.TryGetProperty("turns", out var turns)
            || turns.ValueKind != JsonValueKind.Array || turns.GetArrayLength() == 0) return "";
        if (!turns[0].TryGetProperty("prompt", out var prompt) || prompt.ValueKind != JsonValueKind.String) return "";
        var text = (prompt.GetString() ?? "").Trim();
        var line = text.Split('\n')[0].Trim();
        if (line.Length > 34) line = line[..34] + "…";
        return line;
    }

    static (string Model, string Effort) ModelOf(JsonElement? selection)
    {
        if (selection is not { } sel || !sel.TryGetProperty("lastUsed", out var used)
            || used.ValueKind != JsonValueKind.Object) return ("", "");
        var provider = Str(used, "provider");
        var model = Str(used, "model");
        var effort = Str(used, "reasoningEffort");
        var name = model.Length > 0 ? model : provider;
        return (name.Length > 0 && effort.Length > 0 ? name + " " + effort : name, effort);
    }

    // ---- JSON 取值（缓存里每行都是 {ver,seq,val} 信封）----

    static JsonElement? Val(JsonElement rows, string key) =>
        rows.TryGetProperty(key, out var row)
        && row.ValueKind == JsonValueKind.Object
        && row.TryGetProperty("val", out var val)
        && val.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? val
            : null;

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>信封值本身就是字符串时用这个（title / agentPreset）。</summary>
    static string Str(JsonElement? e) =>
        e is { } el && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    static long Long(JsonElement? e, string name)
    {
        if (e is not { } el || !el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
            return 0;
        if (v.TryGetInt64(out var n)) return n;
        if (v.TryGetDouble(out var d)) return (long)d;
        return 0;
    }

    static bool Bool(JsonElement? e, string name) =>
        e is { } el && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static int CountProps(JsonElement? e, string name)
    {
        if (e is not { } el || !el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object)
            return 0;
        var n = 0;
        foreach (var _ in v.EnumerateObject()) n++;
        return n;
    }
}
