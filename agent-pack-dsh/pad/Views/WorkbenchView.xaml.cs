using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Pad.Core;
using IOPath = System.IO.Path;

namespace Pad.Views;

/// <summary>
/// 管理首页。左边是可收起的项目侧栏，右边是可由用户自排的卡片区（D28）。
/// 卡片 = 数据源 × 表现形；偏好存在 pad.json#ui.cards，没有隐藏状态。
/// </summary>
public partial class WorkbenchView : UserControl
{
    readonly AppState _state = AppState.Current;
    readonly MainWindow _host;

    Inventory? _inventory;
    int[] _activity = new int[7];
    (string Tool, int Calls, long Tokens)[] _tools = [];
    bool _editing;
    CardPref? _open;

    /// <summary>侧栏当前选中的 id；空 = 固定的「主页」行（D54）。</summary>
    string _selected = "";

    /// <summary>右栏当前看的是哪个项目；null = 管理主页（本机概览）。</summary>
    Instance? _scope;
    RunVm? _scopeRun;
    int[] _scopeActivity = new int[7];
    (string Tool, int Calls, long Tokens)[] _scopeTools = [];
    Inspect.Report? _scopeInspect;
    List<SessionRow> _scopeSessions = [];
    List<BundleRow> _scopeBundles = [];
    List<DshProfile> _scopeProfiles = [];
    List<PackRow> _scopePacks = [];
    string _scopePacksNote = "";
    bool _scopePacksLoading;

    // 对话（D55）：盘上的 session 摘要 +（有管理口时）DSH session.list 的活状态。
    List<DialogRow> _scopeDialogs = [];
    bool _scopeDialogsLoading;
    Dictionary<string, LiveSession> _scopeLive = new(StringComparer.OrdinalIgnoreCase);
    string _scopeGatewayNote = "";
    bool _scopeGatewayAsked;

    /// <summary>运行现场（D57）：进程 / pid / 端口 / 配置 / 已跑 / 内存，读进程现场与 TCP 表。</summary>
    RunFacts? _scopeFacts;

    /// <summary>运行卡的 logo / 统计只做一次进场动效，别被 2s 的状态刷新反复触发（D58）。</summary>
    bool _heroAnimated;

    static Brush Muted => (Brush)Application.Current.Resources["TextMuted"];
    static Brush Primary => (Brush)Application.Current.Resources["TextPrimary"];
    static Brush AccentBrush => (Brush)Application.Current.Resources["Accent"];
    static Brush BodyBrush => (Brush)Application.Current.Resources["TextBody"];

