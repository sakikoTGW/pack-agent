using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 关系的唯一真源：工作区 → 实例 → agent → 子 agent。
/// 这里只负责"谁属于谁"，不负责怎么画；所有计数都由结构算出来，不存在手写的数字（D35/D37）。
/// </summary>
public static class Graph
{
    public sealed record Agent(
        string Id,
        string Title,
        DateTimeOffset Touched,
        int Depth,
        string? ParentId,
        IReadOnlyList<Agent> Children)
    {
        public bool IsChild => Depth > 0;

        /// <summary>会话最后 known 的上下文占用（surfaceTokens）；0 = 缓存里没写。</summary>
        public long Ctx { get; init; }

        /// <summary>该会话声明过的上下文窗口；0 = 未知。Ctx/Win 才是"水位"。</summary>
        public long Win { get; init; }

        /// <summary>已被压缩进摘要的轮数（archived 长度）。</summary>
        public int Compactions { get; init; }
    }

    public sealed record InstanceNode(
        Instance Inst,
        string Code,
        bool Running,
        IReadOnlyList<Agent> Agents)
    {
        public int Total => Count(Agents);
        public int Subagents => Total - Agents.Count;

        internal static int Count(IReadOnlyList<Agent> list) =>
            list.Sum(a => 1 + Count(a.Children));
    }

    public sealed record WorkspaceNode(
        string Path,
        string Label,
        IReadOnlyList<InstanceNode> Instances);

    public sealed record Model(
        IReadOnlyList<WorkspaceNode> Workspaces,
        int Instances,
        int Agents,
        int Subagents);

