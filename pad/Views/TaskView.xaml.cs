using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Pad.Core;

namespace Pad.Views;

public partial class TaskView : UserControl, IRefreshable
{
    readonly MainWindow _win;
    readonly AppState _state = AppState.Current;

    List<JobRecord> _jobs = [];
    DispatcherTimer? _poll;

    public TaskView(MainWindow win)
    {
        InitializeComponent();
        _win = win;
        DataContext = this;
        VisualTreeGuard.ThrowIfSelfHosted(this);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) StartPoll();
            else StopPoll();
        };
    }

    public bool NoneRunning => VisibleCount == 0;

    int VisibleCount { get; set; }

    string SearchQuery => (SearchBox?.Text ?? "").Trim();

    public void OnShown()
    {
        Load();
        StartPoll();
    }

    void StartPoll()
    {
        StopPoll();
        var sec = Math.Clamp(_state.Settings.Other.RefreshSec, 1, 30);
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(sec) };
        _poll.Tick += (_, _) => Load();
        _poll.Start();
    }

    void StopPoll()
    {
        _poll?.Stop();
        _poll = null;
    }

    void RaisePropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    void Load()
    {
        if (JobList is null) return;
        _jobs = _state.Launcher.ListJobs();
        _ = LaunchPolicy.PickJob(_jobs, j => j.Status, j => j.UpdatedAt);
        var rows = DownloadDock.ChromeList(_jobs, SearchQuery).Select(j => new JobCard { Job = j }).ToList();
        VisibleCount = rows.Count;
        JobList.ItemsSource = rows;
        RaisePropertyChanged(nameof(NoneRunning));
    }

    void Search_Changed(object sender, TextChangedEventArgs e) => Load();

    void Search_Key(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Load();
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        try
        {
            _state.Launcher.CancelJob(job.Id);
            _win.Toast("已取消 " + job.Title);
            Load();
        }
        catch (Exception ex) { _win.Toast(PadError.Describe(ex), bad: true); }
    }

    void Forget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: JobRecord job }) return;
        _state.Launcher.ForgetJob(job.Id);
        Load();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _state.Launcher.ForgetFinishedJobs();
        Load();
    }

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

    void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    void Download_Click(object sender, RoutedEventArgs e) => _win.ShowTab("download");
}
