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

    void Run_Click(object sender, RoutedEventArgs e)
    {
        var profile = _state.SelectedProfile;
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
        _win.ShowTab("manage");
        LaunchFloat.Open(_win, profile);
    }

    void Manage_Click(object sender, RoutedEventArgs e) => _win.ShowTab("manage");

    void Versions_Click(object sender, RoutedEventArgs e) => _win.ShowInner("versions", "版本选择");

    void Instance_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedInstance is null) return;
        _win.ShowInner("instance", "版本设置");
    }
}
