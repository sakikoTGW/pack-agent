using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pad.Core;

/// <summary>
/// WindowStyle=None + AllowsTransparency never copies ApplicationIcon onto the HWND.
/// Explorer sees the PE icon; the taskbar and MessageBoxW do not, unless we WM_SETICON.
/// </summary>
public static class WinIcon
{
    const int WM_SETICON = 0x0080;
    const int IconSmall = 0;
    const int IconBig = 1;

    /// <summary>Cached before the UI can freeze, so NativeAlert does not have to Invoke.</summary>
    public static IntPtr MainHwnd { get; private set; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint n);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        MainHwnd = hwnd;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        if (ExtractIconEx(exe, 0, large, small, 1) == 0) return;
        if (large[0] != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, IconBig, large[0]);
        if (small[0] != IntPtr.Zero) SendMessage(hwnd, WM_SETICON, IconSmall, small[0]);
    }
}
