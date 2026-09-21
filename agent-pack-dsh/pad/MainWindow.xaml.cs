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
                    "manage" => TabManage,
                    "tasks" => TabTasks,
                    "download" or "market" or "dl-tasks" => TabDownload,
                    "settings" or "settings-ui" or "settings-download" or "settings-other"
                        or "settings-launch" or "settings-api" => TabSettings,
                    _ => TabLaunch,
                };
                tab.IsChecked = true;
                if (App.ShotPage == "versions") ShowInner("versions", "版本选择");
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
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WinIcon.Apply(this);
    }

    /// <summary>
    /// Window geometry comes from pad.json. Clamp to the work area so a 150% DPI
    /// 1080p panel does not hang off the bottom edge.
    /// </summary>
    public void ApplyWindowPrefs()
    {
        var ui = AppState.Current.Settings.Ui;
        var work = SystemParameters.WorkArea;
        var wantW = ui.WindowWidth;
        var wantH = ui.WindowHeight;
        if (work.Width > 0) wantW = (int)Math.Min(wantW, Math.Max(880, work.Width - 24));
        if (work.Height > 0) wantH = (int)Math.Min(wantH, Math.Max(520, work.Height - 24));
        Width = wantW;
        Height = wantH;
        OuterShell.Margin = new Thickness(ui.WindowMargin);
        ChromeRow.Height = new GridLength(ui.ChromeHeight);
        Opacity = ui.WindowOpacity / 100.0;
        ApplyHiddenTabs();
        ApplySkin();
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
        var ms = App.ShotPage == "market" ? other.ShotMarketDelayMs : other.ShotDelayMs;
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
                    + $"host {(Host.Content as FrameworkElement)?.ActualWidth}x{(Host.Content as FrameworkElement)?.ActualHeight}\n");
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
            "manage" => new ManageView(this),
            "tasks" => new TaskView(this),
            "download" => new DownloadView(this),
            "settings" => new SettingsView(),
            "versions" => new VersionsView(this),
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
            if (!same) Host.Content = page;
            if (!same) Fx.PageIn(page);
            if (page is IRefreshable r) r.OnShown();
        }
        catch (Exception ex)
        {
            App.ReportPage(ex);
        }
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
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
        };
        ToastLayer.Children.Add(card);

        if (Fx.Animated)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
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
            var fail = packs.Unavailable();
            if (fail is not null)
            {
                Toast(fail.Reason + "\n" + fail.Detail, bad: true);
                return;
            }
            await packs.ScanDrop(null, CancellationToken.None);
            state.Reload();
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
