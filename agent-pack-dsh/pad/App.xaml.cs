using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Pad.Core;

namespace Pad;

public partial class App : Application
{
    public static string CrashLog => Path.Combine(AppContext.BaseDirectory, "pad-crash.log");

    /// <summary>
    /// When set by <c>--shot &lt;path&gt;</c>, the window renders itself to a PNG once
    /// laid out. Screen capture cannot be trusted for a layered WPF window, so the
    /// only honest picture of the UI is the one the window draws itself.
    /// </summary>
    public static string? ShotPath { get; private set; }

    public static bool ShotExit { get; private set; }

    /// <summary>Which page to render for <c>--shot</c>; defaults to the launch page.</summary>
    public static string ShotPage { get; private set; } = "launch";

    /// <summary>true = 正在真正退出（关闭窗口不进托盘）。</summary>
    public static bool Exiting { get; private set; }

    /// <summary>托盘图标（窗口隐藏后靠它恢复/退出）。</summary>
    public static Tray? TrayIcon { get; private set; }

    static SingleInstance? _instance;

    /// <summary>关窗是否缩到托盘（设置里可改）。</summary>
    public static bool CloseToTray
    {
        get
        {
            try { return Core.AppState.Current.Settings.Ui.CloseToTray; }
            catch { return true; }
        }
        set
        {
            try
            {
                Core.AppState.Current.Settings.Ui.CloseToTray = value;
                Core.AppState.Current.SaveSettings();
                TrayIcon?.SyncCloseToTray(value);
            }
            catch { /* 设置写不进去也不影响当前会话 */ }
        }
    }

    public static void Quit(int code = 0)
    {
        Exiting = true;
        Current.Shutdown(code);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // `pad cli …` runs the launcher headless and never opens a window.
        // Task.Run is load-bearing: blocking the UI thread here while the CLI awaits
        // would deadlock, because WPF's SynchronizationContext posts every
        // continuation back to the thread already stuck on GetResult().
        if (e.Args.Length > 0 && e.Args[0] == "cli")
        {
            var args = e.Args.Skip(1).ToArray();
            var code = Task.Run(() => Core.Cli.Run(args)).GetAwaiter().GetResult();
            Shutdown(code);
            return;
        }

        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--shot" && i + 1 < e.Args.Length) ShotPath = e.Args[++i];
            else if (e.Args[i] == "--shot-page" && i + 1 < e.Args.Length) ShotPage = e.Args[++i];
            else if (e.Args[i] == "--shot-exit") ShotExit = true;
        }
        DispatcherUnhandledException += OnDispatcherError;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Record(args.ExceptionObject as Exception, fatal: true);
            if (args.ExceptionObject is Exception ex)
                NativeAlert.Error(DialogFor(ex));
        };

        // 同一个 .pack-launcher 只开一个窗口实例：第二个进程让老窗口显形后自己退出。
        var root = Core.AppState.Current.Launcher.Root;
        if (ShotPath is null)
        {
            _instance = SingleInstance.TryAcquire(root);
            if (_instance is null)
            {
                SingleInstance.SignalExisting(root);
                Shutdown(0);
                return;
            }
        }

        // 上次下载好的 PAD 更新：这次启动把新 exe 换上去再重启（用户自己选过才走到这里）。
        if (ShotPath is null && Core.PadUpdate.TryApplyPending(Core.AppState.Current.Launcher, out _))
        {
            Shutdown(0);
            return;
        }
        // 上个 PAD 进程留下的假任务先清掉，再开窗口。
        try { Core.AppState.Current.Launcher.SweepStaleJobs(); } catch { /* 不影响启动 */ }
        // 整合包首次启动：先把折叠的 node_modules 解出来，再开主窗口。
        PortableBootstrap.RunIfNeeded();
        Core.AppState.Current.ApplyUiBoot();
        var window = new MainWindow();
        window.Show();
        if (ShotPath is null)
        {
            _instance!.Listen(() => Dispatcher.BeginInvoke(() => window.RestoreFromTray()));
            TrayIcon = new Tray(window,
                () => window.RestoreFromTray(),
                ExitFromTray,
                () => CloseToTray,
                on => CloseToTray = on);
            window.SyncTrayState();
        }
        if (ShotPath is null) window.ProbePadUpdate();
        if (ShotPath is null)
            UiWatchdog.Attach(Dispatcher, CrashLog, () =>
            {
                try { return Core.AppState.Current.Settings.Other.OpenLogOnError; }
                catch { return false; }
            });
    }

    static void ExitFromTray()
    {
        Exiting = true;
        try { Core.AppState.Current.SaveSelection(); } catch { /* ignore */ }
        Current.Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayIcon?.Dispose();
        TrayIcon = null;
        _instance?.Dispose();
        _instance = null;
        base.OnExit(e);
    }

    void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Record(e.Exception, fatal: false);
        e.Handled = true;
        if (ShotPath is not null)
        {
            try { File.WriteAllText(ShotPath + ".txt", e.Exception.ToString()); }
            catch { /* shot already failed */ }
            Quit(1);
            return;
        }
        MessageBox.Show(DialogFor(e.Exception), "PAD 出错了", MessageBoxButton.OK, MessageBoxImage.Error);
        OpenLogIfWanted();
    }

    void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Record(e.Exception, fatal: false);
        NativeAlert.Error(DialogFor(e.Exception.InnerException ?? e.Exception));
        e.SetObserved();
    }

    public static void ReportPage(Exception ex)
    {
        Record(ex, fatal: false);
        if (ShotPath is not null)
        {
            try { File.WriteAllText(ShotPath + ".txt", ex.ToString()); }
            catch { /* shot already failed */ }
            Quit(1);
            return;
        }
        MessageBox.Show(DialogFor(ex), "PAD 出错了", MessageBoxButton.OK, MessageBoxImage.Error);
        OpenLogIfWanted();
    }

    static string DialogFor(Exception ex) =>
        ex is PadError { Code: UiHang.Code } pad
            ? UiHang.SelfHostDialog(pad.Location, CrashLog)
            : PadError.Describe(ex) + Environment.NewLine + Environment.NewLine + "详情写到了 " + CrashLog;

    static void OpenLogIfWanted()
    {
        try
        {
            if (!Core.AppState.Current.Settings.Other.OpenLogOnError) return;
            var dir = Path.GetDirectoryName(CrashLog);
            if (dir is not null)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    static void Record(Exception? ex, bool fatal)
    {
        if (ex is null) return;
        try
        {
            File.AppendAllText(CrashLog,
                $"--- {DateTimeOffset.Now:o} {(fatal ? "fatal" : "handled")}{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch { /* nowhere left to report */ }
    }
}
