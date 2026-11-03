using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 安装概念（D107）· 预检 → 安装报告：两阶段同窗。
/// 阶段 1 展示 <see cref="PreflightView"/>（内核/下架/豁免/冲突 + 两个授权复选框）；
/// 阶段 2 展示 <see cref="ReportView"/>（状态、统计、跳过/失败要点、修复/打开实例/重新导出）。
/// 只负责展示与交互；预检/授权/安装由调用方走 Core API，本窗口不碰 Packs.cs。
/// </summary>
public partial class PackPreflightWindow : Window
{
    /// <summary>检查/报告的一行：Tone = ok|warn|bad|info|none。</summary>
    public sealed record Line
    {
        public string Marker { get; init; } = "✓";
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        public string BadgeText { get; init; } = "";
        public string Tone { get; init; } = "none";
        public bool HasDetail => Detail.Length > 0;
        public bool HasBadge => BadgeText.Length > 0;
    }

    public sealed record Stat(int Count, string Label);

    /// <summary>阶段 1 数据：预检结果 + 需要用户决定的选项。</summary>
    public sealed record PreflightView
    {
        public string PackLabel { get; init; } = "";
        public string InstanceLabel { get; init; } = "";
        /// <summary>概览行（内核匹配 / 插件分类汇总 / 版本冲突）。</summary>
        public IReadOnlyList<Line> Lines { get; init; } = [];
        /// <summary>逐插件明细（needs-exemption / missing / version-conflict）。</summary>
        public IReadOnlyList<Line> DetailLines { get; init; } = [];
        public string AcceptRiskText { get; init; } = "";
        public bool ShowAcceptRisk { get; init; }
        public bool AcceptRiskChecked { get; init; }
        public string SkipText { get; init; } = "";
        public bool ShowSkip { get; init; }
        public bool SkipChecked { get; init; }
        public string StartText { get; init; } = "开始安装";
        public bool CanStart { get; init; } = true;
        public string Hint { get; init; } = "只取消这一个包，不动实例其它内容。";
    }

    /// <summary>阶段 2 数据：安装报告（不静默，失败原样列）。</summary>
    public sealed record ReportView
    {
        public string PackLabel { get; init; } = "";
        public string InstanceLabel { get; init; } = "";
        public string StatusText { get; init; } = "";
        public string StatusTone { get; init; } = "ok";
        public string GapText { get; init; } = "";
        public bool ShowGap { get; init; }
        public string LedgerText { get; init; } = "";
        public IReadOnlyList<Stat> Stats { get; init; } = [];
        public IReadOnlyList<Line> Lines { get; init; } = [];
        public string Hint { get; init; } = "";
        public bool ShowRepair { get; init; }
    }

    /// <summary>开始安装（acceptRisk / skipMissing 来自两个复选框）。</summary>
    public event Action<bool, bool>? StartRequested;
    public event Action? CancelRequested;
    public event Action? RepairRequested;
    public event Action? OpenInstanceRequested;
    public event Action? ReExportRequested;

    public PackPreflightWindow()
    {
        InitializeComponent();
    }

    /// <summary>切到阶段 1（安装前检查）。</summary>
    public void ShowPreflight(PreflightView view)
    {
        StageCheck.Visibility = Visibility.Visible;
        StageReport.Visibility = Visibility.Collapsed;
        CheckButtons.Visibility = Visibility.Visible;
        ReportButtons.Visibility = Visibility.Collapsed;
        StageTitle.Text = "安装前检查";
        StageSub.Text = "先算清楚会发生什么，再由你决定。";
        Title = "安装前检查 · " + view.PackLabel;
        PackLabel.Text = view.PackLabel;
        InstanceLabel.Text = view.InstanceLabel;
        CheckLines.ItemsSource = view.Lines;
        DetailLines.ItemsSource = view.DetailLines;
        DetailLines.Visibility = view.DetailLines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AcceptRiskBox.Content = view.AcceptRiskText;
        AcceptRiskBox.Visibility = view.ShowAcceptRisk ? Visibility.Visible : Visibility.Collapsed;
        AcceptRiskBox.IsChecked = view.ShowAcceptRisk && view.AcceptRiskChecked;
        SkipMissingBox.Content = view.SkipText;
        SkipMissingBox.Visibility = view.ShowSkip ? Visibility.Visible : Visibility.Collapsed;
        SkipMissingBox.IsChecked = view.ShowSkip && view.SkipChecked;
        Options.Visibility = view.ShowAcceptRisk || view.ShowSkip ? Visibility.Visible : Visibility.Collapsed;
        StartBtn.Content = view.StartText;
        StartBtn.IsEnabled = view.CanStart;
        _canStart = view.CanStart;
        FooterHint.Text = view.Hint;
    }

