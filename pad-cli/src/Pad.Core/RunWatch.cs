using System.IO;

namespace Pad.Core;

/// <summary>
/// 一个项目此刻处在哪一段：没跑 / PAD 正在起 / 在跑。
/// 三态是给界面用的，判据全部是盘上和进程里的事实（D18：PAD 只呈现，不判读）。
/// </summary>
public enum RunPhase
{
    Stopped,
    Starting,
    Running,
}

/// <summary>
/// 一个实例的运行状态。<see cref="Detail"/> 写"是哪条证据"，给 tooltip 用——
/// 不是给人下结论的，是让人能自己复核。
/// </summary>
public sealed record ProjectRun(string InstanceId, RunPhase Phase, string Detail)
{
    public bool Live => Phase is RunPhase.Starting or RunPhase.Running;

    /// <summary>界面上唯一允许出现的三个词。</summary>
    public string Say => Phase switch
    {
        RunPhase.Starting => "启动中",
        RunPhase.Running => "在跑",
        _ => "未启动",
    };
}

/// <summary>
/// 「这个项目在不在跑」的唯一判定处：侧栏、项目卡、<c>pad cli status</c> 都走这里，
/// 免得三处各说各话（D52）。
///
/// 只看 PAD 账本是不够的（D25 #2 的老账）：桌面端的 home 从不写 PAD 账本，
/// 双击 <c>launch-*.cmd</c> / <c>cli node open</c> 起的进程也不写。所以读四个来源：
/// <list type="number">
/// <item>PAD 账本 <c>runtime.json</c>（PAD 自己起的 run，pid 还活着）；</item>
/// <item>节点账本 <c>&lt;home&gt;/.pad/nodes.json</c>（<c>pad cli node open</c> 起的节点）；</item>
/// <item>home 锁 <see cref="LaunchPolicy.LiveHomeOwner"/>（桌面壳独占 home 父目录的 lockfile）；</item>
/// <item>进程现场 <see cref="ProbeHomes"/>：进程的 <c>DSH_HOME</c> 指向该 home（最硬的归属证据）。</item>
/// </list>
/// 外加一个瞬态：<see cref="MarkStarting"/> 登记的"PAD 正在启动"——点下去灯就亮，
/// 不用等账本或端点。窗口关掉即清除，不靠超时猜。
/// </summary>
public static class RunWatch
{
    /// <summary>启动中登记：LaunchFloat 从 Begin 到窗口关闭之间写。带时间是为了 PAD 崩了也不会永久亮。</summary>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> Starting_ =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Discover 扫出来的"在跑 home"（进程现场级别）。工作台加载时灌进来，之后每 2s 的轻扫描复用它。</summary>
    static HashSet<string> _deep = new(StringComparer.OrdinalIgnoreCase);
    static readonly object Gate = new();

    /// <summary>点「启动」时登记：这一行立刻显示"启动中"。</summary>
    public static void MarkStarting(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        Starting_[instanceId!] = DateTimeOffset.UtcNow;
    }

    /// <summary>启动流程结束（成功或失败）时清除。清早了会掉回"未启动"，所以只在窗口关闭时调。</summary>
    public static void ClearStarting(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        Starting_.TryRemove(instanceId!, out _);
    }

    /// <summary>是否在 PAD 的启动流程里。超过启动宽限期视为失效（PAD 崩在启动中不会永久亮）。</summary>
    public static bool Starting(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return false;
        return Starting_.TryGetValue(instanceId!, out var at)
               && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(LaunchPolicy.RunStartGraceSec);
    }

