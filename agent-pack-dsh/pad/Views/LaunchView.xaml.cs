using System.Windows;
using System.Windows.Controls;
using Pad.Core;
namespace Pad.Views;

public partial class LaunchView : UserControl, IRefreshable
{
    readonly MainWindow _win;
    readonly AppState _state = AppState.Current;

    public LaunchView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        DataContext = _state;
        _state.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.CanLaunch) or nameof(AppState.NeedsSetup)
                or nameof(AppState.HasInstanceNoProfile) or nameof(AppState.StepTarget)
                or nameof(AppState.SelectedProfile) or nameof(AppState.SelectedInstance))
            {
                PaintRunBtn();
                PaintKey();
            }
        };
        PaintRunBtn();
    }

    public void OnShown()
    {
        if (VersionEntryGrid is not null)
            VersionEntryGrid.Visibility = _state.Settings.Ui.HideVersionEntry
                ? Visibility.Collapsed : Visibility.Visible;
        _state.RefreshRunning();
        PaintRunBtn();
        PaintKey();
        _ = _state.ReloadPacks();
    }

    void PaintKey()
    {
        if (KeyWarn is null) return;
        var probe = CredentialsGate.Inspect(_state.Launcher, _state.SelectedInstance, _state.Settings);
        KeyWarn.Visibility = probe.Configured ? Visibility.Collapsed : Visibility.Visible;
    }

    void PaintRunBtn()
    {
        if (RunLabel is null) return;
        if (_state.CanLaunch) RunLabel.Text = "启动";
        else if (_state.StepTarget == "download") RunLabel.Text = "下载 DSH";
        else RunLabel.Text = "版本选择";
    }

    public void RevealVersionEntry()
    {
        if (VersionEntryGrid is not null) VersionEntryGrid.Visibility = Visibility.Visible;
    }

    void Step_Click(object sender, RoutedEventArgs e) => GoToStep();

    void Adopt_Click(object sender, RoutedEventArgs e) => _win.AdoptFolder();

    void GoToStep()
    {
        if (_state.StepTarget == "download") _win.TabDownload.IsChecked = true;
        else _win.ShowInner("versions", "版本选择");
    }

    async void Run_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 容器列表里点的是探测到的 TUI / 桌面端：按它自己的入口启动（PAD 只是拉起它）。
            switch (_state.SelectedContainer)
            {
                case DetectedTuiRow tui when !tui.LocalRunning:
                {
                    var opened = await BundleLaunch.OpenTuiAsync(_state.Launcher, tui.Source, CancellationToken.None);
                    _win.Toast($"已启动 {tui.Name}（终端窗口，pid {opened.Pid}）");
                    _state.RefreshRunning();
                    return;
                }
                case DetectedTuiRow tui:
                    _win.Toast($"{tui.Name} 已经在跑（不是 PAD 启动的）");
                    return;
                case DetectedShellRow shell when !shell.LocalRunning:
                {
                    var opened = BundleLaunch.OpenShell(_state.Launcher, shell.Source);
                    _win.Toast($"已启动 {shell.Name}（桌面端窗口，pid {opened.Pid}）");
                    _state.RefreshRunning();
                    return;
                }
                case DetectedShellRow shell:
                    _win.Toast($"{shell.Name} 已经在跑");
                    return;
            }

            // profile 拆出来的 surface 行：这次要用哪种「怎么看见」是行自己带的。
            if (_state.SelectedContainer is BundleRow surfaceRow)
            {
                var surface = Bundle.For(surfaceRow.BundleKind);
                if (surface is null) return;
                var opened = await BundleLaunch.OpenAsync(_state.Launcher, surfaceRow.Instance,
                    surfaceRow.Profile.Name, surface, _state.Settings, CancellationToken.None);
                _win.Toast($"已启动 {surfaceRow.InstanceName} · {surfaceRow.Profile.Name}（{surface.Shows}，pid {opened.Pid}）");
                _state.RefreshRunning();
                return;
            }

            var profile = _state.SelectedProfile ?? _state.SelectedContainer;
            if (profile is null)
            {
                GoToStep();
                return;
            }
            var key = CredentialsGate.Inspect(_state.Launcher, profile.Instance, _state.Settings);
            if (!key.Configured)
            {
                _win.OpenSettingsApi();
                _win.LaunchFailed(CredentialsFile.MissingError(key.Location).Render(), profile.Instance);
                return;
            }
            // 一个 profile 同时提供两种 surface 是合法的（profile 决定装什么）；
            // 只是启动时要挑一个。这里先说明这次按哪个开，选择器是下一步。
            if (profile.Profile.UiSurfaces.Count > 1)
                _win.Toast($"{profile.Name} 提供 " + string.Join(" / ", profile.Profile.UiSurfaces) +
                           $" 两种 surface，这次按 {(profile.Profile.IsWeb ? "web" : "tui")} 启动");
            _win.ShowTab("manage");
            LaunchFloat.Open(_win, profile);
        }
        catch (Exception ex)
        {
            _win.Toast(PadError.Describe(ex), bad: true);
        }
    }

    void Manage_Click(object sender, RoutedEventArgs e) => _win.ShowTab("manage");

    void Versions_Click(object sender, RoutedEventArgs e) => _win.ShowInner("versions", "版本选择");

    void Instance_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedInstance is null) return;
        _win.ShowInner("instance", "版本设置");
    }
}
