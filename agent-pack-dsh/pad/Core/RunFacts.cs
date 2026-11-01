using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// 项目页「运行」卡的现场事实（D57）：进程、pid、监听端口、配置 profile、已跑时长、内存。
/// 全部来自盘上/进程里（D18）：进程现场认 <c>DSH_HOME</c> 归属，端口问系统 TCP 表；
/// PAD 自己起的再叠账本里的 URL（裸端口要 token，不能直接开）。
/// </summary>
public sealed record RunFacts(
    ProjectRun Phase,
    string Home,
    bool PadStarted,
    string? PadProfile,
    string? PadUrl,
    int? Pid,
    string? ProcessName,
    string? Exe,
    string? ConfigProfile,
    IReadOnlyList<Ports.Listener> Listeners,
    int? Port,
    DateTimeOffset? StartedAt,
    TimeSpan? Uptime,
    long WorkingSet,
    bool HasWindow,
    IReadOnlyList<int> Pids,
    string Source)
{
    public bool Live => Phase.Live;

    /// <summary>已跑多久：分钟/小时；刚起来给秒。</summary>
    public string UptimeText
    {
        get
        {
            if (Uptime is not { } up) return "—";
            if (up.TotalMinutes < 1) return $"{(int)up.TotalSeconds} 秒";
            if (up.TotalHours < 1) return $"{(int)up.TotalMinutes} 分钟";
            if (up.TotalDays < 1) return $"{(int)up.TotalHours} 小时 {up.Minutes} 分";
            return $"{(int)up.TotalDays} 天 {up.Hours} 小时";
        }
    }

    /// <summary>主端口：优先回环地址（127.0.0.1），没有就给第一个。</summary>
    public string PortText => Port is { } p ? p.ToString() : "—";

    public string AddressText
    {
        get
        {
            var hit = Listeners.FirstOrDefault(l => l.Port == Port && l.Loopback) ?? Listeners.FirstOrDefault(l => l.Port == Port);
            return hit is null ? "—" : hit.Label;
        }
    }

    public string MemoryText => WorkingSet <= 0 ? "—"
        : WorkingSet < 1024L * 1024 * 1024 ? $"{WorkingSet / (1024 * 1024)} MB"
        : $"{WorkingSet / (1024.0 * 1024 * 1024):0.#} GB";

    public static RunFacts Collect(Instance inst, ProjectRun phase, RunVm? padRun, IReadOnlyList<Nodes.Running> ledger)
    {
        var homeNorm = Norm(inst.Home);
        var procs = new List<ProcInfo>();
        try
        {
            foreach (var proc in ProcTable.Snapshot(900, CancellationToken.None))
            {
                if (proc.Pid == Environment.ProcessId) continue;
                if (RunWatch.IsPadItself(proc.Exe)) continue;
                if (Norm(proc.DshHome) != homeNorm) continue;
                procs.Add(proc);
            }
        }
        catch
        {
            /* 进程现场读不到：只给状态词，不编造 pid/端口 */
        }

        // 主进程：host（dsh-desktop-host / bin.js）优先，其次内存最大的那个。
        var main = procs.FirstOrDefault(p => HostLike(p.CommandLine))
                   ?? procs.OrderByDescending(MemoryOf).FirstOrDefault();

        int? pid = main?.Pid;
        var exe = main?.Exe;
        var name = exe is null ? null : Path.GetFileNameWithoutExtension(exe);
        var ws = main is null ? 0 : MemoryOf(main);
        DateTimeOffset? started = null;
        if (main is not null)
        {
            try
            {
                var p = Process.GetProcessById(main.Pid);
                started = new DateTimeOffset(p.StartTime);
            }
            catch { /* 权限/已退出：不给启动时间 */ }
        }

        var listeners = Ports.Listening(procs.Select(p => p.Pid));
        var port = listeners.Where(l => l.Loopback).Select(l => (int?)l.Port).FirstOrDefault()
                   ?? listeners.Select(l => (int?)l.Port).FirstOrDefault();

        var profile = main?.DshProfile;
        if (string.IsNullOrWhiteSpace(profile)) profile = main?.ProfileArg;
        if (string.IsNullOrWhiteSpace(profile)) profile = ProfileFromCmd(main?.CommandLine);

        var padLedger = padRun is null
            ? null
            : ledger.FirstOrDefault(e => string.Equals(e.Profile, padRun.Profile, StringComparison.OrdinalIgnoreCase));

        var hasWindow = false;
        try
        {
            foreach (var p in procs)
            {
                var proc = Process.GetProcessById(p.Pid);
                if (proc.MainWindowHandle != IntPtr.Zero) { hasWindow = true; break; }
            }
        }
        catch { /* 读窗口句柄失败就当作没有 */ }

        return new RunFacts(
            phase,
            inst.Home,
            padRun is not null,
            padRun?.Profile,
            padLedger?.Url,
            pid,
            name,
            exe,
            string.IsNullOrWhiteSpace(profile) ? null : profile,
            listeners,
            port,
            started,
            started is null ? null : DateTimeOffset.Now - started,
            ws,
            hasWindow,
            procs.Select(p => p.Pid).ToList(),
            padRun is not null ? "PAD 启动" : "外部启动 · 进程现场");
    }

    static bool HostLike(string? cmd) =>
        cmd is not null
        && (cmd.Contains("dsh-desktop-host", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("bin.js", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("--expose-internals", StringComparison.OrdinalIgnoreCase));

    static long MemoryOf(ProcInfo proc)
    {
        try { return Process.GetProcessById(proc.Pid).WorkingSet64; }
        catch { return 0; }
    }

    static readonly Regex ProfileRx = new(@"profiles[\\/]([^\\/\s""']+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static string? ProfileFromCmd(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        var m = ProfileRx.Match(cmd);
        return m.Success ? m.Groups[1].Value : null;
    }

    static string Norm(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant(); }
        catch { return path!.Trim().TrimEnd('\\', '/').ToLowerInvariant(); }
    }
}