    public static Model Build(IReadOnlyList<Instance> insts)
    {
        var byDir = insts
            .GroupBy(i => (i.Workspace?.Path ?? "").TrimEnd('\\', '/').ToLowerInvariant())
            .Select(g => (Dir: g.Key, Items: g.ToList()))
            .OrderBy(g => g.Dir, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var workspaces = new List<WorkspaceNode>();
        var instanceCount = 0;
        var agentCount = 0;
        var subagentCount = 0;

        foreach (var (dir, items) in byDir)
        {
            var nodes = new List<InstanceNode>();
            foreach (var inst in items)
            {
                var agents = AgentsOf(inst.Home);
                var total = InstanceNode.Count(agents);
                nodes.Add(new InstanceNode(inst, Fingerprint.Of(inst).Code,
                    Nodes.Ledger.Load(inst.Home).Any(e => Proc.Alive(e.Pid)), agents));

                instanceCount++;
                agentCount += total;
                subagentCount += total - agents.Count;
            }

            var label = dir.Length == 0 ? "(没有 workspace)" : Short(dir);
            workspaces.Add(new WorkspaceNode(dir, label, nodes));
        }

        return new Model(workspaces, instanceCount, agentCount, subagentCount);
    }

    static string Short(string dir)
    {
        var name = Path.GetFileName(dir);
        if (name is "home" or "dsh-home" or "harness" or "workspace")
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? "");
            if (!string.IsNullOrWhiteSpace(parent)) return parent + "/" + name;
        }
        return string.IsNullOrWhiteSpace(name) ? dir : name;
    }

    /// <summary>一条会话缓存里我们能读到的事实。</summary>
    sealed record Session(string Id, string Title, DateTimeOffset Touched,
        long Ctx, long Win, int Compactions,
        IReadOnlyList<(string ChildId, string Label)> Children);

    /// <summary>
    /// 全部会话，不加截断：缺信息比慢更糟。子 agent 若没有自己的缓存文件（被清理或还没落盘），
    /// 仍然按目录里登记的名字画出来——"它在"这件事本身就是信息。
    /// </summary>
    static IReadOnlyList<Agent> AgentsOf(string home, int maxDepth = 4)
    {
        var dir = Path.Combine(home, "storages", "session_projcache", "sessions");
        if (!Directory.Exists(dir)) return [];

        FileInfo[] files;
        try { files = new DirectoryInfo(dir).GetFiles("*.json"); }
        catch { return []; }

        var sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (id.StartsWith("session-", StringComparison.Ordinal)) id = id["session-".Length..];

            var title = "";
            var children = new List<(string, string)>();
            long ctx = 0, win = 0;
            var compactions = 0;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(file.FullName));
                if (doc.RootElement.TryGetProperty("record", out var rec) &&
                    rec.TryGetProperty("rows", out var rows))
                {
                    if (rows.TryGetProperty("title", out var t) &&
                        t.TryGetProperty("val", out var tv) && tv.ValueKind == JsonValueKind.String)
                    {
                        title = (tv.GetString() ?? "").Trim();
                    }

                    // 规模：和 inspect 同源的事实（surfaceTokens / contextWindow / archived），
                    // 这里顺手读走，图形界面就不用为每个会话再跑一次 inspect
                    if (rows.TryGetProperty("contextPressure", out var cp) &&
                        cp.TryGetProperty("val", out var cv) && cv.ValueKind == JsonValueKind.Object)
                    {
                        ctx = LongOf(cv, "surfaceTokens");
                        win = LongOf(cv, "contextWindow");
                    }
                    if (rows.TryGetProperty("contextTimeline", out var tl) &&
                        tl.TryGetProperty("val", out var lv) && lv.ValueKind == JsonValueKind.Object &&
                        lv.TryGetProperty("archived", out var arch) &&
                        arch.ValueKind == JsonValueKind.Array)
                    {
                        compactions = arch.GetArrayLength();
                    }

                    if (rows.TryGetProperty("subagentCatalog", out var sc) &&
                        sc.TryGetProperty("val", out var sv) &&
                        sv.TryGetProperty("head", out var head) &&
                        head.TryGetProperty("values", out var values) &&
                        values.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var v in values.EnumerateArray())
                        {
                            if (!v.TryGetProperty("childId", out var cid) ||
                                cid.ValueKind != JsonValueKind.String) continue;
                            var label = v.TryGetProperty("label", out var l) &&
                                        l.ValueKind == JsonValueKind.String
                                ? (l.GetString() ?? "").Trim()
                                : "";
                            children.Add((cid.GetString()!, label));
                        }
                    }
                }
            }
            catch { /* 坏缓存：保留 id 与时间 */ }

            sessions[id] = new Session(id,
                string.IsNullOrWhiteSpace(title) ? "未命名会话" : title,
                file.LastWriteTime, ctx, win, compactions, children);
        }

        if (sessions.Count == 0) return [];

        // 被别处登记为子会话的，不在顶层重复出现。
        var claimed = sessions.Values
            .SelectMany(s => s.Children.Select(c => c.ChildId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roots = sessions.Values
            .Where(s => !claimed.Contains(s.Id))
            .OrderByDescending(s => s.Touched)
            .Select(s => Build(s, 0, null, maxDepth))
            .ToList();

        return roots;
    }

    static Agent Build(Session session, int depth, string? parent, int maxDepth)
    {
        var children = new List<Agent>();
        if (depth < maxDepth)
        {
            foreach (var (childId, label) in session.Children)
            {
                // 子会话通常也在缓存里；不在就只画登记项（规模为零）。
                children.Add(new Agent(
                    childId,
                    string.IsNullOrWhiteSpace(label) ? "子 agent" : label,
                    DateTimeOffset.MinValue,
                    depth + 1,
                    session.Id,
                    []));
            }
        }

        return new Agent(session.Id, session.Title, session.Touched, depth, parent, children)
        {
            Ctx = session.Ctx,
            Win = session.Win,
            Compactions = session.Compactions,
        };
    }

    static long LongOf(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;

    /// <summary>
    /// 距最后活动多少分钟；-1 表示会话没落盘。给视觉通道用（热度点），
    /// 文案仍由 Ago 生成——同一事实，两套表达，都从这一个数出发。
    /// </summary>
    public static double AgeMinutes(DateTimeOffset when) =>
        when == DateTimeOffset.MinValue ? -1 : (DateTimeOffset.Now - when).TotalMinutes;

    /// <summary>How long ago, in the two units that matter to a person scanning a list.</summary>
    public static string Ago(DateTimeOffset when)
    {
        if (when == DateTimeOffset.MinValue) return "未落盘";
        var span = DateTimeOffset.Now - when;
        if (span.TotalMinutes < 1) return "刚刚";
        if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " 分钟前";
        if (span.TotalHours < 24) return (int)span.TotalHours + " 小时前";
        return (int)span.TotalDays + " 天前";
    }
}
