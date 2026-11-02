using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

using System.Windows.Media;
using System.Windows.Threading;
using Pad.Core;

namespace Pad.Views;

public partial class DownloadView : UserControl, IRefreshable
{
    readonly MainWindow _win;
    readonly AppState _state = AppState.Current;
    bool _pickingTarget;
    MarketRow? _detail;
    MarketPage? _lastPage;

    /// <summary>每页多少个按窗口高度自适应（近似 17px/个），不再固定 20。</summary>
    int StorePageSize => Math.Clamp((int)(_win.ActualHeight / 17), 10, 100);

    int _storePageSizeUsed = 20;
    bool _growthBusy;
    string _storeMode = "downloads";
    MarketRow? _heroRow;
    int _storePeriod = 7;
    DispatcherTimer? _storeResizeTimer;

    void HookResize()
    {
        SizeChanged += (_, _) =>
        {
            _storeResizeTimer ??= CreateStoreResizeTimer();
            _storeResizeTimer.Stop();
            _storeResizeTimer.Start();
        };
    }

    DispatcherTimer CreateStoreResizeTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            ReloadStoreForResize();
        };
        return t;
    }

    void ReloadStoreForResize()
    {
        if (!IsVisible || PaneMarket.Visibility != Visibility.Visible) return;
        var now = StorePageSize;
        if (now == _storePageSizeUsed) return;
        var pageIndex = _storePageSizeUsed > 0 ? _from / _storePageSizeUsed : 0;
        _storePageSizeUsed = now;
        _from = Math.Max(0, pageIndex * now);
        _ = Load();
    }
    string _tag = "";
    PluginCatalog? _catalog;
    string _detailHome = "";
    string _chip = "全部";

    /// <summary>Empty means the 发行号 pane; otherwise a shelf id from Market.Shelves.</summary>
    string _shelf = "";
    int _from;
    CancellationTokenSource? _live;
    bool _shelvesPainted;
    DispatcherTimer? _dlPoll;

    public DownloadView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        DataContext = _state;
        Loaded += (_, _) => PaintShelves();
        HookResize();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true) StopDlPoll();
            else if (_shelf == "dltasks") StartDlPoll();
        };
    }

    HttpClient Http()
    {
        var sec = Math.Clamp(_state.Settings.Download.InstallTimeoutSec, 5, 300);
        return new HttpClient { Timeout = TimeSpan.FromSeconds(sec) };
    }

    /// <summary>Switch the left rail. Used by --shot-page market|dl-tasks.</summary>
    public void OpenShelf(string id)
    {
        PaintShelves();
        if (id == "release")
        {
            NavRelease.IsChecked = true;
            return;
        }
        if (id is "dltasks" or "dl-tasks")
        {
            NavDlTasks.IsChecked = true;
            return;
        }
        foreach (var child in ShelfNav.Children.OfType<RadioButton>())
        {
            if ((child.Tag as string) == id)
            {
                child.IsChecked = true;
                return;
            }
        }
        if (ShelfNav.Children.OfType<RadioButton>().FirstOrDefault() is { } first)
            first.IsChecked = true;
    }

    void PaintShelves()
    {
        var want = string.Join('\n', Market.Shelves.Select(s => $"{s.Id}|{s.Label}|{s.Query}"));
        if (_shelvesPainted && (ShelfNav.Tag as string) == want) return;
        ShelfNav.Children.Clear();
        var glyphs = new Geometry?[]
        {
            TryGeom("I.Mod"), TryGeom("I.Data"), TryGeom("I.Sun"), TryGeom("I.Globe"),
        };
        var i = 0;
        foreach (var s in Market.Shelves)
        {
            var btn = new RadioButton
            {
                Style = (Style)FindResource("NavItem"),
                Content = s.Label,
                Tag = s.Id,
                GroupName = "dlNav",
            };
            if (glyphs[i % glyphs.Length] is { } g) Ico.SetOf(btn, g);
            btn.Checked += Nav_Checked;
            ShelfNav.Children.Add(btn);
            i++;
        }
        ShelfNav.Tag = want;
        _shelvesPainted = true;

    }

    Geometry? TryGeom(string key) =>
        TryFindResource(key) as Geometry;

    public void OnShown()
    {
        PaintShelves();
        var def = _state.Settings.Launch.DefaultRelease;
        if (string.IsNullOrWhiteSpace(VersionBox.Text) && def.Length > 0)
            VersionBox.Text = def;
        _state.ReloadReleases();
        // State the tool situation before the user hits install and gets a wall of pnpm text.
        var node = Proc.Which("node");
        var pnpm = Pnpm.Locate();
        Tool(NodePip, NodeLine, node, "不在 PATH");
        Tool(PnpmPip, PnpmLine, pnpm, "不在 PATH");

        ToolHint.Text = (node, pnpm) switch
        {
            (null, null) => "DSH 要 Node 22.19+ 或 24+，安装走 pnpm。两个都装上再回来。",
            (null, _) => "DSH 要 Node 22.19+ 或 24+。",
            (_, null) => "DSH 官方用 pnpm 装插件。",
            _ => "",
        };
        if (node is null && _state.Settings.Launch.RuntimeInstall)
            ToolHint.Text += " 启动时会自动装进 runtime/node/。";
        ToolHint.Visibility = ToolHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PickTarget();
        if (_shelf == "dltasks")
        {
            PaintDlTasks();
            StartDlPoll();
            return;
        }
        if (!_state.Settings.Download.AutoFetchOnOpen) return;
        if (string.IsNullOrEmpty(_shelf))
            _ = FetchVersions();
        else
        {
            _from = 0;
            _ = Load();
        }
    }

    void Tool(Border pip, TextBlock line, string? path, string missing)
    {
        var found = path is not null;
        pip.Background = (System.Windows.Media.Brush)FindResource(found ? "Success" : "Warning");
        line.Text = path ?? missing;
        line.Foreground = (System.Windows.Media.Brush)FindResource(found ? "TextBody" : "WarningText");
    }

    void Log(string line) => Dispatcher.BeginInvoke(() =>
    {
        LogBox.Visibility = Visibility.Visible;
        LogBox.Height = 170;
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    });

    // ---- 发行号 -----------------------------------------------------------

    async void Fetch_Click(object sender, RoutedEventArgs e) => await FetchVersions();

    async Task FetchVersions()
    {
        FetchBtn.IsEnabled = false;
        try
        {
            Log("[PAD] GET " + Market.RegistryBase + "/@deepseek-ai/dsh");
            using var http = Http();
            var json = await http.GetStringAsync(Market.RegistryBase + "/@deepseek-ai/dsh");
            var catalog = DshPackument.Parse(json);

            _state.SetRemoteReleases(catalog.Versions.Select(v => new NpmRelease
            {
                Version = v.Version,
                TimeText = DshPackument.TimeText(v.Time),
                Latest = v.Latest,
            }));

            VersionBox.Items.Clear();
            foreach (var v in catalog.Versions) VersionBox.Items.Add(v.Version);
            if (catalog.Latest is not null)
            {
                VersionBox.Text = catalog.Latest;
                Log($"[PAD] {catalog.Versions.Count} 个版本，latest = {catalog.Latest}");
            }
        }
        catch (Exception ex)
        {
            Log("[PAD] 获取版本列表失败：" + ex.Message);
        }
        finally
        {
            FetchBtn.IsEnabled = true;
        }
    }

    async void InstallRemote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: NpmRelease row }) return;
        VersionBox.Text = row.Version;
        await InstallVersion(row.Version);
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var version = (VersionBox.Text ?? "").Trim();
        if (version.Length == 0)
        {
            MessageBox.Show("先填一个发行号，或点刷新获取版本列表。", "PAD",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await InstallVersion(version);
    }

    async Task InstallVersion(string version)
    {
        InstallBtn.IsEnabled = false;
        Busy.Visibility = Visibility.Visible;
        if (InstallMeter is not null)
        {
            InstallMeter.Visibility = Visibility.Visible;
            Fx.MeterTo(InstallMeter, 6);
        }
        var progress = new Progress<string>(Log);
        var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        poll.Tick += (_, _) =>
        {
            var job = _state.Launcher.ListJobs()
                .FirstOrDefault(j => j.Kind == "install-version" && j.CanCancel);
            if (job is not null && InstallMeter is not null)
                Fx.MeterTo(InstallMeter, Math.Max(8, job.Percent));
        };
        poll.Start();
        var install = _state.Launcher.InstallRelease(version, progress, CancellationToken.None);
        try
        {
            var rec = await install;
            if (InstallMeter is not null) Fx.MeterTo(InstallMeter, 100);
            Log(rec.Verified
                ? $"[PAD] {version} 装好并通过校验：{rec.VerifyOutput}"
                : $"[PAD] {version} 装好了，但校验没通过：{rec.VerifyOutput}");
            _state.ReloadReleases();
            if (App.ShotPath is null) _win.ShowInner("versions", "版本选择");
        }
        catch (Exception ex)
        {
            Log(PadError.Describe(ex));
            _win.Toast(PadError.Describe(ex), bad: true);
        }
        finally
        {
            poll.Stop();
            InstallBtn.IsEnabled = true;
            Busy.Visibility = Visibility.Collapsed;
        }
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DshRelease rel }) return;
        try
        {
            _state.Launcher.RemoveRelease(rel.Version);
            _state.ReloadReleases();
        }
        catch (Exception ex)
        {
            Log(PadError.Describe(ex));
        }
    }

    async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DshRelease rel }) return;
        var bin = _state.Launcher.DshBin(rel.Version);
        if (!System.IO.File.Exists(bin))
        {
            _win.Toast("这份发行号没有 CLI 入口，先重装。", bad: true);
            return;
        }
        try
        {
            Log($"[PAD] node {bin} --version");
            var res = await Proc.Run("node", $"\"{bin}\" --version",
                _state.Launcher.VersionDir(rel.Version), null, CancellationToken.None);
            rel.Verified = res.ExitCode == 0;
            rel.VerifyOutput = (res.Tail ?? "").Trim();
            _state.Launcher.SaveRelease(rel);
            _state.ReloadReleases();
            Log(rel.Verified
                ? $"[PAD] 校验通过：{rel.VerifyOutput}"
                : $"[PAD] 校验没通过：{rel.VerifyOutput}");
        }
        catch (Exception ex)
        {
            Log(PadError.Describe(ex));
        }
    }

    void OpenVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DshRelease rel }) return;
        Reveal.Path(_state.Launcher.VersionDir(rel.Version));
    }

    // ---- 社区资源 --------------------------------------------------------

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PaneRelease is null || sender is not RadioButton { Tag: string tag }) return;

        var release = tag == "release";
        var tasks = tag == "dltasks";
        PaneRelease.Visibility = release ? Visibility.Visible : Visibility.Collapsed;
        PaneMarket.Visibility = !release && !tasks ? Visibility.Visible : Visibility.Collapsed;
        if (PaneDlTasks is not null)
            PaneDlTasks.Visibility = tasks ? Visibility.Visible : Visibility.Collapsed;

        if (tasks)
        {
            _shelf = "dltasks";
            PaintDlTasks();
            StartDlPoll();
            if (Fx.Animated && PaneDlTasks is not null) Fx.PageIn(PaneDlTasks);
            return;
        }

        StopDlPoll();
        ShowMarketList();
        if (Fx.Animated) Fx.PageIn(release ? PaneRelease : PaneMarket);
        if (release) { _shelf = ""; return; }

        if (_shelf == tag) return;
        _shelf = tag;
        var shelf = Shelf();
        ShelfTitle.Text = $"{shelf.Label} · {shelf.Query}";
        ShelfTitle.ToolTip = shelf.Query;
        SearchBox.Clear();
        _from = 0;
        PickTarget();
        _ = Load();
    }

    (string Id, string Label, string Query) Shelf() =>
        Market.Shelves.FirstOrDefault(s => s.Id == _shelf, Market.Shelves[0]);

    void StartDlPoll()
    {
        StopDlPoll();
        var sec = Math.Clamp(_state.Settings.Other.RefreshSec, 1, 30);
        _dlPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
        _dlPoll.Tick += (_, _) => PaintDlTasks();
        _dlPoll.Start();
    }

    void StopDlPoll()
    {
        _dlPoll?.Stop();
        _dlPoll = null;
    }

    void PaintDlTasks()
    {
        if (DlRunning is null || DlDone is null) return;
        var jobs = _state.Launcher.ListJobs();
        var run = DownloadDock.Running(jobs).Select(j => new JobCard { Job = j }).ToList();
        var done = DownloadDock.Done(jobs, 40).Select(j => new JobCard { Job = j }).ToList();
        DlRunning.ItemsSource = run;
        DlDone.ItemsSource = done;
        if (DlEmptyRun is not null)
            DlEmptyRun.Visibility = run.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (DlEmptyDone is not null)
            DlEmptyDone.Visibility = done.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        try
        {
            _state.Launcher.CancelJob(job.Id);
            _win.Toast("已取消 " + job.Title);
            PaintDlTasks();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    /// <summary>删除任务行：正在跑的先取消并结束进程，再从列表移除；不动已装文件。</summary>
    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        try
        {
            _state.Launcher.DeleteJob(job.Id);
            _win.Toast("已删除 " + job.Title);
            PaintDlTasks();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    /// <summary>暂停/继续：worker 在复制/压缩循环里检查暂停标记（不丢进度）。</summary>
    void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        try
        {
            if (job.Paused) _state.Launcher.ResumeJob(job.Id);
            else _state.Launcher.PauseJob(job.Id);
            _win.Toast(job.Paused ? "已继续 " + job.Title : "已暂停 " + job.Title);
            PaintDlTasks();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        // 点按钮时不触发行的“打开文件夹”。
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null) return;
        Reveal.Path(FolderOf(job));
    }

    static T? FindParent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T hit) return hit;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return null;
    }
    void Forget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        _state.Launcher.ForgetJob(job.Id);
        PaintDlTasks();
    }

    void DlClear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in DownloadDock.Done(_state.Launcher.ListJobs(), 1000))
            _state.Launcher.ForgetJob(job.Id);
        PaintDlTasks();
    }

    void DlRefresh_Click(object sender, RoutedEventArgs e) => PaintDlTasks();

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        Reveal.Path(FolderOf(job));
        _ = _state.Launcher.ReadJobLog(job.Id);
    }

    string FolderOf(JobRecord job)
    {
        if (job.Kind == "install-version" && !string.IsNullOrWhiteSpace(job.Label))
            return _state.Launcher.VersionDir(job.Label);
        if (!string.IsNullOrWhiteSpace(job.Instance))
        {
            var inst = _state.Instances.FirstOrDefault(i => i.Id == job.Instance);
            if (inst is { Home.Length: > 0 }) return inst.Home;
            return _state.Launcher.InstanceDir(job.Instance);
        }
        return _state.Launcher.JobLogPath(job.Id);
    }

    /// <summary>对照 PCL：版本=该实例钉的发行号，装到=哪份实例。</summary>
    void PickTarget()
    {
        _pickingTarget = true;
        try
        {
            TargetInstance.SelectedItem = _state.SelectedInstance ?? _state.Instances.FirstOrDefault();
            PaintTargetRelease(TargetInstance.SelectedItem as Instance);
        }
        finally { _pickingTarget = false; }
    }

    void TargetInstance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingTarget) return;
        if (TargetInstance.SelectedItem is not Instance inst)
        {
            PaintTargetRelease(null);
            return;
        }
        if (_state.SelectedInstance?.Id != inst.Id) _state.SelectedInstance = inst;
        PaintTargetRelease(inst);
    }

    void PaintTargetRelease(Instance? inst)
    {
        TargetRelease.Items.Clear();
        var ver = inst?.Dsh.Version ?? "";
        if (ver.Length == 0) return;
        TargetRelease.Items.Add(ver);
        TargetRelease.SelectedIndex = 0;
    }

    string? LaunchProfile(Instance inst)
    {
        if (_state.SelectedInstance?.Id == inst.Id && _state.SelectedProfile is { } cur)
            return cur.Name;
        return inst.LastProfile ?? _state.Launcher.Profiles(inst).FirstOrDefault()?.Name;
    }

    void Warn(string text)
    {
        var t = (text ?? "").Trim();
        MarketErrorTitle.Text = t.Contains("超时")
            ? "npm 搜索超时"
            : t.Contains("PA033") || t.Contains("没连上")
                ? "npm 搜索没连上"
                : "搜索出错了";
        // 原始错误里 error[..]/--> / | / = help 是给命令行看的；界面只留一句人话 + 一条建议。
        var lines = t.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('|').Trim())
            .Where(x => x.Length > 0)
            .Where(x => !x.StartsWith("error[") && !x.StartsWith("-->") && !x.Contains("= help"))
            .Select(x => x.Contains("A task was canceled", StringComparison.OrdinalIgnoreCase)
                ? "请求被取消（超时或网络不通）。" : x)
            .ToList();
        MarketErrorText.Text = lines.Count > 0
            ? string.Join(" ", lines.Take(2))
            : "请求被取消或超时，可能是网络 / 代理 / registry 不通。";
        MarketErrorHint.Text = "检查网络或代理；或到 设置 → 下载 改 registry / 超时，然后点重试。";
        MarketError.Visibility = Visibility.Visible;
    }

    void Working(string? what)
    {
        MarketStatus.Text = what ?? "";
        MarketStatus.Visibility = what is null ? Visibility.Collapsed : Visibility.Visible;
        MarketBusy.Visibility = what is null ? Visibility.Collapsed : Visibility.Visible;
        SearchBtn.IsEnabled = what is null;
        ClearBtn.IsEnabled = what is null;
    }

    /// <summary>搜索永远在「全部插件」里做；增长首页收到搜索词时先切回搜索模式。</summary>
    void RunSearch()
    {
        _from = 0;
        if (_storeMode != "all")
        {
            ModeAll.IsChecked = true; // 触发 StoreMode_Changed -> Load
            return;
        }
        _ = Load();
    }

    void Search_Click(object sender, RoutedEventArgs e) => RunSearch();

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        _from = 0;
        _ = Load();
    }

    void Search_Key(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        RunSearch();
    }

    void First_Click(object sender, RoutedEventArgs e) { _from = 0; _ = Load(); }

    void Prev_Click(object sender, RoutedEventArgs e)
    {
        _from = Math.Max(0, _from - StorePageSize);
        _ = Load();
    }

    void Next_Click(object sender, RoutedEventArgs e)
    {
        _from += StorePageSize;
        _ = Load();
    }

    /// <summary>
    /// One page of one shelf. The registry is the only source; when it is unreachable
    /// the shelf says so instead of rendering as an empty list.
    /// </summary>
    async Task Load()
    {
        if (_shelf == "dltasks") return;
        if (_storeMode != "all")
        {
            await LoadGrowth();
            return;
        }
        _live?.Cancel();
        var live = _live = new CancellationTokenSource();

        MarketError.Visibility = Visibility.Collapsed;
        ResultCount.Text = "";
        EmptyCard.Visibility = Visibility.Collapsed;
        var first = ResultList.ItemsSource is null;
        if (first) ResultCard.Visibility = Visibility.Collapsed;
        PagerCard.Visibility = Visibility.Collapsed;
        LoadCard.Visibility = first ? Visibility.Visible : Visibility.Collapsed;
        Working("正在搜索");

        try
        {
            _storePageSizeUsed = StorePageSize;
            var page = await Market.Search(Shelf().Query, SearchBox.Text, _from, live.Token, _storePageSizeUsed);
            if (live.IsCancellationRequested) return;
            Paint(page);
            PrefetchNext(page);
            if (App.ShotPage == "market-more")
            {
                StoreMore.Visibility = Visibility.Visible;
                StoreMoreAngle.Angle = 180;
            }
            if (App.ShotPage == "market-detail" && page.Rows.Count > 0) QueueStoreDetailShot(page.Rows[0]);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            ResultList.ItemsSource = null;
            ResultCard.Visibility = Visibility.Collapsed;
            Warn(PadError.Describe(ex));
        }
        finally
        {
            if (!live.IsCancellationRequested)
            {
                LoadCard.Visibility = Visibility.Collapsed;
                Working(null);
            }
        }
    }

    bool _storeShotQueued;
    void QueueStoreDetailShot(MarketRow row)
    {
        if (_storeShotQueued || App.ShotPath is not { Length: > 0 } shot) return;
        _storeShotQueued = true;
        var dir = System.IO.Path.GetDirectoryName(shot) ?? "";
        var path = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(shot) + "-store-detail.png");
        var inst = TargetInstance.SelectedItem as Instance;
        var plugin = new PluginRow
        {
            Profile = inst?.Name ?? "",
            Spec = row.Name,
            Version = row.Version,
            Description = row.DescriptionText,
            Repository = row.Repository,
            Keywords = row.Keywords,
            IconPath = row.IconPath,
            Latest = "",
        };
        var win = new PluginDetailWindow(
            plugin,
            install: null,
            versions: async () =>
            {
                var cat = await Market.FetchPackument(row.Name, CancellationToken.None);
                return cat.Versions.Count > 0 ? cat.Versions : cat.Files.Select(f => f.Version).ToList();
            },
            installVersion: _ => { })
        {
            ShowInTaskbar = false,
            Left = -20000,
            Top = -20000,
        };
        win.Loaded += (_, _) =>
        {
            win.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var wait = Task.WhenAll(
                        Task.WhenAny(win.ShotsLoaded, Task.Delay(TimeSpan.FromSeconds(8))),
                        Task.WhenAny(win.VersionsLoaded, Task.Delay(TimeSpan.FromSeconds(8))));
                    await wait;
                    win.RenderTo(path);
                }
                finally { win.Close(); }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        win.Show();
    }

    bool _tagBuilding;
    void BuildTagChoices(List<MarketRow> rows)
    {
        _tagBuilding = true;
        try
        {
            var tags = rows.Select(r => r.TagText)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
            var items = new List<object> { "全部" };
            items.AddRange(tags);
            TagBox.ItemsSource = items;
            TagBox.SelectedItem = _tag.Length > 0 && tags.Contains(_tag, StringComparer.OrdinalIgnoreCase)
                ? tags.First(t => string.Equals(t, _tag, StringComparison.OrdinalIgnoreCase))
                : "全部";
        }
        finally { _tagBuilding = false; }
    }

    void Tag_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_tagBuilding) return;
        _tag = TagBox.SelectedItem as string ?? "";
        if (_tag == "全部") _tag = "";
        if (_lastPage is not null) Paint(_lastPage);
    }

    List<MarketRow> ApplyStoreControls(List<MarketRow> rows)
    {
        IEnumerable<MarketRow> q = rows;
        if (_tag.Length > 0) q = q.Where(r => string.Equals(r.TagText, _tag, StringComparison.OrdinalIgnoreCase));
        if (InstalledOnly.IsChecked == true && TargetInstance.SelectedItem is Instance inst)
        {
            var have = _state.Launcher.ListBundles(inst)
                .Select(b => b.Spec)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            q = q.Where(r => have.Contains(r.Name));
        }
        var sort = (SortBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "hot";
        q = sort switch
        {
            "date" => q.OrderByDescending(r => DateTimeOffset.TryParse(r.Date, out var t) ? t : DateTimeOffset.MinValue),
            "name" => q.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => q.OrderByDescending(r => r.Popularity),
        };
        return q.ToList();
    }

    void StoreMore_Click(object sender, RoutedEventArgs e)
    {
        var show = StoreMore.Visibility != Visibility.Visible;
        StoreMore.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        StoreMoreAngle.Angle = show ? 180 : 0;
    }

    void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_lastPage is not null) Paint(_lastPage);
    }

    void StoreFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_lastPage is not null) Paint(_lastPage);
    }

    void MarketFav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MarketRow row }) return;
        var on = Favorites.Toggle(_state.Launcher.Root, row.Name);
        row.Favorite = on;
        _win.Toast(on ? $"已收藏 {row.Title}" : $"取消收藏 {row.Title}");
    }

    void Paint(MarketPage page)
    {
        _from = page.From;
        _lastPage = page;
        BuildTagChoices(page.Rows);
        var shown = ApplyStoreControls(page.Rows);
        foreach (var r in shown) r.Favorite = Favorites.IsFavorite(_state.Launcher.Root, r.Name);
        ResultList.ItemsSource = shown;
        _ = LoadMarketIcons(page.Rows);
        GrowthHero.Visibility = Visibility.Collapsed;
        ChipRow.Visibility = Visibility.Collapsed;
        StoreMoreBtn.Visibility = Visibility.Visible;
        StoreModeLink.Content = "‹ 返回增长榜";
        StoreSectionTitle.Text = "全部插件";
        StoreSectionSub.Text = $"{page.Total:N0} 个插件";
        StoreSectionRight.Text = SearchBox.Text.Trim().Length > 0 ? $"搜索“{SearchBox.Text.Trim()}”" : "";
        ScrollStoreTop();
        ResultCard.Visibility = page.Rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        EmptyText.Text = SearchBox.Text.Trim().Length == 0
            ? "无搜索结果"
            : "无搜索结果，请尝试搜索其英文名称";
        EmptyCard.Visibility = page.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ResultCount.Text = $"{page.Total:N0} 个插件";
        PageText.Text = $"{page.PageIndex + 1} / {page.PageCount}";
        FirstBtn.IsEnabled = page.HasPrev;
        PrevBtn.IsEnabled = page.HasPrev;
        NextBtn.IsEnabled = page.HasNext;
        PagerCard.Visibility = page.PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Install into a profile of the selected instance, through DSH's own
    /// <c>dsh plugin --profile &lt;name&gt; add</c>. The registry is asked first whether the
    /// package declares <c>dsh.bundle</c>, so the result can say whether it became a
    /// profile layer or a plain dependency rather than leaving the user to guess.
    /// </summary>
    /// <summary>Next page is one click away in practice; loading it while the user reads
    /// this one makes pagination feel instant (Market keeps a short-lived page cache).</summary>
    void PrefetchNext(MarketPage page)
    {
        if (!page.HasNext || _shelf == "dltasks") return;
        var query = Shelf().Query;
        var text = SearchBox.Text;
        var size = page.Size;
        var next = page.From + page.Size;
        _ = Task.Run(async () =>
        {
            try { await Market.Search(query, text, next, CancellationToken.None, size); }
            catch { /* prefetch is best-effort; the real load will surface errors */ }
        });
    }

    // ── 商店的三种模式：全部 / Star 增长 / 下载增长；增长不是单独页面。 ──

    void StoreMode_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } rb) return;
        _storeMode = rb.Name == nameof(ModeStars) ? "stars"
            : rb.Name == nameof(ModeDownloads) ? "downloads" : "all";
        if (!IsLoaded) return;
        _from = 0;
        _ = Load();
    }

    void StorePeriod_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } rb) return;
        _storePeriod = rb.Name == nameof(PeriodDay) ? 1 : rb.Name == nameof(PeriodMonth) ? 30 : 7;
        if (!IsLoaded || _storeMode == "all") return;
        _from = 0;
        _ = Load();
    }

    /// <summary>供 --shot-page 用：切到全部插件模式。</summary>
    public void ShowAllShot()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            ModeAll.IsChecked = true;
            _storeMode = "all";
            _from = 0;
            await Load();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>供 --shot-page 用：截收起的侧栏。</summary>
    public void CollapseRailForShot()
    {
        StoreRail.Visibility = Visibility.Collapsed;
        StoreRailShow.Visibility = Visibility.Visible;
    }

    /// <summary>供 --shot-page 用：打开下载增长模式看新商店的样子。</summary>
    public void ShowGrowthShot()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            ModeDownloads.IsChecked = true; // 触发 StoreMode_Changed
            _storeMode = "downloads";
            _from = 0;
            await Load();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void ScrollStoreTop() =>
        Dispatcher.BeginInvoke(() => MarketScroll.ScrollToVerticalOffset(0),
            System.Windows.Threading.DispatcherPriority.Loaded);

    void StoreModeLink_Click(object sender, RoutedEventArgs e)
    {
        if (_storeMode == "all") ModeDownloads.IsChecked = true;
        else ModeAll.IsChecked = true;
    }

    void HeroOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_heroRow is not null) OpenStoreDetail(_heroRow);
    }

    void StoreRailToggle_Click(object sender, RoutedEventArgs e)
    {
        var hide = StoreRail.Visibility == Visibility.Visible;
        StoreRail.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        StoreRailShow.Visibility = hide ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task LoadGrowth()
    {
        if (_growthBusy) return;
        _growthBusy = true;
        MarketError.Visibility = Visibility.Collapsed;
        EmptyCard.Visibility = Visibility.Collapsed;
        ResultCard.Visibility = Visibility.Visible;
        PagerCard.Visibility = Visibility.Collapsed;
        GrowthHero.Visibility = Visibility.Collapsed;
        ChipRow.Visibility = Visibility.Visible;
        StoreMoreBtn.Visibility = Visibility.Collapsed;
        StoreModeLink.Content = "浏览全部插件 ›";
        LoadCard.Visibility = ResultList.ItemsSource is null ? Visibility.Visible : Visibility.Collapsed;
        StoreSectionTitle.Text = "增长榜";
        StoreSectionSub.Text = _storeMode == "stars" ? "Star 增长" : "下载增长";
        StoreSectionRight.Text = "";
        Working("正在算增长榜");
        try
        {
            var boards = await MarketMetrics.GetAsync(_state.Launcher, false,
                new Progress<string>(x => StoreSectionRight.Text = x), CancellationToken.None);
            var periodKey = _storePeriod switch { 1 => "day", 7 => "week", _ => "month" };
            var board = boards.Boards.FirstOrDefault(b => b.Key == $"{_storeMode}-{periodKey}");
            var rows = (board?.Rows ?? []).Take(12).Select(m => new MarketRow
            {
                Name = m.Name,
                Version = m.Version,
                Publisher = m.Publisher,
                Description = m.Description,
                Repository = m.Repository,
                Homepage = m.Repository,
                Date = DateTimeOffset.UtcNow.ToString("o"),
                Rank = m.Rank,
                GrowthBadge = m.GrowthText,
                GrowthTotal = m.TotalText,
                GrowthSign = m.GrowthSign,
            }).ToList();
            foreach (var r in rows) r.Favorite = Favorites.IsFavorite(_state.Launcher.Root, r.Name);
            ResultList.ItemsSource = rows;
            StoreSectionSub.Text = $"{board?.Label ?? ""} · Top {rows.Count}";
            StoreSectionRight.Text = boards.FromCache ? $"缓存 · {boards.GeneratedText}" : $"更新 · {boards.GeneratedText}";
            var any = rows.Count > 0;
            ResultCard.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            EmptyCard.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            EmptyText.Text = "暂时拉不到增长数据（网络或 GitHub 限流）";
            if (any)
            {
                ScrollStoreTop();
                _heroRow = rows[0];
                HeroTitle.Text = _heroRow.Title;
                HeroDesc.Text = $"{_heroRow.DescriptionText} · 本期 {_heroRow.GrowthBadge}";
                HeroIcon.Spec = _heroRow.Name;
                GrowthHero.Visibility = Visibility.Visible;
                await LoadMarketIcons(rows);
                HeroIcon.IconPath = _heroRow.IconPath;
            }
        }
        catch (Exception ex)
        {
            ResultList.ItemsSource = null;
            ResultCard.Visibility = Visibility.Collapsed;
            EmptyCard.Visibility = Visibility.Visible;
            EmptyText.Text = PadError.Describe(ex);
        }
        finally
        {
            _growthBusy = false;
            LoadCard.Visibility = Visibility.Collapsed;
            Working(null);
        }
    }

    /// <summary>货架行逐个补“插件自己的图标”：官方 DSH 包用内置品牌图标，其余按 npm 元数据里的仓库取 logo。</summary>
    async Task LoadMarketIcons(IReadOnlyList<MarketRow> rows)
    {
        // Icons are one GitHub fetch each; a semaphore keeps the page responsive while
        // letting several rows finish together instead of waiting one by one.
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(rows.Select(async row =>
        {
            await gate.WaitAsync();
            try
            {
                var icon = await BundleIcon.EnsureFromRepo(_state.Launcher.Root, row.Name,
                    row.Repository.Length > 0 ? row.Repository : row.Homepage, CancellationToken.None);
                if (icon is not null) row.IconPath = icon;
            }
            catch { /* 网络/仓库不可达就用占位字形 */ }
            finally { gate.Release(); }
        }));
    }

    async Task InstallFrom(MarketRow row, string version, FrameworkElement? busy)
    {
        if (TargetInstance.SelectedItem is not Instance inst)
        {
            _win.ShowInner("versions", "版本选择");
            return;
        }
        var profile = LaunchProfile(inst);
        if (string.IsNullOrEmpty(profile))
        {
            _win.ShowInner("instance", "版本设置");
            return;
        }

        _state.SelectedInstance = inst;
        MarketError.Visibility = Visibility.Collapsed;
        if (busy is Button btn) btn.IsEnabled = false;
        var spec = version.Length > 0 ? $"{row.Name}@{version}" : row.Name;
        _win.Toast($"开始装 {row.Name} → {inst.Name}");
        Working($"正在装 {row.Name}");
        try
        {
            var layer = await Market.IsBundle(row.Name, version.Length > 0 ? version : row.Version,
                CancellationToken.None);
            await _state.Launcher.AddBundles(inst, profile, [spec], null, CancellationToken.None);
            var where = inst.Name;
            _state.Reload();
            PickTarget();
            _win.Toast(layer
                ? $"{row.Name} 装进 {where}，已进 dsh.profile.bundles"
                : $"{row.Name} 装进 {where}，是普通依赖，没进 dsh.profile.bundles");
        }
        catch (Exception ex)
        {
            Warn(PadError.Describe(ex));
            _win.Toast($"{row.Name} 没装成", bad: true);
        }
        finally
        {
            if (busy is Button b) b.IsEnabled = true;
            Working(null);
        }
    }

    void Row_OpenDetail(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MarketRow row }) return;
        OpenStoreDetail(row);
    }

    /// <summary>App Store 式插件详情窗口：商店卡片、排行榜行共用同一个入口。</summary>
    void OpenStoreDetail(MarketRow row)
    {
        var inst = TargetInstance.SelectedItem as Instance;
        var plugin = new PluginRow
        {
            Profile = inst?.Name ?? "",
            Spec = row.Name,
            Version = row.Version,
            Description = row.DescriptionText,
            Repository = row.Repository,
            Keywords = row.Keywords,
            IconPath = row.IconPath,
            Latest = "",
        };
        // 商店详情也用 App Store 式窗口；安装走现有 InstallFrom（默认装最新版本）
        var win = new PluginDetailWindow(
            plugin,
            install: null,
            versions: async () =>
            {
                var cat = await Market.FetchPackument(row.Name, CancellationToken.None);
                return cat.Versions.Count > 0 ? cat.Versions : cat.Files.Select(f => f.Version).ToList();
            },
            installVersion: version => _ = InstallFrom(row, version, null))
        {
            Owner = _win,
        };
        win.ShowDialog();
    }

    void ShowMarketList()
    {
        if (PaneDetail is null || PaneMarketList is null) return;
        PaneDetail.Visibility = Visibility.Collapsed;
        PaneMarketList.Visibility = Visibility.Visible;
    }

    async Task OpenDetail(MarketRow row)
    {
        _detail = row;
        _detailHome = row.Homepage;
        _chip = "全部";
        _catalog = SeedCatalog(row);
        PaneMarketList.Visibility = Visibility.Collapsed;
        PaneDetail.Visibility = Visibility.Visible;
        DetailHeadTitle.Text = row.Name;
        DetailTitle.Text = row.Name;
        DetailMono.Content = row.Monogram;
        DetailBody.Text = row.DescriptionText;
        DetailTags.Text = row.Keywords.Count > 0 ? string.Join(" · ", row.Keywords) : "";
        DetailBundle.Text = "正在读 npm packument…";
        DetailMeta.Text = row.Publisher.Length > 0 ? row.Publisher : "";
        DetailHomeBtn.IsEnabled = _detailHome.Length > 0;
        PaintChips();
        PaintFiles();
        try
        {
            var cat = await Market.FetchPackument(row.Name, CancellationToken.None);
            if (_detail?.Name != row.Name) return;
            _catalog = cat;
            DetailBody.Text = cat.Description.Length > 0 ? cat.Description : row.DescriptionText;
            DetailBundle.Text = cat.IsBundle
                ? "有 dsh.bundle，会进 dsh.profile.bundles"
                : "普通依赖，没进 dsh.profile.bundles";
            if (cat.Keywords.Count > 0) DetailTags.Text = string.Join(" · ", cat.Keywords);
            if (cat.Homepage.Length > 0) _detailHome = cat.Homepage;
            DetailHomeBtn.IsEnabled = _detailHome.Length > 0;
            var latest = cat.Files.FirstOrDefault(f => f.Latest);
            var bits = new List<string>();
            if (cat.Latest.Length > 0) bits.Add("latest " + cat.Latest);
            if (latest?.TimeText is { Length: > 0 } t) bits.Add(t);
            bits.Add("npm");
            DetailMeta.Text = string.Join(" · ", bits);
            PaintChips();
            PaintFiles();
        }
        catch (Exception ex)
        {
            DetailBundle.Text = PadError.Describe(ex);
        }
    }

    static PluginCatalog SeedCatalog(MarketRow row)
    {
        var cat = new PluginCatalog { Name = row.Name, Description = row.Description, Homepage = row.Homepage };
        if (row.Version.Length == 0) return cat;
        cat.Latest = row.Version;
        cat.Versions.Add(row.Version);
        cat.Files.Add(new PluginFile
        {
            Name = row.Name,
            Version = row.Version,
            Time = row.Date,
            Latest = true,
        });
        return cat;
    }

    void PaintChips()
    {
        if (DetailChips is null) return;
        DetailChips.Children.Clear();
        var labels = new List<string> { "全部" };
        if (_catalog is { Latest.Length: > 0 }) labels.Add("latest");
        if (_catalog is not null)
            foreach (var c in _catalog.Files.Select(f => f.Chip).Where(s => s.Length > 0).Distinct())
                labels.Add(c);
        if (!labels.Contains(_chip)) _chip = "全部";
        foreach (var label in labels)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("FilterTab"),
                Content = label,
                GroupName = "dlChip",
                MinWidth = 56,
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = label == _chip,
            };
            rb.Checked += Chip_Checked;
            DetailChips.Children.Add(rb);
        }
    }

    void Chip_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Content: string label }) return;
        if (_chip == label) return;
        _chip = label;
        PaintFiles();
    }

    void PaintFiles()
    {
        if (DetailFileList is null) return;
        IEnumerable<PluginFile> files = _catalog?.Files ?? [];
        files = _chip switch
        {
            "全部" => files,
            "latest" => files.Where(f => f.Latest),
            _ => files.Where(f => f.Chip == _chip),
        };
        var list = files.ToList();
        DetailFileList.ItemsSource = list;
        if (DetailFileHead is not null)
            DetailFileHead.Text = _chip switch
            {
                "全部" => "可下版本",
                "latest" => "latest",
                _ => _chip,
            };
    }

    void DetailBack_Click(object sender, RoutedEventArgs e) => ShowMarketList();

    void DetailHome_Click(object sender, RoutedEventArgs e) => OpenUrl(_detailHome);

    void DetailNpm_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;
        OpenUrl("https://www.npmjs.com/package/" + _detail.Name);
    }

    void DetailCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;
        Clipboard.SetText(_detail.Name);
        _win.Toast($"已复制 {_detail.Name}");
    }

    void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    async void File_Install(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;
        if (sender is not FrameworkElement { Tag: PluginFile file }) return;
        await InstallFrom(_detail, file.Version, sender as Button);
    }

    void File_InstallRow(object sender, MouseButtonEventArgs e) => File_Install(sender, e);
}
