using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 视图定义（V1）：一列一个字段，写在 JSON 里，不写进代码。
/// 目的：加一个视图只改文件；加一种数据才动代码。
///
/// 文件位置（后读的覆盖先读的）
///   pad/Assets/views/*.json        随包发布
///   &lt;launcher root&gt;/views/*.json  用户/二次开发覆盖
/// </summary>
public static class ViewCatalog
{
    public sealed record Column(string Key, string Label, int Width = 0, string Align = "left");

    public sealed record Spec(
        string Id,
        string Title,
        /// project | session
        string Orientation,
        /// table（当前唯一实现）
        string Renderer,
        /// 数据源名，由 Host 提供：projects | sessions
        string Source,
        IReadOnlyList<Column> Columns,
        string Note = "");

    public sealed record Table(string Id, string Title, string Orientation, string Renderer,
        IReadOnlyList<Column> Columns, IReadOnlyList<object> Rows, string Note);

    /// <summary>随包内置的定义；文件缺失时仍然能工作。</summary>
    static readonly Spec[] BuiltIn =
    [
        new("projects", "按项目", "project", "table", "projects",
        [
            new Column("label", "工作区", 260),
            new Column("instances", "实例", 70, "right"),
            new Column("agents", "agent", 70, "right"),
            new Column("running", "在跑", 70, "right"),
        ], "以工作区为单位看结构"),

        new("sessions", "按会话", "session", "table", "sessions",
        [
            new Column("title", "会话", 320),
            new Column("project", "工作区", 200),
            new Column("instance", "实例", 140),
            new Column("state", "状态", 90),
            new Column("when", "最后活动", 110, "right"),
        ], "以 agent（会话）为单位平铺"),

        // icicle 镜头：不需要列，tree 就是全部输入。宽度 = 会话上下文 token。
        new("sessions-icicle", "按占比", "icicle", "icicle", "sessions",
        [], "宽度 = 上下文 token · 颜色 = 活动热度 · 点一段只看这一支"),
    ];

    public static IReadOnlyList<Spec> Load(string? root)
    {
        var loaded = new List<Spec>(BuiltIn);
        var seen = new HashSet<string>(BuiltIn.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Dirs(root))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in SafeFiles(dir))
            {
                foreach (var spec in Read(file))
                {
                    loaded.RemoveAll(s => string.Equals(s.Id, spec.Id, StringComparison.OrdinalIgnoreCase));
                    loaded.Add(spec);
                    seen.Add(spec.Id);
                }
            }
        }
        return loaded;
    }

    static IEnumerable<string> Dirs(string? root)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "views");
        if (!string.IsNullOrWhiteSpace(root)) yield return Path.Combine(root!, "views");
    }

    static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.json"); }
        catch { return []; }
    }

    static IEnumerable<Spec> Read(string file)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllText(file)); }
        catch { yield break; }

        using (doc)
        {
            var list = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : [doc.RootElement];

            foreach (var item in list)
            {
                var spec = Parse(item);
                if (spec is not null) yield return spec;
            }
        }
    }

    static Spec? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;

        var id = Str(item, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var columns = new List<Column>();
        if (item.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cols.EnumerateArray())
            {
                var key = Str(c, "key");
                if (string.IsNullOrWhiteSpace(key)) continue;
                columns.Add(new Column(key!, Str(c, "label") ?? key!,
                    Int(c, "width"), Str(c, "align") ?? "left"));
            }
        }
        if (columns.Count == 0) return null;

        return new Spec(
            id!,
            Str(item, "title") ?? id!,
            Str(item, "orientation") ?? "project",
            Str(item, "renderer") ?? "table",
            Str(item, "source") ?? "projects",
            columns,
            Str(item, "note") ?? "");
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
