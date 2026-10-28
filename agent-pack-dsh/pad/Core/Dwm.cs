using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pad.Core;

/// <summary>
/// Window frame policy. The window is a normal (non-layered) HWND so the GPU can
/// compose it; Windows 11 then draws the rounded corners and the outer shadow that
/// used to be faked with AllowsTransparency plus a blur across the whole visual tree.
/// On Windows 10 nothing is set and the window stays square — an opaque window is
/// still far cheaper than a layered one.
/// </summary>
public static class Dwm
{
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Round(Window window)
    {
        if (Environment.OSVersion.Version.Build < 22000) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var pref = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref pref, sizeof(int));
    }
}
