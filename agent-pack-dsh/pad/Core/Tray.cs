using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pad.Core;

/// <summary>
/// 托盘图标（Win32 Shell_NotifyIcon，不引 WinForms，避免和 WPF 的全局 using 打架）。
/// 菜单：打开 PAD / 关闭窗口时最小化到托盘（勾选）/ 退出 PAD。
/// 双击图标 = 打开。窗口隐藏时发一次气泡提示。
/// </summary>
public sealed class Tray : IDisposable
{
    const int WM_APP = 0x8000;
    const uint CallbackMessage = WM_APP + 42;

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    const uint NIIF_INFO = 1;
    const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 8, MF_UNCHECKED = 0;
    const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100;

    const int WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;
    const uint WM_NULL = 0;

    const int CmdOpen = 1, CmdToggle = 2, CmdExit = 3;

    readonly IntPtr _hwnd;
    readonly HwndSource _source;
    readonly Action _show;
    readonly Action _exit;
    readonly Func<bool> _getCloseToTray;
    readonly Action<bool> _setCloseToTray;
    readonly IntPtr _icon;
    readonly IntPtr _menu;
    NOTIFYICONDATAW _nid;
    bool _added;
    bool _balloonShown;
    bool _disposed;

    public Tray(Window window, Action show, Action exit, Func<bool> getCloseToTray, Action<bool> setCloseToTray)
    {
        _show = show;
        _exit = exit;
        _getCloseToTray = getCloseToTray;
        _setCloseToTray = setCloseToTray;
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd)
                  ?? throw new InvalidOperationException("tray needs a window handle");
        _source.AddHook(WndProc);

        _icon = ExtractIcon(Environment.ProcessPath);
        _menu = CreatePopupMenu();
        AppendMenuW(_menu, MF_STRING, CmdOpen, "打开 PAD");
        AppendMenuW(_menu, MF_SEPARATOR, 0, "");
        AppendMenuW(_menu, MF_STRING, CmdToggle, "关闭窗口时最小化到托盘");
        AppendMenuW(_menu, MF_SEPARATOR, 0, "");
        AppendMenuW(_menu, MF_STRING, CmdExit, "退出 PAD");
        SyncToggle();

        _nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = "PAD — pack-agent for DSH",
        };
        _added = Shell_NotifyIconW(NIM_ADD, ref _nid);
    }

    public bool Visible => _added;

    /// <summary>窗口第一次收到托盘时提示一次（之后不再烦）。</summary>
    public void NotifyHiddenOnce()
    {
        if (_balloonShown || !_added) return;
        _balloonShown = true;
        try
        {
            _nid.uFlags = NIF_INFO;
            _nid.szInfo = "窗口已收到托盘。双击托盘图标打开，右键菜单可以退出。";
            _nid.szInfoTitle = "PAD 还在运行";
            _nid.dwInfoFlags = NIIF_INFO;
            _nid.uTimeoutOrVersion = 3000;
            Shell_NotifyIconW(NIM_MODIFY, ref _nid);
        }
        catch { /* 气泡失败不影响功能 */ }
    }

    public void SyncCloseToTray(bool on)
    {
        if (on) CheckMenuItem(_menu, CmdToggle, MF_CHECKED);
        else CheckMenuItem(_menu, CmdToggle, MF_UNCHECKED);
    }

    void SyncToggle() => SyncCloseToTray(_getCloseToTray());

    void Run(Action action)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(action);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg != CallbackMessage) return IntPtr.Zero;
        switch ((int)lParam & 0xFFFF)
        {
            case WM_LBUTTONDBLCLK:
                Run(_show);
                handled = true;
                break;
            case WM_RBUTTONUP:
                ShowMenu();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    void ShowMenu()
    {
        SyncToggle();
        GetCursorPos(out var pt);
        // 菜单要在前台窗口上弹；先激活 PAD 的 hwnd（即使窗口是隐藏的，菜单也能正常收掉）。
        SetForegroundWindow(_hwnd);
        var cmd = TrackPopupMenu(_menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | 0x0, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        switch ((int)cmd)
        {
            case CmdOpen:
                Run(_show);
                break;
            case CmdToggle:
            {
                var next = !_getCloseToTray();
                SyncCloseToTray(next);
                Run(() => _setCloseToTray(next));
                break;
            }
            case CmdExit:
                Run(_exit);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_added) Shell_NotifyIconW(NIM_DELETE, ref _nid);
        }
        catch { }
        try { _source.RemoveHook(WndProc); } catch { }
        try { if (_icon != IntPtr.Zero) DestroyIcon(_icon); } catch { }
        try { if (_menu != IntPtr.Zero) DestroyMenu(_menu); } catch { }
    }

    static IntPtr ExtractIcon(string? exe)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exe)) return IntPtr.Zero;
            if (ExtractIconExW(exe, 0, out var large, out var small, 1) == 0) return IntPtr.Zero;
            if (large != IntPtr.Zero) DestroyIcon(large);
            return small != IntPtr.Zero ? small : large;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenuW(IntPtr menu, uint flags, int id, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint CheckMenuItem(IntPtr menu, int id, uint check);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);
}
