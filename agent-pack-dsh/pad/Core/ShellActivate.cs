using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pad.Core;

/// <summary>
/// 把外部启动的 DSH 壳窗口切到前台。PAD 不托管外部进程（D6/D18），
/// 这里只做"切窗口"这一个人的动作，不改任何状态。
///
/// Electron 窗口的 pid 不一定等于 <see cref="Process.MainWindowHandle"/> 读到的那个进程：
/// 主窗口可能挂在同一 exe 的另一个进程上。所以枚举所有顶层窗口，
/// 在给定 pid 集合里找"可见、有标题"的那个；再用 AttachThreadInput 绕前台锁。
/// </summary>
public static class ShellActivate
{
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>在给定 pid 集合里找一个可见的顶层窗口，还原并前置；找不到返回 false。</summary>
    public static bool Try(IEnumerable<int> pids)
    {
        var want = new HashSet<uint>();
        foreach (var pid in pids) if (pid > 0) want.Add((uint)pid);
        if (want.Count == 0) return false;

        var best = IntPtr.Zero;
        long bestArea = -1;
        var bestTitle = "";
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (!want.Contains(pid)) return true;
            if (!IsWindowVisible(hWnd)) return true;
            var len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Length == 0) return true;
            GetWindowRect(hWnd, out var rect);
            long area = Math.Max(0, rect.Right - rect.Left) * (long)Math.Max(0, rect.Bottom - rect.Top);
            // 优先带 DSH 字样的主窗口，其次面积最大的可见窗口。
            var preferred = title.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)
                            || title.Contains("Harness", StringComparison.OrdinalIgnoreCase)
                            || title.Contains("DSH", StringComparison.OrdinalIgnoreCase);
            var score = area + (preferred ? long.MaxValue / 4 : 0);
            if (score <= bestArea) return true;
            bestArea = score;
            best = hWnd;
            bestTitle = title;
            return true;
        }, IntPtr.Zero);

        if (best == IntPtr.Zero) return false;
        if (IsIconic(best)) ShowWindow(best, 9); // SW_RESTORE
        ShowWindow(best, 5);                       // SW_SHOW

        // 前台锁：把当前前台线程的输入队列临时接上，SetForegroundWindow 才会真的生效。
        var foreground = GetForegroundWindow();
        uint fgThread = 0;
        if (foreground != IntPtr.Zero) fgThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(best, out _);
        var attached = fgThread != 0 && fgThread != targetThread;
        if (attached) AttachThreadInput(fgThread, targetThread, true);
        try
        {
            BringWindowToTop(best);
            SetForegroundWindow(best);
        }
        finally
        {
            if (attached) AttachThreadInput(fgThread, targetThread, false);
        }

        return true;
    }
}