    /// <summary>灌入 Discover 的"在跑 home"清单（工作台每次加载后调用）。</summary>
    public static void NoteHomes(IEnumerable<string>? runningHomes)
    {
        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var home in runningHomes ?? [])
        {
            var norm = Norm(home);
            if (norm is not null) next.Add(norm);
        }
        lock (Gate) _deep = next;
    }

    /// <summary>当前记着的"在跑 home"（只读快照）。</summary>
    public static IReadOnlySet<string> DeepHomes()
    {
        lock (Gate) return new HashSet<string>(_deep, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 进程现场（PEB）里 <c>DSH_HOME</c> 指向的 home。TUI / 双击脚本起的实例不写 PAD 账本，
    /// 只能问进程；这是 <c>pad cli status</c> 的深证据。读不到进程就按没有算，不抛。
    /// </summary>
    public static IReadOnlySet<string> ProbeHomes(CancellationToken ct = default)
    {
        var homes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;
        try
        {
            foreach (var proc in ProcTable.Snapshot(900, ct))
            {
                // PAD 自己也带 DSH_HOME：GUI 从宿主 harness 继承、从某实例终端里被拉起时
                // 会继承那个实例的 home。把自己算成"实例在跑"就是假绿灯（现场核查 2026-10-31）。
                if (proc.Pid == self || IsPadItself(proc.Exe)) continue;
                var norm = Norm(proc.DshHome);
                if (norm is not null) homes.Add(norm);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* 进程表现场读不到：状态灯宁可少亮，也不能把页面拖挂 */ }
        return homes;
    }

    /// <summary>PAD 家族的 exe（<c>pack-agent-for DSH.exe</c>）：启动器不是 DSH 载体，不算运行证据。</summary>
    public static bool IsPadItself(string? exe)
    {
        var name = Path.GetFileNameWithoutExtension((exe ?? "").Trim().Trim('"'));
        return name.StartsWith("pack-agent", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 批量算状态。便宜：只读 JSON / 试锁 / 查活 pid，不扫进程表——
    /// UI 每 2s 调一次，深证据由 <see cref="NoteHomes"/> 提前灌好。
    /// </summary>
    /// <param name="ledger"><c>launcher.Runtime().Entries</c>；调用方已经在同一拍里读过，避免重复读盘。</param>
    public static IReadOnlyDictionary<string, ProjectRun> Scan(
        Launcher launcher,
        IReadOnlyList<Instance> instances,
        IReadOnlyList<RunEntry> ledger)
    {
        var map = new Dictionary<string, ProjectRun>(StringComparer.OrdinalIgnoreCase);
        var deep = DeepHomes();

        foreach (var inst in instances)
        {
            if (string.IsNullOrWhiteSpace(inst.Id)) continue;
            try
            {
                map[inst.Id] = Read(launcher, inst, ledger, deep);
            }
            catch
            {
                // 单个实例的盘读不动，不能连累整列灯
                map[inst.Id] = new ProjectRun(inst.Id, RunPhase.Stopped, "");
            }
        }
        return map;
    }

    /// <summary>单个实例的状态（<see cref="Scan"/> 的逐行版）。</summary>
    public static ProjectRun Read(
        Launcher launcher,
        Instance inst,
        IReadOnlyList<RunEntry> ledger,
        IReadOnlySet<string>? deep = null)
    {
        if (Starting(inst.Id)) return new ProjectRun(inst.Id, RunPhase.Starting, "PAD 正在启动");

        var home = Norm(inst.Home);
        var why = new List<string>();

        if (ledger.Any(e => SameId(e.Instance, inst.Id) && Proc.Alive(e.Pid)))
            why.Add("PAD 账本");
        if (!string.IsNullOrWhiteSpace(inst.Home) && Nodes.Ledger.Load(inst.Home).Any(e => Proc.Alive(e.Pid)))
            why.Add("节点账本");
        if (!string.IsNullOrWhiteSpace(inst.Home) && LaunchPolicy.LiveHomeOwner(inst.Home) is not null)
            why.Add("home 锁");
        if (home is not null && (deep ?? DeepHomes()).Contains(home))
            why.Add("进程现场");

        return why.Count > 0
            ? new ProjectRun(inst.Id, RunPhase.Running, string.Join(" · ", why))
            : new ProjectRun(inst.Id, RunPhase.Stopped, "");
    }

    static bool SameId(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 路径归一化：盘符大小写（实例里存的是 <c>e:\</c>，进程里是 <c>E:\</c>）、尾部反斜杠、
    /// 相对路径都在这里抹平；比较统一用 OrdinalIgnoreCase。
    /// </summary>
    static string? Norm(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path!.Trim().Trim('"'));
            return full.TrimEnd('\\', '/');
        }
        catch
        {
            return null;
        }
    }
}