    /// <summary>切到阶段 2（安装报告）。</summary>
    public void ShowReport(ReportView view)
    {
        StageCheck.Visibility = Visibility.Collapsed;
        StageReport.Visibility = Visibility.Visible;
        CheckButtons.Visibility = Visibility.Collapsed;
        ReportButtons.Visibility = Visibility.Visible;
        StageTitle.Text = "安装报告";
        StageSub.Text = "结果都写进账本；缺件随时可修复，不会静默失败。";
        Title = "安装报告 · " + view.PackLabel;
        PackLabel.Text = view.PackLabel;
        InstanceLabel.Text = view.InstanceLabel;
        StatusPill.Content = view.StatusText;
        StatusPill.DataContext = new { Tone = view.StatusTone };
        GapPill.Content = view.GapText;
        GapPill.DataContext = new { Tone = "warn" };
        GapPill.Visibility = view.ShowGap ? Visibility.Visible : Visibility.Collapsed;
        LedgerText.Text = view.LedgerText;
        StatTiles.ItemsSource = view.Stats;
        ReportLines.ItemsSource = view.Lines;
        ReportHint.Text = view.Hint;
        RepairBtn.Visibility = view.ShowRepair ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>安装中：锁住两个阶段的动作，避免双击/重复提交。</summary>
    public void SetBusy(bool busy)
    {
        StartBtn.IsEnabled = !busy && _canStart;
        AcceptRiskBox.IsEnabled = !busy;
        SkipMissingBox.IsEnabled = !busy;
        RepairBtn.IsEnabled = !busy;
    }

    bool _canStart = true;

    void Start_Click(object sender, RoutedEventArgs e)
    {
        var acceptRisk = AcceptRiskBox.IsChecked == true;
        var skipMissing = SkipMissingBox.IsChecked == true;
        StartRequested?.Invoke(acceptRisk, skipMissing);
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke();
        Close();
    }

    void Repair_Click(object sender, RoutedEventArgs e) => RepairRequested?.Invoke();

    void Done_Click(object sender, RoutedEventArgs e) => Close();

    void OpenInstance_Click(object sender, RoutedEventArgs e) => OpenInstanceRequested?.Invoke();

    void ReExport_Click(object sender, RoutedEventArgs e) => ReExportRequested?.Invoke();

    protected override void OnClosed(EventArgs e) => base.OnClosed(e);

    /// <summary>离屏把本窗口渲染成 PNG（--shot-page pack-preflight 用；不经屏幕捕获）。</summary>
    public void RenderTo(string path)
    {
        try
        {
            UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(this);
            var w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            var h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                PixelFormats.Pbgra32);
            rtb.Render(this);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            png.Save(fs);
            File.WriteAllText(path + ".txt",
                $"logical {ActualWidth}x{ActualHeight}\ndpi {dpi.DpiScaleX}x{dpi.DpiScaleY}\npixels {w}x{h}\n");
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".txt", ex.ToString()); } catch { /* 截图失败不炸 */ }
        }
    }

    /// <summary>固定 demo 数据：阶段 1（与原型 03 左卡对齐）。</summary>
    public static PreflightView DemoPreflight(string pack = "Test 0.1.0", string instance = "sisyphus")
    {
        return new PreflightView
        {
            PackLabel = pack,
            InstanceLabel = instance,
            Lines =
            [
                new Line
                {
                    Marker = "✓", Tone = "ok", Title = "内核匹配", BadgeText = "通过",
                    Detail = "包钉 0.2.1-rc.2 = 实例 0.2.1-rc.2",
                },
                new Line
                {
                    Marker = "✓", Tone = "ok",
                    Title = "插件 10 个：8 个可装 · 1 个需授权 · 1 个已下架",
                    Detail = "下面两条需要你决定，其余不会打断安装。",
                },
                new Line
                {
                    Marker = "✓", Tone = "ok", Title = "版本冲突", BadgeText = "无",
                    Detail = "实例里已有的插件没有和包声明冲突的版本。",
                },
            ],
            DetailLines =
            [
                new Line
                {
                    Marker = "!", Tone = "warn", BadgeText = "需授权",
                    Title = "dsh-find-skill@0.2.1 与内核不兼容",
                    Detail = "它的 peers 要求 @deepseek-ai/dsh-* ^0.1.0-rc.6，当前内核 0.2.1-rc.2。"
                             + "可写 DSH 官方 allow-version 豁免，风险：可能崩/坏数据。",
                },
                new Line
                {
                    Marker = "✕", Tone = "bad", BadgeText = "不可装",
                    Title = "@yuxianglin/dsh-bridge-browser 已从 npm 下架",
                    Detail = "npm registry 404，装不上。跳过它这个包会缺 1 个功能，但其余 9 个可用。",
                },
            ],
            AcceptRiskText = "接受 1 个插件的兼容风险（写 allow-version 豁免）",
            ShowAcceptRisk = true,
            AcceptRiskChecked = false,
            SkipText = "跳过 1 个已下架插件，包标「不完整」（之后可修复/重新导出）",
            ShowSkip = true,
            SkipChecked = false,
            StartText = "开始安装",
            Hint = "只取消这一个包，不动实例其它内容。",
        };
    }

    /// <summary>固定 demo 数据：阶段 2（与原型 03 右卡对齐）。</summary>
    public static ReportView DemoReport(string pack = "Test", string instance = "sisyphus")
    {
        return new ReportView
        {
            PackLabel = pack,
            InstanceLabel = instance,
            StatusText = "已装到 " + instance,
            StatusTone = "ok",
            GapText = "不完整 · 缺 1 个",
            ShowGap = true,
            LedgerText = "账本已写入 pack-ledger.json",
            Stats =
            [
                new Stat(9, "新增插件"),
                new Stat(0, "保留"),
                new Stat(1, "风险豁免"),
                new Stat(1, "跳过"),
                new Stat(0, "失败"),
            ],
            Lines =
            [
                new Line
                {
                    Marker = "✓", Tone = "ok", Title = "9 个插件已装进 web profile",
                    Detail = "含 dsh-find-skill（已接受风险，豁免只对 0.2.1-rc.2 生效）；"
                             + "投影 + 管理器 + 内容白名单均已就绪。",
                },
                new Line
                {
                    Marker = "✕", Tone = "bad", Title = "跳过 @yuxianglin/dsh-bridge-browser",
                    Detail = "npm 上已下架。包标记「不完整」，以后包作者重新发布或你重新导出后可以「修复」补上。",
                },
                new Line
                {
                    Marker = "✓", Tone = "ok", Title = "实例里其它内容没有被动过",
                    Detail = "只写这个实例的投影/账本/profile 插件，settings、其它实例不受影响。",
                },
            ],
            Hint = "关闭这个窗口不会撤销安装；缺件可在实例页「整合包」块点修复。",
            ShowRepair = true,
        };
    }

    // ---- 真实 API → 窗口 VM + 共用安装流程（D107 task-12）------------------

    static string KernelText(string? version) => string.IsNullOrWhiteSpace(version) ? "未声明" : version!;

    /// <summary>PackPreflight → 阶段 1「安装前检查」视图。</summary>
    public static PreflightView ToView(PackPreflight p, string instanceName, string profile,
        bool checkOnly = false)
    {
        var lines = new List<Line>
        {
            new()
            {
                Marker = p.KernelMatches ? "✓" : "!", Tone = p.KernelMatches ? "ok" : "warn",
                Title = "内核匹配", BadgeText = p.KernelMatches ? "通过" : "不一致",
                Detail = $"包钉 {KernelText(p.PackKernel)} = 实例 {KernelText(p.InstanceKernel)}",
            },
            new()
            {
                Marker = p.ManagerInstalled ? "✓" : "!",
                Tone = p.ManagerInstalled ? "ok" : "warn",
                Title = p.ManagerInstalled ? "整合包管理器已装" : "整合包管理器未装（安装时自动补）",
                BadgeText = p.ManagerInstalled ? "通过" : "自动",
            },
        };
        var parts = new List<string>();
        if (p.OkCount > 0) parts.Add($"{p.OkCount} 个可装");
        if (p.ExemptionCount > 0) parts.Add($"{p.ExemptionCount} 个需授权");
        if (p.MissingCount > 0) parts.Add($"{p.MissingCount} 个已下架/缺失");
        if (p.ConflictCount > 0) parts.Add($"{p.ConflictCount} 个版本冲突");
        if (p.UnknownCount > 0) parts.Add($"{p.UnknownCount} 个未知（不阻塞）");
        lines.Add(new Line
        {
            Marker = p.NeedsDecision ? "!" : "✓",
            Tone = p.NeedsDecision ? "warn" : "ok",
            Title = $"插件 {p.Plugins.Count} 个：" + (parts.Count > 0 ? string.Join(" · ", parts) : "全部可装"),
            Detail = p.NeedsDecision
                ? "下面逐条列出需要你决定的项；其余不会打断安装。"
                : "不需要额外授权，可以直接安装。",
        });
        lines.Add(new Line
        {
            Marker = p.ConflictCount > 0 ? "!" : "✓",
            Tone = p.ConflictCount > 0 ? "warn" : "ok",
            Title = "版本冲突",
            BadgeText = p.ConflictCount > 0 ? $"{p.ConflictCount} 个" : "无",
            Detail = p.ConflictCount > 0
                ? "实例里已有的插件与包声明的版本不一致；安装时按你选择的策略对齐或保留。"
                : "实例里已有的插件没有和包声明冲突的版本。",
        });
        foreach (var warning in p.Warnings.Take(3))
            lines.Add(new Line { Marker = "!", Tone = "warn", Title = warning });
        var details = p.Plugins.Where(x => !x.IsOk).Select(PluginLine).ToList();
        return new PreflightView
        {
            PackLabel = $"{p.Title} {p.Version}".Trim(),
            InstanceLabel = instanceName + (profile.Length > 0 ? " · " + profile : ""),
            Lines = lines,
            DetailLines = details,
            AcceptRiskText = $"接受 {p.ExemptionCount} 个插件的兼容风险（写 DSH allow-version 豁免）",
            ShowAcceptRisk = p.ExemptionCount > 0,
            AcceptRiskChecked = false,
            SkipText = $"跳过 {p.MissingCount} 个已下架/缺失插件，包标「不完整」（之后可修复/重新导出）",
            ShowSkip = p.MissingCount > 0,
            SkipChecked = false,
            StartText = checkOnly ? "仅检查（dry-run）" : p.NeedsDecision ? "按勾选开始安装" : "开始安装",
            CanStart = !checkOnly,
            Hint = checkOnly
                ? "这是 dry-run：只看检查结果，不写账本、不动实例。"
                : "只动这一个包；失败/跳过都会进报告，不静默。",
        };
    }

    static Line PluginLine(PreflightPlugin plugin)
    {
        var name = string.IsNullOrWhiteSpace(plugin.Name) ? plugin.Spec : plugin.Name;
        var version = string.IsNullOrWhiteSpace(plugin.LatestVersion) ? "" : "@" + plugin.LatestVersion;
        return plugin.Status switch
        {
            PackPluginStatus.NeedsExemption => new Line
            {
                Marker = "!", Tone = "warn", BadgeText = "需授权",
                Title = $"{name}{version} 与内核不兼容",
                Detail = (plugin.Requirement.Length > 0 ? "它的要求：" + plugin.Requirement + "。" : "")
                         + "可写 DSH 官方 allow-version 豁免（只对当前内核精确版本生效），风险：可能崩/坏数据。",
            },
            PackPluginStatus.Missing => new Line
            {
                Marker = "✕", Tone = "bad", BadgeText = "不可装",
                Title = $"{name}{version} 在 npm 上找不到/已下架",
                Detail = "跳过它这个包会缺 1 个功能（包标「不完整」，之后可修复/重新导出）。",
            },
            PackPluginStatus.Unknown => new Line
            {
                Marker = "?", Tone = "info", BadgeText = "未知",
                Title = $"{name}{version} 的元数据没查到",
                Detail = "网络/registry 问题，装的时候再解析；不会因此阻断安装。",
            },
            PackPluginStatus.VersionConflict => new Line
            {
                Marker = "!", Tone = "warn", BadgeText = "版本冲突",
                Title = $"{name} 版本冲突：包要 {plugin.Requested}，实例是 {plugin.InstalledVersion}",
                Detail = "安装时会让你选：升到包版本 / 保留现有（包标不完整）/ 取消。",
            },
            _ => new Line { Marker = "✓", Tone = "ok", Title = name },
        };
    }

    /// <summary>PackInstallReport → 阶段 2「安装报告」视图。</summary>
    public static ReportView ToView(PackInstallReport report, string instanceName)
    {
        var state = report.State;
        var incomplete = state == "incomplete" || report.Skipped.Count > 0
                         || report.Failures.Any(f => f.Required);
        var missing = report.Skipped.Count + report.Failures.Count(f => f.Required);
        var lines = new List<Line>();
        if (report.Added.Count > 0)
            lines.Add(new Line
            {
                Marker = "✓", Tone = "ok",
                Title = $"{report.Added.Count} 个插件已装进 {report.Profile} profile",
                Detail = report.Exempted.Count > 0
                    ? "含已接受风险的插件：" + string.Join("、", report.Exempted)
                      + "（豁免只对当前内核精确版本生效）；投影 + 管理器 + 内容白名单均已就绪。"
                    : "投影 + 管理器 + 内容白名单均已就绪。",
            });
        if (report.Kept.Count > 0)
            lines.Add(new Line
            {
                Marker = "i", Tone = "info", BadgeText = "保留",
                Title = $"{report.Kept.Count} 个插件保留现有版本（未按包版本对齐）",
                Detail = string.Join("、", report.Kept.Take(8)),
            });
        if (report.Exempted.Count > 0)
            lines.Add(new Line
            {
                Marker = "✓", Tone = "ok", BadgeText = "已豁免",
                Title = "allow-version 豁免已写入",
                Detail = string.Join("、", report.Exempted) + "（只对当前内核精确版本生效）",
            });
        foreach (var skipped in report.Skipped.Take(6))
            lines.Add(new Line
            {
                Marker = "✕", Tone = "bad", BadgeText = "跳过",
                Title = "跳过 " + skipped,
                Detail = "npm 上找不到/已下架；包标「不完整」，之后可以修复或重新导出补上。",
            });
        foreach (var failure in report.Failures.Take(6))
            lines.Add(new Line { Marker = "✕", Tone = "bad", Title = failure.Text });
        foreach (var warning in report.Warnings.Take(4))
            lines.Add(new Line { Marker = "!", Tone = "warn", Title = warning });
        lines.Add(new Line
        {
            Marker = "✓", Tone = "ok", Title = "实例里其它内容没有被动过",
            Detail = "只写这个实例的投影/账本/profile 插件；settings、其它实例不受影响。",
        });
        var statusText = state switch
        {
            "installed" => $"已装到 {instanceName}",
            "incomplete" => $"已装到 {instanceName}",
            "dry-run" => "仅检查（未安装）",
            "needs-decision" => "需要你决定",
            _ => string.IsNullOrWhiteSpace(state) ? "完成" : state,
        };
        return new ReportView
        {
            PackLabel = string.IsNullOrWhiteSpace(report.Title) ? report.PackId : report.Title,
            InstanceLabel = instanceName,
            StatusText = statusText,
            StatusTone = state == "installed" ? "ok" : state == "incomplete" ? "warn" : "info",
            GapText = incomplete ? $"不完整 · 缺 {missing} 个" : "",
            ShowGap = incomplete,
            LedgerText = "账本已写入 pack-ledger.json",
            Stats =
            [
                new Stat(report.Added.Count, "新增插件"),
                new Stat(report.Kept.Count, "保留"),
                new Stat(report.Exempted.Count, "风险豁免"),
                new Stat(report.Skipped.Count, "跳过"),
                new Stat(report.Failures.Count, "失败"),
            ],
            Lines = lines,
            Hint = incomplete
                ? "缺件可在实例页「整合包」块点「修复」补回；也可以重新导出这个包。"
                : "关闭这个窗口不会撤销安装。",
            ShowRepair = incomplete,
        };
    }

    /// <summary>
    /// 共用安装流程：PreflightPack → 阶段 1（勾选授权）→ InstallPackToInstance → 阶段 2（报告）。
    /// 版本页「装到当前实例」与实例页包库选择器共用；checkOnly = 只检查（DryRun，不写盘）。
    /// </summary>
    public static async Task RunInstallFlowAsync(Window owner, Packs packs, Instance inst, string profile,
        PackCard card, IProgress<string>? log, Action? afterApplied, Action? openInstance = null,
        Func<Task>? reExport = null, Action<string, bool>? toast = null, bool checkOnly = false)
    {
        PackPreflight preflight;
        try
        {
            preflight = await packs.PreflightPack(inst, profile, card, log, CancellationToken.None);
        }
        catch (Exception ex)
        {
            toast?.Invoke(PadError.Describe(ex), true);
            return;
        }

        var instanceName = string.IsNullOrWhiteSpace(inst.Name) ? inst.Id : inst.Name;
        var win = new PackPreflightWindow { Owner = owner };
        win.ShowPreflight(ToView(preflight, instanceName, profile, checkOnly));
        if (checkOnly)
        {
            win.ShowDialog();
            return;
        }

        var started = false;
        win.StartRequested += async (acceptRisk, skipMissing) =>
        {
            if (started) return;
            started = true;
            win.SetBusy(true);
            try
            {
                var report = await packs.InstallPackToInstance(inst, profile, card,
                    new PackInstallOptions { AcceptRisk = acceptRisk, SkipMissing = skipMissing },
                    log, CancellationToken.None);
                win.ShowReport(ToView(report, instanceName));
                win.SetBusy(false);
                afterApplied?.Invoke();
                if (!string.IsNullOrWhiteSpace(report.Text))
                    toast?.Invoke(report.Text, !report.Ok && report.State != "incomplete");
            }
            catch (Exception ex)
            {
                started = false;
                win.SetBusy(false);
                toast?.Invoke(PadError.Describe(ex), true);
            }
        };
        win.RepairRequested += async () =>
        {
            try
            {
                var repaired = await packs.RepairPack(inst, preflight.PackId, profile, log,
                    CancellationToken.None);
                afterApplied?.Invoke();
                toast?.Invoke(repaired.Text, repaired.StillMissing.Count > 0);
                win.Close();
            }
            catch (Exception ex)
            {
                toast?.Invoke(PadError.Describe(ex), true);
            }
        };
        win.OpenInstanceRequested += () =>
        {
            openInstance?.Invoke();
            win.Close();
        };
        win.ReExportRequested += async () =>
        {
            try { if (reExport is not null) await reExport(); }
            catch (Exception ex) { toast?.Invoke(PadError.Describe(ex), true); }
        };
        win.ShowDialog();
    }
}
