using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pad.Core;

/// <summary>
/// 原生 junction 创建（不走 cmd /c mklink——每个链接起一个进程要 ~17ms，
/// 3 万个链接就是 8 分钟，实测便携导出卡在这里）。不需要管理员/开发者模式。
/// </summary>
public static class Reparse
{
    const uint GENERIC_WRITE = 0x40000000;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    const uint FSCTL_SET_REPARSE_POINT = 0x000900A4;
    const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr handle, uint code, byte[] inBuf, int inSize,
        IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr process);
    [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr process);

    /// <summary>暂停/恢复外部压缩进程（7z 在跑的时候用户点暂停）。</summary>
    public static void SuspendProcess(System.Diagnostics.Process proc)
    {
        try { if (!proc.HasExited) NtSuspendProcess(proc.Handle); } catch { /* ignore */ }
    }

    public static void ResumeProcess(System.Diagnostics.Process proc)
    {
        try { if (!proc.HasExited) NtResumeProcess(proc.Handle); } catch { /* ignore */ }
    }

    public static bool TryCreateJunction(string link, string target)
    {
        try
        {
            var parent = Path.GetDirectoryName(link);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (Directory.Exists(link) || File.Exists(link)) return true;
            Directory.CreateDirectory(link);

            var handle = CreateFileW(link, GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
            try
            {
                var data = BuildMountPoint(target);
                return DeviceIoControl(handle, FSCTL_SET_REPARSE_POINT, data, data.Length,
                    IntPtr.Zero, 0, out _, IntPtr.Zero);
            }
            finally { CloseHandle(handle); }
        }
        catch { return false; }
    }

    static byte[] BuildMountPoint(string target)
    {
        var substitute = @"\??\" + Path.GetFullPath(target);
        var print = Path.GetFullPath(target);
        var substituteBytes = Encoding.Unicode.GetBytes(substitute);
        var printBytes = Encoding.Unicode.GetBytes(print);

        // REPARSE_DATA_BUFFER + MountPointReparseBuffer
        var pathBuffer = new byte[substituteBytes.Length + 2 + printBytes.Length + 2];
        Buffer.BlockCopy(substituteBytes, 0, pathBuffer, 0, substituteBytes.Length);
        Buffer.BlockCopy(printBytes, 0, pathBuffer, substituteBytes.Length + 2, printBytes.Length);

        var total = 8 /*header*/ + 8 /*mount header*/ + pathBuffer.Length;
        var data = new byte[total];
        var tag = IO_REPARSE_TAG_MOUNT_POINT;
        BitConverter.GetBytes(tag).CopyTo(data, 0);
        BitConverter.GetBytes((ushort)(8 + pathBuffer.Length)).CopyTo(data, 4); // ReparseDataLength
        BitConverter.GetBytes((ushort)0).CopyTo(data, 6);                       // Reserved
        BitConverter.GetBytes((ushort)0).CopyTo(data, 8);                       // SubstituteNameOffset
        BitConverter.GetBytes((ushort)substituteBytes.Length).CopyTo(data, 10); // SubstituteNameLength
        BitConverter.GetBytes((ushort)(substituteBytes.Length + 2)).CopyTo(data, 12); // PrintNameOffset
        BitConverter.GetBytes((ushort)printBytes.Length).CopyTo(data, 14);      // PrintNameLength
        Buffer.BlockCopy(pathBuffer, 0, data, 16, pathBuffer.Length);
        return data;
    }
}
