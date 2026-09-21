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

        Core.AppState.Current.ApplyUiBoot();
        new MainWindow().Show();
        if (ShotPath is null)
            UiWatchdog.Attach(Dispatcher, CrashLog, () =>
            {
                try { return Core.AppState.Current.Settings.Other.OpenLogOnError; }
                catch { return false; }
            });
    }

    void OnDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Record(e.Exception, fatal: false);
        e.Handled = true;
        if (ShotPath is not null)
        {
            try { File.WriteAllText(ShotPath + ".txt", e.Exception.ToString()); }
            catch { /* shot already failed */ }
            Shutdown(1);
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
            Current.Shutdown(1);
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
