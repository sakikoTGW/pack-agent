using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Pad.Core;
using IOPath = System.IO.Path;

namespace Pad.Views;

public partial class VersionsView : UserControl, IRefreshable
{
    readonly MainWindow _win;
    readonly AppState _state = AppState.Current;
    public bool ShowHidden { get; private set; }

    Inventory? _detected;
    int _detectGen;
    List<VersionChoice> _versionChoices = [];
    /// <summary>容器列表在重建/恢复选中时，别让 SelectionChanged 再反向触发实例重建（会互相递归）。</summary>
    bool _containerSync;

    public VersionsView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        DataContext = _state;
        ((CollectionViewSource)Resources["InstSrc"]).Source = _state.Instances;
        ((CollectionViewSource)Resources["LaunchSrc"]).Source = _state.Launchables;
        RebuildContainerRows();
    }

    public void ToggleHidden()
    {
        ShowHidden = !ShowHidden;
        if (Resources["InstSrc"] is CollectionViewSource src)
            src.View?.Refresh();
    }

    void InstSrc_Filter(object sender, FilterEventArgs e)
    {
        e.Accepted = e.Item is Instance inst &&
            (ShowHidden || inst.Display?.Category is not ("隐藏" or "hidden"));
    }

    public void OnShown()
    {
        _state.Reload();
        if (Resources["InstSrc"] is CollectionViewSource src)
            src.View?.Refresh();
        RebuildContainerRows();
        SyncPinCombo();
        _ = ScanDetectedAsync(force: _detected is null);
        _ = RefreshPacksAsync();
    }

    void InstanceList_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (InstanceList.SelectedItem is Instance inst)
            _state.SelectedInstance = inst;
        if (PinRelease is null) return;
        SyncPinCombo();
        // 容器选中会反过来设置实例；这时不要再重建容器列表（递归 → 栈溢出）。
        if (!_containerSync) RebuildContainerRows();
    }

    void SyncPinCombo()
    {
        if (PinRelease is null) return;
        if (_versionChoices.Count == 0) RefreshVersionChoices();
        var v = _state.SelectedInstance?.Dsh.Version ?? "";
        PinRelease.SelectedItem = _versionChoices.FirstOrDefault(c =>
            string.Equals(c.Version, v, StringComparison.OrdinalIgnoreCase));
    }

    void PinApply_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (PinRelease.SelectedItem is not VersionChoice choice) return;
        if (choice.Release is null)
        {
            _win.Toast($"{choice.Version} 是本机探测到的版本（{choice.Source}），不在 PAD 版本库，不能改钉；" +
                       "要钉它请先在下载页安装同版本", bad: true);
            return;
        }
        if (choice.Version == inst.Dsh.Version) return;
        try
        {
            _state.Launcher.PinInstance(inst.Id, choice.Release.Version);
            _state.Reload();
            SyncPinCombo();
            _win.Toast($"已改钉到 {choice.Version}（组合包未验证）");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Log(string line) => System.Diagnostics.Debug.WriteLine(line);

    async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = Prompt("新建实例", "实例名：");
        if (name is null or { Length: 0 }) return;
        try
        {
            string version;
            var want = _state.Settings.Launch.DefaultRelease;
            if (_state.Releases.FirstOrDefault(r => r.Version == want) is { } pinned)
            {
                version = pinned.Version;
            }
            else if (_state.Releases.FirstOrDefault() is { } first)
            {
                version = first.Version;
            }
            else
            {
                _win.Toast("在装 DSH 发行号");
                version = await _state.Launcher.EnsureRelease(null, CancellationToken.None);
                _state.Reload();
            }
            var inst = _state.Launcher.CreateInstance(name, version);
            _state.Reload();
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == inst.Id);
            _win.ShowInner("instance", "版本设置");
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void Adopt_Click(object sender, RoutedEventArgs e) => _win.AdoptFolder();

    void Download_Click(object sender, RoutedEventArgs e) => _win.ShowTab("download");

    async void ImportPack_Click(object sender, RoutedEventArgs e)
    {
        var packs = new Packs(_state.Launcher);
        if (packs.Unavailable() is { } missing)
        {
            _win.Toast($"{missing.Reason}。{missing.Detail}", bad: true);
            return;
        }
        var dlg = new OpenFileDialog
        {
            Title = "选一个整合包",
            Filter = "整合包 (*.pack.zip;*.pack.json;*.pinst.zip)|*.pack.zip;*.pack.json;*.pinst.zip|所有文件|*.*",
        };
        if (dlg.ShowDialog(_win) != true) return;
        try
        {
            // 先留一份进整合包库：import 成功后 ts 侧可能不再保留原文件，
            // 库里这份让「整合包」列表还能看到它、可再次建实例（PCL 的 modpack 目录语义）。
            try { packs.CollectToLibrary(dlg.FileName); } catch { /* 库拷贝失败不挡 import */ }
            _win.Toast("正在装 " + Path.GetFileName(dlg.FileName));
            await packs.Import(dlg.FileName, new Progress<string>(Log), CancellationToken.None);
            _state.Reload();
            var newest = _state.Instances.OrderByDescending(i => i.Created).FirstOrDefault();
            if (newest is not null) _state.SelectedInstance = newest;
            _win.ShowInner("instance", "版本设置");
            _ = RefreshPacksAsync();
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    // ---- 整合包（插件部分 + 内核）--------------------------------------------

    bool _creatingFromPack;

    /// <summary>worker 上重新收集本机整合包，填 PackCatalog；UI 只显示结果（不摆技术说明）。</summary>
    async Task RefreshPacksAsync()
    {
        PackEmpty.Visibility = Visibility.Collapsed;
        await _state.ReloadPackCatalog();
        var n = _state.PackCatalog.Count;
        var error = _state.PackCatalogError;
        PackEmpty.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (n == 0)
        {
            PackEmptyText.Text = error.Length > 0 ? "整合包读不出来" : "本机没有整合包。";
            PackEmptyText.ToolTip = error.Length > 0 ? error : null;
        }
    }

    void RefreshPackCatalog_Click(object sender, RoutedEventArgs e) => _ = RefreshPacksAsync();

    /// <summary>已建好的包：右键 → 导出成一份文件（拷到用户选的位置，不重新组包）。</summary>
    void ExportPack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var menu = mi.Parent as ContextMenu ?? ItemsControl.ItemsControlFromItemContainer(mi) as ContextMenu;
        if (menu?.PlacementTarget is not FrameworkElement row) return;
        if (row.DataContext is not PackCard card) return;
        if (!File.Exists(card.Path))
        {
            _win.Toast("这个包的文件不见了", bad: true);
            return;
        }
        var isZip = card.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var dlg = new SaveFileDialog
        {
            Title = "导出便携包",
            Filter = isZip ? "便携包 (*.zip)|*.zip" : "便携包 (*.pack.json)|*.pack.json",
            FileName = Path.GetFileName(card.Path),
            InitialDirectory = ExportHome(),
        };
        if (dlg.ShowDialog(_win) != true) return;
        try
        {
            CopyWithRetry(card.Path, dlg.FileName);
            _win.Toast($"已导出：{Path.GetFileName(dlg.FileName)}");
        }
        catch (IOException)
        {
            // 常见：目标文件正被别的程序（资源管理器预览/压缩软件）占着。
            _win.Toast($"目标文件被占用：{Path.GetFileName(dlg.FileName)}。换个位置，或先关掉打开它的程序再试", bad: true);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>目标常被杀软/资源管理器短暂占用；重试几次再判定失败。</summary>
    static void CopyWithRetry(string source, string target)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                return;
            }
            catch (IOException) when (i < 4)
            {
                Thread.Sleep(200);
            }
        }
    }

    static string ExportHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(home, "Downloads");
        return Directory.Exists(downloads) ? downloads : home;
    }

    /// <summary>
    /// 已建好的包也能导出成整合包：找到用这个包的实例；没有就临时建一个，导出完删掉。
    /// 整合包 = PAD 单文件 exe + .pack-launcher（版本库/实例/runtime），解压后打开 PAD 就能跑。
    /// </summary>
    async void ExportPackPortable_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var menu = mi.Parent as ContextMenu ?? ItemsControl.ItemsControlFromItemContainer(mi) as ContextMenu;
        if (menu?.PlacementTarget is not FrameworkElement row) return;
        if (row.DataContext is not PackCard card) return;
        if (string.IsNullOrWhiteSpace(card.Kernel))
        {
            _win.Toast("这个包没写内核，建不了实例，也打不了整合包", bad: true);
            return;
        }

        var prep = await PreparePortableAsync(card);
        if (prep is null) return;
        var (inst, temp) = prep.Value;
        try
        {
            // 整合包：带 PAD + 依赖的单文件（.7z，没有 ZIP64 上限）。
            var ext = PortableExport.SingleFileExtension;
            var dlg = new SaveFileDialog
            {
                Title = "导出整合包",
                Filter = $"整合包 (*{ext})|*{ext}",
                FileName = SafeName(card.Title) + "-" + SafeName(card.Version) + "-整合" + ext,
                InitialDirectory = ExportHome(),
            };
            if (dlg.ShowDialog(_win) != true) return;
            var target = dlg.FileName;
            if (!target.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) target += ext;

            // 按实例固定的暂存目录：取消/中断后保留，下次导出直接续传。
            var tmp = PortableExport.StageDir(inst!.Id);
            try
            {
                _win.Progress("正在估算整合包大小…");
                var estimate = await Task.Run(() =>
                    PortableExport.Estimate(_state.Launcher, card.Kernel!, inst!.Id));
                _win.Progress($"即将打整合包（约 {estimate / (1024 * 1024)} MB）…");
                var kitDir = Path.Combine(tmp, "kit");
                var job = await PortableExport.ExportAsync(_win, _state.Launcher, kitDir, inst!.Id,
                    "打整合包", estimate);
                try
                {
                    await PortableExport.PackAsync(_win, _state.Launcher, kitDir, target, estimate, job);
                    _state.Launcher.FinishJob(job.Id, "done");
                    await Task.Run(() => PortableExport.ClearStage(inst!.Id)); // 成功后才清续传缓存（后台删，别卡 UI）
                }
                catch
                {
                    if (_state.Launcher.ListJobs().FirstOrDefault(j => j.Id == job.Id)?.Status == "running")
                        _state.Launcher.FinishJob(job.Id, "failed");
                    throw;
                }
                _win.Toast($"已导出：{Path.GetFileName(target)}");
            }
            finally
            {
                _win.ProgressDone(0);
            }
        }
        catch (OperationCanceledException)
        {
            _win.ProgressDone(0);
            _win.Toast("已取消；已复制的部分保留，下次导出会从断点继续");
        }
        catch (PadError pe)
        {
            _win.ProgressDone(0);
            _win.Toast(pe.Code == "PA001"
                    ? $"这个包要内核 {card.Kernel}，本机没装上（或下载失败）。先装好内核再导出整合包"
                    : pe.Message,
                bad: true);
        }
        catch (Exception ex)
        {
            _win.ProgressDone(0);
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            _win.ProgressDone(600);
            CleanupTempInstance(temp);
        }
    }

    /// <summary>整合包（文件夹）：不压缩，直接给目录；复制过程同样报进度/速度/剩余。</summary>
    async void ExportPackPortableFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var menu = mi.Parent as ContextMenu ?? ItemsControl.ItemsControlFromItemContainer(mi) as ContextMenu;
        if (menu?.PlacementTarget is not FrameworkElement row) return;
        if (row.DataContext is not PackCard card) return;
        if (string.IsNullOrWhiteSpace(card.Kernel))
        {
            _win.Toast("这个包没写内核，建不了实例，也打不了整合包", bad: true);
            return;
        }
        var prep = await PreparePortableAsync(card);
        if (prep is null) return;
        var (inst, temp) = prep.Value;
        try
        {
            var dlg = new OpenFolderDialog { Title = "选一个文件夹放整合包" };
            if (dlg.ShowDialog(_win) != true) return;
            var dest = Path.Combine(dlg.FolderName,
                PortableExport.DefaultFolderName(card.Title, card.Version));
            _win.Progress("正在估算整合包大小…（版本库文件多，约几秒）");
            var estimate = await Task.Run(() =>
                PortableExport.Estimate(_state.Launcher, card.Kernel!, inst!.Id));
            _win.Progress($"即将打整合包（文件夹，约 {estimate / (1024 * 1024)} MB）…");
            var folderJob = await PortableExport.ExportAsync(_win, _state.Launcher, dest, inst!.Id,
                "打整合包（文件夹）", estimate, zipPhase: false);
            _state.Launcher.FinishJob(folderJob.Id, "done");
            _win.Toast($"已导出文件夹：{dest}");
            Reveal.Path(dest);
        }
        catch (OperationCanceledException)
        {
            _win.ProgressDone(0);
            _win.Toast("已取消；已复制/压缩的部分保留，下次会继续");
        }
        catch (PadError pe)
        {
            _win.ProgressDone(0);
            _win.Toast(pe.Message, bad: true);
        }
        catch (Exception ex)
        {
            _win.ProgressDone(0);
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            _win.ProgressDone(600);
            CleanupTempInstance(temp);
        }
    }

    void CleanupTempInstance(Instance? temp)
    {
        if (temp is null) return;
        try { _state.Launcher.RemoveInstance(temp.Id); _state.Reload(); }
        catch { /* 临时实例没删掉不影响导出结果 */ }
    }

    /// <summary>
    /// 整合包要先有实例：找用这个包的实例；没有就建临时实例（缺内核先问用户安装）。
    /// 用户取消返回 null。
    /// </summary>
    async Task<(Instance? Inst, Instance? Temp)?> PreparePortableAsync(PackCard card)
    {
        var inst = _state.Instances.FirstOrDefault(i =>
            (!string.IsNullOrWhiteSpace(i.Source?.PackPath) &&
             string.Equals(i.Source!.PackPath, card.Path, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(i.Source?.PackId) &&
             string.Equals(i.Source!.PackId, card.Id, StringComparison.OrdinalIgnoreCase)));
        if (inst is not null) return (inst, null);

        var hasRelease = _state.Releases.Any(r =>
            string.Equals(r.Version, card.Kernel, StringComparison.OrdinalIgnoreCase));
        if (!hasRelease)
        {
            var ask = _win.Ask("导出整合包",
                $"这个包要内核 {card.Kernel}，本机还没装。\n\n现在下载安装，装完继续导出？");
            if (!ask) return null;
            var meter = new InstallMeter(_win, card.Kernel!);
            _win.Progress($"安装内核 {card.Kernel} · 准备中…");
            // Progress<T> 必须在 UI 线程创建！放进 Task.Run 里创建会捕获不到
            // DispatcherSynchronizationContext，回调落到线程池 → 更新 TextBlock 直接
            // "调用线程无法访问此对象"（用户 2026-10-28 连弹多个 PAD 出错）。
            var meterProgress = new Progress<string>(meter.Line);
            await Task.Run(() => _state.Launcher.InstallRelease(card.Kernel!,
                meterProgress, CancellationToken.None));
            _win.ProgressDone(0);
            _state.Reload();
        }
        _win.Progress($"为 {card.Title} 建临时实例…");
        var temp = await Task.Run(() => _state.Launcher.CreateFromPack(card,
            "pack-" + SafeName(card.Title), new Progress<string>(_ => { }), CancellationToken.None));
        _state.Reload();
        return (temp, temp);
    }

    /// <summary>把 pnpm 的原始输出翻译成看得懂的进度+速度（装 DSH 内核时用）。</summary>
    sealed class InstallMeter(MainWindow win, string version)
    {
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        double _lastMb;
        double _lastSec;

        public void Line(string? raw)
        {
            var line = (raw ?? "").Trim();
            if (line.Length == 0) return;
            if (line.StartsWith("Downloading ", StringComparison.OrdinalIgnoreCase) && line.Contains("MB/"))
            {
                var (got, total) = MbPair(line);
                if (got <= 0) return;
                var sec = _clock.Elapsed.TotalSeconds;
                var dt = Math.Max(0.4, sec - _lastSec);
                var speed = Math.Max(0, (got - _lastMb) / dt);
                _lastMb = got;
                _lastSec = sec;
                win.Progress($"下载 {got:F1}/{total:F1} MB · {speed:F1} MB/s", total > 0 ? got / total : null, $"安装内核 {version}");
                return;
            }
            if (line.StartsWith("Progress:", StringComparison.OrdinalIgnoreCase))
            {
                win.Progress(line["Progress:".Length..].Trim(), null, $"安装内核 {version}");
                return;
            }
            if (line.Contains("Lockfile", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Packages:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("resolved", StringComparison.OrdinalIgnoreCase))
            {
                win.Progress(line, null, $"安装内核 {version}");
                return;
            }
            if (line.Contains("ERR_", StringComparison.OrdinalIgnoreCase))
                win.Progress(line, null, $"安装内核 {version}");
        }

        static (double Got, double Total) MbPair(string line)
        {
            try
            {
                var slash = line.IndexOf("MB/", StringComparison.OrdinalIgnoreCase);
                if (slash <= 0) return (0, 0);
                var before = line[..slash];
                var start = before.LastIndexOf(' ');
                var got = double.Parse(before[(start + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                var tail = line[(slash + 3)..];
                var end = tail.IndexOf(" MB", StringComparison.OrdinalIgnoreCase);
                var total = double.Parse((end > 0 ? tail[..end] : tail).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                return (got, total);
            }
            catch { return (0, 0); }
        }
    }

    static string SafeName(string text)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = (text ?? "").Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray();
        return chars.Length == 0 ? "pack" : new string(chars);
    }

    /// <summary>导出整合包：进内页自己选「核心 / 容器 / 插件」，至少一个插件，存成 .pack.zip。</summary>
    void MakePack_Click(object sender, RoutedEventArgs e) =>
        _win.ShowInner("makepack", "导出整合包", "versions");

    async void CreateFromPack_Click(object sender, RoutedEventArgs e)
    {
        if (_creatingFromPack || sender is not Button { Tag: PackCard card }) return;
        // .pinst.zip 是导出的实例，不是包：走 TS 原生 import 链。
        // .pack.json 与 .pack.zip 都能直接投影（pack-ops 对 zip 读中央目录取根 manifest）。
        var kind = card.Kind ?? "";
        if (kind.Contains("pinst", StringComparison.OrdinalIgnoreCase)
            || (kind.Length == 0 && card.Path.EndsWith(".pinst.zip", StringComparison.OrdinalIgnoreCase)))
        {
            await ImportPackCardAsync(card);
            return;
        }
        if (string.IsNullOrWhiteSpace(card.Kernel))
        {
            _win.Toast("这个包没写 dsh.version（内核），不能建实例", bad: true);
            return;
        }
        var name = Prompt("用整合包新建实例", $"实例名（内核 {card.Kernel}）：", card.Title);
        if (string.IsNullOrWhiteSpace(name)) return;
        _creatingFromPack = true;
        try
        {
            var inst = await CreateInstanceFromPackAsync(card, name!.Trim());
            if (inst is null) return;
            _state.Reload();
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == inst.Id);
            _win.ShowInner("instance", "版本设置");
            _ = RefreshPacksAsync();
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            _creatingFromPack = false;
        }
    }

    /// <summary>.pack.zip / .pinst.zip：走 import 链（TS 侧原生支持），顺便留库。</summary>
    async Task ImportPackCardAsync(PackCard card)
    {
        var packs = new Packs(_state.Launcher);
        if (packs.Unavailable() is { } missing)
        {
            _win.Toast($"{missing.Reason}：{missing.Detail}", bad: true);
            return;
        }
        _creatingFromPack = true;
        try
        {
            _win.Toast("正在导入 " + card.Title);
            try { packs.CollectToLibrary(card.Path); } catch { /* 库拷贝失败不挡导入 */ }
            await packs.Import(card.Path, new Progress<string>(Log), CancellationToken.None);
            _state.Reload();
            _win.ShowInner("instance", "版本设置");
            _ = RefreshPacksAsync();
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            _creatingFromPack = false;
        }
    }

    /// <summary>内核不在版本库里时跳窗询问，答「是」就下载安装再接着建实例。</summary>
    async Task<Instance?> CreateInstanceFromPackAsync(PackCard card, string name)
    {
        try
        {
            return await Task.Run(() =>
                _state.Launcher.CreateFromPack(card, name, new Progress<string>(Log), CancellationToken.None));
        }
        catch (PadError pe) when (pe.Code == "PA001")
        {
            var ask = MessageBox.Show(
                $"这个整合包要内核 {card.Kernel}，它还没装。\n\n现在下载安装，装完继续建实例？",
                "整合包 · 缺内核", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return null;
            _win.Toast($"正在安装内核 {card.Kernel}…");
            await Task.Run(() =>
                _state.Launcher.InstallRelease(card.Kernel!, new Progress<string>(Log), CancellationToken.None));
            _state.Reload();
            return await Task.Run(() =>
                _state.Launcher.CreateFromPack(card, name, new Progress<string>(Log), CancellationToken.None));
        }
    }

    /// <summary>点容器 = 选中（学 PCL）。实例自带的启动配置顺手选中实例；探测到的容器只选中，不搞「收编」。</summary>
    void ContainerList_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (_containerSync) return;
        if (ContainerList.SelectedItem is not ProfileVm vm) return;
        if (!vm.IsDetected)
        {
            _state.SelectedInstance = vm.Instance;
            _state.SelectedProfile = _state.Launchables.FirstOrDefault(p => p.Name == vm.Name) ?? vm;
            _state.SaveSelection();
            return;
        }
        if (string.IsNullOrWhiteSpace(vm.DetectedHome)) return;
        var matched = _state.Instances.FirstOrDefault(i =>
            string.Equals(NormHome(i.Home), NormHome(vm.DetectedHome), StringComparison.OrdinalIgnoreCase));
        if (matched is null) return;
        _state.SelectedInstance = matched;
        InstanceList.SelectedItem = matched;
    }

    /// <summary>行右侧只有设置：实例 → 版本设置页；探测到的容器 → 容器设置卡片（只读信息）。</summary>
    void Setup_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: ProfileVm vm }) return;
        if (!vm.IsDetected)
        {
            OpenSetup(vm);
            return;
        }
        _win.Ask("容器设置 · " + vm.Name, ContainerInfo(vm), previewPath: vm.IconPath);
    }

    /// <summary>容器设置卡片里能读到的字段；探测容器只读，PAD 不改它。</summary>
    static string ContainerInfo(ProfileVm vm)
    {
        var lines = new List<string>
        {
            "类型：" + vm.Kind,
            "内核：" + (string.IsNullOrWhiteSpace(vm.Instance.Dsh.Version) ? "未知" : vm.Instance.Dsh.Version),
            "home：" + (string.IsNullOrWhiteSpace(vm.Instance.Home) ? "—" : vm.Instance.Home),
            "状态：" + (vm.Running ? "在跑" : "未跑"),
        };
        lines.Add("");
        lines.Add("这是本机探测到的容器，不是 PAD 实例。PAD 只看它、不往里写；");
        lines.Add("要在它上面跑 PAD 需要先建实例（左下「新建实例」或「添加已有文件夹」）。");
        return string.Join(Environment.NewLine, lines);
    }

    void OpenSetup(ProfileVm vm)
    {
        _state.SelectedInstance = vm.Instance;
        _state.SelectedProfile = _state.Launchables.FirstOrDefault(p => p.Name == vm.Name) ?? vm;
        _state.SaveSelection();
        _win.ShowInner("instance", "版本设置");
    }

    void Gear_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: Instance inst } fe) return;
        _state.SelectedInstance = inst;
        InstanceList.SelectedItem = inst;
        if (InstanceList.ContextMenu is { } menu)
        {
            menu.PlacementTarget = fe;
            menu.IsOpen = true;
        }
    }

    void OpenHome_Click(object sender, RoutedEventArgs e) => Reveal.Path(_state.SelectedInstance?.Home);

    void OpenWorkspace_Click(object sender, RoutedEventArgs e) =>
        Reveal.Path(_state.SelectedInstance?.Workspace.Path);

    void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedInstance is null) return;
        Reveal.Path(_state.Launcher.LogsDir(_state.SelectedInstance.Id));
    }

    void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var profile = inst.LastProfile ?? _state.Launchables.FirstOrDefault()?.Name;
        if (profile is null)
        {
            _win.Toast("未找到可用的版本", bad: true);
            return;
        }
        try
        {
            var path = _state.Launcher.WriteShortcut(inst, profile);
            _win.Toast("快捷方式写到了 library/shortcuts");
            Reveal.Path(path);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void InstanceList_Right(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        var item = ItemsControl.ContainerFromElement(InstanceList, src) as ListBoxItem;
        if (item is not null) item.IsSelected = true;
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        if (!_win.Ask("版本删除确认",
            $"你确定要删除实例 {inst.Name} 吗？\n该实例对应的 DSH_HOME、工作区和 session 也会被一并删除。",
            warn: true)) return;
        _state.Launcher.RemoveInstance(inst.Id);
        _state.Reload();
    }

    void Clone_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var name = Prompt("克隆实例", $"克隆 {inst.Name} 为：");
        if (name is null or { Length: 0 }) return;
        try
        {
            var cloned = _state.Launcher.CloneInstance(inst.Id, name);
            _state.Reload();
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == cloned.Id);
            _win.Toast($"已克隆为 {cloned.Name}");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Rename_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var name = Prompt("改名", $"把 {inst.Name} 改为：", inst.Name);
        if (name is null or { Length: 0 } || name == inst.Name) return;
        try
        {
            _state.Launcher.RenameInstance(inst.Id, name);
            _state.Reload();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Pin_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var releases = _state.Launcher.Releases();
        if (releases.Count == 0) { _win.Toast("版本库里还没有发行号", bad: true); return; }
        var current = inst.Dsh.Version;
        var options = releases.Select(r => r.Version).ToList();
        var pick = PickOption("改钉发行号", $"当前 {current}，改为：", options);
        if (pick is null or { Length: 0 } || pick == current) return;
        try
        {
            _state.Launcher.PinInstance(inst.Id, pick);
            _state.Reload();
            _win.Toast($"已改钉到 {pick}（组合包未验证）");
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Star_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        try
        {
            _state.Launcher.SetDisplay(inst.Id, star: !(inst.Display?.Star ?? false));
            _state.Reload();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Category_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var cat = Prompt("设置分类", "分类名（留空取消）：", inst.Display?.Category ?? "");
        if (cat is null) return;
        try
        {
            _state.Launcher.SetDisplay(inst.Id, category: cat);
            _state.Reload();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void OpenProfiles_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        Reveal.Path(_state.Launcher.ProfilesDir(inst));
    }

    void OpenProjection_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        Reveal.Path(_state.Launcher.ProjectionDir(inst));
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出实例",
            Filter = "实例包 (*.pinst.zip)|*.pinst.zip",
            FileName = $"{inst.Id}.pinst.zip",
        };
        if (dlg.ShowDialog(_win) != true) return;
        try
        {
            var path = _state.Launcher.ExportInstance(inst.Id, dlg.FileName);
            _win.Toast("导出了（凭据已剥掉）");
            Reveal.Path(path);
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    // ------------------------------------------------------------------ 本机探测（D36）：版本/终端

    /// <summary>
    /// 探测本机 DSH（内核/载体/home），把结果喂给两处：
    /// 「DSH 版本」下拉补上本机探测到的内核版本；「终端」列表补上探测到的 TUI。
    /// 扫描在 worker 上跑（D21），页面先画再补。
    /// </summary>
    async Task ScanDetectedAsync(bool force = false)
    {
        if (!force && _detected is not null)
        {
            ApplyDetected();
            return;
        }
        var gen = ++_detectGen;
        Inventory inv;
        try
        {
            inv = await Task.Run(() => Discover.Run(_state.Launcher, [], CancellationToken.None));
        }
        catch
        {
            return;
        }
        if (gen != _detectGen) return;
        _detected = inv;
        ApplyDetected();
    }

    void ApplyDetected()
    {
        RefreshVersionChoices();
        RebuildContainerRows();
        // 截图专用窗口（--shot-page versions-open）：把版本下拉展开，证明探测到的版本真在里面。
        if (App.ShotPage == "versions-open" && PinRelease is not null && !PinRelease.IsDropDownOpen)
            PinRelease.IsDropDownOpen = true;
    }

    void RefreshVersionChoices()
    {
        // 一个版本一行；来源合并——不能因为版本库里已有 0.1.0-rc.7 就把
        // 「TUI 启动脚本声明的 0.1.0-rc.7」整条吃掉（用户会以为没探测到）。
        var byVersion = new Dictionary<string, (DshRelease? Release, List<string> Sources)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var rel in _state.Releases)
        {
            if (!byVersion.TryGetValue(rel.Version, out var entry))
                entry = (rel, []);
            entry.Sources.Insert(0, "PAD 版本库");
            byVersion[rel.Version] = entry;
        }

        if (_detected is not null)
        {
            foreach (var kernel in _detected.Kernels)
            {
                if (string.IsNullOrWhiteSpace(kernel.Version)) continue;
                var source = ShortSource(kernel);
                if (!byVersion.TryGetValue(kernel.Version, out var entry))
                    entry = (null, []);
                if (!entry.Sources.Contains(source, StringComparer.OrdinalIgnoreCase))
                    entry.Sources.Add(source);
                byVersion[kernel.Version] = entry;
            }
        }

        var choices = byVersion
            .Select(kv => new VersionChoice
            {
                Version = kv.Key,
                Source = string.Join(" + ", kv.Value.Sources),
                Release = kv.Value.Release,
            })
            .OrderBy(c => c.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _versionChoices = choices;
        if (PinRelease is null) return;
        PinRelease.ItemsSource = _versionChoices;
        SyncPinCombo();
    }

    /// <summary>版本下拉里给探测来源一个短标签，别把整条 asar 路径塞进下拉。</summary>
    static string ShortSource(Kernel kernel)
    {
        var source = kernel.Source ?? "";
        if (source.StartsWith("desktop bundle:", StringComparison.OrdinalIgnoreCase))
        {
            var exe = source["desktop bundle:".Length..].Trim();
            var name = IOPath.GetFileNameWithoutExtension(exe);
            return name.Length > 0 ? $"{name} 自带" : "桌面端自带";
        }
        if (source.StartsWith("launcher script:", StringComparison.OrdinalIgnoreCase)) return "启动脚本";
        if (source.Equals("pad version store", StringComparison.OrdinalIgnoreCase)) return "版本库";
        if (source.Equals("npx cache", StringComparison.OrdinalIgnoreCase)) return "npx";
        if (source.Equals("npm global", StringComparison.OrdinalIgnoreCase)) return "npm";
        return "本机";
    }

    /// <summary>
    /// 「容器」列表 = 本实例的启动配置（profile）+ 本机探测到的桌面端 / TUI。
    /// 识别到就列出来；命令行入口不是容器，不列。
    /// </summary>
    void RebuildContainerRows()
    {
        if (ContainerList is null) return;
        var rows = new List<ProfileVm>(_state.Launchables);
        if (_detected is not null)
        {
            var instanceHomes = new HashSet<string>(
                _state.Instances.Select(i => NormHome(i.Home)), StringComparer.OrdinalIgnoreCase);
            foreach (var shell in _detected.Shells)
            {
                var home = NormHome(shell.Home);
                rows.Add(new DetectedShellRow(shell, canAdopt: home.Length > 0 && !instanceHomes.Contains(home))
                {
                    Running = shell.Running,
                });
            }
            foreach (var tui in _detected.Tuis)
            {
                if (string.IsNullOrWhiteSpace(tui.Package)) continue;
                var home = NormHome(tui.Home);
                if (home.Length > 0 && instanceHomes.Contains(home)) continue; // 已收编：实例自己的 profile 已列出
                rows.Add(new DetectedTuiRow(tui, canAdopt: true) { Running = tui.Running });
            }
        }
        var keepKey = (ContainerList.SelectedItem as ProfileVm)?.InstanceName;
        _containerSync = true;
        try
        {
            ContainerList.ItemsSource = rows;
            if (keepKey is not null)
                ContainerList.SelectedItem = rows.FirstOrDefault(r => r.InstanceName == keepKey);
        }
        finally { _containerSync = false; }
    }

    static string NormHome(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return IOPath.GetFullPath(path!.Trim().Trim('"')).TrimEnd('\\', '/'); }
        catch { return ""; }
    }

    /// <summary>版本下拉的一项：PAD 版本库的可钉版本，或本机探测到的内核（只读，标来源）。</summary>
    public sealed class VersionChoice
    {
        public string Version { get; init; } = "";
        /// <summary>来源合并后的说明，例如 "PAD 版本库 + 本机启动脚本声明"。</summary>
        public string Source { get; init; } = "";
        public DshRelease? Release { get; init; }
        public bool HasSource => Source.Length > 0;
    }

    string? Prompt(string title, string label, string defaultValue = "") =>
        _win.AskInput(title, label, defaultValue);

    static string? PickOption(string title, string label, List<string> options)
    {
        var combo = new ComboBox { Width = 300, ItemsSource = options, SelectedIndex = 0 };
        var dlg = new Window
        {
            Title = title,
            Width = 400, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(20), Children =
            {
                new TextBlock { Text = label, Margin = new Thickness(0,0,0,8) },
                combo,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0,12,0,0), Children =
                    {
                        new Button { Content = "取消", Width = 70, Margin = new Thickness(0,0,8,0), IsCancel = true },
                        new Button { Content = "确定", Width = 70, IsDefault = true,
                            Command = ApplicationCommands.Open },
                    } },
            } },
        };
        dlg.CommandBindings.Add(new CommandBinding(ApplicationCommands.Open,
            (_, _) => { dlg.DialogResult = true; dlg.Close(); }));
        return dlg.ShowDialog() == true ? combo.SelectedItem as string : null;
    }
}
