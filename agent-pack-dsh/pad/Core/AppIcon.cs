using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pad.Core;

/// <summary>
/// 正在跑的那个 exe 的真实图标（D58）：从外壳自己身上取，不贴近似 logo。
/// 用 shell32 的 <c>SHGetFileInfo</c> 拿 HICON，再用 WPF 的
/// <see cref="Imaging.CreateBitmapSourceFromHIcon"/> 编码成 PNG，缓存在
/// <c>&lt;root&gt;/library/icons/exe/</c>；同一 exe（同时间戳）只取一次。
/// 不引 System.Drawing：便携包里少一个依赖。
/// </summary>
public static class AppIcon
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    const uint SHGFI_ICON = 0x000000100;
    const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);

    public static string? PathFor(string? exe, string root)
    {
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe) || string.IsNullOrWhiteSpace(root))
            return null;
        try
        {
            var stamp = File.GetLastWriteTimeUtc(exe).Ticks;
            var key = Convert.ToHexString(
                SHA1.HashData(Encoding.UTF8.GetBytes(exe!.ToLowerInvariant() + "|" + stamp)))[..12].ToLowerInvariant();
            var dir = Path.Combine(root, "library", "icons", "exe");
            var png = Path.Combine(dir, key + ".png");
            if (File.Exists(png) && new FileInfo(png).Length > 0) return png;

            var info = new SHFILEINFO();
            var ok = SHGetFileInfo(exe, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (ok == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Directory.CreateDirectory(dir);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(src));
                using var fs = File.Create(png);
                encoder.Save(fs);
                return png;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null; // 图标取不到不是错误：卡片退回实例芯片
        }
    }
}
