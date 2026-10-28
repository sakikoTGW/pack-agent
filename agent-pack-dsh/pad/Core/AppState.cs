using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace Pad.Core;

/// <summary>One launchable row: an instance's profile, the counterpart of a PCL version.</summary>
public class ProfileVm(Instance inst, DshProfile profile) : Observable
{
    public Instance Instance { get; } = inst;
    public DshProfile Profile { get; } = profile;

    public string Name => Profile.Name;
    public string InstanceName => Instance.Name;
    public string TargetText => $"{InstanceName} · {Name}";
    public string Release => Instance.Dsh.Version;

    public virtual string Kind => Profile.HasTui ? "终端" : Profile.IsWeb ? "网页" : "自定义";

    /// <summary>Sort key so 终端 sits above 网页, like PCL cards.</summary>
    public virtual int GroupOrder => Profile.HasTui ? 0 : Profile.IsWeb ? 1 : 2;

    /// <summary>列表里只留最关键的一行（版本）；其余都进 <see cref="DetailText"/>（tooltip / 设置）。</summary>
    public virtual string Summary => Release;

    /// <summary>一行放不下的细节：tooltip 和设置卡片用，不摊在列表里。</summary>
    public virtual string DetailText =>
        string.Join(Environment.NewLine,
            $"{Kind} · {Release}",
            $"实例 {InstanceName}",
            string.IsNullOrWhiteSpace(Instance.Home) ? "" : $"home {Instance.Home}",
            Running ? "在跑" : "未跑");

    public virtual string Badge => Profile.HasTui ? "TUI" : Profile.IsWeb ? "WEB" : Name.Length > 0
        ? Name[..1].ToUpperInvariant() : "?";

    public bool Manageable => Profile.HasApiProxy;

    /// <summary>本机探测出来的行（不是 PAD 实例的 profile），见 <see cref="DetectedTuiRow"/>。</summary>
    public virtual bool IsDetected => false;

    /// <summary>探测到的 home 是否需要/可以收编成 PAD 实例。</summary>
    public virtual bool CanAdopt => false;

    public virtual string? DetectedHome => null;

    /// <summary>探测行自带图标（桌面端的 resources\icon.png 等）；null = 走 InstMark 原来的载体图标链。</summary>
    public virtual string? IconPath => null;

    /// <summary>桌面端自己的应用图标：&lt;exe 目录&gt;\resources\icon.png / tray.ico。</summary>
    public static string? ShellIconPath(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        try
        {
            var dir = Path.GetDirectoryName(exe!.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(dir)) return null;
            foreach (var name in new[] { "icon.png", "icon.ico", "tray.ico" })
            {
                var p = Path.Combine(dir!, "resources", name);
                if (File.Exists(p)) return p;
            }
        }
        catch { /* 读不到图标就退回默认链 */ }
        return null;
    }

