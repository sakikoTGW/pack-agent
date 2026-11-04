using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    /// <summary>
    /// 刷新版本下拉期间不再回刷自己：版本库和本机内核都还空的时候（刚装好的 PAD、
    /// 还没探测完），<see cref="SyncPinCombo"/> ↔ <see cref="RefreshVersionChoices"/>
    /// 会互相递归直接栈溢出。
    /// </summary>
    bool _refreshingChoices;

    void SyncPinCombo()
    {
        if (PinRelease is null) return;
        if (_versionChoices.Count == 0 && !_refreshingChoices) RefreshVersionChoices();
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
            _win.Toast($"已改钉到 {choice.Version}（插件未验证）");
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
            Filter = "整合包 (*.pack.zip;*.pack.json;*.pinst.zip;*.dspack)|*.pack.zip;*.pack.json;*.pinst.zip;*.dspack|所有文件|*.*",
        };
        if (dlg.ShowDialog(_win) != true) return;

        // v3 包：走新窗口（PackInstall），其余格式继续走旧 import 链路。
        var v3 = PackSpec.Load(dlg.FileName);
        if (v3.Valid && v3.Schema == PackSpec.SchemaV3)
        {
            var win = new PackImportWindow(_state.Launcher, dlg.FileName, v3, _state.SelectedInstance) { Owner = _win };
            if (win.ShowDialog() == true && win.Installed is not null)
            {
                _state.Reload();
                _win.Toast($"已导入 {v3.Name} {v3.Version}：{win.Summary}");
                await RefreshPacksAsync();
            }
            return;
        }

        // PackForge .dspack：走 compat 引擎（新实例 + overrides/home/bundles/deps），
        // 与原生 .pack.zip 的投影路径分开，不混装。
        if (LaunchPolicy.IsDspackDrop(dlg.FileName))
        {
            try
            {
                _win.Toast("正在装 " + Path.GetFileName(dlg.FileName));
                var result = await PacksForge.ImportAuto(_state.Launcher, packs, dlg.FileName,
                    deps: true, new Progress<string>(Log), CancellationToken.None);
                _state.Reload();
                var newestDspack = _state.Instances.OrderByDescending(i => i.Created).FirstOrDefault();
                if (newestDspack is not null) _state.SelectedInstance = newestDspack;
                _win.ShowInner("instance", "版本设置");
                _win.Toast($"装好了：{result.Name}（DSH {result.Release}）");
                _ = RefreshPacksAsync();
            }
            catch (Exception ex)
            {
                _win.Toast(PadError.Describe(ex), bad: true);
            }
            return;
        }

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
        // D105 UX（用户 2026-11-04）：行左列换整合包自己的 icon；不再显示「装到哪个实例」。
        PackList.ItemsSource = _state.PackCatalog
            .Select(card => new PackItem(card, PackIcon.Resolve(_state.Launcher.Root, card.Path)))
            .ToList();
    }

    void RefreshPackCatalog_Click(object sender, RoutedEventArgs e) => _ = RefreshPacksAsync();

    /// <summary>已建好的包：右键 → 导出成一份文件（拷到用户选的位置，不重新组包）。</summary>
    void ExportPack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        var menu = mi.Parent as ContextMenu ?? ItemsControl.ItemsControlFromItemContainer(mi) as ContextMenu;
        if (menu?.PlacementTarget is not FrameworkElement row) return;
        if (row.DataContext is not PackItem { Card: var card }) return;
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
        if (row.DataContext is not PackItem { Card: var card }) return;
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
        if (row.DataContext is not PackItem { Card: var card }) return;
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
        if (_creatingFromPack || sender is not Button { Tag: PackItem item }) return;
        var card = item.Card;
        // .pinst.zip 是导出的实例，不是包：走 TS 原生 import 链。
        var kind = card.Kind ?? "";
        if (kind.Contains("pinst", StringComparison.OrdinalIgnoreCase)
            || (kind.Length == 0 && card.Path.EndsWith(".pinst.zip", StringComparison.OrdinalIgnoreCase)))
        {
            await RunPackActionAsync(item, () => ImportPackCardAsync(card));
            return;
        }
        var name = Prompt("用整合包新建实例", $"实例名（{card.KernelText}）：", card.Title);
        if (string.IsNullOrWhiteSpace(name)) return;
        // .pack.zip / .pack.json：走完整 import 链（建实例 + 投影 + 装 manager + dsh plugin add）。
        // 旧的只投影路径装不上 dsh.plugins[]：界面显示建好了，插件实际一个都没装。
        await RunPackActionAsync(item, () => InstallPackAsync(card, name.Trim()));
    }

    /// <summary>
    /// D107 安装概念：行右侧「安装 ▾」→ 三项菜单。
    /// ① 装到当前实例（主推荐）→ Preflight → 授权 → InstallPackToInstance（写账本，装完实例页发光）；
    /// ② 新建实例… = 现有完整 import 链（D104，保留）；
    /// ③ 先检查能不能装… = 只预检（DryRun，不装）。
    /// </summary>
    void InstallMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_creatingFromPack || sender is not Button { Tag: PackItem item } btn) return;
        var card = item.Card;
        var target = _state.SelectedInstance;

        var menu = new ContextMenu { PlacementTarget = btn, Placement = PlacementMode.Bottom };
        var current = new MenuItem
        {
            Header = target is null ? "装到当前实例（先在左栏选一个实例）" : $"装到当前实例（{target.Name}）",
            IsEnabled = target is not null,
            ToolTip = "投影 + 管理器 + 按 diff 装插件，写账本；装完在实例页发光",
        };
        current.Click += async (_, _) => await InstallToCurrentInstanceAsync(item);
        var create = new MenuItem
        {
            Header = "新建实例…",
            ToolTip = "用这个包派生一个新实例（PCL 式，现有行为）",
        };
        create.Click += (_, _) => CreateFromPack_Click(new Button { Tag = item }, new RoutedEventArgs());
        var check = new MenuItem
        {
            Header = "先检查能不能装…",
            ToolTip = "内核 / 插件下架 / 兼容豁免 / 版本冲突，装之前一次列清",
        };
        check.Click += async (_, _) => await CheckOnlyAsync(item);
        menu.Items.Add(current);
        menu.Items.Add(new Separator());
        menu.Items.Add(create);
        menu.Items.Add(check);
        menu.IsOpen = true;
    }

    string FirstProfileName(Instance inst) =>
        _state.Launcher.Profiles(inst).FirstOrDefault()?.Name ?? "web";

    /// <summary>装到哪个 profile：当前选中实例跟它一致时用 AppState.SelectedProfile，否则 lastProfile/第一个。</summary>
    string ProfileForInstance(Instance inst)
    {
        var selected = _state.SelectedInstance;
        var same = selected is not null && string.Equals(selected.Id, inst.Id, StringComparison.OrdinalIgnoreCase);
        return same
            ? _state.SelectedProfile?.Name ?? inst.LastProfile ?? FirstProfileName(inst)
            : inst.LastProfile ?? FirstProfileName(inst);
    }

    async Task InstallToCurrentInstanceAsync(PackItem item)
    {
        var inst = _state.SelectedInstance;
        if (inst is null)
        {
            _win.Toast("先在左栏选一个实例，再装到它", bad: true);
            return;
        }
        var profile = ProfileForInstance(inst);
        var card = item.Card;
        _win.Toast("正在预检 " + card.Title + "…");
        _win.Progress($"{card.Title} · 预检 / 安装中…", null, "装整合包");
        try
        {
            await PackPreflightWindow.RunInstallFlowAsync(
                Window.GetWindow(this), new Packs(_state.Launcher), inst, profile, card,
                new Progress<string>(Log),
                afterApplied: () =>
                {
                    _state.Reload();
                    _ = RefreshPacksAsync();
                },
                openInstance: () =>
                {
                    _state.SelectedInstance = inst;
                    _win.ShowInner("instance", "版本设置");
                },
                reExport: () => ReExportPackAsync(card),
                toast: (text, bad) => _win.Toast(text, bad));
        }
        finally
        {
            _win.ProgressDone(0);
        }
    }

    /// <summary>「先检查能不能装…」：只预检并展示阶段 1（DryRun，不写盘）。</summary>
    async Task CheckOnlyAsync(PackItem item)
    {
        var inst = _state.SelectedInstance;
        if (inst is null)
        {
            _win.Toast("先在左栏选一个实例，才能检查它装不装得上", bad: true);
            return;
        }
        var profile = ProfileForInstance(inst);
        var card = item.Card;
        _win.Progress($"{card.Title} · 检查中（不安装）…", null, "装整合包");
        try
        {
            await PackPreflightWindow.RunInstallFlowAsync(
                Window.GetWindow(this), new Packs(_state.Launcher), inst, profile, card,
                new Progress<string>(Log),
                afterApplied: null,
                openInstance: null,
                reExport: () => ReExportPackAsync(card),
                toast: (text, bad) => _win.Toast(text, bad),
                checkOnly: true);
        }
        finally
        {
            _win.ProgressDone(0);
        }
    }

    /// <summary>安装报告里的「重新导出这个包」：与右键导出便携包同语义（拷一份文件出去）。</summary>
    Task ReExportPackAsync(PackCard card)
    {
        if (!File.Exists(card.Path))
        {
            _win.Toast("这个包的文件不见了", bad: true);
            return Task.CompletedTask;
        }
        var isZip = card.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var dlg = new SaveFileDialog
        {
            Title = "重新导出这个包",
            Filter = isZip ? "整合包 (*.zip)|*.zip" : "整合包 (*.pack.json)|*.pack.json",
            FileName = Path.GetFileName(card.Path),
            InitialDirectory = ExportHome(),
        };
        if (dlg.ShowDialog(_win) != true) return Task.CompletedTask;
        try
        {
            CopyWithRetry(card.Path, dlg.FileName);
            _win.Toast($"已导出：{Path.GetFileName(dlg.FileName)}");
        }
        catch (IOException ex)
        {
            _win.Toast("导出失败：" + ex.Message, bad: true);
        }
        return Task.CompletedTask;
    }

    /// <summary>一次「用包建实例」期间：行按钮变「正在装…」并禁用，进度卡给提示（装插件可能要几分钟）。</summary>
    async Task RunPackActionAsync(PackItem item, Func<Task> action)
    {
        _creatingFromPack = true;
        item.Busy = true;
        try
        {
            await action();
        }
        finally
        {
            item.Busy = false;
            _creatingFromPack = false;
            _win.ProgressDone(0);
        }
    }

    /// <summary>
    /// P3：新建实例装包时撞 DSH 兼容门——不摆死路。找 import 上次留下的 import-failed 实例
    /// （优先 source.packId == 这个包的），三选一：①接受风险（allow-version 后在这份实例上
    /// 续装）②换一个本机已装内核重试 ③取消并说明是包内容/版本问题。
    /// 续装成功：MarkInstanceStatus(ready) → Reload → 选中实例 → 进版本设置页；失败保持
    /// import-failed 并如实报错（半成品不删）。
    /// </summary>
    async Task RecoverIncompatibleImportAsync(PackCard card, PackCompatFix fix)
    {
        _state.Reload();
        var failed = _state.Instances
            .Where(i => string.Equals(i.Status, "import-failed", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var leftover = failed.FirstOrDefault(i =>
                           string.Equals(i.Source?.PackId, card.Id, StringComparison.OrdinalIgnoreCase))
                       ?? failed.OrderByDescending(i => i.Created, StringComparer.Ordinal).FirstOrDefault();
        if (leftover is null)
        {
            _win.Toast($"这个整合包里的插件与它钉的内核不兼容：{fix.Text}。没有找到上次失败的实例可续装；"
                       + "这是包内容/版本问题：按当前环境重新导出包，或改插件版本/内核后重试", bad: true);
            return;
        }
        var profile = fix.Profile.Length > 0
            ? fix.Profile
            : leftover.LastProfile ?? _state.Launcher.Profiles(leftover).FirstOrDefault()?.Name ?? "web";
        var accept = _win.Ask("插件与内核不兼容 · 接受风险安装",
            $"这个整合包里的插件与它钉的内核不兼容：{fix.Text}。\n\n"
            + $"上次导入留下的实例 {leftover.Name} 还在；接受风险会允许 {fix.PackageVersion} 在 DSH "
            + $"{fix.DshVersion} 上运行（可能崩溃或损坏数据，豁免只对这两个精确版本生效），"
            + "然后在这个实例上继续把插件装完。\n\n接受风险安装并续装？",
            warn: true, okText: "接受风险安装", cancelText: "换内核/取消…");
        var target = leftover;
        if (accept)
        {
            try
            {
                await _state.Launcher.AllowVersion(target, profile, fix.PackageVersion, fix.DshVersion,
                    new Progress<string>(Log), CancellationToken.None);
                _win.Toast($"已允许 {fix.PackageVersion} 在 DSH {fix.DshVersion} 上运行，续装插件…");
            }
            catch (Exception ex)
            {
                _win.Toast("接受风险没成功：" + PadError.Describe(ex), bad: true);
                return;
            }
        }
        else
        {
            var switched = SwitchKernelForRecovery(target);
            if (switched is null) return;   // 取消：提示已给
            target = switched;
            if (string.IsNullOrWhiteSpace(fix.Profile))
                profile = ProfileForRecovery(target);
        }

        var packs = new Packs(_state.Launcher);
        PackApplyResult result;
        try
        {
            _win.Progress($"{card.Title} · 正在把上次没装完的插件补齐…", null, "装整合包");
            result = await packs.ApplyPackToInstance(target, card.Path, profile,
                new Progress<string>(Log), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _win.Toast("续装失败：" + PadError.Describe(ex) + "；实例保持 import-failed，可看任务日志", bad: true);
            _state.Reload();
            return;
        }
        finally
        {
            _win.ProgressDone(0);
        }

        if (result.Ok && result.Conflicts.Count == 0)
        {
            _state.Launcher.MarkInstanceStatus(target.Id, "ready");
            _state.Reload();
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == target.Id);
            _win.ShowInner("instance", "版本设置");
            _win.Toast($"装好了：{target.Name}（DSH {target.Dsh.Version}）");
            _ = RefreshPacksAsync();
            return;
        }
        var detail = result.Failures.Select(f => f.Text)
            .Concat(result.Conflicts.Select(c => c.Text))
            .Take(4)
            .ToList();
        _win.Toast("续装没有完全成功（实例保持 import-failed）：" + result.Text
                   + (detail.Count > 0 ? "；" + string.Join("；", detail) : ""), bad: true);
        _state.Reload();
    }

    string ProfileForRecovery(Instance inst) =>
        inst.LastProfile ?? _state.Launcher.Profiles(inst).FirstOrDefault()?.Name ?? "web";

    /// <summary>P3 续装路径的「换内核重试」：列本机已装版本 → 输入/选一个 → PinInstance → Reload。</summary>
    Instance? SwitchKernelForRecovery(Instance inst)
    {
        var versions = _state.Releases.Select(r => r.Version)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (versions.Count == 0)
        {
            _win.Toast("本机没有别的已装内核可换。这是包内容/版本问题：按当前内核重新导出包，或改插件版本后重试", bad: true);
            return null;
        }
        var pick = _win.AskInput("插件与内核不兼容 · 换内核重试",
            "本机已装内核：" + string.Join("、", versions) + Environment.NewLine + Environment.NewLine
            + "输入要换的版本号（留空取消）：", inst.Dsh.Version, warn: true);
        if (string.IsNullOrWhiteSpace(pick))
        {
            _win.Toast("取消：这是包内容/版本问题——按当前内核重新导出包，或改插件版本后重试", bad: true);
            return null;
        }
        var chosen = versions.FirstOrDefault(v => string.Equals(v, pick.Trim(), StringComparison.OrdinalIgnoreCase));
        if (chosen is null)
        {
            _win.Toast("本机没装这个内核：" + pick.Trim(), bad: true);
            return null;
        }
        try
        {
            _state.Launcher.PinInstance(inst.Id, chosen);
            _state.Reload();
            var fresh = _state.Launcher.Instance(inst.Id) ?? inst;
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == fresh.Id) ?? _state.SelectedInstance;
            _win.Toast($"已改钉到 {chosen}，续装剩下没装完的插件…");
            return fresh;
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
            return null;
        }
    }

    /// <summary>
    /// .pack.zip / .pack.json 的「新建实例」：Packs.ImportPack 跑 TS 完整 import 链
    /// （--name、内核缺了会自己装、建 home、投影、装 manager、逐个 dsh plugin add）。
    /// 不再走只投影的 Launcher.CreateFromPack，否则 dsh.plugins[] 一个都装不上。
    /// </summary>
    async Task InstallPackAsync(PackCard card, string name)
    {
        var packs = new Packs(_state.Launcher);
        if (packs.Unavailable() is { } missing)
        {
            _win.Toast($"{missing.Reason}：{missing.Detail}", bad: true);
            return;
        }
        _win.Toast("正在装 " + card.Title);
        _win.Progress($"{card.Title} · 正在导入并安装插件（包大、插件多时要几分钟）…", null, "装整合包");
        try
        {
            // 先留一份进整合包库：import 成功后原位置可能不再保留原文件，库里这份让列表
            // 还能看到它、可再次建实例（和「导入整合包」同一语义）。
            try { packs.CollectToLibrary(card.Path); } catch { /* 库拷贝失败不挡导入 */ }
            var inst = await packs.ImportPack(card, name, new Progress<string>(Log), CancellationToken.None);
            _state.Reload();
            _state.SelectedInstance = _state.Instances.FirstOrDefault(i => i.Id == inst.Id) ?? inst;
            _win.ShowInner("instance", "版本设置");
            _win.Toast($"装好了：{inst.Name}（DSH {inst.Dsh.Version}）");
            _ = RefreshPacksAsync();
        }
        catch (Exception ex)
        {
            // P3：兼容门不是死路——找上次失败的实例续装 / 换内核重试；其余照常报原错。
            if (PackCompat.FromError(ex) is { } fix)
            {
                await RecoverIncompatibleImportAsync(card, fix);
                return;
            }
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>.pinst.zip：走 import 链（TS 侧原生支持），顺便留库。</summary>
    async Task ImportPackCardAsync(PackCard card)
    {
        var packs = new Packs(_state.Launcher);
        if (packs.Unavailable() is { } missing)
        {
            _win.Toast($"{missing.Reason}：{missing.Detail}", bad: true);
            return;
        }
        _win.Toast("正在导入 " + card.Title);
        _win.Progress($"{card.Title} · 正在导入…", null, "装整合包");
        try
        {
            try { packs.CollectToLibrary(card.Path); } catch { /* 库拷贝失败不挡导入 */ }
            await packs.Import(card.Path, new Progress<string>(Log), CancellationToken.None);
            _state.Reload();
            _win.ShowInner("instance", "版本设置");
            _win.Toast($"装好了：{card.Title}");
            _ = RefreshPacksAsync();
        }
        catch (Exception ex)
        {
            if (PackCompat.FromError(ex) is { } fix)
                _win.Toast("这个整合包里的插件与它钉的内核不兼容：" + fix.Text +
                           "。这是包内容/版本问题：按当前环境重新「导出」一个包，" +
                           "或改包里的插件版本/内核后重试。", bad: true);
            else
                _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>
    /// 点容器 = 选中（学 PCL）：只决定「启动页按哪个容器启动」，不启动。
    /// 探测到的行（TUI / 桌面端）也进选中，这样启动页按它启动时就走它自己的入口。
    /// </summary>
    void ContainerList_Sel(object sender, SelectionChangedEventArgs e)
    {
        if (_containerSync) return;
        if (ContainerList.SelectedItem is not ProfileVm vm) return;
        if (!vm.IsDetected)
        {
            _state.SelectedInstance = vm.Instance;
            _state.SelectedProfile = _state.Launchables.FirstOrDefault(p => p.Name == vm.Name) ?? vm;
            _state.SelectedContainer = vm;
            _state.SaveSelection();
            return;
        }
        if (string.IsNullOrWhiteSpace(vm.DetectedHome)) return;
        var matched = _state.Instances.FirstOrDefault(i =>
            string.Equals(NormHome(i.Home), NormHome(vm.DetectedHome), StringComparison.OrdinalIgnoreCase));
        if (matched is null) return;
        // 先切实例，再记容器：切实例会把「上一个实例选中的容器」清掉。
        _state.SelectedInstance = matched;
        InstanceList.SelectedItem = matched;
        _state.SelectedContainer = vm;
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
            // 绿灯 = PAD 启动的 profile（runtime 账本）；探测到的容器永远不是。
            "状态：" + (vm.Running ? "在跑（PAD 启动）" : "PAD 没启动它"),
            // 它自己的进程可能在跑（桌面端开着），这份状态只在提示里说，不点灯。
            "本机进程：" + (vm.LocalRunning ? "在跑（不是 PAD 启动的）" : "没在跑"),
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
            _win.Toast($"已改钉到 {pick}（插件未验证）");
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

    /// <summary>导出 v3 整合包（新链路）；pinst.zip 仍走 Export_Click。</summary>
    void ExportPackV3_Click(object sender, RoutedEventArgs e)
    {
        var inst = _state.SelectedInstance;
        if (inst is null)
        {
            _win.Toast("先在左边选一个实例", bad: true);
            return;
        }
        var win = new PackExportWindow(_state.Launcher, inst) { Owner = _win };
        if (win.ShowDialog() != true || win.OutputPath is null) return;
        _win.Toast("已导出 " + System.IO.Path.GetFileName(win.OutputPath));
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
        _refreshingChoices = true;
        try { SyncPinCombo(); }
        finally { _refreshingChoices = false; }
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
        // 一行 = 一种 surface。profile 里装了两种 UI bundle 就拆成两行：
        // 浏览器窗口和终端窗口是两种「怎么看见」，不是同一行的两种标签。
        var rows = new List<ProfileVm>();
        foreach (var p in _state.Launchables)
        {
            var surfaces = p.Profile.UiSurfaces;
            if (surfaces.Count <= 1) { rows.Add(p); continue; }
            foreach (var surface in surfaces)
                rows.Add(new BundleRow(p.Instance, p.Profile, surface));
        }
        if (_detected is not null)
        {
            var instanceHomes = new HashSet<string>(
                _state.Instances.Select(i => NormHome(i.Home)), StringComparer.OrdinalIgnoreCase);
            foreach (var shell in _detected.Shells)
            {
                var home = NormHome(shell.Home);
                rows.Add(new DetectedShellRow(shell, canAdopt: home.Length > 0 && !instanceHomes.Contains(home))
                {
                    // OS 里这个桌面端活着 ≠ PAD 启动过它：只记 LocalRunning，不点呼吸绿灯。
                    LocalRunning = shell.Running,
                });
            }
            foreach (var tui in _detected.Tuis)
            {
                if (string.IsNullOrWhiteSpace(tui.Package)) continue;
                var home = NormHome(tui.Home);
                if (home.Length > 0 && instanceHomes.Contains(home)) continue; // 已收编：实例自己的 profile 已列出
                rows.Add(new DetectedTuiRow(tui, canAdopt: true) { LocalRunning = tui.Running });
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

    /// <summary>
    /// 整合包列表的一行：PackCard 的显示包装。行左列 = 整合包自己的 icon（解析不到才回落「包」），
    /// 装到哪个实例不再显示（D105 UX，用户 2026-11-04：装态改到实例页用发光表达）。
    /// </summary>
    public sealed class PackItem(PackCard card, string? iconPath) : Observable
    {
        public PackCard Card { get; } = card;
        public string Title => string.IsNullOrWhiteSpace(Card.Title) ? Card.Id : Card.Title;
        public string Version => Card.Version;
        public string SummaryText => Card.SummaryText;
        public string Path => Card.Path;

        /// <summary>整合包自己的图标文件（缓存 png / json 旁源文件）；空 = 回落「包」pill。</summary>
        public string IconPath { get; } = iconPath ?? "";
        public bool HasIcon => IconPath.Length > 0;

        bool _busy;
        public bool Busy
        {
            get => _busy;
            set
            {
                if (!Set(ref _busy, value)) return;
                Raise(nameof(CanCreate));
                Raise(nameof(ActionText));
            }
        }

        public bool CanCreate => !Busy;
        public string ActionText => Busy ? "正在装…" : "新建实例";
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
