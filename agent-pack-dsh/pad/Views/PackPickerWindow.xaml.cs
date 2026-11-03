using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 安装概念（D107）· 包库选择器：从本机包库挑一个整合包装进目标实例。
/// 只负责展示与交互；预检/安装由调用方（InstanceView）走 Core API，本窗口不碰 Packs.cs。
/// 两阶段流程由 <see cref="PackPreflightWindow"/> 承接；"--shot-page pack-picker" 用离屏渲染。
/// </summary>
public partial class PackPickerWindow : Window
{
    /// <summary>预检徽标（行内小 pill）：Tone = ok|warn|bad|info|none。</summary>
    public sealed record Badge(string Text, string Tone = "none");

    /// <summary>包库一行（由调用方从 PackCard + PackPreflight 映射）。</summary>
    public sealed record Row
    {
        public string Key { get; init; } = "";
        public string IconPath { get; init; } = "";
        public string Title { get; init; } = "";
        public string VersionText { get; init; } = "";
        /// <summary>「内核 0.2.1-rc.2 · 插件 10 · library/packs/Test-0.1.0.pack.zip」。</summary>
        public string SubText { get; init; } = "";
        public IReadOnlyList<Badge> Badges { get; init; } = [];
        public string ActionText { get; init; } = "安装";
        public bool CanAct { get; init; } = true;
        /// <summary>修复入口（已装当前实例且 incomplete）。</summary>
        public bool Repair { get; init; }
    }

    readonly List<Row> _all = [];

    public event Action<Row>? RowActionRequested;
    public event Action? FromFileRequested;
    public event Action? Cancelled;

    public PackPickerWindow(string instanceLabel = "", string profile = "")
    {
        InitializeComponent();
        SetTarget(instanceLabel, profile);
    }

    public void SetTarget(string? instanceLabel, string? profile)
    {
        var name = string.IsNullOrWhiteSpace(instanceLabel) ? "当前实例" : instanceLabel.Trim();
        var prof = string.IsNullOrWhiteSpace(profile) ? "" : " · " + profile.Trim();
        TargetText.Text = name + prof;
        Title = "装整合包 · " + name;
    }

    /// <summary>整批换行（也会被搜索框过滤）；total = 本机包库总数（页脚计数）。</summary>
    public void SetRows(IEnumerable<Row> rows, int? total = null)
    {
        _all.Clear();
        _all.AddRange(rows);
        SetBusy(false);
        CountText.Text = $"本机包库 {(total ?? _all.Count)} 个";
        ApplyFilter();
    }

    /// <summary>加载中：列表换骨架，搜索框禁用。</summary>
    public void SetBusy(bool busy)
    {
        SkeletonRows.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ListScroll.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;
        SearchBox.IsEnabled = !busy;
    }

    public void SetEmpty(string text)
    {
        SetBusy(false);
        PackRows.ItemsSource = null;
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
    }