    /// <summary>按 1024 单位 iconfont 资源造一个 Ico（D58）。颜色跟随 Foreground。</summary>
    /// <summary>线性图标（D59）：Lucide 24 网格描边，颜色跟随 Foreground。</summary>
    static LineIco Icon(string key, double size, Brush foreground) => new()
    {
        Glyph = Application.Current.Resources[key] as Geometry,
        Width = size,
        Height = size,
        Foreground = foreground,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>舞台侧栏（MainWindow 持有）：本页只负责喂数据，不拥有它。</summary>
    ProjectRail? _rail;
    IReadOnlyList<Instance>? _lastInsts;

    /// <summary>主页的项目行（D60）：实例 + 状态 + 运行现场 + 对话摘要，一次装在内存里喂两张卡。</summary>
    List<HomeProject> _homeProjects = [];
    int _homeLive;

    /// <summary>
    /// 项目轴（D62/D63）：右栏还可以看一个「绑定项目」——工作区集合 + agent preset，
    /// 实例只是它的运行体。项目页与实例页共用页头和卡片语言，但项目页先回答
    /// 「启动/停止、绑了哪些 preset、哪些目录」。
    /// </summary>
    Project? _project;
    List<Project> _projects = [];
    PresetProbe? _projectProbe;
    bool _projectProbeLoading;
    bool _projectStarting;

    sealed record HomeProject(Instance Inst, ProjectRun Status, RunFacts? Facts, int Dialogs,
        DateTimeOffset? LastDialog, IReadOnlyList<DialogRow> Recent);

    /// <summary>主页这几张卡统计的是哪份 home（D61）：在跑的那份 home 属于哪个项目。</summary>
    HomeProject? HomeOwner()
    {
        var home = DefaultHome();
        return _homeProjects.FirstOrDefault(p => LaunchPolicy.SamePath(p.Inst.Home, home))
               ?? _homeProjects.FirstOrDefault(p => p.Status.Live);
    }

    static readonly (string Type, string Label, string View, int Cols)[] Catalog =
    [
        ("running", "在跑", "list", 2),
        ("context", "上下文", "bar", 1),
        ("projects", "项目", "rows", 2),
        ("note", "便签", "", 1),
        ("activity", "近 7 天", "bars", 1),
        ("tools", "工具调用", "bars", 2),
        ("inventory", "本机 DSH", "count", 1),
        ("project", "项目卡", "dots", 1),
    ];

    public WorkbenchView(MainWindow host)
    {
        InitializeComponent();
        _host = host;
        // 项目状态是活的：灯和卡片上的字随 AppState 的快照走，不在加载时定格（D53）。
        _state.StatusChanged += OnStatus;
        if (App.ShotPage == "manage-edit")
        {
            _editing = true;
            EditBtn.Content = "完成";
        }
        if (App.ShotPage == "manage-open")
        {
            // Prove the expanded face without a pointer: open the inventory card for the shot.
            Loaded += (_, _) =>
            {
                // Prefs() also materialises the defaults, so ask it before picking one.
                _open = Prefs().FirstOrDefault(c => c.Type == "inventory");
                if (_open is not null) Load();
            };
        }
        // Loaded fires inside the layout pass, and Load enumerates the process table, home
        // directories and session files. Running that here froze the first frame of every
        // switch into 管理 — the one hitch that survived the motion work. The scan now runs
        // off the UI thread and paints when it has data.
        Loaded += (_, _) => Load();
        BuildAddMenu();
        SetupHeaderIcons();
    }

    // ------------------------------------------------------------------ 图标 / 交互（D59）

    /// <summary>页头按钮统一成线性图标（D59）：拿掉 iconfont 附加图标，换成 LineIco + 文本。</summary>
    void SetupHeaderIcons()
    {
        StyleHeaderButton(BackHomeBtn, "主页", "L.Home");
        StyleHeaderButton(AddBtn, "+ 卡片", "L.Plus");
        StyleHeaderButton(EditBtn, "编辑", "L.Pencil");
        StyleHeaderButton(VersionsBtn, "版本设置", "L.Sliders");
        StyleHeaderButton(BundlesBtn, "实例与组合包", "L.Package");
        StyleHeaderButton(RelationsBtn, "关系", "L.Share");
        SetRunButton(canStop: false);
    }

    static StackPanel BtnContent(string label, string iconKey)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        // 不设 Foreground：让 LineIco / 文本跟随 Button 的 Foreground，hover/press 才有反馈。
        row.Children.Add(new LineIco
        {
            Glyph = Application.Current.Resources[iconKey] as Geometry,
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    static void StyleHeaderButton(Button btn, string label, string iconKey)
    {
        btn.ClearValue(Ico.OfProperty); // 不再用填充图标的附加属性
        btn.Content = BtnContent(label, iconKey);
    }

    /// <summary>「去启动 / 停止」的图标随状态换（D59）。</summary>
    void SetRunButton(bool canStop) =>
        RunBtn.Content = BtnContent(canStop ? "停止" : "去启动", canStop ? "L.Stop" : "L.Play");

    /// <summary>
    /// 卡片悬停：抬 2px + 阴影加深 + 描边浮现（D59）。
    /// 每张卡自己持有一份非冻结的 Effect/Brush，动画才不会作用在共享资源上。
    /// </summary>
    static void HoverLift(Border card)
    {
        var lift = new TranslateTransform();
        card.RenderTransform = lift;
        var shadow = new DropShadowEffect
        {
            BlurRadius = 26,
            ShadowDepth = 6,
            Direction = 270,
            Opacity = 0.10,
            Color = Color.FromRgb(0x20, 0x30, 0x3F),
        };
        card.Effect = shadow;
        var line = (SolidColorBrush)Application.Current.Resources["LineBorder"];
        var border = new SolidColorBrush(line.Color) { Opacity = 0 };
        card.BorderThickness = new Thickness(1);
        card.BorderBrush = border;

        card.MouseEnter += (_, _) =>
        {
            if (!Fx.Animated)
            {
                lift.Y = -2;
                shadow.Opacity = 0.22;
                border.Opacity = 1;
                return;
            }
            lift.BeginAnimation(TranslateTransform.YProperty, Fx.Move(lift.Y, -2, 150));
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, Fx.Fade(0.10, 0.22, 160));
            border.BeginAnimation(SolidColorBrush.OpacityProperty, Fx.Fade(0, 1, 160));
        };
        card.MouseLeave += (_, _) =>
        {
            if (!Fx.Animated)
            {
                lift.Y = 0;
                shadow.Opacity = 0.10;
                border.Opacity = 0;
                return;
            }
            lift.BeginAnimation(TranslateTransform.YProperty, Fx.Move(lift.Y, 0, 190));
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, Fx.Fade(0.22, 0.10, 190));
            border.BeginAnimation(SolidColorBrush.OpacityProperty, Fx.Fade(1, 0, 190));
        };
    }

    void OpenBundles_Click(object sender, RoutedEventArgs e) =>
        _host.ShowInner("bundles", "实例与组合包", "manage");

    void OpenRelations_Click(object sender, RoutedEventArgs e) =>
        _host.ShowRelations();

    // ------------------------------------------------------------------ 加载

    /// <summary>
    /// Prewarm. Run the scan before the page is ever shown so the first switch has cards to
    /// stagger in instead of an empty grid (the window unfold no longer waits for data).
    /// </summary>
    public void Prepare() => Load();

    void Load() => _ = LoadAsync();

    int _loadGen;

    /// <summary>
    /// Nothing in the first half touches WPF: process enumeration, the known-location walk,
    /// session mtimes and the token-usage cache all read the disk/machine on a worker. The
    /// page is already on screen when this starts, so the switch has no stall to hide.
    /// </summary>
    async Task LoadAsync()
    {
        var gen = ++_loadGen;
        try
        {
            var launcher = _state.Launcher;
            var insts = await Task.Run(() => launcher.Instances());
            if (gen != _loadGen) return;

            // 侧栏选中什么，右栏就是什么：'' = 主页，其它 = 项目 id。
            if (_scope is not null)
            {
                _scope = insts.FirstOrDefault(i => i.Id == _scope!.Id);
                if (_scope is null) _selected = "";
            }

            if (_project is not null)
            {
                if (_scope is not null) await LoadScopeAsync(_scope, gen);
                if (gen != _loadGen) return;
                await LoadProjectAsync(_project, gen);
            }
            else if (_scope is null) await LoadHomeAsync(insts, gen);
            else await LoadScopeAsync(_scope, gen);
            if (gen != _loadGen) return;

            PaintHeader();
            PaintProjects(insts);
            PaintCards(insts);
            Footnote.Text = "";
        }
        catch (Exception ex)
        {
            Footnote.Text = PadError.Describe(ex);
        }
    }

    /// <summary>主页：本机级聚合（原来的工作台数据源）。</summary>
    async Task LoadHomeAsync(IReadOnlyList<Instance> insts, int gen)
    {
        var launcher = _state.Launcher;
        var inventory = await Task.Run(() => Discover.Run(launcher, [], CancellationToken.None));
        if (gen != _loadGen) return;
        _inventory = inventory;

        // 进程现场扫到的"在跑 home"灌给状态灯：桌面端 / 双击脚本起的实例也在这拍亮起来（D53）。
        RunWatch.NoteHomes(inventory.Homes.Where(h => h.Running).Select(h => h.Path));
        _state.RefreshStatus();

        // 主页的两张主卡（在跑 / 项目）要的同一份数据：状态 + 运行现场 + 对话摘要（D60）。
        var rows = await Task.Run(() =>
        {
            var liveRuns = _state.Running.ToList();
            var list = new List<HomeProject>();
            foreach (var inst in insts)
            {
                var status = _state.StatusOf(inst.Id);
                RunFacts? facts = null;
                if (status.Live)
                {
                    try
                    {
                        var padRun = liveRuns.FirstOrDefault(r => r.Entry.Instance == inst.Id);
                        IReadOnlyList<Nodes.Running> ledger = [];
                        try { ledger = Nodes.Ledger.Load(inst.Home); } catch { }
                        facts = RunFacts.Collect(inst, status, padRun, ledger);
                    }
                    catch { /* 进程现场读不到：这一行只给状态词 */ }
                }
                var (count, last) = Dialogs.Summary(inst.Home);
                // 在跑的项目再读几条对话：主页第一屏要直接回答"在跑啥"（D61）。
                IReadOnlyList<DialogRow> recent = [];
                if (status.Live)
                {
                    try { recent = Dialogs.Read(inst.Home, 4); } catch { /* 读不到就只给状态 */ }
                }
                list.Add(new HomeProject(inst, status, facts, count, last, recent));
            }
            return list;
        });
        if (gen != _loadGen) return;
        _homeProjects = rows;
        _homeLive = rows.Count(r => r.Status.Live);
        _projects = new Projects(launcher).List();

        var liveHome = inventory.Homes.FirstOrDefault(h => h.Running)?.Path
                       ?? inventory.Shells.FirstOrDefault(sh => !string.IsNullOrWhiteSpace(sh.Home))?.Home
                       ?? DesktopHome();
        var scan = await Task.Run(() => (Activity: ScanActivity(liveHome), Tools: ScanTools(liveHome)));
        if (gen != _loadGen) return;
        _activity = scan.Activity;
        _tools = scan.Tools;
    }

    /// <summary>
    /// 项目页：同一套卡片语言，但每一项都只统计这份实例的 home / workspace（D54）。
    /// 机器级聚合（本机 DSH / 载体 / 所有 home）在这里没有意义，不搬过来。
    /// </summary>
    async Task LoadScopeAsync(Instance inst, int gen)
    {
        var launcher = _state.Launcher;
        var home = inst.Home;
        _scopeDialogsLoading = true;
        var scan = await Task.Run(() =>
        {
            Inspect.Report? inspect = null;
            try { inspect = Inspect.Measure(home); } catch { /* home 不在了：卡片给空态 */ }
            List<SessionRow> sessions = [];
            try { sessions = launcher.Sessions(inst); } catch { }
            List<BundleRow> bundles = [];
            try { bundles = launcher.ListBundles(inst); } catch { }
            List<DshProfile> profiles = [];
            try { profiles = launcher.Profiles(inst); } catch { }
            // 对话：读这个 home 的 session 投影缓存（只读，D55）。
            IReadOnlyList<DialogRow> dialogs = [];
            try { dialogs = Dialogs.Read(home, 16); } catch { }
            return (inspect,
                activity: ScanActivity(home),
                tools: ScanTools(home),
                sessions,
                bundles,
                profiles,
                dialogs);
        });
        if (gen != _loadGen) return;

        _scopeInspect = scan.inspect;
        _scopeActivity = scan.activity;
        _scopeTools = scan.tools;
        _scopeSessions = scan.sessions;
        _scopeBundles = scan.bundles;
        _scopeProfiles = scan.profiles;
        _scopeDialogs = scan.dialogs.ToList();
        _scopeDialogsLoading = false;
        _scopeRun = _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
        _scopeLive = new Dictionary<string, LiveSession>(StringComparer.OrdinalIgnoreCase);
        _scopeGatewayNote = "";
        _scopeGatewayAsked = false;
        _scopeFacts = null;

        // 整合包走 TS CLI（bun + 仓库）：可能慢、可能不可用，单独加载，不挡其它卡。
        _scopePacks = [];
        _scopePacksNote = "";
        _scopePacksLoading = true;
        _ = LoadScopePacksAsync(inst, gen);
        // 管理口的活状态：不挡首帧。
        _ = LoadScopeLiveAsync(inst, gen);
        // 运行现场（pid/端口/已跑/内存）：走进程现场 + TCP 表，慢一拍但最有用（D57）。
        _ = LoadScopeFactsAsync(inst, gen);
    }

    /// <summary>
    /// 运行现场：识别这个 home 的进程（DSH_HOME 归属）、系统 TCP 表里的监听端口、
    /// 启动时间与内存；PAD 自己起的再叠账本里的带 token URL（D57）。
    /// </summary>
    async Task LoadScopeFactsAsync(Instance inst, int gen)
    {
        var phase = _state.StatusOf(inst.Id);
        var padRun = _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
        IReadOnlyList<Nodes.Running> ledger;
        try { ledger = Nodes.Ledger.Load(inst.Home); }
        catch { ledger = []; }

        RunFacts facts;
        try { facts = await Task.Run(() => RunFacts.Collect(inst, phase, padRun, ledger)); }
        catch { return; }

        if (gen != _loadGen) return;
        if (_scope is null || _scope.Id != inst.Id) return;
        _scopeFacts = facts;
        PaintHeader();
        PaintCards(_lastInsts ?? []);
    }

    async Task LoadScopePacksAsync(Instance inst, int gen)
    {
        var packs = new Packs(_state.Launcher);
        List<PackRow> rows;
        string note;
        try
        {
            var missing = packs.Unavailable();
            if (missing is not null)
            {
                rows = [];
                note = "整合包读不出来（" + missing.Reason + "）";
            }
            else
            {
                rows = await packs.List(inst, CancellationToken.None);
                note = "";
            }
        }
        catch (Exception ex)
        {
            rows = [];
            note = "整合包读不出来：" + PadError.Describe(ex);
        }

        if (gen != _loadGen) return;
        if (_scope is null || _scope.Id != inst.Id) return;
        _scopePacksLoading = false;
        _scopePacks = rows;
        _scopePacksNote = note;
        PaintCards(_lastInsts ?? []);
    }

    /// <summary>
    /// 管理口：有 pad-gateway 层时拿 DSH 的 session.list（真正的"在跑"）；
    /// 没有就只留一句为什么——不替它判读（D18）。
    /// </summary>
    async Task LoadScopeLiveAsync(Instance inst, int gen)
    {
        var live = new Dictionary<string, LiveSession>(StringComparer.OrdinalIgnoreCase);
        var note = "";
        try
        {
            var phase = _state.StatusOf(inst.Id);
            if (!phase.Live)
            {
                note = "实例没在跑：管理口连不上，只能按落盘时间看动静";
            }
            else
            {
                var probe = await Gateway.Probe(inst, anyRunning: true, CancellationToken.None);
                if (probe.State == GatewayState.Ready && probe.Ad is not null)
                {
                    var tree = await Gateway.Tree(probe.Ad, CancellationToken.None);
                    foreach (var node in tree)
                        foreach (var session in node.Sessions)
                            if (session.SessionId.Length > 0) live[session.SessionId] = session;
                }
                else
                {
                    note = "管理口没连上：" + probe.Message;
                }
            }
        }
        catch (Exception ex)
        {
            note = "管理口没连上：" + PadError.Describe(ex);
        }

        if (gen != _loadGen) return;
        if (_scope is null || _scope.Id != inst.Id) return;
        _scopeLive = live;
        _scopeGatewayNote = note;
        _scopeGatewayAsked = true;
        PaintHeader();
        PaintCards(_lastInsts ?? []);
    }

    /// <summary>回主页：右栏换成机器级仪表，侧栏主页行点亮。</summary>
    void ShowHome()
    {
        _scope = null;
        _scopeRun = null;
        _project = null;
        _projectProbe = null;
        _selected = "";
        _open = null;
        PaintHeader();
        PaintTransition(); // 缓存还在就立刻画回主页，不等扫描
        if (_lastInsts is not null) FillRail(_lastInsts);
        else PaintProjects(_state.Launcher.Instances());
        CardScroll?.ScrollToTop();
        Load();
    }

    /// <summary>看某个项目：右栏换成这份项目自己的页，同时把它设成全局选中实例。</summary>
    void ShowScope(Instance inst)
    {
        _scope = inst;
        _project = null;
        _projectProbe = null;
        _selected = inst.Id;
        _open = null;
        _heroAnimated = false;
        _state.SelectedInstance = inst; // 唯一真源：实例与组合包 / 版本设置 跟着走
        PaintHeader();
        PaintTransition(); // 别把上一个 scope 的卡留在标题下面
        if (_lastInsts is not null) FillRail(_lastInsts);
        CardScroll?.ScrollToTop();
        Load();
    }

    // ------------------------------------------------------------------ 项目轴（D62/D63）

    Instance? ResolveProjectInstance(Project proj)
    {
        var id = proj.Instance ?? proj.Id;
        return _state.Launcher.Instance(id) ?? _state.Launcher.Instance(proj.Id);
    }

    /// <summary>看一个绑定项目：右栏换成项目页；实例只是它的运行体，没有实例也能看。</summary>
    void ShowProject(Project proj)
    {
        _project = proj;
        _projectProbe = null;
        _scope = ResolveProjectInstance(proj);
        _scopeRun = _scope is null ? null : _state.Running.FirstOrDefault(r => r.Entry.Instance == _scope.Id);
        _selected = _scope?.Id ?? proj.Id;
        _open = null;
        _heroAnimated = false;
        if (_scope is not null) _state.SelectedInstance = _scope;
        // 先在屏幕摆对项目页（数据都在 project.json 里），preset 探测在后台刷新：
        // 页头/启动/工作区不该等一次 dump-config。
        _projectProbeLoading = true;
        PaintHeader();
        CardHost.Children.Clear();
        PaintProjectCards();
        if (_lastInsts is not null && _scope is not null) FillRail(_lastInsts);
        CardScroll?.ScrollToTop();
        Load();
    }

    /// <summary>项目页的 preset 事实来自发行号自己的 dump-config（只读，失败不挡页面）。</summary>
    async Task LoadProjectAsync(Project proj, int gen)
    {
        _projectProbeLoading = true;
        PresetProbe? probe = null;
        try
        {
            var inst = _scope;
            if (inst is not null && !string.IsNullOrWhiteSpace(inst.Home))
            {
                var settings = _state.Settings;
                var profile = proj.Dsh.Profile ?? inst.LastProfile ?? settings.Launch.DefaultProfile;
                var version = proj.Dsh.Version ?? inst.Dsh.Version ?? settings.Launch.DefaultRelease;
                var run = await AgentPresets.ProbeAsync(_state.Launcher, inst.Home, version,
                    inst.Workspace?.Path ?? inst.Home, profile, CancellationToken.None);
                probe = run.Probe;
            }
        }
        catch { /* 发行号没装 / node 不可用：只显示已绑的 id */ }
        if (gen != _loadGen) return;
        _projectProbe = probe;
        _projectProbeLoading = false;
        PaintHeader();
        PaintCards(_lastInsts ?? []);
    }

    async Task StartProjectAsync(Project proj)
    {
        if (_projectStarting) return;
        _projectStarting = true;
        _host.Toast($"正在启动 {proj.Name}…");
        try
        {
            var state = await Task.Run(() => ProjectRunner.RunAsync(
                _state.Launcher, new Projects(_state.Launcher), proj, null, CancellationToken.None));
            var status = state.GetType().GetProperty("status")?.GetValue(state)?.ToString();
            _state.RefreshRunning();
            _state.Reload();
            _project = new Projects(_state.Launcher).Get(proj.Id) ?? proj;
            _scope = ResolveProjectInstance(_project);
            _host.Toast(status == "already-running" ? "已在跑，已切到窗口" : "已启动");
            Load();
        }
        catch (Exception ex)
        {
            _host.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            _projectStarting = false;
        }
    }

    void StopProject(Project proj)
    {
        var inst = ResolveProjectInstance(proj);
        if (inst is null) return;
        var run = _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
        if (run is null)
        {
            _host.Toast("这个进程不是 PAD 起的，停不了", bad: true);
            return;
        }
        if (_state.Settings.Other.ConfirmStop)
        {
            var ask = MessageBox.Show($"停止 {proj.Name} · {run.Profile}？", "PAD",
                MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ask != MessageBoxResult.OK) return;
        }
        _state.Runner.Stop(run.Entry.Instance, run.Profile);
        _state.RefreshRunning();
        _host.Toast("已停止");
        Load();
    }

    /// <summary>点 preset chip = 设成这个项目的默认（写回 profile patch 层）。</summary>
    void UsePreset(Project proj, string presetId)
    {
        try
        {
            var inst = ResolveProjectInstance(proj);
            if (inst is null) { _host.Toast("项目还没有实例：先启动一次", bad: true); return; }
            var profile = proj.Dsh.Profile ?? inst.LastProfile ?? _state.Settings.Launch.DefaultProfile;
            var list = proj.Agents.ToList();
            list.RemoveAll(x => string.Equals(x, presetId, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, presetId);
            AgentPresets.Apply(AgentPresets.PatchPath(inst.Home, profile), presetId);
            proj.Agents = list;
            proj.Dsh.Profile ??= profile;
            new Projects(_state.Launcher).Save(proj);
            _project = proj;
            _host.Toast($"默认 preset：{presetId}");
            Load();
        }
        catch (Exception ex)
        {
            _host.Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>「添加」把发行号里还没绑的 preset 挂到项目上；默认不变。</summary>
    void AddPreset(Project proj)
    {
        var existing = proj.Agents.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = _projectProbe?.Presets.Where(x => !existing.Contains(x.Id)).ToList() ?? [];
        if (choices.Count == 0)
        {
            _host.Toast(_projectProbeLoading ? "正在读发行号的 preset…" : "没有可选的新 preset");
            return;
        }
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Bottom };
        if (RunBtn is not null) menu.PlacementTarget = RunBtn;
        foreach (var c in choices)
        {
            var item = new MenuItem { Header = $"{c.Id}  ·  order {c.Order}" };
            item.Click += (_, _) => AddPresetToProject(proj, c.Id);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    void AddPresetToProject(Project proj, string presetId)
    {
        try
        {
            var inst = ResolveProjectInstance(proj);
            if (inst is null) { _host.Toast("项目还没有实例：先启动一次", bad: true); return; }
            var profile = proj.Dsh.Profile ?? inst.LastProfile ?? _state.Settings.Launch.DefaultProfile;
            var list = proj.Agents.ToList();
            if (!list.Contains(presetId, StringComparer.OrdinalIgnoreCase)) list.Add(presetId);
            AgentPresets.Apply(AgentPresets.PatchPath(inst.Home, profile), list[0]);
            proj.Agents = list;
            proj.Dsh.Profile ??= profile;
            new Projects(_state.Launcher).Save(proj);
            _project = proj;
            _host.Toast($"已添加 preset {presetId}");
            Load();
        }
        catch (Exception ex)
        {
            _host.Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>项目页页头：项目名 + 项目 logo + 运行灯 + 工作区/preset 事实。</summary>
    void ProjectHeader()
    {
        var proj = _project!;
        var inst = _scope;
        var phase = inst is null ? new ProjectRun(proj.Id, RunPhase.Stopped, "") : _state.StatusOf(inst.Id);
        var run = inst is null ? null : _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);

        AddBtn.Visibility = Visibility.Collapsed;
        EditBtn.Visibility = Visibility.Visible;
        EditBtn.Content = "编辑";
        EditBtn.ToolTip = "重命名 / 合并 / 取消分组";
        BackHomeBtn.Visibility = Visibility.Visible;
        RunBtn.Visibility = Visibility.Visible;
        VersionsBtn.Visibility = inst is null ? Visibility.Collapsed : Visibility.Visible;
        ScopeLogoHost.Visibility = Visibility.Visible;
        ScopeLamp.Visibility = Visibility.Visible;
        ScopeMeta.Visibility = Visibility.Visible;

        PageTitle.Text = proj.Name;
        ScopeLogoHost.Content = inst is null ? LogoBox(AccentBrush, 28) : LogoFor(inst, _scopeFacts, 28);
        ScopeLogoHost.ToolTip = "这是「项目」（绑定层）；实例是它的运行体";
        ScopeLogoHost.Cursor = System.Windows.Input.Cursors.Arrow;
        ScopeLamp.Running = phase.Live;
        var preset = proj.Agents.Count > 0 ? proj.Agents[0] : "未绑 preset";
        ScopeMeta.Text = $"{proj.Workspaces.Count} 工作区 · preset {preset} · 实例 {inst?.Id ?? "未建"}";
        ScopeMeta.ToolTip = phase.Detail.Length > 0 ? "在跑的证据：" + phase.Detail : null;

        SetRunButton(canStop: run is not null);
        RunBtn.Style = (Style)Application.Current.Resources[run is not null ? "BtnDanger" : "BtnPrimary"];
    }

    void PaintProjectCards()
    {
        var proj = _project!;
        CardHost.Children.Add(ScopeCard("启动", 3, ProjectRunCard(proj), "L.Play"));
        CardHost.Children.Add(ScopeCard("agent preset", 1, ProjectPresetCard(proj), "L.Layers"));
        CardHost.Children.Add(ScopeCard("工作区", 1, ProjectWorkspacesCard(proj), "L.Folder"));
        if (_scope is not null)
        {
            CardHost.Children.Add(ScopeCard("对话", 1, DialogCard(), "L.Chat"));
            CardHost.Children.Add(ScopeCard("环境", 3, EnvCard(_scope), "L.Layers"));
        }
    }

    /// <summary>启动卡：项目在不在跑、怎么启动/停止/打开；实例只是运行体。</summary>
    UIElement ProjectRunCard(Project proj)
    {
        var inst = _scope;
        var phase = inst is null ? new ProjectRun(proj.Id, RunPhase.Stopped, "") : _state.StatusOf(inst.Id);
        var run = inst is null ? null : _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
        var facts = _scopeFacts;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Icon("L.Play", 13, AccentBrush));
        head.Children.Add(new TextBlock
        {
            Text = phase.Say,
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = (Brush)Application.Current.Resources[phase.Live ? "Accent" : "TextMuted"],
        });
        head.Children.Add(new RunLamp
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(8, 2, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Running = phase.Live,
        });
        left.Children.Add(head);
        left.Children.Add(new TextBlock
        {
            Text = inst is null
                ? "还没有实例：点「启动」会按项目的发行号/profile 建一个，并写好工作区注册表。"
                : $"{inst.Dsh.Version} · {ShortHome(inst.Home)}",
            FontSize = 10.5,
            Foreground = Muted,
            Margin = new Thickness(1, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = inst?.Home,
        });

        var stats = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(0, 12, 0, 0) };
        stats.Children.Add(Stat("实例", inst?.Id ?? "—", "L.Hash", mono: true));
        stats.Children.Add(Stat("pid", facts?.Pid?.ToString() ?? "—", "L.Cpu", mono: true));
        stats.Children.Add(Stat("端口", facts?.AddressText ?? "—", "L.Plug", mono: true));
        stats.Children.Add(Stat("已跑", facts?.UptimeText ?? "—", "L.Clock"));
        stats.Children.Add(Stat("工作区", proj.Workspaces.Count.ToString(), "L.Folder", copyable: false));
        stats.Children.Add(Stat("默认 preset", proj.Agents.Count > 0 ? proj.Agents[0] : "—", "L.Layers", mono: true));
        left.Children.Add(stats);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 2, 0, 0) };
        actions.Children.Add(HeroButton(run is not null ? "停止" : "启动",
            run is not null ? "L.Stop" : "L.Play",
            (_, _) =>
            {
                if (_project is null) return;
                if (run is not null) StopProject(_project);
                else _ = StartProjectAsync(_project);
            }));
        if (!string.IsNullOrWhiteSpace(facts?.PadUrl))
            actions.Children.Add(HeroButton("打开界面", "L.External", (_, _) => Reveal.Url(facts!.PadUrl!)));
        else if (facts?.HasWindow == true)
            actions.Children.Add(HeroButton("切到窗口", "L.Window", ActivateShell_Click));
        if (proj.Workspaces.Count > 0)
            actions.Children.Add(HeroButton("打开工作区", "L.Folder", (_, _) => Reveal.Path(proj.Workspaces[0])));
        if (inst is not null)
            actions.Children.Add(HeroButton("实例视角", "L.Layers", (_, _) => ShowScope(inst)));
        Grid.SetColumn(actions, 1);
        root.Children.Add(actions);
        return root;
    }

    /// <summary>preset 卡：chip = 已绑，点一个设成默认；「添加」从发行号里挂新选项。</summary>
    UIElement ProjectPresetCard(Project proj)
    {
        var stack = new StackPanel();
        var wrap = new WrapPanel();
        foreach (var id in proj.Agents)
        {
            var chip = new Button
            {
                Content = id,
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 6),
                Style = (Style)Application.Current.Resources["BtnSm"],
                ToolTip = string.Equals(id, proj.Agents[0], StringComparison.OrdinalIgnoreCase)
                    ? "当前默认"
                    : "点一下设为默认",
            };
            if (string.Equals(id, proj.Agents[0], StringComparison.OrdinalIgnoreCase))
                chip.Style = (Style)Application.Current.Resources["BtnPrimary"];
            var preset = id;
            chip.Click += (_, _) => UsePreset(proj, preset);
            wrap.Children.Add(chip);
        }
        var add = new Button
        {
            Content = BtnContent("添加", "L.Plus"),
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 0, 6),
            Style = (Style)Application.Current.Resources["BtnSm"],
        };
        add.Click += (_, _) => AddPreset(proj);
        wrap.Children.Add(add);
        stack.Children.Add(wrap);

        var note = _projectProbeLoading
            ? "正在读发行号的 preset…"
            : _projectProbe is null
                ? "读不到发行号的 preset（实例还没建/内核没装）；已绑的仍可用"
                : "发行号可选：" + string.Join(" / ", _projectProbe.Presets.Select(x => x.Id));
        stack.Children.Add(new TextBlock
        {
            Text = note,
            FontSize = 10,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "chip = 这个项目的默认 agent；添加只是挂选项，不换默认",
            FontSize = 10,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
        });
        return stack;
    }

    /// <summary>工作区卡：项目的绑定目录（点路径打开）。</summary>
    UIElement ProjectWorkspacesCard(Project proj)
    {
        var stack = new StackPanel();
        foreach (var ws in proj.Workspaces.Take(4))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(Icon("L.Folder", 12, Muted));
            var name = new TextBlock
            {
                Text = ShortHome(ws),
                FontSize = 11.5,
                Foreground = Primary,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 150,
                ToolTip = ws,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            name.MouseLeftButtonUp += (_, _) => Reveal.Path(ws);
            row.Children.Add(name);
            stack.Children.Add(row);
        }
        if (proj.Workspaces.Count > 4)
            stack.Children.Add(new TextBlock
            {
                Text = $"+{proj.Workspaces.Count - 4} 个目录",
                FontSize = 10,
                Foreground = Muted,
                Margin = new Thickness(0, 2, 0, 0),
            });
        return stack;
    }

    /// <summary>主页的「项目」卡：项目轴的第一入口（点一行进项目页）。</summary>
    UIElement ProjectAxisBody()
    {
        var stack = new StackPanel();
        foreach (var p in _projects.Take(4))
        {
            var inst = ResolveProjectInstance(p);
            var phase = inst is null ? new ProjectRun(p.Id, RunPhase.Stopped, "") : _state.StatusOf(inst.Id);
            var run = inst is null ? null : _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
            var row = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 5),
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            left.Children.Add(new TextBlock
            {
                Text = p.Name,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Primary,
            });
            left.Children.Add(new TextBlock
            {
                Text = $"{p.Workspaces.Count} 工作区 · {p.Agents.Count} preset" +
                       (run is not null ? $" · {run.Profile}" : ""),
                FontSize = 10,
                Foreground = Muted,
            });
            grid.Children.Add(left);
            var lamp = new RunLamp
            {
                Width = 10,
                Height = 10,
                Running = phase.Live,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 2, 0),
            };
            Grid.SetColumn(lamp, 1);
            grid.Children.Add(lamp);
            row.Child = grid;
            row.MouseLeftButtonUp += (_, _) => ShowProject(p);
            row.MouseEnter += (_, _) => row.Background = (Brush)Application.Current.Resources["BgHover"];
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            stack.Children.Add(row);
        }
        if (_projects.Count > 4)
            stack.Children.Add(new TextBlock
            {
                Text = $"+{_projects.Count - 4} 个项目",
                FontSize = 10,
                Foreground = Muted,
            });
        return stack;
    }


    /// 否则在扫描完成前，标题已经是新的、卡片还是上一个 scope 的——那是撒谎的界面。
    /// </summary>
    void PaintTransition()
    {
        CardHost.Children.Clear();
        if (_project is not null)
        {
            CardHost.Children.Add(ScopeCard("读取中", 1, new TextBlock
            {
                Text = "正在读这个项目…",
                Style = (Style)Application.Current.Resources["Muted"],
                TextWrapping = TextWrapping.Wrap,
            }, "L.Clock"));
            return;
        }
        if (_scope is null)
        {
            if (_inventory is not null) PaintCards(_lastInsts ?? _state.Launcher.Instances());
            return;
        }
        CardHost.Children.Add(ScopeCard("读取中", 1, new TextBlock
        {
            Text = "正在读这份实例…",
            Style = (Style)Application.Current.Resources["Muted"],
            TextWrapping = TextWrapping.Wrap,
        }, "L.Clock"));
    }

    /// <summary><c>--shot-page manage-project</c>：不用指针也能进项目页，截图可复现。</summary>
    public void OpenProjectForShot()
    {
        // 项目轴优先：有 PAD 项目就截项目页（绑定层）；没有才退回实例页。
        var projects = new Projects(_state.Launcher).List();
        if (projects.Count > 0)
        {
            var wanted = _state.Settings.Other.Debug
                ? projects.FirstOrDefault(p => p.Instance is not null) ?? projects[0]
                : projects[0];
            ShowProject(wanted);
            return;
        }
        var insts = _lastInsts ?? _state.Launcher.Instances();
        var inst = _state.SelectedInstance is not null
            && insts.Any(i => i.Id == _state.SelectedInstance.Id)
                ? _state.SelectedInstance
                : insts.FirstOrDefault();
        if (inst is not null) ShowScope(inst);
    }

    static string DesktopHome() => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-desktop", "harness");

    /// <summary>Logo lookups hit the disk, so remember the answer per workspace.</summary>
    readonly Dictionary<string, string?> _marks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string?> _shots = new(StringComparer.OrdinalIgnoreCase);

    string? MarkFor(Instance inst) => Cached(_marks, inst, _ => InstIcon.PackImage(inst));

    /// <summary>
    /// Icon priority: what the user set → the instance's pack image → the carrier's icon
    /// (TUI/desktop bundle) → the DSH whale. A letter is never drawn.
    /// </summary>
    string? IconFor(Instance inst)
    {
        if (!string.IsNullOrWhiteSpace(inst.Display?.Logo))
        {
            var set = Skin.Resolve(inst.Display!.Logo!);
            if (System.IO.File.Exists(set)) return set;
        }

        var mark = MarkFor(inst);
        if (mark is not null) return mark;

        // Carriers are plugins, and there can be several TUIs: ask the packages themselves,
        // then the carrier database, before settling for the whale.
        var carrier = Carriers.Resolve(inst.Home, _state.Launcher.Root, Carriers.Bundles(inst.Home));
        if (carrier is not null) return carrier;

        return WhaleIcon();
    }

    /// <summary>The DSH app icon that ships beside the desktop shell.</summary>
    string? WhaleIcon()
    {
        if (_whaleLoaded) return _whale;
        _whaleLoaded = true;
        var exe = _inventory?.Shells.FirstOrDefault()?.Exe;
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var at = IOPath.Combine(IOPath.GetDirectoryName(exe) ?? "", "resources", "icon.png");
        if (System.IO.File.Exists(at)) _whale = at;
        return _whale;
    }

    bool _whaleLoaded;
    string? _whale;

    string? ShotFor(Instance inst) => Cached(_shots, inst, Logos.Ui);

    static string? Cached(Dictionary<string, string?> cache, Instance inst, Func<string?, string?> find)
    {
        var dir = inst.Workspace?.Path;
        if (string.IsNullOrWhiteSpace(dir)) return null;
        if (cache.TryGetValue(dir, out var known)) return known;
        var found = find(dir);
        cache[dir] = found;
        return found;
    }

    /// <summary>An ImageBrush for a local image; null when it cannot be decoded.</summary>
    static ImageBrush? ImageBrushFor(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>How many sessions touched this home on each of the last seven days.</summary>
    static int[] ScanActivity(string home)
    {
        var activity = new int[7];
        var dir = IOPath.Combine(home, "storages", "session_projcache", "sessions");
        if (!System.IO.Directory.Exists(dir)) return activity;

        var today = DateTime.Now.Date;
        try
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.json"))
            {
                var days = (today - System.IO.File.GetLastWriteTime(file).Date).Days;
                if (days is >= 0 and < 7) activity[6 - days]++;
            }
        }
        catch { /* unreadable dir: leave zeros */ }
        return activity;
    }

    /// <summary>Top five tools by call count; the report already reads them from the cache.</summary>
    static (string Tool, int Calls, long Tokens)[] ScanTools(string home)
    {
        try
        {
            return Usage.Measure(home).Tools
                .OrderByDescending(t => t.Calls)
                .Take(5)
                .Select(t => (t.Tool, t.Calls, t.Tokens))
                .ToArray();
        }
        catch
        {
            /* no cache yet */
            return [];
        }
    }

    /// <summary>
    /// The rail is shared with the relations page, so switching pages keeps it still.
    /// This method only feeds it and reacts to a pick.
    /// </summary>
    /// <summary>挂接舞台侧栏。页面是缓存的：再次上台时把上次的项目列表原位放回去。</summary>
    public void AttachRail(ProjectRail rail)
    {
        if (_rail is not null)
        {
            _rail.HomePicked -= ShowHome;
            _rail.Picked -= ShowScope;
            _rail.ProjectPicked -= ShowProjectById;
        }
        _rail = rail;
        _rail.HomePicked += ShowHome;
        _rail.Picked += ShowScope;
        _rail.ProjectPicked += ShowProjectById;
        if (_lastInsts is not null) FillRail(_lastInsts);
    }

    /// <summary>侧栏按当前视角喂数据：项目页/主页 = 项目视角；实例页 = 实例视角（D66）。</summary>
    void FillRail(IReadOnlyList<Instance> insts)
    {
        if (_rail is null) return;
        // 截图/直接进项目页时主页数据还没加载：项目列表便宜，就地补一次。
        if (_projects.Count == 0) _projects = new Projects(_state.Launcher).List();
        if (_project is not null)
        {
            _rail.FillProjects(ProjectRailRows(), _project.Id);
            return;
        }
        if (_scope is not null)
        {
            _rail.Fill(insts, _scope.Id);
            return;
        }
        if (_projects.Count > 0)
        {
            _rail.FillProjects(ProjectRailRows(), "");
            return;
        }
        _rail.Fill(insts, "");
    }

    /// <summary>项目行要的事实：工作区数、preset 数、在跑的 profile、状态灯。</summary>
    List<ProjectRail.ProjectRowData> ProjectRailRows()
    {
        var rows = new List<ProjectRail.ProjectRowData>();
        foreach (var p in _projects)
        {
            var inst = ResolveProjectInstance(p);
            var phase = inst is null
                ? new ProjectRun(p.Id, RunPhase.Stopped, "")
                : _state.StatusOf(inst.Id);
            var run = inst is null ? null : _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
            var meta = $"{p.Workspaces.Count} 工作区 · {p.Agents.Count} preset" +
                       (run is not null ? " · " + run.Profile : "");
            rows.Add(new ProjectRail.ProjectRowData(p.Id, p.Name, meta, phase.Live));
        }
        return rows;
    }

    void ShowProjectById(string id)
    {
        var proj = new Projects(_state.Launcher).Get(id);
        if (proj is null) return;
        ShowProject(proj);
    }

    void PaintProjects(IReadOnlyList<Instance> insts)
    {
        _lastInsts = insts;
        FillRail(insts);
    }

    /// <summary>项目卡的默认绑定：卡片没指定 source 时跟随全局选中实例（D28）。</summary>
    string DefaultProjectId()
    {
        if (_state.SelectedInstance is { } picked) return picked.Id;
        return _lastInsts?.FirstOrDefault()?.Id ?? "";
    }


    // ------------------------------------------------------------------ 卡片

    /// <summary>旧默认版面（D28 那套：环 + 载体计数 + 单项目卡）——只用来判断要不要迁到 D60 的新默认。</summary>
    static readonly (string Type, string View, int Cols)[] LegacyDefault =
    [
        ("running", "ring", 1),
        ("inventory", "count", 1),
        ("context", "bar", 1),
        ("activity", "bars", 2),
        ("project", "dots", 1),
        ("tools", "bars", 2),
        ("note", "", 1),
    ];

    /// <summary>
    /// 主页默认版面（D60）：先"谁在跑"，再"有哪些项目"，最后才是 7 天/工具这类历史。
    /// 载体计数（本机 DSH 11）降为可加卡片，不再占默认版面——它不是人打开管理页要先看的东西。
    /// </summary>
    static readonly (string Type, string View, int Cols)[] DefaultCards =
    [
        ("running", "list", 2),
        ("context", "bar", 1),
        ("projects", "rows", 2),
        ("note", "", 1),
        ("activity", "bars", 1),
        ("tools", "bars", 2),
    ];

    List<CardPref> Prefs()
    {
        var cards = _state.Settings.Ui.Cards;
        if (cards.Count == 0 || IsLegacyDefault(cards))
        {
            cards.Clear();
            foreach (var (type, view, cols) in DefaultCards)
                cards.Add(new CardPref { Type = type, View = view, Cols = cols });
            _state.SaveSettings();
        }
        return cards;
    }

    static bool IsLegacyDefault(List<CardPref> cards)
    {
        if (cards.Count != LegacyDefault.Length) return false;
        for (var i = 0; i < cards.Count; i++)
        {
            var (type, view, cols) = LegacyDefault[i];
            if (cards[i].Type != type || (cards[i].View ?? "") != view || cards[i].Cols != cols) return false;
        }
        return true;
    }

    void PaintCards(IReadOnlyList<Instance> insts)
    {
        CardHost.Children.Clear();
        if (_project is not null)
        {
            PaintProjectCards();
            return;
        }
        if (_scope is not null)
        {
            PaintScopeCards();
            return;
        }
        if (_projects.Count > 0)
            CardHost.Children.Add(ScopeCard("项目", 2, ProjectAxisBody(), "L.Folder"));
        foreach (var pref in Prefs())
            CardHost.Children.Add(Card(pref, insts));
        if (_editing) CardHost.Children.Add(AddTile());
    }

    /// <summary>
    /// 页头：主页和项目页共用同一个标题位——切换的是标题、状态行和能用的动作。
    /// 卡片编排（+ 卡片 / 编辑）只属于主页；项目页的动作是启动、版本设置、打开目录。
    /// </summary>
    void PaintHeader()
    {
        var scoped = _scope is not null;
        if (_project is not null)
        {
            ProjectHeader();
            return;
        }
        PageTitle.Text = scoped ? _scope!.Name : "本机";
        AddBtn.Visibility = scoped ? Visibility.Collapsed : Visibility.Visible;
        EditBtn.Visibility = scoped ? Visibility.Collapsed : Visibility.Visible;
        BackHomeBtn.Visibility = scoped ? Visibility.Visible : Visibility.Collapsed;
        RunBtn.Visibility = scoped ? Visibility.Visible : Visibility.Collapsed;
        VersionsBtn.Visibility = scoped ? Visibility.Visible : Visibility.Collapsed;
        // 项目页：标题左边是实例 logo + 运行灯（D58）；主页不摆。
        ScopeLogoHost.Visibility = scoped ? Visibility.Visible : Visibility.Collapsed;
        ScopeLamp.Visibility = scoped ? Visibility.Visible : Visibility.Collapsed;

        if (!scoped)
        {
            // 主页也给一行事实：几个项目、几个在跑（D60）。
            ScopeMeta.Visibility = _homeProjects.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ScopeMeta.Text = $"{_homeProjects.Count} 个项目 · {_homeLive} 个在跑";
            ScopeMeta.ToolTip = null;
            ScopeLogoHost.Content = null;
            ScopeLamp.Running = false;
            return;
        }

        var inst = _scope!;
        var phase = _state.StatusOf(inst.Id);
        ScopeLogoHost.Content = LogoFor(inst, _scopeFacts, 28);
        ScopeLogoHost.ToolTip = "点击换这个项目的 logo";
        ScopeLogoHost.Cursor = System.Windows.Input.Cursors.Hand;
        ScopeLamp.Running = phase.Live;
        var where = string.IsNullOrWhiteSpace(inst.Workspace?.Path) ? "" : " · " + ShortHome(inst.Workspace!.Path);
        ScopeMeta.Text = $"{inst.Dsh.Version} · {phase.Say}{where}";
        ScopeMeta.ToolTip = phase.Detail.Length > 0 ? "在跑的证据：" + phase.Detail : null;
        ScopeMeta.Visibility = Visibility.Visible;
        var run = _scopeRun ?? _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
        // 外部启动的实例 PAD 停不了：标题栏就别摆一颗按不动的「停止」。
        RunBtn.Visibility = run is not null || !phase.Live ? Visibility.Visible : Visibility.Collapsed;
        SetRunButton(canStop: run is not null);
        RunBtn.Style = (Style)Application.Current.Resources[run is not null ? "BtnDanger" : "BtnPrimary"];
    }

    // ------------------------------------------------------------------ 项目页（D54）

    /// <summary>项目页的一张卡：图标 + 标题 + 内容；鼠标悬停轻抬 2px（D58）。</summary>
    UIElement ScopeCard(string title, int cols, UIElement body, string iconKey)
    {
        var width = cols >= 3 ? 864 : cols >= 2 ? 576 : 280;
        var stack = new StackPanel();
        var head = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14),
        };
        head.Children.Add(Icon(iconKey, 14, Muted));
        head.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Resources["WbLabel"],
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(head);
        stack.Children.Add(body);

        var card = new Border
        {
            Width = width,
            Height = 226,
            Margin = new Thickness(0, 0, 16, 16),
            Child = stack,
        };
        card.Style = (Style)Resources["WbCard"];
        // Tagged like the home cards so the page entrance cascades them (Fx.EnterPage).
        Fx.SetEnter(card, "card");
        HoverLift(card); // D59：抬 2px + 阴影加深 + 描边浮现
        return card;
    }

    /// <summary>
    /// 项目页的排版（D57）：
    ///   运行（整行）→ 对话 + 项目 → 环境（整行）。
    /// 人最想先知道的是"在不在跑、跑在哪、在干什么"；内核/组合包/工具调用这些细节，
    /// 收进第三行的「环境」卡，不再各占一张卡。
    /// </summary>
    void PaintScopeCards()
    {
        var inst = _scope!;
        var live = LiveDialogCount();
        CardHost.Children.Add(ScopeCard("运行", 3, RunHeroCard(inst), "L.Play"));
        CardHost.Children.Add(ScopeCard(live > 0 ? $"对话 · {live} 在执行" : "对话", 2, DialogCard(), "L.Chat"));
        CardHost.Children.Add(ScopeCard("项目", 1, ProjectFactsCard(inst), "L.Folder"));
        CardHost.Children.Add(ScopeCard("环境", 3, EnvCard(inst), "L.Layers"));
    }

    /// <summary>
    /// 运行：一张卡回答"在不在跑、跑在哪、怎么打开"。
    /// 状态词和证据与左栏同一份（D52/D53）；pid / 端口 / 配置 / 已跑 / 内存来自进程现场
    /// （`RunFacts`：识别 DSH_HOME 的进程 + 系统 TCP 表），不是"不是 PAD 起的就不知道"（D57）。
    /// </summary>
    UIElement RunHeroCard(Instance inst)
    {
        var phase = _state.StatusOf(inst.Id);
        var facts = _scopeFacts;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };

        // 运行中外壳自己的图标（真实 logo）；取不到退回实例芯片（D58）。
        head.Children.Add(LogoFor(inst, facts, 38));

        var title = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal };
        stateRow.Children.Add(new TextBlock
        {
            Text = phase.Say,
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources[phase.Live ? "Accent" : "TextMuted"],
        });
        stateRow.Children.Add(new RunLamp
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 2, 0, 0),
            Running = phase.Live,
            ToolTip = phase.Live && phase.Detail.Length > 0 ? phase.Detail : null,
        });
        title.Children.Add(stateRow);
        title.Children.Add(new TextBlock
        {
            Text = facts?.Source ?? "正在读进程现场…",
            FontSize = 10.5,
            Foreground = Muted,
            Margin = new Thickness(1, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = phase.Detail.Length > 0 ? "在跑的证据：" + phase.Detail : null,
        });
        head.Children.Add(title);
        left.Children.Add(head);

        var stats = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(0, 12, 0, 0) };
        stats.Children.Add(Stat("进程", facts?.ProcessName ?? "—", "L.Cpu"));
        stats.Children.Add(Stat("pid", facts?.Pid is { } pid ? pid.ToString() : "—", "L.Hash", mono: true));
        stats.Children.Add(Stat("端口", facts?.AddressText ?? "—", "L.Plug", mono: true));
        stats.Children.Add(Stat("配置", facts?.ConfigProfile ?? "—", "L.Sliders", mono: true));
        stats.Children.Add(Stat("已跑", facts?.UptimeText ?? "—", "L.Clock"));
        stats.Children.Add(Stat("内存", facts?.MemoryText ?? "—", "L.Memory"));
        left.Children.Add(stats);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        // 现场数据第一次到达：logo 弹一下、统计淡入（之后 2s 状态刷新不再重播）。
        if (facts is not null && !_heroAnimated)
        {
            _heroAnimated = true;
            Fx.PulseIn(head, pop: true);
            if (Fx.Animated)
            {
                stats.Opacity = 0;
                stats.BeginAnimation(UIElement.OpacityProperty, Fx.Fade(0, 1, 280));
            }
        }

        var actions = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(18, 2, 0, 0),
        };
        if (facts?.HasWindow == true)
            actions.Children.Add(HeroButton("切到窗口", "L.Window", ActivateShell_Click));
        if (!string.IsNullOrWhiteSpace(facts?.PadUrl))
            actions.Children.Add(HeroButton("打开界面", "L.External", OpenPadUrl_Click));
        actions.Children.Add(HeroButton("打开工作区", "L.Folder", (_, _) => Reveal.Path(inst.Workspace?.Path)));
        actions.Children.Add(HeroButton("日志", "L.File", (_, _) => Reveal.Path(_state.Launcher.LogsDir(inst.Id))));
        Grid.SetColumn(actions, 1);
        root.Children.Add(actions);
        return root;
    }

    /// <summary>
    /// 项目 logo（D59）：用户设的 `display.logo` 最高优先 → 正在跑的外壳真实图标 → InstMark 芯片。
    /// 页头和运行卡共用这一份优先级。
    /// </summary>
    FrameworkElement LogoFor(Instance inst, RunFacts? facts, double size)
    {
        if (!string.IsNullOrWhiteSpace(inst.Display?.Logo))
        {
            var set = Skin.Resolve(inst.Display!.Logo!);
            var brush = System.IO.File.Exists(set) ? ImageBrushFor(set) : null;
            if (brush is not null) return LogoBox(brush, size);
        }
        var png = facts?.Exe is { Length: > 0 } exe ? AppIcon.PathFor(exe, _state.Launcher.Root) : null;
        var iconBrush = png is null ? null : ImageBrushFor(png);
        if (iconBrush is not null) return LogoBox(iconBrush, size);
        return new InstMark { Width = size, Height = size, DataContext = inst };
    }

    static Border LogoBox(Brush brush, double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size <= 30 ? 8 : 10),
        Background = brush,
    };

    /// <summary>环境：内核 / 组合包 / 整合包 / 会话 / 上下文 / 工具调用——技术细节，第三行。</summary>
    UIElement EnvCard(Instance inst)
    {
        var stack = new StackPanel();
        var grid = new UniformGrid { Columns = 3, Rows = 2 };
        grid.Children.Add(Stat("内核", inst.Dsh.Version.Length == 0 ? "—" : inst.Dsh.Version, "L.Cpu", mono: true));
        grid.Children.Add(Stat("组合包", _scopeBundles.Count == 0 ? "—" : _scopeBundles.Count.ToString(), "L.Package"));
        grid.Children.Add(Stat("整合包", PacksStat(), "L.Layers"));
        grid.Children.Add(Stat("会话", (_scopeInspect?.Sessions ?? 0).ToString(), "L.Chat"));
        grid.Children.Add(Stat("上下文", ContextPercentText(), "L.Gauge"));
        grid.Children.Add(Stat("工具调用", ToolsStat(), "L.Wrench"));
        stack.Children.Add(grid);
        stack.Children.Add(new TextBlock
        {
            Text = $"profile {_scopeProfiles.Count} 个 · 近 7 天 {_scopeActivity.Sum()} 次落盘 · 工作区 {(inst.Workspace?.Kind == "owned" ? "实例自带" : "外部目录")}",
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            Margin = new Thickness(0, 10, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return stack;
    }

    string ToolsStat()
    {
        var calls = _scopeTools.Sum(t => t.Calls);
        return calls == 0 ? "—" : calls.ToString();
    }

    string PacksStat()
    {
        if (_scopePacksLoading) return "读取中";
        if (_scopePacksNote.Length > 0) return "读不出来";
        return _scopePacks.Count == 0 ? "—" : _scopePacks.Count.ToString();
    }

    string ContextPercentText()
    {
        var ctx = _scopeInspect;
        if (ctx is null || ctx.ContextWindow <= 0) return "—";
        return $"{Math.Round(ctx.ContextTokens * 100.0 / ctx.ContextWindow)}%";
    }

    /// <summary>
    /// 小统计：图标 + 标签在上、数值在下；点一下复制（D58 的交互反馈）。
    /// 值是事实（pid/端口/路径…），复制比截图/手抄有用。
    /// </summary>
    UIElement Stat(string label, string value, string iconKey, bool mono = false, bool copyable = true)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(Icon(iconKey, 11, Muted));
        head.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = Muted,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(head);
        var val = new TextBlock
        {
            Text = value,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Primary,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = copyable ? $"点击复制：{value}" : value,
        };
        if (mono) val.Style = (Style)Application.Current.Resources["Mono"];
        stack.Children.Add(val);

        var tile = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 3, 8, 4),
            Margin = new Thickness(-7, -3, 5, -1),
            Background = Brushes.Transparent,
        };
        if (!copyable) return tile;

        tile.Cursor = System.Windows.Input.Cursors.Hand;
        tile.MouseEnter += (_, _) => tile.Background = (Brush)Application.Current.Resources["AccentWash"];
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Clipboard.SetText(value);
                _host.Toast($"已复制 {label}：{value}");
            }
            catch
            {
                /* 剪贴板被别的进程占着：不弹错，不打断 */
            }
        };
        return tile;
    }

    Button HeroButton(string label, string iconKey, RoutedEventHandler click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Icon(iconKey, 12, BodyBrush));
        content.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var btn = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources["BtnSm"],
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        btn.Click += click;
        return btn;
    }

    // ------------------------------------------------------------------ 对话（D55）

    /// <summary>管理口连上了就以 DSH 的 session.list 为准；没有就用投影缓存里的 openStep。</summary>
    bool GatewayReady => _scopeGatewayAsked && _scopeGatewayNote.Length == 0;

    bool LiveRunning(DialogRow dialog)
    {
        if (_scopeLive.TryGetValue(dialog.Id, out var live)) return live.Running;
        return _scopeLive.TryGetValue("session-" + dialog.Id, out live) && live.Running;
    }

    bool IsLive(DialogRow dialog) => GatewayReady ? LiveRunning(dialog) : dialog.Executing;

    int LiveDialogCount() => _scopeDialogs.Count(IsLive);

    /// <summary>
    /// 这个 home 一共有多少条对话：用 Inspect 数出来的文件数（只数不解析），
    /// 列表只解析最近 16 份——不能拿"解析了几份"当总数。
    /// </summary>
    int DialogTotal()
    {
        var total = _scopeInspect?.Sessions ?? 0;
        return total > _scopeDialogs.Count ? total : _scopeDialogs.Count;
    }

    /// <summary>
    /// 对话：这个 home 最近在写什么、哪几条在执行。
    /// 有管理口时用 DSH 的 session.list（真正的 agent 在跑）；没有时用投影缓存里的事实
    /// （openStep 未收尾 / 最后消息时间），脚注写明依据——不替 DSH 判读（D18）。
    /// </summary>
    UIElement DialogCard()
    {
        var stack = new StackPanel();
        var live = LiveDialogCount();

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock
        {
            Text = live.ToString(),
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources[live > 0 ? "Accent" : "TextMuted"],
        });
        var headText = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        headText.Children.Add(new TextBlock
        {
            Text = live > 0 ? "条对话在执行" : "没有对话在执行",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources[live > 0 ? "Accent" : "TextMuted"],
        });
        headText.Children.Add(new TextBlock
        {
            Text = $"共 {DialogTotal()} 条" + RecentText(),
            FontSize = 10.5,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            Margin = new Thickness(0, 2, 0, 0),
        });
        head.Children.Add(headText);
        stack.Children.Add(head);

        if (_scopeDialogsLoading)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "正在读这个 home 的会话…",
                Style = (Style)Application.Current.Resources["Muted"],
                Margin = new Thickness(0, 10, 0, 0),
            });
            return stack;
        }
        if (_scopeDialogs.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "这个 home 的 session 缓存里还没有对话",
                Style = (Style)Application.Current.Resources["Muted"],
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
            return stack;
        }

        foreach (var dialog in OrderedDialogs().Take(4))
            stack.Children.Add(DialogLine(dialog));
        stack.Children.Add(GatewayNote());
        return stack;
    }

    IEnumerable<DialogRow> OrderedDialogs() =>
        _scopeDialogs
            .OrderByDescending(IsLive)
            .ThenByDescending(d => d.LastActivity ?? DateTimeOffset.MinValue);

    string RecentText()
    {
        var newest = _scopeDialogs.Select(d => d.LastActivity).Where(x => x is not null)
            .OrderByDescending(x => x).FirstOrDefault();
        return newest is null ? "" : " · 最近 " + DialogRow.Age(newest);
    }

    /// <summary>一行对话：灯 + 图标 + 标题 + 工作区/时间；悬停有底纹、执行中的行呼吸（D58）。</summary>
    UIElement DialogLine(DialogRow dialog)
    {
        var on = IsLive(dialog);
        var row = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ToolTip = dialog.Detail.Length > 0 ? dialog.Detail : dialog.Cwd;

        var lamp = new RunLamp
        {
            Width = 11,
            Height = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 7, 0),
            Running = on,
            ToolTip = !on
                ? null
                : GatewayReady
                    ? "DSH session.list：这条 session 的 agent 在跑"
                    : "DSH 的 sessionStats.openStep 还没收尾",
        };
        Grid.SetColumn(lamp, 0);
        row.Children.Add(lamp);

        var chat = Icon("L.Chat", 11, on ? AccentBrush : Muted);
        chat.Margin = new Thickness(0, 0, 6, 0);
        Grid.SetColumn(chat, 1);
        row.Children.Add(chat);

        var title = new TextBlock
        {
            Text = dialog.Title,
            FontSize = 11,
            FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)Application.Current.Resources[on ? "TextPrimary" : "TextBody"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(title, 2);
        row.Children.Add(title);

        var right = new TextBlock
        {
            Text = (dialog.Workspace.Length > 0 ? dialog.Workspace + " · " : "") + dialog.AgeText,
            FontSize = 10,
            Foreground = on
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(right, 3);
        row.Children.Add(right);

        var tile = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(-4, 0, -4, 0),
            Background = Brushes.Transparent,
        };
        tile.MouseEnter += (_, _) => tile.Background = (Brush)Application.Current.Resources["AccentWash"];
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        return tile;
    }

    /// <summary>脚注：这份"在执行"是哪来的。管理口连上 = DSH 说的；没连上 = 缓存的 openStep。</summary>
    UIElement GatewayNote()
    {
        var text = !_scopeGatewayAsked
            ? "管理口：正在问…"
            : GatewayReady
                ? $"管理口已连：在跑来自 DSH 的 session.list（可见 {_scopeLive.Count} 条 session）"
                : _scopeGatewayNote;
        return new TextBlock
        {
            Text = text,
            FontSize = 9.5,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            Margin = new Thickness(0, 9, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = text,
        };
    }


    /// <summary>项目本身：工作区 / home / 来源 / 备注 + 打开按钮（D57：降成 1 列；D58 带图标）。</summary>
    UIElement ProjectFactsCard(Instance inst)
    {
        var stack = new StackPanel();
        stack.Children.Add(FactRow("工作区", inst.Workspace?.Path ?? "（没有工作区）", 58, "L.Folder"));
        stack.Children.Add(FactRow("home", inst.Home, 58, "L.Home"));
        stack.Children.Add(FactRow("来源", inst.Adopted ? "收编的 DSH home" : "PAD 建的实例", 58, "L.Package"));
        var note = inst.Note ?? inst.Display?.Info;
        stack.Children.Add(FactRow("备注", string.IsNullOrWhiteSpace(note) ? "（没写）" : note!, 58, "L.File"));

        // 同一件东西的第二条表示（D66）：实例（运行层）↔ 项目（绑定层）互相能跳。
        var owner = _projects.FirstOrDefault(p => ResolveProjectInstance(p)?.Id == inst.Id);
        if (owner is not null)
            stack.Children.Add(FactRow("项目", owner.Name, 58, "L.Data"));

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(OpenButton("工作区", inst.Workspace?.Path, "L.Folder"));
        buttons.Children.Add(OpenButton("目录", inst.Home, "L.Home"));
        buttons.Children.Add(OpenButton("日志", _state.Launcher.LogsDir(inst.Id), "L.File"));
        var logo = new Button
        {
            Content = BtnContent("换 logo", "L.Image"),
            Style = (Style)Application.Current.Resources["BtnSm"],
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 4),
            ToolTip = "给这个项目选一张 logo（也支持点页头那个 logo）",
        };
        logo.Click += (_, _) => PickLogo(inst);
        buttons.Children.Add(logo);
        if (owner is not null)
        {
            var link = new Button
            {
                Content = BtnContent("项目视角", "L.Data"),
                Style = (Style)Application.Current.Resources["BtnSm"],
                Height = 28,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 6, 4),
                ToolTip = "回到「" + owner.Name + "」的项目页（绑定层）",
            };
            link.Click += (_, _) => ShowProject(owner);
            buttons.Children.Add(link);
        }
        stack.Children.Add(buttons);
        return stack;
    }

    /// <summary>
    /// 换项目 logo（D59）：和实例设置里的「自定义 logo」是同一份 `display.logo`。
    /// 文件拷进实例目录（instances/&lt;id&gt;/logo.&lt;ext&gt;），只改 display，不动 home/workspace。
    /// </summary>
    void PickLogo(Instance inst)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选一个项目 logo",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp",
        };
        if (dlg.ShowDialog(_host) != true) return;
        try
        {
            var dir = _state.Launcher.InstanceDir(inst.Id);
            System.IO.Directory.CreateDirectory(dir);
            var ext = IOPath.GetExtension(dlg.FileName).ToLowerInvariant();
            var dest = IOPath.Combine(dir, "logo" + ext);
            foreach (var old in System.IO.Directory.EnumerateFiles(dir, "logo.*"))
                if (!string.Equals(old, dest, StringComparison.OrdinalIgnoreCase)) System.IO.File.Delete(old);
            System.IO.File.Copy(dlg.FileName, dest, true);
            _state.Launcher.SetDisplay(inst.Id, logo: dest);
            _state.Reload();
            PaintHeader();
            PaintCards(_lastInsts ?? []);
            _host.Toast("已换 logo");
        }
        catch (Exception ex)
        {
            _host.Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>点页头的 logo 等于换 logo（D59）。</summary>
    void ScopeLogo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_scope is { } inst) PickLogo(inst);
    }

    UIElement OpenButton(string label, string? path, string iconKey)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Icon(iconKey, 11, BodyBrush));
        content.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var btn = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources["BtnSm"],
            Height = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
        };
        btn.Click += (_, _) => Reveal.Path(path);
        return btn;
    }

    /// <summary>一行 图标 · 标签 · 值：卡片里的事实行，值太长就省略 + tooltip。</summary>
    UIElement FactRow(string label, string value, double labelWidth = 60, string? iconKey = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(iconKey)) nameRow.Children.Add(Icon(iconKey!, 10, Muted));
        nameRow.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Muted,
            Margin = new Thickness(iconKey is null ? 0 : 4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(nameRow, 0);
        grid.Children.Add(nameRow);

        var val = new TextBlock
        {
            Text = value,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextBody"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = value,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(val, 1);
        grid.Children.Add(val);
        return grid;
    }

    /// <summary>主页卡片标题的小图标（D58）。</summary>
    static string CardIcon(string type) => type switch
    {
        "running" => "L.Play",
        "inventory" => "L.Package",
        "context" => "L.Gauge",
        "projects" => "L.Folder",
        "activity" => "L.Activity",
        "tools" => "L.Wrench",
        "project" => "L.Folder",
        "note" => "L.Note",
        _ => "L.Hash",
    };

    UIElement Card(CardPref pref, IReadOnlyList<Instance> insts)
    {
        var wide = pref.Cols >= 2;
        var open = ReferenceEquals(_open, pref);
        var body = open
            ? ExpandBody(pref)
            : pref.Type switch
            {
                "running" => pref.View == "list" ? RunningListCard() : RingCard(),
                "projects" => ProjectsCard(insts),
                "inventory" => InventoryCard(),
                "context" => ContextCard(),
                "activity" => pref.Cols >= 2 ? ActivityCard() : ActivityMiniCard(),
                "tools" => ToolsCard(),
                "project" => ProjectCard(pref, insts),
                "note" => NoteCard(pref),
                _ => new TextBlock { Text = pref.Type },
            };

        var stack = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        head.Children.Add(Icon(CardIcon(pref.Type), 13, Muted));
        head.Children.Add(new TextBlock
        {
            Text = Title(pref, insts),
            Style = (Style)Resources["WbLabel"],
            Foreground = Muted,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(head);
        stack.Children.Add(body);

        var inner = new Grid();
        inner.Children.Add(stack);
        if (_editing)
        {
            var del = new Button
            {
                Content = "×",
                Style = (Style)Application.Current.Resources["BtnGhost"],
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -6, -6, 0),
                ToolTip = "移除这张卡片",
            };
            del.Click += (_, _) =>
            {
                var cards = _state.Settings.Ui.Cards;
                var idx = cards.IndexOf(pref);
                if (idx >= 0) cards.RemoveAt(idx);
                _state.SaveSettings();
                Load();
            };
            inner.Children.Add(del);
        }

        var card = new Border
        {
            Width = wide ? 576 : 280,
            Height = open ? 372 : 226,
            Margin = new Thickness(0, 0, 16, 16),
            Child = inner,
        };
        card.Style = (Style)Resources["WbCard"];
        // Tagged so the page-level entrance can cascade the cards instead of the panel
        // arriving as one block (Fx.EnterPage).
        Fx.SetEnter(card, "card");
        HoverLift(card); // D59：和项目页卡片同一套悬停层级
        if (!_editing)
        {
            card.Cursor = System.Windows.Input.Cursors.Hand;
            card.ToolTip = open ? "收起" : "展开";
            card.MouseLeftButtonUp += (_, _) =>
            {
                _open = open ? null : pref;
                Load();
            };
        }
        // No per-card DropShadowEffect: a blurred surface per card is re-rendered on
        // every frame of the entrance stagger and on every hover. The card style's
        // border carries the separation instead.
        return card;
    }

    /// <summary>
    /// The expanded face of a card: the same data, but every row instead of the aggregate.
    /// Interaction is where extra content comes from; the closed card stays a shape (D31).
    /// </summary>
    UIElement ExpandBody(CardPref pref)
    {
        var stack = new StackPanel();
        var homes = _inventory?.Homes ?? [];

        switch (pref.Type)
        {
            case "inventory":
            {
                // 展开不引入新数据源：把「本机 DSH」的载体逐行列出来（类型 · 版本/名 · home/路径 · 在跑）。
                foreach (var c in CarrierRows(_inventory))
                    stack.Children.Add(CarrierLine(c));
                break;
            }
            case "running":
            {
                var max = Math.Max(homes.Count == 0 ? 0 : homes.Max(h => h.Sessions), 1);
                foreach (var h in homes)
                    stack.Children.Add(DetailRow(ShortHome(h.Path), h.Sessions, max, h.Running));
                break;
            }
            case "tools":
            {
                var max = Math.Max(_tools.Length == 0 ? 0 : _tools.Max(t => t.Calls), 1);
                foreach (var (tool, calls, tokens) in _tools)
                    stack.Children.Add(DetailRow($"{tool} · {(tokens / 1000)}k", calls, max, false));
                break;
            }
            case "activity":
            {
                var max = Math.Max(_activity.DefaultIfEmpty(0).Max(), 1);
                for (var i = 0; i < _activity.Length; i++)
                    stack.Children.Add(DetailRow(DayLabel(6 - i), _activity[i], max, false));
                break;
            }
            default:
                return new TextBlock
                {
                    Text = "这张卡没有更多明细",
                    Style = (Style)Resources["WbLabel"],
                };
        }
        return stack;
    }

    static string ShortHome(string path)
    {
        var full = path.TrimEnd('\\', '/');
        var name = IOPath.GetFileName(full);
        if (string.IsNullOrWhiteSpace(name)) name = full;
        // Every PAD instance ends in a folder called "home"; the parent is what differs.
        if (name is "home" or "dsh-home" or "harness" or "workspace")
        {
            var parent = IOPath.GetFileName(IOPath.GetDirectoryName(full) ?? "");
            if (!string.IsNullOrWhiteSpace(parent)) name = parent + "/" + name;
        }
        return name.Length > 18 ? name[..18] : name;
    }

    /// <summary>One detail line: name, a bar for the amount, and the number itself.</summary>
    UIElement DetailRow(string label, int value, int max, bool lit)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8), Height = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextBody"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(name, 0);
        row.Children.Add(name);

        var bar = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = Math.Max(4, 210.0 * value / max),
            Background = lit
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["AccentSoft"],
        };
        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);

        var num = new TextBlock
        {
            Text = value.ToString(),
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(num, 2);
        row.Children.Add(num);

        return row;
    }

    /// <summary>
    /// 卡片标题。主页的上下文 / 工具调用 / 近 7 天统计的是"正在跑的那份 home"——
    /// 把项目名写进标题，否则人不知道这张卡是谁的数字（D61）。
    /// </summary>
    string Title(CardPref pref, IReadOnlyList<Instance> insts)
    {
        if (pref.Type != "project")
        {
            var label = Catalog.FirstOrDefault(c => c.Type == pref.Type).Label ?? pref.Type;
            if (_scope is null && pref.Type is "context" or "tools" or "activity")
            {
                var owner = HomeOwner();
                if (owner is not null) return $"{label} · {owner.Inst.Name}";
            }
            return label;
        }
        var id = string.IsNullOrWhiteSpace(pref.Source) ? DefaultProjectId() : pref.Source;
        return insts.FirstOrDefault(i => i.Id == id)?.Name ?? "未绑定";
    }

    UIElement RingCard()
    {
        var homes = _inventory?.Homes ?? [];
        var running = homes.Count(h => h.Running);
        const double r = 44;
        var grid = new Grid { Width = 104, Height = 104, HorizontalAlignment = HorizontalAlignment.Left };

        grid.Children.Add(new Path
        {
            Stroke = (Brush)Application.Current.Resources["LineDivider"],
            StrokeThickness = 9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = new EllipseGeometry(new Point(52, 52), r, r),
        });
        grid.Children.Add(new Path
        {
            Stroke = (Brush)Application.Current.Resources["Accent"],
            StrokeThickness = 9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Arc(52, 52, r, homes.Count <= 0 ? 0 : running / (double)homes.Count),
        });
        grid.Children.Add(new TextBlock
        {
            Text = running.ToString(),
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // 第二层：每个 home 一根细柱，高度=会话数，实心=在跑。
        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 34,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var maxSessions = Math.Max(homes.Count == 0 ? 0 : homes.Max(h => h.Sessions), 1);
        var accent = (Brush)Application.Current.Resources["Accent"];
        var idle = (Brush)Application.Current.Resources["LineBorder"];

        foreach (var h in homes.Take(10))
        {
            // +1 so an idle home still shows a stub: absence is information too.
            var barH = Math.Max(10, 32.0 * (h.Sessions + 1) / (maxSessions + 1));
            strip.Children.Add(new Border
            {
                Width = 12,
                Height = barH,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = h.Running ? accent : idle,
            });
        }

        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(strip);
        return stack;
    }

    UIElement InventoryCard()
    {
        var inv = _inventory;
        var carriers = CarrierRows(inv);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = carriers.Count.ToString(),
            FontSize = 40,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });

        // 第二层：一个载具一个方块。实心 = 在跑，浅色 = 装了没跑，灰 = home。
        var grid = new UniformGrid
        {
            Columns = 8,
            Rows = 2,
            Height = 64,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var accent = (Brush)Application.Current.Resources["Accent"];
        var soft = (Brush)Application.Current.Resources["AccentLight"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];
        if (carriers.Count == 0)
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = pale,
            });
        foreach (var c in carriers.Take(16))
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = c.Running ? accent : soft,
            });
        stack.Children.Add(grid);
        stack.Children.Add(new TextBlock
        {
            Text = $"内核 {(inv?.Kernels.Count ?? 0)} · home {(inv?.Homes.Count ?? 0)}",
            Style = (Style)Application.Current.Resources["Muted"],
            Margin = new Thickness(0, 8, 0, 0),
        });
        return stack;
    }

    /// <summary>一张卡片行：载具类型 · 版本/名 · home/路径 · 在跑。</summary>
    sealed record CarrierRow(string Kind, string Label, string Detail, bool Running);

    /// <summary>载体 = 桌面端 + TUI + CLI。按类型排，同内核算一组由数据本身区分（D36）。</summary>
    static List<CarrierRow> CarrierRows(Inventory? inv)
    {
        var rows = new List<CarrierRow>();
        if (inv is null) return rows;
        foreach (var s in inv.Shells)
            rows.Add(new CarrierRow("shell", $"{s.Version ?? "?"} · {IOPath.GetFileName(s.Exe)}",
                s.Home is null ? "" : "home " + ShortHome(s.Home), s.Running));
        foreach (var t in inv.Tuis)
            rows.Add(new CarrierRow("tui", $"{t.Package} {(t.Version ?? "")}".Trim(),
                t.Home is null ? "" : "home " + ShortHome(t.Home), t.Running));
        foreach (var c in inv.Clis)
            rows.Add(new CarrierRow("cli", c.Version ?? "?",
                c.Running ? "在跑" : ShortHome(c.Bin), c.Running));
        return rows;
    }

    UIElement CarrierLine(CarrierRow row)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6), Height = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var kind = new TextBlock
        {
            Text = row.Kind switch { "shell" => "桌面端", "tui" => "TUI", _ => "CLI" },
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources[row.Running ? "Accent" : "TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(kind, 0);
        grid.Children.Add(kind);

        var label = new TextBlock
        {
            Text = row.Label,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextBody"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var detail = new TextBlock
        {
            Text = row.Running ? "在跑" : row.Detail,
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources[row.Running ? "Accent" : "TextMuted"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);
        return grid;
    }

    /// <summary>Lerp pale → accent by activity, so one glance ranks every home.</summary>
    static Brush Heat(int value, int max, bool running)
    {
        var accent = ((SolidColorBrush)Application.Current.Resources["Accent"]).Color;
        var pale = ((SolidColorBrush)Application.Current.Resources["LineBorder"]).Color;
        if (!running)
            return new SolidColorBrush(Color.FromArgb(255, pale.R, pale.G, pale.B));

        var t = max <= 0 ? 0 : Math.Min(1.0, value / (double)max);
        t = 0.35 + 0.65 * t; // 即使在跑但没会话，也要看得见
        return new SolidColorBrush(Color.FromArgb(
            255,
            (byte)(pale.R + (accent.R - pale.R) * t),
            (byte)(pale.G + (accent.G - pale.G) * t),
            (byte)(pale.B + (accent.B - pale.B) * t)));
    }

    Border KindTick(int count, double width) => new()
    {
        Width = Math.Max(4, width),
        Height = 5,
        CornerRadius = new CornerRadius(3),
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Background = count > 0
            ? (Brush)Application.Current.Resources["Accent"]
            : (Brush)Application.Current.Resources["LineDivider"],
    };

    UIElement ContextCard() => ContextCard(DefaultHome());

    /// <summary>主页取在跑的那份 home；项目页取该项目自己的 home（D54）。</summary>
    UIElement ContextCard(string home)
    {
        var tokens = 0L;
        var window = 1_000_000L;
        var schema = 0L;
        try
        {
            var report = Inspect.Measure(home);
            tokens = report.ContextTokens;
            schema = report.ToolsTokens;
            if (report.ContextWindow > 0) window = report.ContextWindow;
        }
        catch { /* home missing */ }

        var pct = Math.Round(tokens * 100.0 / window);

        var bar = new Grid { Width = 20, Height = 124, VerticalAlignment = VerticalAlignment.Top };
        bar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["LineDivider"],
        });
        bar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["Accent"],
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = Math.Max(4, 124 * Math.Min(1.0, tokens / (double)window)),
        });

        var right = new StackPanel { Margin = new Thickness(16, 6, 0, 0) };
        right.Children.Add(new TextBlock
        {
            Text = pct + "%",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        right.Children.Add(new TextBlock
        {
            Text = $"{tokens / 1000}k / {window / 1000}k",
            Style = (Style)Application.Current.Resources["Muted"],
        });

        // 第二层：上下文里有多少是工具定义（深）与其余部分（浅）。
        var mix = new Grid { Width = 120, Height = 8, Margin = new Thickness(0, 12, 0, 0) };
        mix.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["LineDivider"],
        });
        mix.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = tokens <= 0 ? 0 : Math.Max(3, 120.0 * Math.Min(schema, tokens) / tokens),
            Background = (Brush)Application.Current.Resources["Accent"],
        });
        right.Children.Add(mix);

        // 谁的上下文？主页看的是"正在跑的那份 home"，写出来，别让人猜（D60）。
        right.Children.Add(new TextBlock
        {
            Text = ShortHome(home),
            FontSize = 9.5,
            Foreground = Muted,
            Margin = new Thickness(0, 10, 0, 0),
            MaxWidth = 160,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = home,
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(bar);
        row.Children.Add(right);
        return row;
    }

    /// <summary>上下文卡默认看哪份 home：在跑的那份，否则桌面端的。</summary>
    string DefaultHome() => _inventory?.Homes.FirstOrDefault(h => h.Running)?.Path
                            ?? IOPath.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "dsh-desktop", "harness");

    /// <summary>近 7 天会话数：每根柱子是一天的会话落盘数。</summary>
    UIElement ActivityCard() => ActivityCard(_activity);

    UIElement ActivityCard(int[] data)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = data.Sum().ToString(),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(ActivityBars(data));
        return stack;
    }

    /// <summary>七根柱 + 逐日标签：主页和项目页共用这一种画法，只是喂的数据不同。</summary>
    UIElement ActivityBars(int[] data)
    {
        var max = Math.Max(data.DefaultIfEmpty(0).Max(), 1);
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 92,
        };

        var accent = (Brush)Application.Current.Resources["Accent"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];
        var labels = new StackPanel { Orientation = Orientation.Horizontal };

        for (var i = 0; i < data.Length; i++)
        {
            var v = data[i];
            var h = v <= 0 ? 5 : Math.Max(9, 84.0 * v / max);
            var col = new StackPanel { Width = 66, VerticalAlignment = VerticalAlignment.Bottom };
            col.Children.Add(new Border
            {
                Width = 40,
                Height = h,
                CornerRadius = new CornerRadius(4),
                Background = v <= 0 ? pale : accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            bars.Children.Add(col);

            labels.Children.Add(new TextBlock
            {
                Text = DayLabel(6 - i),
                Width = 66,
                TextAlignment = TextAlignment.Center,
                Style = (Style)Application.Current.Resources["Muted"],
                FontSize = 10,
                Margin = new Thickness(0, 5, 0, 0),
            });
        }

        var wrap = new StackPanel();
        wrap.Children.Add(bars);
        wrap.Children.Add(labels);
        return wrap;
    }

    static string DayLabel(int daysAgo) => daysAgo switch
    {
        0 => "今天",
        1 => "昨天",
        _ => "-" + daysAgo + "天",
    };

    // ------------------------------------------------------------------ 主页主卡（D60）

    /// <summary>
    /// 在跑（D60）：列出正在跑的项目——logo、名称、profile/端口、已跑多久；点一行进项目页。
    /// 不再用"环 + 一排灰柱"：数字 2 不告诉人是谁在跑（D30 的密度应当是可读的密度）。
    /// </summary>
    UIElement RunningListCard()
    {
        var stack = new StackPanel();
        var live = _homeProjects.Where(p => p.Status.Live).ToList();
        if (live.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "现在没有项目在跑",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Muted,
                Margin = new Thickness(0, 6, 0, 0),
            });
            stack.Children.Add(new TextBlock
            {
                Text = "从「启动」页起一个，或点下面的项目进项目页。",
                Style = (Style)Application.Current.Resources["Muted"],
                Margin = new Thickness(0, 6, 0, 0),
            });
            var go = new Button
            {
                Content = BtnContent("去启动", "L.Play"),
                Style = (Style)Application.Current.Resources["BtnSm"],
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 0),
            };
            go.Click += (_, _) => _host.ShowTab("launch");
            stack.Children.Add(go);
            return stack;
        }

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock
        {
            Text = live.Count.ToString(),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = AccentBrush,
        });
        var execCount = live.Sum(p => p.Recent.Count(d => d.Executing));
        head.Children.Add(new TextBlock
        {
            Text = " 个项目在跑" + (execCount > 0 ? $" · {execCount} 条对话在执行" : ""),
            FontSize = 12,
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 0, 0, 5),
        });
        stack.Children.Add(head);
        // 在跑啥（D61）：项目行下面直接列它正在做的对话；跨项目共用 2 行预算，卡高放得下。
        var shown = 0;
        foreach (var p in live.Take(3))
        {
            stack.Children.Add(ProjectLine(p, running: true));
            if (shown >= 2) continue;
            var exec = p.Recent.Where(d => d.Executing).ToList();
            var pick = exec.Count > 0 ? exec : p.Recent.ToList();
            foreach (var d in pick.Take(2 - shown))
            {
                stack.Children.Add(HomeDialogLine(d));
                shown++;
            }
        }

        // 底部动作：进项目页 / 切到那个窗口——在跑卡不能只"报告在跑"（D60）。
        var first = live[0];
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var open = new Button
        {
            Content = BtnContent("进入项目", "L.Folder"),
            Style = (Style)Application.Current.Resources["BtnSm"],
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 0),
        };
        open.Click += (_, _) => ShowScope(first.Inst);
        actions.Children.Add(open);
        if (first.Facts?.HasWindow == true)
        {
            var focus = new Button
            {
                Content = BtnContent("切到窗口", "L.Window"),
                Style = (Style)Application.Current.Resources["BtnSm"],
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
            };
            focus.Click += (_, _) => ShellActivate.Try(first.Facts!.Pids);
            actions.Children.Add(focus);
        }
        stack.Children.Add(actions);
        // 没有对话可列时补一行"最后在忙什么"；已经有对话行就不重复（D61）。
        if (shown == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = first.LastDialog is null
                    ? $"还没读到正在执行的内容 · {first.Inst.Dsh.Version}"
                    : $"最近 {first.Dialogs} 条对话 · 最新 {DialogRow.Age(first.LastDialog)}",
                FontSize = 10,
                Foreground = Muted,
                Margin = new Thickness(0, 10, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = first.Inst.Home,
            });
        }
        return stack;
    }

    /// <summary>主页的"在跑啥"一行：灯 + 对话标题 + 工作区/时间（D61）。</summary>
    UIElement HomeDialogLine(DialogRow d)
    {
        var row = new Grid { Margin = new Thickness(34, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lamp = new RunLamp
        {
            Width = 8,
            Height = 8,
            Running = d.Executing,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };
        row.Children.Add(lamp);

        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock
        {
            Text = d.Executing ? "在跑 " : "最近 ",
            FontSize = 9.5,
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        title.Children.Add(new TextBlock
        {
            Text = d.Title,
            FontSize = 10.5,
            Foreground = d.Executing ? AccentBrush : BodyBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        var right = new TextBlock
        {
            Text = (d.Workspace.Length > 0 ? d.Workspace + " · " : "") + d.AgeText,
            FontSize = 9.5,
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(right, 2);
        row.Children.Add(right);

        var tile = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(-4, 0, -4, 0),
            Background = Brushes.Transparent,
        };
        tile.ToolTip = d.Detail.Length > 0 ? d.Detail : d.Cwd;
        tile.MouseEnter += (_, _) => tile.Background = (Brush)Application.Current.Resources["AccentWash"];
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        return tile;
    }

    /// <summary>项目（D60）：所有实例一行一个；点一行 = 进那个项目的项目页（和侧栏同一个 ShowScope）。</summary>
    UIElement ProjectsCard(IReadOnlyList<Instance> insts)
    {
        var stack = new StackPanel();
        // 在跑的排最前，其余按"最近写过对话"排：主页这张列表的头几行必须是最该看的（D60）。
        var ordered = _homeProjects
            .OrderByDescending(p => p.Status.Live)
            .ThenByDescending(p => p.LastDialog ?? DateTimeOffset.MinValue)
            .ToList();
        // 卡高 226 只放得下 4 行；总数在页头（"N 个项目 · M 个在跑"）已经写了，不在卡底重复。
        foreach (var p in ordered.Take(4)) stack.Children.Add(ProjectLine(p, running: false));
        return stack;
    }

    /// <summary>
    /// 主页的项目行（D60）：logo + 名称 + 状态灯 + 右列事实。
    /// 在跑的右列给 profile / 端口 / 已跑；停着的右列给最近对话时间 + 内核版本。
    /// </summary>
    UIElement ProjectLine(HomeProject p, bool running)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = LogoFor(p.Inst, p.Facts, 22);
        Grid.SetColumn(logo, 0);
        row.Children.Add(logo);

        var nameCol = new StackPanel { Margin = new Thickness(9, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        nameCol.Children.Add(new TextBlock
        {
            Text = p.Inst.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Primary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        nameCol.Children.Add(new TextBlock
        {
            Text = p.Inst.Dsh.Version,
            FontSize = 9.5,
            Foreground = Muted,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(nameCol, 1);
        row.Children.Add(nameCol);

        var state = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        state.Children.Add(new RunLamp
        {
            Width = 10,
            Height = 10,
            Running = p.Status.Live,
            Margin = new Thickness(0, 0, 5, 0),
        });
        state.Children.Add(new TextBlock
        {
            Text = p.Status.Say,
            FontSize = 10,
            Foreground = p.Status.Live ? AccentBrush : Muted,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(state, 2);
        row.Children.Add(state);

        var right = new TextBlock
        {
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 210,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (running && p.Facts is { } f)
        {
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(f.ConfigProfile) && f.ConfigProfile != "—") bits.Add(f.ConfigProfile);
            if (!string.IsNullOrWhiteSpace(f.AddressText) && f.AddressText != "—") bits.Add(f.AddressText);
            if (p.Facts.Uptime is not null) bits.Add(f.UptimeText);
            right.Text = string.Join(" · ", bits);
            right.Foreground = AccentBrush;
        }
        else
        {
            right.Text = p.LastDialog is null
                ? "还没对话"
                : $"最近 {DialogRow.Age(p.LastDialog)} · {p.Dialogs} 条对话";
            right.Foreground = Muted;
        }
        Grid.SetColumn(right, 3);
        row.Children.Add(right);

        var tile = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(-6, 0, -6, 0),
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = $"进入 {p.Inst.Name} 的项目页",
        };
        tile.MouseEnter += (_, _) => tile.Background = (Brush)Application.Current.Resources["AccentWash"];
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonUp += (_, _) => ShowScope(p.Inst);
        return tile;
    }

    /// <summary>近 7 天（D60：单列版）：大数字 + 七根小柱，不再占两列。</summary>
    UIElement ActivityMiniCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = _activity.Sum().ToString(),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Primary,
            Margin = new Thickness(0, 0, 0, 6),
        });
        stack.Children.Add(MiniBars(_activity));
        stack.Children.Add(new TextBlock
        {
            Text = "近 7 天会话落盘",
            FontSize = 10,
            Foreground = Muted,
            Margin = new Thickness(0, 8, 0, 0),
        });
        return stack;
    }

    /// <summary>七根小柱（单列卡）：只用高度和颜色表示相对量，没有标签。</summary>
    UIElement MiniBars(int[] data)
    {
        var max = Math.Max(data.DefaultIfEmpty(0).Max(), 1);
        var bars = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 96,
        };
        var accent = AccentBrush;
        var pale = (Brush)Application.Current.Resources["LineDivider"];
        foreach (var v in data)
        {
            var h = v <= 0 ? 5 : Math.Max(9, 88.0 * v / max);
            var col = new StackPanel { Width = 30, VerticalAlignment = VerticalAlignment.Bottom };
            col.Children.Add(new Border
            {
                Width = 16,
                Height = h,
                CornerRadius = new CornerRadius(4),
                Background = v <= 0 ? pale : accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
            bars.Children.Add(col);
        }
        return bars;
    }

    /// <summary>工具调用分布：前五名各一根横条，长度按调用次数。</summary>
    UIElement ToolsCard() => ToolsCard(_tools);

    UIElement ToolsCard((string Tool, int Calls, long Tokens)[] tools)
    {
        var stack = new StackPanel();
        var total = tools.Sum(t => t.Calls);
        // 数字后面直接写口径：谁的、哪段时间的（D61）。
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new TextBlock
        {
            Text = total == 0 ? "—" : total.ToString(),
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        head.Children.Add(new TextBlock
        {
            Text = " 次调用 · 近 7 天",
            FontSize = 11,
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 0, 0, 5),
        });
        stack.Children.Add(head);

        if (tools.Length == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "这个 home 还没有工具调用记录",
                Style = (Style)Application.Current.Resources["Muted"],
            });
            return stack;
        }

        var max = Math.Max(tools.Max(t => t.Calls), 1);
        var maxTokens = Math.Max(tools.Max(t => t.Tokens), 1);
        var accent = (Brush)Application.Current.Resources["Accent"];
        var accentSoft = (Brush)Application.Current.Resources["AccentLight"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];

        foreach (var (tool, calls, tokens) in tools)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4), Height = 18 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = tool,
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextBody"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 0);
            row.Children.Add(name);

            // 两层：上=调用次数，下=该工具吃掉的 token。同一条柱的两个维度。
            var bars = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            bars.Children.Add(new Border
            {
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = accent,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 240.0 * calls / max),
            });
            bars.Children.Add(new Border
            {
                Height = 4,
                Margin = new Thickness(0, 3, 0, 0),
                CornerRadius = new CornerRadius(2),
                Background = accentSoft,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(3, 240.0 * tokens / maxTokens),
            });
            Grid.SetColumn(bars, 1);
            row.Children.Add(bars);

            var num = new TextBlock
            {
                Text = calls.ToString(),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextMuted"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(num, 2);
            row.Children.Add(num);

            stack.Children.Add(row);
        }
        return stack;
    }

    UIElement ProjectCard(CardPref pref, IReadOnlyList<Instance> insts)
    {
        var id = string.IsNullOrWhiteSpace(pref.Source) ? DefaultProjectId() : pref.Source;
        var inst = insts.FirstOrDefault(i => i.Id == id) ?? insts.FirstOrDefault();
        var stack = new StackPanel();

        var sessions = 0;
        var run = inst is null
            ? new ProjectRun("", RunPhase.Stopped, "")
            : _state.StatusOf(inst.Id);
        if (inst is not null)
        {
            try { sessions = Inspect.Measure(inst.Home).Sessions; } catch { }
        }

        stack.Children.Add(new TextBlock
        {
            Text = sessions.ToString(),
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });

        var dots = new UniformGrid { Columns = 8, Rows = 1, Height = 26, Margin = new Thickness(0, 10, 0, 0) };
        PaintDots(dots, Math.Max(sessions, 1), Math.Min(sessions, 8));
        stack.Children.Add(dots);
        stack.Children.Add(StatusLine(run, new Thickness(0, 12, 0, 0)));

        // A project that ships a UI shot shows it: the card stops being a blank number.
        var shot = inst is null ? null : ShotFor(inst);
        var brush = shot is null ? null : ImageBrushFor(shot);
        if (brush is null) return stack;

        var cover = new Border
        {
            Height = 92,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(0, 0, 0, 10),
            Background = brush,
        };

        // With a cover the card is shared space, so the rows below it get compact.
        var compact = new StackPanel { Orientation = Orientation.Horizontal };
        compact.Children.Add(new TextBlock
        {
            Text = sessions.ToString(),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["TextPrimary"],
        });
        compact.Children.Add(StatusLine(run, new Thickness(10, 0, 0, 4), bottom: true));

        var withCover = new StackPanel();
        withCover.Children.Add(cover);
        withCover.Children.Add(compact);
        return withCover;
    }

    /// <summary>
    /// 项目卡上的状态行：字（启动中 / 在跑 / 未启动）+ 右边的绿灯。
    /// 看的是 <see cref="AppState.StatusOf"/>，和左栏同一份事实（D53）。
    /// </summary>
    static StackPanel StatusLine(ProjectRun run, Thickness margin, bool bottom = false)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = margin,
            VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
        };
        line.Children.Add(new TextBlock
        {
            Text = run.Say,
            Foreground = run.Live
                ? (Brush)Application.Current.Resources["Accent"]
                : (Brush)Application.Current.Resources["TextMuted"],
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(new RunLamp
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Running = run.Live,
            ToolTip = run.Live ? run.Detail : null,
        });
        return line;
    }

    /// <summary>状态快照变了：只重画页头与卡片区（数据都在内存里，不重跑扫描）。</summary>
    void OnStatus()
    {
        if (_lastInsts is null) return;
        PaintHeader();
        PaintCards(_lastInsts);
    }

    UIElement NoteCard(CardPref pref)
    {
        // View mode stays a plain block: a TextBox drags the shared field style along and a
        // note is not a form field. Editing happens in edit mode, where a field is honest.
        if (!_editing)
            return new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(pref.Text) ? "空便签 · 点「编辑」写一句" : pref.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextBody"],
                VerticalAlignment = VerticalAlignment.Top,
            };

        var box = new TextBox
        {
            Text = pref.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)Application.Current.Resources["TextBody"],
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        box.ClearValue(StyleProperty);
        box.TextChanged += (_, _) =>
        {
            if (pref.Text == box.Text) return;
            pref.Text = box.Text;
        };
        box.LostFocus += (_, _) => _state.SaveSettings();
        return box;
    }

    UIElement AddTile()
    {
        var tile = new Border
        {
            Width = 288,
            Height = 214,
            Margin = new Thickness(0, 0, 14, 14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(2),
            BorderBrush = (Brush)Application.Current.Resources["LineDivider"],
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 34,
                Foreground = (Brush)Application.Current.Resources["LineDivider"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        tile.MouseLeftButtonUp += (_, _) => AddPop.IsOpen = true;
        return tile;
    }

    static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["Muted"],
        Margin = new Thickness(0, 10, 0, 0),
    };

    static void PaintDots(UniformGrid host, int total, int lit)
    {
        host.Children.Clear();
        var accent = (Brush)Application.Current.Resources["Accent"];
        var pale = (Brush)Application.Current.Resources["LineDivider"];
        for (var i = 0; i < total; i++)
            host.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 5, 5),
                Background = i < lit ? accent : pale,
            });
    }

    static Geometry Arc(double cx, double cy, double r, double fraction)
    {
        if (fraction <= 0.0001) return Geometry.Empty;
        var start = -90 * Math.PI / 180;
        var sweep = Math.Min(359.9, 360 * fraction);
        var end = start + sweep * Math.PI / 180;
        var figure = new PathFigure
        {
            StartPoint = new Point(cx + r * Math.Cos(start), cy + r * Math.Sin(start)),
            IsClosed = false,
        };
        figure.Segments.Add(new ArcSegment(
            new Point(cx + r * Math.Cos(end), cy + r * Math.Sin(end)),
            new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        return geo;
    }

    // ------------------------------------------------------------------ 交互

    void BuildAddMenu()
    {
        AddMenu.Children.Clear();
        foreach (var (type, label, view, cols) in Catalog)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)Application.Current.Resources["BtnGhost"],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 2),
            };
            btn.Click += (_, _) =>
            {
                _state.Settings.Ui.Cards.Add(new CardPref
                {
                    Type = type,
                    View = view,
                    Cols = cols,
                    Source = type == "project" ? DefaultProjectId() : "",
                });
                _state.SaveSettings();
                AddPop.IsOpen = false;
                Load();
            };
            AddMenu.Children.Add(btn);
        }
    }

    void Add_Click(object sender, RoutedEventArgs e) => AddPop.IsOpen = true;

    /// <summary>项目页的「主页」：回本机概览，侧栏主页行点亮。</summary>
    void BackHome_Click(object sender, RoutedEventArgs e) => ShowHome();

    /// <summary>外部启动的壳：切到它的窗口（PAD 不托管它，只负责把人送过去）。</summary>
    void ActivateShell_Click(object sender, RoutedEventArgs e)
    {
        var facts = _scopeFacts;
        if (facts is null || facts.Pids.Count == 0) return;
        if (!ShellActivate.Try(facts.Pids))
            MessageBox.Show("没有找到可切换的窗口（进程可能在后台或已退出）。",
                "PAD", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>PAD 自己起的 web 节点：账本里存的是带 token 的完整 URL，直接打开。</summary>
    void OpenPadUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = _scopeFacts?.PadUrl;
        if (!string.IsNullOrWhiteSpace(url)) Reveal.Url(url!);
    }

    /// <summary>项目页的「版本设置」：内层页返回时回到管理页这个项目（不是启动页）。</summary>
    void Versions_Click(object sender, RoutedEventArgs e) =>
        _host.ShowInner("instance", "版本设置", "manage");

    /// <summary>在跑 = 停止（照 confirmStop 问一句）；没在跑 = 去启动页选 profile 启动。</summary>
    void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_project is not null)
        {
            var projInst = ResolveProjectInstance(_project);
            var projRun = projInst is null ? null : _state.Running.FirstOrDefault(r => r.Entry.Instance == projInst.Id);
            if (projRun is not null) StopProject(_project);
            else _ = StartProjectAsync(_project);
            return;
        }
        var inst = _scope;
        if (inst is null) return;
        var run = _scopeRun ?? _state.Running.FirstOrDefault(r => r.Entry.Instance == inst.Id);
        if (run is null)
        {
            _host.ShowTab("launch");
            return;
        }

        if (_state.Settings.Other.ConfirmStop)
        {
            var ask = MessageBox.Show(
                $"停止 {run.InstanceName} · {run.Profile}？",
                "PAD", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (ask != MessageBoxResult.OK) return;
        }
        _state.Runner.Stop(run.Entry.Instance, run.Profile);
        _state.RefreshRunning();
        Load();
    }

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_project is not null)
        {
            OpenProjectMenu();
            return;
        }
        _editing = !_editing;
        EditBtn.Content = _editing ? "完成" : "编辑";
        Load();
    }
}
