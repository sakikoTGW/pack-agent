using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 一个项目的 DSH 工作区。PAD 不重画对话，把该项目自己的 localhost 页面嵌进来，
/// 左边仍是 PAD 的项目管理。DSH 一行不改，关掉 PAD 也不影响它。
/// </summary>
public partial class ProjectDshView : UserControl
{
    readonly AppState _state = AppState.Current;
    bool _ready;
    string? _url;
    string? _projectName;

    /// <summary>“启动这个项目”：宿主接上 ProjectRunner。</summary>
    public Func<Task>? StartRequested { get; set; }
    /// <summary>“在浏览器打开”：宿主接上 Reveal.Url。</summary>
    public Action? OpenInBrowser { get; set; }

    public ProjectDshView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
    }

    async Task InitAsync()
    {
        if (_ready) return;
        try
        {
            var profile = Path.Combine(_state.Launcher.Root, "webview-dsh");
            Directory.CreateDirectory(profile);
            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            await Web.EnsureCoreWebView2Async(env);

            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            // 只允许本机 DSH 页面；点到外链就交给系统浏览器。
            Web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                var uri = e.Uri ?? "";
                if (uri.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || uri.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
                    || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    return;
                e.Cancel = true;
                try { Reveal.Url(uri); } catch { }
            };
            Web.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess) return;
                Splash.Visibility = Visibility.Visible;
                Web.Visibility = Visibility.Collapsed;
                SplashTitle.Text = _projectName is null ? "打不开 DSH 页面" : $"{_projectName} 的 DSH 页面打不开";
                SplashText.Text = "地址可能过期了（DSH 的 token 每次启动会变）。重新启动这个项目即可。";
                StartBtn.Visibility = Visibility.Visible;
                RetryBtn.Visibility = Visibility.Visible;
                SplashHint.Text = "";
            };
            _ready = true;
            if (_url is not null) Navigate();
        }
        catch (Exception ex)
        {
            SplashTitle.Text = "WebView2 起不来";
            SplashText.Text = PadError.Describe(ex);
            StartBtn.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>项目没在跑：引导卡。</summary>
    public void ShowStopped(string projectName)
    {
        _projectName = projectName;
        _url = null;
        Splash.Visibility = Visibility.Visible;
        Web.Visibility = Visibility.Collapsed;
        Chip.Visibility = Visibility.Collapsed;
        SplashTitle.Text = $"{projectName} 还没启动";
        SplashText.Text = "启动后这里就是这个项目的 DSH 工作区——窗口里只有它自己的东西。";
        StartBtn.Visibility = Visibility.Visible;
        BrowserBtn.Visibility = Visibility.Collapsed;
        RetryBtn.Visibility = Visibility.Collapsed;
        SplashHint.Text = "PAD 只负责按项目管理、启动、切窗口；DSH 本身不改。";
    }

    /// <summary>项目在跑：把 DSH 页面嵌进来。</summary>
    public void ShowUrl(string projectName, string url)
    {
        _projectName = projectName;
        _url = url;
        Splash.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;
        Chip.Visibility = Visibility.Visible;
        ChipText.Text = projectName;
        if (_ready) Navigate();
    }

    void Navigate()
    {
        if (!_ready || _url is null) return;
        try { Web.CoreWebView2.Navigate(_url); }
        catch { /* 下一次刷新再试 */ }
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (StartRequested is null) return;
        StartBtn.IsEnabled = false;
        StartBtn.Content = "正在启动…";
        SplashText.Text = "PAD 在启动这个项目的 DSH，就绪后自动切过来。";
        try { await StartRequested(); }
        finally
        {
            StartBtn.IsEnabled = true;
            StartBtn.Content = "启动这个项目";
        }
    }

    void Browser_Click(object sender, RoutedEventArgs e)
    {
        if (_url is not null) OpenInBrowser?.Invoke();
        else OpenInBrowser?.Invoke();
    }

    void Retry_Click(object sender, RoutedEventArgs e)
    {
        Splash.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;
        Navigate();
        Web.Reload();
    }

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        try { Web.CoreWebView2?.Reload(); } catch { }
    }
}
