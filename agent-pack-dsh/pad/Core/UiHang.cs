using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Pad.Core;

/// <summary>
/// UI thread hang is not an Exception. DispatcherUnhandledException never runs.
/// The dialog must come from user32 on a background thread.
/// </summary>
public static class UiHang
{
    public const string Code = "PA040";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public static string HangDialog(TimeSpan waited, string logPath) =>
        $"error[{Code}]\n界面卡住了（超过 {waited.TotalSeconds:0} 秒没有响应）。\n详情写到了 {logPath}\n点确定结束 PAD。";

    public static string SelfHostDialog(string where, string logPath) =>
        $"error[{Code}]\n这一页把自己塞进自己的控件里了。已经拦住，没有卡死。\n位置：{where}\n详情写到了 {logPath}";

    public static string HangLog(TimeSpan waited) =>
        $"界面线程超过 {waited.TotalSeconds:0} 秒没有处理心跳。没有抛异常，DispatcherUnhandledException 走不到。常见原因：某页 Content 绑到了本页。";
}

public static class VisualTreeGuard
{
    public static void ThrowIfSelfHosted(DependencyObject? root)
    {
        if (root is null) return;
        if (!FindsSelf(root, root, 0)) return;
        var where = root.GetType().Name;
        throw new PadError(UiHang.Code, "this page hosts itself in its own Content", where, where,
            ["the page switch was blocked", "the window did not freeze"]);
    }

    static bool FindsSelf(DependencyObject root, DependencyObject node, int depth)
    {
        if (depth > 80) return false;
        if (node is ContentControl cc && !ReferenceEquals(cc, root) && ReferenceEquals(cc.Content, root))
            return true;
        if (node is ContentPresenter cp && !ReferenceEquals(cp, root) && ReferenceEquals(cp.Content, root))
            return true;
        foreach (var child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is not DependencyObject d) continue;
            if (ReferenceEquals(d, root))
            {
                if (!ReferenceEquals(node, root)) return true;
                continue;
            }
            if (FindsSelf(root, d, depth + 1)) return true;
        }
        return false;
    }
}

public static class NativeAlert
{
    const uint MbOk = 0x00000000;
    const uint MbIconError = 0x00000010;
    const uint MbSystemModal = 0x00001000;
    const uint MbSetForeground = 0x00010000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Error(string text, string caption = "PAD 出错了") =>
        MessageBoxW(WinIcon.MainHwnd, text, caption, MbOk | MbIconError | MbSystemModal | MbSetForeground);
}

public static class UiWatchdog
{
    static int _started;
    static long _quietUntil;

    /// <summary>First paint of a new page sits on the dispatcher. Skip pings so PA040 is not a first-frame false alarm.</summary>
    public static void QuietFor(TimeSpan window) =>
        _quietUntil = Environment.TickCount64 + (long)Math.Max(0, window.TotalMilliseconds);

    public static void Attach(Dispatcher dispatcher, string crashLog, Func<bool> openLog)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        var t = new Thread(() => Loop(dispatcher, crashLog, openLog))
        {
            IsBackground = true,
            Name = "pad-ui-watchdog",
        };
        t.Start();
    }

    static void Loop(Dispatcher dispatcher, string crashLog, Func<bool> openLog)
    {
        try { Thread.Sleep(6000); }
        catch { return; }
        while (true)
        {
            if (!Ping(dispatcher, UiHang.Timeout))
            {
                ReportHang(crashLog, openLog);
                return;
            }
            try { Thread.Sleep(2000); }
            catch { return; }
        }
    }

    static bool Ping(Dispatcher dispatcher, TimeSpan timeout)
    {
        if (Environment.TickCount64 < _quietUntil) return true;
        var pinged = new ManualResetEventSlim(false);
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                try { pinged.Set(); }
                catch { /* shutting down */ }
            }, DispatcherPriority.Send);
        }
        catch
        {
            return true;
        }
        return pinged.Wait(timeout);
    }

    static void ReportHang(string crashLog, Func<bool> openLog)
    {
        var text = UiHang.HangDialog(UiHang.Timeout, crashLog);
        try
        {
            File.AppendAllText(crashLog,
                $"--- {DateTimeOffset.Now:o} hang{Environment.NewLine}{UiHang.HangLog(UiHang.Timeout)}{Environment.NewLine}");
        }
        catch { /* nowhere left */ }
        try
        {
            if (openLog())
            {
                var dir = Path.GetDirectoryName(crashLog);
                if (dir is not null)
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch { /* ignore */ }
        NativeAlert.Error(text);
        Environment.Exit(1);
    }
}