    /// <summary>后台预检完成后按 Key 替换一行（保持当前搜索过滤）。</summary>
    public void UpdateRow(Row row)
    {
        var index = _all.FindIndex(r => string.Equals(r.Key, row.Key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _all[index] = row;
        else _all.Add(row);
        if (SkeletonRows.Visibility == Visibility.Visible) SetBusy(false);
        ApplyFilter();
    }

    /// <summary>
    /// 真实数据：本机包库 Catalog（异步）→ 先出列表 → 逐行后台 PreflightPack（并发 3）回填徽标/动作。
    /// 版本页「装到当前实例」入口和 --shot-page pack-picker 共用。
    /// </summary>
    public async Task LoadAsync(Packs packs, Instance inst, string profile, string launcherRoot)
    {
        SetBusy(true);
        List<PackCard> cards;
        try
        {
            cards = await packs.Catalog(AppContext.BaseDirectory, null, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetEmpty("包库读不出来：" + PadError.Describe(ex));
            return;
        }
        if (cards.Count == 0)
        {
            SetEmpty("本机包库还没有整合包：可以「从文件…」导入一个。");
            return;
        }
        // 装整合包要求包声明了内核（没有 dsh.version 的包没有可钉内核，安装链无从开始）。
        cards = cards.Where(c => !string.IsNullOrWhiteSpace(c.Kernel)).ToList();
        if (cards.Count == 0)
        {
            SetEmpty("包库里的整合包都没写内核（dsh.version），暂时不能安装。");
            return;
        }
        SetRows(cards.Select(card => BaseRow(card, launcherRoot, badge: null)), cards.Count);
        var gate = new SemaphoreSlim(3);
        var jobs = cards.Select(async card =>
        {
            await gate.WaitAsync();
            try
            {
                var preflight = await packs.PreflightPack(inst, profile, card, null, CancellationToken.None);
                Dispatcher.Invoke(() => UpdateRow(FromPreflight(card, preflight, launcherRoot)));
            }
            catch
            {
                Dispatcher.Invoke(() => UpdateRow(BaseRow(card, launcherRoot,
                    new Badge("预检失败（点安装再试）", "warn"))));
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(jobs);
        gate.Dispose();
    }

    static Row BaseRow(PackCard card, string launcherRoot, Badge? badge) => new()
    {
        Key = card.Path,
        IconPath = PackIcon.Resolve(launcherRoot, card.Path) ?? "",
        Title = string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title,
        VersionText = card.Version,
        SubText = $"内核 {(string.IsNullOrWhiteSpace(card.Kernel) ? "缺失" : card.Kernel.Trim())}"
                  + $" · 插件 {card.Components?.Plugin ?? 0} · {ShortPath(card.Path)}",
        Badges = badge is null ? [new Badge("检查中…", "info")] : [badge],
        ActionText = "安装",
    };

    static Row FromPreflight(PackCard card, PackPreflight preflight, string launcherRoot)
    {
        var badges = new List<Badge>();
        var incomplete = string.Equals(preflight.LedgerState, PackLedger.StateIncomplete,
                             StringComparison.OrdinalIgnoreCase) || preflight.MissingCount > 0;
        if (preflight.AlreadyInstalled)
            badges.Add(incomplete
                ? new Badge($"当前实例缺 {preflight.MissingCount} 个 · 可修复", "warn")
                : new Badge("已装到当前实例", "ok"));
        if (preflight.ExemptionCount > 0)
            badges.Add(new Badge($"{preflight.ExemptionCount} 个需授权", "warn"));
        if (preflight.MissingCount > 0)
            badges.Add(new Badge($"{preflight.MissingCount} 个已下架/缺失", "bad"));
        if (preflight.ConflictCount > 0)
            badges.Add(new Badge($"{preflight.ConflictCount} 个版本冲突", "warn"));
        if (preflight.UnknownCount > 0)
            badges.Add(new Badge($"{preflight.UnknownCount} 个未知（不阻塞）", "info"));
        if (badges.Count == 0)
            badges.Add(new Badge(preflight.Plugins.Count == 0 ? "无插件" : "可装", "ok"));
        var repair = preflight.AlreadyInstalled && incomplete;
        return new Row
        {
            Key = card.Path,
            IconPath = PackIcon.Resolve(launcherRoot, card.Path) ?? "",
            Title = string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title,
            VersionText = card.Version,
            SubText = $"内核 {(string.IsNullOrWhiteSpace(card.Kernel) ? "缺失" : card.Kernel.Trim())}"
                      + $" · 插件 {preflight.Plugins.Count}"
                      + (preflight.AlreadyInstalled ? " · 已装到当前实例" : "")
                      + $" · {ShortPath(card.Path)}",
            Badges = badges,
            ActionText = repair ? "修复" : preflight.NeedsDecision ? "检查并安装" : "安装",
            Repair = repair,
        };
    }

    static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var parts = path!.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 3 ? path : string.Join("\\", parts[^3..]);
    }

    void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    void ApplyFilter()
    {
        var query = (SearchBox?.Text ?? "").Trim();
        var rows = query.Length == 0
            ? _all
            : _all.Where(r =>
                r.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.SubText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.VersionText.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        // 必须给新 List：预检回填是「换行对象」，同一个 List 引用不会让 ItemsControl 重建容器。
        PackRows.ItemsSource = rows.ToList();
        var empty = rows.Count == 0;
        EmptyText.Text = _all.Count == 0 ? "本机包库没有整合包。" : "没有匹配的整合包。";
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Row row }) RowActionRequested?.Invoke(row);
    }

    void FromFile_Click(object sender, RoutedEventArgs e)
    {
        FromFileRequested?.Invoke();
        Close();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Cancelled?.Invoke();
    }

    /// <summary>离屏把本窗口渲染成 PNG（--shot-page pack-picker 用；不经屏幕捕获）。</summary>
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

    /// <summary>
    /// 固定 demo 数据（--shot / 设计走查用；接线后由真实包库+预检替换）。
    /// 与原型 02 对齐：一条 需授权+下架（检查并安装）、一条可装、一条已装缺件（修复）、一条无图标。
    /// </summary>
    public void ShowDemo(string instanceLabel = "sisyphus", string profile = "web")
    {
        SetTarget(instanceLabel, profile);
        SetRows(
        [
            new Row
            {
                Key = "Test-0.1.0",
                Title = "Test",
                VersionText = "0.1.0",
                SubText = "内核 0.2.1-rc.2 · 插件 10 · library/packs/Test-0.1.0.pack.zip",
                Badges =
                [
                    new Badge("1 个插件已下架", "bad"),
                    new Badge("1 个需授权", "warn"),
                    new Badge("8 个可装", "ok"),
                ],
                ActionText = "检查并安装",
            },
            new Row
            {
                Key = "Verify-Probe-0.1.0",
                Title = "Verify Probe",
                VersionText = "0.1.0",
                SubText = "内核 0.2.1-rc.2 · 插件 1 · library/packs/Verify-Probe-0.1.0.pack.zip",
                Badges = [new Badge("可装", "ok")],
                ActionText = "安装",
            },
            new Row
            {
                Key = "Shared-B-0.1.0",
                Title = "Shared B",
                VersionText = "0.1.0",
                SubText = "内核 0.2.1-rc.2 · 插件 1 · 已装到当前实例，缺 1 个插件",
                Badges = [new Badge("当前实例缺 1 个 · 可修复", "warn")],
                ActionText = "修复",
                Repair = true,
            },
            new Row
            {
                Key = "Plain-Pack-0.1.0",
                Title = "Plain Pack",
                VersionText = "0.1.0",
                SubText = "内核 0.2.1-rc.2 · 插件 1 · 包内没有图标",
                Badges = [new Badge("可装", "ok")],
                ActionText = "安装",
            },
        ], total: 4);
    }
}
