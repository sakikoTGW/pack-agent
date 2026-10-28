using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using Pad.Core;
using Pad.Views;

namespace Pad;

public partial class MainWindow : Window
{
    readonly Dictionary<string, UIElement> _pages = [];
    string _back = "launch";
    string _page = "launch";
    DispatcherFrame? _msgFrame;
    bool _msgAccepted;

    static readonly Dictionary<string, string> PageNames = new()
    {
        ["launch"] = "启动",
        ["manage"] = "管理",
        ["tasks"] = "任务",
        ["download"] = "下载",
        ["settings"] = "设置",
    };

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnMdLink));
        PreviewKeyDown += Window_PreviewKeyDown;
        StageRailList.CollapseClicked += () => ToggleRail();
        ApplyWindowPrefs();
        ApplyBrand();
        Show("launch");
        Loaded += (_, _) =>
        {
            UiWatchdog.QuietFor(TimeSpan.FromSeconds(15));
            MoveTabPill(CurrentTab());
            ApplyBrand();
            ApplyWindowPrefs();
            if (AppState.Current.Settings.Other.LoadWarning is { Length: > 0 } warn)
                Toast(warn, bad: true);
            if (App.ShotPath is null) OfferAdoptIfNeeded();
            if (App.ShotPath is null) PickupSidecarPacks();
            if (App.ShotPage != "launch" || App.ShotPath is not null)
            {
                var tab = App.ShotPage switch
                {
                    "manage" or "manage-edit" or "manage-open" => TabManage,
                    // 注意：`X or _ when G` 的 when 是整个 arm 的守卫，`relations` 本身不含
                    // "relations-" 会被守卫否决——所以这里用显式布尔，不玩模式组合。
                    _ when App.ShotPage.StartsWith("relations") => TabManage,
                    "tasks" => TabTasks,
                    "download" or "market" or "dl-tasks" => TabDownload,
                    "settings" or "settings-ui" or "settings-download" or "settings-other"
                        or "settings-launch" or "settings-api" => TabSettings,
                    _ => TabLaunch,
                };
                tab.IsChecked = true;
                if (App.ShotPath is not null)
                    System.IO.File.AppendAllText(App.ShotPath + ".pill.txt",
                        $"set {tab.Name} manage={TabManage.IsChecked} launch={TabLaunch.IsChecked}\n");
                if (App.ShotPage is "versions" or "versions-open")
                    ShowInner("versions", "版本选择");
                if (App.ShotPage == "makepack") ShowInner("makepack", "导出整合包", "versions");
                if (App.ShotPage == "bundles") ShowInner("bundles", "实例与组合包", "manage");
                if (App.ShotPage == "relations" || App.ShotPage.StartsWith("relations-")) ShowRelations();
                if (App.ShotPage is "instance" or "instance-settings" or "instance-plugins" or "instance-bundles")
                    ShowInner("instance", "版本设置");
                if (App.ShotPage == "market" && Page("download") is DownloadView market)
                    market.OpenShelf("plugin");
                if (App.ShotPage == "dl-tasks" && Page("download") is DownloadView dlTasks)
                    dlTasks.OpenShelf("dltasks");
                if (App.ShotPage is "settings-ui" or "settings-download" or "settings-other"
                    or "settings-launch" or "settings-api")
                {
                    TabSettings.IsChecked = true;
                    if (Page("settings") is SettingsView sv)
                        sv.OpenLayer(App.ShotPage["settings-".Length..]);
                }
            }
            if (App.ShotPath is not null) ScheduleShot(App.ShotPath);
            if (App.ShotPath is null) PrewarmPages();
        };
    }

    /// <summary>
    /// Building a page's visual tree costs more than animating it. Pay that while the
    /// window is idle (one page per idle turn, so it never becomes a startup hitch), and
    /// the first click on 管理 / 下载 / 设置 only pays layout.
    /// </summary>
    void PrewarmPages()
    {
        var queue = new Queue<string>(["manage", "download", "settings", "tasks"]);
        void Step()
        {
            if (queue.Count == 0) return;
            var name = queue.Dequeue();
            try
            {
                var page = Page(name);
                if (page is FrameworkElement fe && fe.Parent is null)
                    fe.Measure(new Size(Math.Max(ActualWidth, 800), Math.Max(ActualHeight, 520)));
                // 管理 paints from a disk scan; start it now so the first switch has cards to
                // stagger instead of an empty grid.
                if (page is WorkbenchView wb) wb.Prepare();
            }
            catch (Exception ex) { App.ReportPage(ex); }
            Dispatcher.BeginInvoke(new Action(Step), DispatcherPriority.ApplicationIdle);
        }
        Dispatcher.BeginInvoke(new Action(Step), DispatcherPriority.ApplicationIdle);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WinIcon.Apply(this);
        Dwm.Round(this);
    }

    /// <summary>
    /// Window geometry belongs to the page, not to the pointer (D26). Management carries the
    /// card grid and takes 72%x86% of the work area; every other page stays compact at the
    /// size recorded in pad.json. Both are clamped so a 150% DPI panel never hangs off-screen.
    /// </summary>
    public void ApplyWindowPrefs()
    {
        var ui = AppState.Current.Settings.Ui;
        // The opaque window owns no outer band: DWM draws the shadow and the corners.
        OuterShell.Margin = AllowsTransparency ? new Thickness(ui.WindowMargin) : new Thickness(0);
        ChromeRow.Height = new GridLength(ui.ChromeHeight);
        // Window.Opacity only exists on a layered window; on an opaque one it is ignored
        // by the compositor, so do not pretend to apply it.
        if (AllowsTransparency) Opacity = ui.WindowOpacity / 100.0;
        // Settings applied from the page: the ring is always drawn there.
        Fx.Edge(EdgeRing, true, 1);
        ApplyPageSize(_page, animate: false);
        ApplyHiddenTabs();
        ApplySkin();
    }

    /// <summary>Last sizing decision; surfaced in the --shot sidecar so a mismatch is provable.</summary>
    string _lastSize = "(none)";

    public bool ApplyPageSize(string page, bool animate)
    {
        var work = SystemParameters.WorkArea;
        var maxW = Math.Max(880, work.Width - 24);
        var maxH = Math.Max(520, work.Height - 24);

        double wantW, wantH;
        if (page is "manage" or "bundles" or "relations")
        {
            wantW = Math.Min(Math.Max(work.Width * 0.72, 1200), maxW);
            wantH = Math.Min(Math.Max(work.Height * 0.86, 760), maxH);
        }
        else
        {
            var ui = AppState.Current.Settings.Ui;
            wantW = Math.Min(ui.WindowWidth, maxW);
            wantH = Math.Min(ui.WindowHeight, maxH);
        }

        if (Math.Abs(Width - wantW) < 1 && Math.Abs(Height - wantH) < 1) return false;

        // Anchor the top-right corner. The window widens to the LEFT first, then the bottom
        // edge travels down. Centring would move four edges at once and read as one blurry
        // jump; two anchored beats read as two edges.
        var left = Math.Max(work.Left, Math.Min(Left + (Width - wantW), work.Right - wantW));
        var top = Math.Max(work.Top, Math.Min(Top, work.Bottom - wantH));

        _lastSize = $"{page}: want {wantW:F0}x{wantH:F0} from {Width:F0}x{Height:F0} animate={animate} fx={Fx.Animated}";

        if (!animate || !Fx.Animated)
        {
            _stagedGen++;
            Fx.StopBounds();
            Width = wantW;
            Height = wantH;
            Left = left;
            Top = top;
            return false;
        }

        var grow = wantW > Width || wantH > Height;
        BeginStagedBounds(left, top, wantW, wantH, grow);
        return true;
    }

    int _stagedGen;

    /// <summary>
    /// Two beats with a deliberate hold between them: widen to the left (~190ms), hold a
    /// beat (~70ms), then let the bottom edge travel down (~190ms). Shrinking mirrors the
    /// path — bottom up, hold, left in — so the window leaves the way it arrived.
    /// </summary>
    void BeginStagedBounds(double left, double top, double width, double height, bool grow)
    {
        const int beat = 190;
        const int hold = 70;
        var gen = ++_stagedGen;
        if (grow)
        {
            Fx.StepBounds(this, left, null, width, null, beat);
            AfterDelay(beat + hold, () =>
            {
                if (gen != _stagedGen) return;
                Fx.StepBounds(this, null, top, null, height, beat);
            });
        }
        else
        {
            Fx.StepBounds(this, null, top, null, height, beat);
            AfterDelay(beat + hold, () =>
            {
                if (gen != _stagedGen) return;
                Fx.StepBounds(this, left, null, width, null, beat);
            });
        }
    }

    void AfterDelay(int ms, Action run)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            run();
        };
        timer.Start();
    }

    /// <summary>Hide manage/download per pad.json; F12 restores them for this run.</summary>
    void ApplyHiddenTabs()
    {
        var launcher = AppState.Current.Launcher;
        var hidden = AppState.Current.Settings.Ui.HiddenTabs;
        TabManage.Visibility = PadRegistry.PageEnabled(launcher, "manage")
            ? Visibility.Visible : Visibility.Collapsed;
        TabTasks.Visibility = PadRegistry.PageEnabled(launcher, "tasks")
            ? Visibility.Visible : Visibility.Collapsed;
        TabDownload.Visibility = hidden.Contains("download") || !PadRegistry.PageEnabled(launcher, "download")
            ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Resolve a single wallpaper: a random image from wallpaperDir, else wallpaperPath.</summary>
    static string? WallpaperPick(UiPrefs ui)
    {
        if (!string.IsNullOrWhiteSpace(ui.WallpaperDir))
        {
            var dir = Skin.Resolve(ui.WallpaperDir);
            if (dir.Length > 0 && System.IO.Directory.Exists(dir))
            {
                var imgs = System.IO.Directory.EnumerateFiles(dir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                    .Where(LaunchPolicy.IsImage).ToArray();
                if (imgs.Length > 0) return imgs[Random.Shared.Next(imgs.Length)];
            }
        }
        return ui.WallpaperPath;
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MsgLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                EndMsg(false);
                e.Handled = true;
            }
            else if (e.Key is Key.F11 or Key.F12)
                e.Handled = true;
            return;
        }
        if (MdLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                HideMd();
                e.Handled = true;
            }
            else if (e.Key is Key.F11 or Key.F12)
                e.Handled = true;
            return;
        }
        if (_pages.TryGetValue("instance", out var instPage) && instPage is InstanceView iv && iv.IntroEditOpen)
        {
            if (e.Key == Key.Escape)
            {
                iv.CloseIntroEdit();
                e.Handled = true;
            }
            else if (e.Key is Key.F11 or Key.F12)
                e.Handled = true;
            return;
        }
        if (e.Key == Key.F11 && _page == "versions")
        {
            if (Page("versions") is VersionsView vv)
            {
                vv.ToggleHidden();
                e.Handled = true;
            }
            return;
        }
        if (e.Key != Key.F12) return;
        TabManage.Visibility = Visibility.Visible;
        TabDownload.Visibility = Visibility.Visible;
        if (Page("launch") is LaunchView lv) lv.RevealVersionEntry();
        e.Handled = true;
    }

    public void ApplyBrand()
    {
        var ui = AppState.Current.Settings.Ui;
        BrandMark.Visibility = ui.ShowBrand || ui.ShowLogo
            ? Visibility.Visible : Visibility.Collapsed;
        var logoOn = ui.ShowLogo ? Visibility.Visible : Visibility.Collapsed;
        BrandLogoPlate.Visibility = logoOn;
        BrandLogo.Visibility = logoOn;
        BrandText.Text = string.IsNullOrWhiteSpace(ui.BrandText) ? "PAD" : ui.BrandText.Trim();
        BrandText.Visibility = ui.ShowBrand ? Visibility.Visible : Visibility.Collapsed;
        ApplyWindowPrefs();
    }

    /// <summary>Logo + wallpaper + wash opacities from pad.json.</summary>
    public void ApplySkin()
    {
        var ui = AppState.Current.Settings.Ui;
        var bundled = new Uri("pack://application:,,,/Assets/pad-logo.png");
        BrandLogo.Source = Skin.LoadBitmap(ui.LogoPath, bundled);

        var paper = Skin.LoadBitmap(WallpaperPick(ui));
        if (paper is null)
        {
            Wallpaper.Visibility = Visibility.Collapsed;
            Wallpaper.Source = null;
            Wallpaper.Effect = null;
        }
        else
        {
            Wallpaper.Source = paper;
            Wallpaper.Stretch = Skin.Fit(ui.WallpaperFit);
            Wallpaper.Opacity = ui.WallpaperOpacity / 100.0;
            Wallpaper.Effect = ui.WallpaperBlur > 0
                ? new System.Windows.Media.Effects.BlurEffect { Radius = ui.WallpaperBlur, KernelType = System.Windows.Media.Effects.KernelType.Gaussian }
                : null;
            Wallpaper.Visibility = Visibility.Visible;
        }

        var page = (Brush)FindResource("BgPage");
        var chrome = (Brush)FindResource("BgChrome");
        PageWash.Background = Skin.Wash(page, ui.ContentOpacity);
        ChromeBar.Background = Skin.Wash(chrome, ui.ChromeOpacity);
    }

    /// <summary>Render the live visual tree to a PNG, at the real device DPI.</summary>
    void ScheduleShot(string path)
    {
        var other = AppState.Current.Settings.Other;
        var ms = App.ShotPage is "market" or "relations" || App.ShotPage.StartsWith("relations-sessions")
                ? Math.Max(other.ShotMarketDelayMs, 8000)
                : other.ShotDelayMs;
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ms),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                var w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
                var h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
                var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                    PixelFormats.Pbgra32);
                rtb.Render(this);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(path);
                png.Save(fs);
                System.IO.File.WriteAllText(path + ".txt",
                    $"logical {ActualWidth}x{ActualHeight}\ndpi {dpi.DpiScaleX}x{dpi.DpiScaleY}\npixels {w}x{h}\n"
                    + $"host {(Host.Content as FrameworkElement)?.ActualWidth}x{(Host.Content as FrameworkElement)?.ActualHeight}\n"
                    + $"size {_lastSize}\n");
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(path + ".txt", ex.ToString());
            }
            if (App.ShotExit) Close();
        };
        timer.Start();
    }

    UIElement Page(string name) => _pages.TryGetValue(name, out var cached)
        ? cached
        : _pages[name] = name switch
        {
            "launch" => new LaunchView(this),
            "manage" => new WorkbenchView(this),
            "bundles" => new ManageView(this),
            "relations" => new RelationsWebView(),
            "tasks" => new TaskView(this),
            "download" => new DownloadView(this),
            "settings" => new SettingsView(),
            "versions" => new VersionsView(this),
            "makepack" => new MakePackView(this),
            "instance" => new InstanceView(this),
            _ => new LaunchView(this),
        };

    public UIElement PageOf(string name) => Page(name);

    public void OpenSettingsApi()
    {
        Show("settings");
        TabSettings.IsChecked = true;
        if (Page("settings") is SettingsView sv) sv.OpenLayer("api");
    }

    /// <summary>Check a title tab so empty-state buttons can send the user to the next page.</summary>
    public void ShowTab(string name)
    {
        var tab = name switch
        {
            "manage" => TabManage,
            "tasks" => TabTasks,
            "download" => TabDownload,
            "settings" => TabSettings,
            _ => TabLaunch,
        };
        tab.IsChecked = true;
    }

    /// <summary>Open the download page on a community shelf. Used by 下载新插件.</summary>
    public void OpenDownloadShelf(string id)
    {
        Show("download");
        TabDownload.IsChecked = true;
        if (Page("download") is DownloadView dv) dv.OpenShelf(id);
    }

    /// <summary>Switch a top-level page. Restores the tab strip.</summary>
    public void Show(string name)
    {
        if (InnerMode.Visibility == Visibility.Visible) Fx.Reveal(TopMode);
        TopMode.Visibility = Visibility.Visible;
        InnerMode.Visibility = Visibility.Collapsed;
        Swap(name);
    }

    /// <summary>Open an inner page: the tab strip is replaced by a back widget and a heading.</summary>
    public void ShowInner(string name, string title, string back = "launch")
    {
        _back = back;
        BackBtn.ToolTip = "返回" + (PageNames.TryGetValue(back, out var label) ? label : "启动");
        if (name == "instance")
        {
            InnerTitle.Text = InstanceSetupTitle();
            InnerBadge.Visibility = Visibility.Collapsed;
        }
        else
        {
            InnerTitle.Text = title;
            var host = AppState.Current.SelectedInstance?.Name;
            InnerBadge.Text = host ?? "";
            InnerBadge.Visibility = string.IsNullOrEmpty(host) ? Visibility.Collapsed : Visibility.Visible;
        }

        if (TopMode.Visibility == Visibility.Visible) Fx.Reveal(InnerMode);
        TopMode.Visibility = Visibility.Collapsed;
        InnerMode.Visibility = Visibility.Visible;
        Swap(name);
    }

    public void RefreshInnerTitle()
    {
        if (InnerMode.Visibility != Visibility.Visible) return;
        if (Host.Content is InstanceView)
            InnerTitle.Text = InstanceSetupTitle();
    }

    /// <summary>制作整合包取消/做完 → 回版本选择（还是内页，不回启动页）。</summary>
    public void BackToVersions() => ShowInner("versions", "版本选择");

    static string InstanceSetupTitle()
    {
        var n = AppState.Current.SelectedInstance?.Name;
        return string.IsNullOrEmpty(n) ? "版本设置" : "版本设置 - " + n;
    }

    public void OpenSettingsLaunch()
    {
        Show("settings");
        TabSettings.IsChecked = true;
        if (Page("settings") is SettingsView sv) sv.OpenLayer("launch");
    }

    void Swap(string name)
    {
        _page = name;
        UiWatchdog.QuietFor(TimeSpan.FromSeconds(15));
        try
        {
            var page = Page(name);
            if (page is DependencyObject d)
                VisualTreeGuard.ThrowIfSelfHosted(d);
            var same = ReferenceEquals(Host.Content, page);

            // Order matters: detach the old page, start the bounds travel, attach the rail,
            // then hand the motion to the page's own parts. The page is visible the whole
            // time — a switch must never show a blank rectangle — and each page arrives with
            // its own choreography (see MotionFor).
            if (!same) Host.Content = page;
            var resizing = ApplyPageSize(name, animate: true);
            AttachStageRail(page, animate: false);
            if (!same)
            {
                if (!resizing) UpdateLayout();
                Fx.BusyFor(resizing ? 820 : 460);
                Fx.EnterPage(page, MotionFor(name));
                if (resizing)
                {
                    // The edge cannot be drawn crisply while the window is between sizes:
                    // fade it out for the travel, back in once the bounds have landed.
                    Fx.Edge(EdgeRing, false, 90);
                    AfterDelay(470, () => Fx.Edge(EdgeRing, true, 160));
                }
                else
                {
                    Fx.Edge(EdgeRing, true, 1);
                }
            }

            // Data refresh reads the disk and process table. Run it after the motion has
            // landed, not while it paints.
            if (page is IRefreshable r)
                RefreshAfterEntrance(page, r, same ? 0 : resizing ? 720 : 340);
        }
        catch (Exception ex)
        {
            App.ReportPage(ex);
        }
    }

    /// <summary>
    /// One motion per page, and they are meant to look different: 启动 is a combo (hero block
    /// travels, the controls cascade under it), 管理 is a wave ordered by screen position,
    /// 下载 slides in from its rail, 设置 and 版本 assemble one part at a time. A switch
    /// should feel like a different place, not the same template replayed.
    /// </summary>
    static PageMotion MotionFor(string name) => name switch
    {
        "launch" => PageMotion.Combo,
        "manage" => PageMotion.Wave,
        "download" => PageMotion.Slide,
        "settings" => PageMotion.Sequential,
        "versions" => PageMotion.Sequential,
        "tasks" => PageMotion.Drift,
        "instance" or "bundles" or "makepack" => PageMotion.Rows,
        "relations" => PageMotion.Drift,
        _ => PageMotion.Drift,
    };

    void RefreshAfterEntrance(UIElement page, IRefreshable refresh, int delayMs)
    {
        void Run()
        {
            if (!ReferenceEquals(Host.Content, page)) return;
            try { refresh.OnShown(); }
            catch (Exception ex) { App.ReportPage(ex); }
        }

        if (delayMs <= 0)
        {
            Dispatcher.BeginInvoke(new Action(Run), DispatcherPriority.Background);
            return;
        }
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Fx.Animated ? delayMs : 40),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Run();
        };
        timer.Start();
    }

    // ------------------------------------------------------------------ 舞台侧栏

    const double RailW = 236;
    bool _railOpen = true;

    /// <summary>
    /// 侧栏容器归舞台（D35）：页面带来自己的视角，挂接 = 把视角灌进容器；
    /// 其余页面容器向左滑出、内容区连续重排。页面只有"重新组合"，没有"换了一个"。
    /// </summary>
    void AttachStageRail(UIElement page, bool animate)
    {
        switch (page)
        {
            case WorkbenchView wb:
                wb.AttachRail(StageRailList);
                ShowRail(animate);
                break;
            case RelationsWebView rw:
                rw.AttachRail(StageRailList);
                rw.GoBack = () => Show("manage");
                ShowRail(animate);
                break;
            default:
                HideRail(animate);
                break;
        }
    }

    void ShowRail(bool animate = true)
    {
        StageRailList.ShowCollapse = true;
        RailEdge.Visibility = Visibility.Collapsed;
        StageRail.Visibility = Visibility.Visible;
        if (!_railOpen)
        {
            // 页面换了但用户收着侧栏：保持收起，把手留在原位
            StageRail.Width = 0;
            StageRailShift.X = -RailW;
            RailEdge.Visibility = Visibility.Visible;
            return;
        }
        if (!animate)
        {
            // A page switch must not animate layout: resizing this rail reflows the
            // whole page every frame. Snap it, and let the page entrance be the motion.
            StageRail.BeginAnimation(FrameworkElement.WidthProperty, null);
            StageRail.Width = RailW;
            StageRailShift.BeginAnimation(TranslateTransform.XProperty, null);
            StageRailShift.X = 0;
            return;
        }
        Fx.WidthTo(StageRail, RailW);
        Fx.Slide(StageRailShift, 0);
    }

    void HideRail(bool animate = true)
    {
        RailEdge.Visibility = Visibility.Collapsed;
        if (StageRail.Visibility != Visibility.Visible) { StageRail.Width = 0; return; }
        if (!animate || !Fx.Animated)
        {
            StageRail.BeginAnimation(FrameworkElement.WidthProperty, null);
            StageRail.Width = 0;
            StageRailShift.BeginAnimation(TranslateTransform.XProperty, null);
            StageRailShift.X = -RailW;
            StageRail.Visibility = Visibility.Collapsed;
            return;
        }
        Fx.WidthTo(StageRail, 0);
        Fx.Slide(StageRailShift, -RailW);
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (StageRail.Width < 1) StageRail.Visibility = Visibility.Collapsed;
        };
        t.Start();
    }

    void ToggleRail()
    {
        _railOpen = !_railOpen;
        if (_railOpen)
        {
            RailEdge.Visibility = Visibility.Collapsed;
            StageRail.Visibility = Visibility.Visible;
            Fx.WidthTo(StageRail, RailW);
            Fx.Slide(StageRailShift, 0);
        }
        else
        {
            Fx.WidthTo(StageRail, 0);
            Fx.Slide(StageRailShift, -RailW);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (StageRail.Width < 1 && !_railOpen)
                {
                    StageRail.Visibility = Visibility.Collapsed;
                    RailEdge.Visibility = Visibility.Visible;
                }
            };
            t.Start();
        }
    }

    void RailEdge_Click(object sender, RoutedEventArgs e)
    {
        if (!_railOpen) ToggleRail();
    }

    /// <summary>
    /// 关系页：舞台上的另一种组合。标签条不动、侧栏不动、窗口模式不变——
    /// 只有内容区把卡片换成图；返回入口在内容区头部，不再整块换 Chrome。
    /// </summary>
    public void ShowRelations()
    {
        if (_page == "relations") return;
        Swap("relations");
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
        // 内页里的返回：制作整合包 → 版本选择（还是内页）。
        if (_back == "versions")
        {
            ShowInner("versions", "版本选择", "launch");
            return;
        }
        // Returning from an inner page re-checks its owning tab, which fires Tab_Checked.
        var tab = _back switch
        {
            "manage" => TabManage,
            "tasks" => TabTasks,
            "download" => TabDownload,
            "settings" => TabSettings,
            _ => TabLaunch,
        };
        if (tab.IsChecked == true) Show(_back);
        else tab.IsChecked = true;
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        MoveTabPill(tag);
        if (Host is null) return;
        Show(tag);
    }

    string CurrentTab() =>
        TabManage.IsChecked == true ? "manage"
        : TabTasks.IsChecked == true ? "tasks"
        : TabDownload.IsChecked == true ? "download"
        : TabSettings.IsChecked == true ? "settings"
        : "launch";

    void MoveTabPill(string tag)
    {
        if (TabPill is null || TabStrip is null) return;
        var tab = tag switch
        {
            "manage" => TabManage,
            "tasks" => TabTasks,
            "download" => TabDownload,
            "settings" => TabSettings,
            _ => TabLaunch,
        };
        tab.UpdateLayout();
        TabStrip.UpdateLayout();
        if (tab.ActualWidth <= 1) return;
        TabPill.Width = tab.ActualWidth;
        var at = tab.TranslatePoint(new Point(0, 0), TabStrip);
        Fx.Slide(TabPillShift, at.X, 220);
    }

    /// <summary>
    /// A corner notice for work whose result is not visible in this window — a terminal
    /// that opened somewhere else, or a subprocess that failed. Never for a change the
    /// user can already see.
    /// </summary>
    public void Toast(string text, bool bad = false)
    {
        if (bad)
        {
            if (App.ShotPath is null)
                MessageBox.Show(this, text, "PAD", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        while (ToastLayer.Children.Count >= Math.Max(1, AppState.Current.Settings.Ui.ToastMax))
            ToastLayer.Children.RemoveAt(0);

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(new Border
        {
            Style = (Style)FindResource("Pip"),
            Background = (Brush)FindResource(bad ? "Danger" : "Accent"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 9, 0),
        });
        body.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = (Brush)FindResource("ChromeInk"),
            FontSize = 12,
            MaxWidth = 340,
            TextWrapping = TextWrapping.Wrap,
        });

        var shift = new TranslateTransform();
        var card = new Border
        {
            Style = (Style)FindResource("Toast"),
            Margin = new Thickness(0, 8, 0, 0),
            Child = body,
            RenderTransform = shift,
            IsHitTestVisible = false, // toast 只提示，不挡下面；进度卡单独可点
        };
        ToastLayer.Children.Add(card);

        if (Fx.Animated)
        {
            var ease = new PowerEase { Power = 4.6, EasingMode = EasingMode.EaseOut };
            card.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180))) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(240))) { EasingFunction = ease });
        }

        var life = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                Math.Clamp(AppState.Current.Settings.Ui.ToastMs, 1200, 10000)),
        };
        life.Tick += (_, _) =>
        {
            life.Stop();
            if (!Fx.Animated) { ToastLayer.Children.Remove(card); return; }
            var fade = Fx.ToastOut(shift);
            fade.Completed += (_, _) => ToastLayer.Children.Remove(card);
            card.BeginAnimation(OpacityProperty, fade);
        };
        life.Start();
    }

    Border? _progressCard;
    TextBlock? _progressTitle;
    TextBlock? _progressLine;
    TextBlock? _progressPct;
    Border? _progressTrack;
    Border? _progressFill;
    double? _progressFraction;
    JobRecord? _progressJob;
    bool _progressCollapsed;
    StackPanel? _progressDetail;
    StackPanel? _progressActions;

    /// <summary>
    /// 右下角长方形进度卡：logo + 标题 + 百分比 + 一行「已复制 x/y MB · 速度 · 剩余」+ 进度条。
    /// 右上角「-」收起、」×」隐藏；任务还在跑时可在这里直接取消/删除；点卡片跳下载任务。
    /// </summary>
    public void Progress(string text, double? fraction = null, string? title = null, JobRecord? job = null)
    {
        if (App.ShotPath is not null) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Progress(text, fraction, title, job)));
            return;
        }
        if (_progressCard is null) BuildProgressCard();
        if (job is not null) _progressJob = job;
        _progressLine!.Text = text;
        if (!string.IsNullOrWhiteSpace(title)) _progressTitle!.Text = title;
        _progressFraction = fraction;
        if (fraction is { } f)
        {
            _progressPct!.Text = $"{Math.Clamp(f, 0, 1) * 100:0}%";
            _progressPct.Visibility = Visibility.Visible;
            _progressFill!.Visibility = Visibility.Visible;
            UpdateProgressFill();
        }
        else
        {
            _progressPct!.Text = "";
            _progressPct.Visibility = Visibility.Collapsed;
            _progressFill!.Visibility = Visibility.Collapsed;
        }
        if (_progressActions is not null)
            _progressActions.Visibility = _progressJob is { CanCancel: true } && !_progressCollapsed
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    void BuildProgressCard()
    {
        _progressTitle = new TextBlock
        {
            Foreground = (Brush)FindResource("TextPrimary"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 0, 0, 0),
        };
        _progressPct = new TextBlock
        {
            Foreground = (Brush)FindResource("AccentText"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
        };
        var logo = new Image
        {
            Width = 26,
            Height = 26,
            Stretch = Stretch.Uniform,
            Source = Skin.LoadBitmap(null, new Uri("pack://application:,,,/Assets/pad-logo.png")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var minus = new Button
        {
            Style = (Style)FindResource("BtnGhost"),
            Content = "–",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "收起",
        };
        var close = new Button
        {
            Style = (Style)FindResource("BtnGhost"),
            Content = "✕",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 10,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "隐藏（任务继续，在下载任务里看）",
        };
        minus.Click += (_, _) =>
        {
            _progressCollapsed = !_progressCollapsed;
            if (_progressDetail is not null)
                _progressDetail.Visibility = _progressCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (_progressActions is not null)
                _progressActions.Visibility = _progressCollapsed || _progressJob is not { CanCancel: true }
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            minus.Content = _progressCollapsed ? "+" : "–";
            minus.ToolTip = _progressCollapsed ? "展开" : "收起";
        };
        close.Click += (_, _) =>
        {
            Toast("进度卡已隐藏，可在下载任务里看");
            ProgressDone(0);
        };
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(logo);
        Grid.SetColumn(_progressTitle, 1);
        head.Children.Add(_progressTitle);
        Grid.SetColumn(_progressPct, 2);
        head.Children.Add(_progressPct);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(minus);
        controls.Children.Add(close);
        Grid.SetColumn(controls, 3);
        head.Children.Add(controls);

        _progressLine = new TextBlock
        {
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _progressTrack = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(3),
            Background = (Brush)FindResource("LineDivider"),
        };
        _progressFill = new Border
        {
            Height = 5,
            CornerRadius = new CornerRadius(3),
            Background = (Brush)FindResource("Accent"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };
        var track = new Grid();
        track.Children.Add(_progressTrack);
        track.Children.Add(_progressFill);
        _progressTrack.SizeChanged += (_, _) => UpdateProgressFill();

        var cancel = new Button
        {
            Style = (Style)FindResource("BtnDanger"),
            Content = "取消",
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11.5,
        };
        cancel.Click += (_, _) =>
        {
            if (_progressJob is null) return;
            try
            {
                AppState.Current.Launcher.CancelJob(_progressJob.Id);
                Toast("已取消 " + _progressJob.Title);
            }
            catch (Exception ex) { Toast(PadError.Describe(ex), bad: true); }
            ProgressDone(300);
        };
        var delete = new Button
        {
            Style = (Style)FindResource("BtnSm"),
            Content = "删除",
            Height = 24,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11.5,
        };
        delete.Click += (_, _) =>
        {
            if (_progressJob is null) return;
            try
            {
                AppState.Current.Launcher.DeleteJob(_progressJob.Id);
                Toast("已删除 " + _progressJob.Title);
            }
            catch (Exception ex) { Toast(PadError.Describe(ex), bad: true); }
            ProgressDone(300);
        };
        var pause = new Button
        {
            Style = (Style)FindResource("BtnSm"),
            Content = "暂停",
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 11.5,
        };
        pause.Click += (_, _) =>
        {
            if (_progressJob is null) return;
            try
            {
                var paused = AppState.Current.Launcher.ListJobs().FirstOrDefault(j => j.Id == _progressJob.Id)?.Paused == true;
                if (paused)
                {
                    AppState.Current.Launcher.ResumeJob(_progressJob.Id);
                    pause.Content = "暂停";
                    Toast("已继续 " + _progressJob.Title);
                }
                else
                {
                    AppState.Current.Launcher.PauseJob(_progressJob.Id);
                    pause.Content = "继续";
                    Toast("已暂停 " + _progressJob.Title);
                }
            }
            catch (Exception ex) { Toast(PadError.Describe(ex), bad: true); }
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        actions.PreviewMouseLeftButtonUp += (_, e) => e.Handled = true;
        actions.Children.Add(pause);
        actions.Children.Add(cancel);
        actions.Children.Add(delete);
        _progressActions = actions;

        _progressDetail = new StackPanel();
        _progressDetail.Children.Add(_progressLine);
        _progressDetail.Children.Add(track);

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(_progressDetail);
        body.Children.Add(actions);
        _progressCard = new Border
        {
            Width = 400,
            Background = (Brush)FindResource("BgCard"),
            BorderBrush = (Brush)FindResource("LineBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 10, 12),
            Margin = new Thickness(0, 8, 0, 0),
            Child = body,
            Cursor = Cursors.Hand,
            ToolTip = "打开下载任务",
        };
        var self = _progressCard;
        self.MouseLeftButtonUp += (_, _) => OpenDownloadTasks();
        // 用局部 self：ProgressDone 会把 _progressCard 置空，鼠标离开时才不会 NRE（02:49:27 崩溃）。
        self.MouseEnter += (_, _) => self.Background = (Brush)FindResource("AccentWash");
        self.MouseLeave += (_, _) => self.Background = (Brush)FindResource("BgCard");
        ToastLayer.Children.Add(_progressCard);
        if (Fx.Animated)
        {
            var shift = new TranslateTransform();
            _progressCard.RenderTransform = shift;
            _progressCard.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160)))
                {
                    EasingFunction = new PowerEase { Power = 4.6, EasingMode = EasingMode.EaseOut },
                });
            shift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, new Duration(TimeSpan.FromMilliseconds(220)))
                {
                    EasingFunction = new PowerEase { Power = 4.6, EasingMode = EasingMode.EaseOut },
                });
        }
    }

    void UpdateProgressFill()
    {
        if (_progressFill is null || _progressTrack is null) return;
        var width = Math.Max(0, _progressTrack.ActualWidth * Math.Clamp(_progressFraction ?? 0, 0, 1));
        if (!Fx.Animated)
        {
            _progressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
            _progressFill.Width = width;
            return;
        }
        _progressFill.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(width, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut },
            });
    }

    /// <summary>点进度卡：跳到下载页的「下载任务」（同一个 jobs.json）。</summary>
    public void OpenDownloadTasks()
    {
        Show("download");
        TabDownload.IsChecked = true;
        if (Page("download") is DownloadView dv) dv.OpenShelf("dltasks");
    }

    public void ProgressDone(int delayMs = 900)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ProgressDone(delayMs)));
            return;
        }
        var card = _progressCard;
        _progressCard = null;
        _progressTitle = null;
        _progressLine = null;
        _progressPct = null;
        _progressTrack = null;
        _progressFill = null;
        _progressFraction = null;
        _progressJob = null;
        _progressCollapsed = false;
        _progressDetail = null;
        _progressActions = null;
        if (card is null) return;
        var life = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(0, delayMs)),
        };
        life.Tick += (_, _) =>
        {
            life.Stop();
            ToastLayer.Children.Remove(card);
        };
        life.Start();
    }

    /// <summary>PCL MyMsgText: in-window card. Warn puts focus on 取消 so Enter does not confirm delete.</summary>
    public bool Ask(string title, string body, bool warn = false, string? previewPath = null) =>
        ShowMsg(title, body, input: false, seed: "", warn, previewPath) is not null;

    /// <summary>PCL MyMsgInput: in-window card with a text box.</summary>
    public string? AskInput(string title, string? body = null, string seed = "", bool warn = false) =>
        ShowMsg(title, body ?? "", input: true, seed, warn);

    string? ShowMsg(string title, string body, bool input, string seed, bool warn, string? previewPath = null)
    {
        if (_msgFrame is not null) return null;
        MsgTitle.Text = title;
        MsgBody.Text = body;
        MsgBody.Visibility = body.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        MsgInput.Text = seed;
        MsgInput.Visibility = input ? Visibility.Visible : Visibility.Collapsed;
        var preview = !string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath);
        MsgPreviewBox.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        MsgPreview.Source = preview ? Skin.LoadBitmap(previewPath) : null;
        MsgTitle.Foreground = (Brush)FindResource(warn ? "DangerText" : "AccentText");
        MsgLine.Fill = (Brush)FindResource(warn ? "Danger" : "Accent");
        MsgDim.Background = warn
            ? new SolidColorBrush(Color.FromArgb(0x99, 0x5A, 0x18, 0x14))
            : new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        MsgOk.Style = (Style)FindResource(warn ? "BtnDanger" : "BtnPrimary");
        MsgOk.IsDefault = !warn;
        MsgCancel.IsDefault = warn;
        _msgAccepted = false;
        MsgLayer.Visibility = Visibility.Visible;
        MsgCard.Opacity = 1;
        Fx.PulseIn(MsgCard, true);
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (MsgLayer.Visibility != Visibility.Visible) return;
            if (input)
            {
                MsgInput.Focus();
                MsgInput.SelectAll();
            }
            else if (warn)
                MsgCancel.Focus();
            else
                MsgOk.Focus();
        }, DispatcherPriority.Input);
        _msgFrame = new DispatcherFrame();
        Dispatcher.PushFrame(_msgFrame);
        _msgFrame = null;
        MsgLayer.Visibility = Visibility.Collapsed;
        MsgPreview.Source = null;
        MsgPreviewBox.Visibility = Visibility.Collapsed;
        MsgOk.IsDefault = false;
        MsgCancel.IsDefault = false;
        MsgCard.Opacity = 1;
        MsgCard.RenderTransform = null;
        if (!_msgAccepted) return null;
        return input ? MsgInput.Text.Trim() : "";
    }

    void MsgOk_Click(object sender, RoutedEventArgs e) => EndMsg(true);

    void MsgCancel_Click(object sender, RoutedEventArgs e) => EndMsg(false);

    void EndMsg(bool accepted)
    {
        if (_msgFrame is null) return;
        _msgAccepted = accepted;
        _msgFrame.Continue = false;
    }

    /// <summary>readme.md overlay on the main window. Headings render; no second HWND.</summary>
    public void ShowMd(string title, string markdown)
    {
        MdTitle.Text = title;
        MdDoc.Document = Md.Document(markdown);
        ApplyMdPageWidth();
        MdLayer.Visibility = Visibility.Visible;
        MdCard.Opacity = 1;
        Fx.PulseIn(MdCard, true);
        _ = Dispatcher.InvokeAsync(ApplyMdPageWidth, DispatcherPriority.Loaded);
    }

    public void HideMd()
    {
        MdLayer.Visibility = Visibility.Collapsed;
        MdDoc.Document = new FlowDocument();
        MdCard.Opacity = 1;
        MdCard.RenderTransform = null;
    }

    void MdDim_Down(object sender, MouseButtonEventArgs e) => HideMd();

    void MdClose_Click(object sender, RoutedEventArgs e) => HideMd();

    void MdDoc_Size(object sender, SizeChangedEventArgs e) => ApplyMdPageWidth();

    void ApplyMdPageWidth()
    {
        if (MdDoc.Document is not FlowDocument doc) return;
        var w = MdDoc.ActualWidth;
        if (w > 40) doc.PageWidth = w - 16;
    }

    static void OnMdLink(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* ignore dead links */ }
        e.Handled = true;
    }

    /// <summary>Launch failed: error plus crash-analyze / open-logs, like PCL's fail dialog.</summary>
    public void LaunchFailed(string text, Instance? inst)
    {
        if (App.ShotPath is not null) return;
        if (inst is null)
        {
            MessageBox.Show(this, text, "PAD", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var analyze = new Button { Content = "崩溃分析", Width = 90 };
        var logs = new Button { Content = "打开日志", Width = 90, Margin = new Thickness(8, 0, 0, 0) };
        var close = new Button { Content = "关闭", Width = 70, IsDefault = true, IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0) };
        var dlg = new Window
        {
            Title = "PAD",
            Width = 520,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                        MaxHeight = 160,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { analyze, logs, close },
                    },
                },
            },
        };
        analyze.Click += (_, _) =>
        {
            try
            {
                var report = CrashAnalyzer.Analyze(AppState.Current.Launcher, inst.Id).Render();
                MessageBox.Show(this, report.Length > 2000 ? report[..2000] : report, "崩溃分析",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { Toast(PadError.Describe(ex), bad: true); }
            dlg.Close();
        };
        logs.Click += (_, _) =>
        {
            Reveal.Path(AppState.Current.Launcher.LogsDir(inst.Id));
            dlg.Close();
        };
        close.Click += (_, _) => dlg.Close();
        dlg.ShowDialog();
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        var packs = LaunchPolicy.PackPaths(e.Data);
        if (packs.Count > 0)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }
        var images = LaunchPolicy.ImagePaths(e.Data);
        if (images.Count > 0 && _page == "instance"
            && _pages.TryGetValue("instance", out var page) && page is InstanceView iv && iv.CanTakeLogoDrop)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var packs = LaunchPolicy.PackPaths(e.Data);
        if (packs.Count > 0)
        {
            _ = TakePacks(packs);
            return;
        }
        var images = LaunchPolicy.ImagePaths(e.Data);
        if (images.Count > 0 && _page == "instance"
            && _pages.TryGetValue("instance", out var page) && page is InstanceView iv)
            iv.OfferDroppedLogo(images[0]);
    }

    public async Task TakePacks(IReadOnlyList<string> paths)
    {
        var state = AppState.Current;
        var packs = new Packs(state.Launcher);
        var missing = packs.Unavailable();
        if (missing is not null)
        {
            Toast($"{missing.Reason}。{missing.Detail}", bad: true);
            return;
        }

        var import = LaunchPolicy.DropUsesImport(state.SelectedInstance is not null);
        var log = new Progress<string>(_ => { });
        try
        {
            foreach (var path in paths)
            {
                if (import || state.SelectedInstance is null)
                {
                    Toast("正在装 " + System.IO.Path.GetFileName(path));
                    await packs.Import(path, log, CancellationToken.None);
                }
                else
                {
                    Toast("装进 " + state.SelectedInstance.Name);
                    await packs.Project(state.SelectedInstance, path, log, CancellationToken.None);
                }
            }
            state.Reload();
            Toast("装好了");
        }
        catch (Exception ex)
        {
            Toast(PadError.Describe(ex), bad: true);
        }
    }

    async void PickupSidecarPacks()
    {
        var state = AppState.Current;
        try
        {
            Packs.CollectSidecar(AppContext.BaseDirectory, state.Launcher.Root);
            if (!System.IO.Directory.Exists(state.Launcher.Root)) return;
            var any = System.IO.Directory.EnumerateFiles(state.Launcher.Root).Any(LaunchPolicy.IsSidecarZip);
            if (!any) return;
            var packs = new Packs(state.Launcher);
            // 先留一份进插件包库：自动导入会删掉根目录的 zip，库里这份让「插件包」
            // 列表还能看到它、可再建实例（PCL 的 modpack 目录语义）。
            try
            {
                foreach (var zip in System.IO.Directory.EnumerateFiles(state.Launcher.Root)
                             .Where(LaunchPolicy.IsSidecarZip))
                    packs.CollectToLibrary(zip);
            }
            catch { /* 库拷贝失败不挡自动导入 */ }
            var fail = packs.Unavailable();
            if (fail is not null)
            {
                Toast(fail.Reason + "\n" + fail.Detail, bad: true);
                return;
            }
            await packs.ScanDrop(null, CancellationToken.None);
            state.Reload();
            _ = state.ReloadPackCatalog();
        }
        catch (Exception ex)
        {
            Toast(PadError.Describe(ex), bad: true);
        }
    }

    public void OfferAdoptIfNeeded()
    {
        var state = AppState.Current;
        if (!state.CanOfferAdopt) return;
        var home = LaunchPolicy.DefaultDshHome(AppState.Current.Settings.Launch.DshHome);
        var ask = MessageBox.Show(
            $"本机有一份 DSH_HOME：\n{home}\n\n收到启动器里当实例用？里面的文件不会被改。",
            "PAD", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
        {
            state.Settings.Other.AdoptDismissed = true;
            state.ApplySettings(state.Settings);
            return;
        }
        AdoptNow();
    }

    public async void AdoptNow()
    {
        var home = LaunchPolicy.DefaultDshHome(AppState.Current.Settings.Launch.DshHome);
        await AdoptHomeAt(home, "已收编为本机 DSH");
    }

    /// <summary>PCL「添加已有文件夹」：先选夹，再收编为实例。</summary>
    public void AdoptFolder()
    {
        var home = LaunchPolicy.DefaultDshHome(AppState.Current.Settings.Launch.DshHome);
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "添加已有文件夹" };
        if (System.IO.Directory.Exists(home)) dlg.InitialDirectory = home;
        if (dlg.ShowDialog(this) != true) return;
        if (string.IsNullOrWhiteSpace(dlg.FolderName)) return;
        _ = AdoptHomeAt(dlg.FolderName, "已添加这个文件夹");
    }

    async Task AdoptHomeAt(string home, string done)
    {
        var state = AppState.Current;
        try
        {
            if (state.Releases.Count == 0)
                Toast("在装 DSH 发行号，随后收编");
            var version = await state.Launcher.EnsureRelease(null, CancellationToken.None);
            var inst = state.Launcher.AdoptHome(home, version);
            state.Reload();
            state.SelectedInstance = state.Instances.FirstOrDefault(i => i.Id == inst.Id);
            Toast(done);
        }
        catch (Exception ex)
        {
            Toast(PadError.Describe(ex), bad: true);
        }
    }

    /// <summary>把探测到的 home 直接收编成实例（不弹选目录对话框，D36）。</summary>
    public Task AdoptHomePath(string home) => AdoptHomeAt(home, "已收编本机 DSH");

    void TitleBar_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Close_Click(object sender, RoutedEventArgs e)
    {
        AppState.Current.SaveSelection();
        Close();
    }
}

/// <summary>A page that wants a chance to reload when it becomes visible.</summary>
public interface IRefreshable
{
    void OnShown();
}
