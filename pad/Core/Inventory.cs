using System.IO;

namespace Pad.Core;

/// <summary>
/// 本机 DSH 资源清单的数据模型。
///
/// 判「是不是同一个 DSH」看内核（<see cref="Kernel"/>）：磁盘上的一份
/// <c>@deepseek-ai/dsh</c> 包。外壳（桌面端 / TUI / CLI）只是载体，home 只是数据。
/// 同一个版本可能有多份内核拷贝（两个桌面端各捆一份、npx 缓存里一份），
/// <see cref="Kernel.Identity"/> 用位置区分它们；版本相同 = 同一个发行版。
/// </summary>
public sealed record Kernel(
    /// <summary>package.json 的 version，例如 0.2.1-rc.2。</summary>
    string Version,
    /// <summary>稳定 id：真实包目录的绝对路径，asar 内为 <c>archive.asar!innerPath</c>，
    /// 只有启动脚本声明而无实体包时为 <c>declared:@deepseek-ai/dsh@ver</c>。</summary>
    string Identity,
    /// <summary>在哪找到的：desktop bundle / npm global / npx cache / pad version store / declared ...</summary>
    string Source,
    /// <summary><c>lib/bin.js</c>；asar 内路径也算（存在性交给 LaunchPolicy.Exists）。</summary>
    string? Bin,
    /// <summary>磁盘上的包目录；asar 内为 null。</summary>
    string? PackageDir,
    /// <summary>捆绑它的桌面端 exe；非桌面端捆的为 null。</summary>
    string? OwnerExe)
{
    public bool IsSameKernel(Kernel other) =>
        string.Equals(Identity, other.Identity, StringComparison.OrdinalIgnoreCase);

    /// <summary>给人看的一行：版本 + 来源。</summary>
    public string Label => string.IsNullOrWhiteSpace(Version) ? Source : Version + " · " + Source;
}

/// <summary>桌面端外壳：一个 Electron 应用，asar 里捆着一份 DSH 内核。</summary>
public sealed record Shell(
    string Exe,
    string? Version,
    string? UserData,
    string? Home,
    bool Running,
    /// <summary>谁找到它的（running process / shortcut / uninstall registry / ...）。</summary>
    string Found,
    string? KernelVersion,
    string? KernelIdentity,
    /// <summary>在跑的壳进程 pid（从进程现场拿得到才有）。</summary>
    int? Pid = null);

/// <summary>终端 TUI：某个 home 的 profile 里声明的 dsh 终端插件。</summary>
public sealed record Tui(
    string Package,
    string? Version,
    string? PackageDir,
    string? Home,
    string? Profile,
    /// <summary>拉起它的脚本（start.cmd / launch*.cmd），有则记下。</summary>
    string? Launcher,
    /// <summary>脚本里声明的内核 <c>@deepseek-ai/dsh@ver</c>。</summary>
    string? DeclaredKernel,
    bool Running,
    string Found,
    /// <summary>跑着这条 TUI 的进程 pid（环境 DSH_HOME + --profile 对上才有）。</summary>
    int? Pid = null);

/// <summary>命令行入口：一条能直接跑的 <c>dsh</c> CLI。</summary>
public sealed record DshCli(
    string Bin,
    string? Version,
    string Found,
    string? KernelIdentity,
    /// <summary>进程表里有一条命令行指向它 = 正在跑。</summary>
    bool Running = false,
    int? Pid = null);

/// <summary>一个 DSH_HOME：带 <c>profiles/</c> 或 <c>storages/</c> 的目录。</summary>
public sealed record DshHome(
    string Path,
    IReadOnlyList<string> Profiles,
    int Sessions,
    bool Running,
    string Found);

/// <summary>本机 DSH 全量清单。载体 = 桌面端 + TUI + CLI；内核单独成列。</summary>
public sealed record Inventory(
    IReadOnlyList<Kernel> Kernels,
    IReadOnlyList<Shell> Shells,
    IReadOnlyList<Tui> Tuis,
    IReadOnlyList<DshCli> Clis,
    IReadOnlyList<DshHome> Homes)
{
    /// <summary>载体数：2 个桌面端 + 1 个 TUI + 1 个 CLI 时是 4。</summary>
    public int CarrierCount => Shells.Count + Tuis.Count + Clis.Count;

    /// <summary>按内核 Identity 分组，同内核的载具排在一起。</summary>
    public IReadOnlyList<(Kernel Kernel, IReadOnlyList<string> Users)> ByKernel()
    {
        var users = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string? identity, string who)
        {
            if (string.IsNullOrWhiteSpace(identity)) return;
            if (!users.TryGetValue(identity, out var list)) users[identity] = list = [];
            if (!list.Contains(who, StringComparer.OrdinalIgnoreCase)) list.Add(who);
        }

        foreach (var s in Shells) Add(s.KernelIdentity, s.Exe);
        foreach (var t in Tuis) Add(t.DeclaredKernel, "TUI " + t.Package);
        foreach (var c in Clis) Add(c.KernelIdentity, "CLI " + c.Bin);
        return Kernels.Select(k => (k, (IReadOnlyList<string>)users.GetValueOrDefault(k.Identity, []))).ToList();
    }

    public static Inventory Empty { get; } = new([], [], [], [], []);
}