    /// <summary>官方桌面端（DeepSeek Harness）还是第三方壳（DSH Desktop 等）。</summary>
    public static bool IsOfficialShell(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return false;
        try
        {
            var trimmed = exe!.Trim().Trim('"');
            var name = Path.GetFileNameWithoutExtension(trimmed);
            if (name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)) return true;
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(trimmed);
            return (info.ProductName ?? "").Contains("DeepSeek", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>home 缩写（末两级），列表里放得下。</summary>
    protected static string ShortHomeText(string? path, int max = 22)
    {
        if (string.IsNullOrWhiteSpace(path)) return "-";
        var full = path!.TrimEnd('\\', '/');
        var name = Path.GetFileName(full);
        var parent = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
        var text = string.IsNullOrWhiteSpace(parent) ? name : parent + "/" + name;
        return text.Length > max ? "…" + text[^max..] : text;
    }

    /// <summary>@scope/pkg → pkg。</summary>
    protected static string ShortPackageName(string? package)
    {
        if (string.IsNullOrWhiteSpace(package)) return "TUI";
        var name = package!.Trim();
        var slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    bool _running;
    public bool Running { get => _running; set => Set(ref _running, value); }
}

/// <summary>
/// 本机探测到的 TUI（不是 PAD 实例的 profile）：出现在版本选择页的「容器」列表里，
/// 点一下/点收编就把它所在的 home 收编成实例。渲染复用 ProfileVm + 合成 Instance/Profile。
/// </summary>
public sealed class DetectedTuiRow(Tui tui, bool canAdopt) : ProfileVm(Synthetic(tui), SyntheticProfile(tui))
{
    public override bool IsDetected => true;

    public override bool CanAdopt => canAdopt && !string.IsNullOrWhiteSpace(tui.Home);

    public override string? DetectedHome => tui.Home;

    public override string Kind => "TUI";

    /// <summary>版本放一行；包名/内核/home 都进 tooltip 与设置卡。</summary>
    public override string Summary => tui.Version ?? "";

    public override string DetailText =>
        string.Join(Environment.NewLine,
            $"{ShortPackageName(tui.Package)} {tui.Version}",
            $"内核 {tui.DeclaredKernel ?? "?"}",
            Running ? "在跑" : "未跑",
            string.IsNullOrWhiteSpace(tui.Home) ? "" : $"home {tui.Home}");

    public override string Badge => "TUI";

    static Instance Synthetic(Tui tui) => new()
    {
        Id = "detected-tui:" + (tui.Home ?? tui.Package),
        Name = (tui.Home ?? tui.Package) + " 的本机 TUI",
        Home = tui.Home ?? "",
        Dsh = new InstanceDsh { Version = tui.DeclaredKernel ?? tui.Version ?? "" },
        Adopted = true,
    };

    static DshProfile SyntheticProfile(Tui tui) => new()
    {
        Name = (tui.Profile ?? ShortPackageName(tui.Package)) + "（本机）",
        Bundles = [tui.Package],
    };
}

/// <summary>本机探测到的桌面端（官方/非官方 Electron 壳）：出现在「容器」列表里，可收编其 home。</summary>
public sealed class DetectedShellRow(Shell shell, bool canAdopt) : ProfileVm(Synthetic(shell), SyntheticProfile(shell))
{
    public override bool IsDetected => true;

    public override bool CanAdopt => canAdopt && !string.IsNullOrWhiteSpace(shell.Home);

    public override string? DetectedHome => shell.Home;

    /// <summary>官方桌面端 = 官方小黑鲸图标；第三方 = 壳自己的 resources\icon.png。</summary>
    public override string? IconPath => ShellIconPath(shell.Exe);

    public override string Kind => IsOfficialShell(shell.Exe) ? "官方" : "第三方";

    /// <summary>版本放一行；pid/home/userData 进 tooltip 和设置卡。</summary>
    public override string Summary => shell.KernelVersion ?? shell.Version ?? "";

    public override string DetailText
    {
        get
        {
            var lines = new List<string>
            {
                $"{(IsOfficialShell(shell.Exe) ? "官方" : "第三方")}桌面端 {shell.Version}",
                $"内核 {shell.KernelVersion ?? "?"}",
                Running ? $"在跑 pid {shell.Pid}" : "未跑",
            };
            if (!string.IsNullOrWhiteSpace(shell.UserData)) lines.Add($"userData {shell.UserData}");
            if (!string.IsNullOrWhiteSpace(shell.Home)) lines.Add($"home {shell.Home}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    public override string Badge => "DSH";

    static Instance Synthetic(Shell shell) => new()
    {
        Id = "detected-shell:" + shell.Exe,
        Name = Path.GetFileNameWithoutExtension(shell.Exe),
        Home = shell.Home ?? "",
        Dsh = new InstanceDsh { Version = shell.KernelVersion ?? shell.Version ?? "" },
        Adopted = true,
    };

    static DshProfile SyntheticProfile(Shell shell) => new()
    {
        Name = Path.GetFileNameWithoutExtension(shell.Exe),
        Bundles = [],
    };
}


/// <summary>A live run PAD started.</summary>
public sealed class RunVm(RunEntry entry, string instanceName) : Observable
{
    public RunEntry Entry { get; } = entry;
    public string InstanceName { get; } = instanceName;
    public string Profile => Entry.Profile;
    public int Pid => Entry.Pid;

    public string Uptime
    {
        get
        {
            if (!DateTimeOffset.TryParse(Entry.StartedAt, out var started)) return "";
            var d = DateTimeOffset.UtcNow - started;
            if (d.TotalHours >= 1) return $"{(int)d.TotalHours} 小时 {d.Minutes} 分";
            if (d.TotalMinutes >= 1) return $"{(int)d.TotalMinutes} 分";
            return $"{(int)d.TotalSeconds} 秒";
        }
    }

    int _agents = -2;
    /// <summary>Live agent count; -2 = not probed yet, -1 = side door unreachable.</summary>
    public int Agents { get => _agents; set { if (Set(ref _agents, value)) Raise(nameof(AgentText)); } }

    int _sessions;
    public int Sessions { get => _sessions; set { if (Set(ref _sessions, value)) Raise(nameof(AgentText)); } }

    bool _keyMissing;
    public bool KeyMissing { get => _keyMissing; set { if (Set(ref _keyMissing, value)) Raise(nameof(AgentText)); } }

    public string AgentText => KeyMissing
        ? "缺 DEEPSEEK_API_KEY"
        : Agents switch
    {
        -2 => "正在探测管理口",
        -1 => "管理口未连上",
        0 when Sessions > 0 => $"{Sessions} 条 session · 都空转",
        0 => "没有 session",
        _ => $"{Agents} 个 agent 在跑 · 共 {Sessions} 条 session",
    };

    public void Tick() { Raise(nameof(Uptime)); }
}

/// <summary>One <c>@deepseek-ai/dsh</c> release from the npm packument.</summary>
public sealed class NpmRelease : Observable
{
    public string Version { get; init; } = "";
    public string TimeText { get; init; } = "";
    public bool Latest { get; init; }
    public bool HasTime => TimeText.Length > 0;

    bool _installed;
    public bool Installed
    {
        get => _installed;
        set { if (Set(ref _installed, value)) Raise(nameof(CanInstall)); }
    }

    public bool CanInstall => !Installed;
}

/// <summary>
/// Shared application state. Views bind to this; nothing rebuilds markup by hand.
/// </summary>
public sealed class AppState : Observable
{
    public static AppState Current { get; } = new();

    public Launcher Launcher { get; private set; }
    public Runner Runner { get; private set; }
    public PadSettings Settings { get; private set; }

    public ObservableCollection<Instance> Instances { get; } = [];
    public ObservableCollection<ProfileVm> Launchables { get; } = [];
    public ObservableCollection<RunVm> Running { get; } = [];
    public ObservableCollection<DshRelease> Releases { get; } = [];
    public ObservableCollection<NpmRelease> RemoteReleases { get; } = [];
    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<PackRow> PackRows { get; } = [];

    /// <summary>本机能搜到的整合包（插件部分 + 对应内核）。PCL 的「可安装 Mod」位。</summary>
    public ObservableCollection<PackCard> PackCatalog { get; } = [];

    /// <summary>上一次整合包扫描的错误（空 = 正常）；UI 直接显示，不弹栈。</summary>
    public string PackCatalogError { get; private set; } = "";

    int _packCatalogGen;

    readonly DispatcherTimer _timer;

    AppState()
    {
        Launcher = Launcher.Default();
        Runner = new Runner(Launcher);
        Settings = Launcher.Settings();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) =>
        {
            // A page switch is animating: process/disk work on the UI thread now would
            // land inside the motion. Skip this beat; the next one is 2s away.
            if (!Views.Fx.Busy) RefreshRunning();
        };
        LoadRoots();
        var switched = PreferSharedRoot();
        _timer.Start();
        if (!switched) Reload();
    }

    Instance? _selectedInstance;
    public Instance? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (!Set(ref _selectedInstance, value)) return;
            PackRows.Clear();
            RaisePackMatch();
            RebuildLaunchables();
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionTitle));
            Raise(nameof(SelectionDetail));
            RaiseStep();
        }
    }

    ProfileVm? _selectedProfile;
    public ProfileVm? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;
            Raise(nameof(CanLaunch));
            Raise(nameof(SelectionTitle));
            Raise(nameof(SelectionDetail));
            RaiseStep();
        }
    }

    public bool HasSelection => SelectedInstance is not null;
    public bool CanLaunch => SelectedInstance is not null && SelectedProfile is not null;
    public bool NeedsSetup => Instances.Count == 0;
    public bool HasInstanceNoProfile => SelectedInstance is not null && SelectedProfile is null;

    public bool CanOfferAdopt
    {
        get
        {
            var home = LaunchPolicy.DefaultDshHome(Settings.Launch.DshHome);
            return LaunchPolicy.OfferAdopt(
                DshSniff.LooksLikeHome(home),
                Launcher.HomeAlreadyAdopted(home),
                Settings.Other.AdoptDismissed);
        }
    }

    /// <summary>Which page the step button and a click on a disabled launch should open.</summary>
    public string StepTarget =>
        Releases.Count == 0 && DshSniff.Bin() is null ? "download" : "versions";

    void RaiseStep()
    {
        Raise(nameof(NeedsSetup));
        Raise(nameof(HasInstanceNoProfile));
        Raise(nameof(CanLaunch));
        Raise(nameof(StepTarget));
        Raise(nameof(CanOfferAdopt));
    }

    /// <summary>The line under the launch button — PCL puts the version name there.</summary>
    public string SelectionTitle => SelectedProfile is null
        ? (SelectedInstance?.Name ?? "还没有实例")
        : $"{SelectedProfile.InstanceName} · {SelectedProfile.Name}";

    public string SelectionDetail => SelectedInstance is null
        ? "到版本选择里新建一个"
        : SelectedProfile is null
            ? $"{SelectedInstance.Dsh.Version} · 未找到可用的版本"
            : SelectedProfile.Summary;

    public void ReloadReleases()
    {
        Releases.Clear();
        foreach (var r in Launcher.Releases()) Releases.Add(r);
        MarkRemoteInstalled();
        Raise(nameof(HasReleases));
        Raise(nameof(NoReleases));
        Raise(nameof(HasRemoteReleases));
        Raise(nameof(ShowReleaseEmpty));
        RaiseStep();
    }

    public bool HasReleases => Releases.Count > 0;
    public bool NoReleases => Releases.Count == 0;
    public bool HasRemoteReleases => RemoteReleases.Count > 0;
    public bool ShowReleaseEmpty => Releases.Count == 0 && RemoteReleases.Count == 0;

    public void SetRemoteReleases(IEnumerable<NpmRelease> rows)
    {
        RemoteReleases.Clear();
        foreach (var r in rows) RemoteReleases.Add(r);
        MarkRemoteInstalled();
        Raise(nameof(HasRemoteReleases));
        Raise(nameof(ShowReleaseEmpty));
    }

    void MarkRemoteInstalled()
    {
        var have = Releases.Select(r => r.Version).ToHashSet(StringComparer.Ordinal);
        foreach (var r in RemoteReleases) r.Installed = have.Contains(r.Version);
    }

    public void Reload()
    {
        ReloadReleases();
        var wanted = SelectedInstance?.Id ?? Settings.LastInstance;
        Instances.Clear();
        foreach (var i in Launcher.Instances()) Instances.Add(i);

        var pick = Instances.FirstOrDefault(i => i.Id == wanted) ?? Instances.FirstOrDefault();
        _selectedInstance = pick;
        Raise(nameof(SelectedInstance));
        Raise(nameof(HasSelection));
        PackRows.Clear();
        RaisePackMatch();
        RebuildLaunchables();
        RefreshRunning();
        RaiseStep();
    }

    /// <summary>
    /// 整合包目录（插件部分 + 内核）：走 ts 侧同一套发现/投影代码（Packs.Catalog），
    /// worker 上跑；这层只持有结果，不判断"哪个包能建实例"。
    /// </summary>
    public async Task ReloadPackCatalog()
    {
        var gen = ++_packCatalogGen;
        List<PackCard> rows;
        try
        {
            // exe 旁是 PCL 式 sidecar zip 的位置；launcher root / library / 盘根命名
            // 由 ts 侧自己的默认根负责，不在这里重复扫。
            var roots = AppContext.BaseDirectory;
            rows = await Task.Run(() => new Packs(Launcher).Catalog(roots, null, null, CancellationToken.None));
        }
        catch (Exception ex)
        {
            if (gen != _packCatalogGen) return;
            PackCatalogError = PadError.Describe(ex);
            PackCatalog.Clear();
            Raise(nameof(PackCatalogError));
            return;
        }
        if (gen != _packCatalogGen) return;
        // 只有带 dsh.version（能钉内核）的才是能建 DSH 实例的整合包。exports 里的
        // Cursor/Claude agent 配置包没有 dsh 层，建不了实例；把它们列成「整合包」
        // 只会让人误会（用户 2026-10-28："一个组件一个整合包"）。
        // 同一个包常同时躺在 library 和桌面/下载里；列表按「名字+版本」只留一份，
        // 优先留 PAD 自己库里的那份（用户看到的是"一个包"，不是同一个包两行）。
        rows = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Kernel)
                        || (r.Kind ?? "").Contains("pinst", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => ((r.Title ?? "").Trim().ToLowerInvariant(), (r.Version ?? "").Trim()))
            .Select(g => g
                .OrderByDescending(r => (r.Source ?? "").Contains("library", StringComparison.OrdinalIgnoreCase))
                .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToList();
        PackCatalog.Clear();
        foreach (var row in rows) PackCatalog.Add(row);
        PackCatalogError = "";
        Raise(nameof(PackCatalogError));
    }

    void RebuildLaunchables()
    {        Launchables.Clear();
        var inst = SelectedInstance;
        if (inst is null)
        {
            SelectedProfile = null;
            Raise(nameof(SelectionTitle));
            Raise(nameof(SelectionDetail));
            return;
        }
        foreach (var p in Launcher.Profiles(inst)) Launchables.Add(new ProfileVm(inst, p));

        var wanted = inst.LastProfile ?? Settings.LastProfile;
        SelectedProfile = Launchables.FirstOrDefault(p => p.Name == wanted) ?? Launchables.FirstOrDefault();
        MarkRunning();
        Raise(nameof(SelectionTitle));
        Raise(nameof(SelectionDetail));
    }

    public void RefreshRunning()
    {
        var live = Launcher.Runtime().Entries;

        for (var i = Running.Count - 1; i >= 0; i--)
        {
            var still = live.Any(e => e.Instance == Running[i].Entry.Instance && e.Profile == Running[i].Profile);
            if (!still) Running.RemoveAt(i);
        }
        foreach (var e in live)
        {
            var known = Running.FirstOrDefault(r => r.Entry.Instance == e.Instance && r.Profile == e.Profile);
            if (known is null)
            {
                var name = Instances.FirstOrDefault(i => i.Id == e.Instance)?.Name ?? e.Instance;
                Running.Add(new RunVm(e, name));
            }
        }
        foreach (var r in Running) r.Tick();
        MarkRunning();
        Raise(nameof(AnyRunning));
        Raise(nameof(NoneRunning));
        _ = ProbeAgents();
    }

    bool _probing;

    /// <summary>
    /// Ask each live Harness how many of its sessions are actually running. Fire and
    /// forget: the launch page must never block on a socket, and a stale count is
    /// better than a frozen window.
    /// </summary>
    async Task ProbeAgents()
    {
        if (_probing) return;
        _probing = true;
        try
        {
            foreach (var run in Running.ToList())
            {
                var inst = Instances.FirstOrDefault(i => i.Id == run.Entry.Instance);
                if (inst is null) continue;
                var ad = Gateway.ReadAd(inst.Home);
                if (ad is null || !Proc.Alive(ad.Pid)) { run.Agents = -1; continue; }
                try
                {
                    var sessions = await Gateway.Sessions(ad, CancellationToken.None);
                    run.Sessions = sessions.Count;
                    run.Agents = sessions.Count(s => s.Running);
                    var key = await Gateway.DeepseekConfigured(ad, CancellationToken.None);
                    if (key is bool configured) run.KeyMissing = !configured;
                }
                catch
                {
                    run.Agents = -1;
                }
            }
        }
        finally
        {
            _probing = false;
        }
    }

    void MarkRunning()
    {
        foreach (var p in Launchables)
            p.Running = Running.Any(r => r.Entry.Instance == p.Instance.Id && r.Profile == p.Name);
    }

    public bool AnyRunning => Running.Count > 0;
    public bool NoneRunning => Running.Count == 0;

    public int EnabledPackCount => PackRows.Count(p => p.Enabled);

    public string PackSummary
    {
        get
        {
            var on = PackRows.Where(p => p.Enabled).Select(p => p.Title).ToList();
            if (on.Count == 0) return "无整合包";
            if (on.Count <= 2) return string.Join(" · ", on);
            return $"{on.Count} 个整合包";
        }
    }

    public async Task ReloadPacks()
    {
        PackRows.Clear();
        RaisePackMatch();
        var inst = SelectedInstance;
        if (inst is null) return;
        var packs = new Packs(Launcher);
        if (packs.Unavailable() is not null)
        {
            RaisePackMatch();
            return;
        }
        try
        {
            foreach (var row in await packs.List(inst, CancellationToken.None))
                PackRows.Add(row);
        }
        catch
        {
            PackRows.Clear();
        }
        RaisePackMatch();
    }

    void RaisePackMatch()
    {
        Raise(nameof(PackSummary));
        Raise(nameof(EnabledPackCount));
    }

    public void SaveSelection()
    {
        Settings.LastInstance = SelectedInstance?.Id;
        Settings.LastProfile = SelectedProfile?.Name;
        Launcher.SaveSettings(Settings);
    }

    /// <summary>Persist the current settings (normalize + write) without touching selection.</summary>
    public void SaveSettings() => ApplySettings(Settings);

    public void ApplySettings(PadSettings s)
    {
        s.Normalize();
        Settings = s;
        Launcher.SaveSettings(s);
        Views.Fx.SetAnimate(s.Ui.Animate);
        Views.Fx.SetSpeed(s.Ui.AnimSpeed / 100.0);
        Theme.Apply(s.Ui);
        Raise(nameof(Settings));
        SyncRefreshTimer();
    }

    /// <summary>Apply UI prefs that must land before the first frame paints.</summary>
    public void ApplyUiBoot()
    {
        Settings.Normalize();
        Views.Fx.SetAnimate(Settings.Ui.Animate);
        Views.Fx.SetSpeed(Settings.Ui.AnimSpeed / 100.0);
        Theme.PrepareMutable();
        Theme.Apply(Settings.Ui);
        SyncRefreshTimer();
    }

    void SyncRefreshTimer()
    {
        _timer.Interval = TimeSpan.FromSeconds(Settings.Other.RefreshSec);
    }

    // ---- multiple launcher roots -----------------------------------------

    string? _lastRoot;

    static string RootsFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "pack-agent-dsh", "roots.json");

    void LoadRoots()
    {
        Roots.Clear();
        _lastRoot = null;
        try
        {
            if (System.IO.File.Exists(RootsFile))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(RootsFile));
                if (doc.RootElement.TryGetProperty("last", out var lastEl))
                {
                    var last = lastEl.GetString();
                    if (!string.IsNullOrWhiteSpace(last))
                        _lastRoot = System.IO.Path.GetFullPath(last);
                }
                if (doc.RootElement.TryGetProperty("roots", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var p = el.GetString();
                        if (string.IsNullOrWhiteSpace(p)) continue;
                        var abs = System.IO.Path.GetFullPath(p);
                        if (!Roots.Contains(abs)) Roots.Add(abs);
                    }
                }
            }
        }
        catch { /* a damaged roots file must not brick startup */ }

        var current = System.IO.Path.GetFullPath(Launcher.Root);
        if (DshSniff.CountInstances(current) > 0 && !Roots.Contains(current))
            Roots.Add(current);
    }

    /// <summary>
    /// Empty exe-side <c>.pack-launcher</c> uses the machine roster / sniffed
    /// roots so two PAD folders share instances. Skips when
    /// <c>PACK_LAUNCHER_ROOT</c> is set.
    /// </summary>
    bool PreferSharedRoot()
    {
        var envPinned = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("PACK_LAUNCHER_ROOT"));
        var local = Launcher.Root;
        var localN = DshSniff.CountInstances(local);
        var lastN = string.IsNullOrWhiteSpace(_lastRoot) ? 0 : DshSniff.CountInstances(_lastRoot);
        var sniffed = new List<(string Path, int Instances)>();
        if (!string.IsNullOrWhiteSpace(_lastRoot))
            sniffed.Add((_lastRoot, lastN));
        foreach (var p in Roots)
            sniffed.Add((p, DshSniff.CountInstances(p)));
        foreach (var p in DshSniff.LauncherRootCandidates())
        {
            var n = DshSniff.CountInstances(p);
            sniffed.Add((p, n));
            if (n > 0 && !Roots.Contains(p)) Roots.Add(p);
        }
        var pick = LaunchPolicy.PickBootRoot(envPinned, local, localN, _lastRoot, lastN, sniffed);
        if (pick is not null && !LaunchPolicy.SamePath(pick, local))
        {
            SwitchRoot(pick);
            return true;
        }
        RememberCurrentIfUseful();
        return false;
    }

    void RememberCurrentIfUseful()
    {
        var abs = System.IO.Path.GetFullPath(Launcher.Root);
        if (DshSniff.CountInstances(abs) > 0)
        {
            if (!Roots.Contains(abs)) Roots.Add(abs);
            _lastRoot = abs;
        }
        PersistRoots();
        Raise(nameof(Roots));
    }

    void PersistRoots()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RootsFile)!);
            var json = System.Text.Json.JsonSerializer.Serialize(
                new { schema = "pack-agent.pad.roots/v1", last = _lastRoot, roots = Roots.ToList() },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(RootsFile, json, new System.Text.UTF8Encoding(false));
        }
        catch { /* best effort */ }
    }

    /// <summary>Add a root to the roster without switching to it.</summary>
    public void AddRoot(string root)
    {
        var abs = System.IO.Path.GetFullPath(root);
        if (!System.IO.Directory.Exists(abs)) return;
        if (!Roots.Contains(abs)) Roots.Add(abs);
        PersistRoots();
        Raise(nameof(Roots));
    }

    /// <summary>Create a fresh root (empty versions/ + instances/) and switch to it.</summary>
    public void NewRoot(string root)
    {
        var abs = System.IO.Path.GetFullPath(root);
        Directory.CreateDirectory(abs);
        new Launcher(abs); // its constructor creates versions/ + instances/
        SwitchRoot(abs);
    }

    /// <summary>Point the whole app at another launcher root and reload all state.</summary>
    public void SwitchRoot(string root)
    {
        var abs = System.IO.Path.GetFullPath(root);
        if (string.Equals(abs, Launcher.Root, System.StringComparison.OrdinalIgnoreCase)) return;

        Launcher = new Launcher(abs);
        Runner = new Runner(Launcher);
        Settings = Launcher.Settings();
        if (!Roots.Contains(abs)) Roots.Add(abs);
        if (DshSniff.CountInstances(abs) > 0) _lastRoot = abs;
        PersistRoots();

        Raise(nameof(Settings));
        Raise(nameof(Roots));
        ApplyUiBoot();
        Reload();
    }
}
