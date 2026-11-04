using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pad.Core;

/// <summary>
/// Windows 实现：junction / cmd / WMI+PEB / AttachConsole。
/// 所有原语都是原来 PAD 里那些代码的搬运，行为保持一致。
/// </summary>
public sealed class WindowsPlatform : IPlatform
{
    public string Name => "win32";
    public bool IsWindows => true;

    // ---- 链接 ----------------------------------------------------------

    public void CreateDirectoryLink(string link, string target)
    {
        var parent = Path.GetDirectoryName(link);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        // 快路径：原生 junction（3 万个链接几秒；cmd mklink 每个要 ~17ms，导出会卡 8 分钟）。
        if (Reparse.TryCreateJunction(link, target)) return;
        try
        {
            Directory.CreateSymbolicLink(link, Path.GetRelativePath(parent!, target));
            return;
        }
        catch
        {
            // junctions do not need developer mode
        }

        var psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(15_000);
        if (!Directory.Exists(link))
            throw new IOException("mklink /J failed for " + link);
    }

    public void CreateFileLink(string link, string target)
    {
        var parent = Path.GetDirectoryName(link);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        try
        {
            File.CreateSymbolicLink(link, Path.GetRelativePath(parent!, target));
            return;
        }
        catch (UnauthorizedAccessException) { /* 没开开发者模式、也不是管理员：退回硬链接 */ }
        catch (IOException) { }

        // 硬链接同样不需要特权，但要求同一个卷（pnpm 的 store 与 node_modules 通常同卷）。
        var res = Proc.Run("cmd.exe", $"/d /c mklink /H \"{link}\" \"{target}\"",
            parent ?? Environment.CurrentDirectory, null, CancellationToken.None).GetAwaiter().GetResult();
        if (res.ExitCode != 0)
            throw new IOException($"cannot create file link `{link}` -> `{target}`: {res.Tail}");
    }

    public string? ReadLinkTarget(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (!info.Exists) return null;
            return info.LinkTarget;
        }
        catch { return null; }
    }

    // ---- 终端 ----------------------------------------------------------

    public Process? StartConsole(string command, string workingDirectory, string? title = null)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/d /k " + command)
        {
            UseShellExecute = true,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
        };
        if (!string.IsNullOrWhiteSpace(title)) psi.WindowStyle = ProcessWindowStyle.Normal;
        return Process.Start(psi);
    }

    // ---- 进程表 --------------------------------------------------------

    public IReadOnlyList<PlatformProcess> Processes(int cap = 900)
    {
        try
        {
            var rows = ProcTable.Snapshot(cap);
            var list = new List<PlatformProcess>(rows.Count);
            foreach (var r in rows) list.Add(new PlatformProcess(r.Pid, r.CommandLine));
            return list;
        }
        catch { return []; }
    }

    // ---- 控制台 --------------------------------------------------------

    [DllImport("kernel32.dll", EntryPoint = "AttachConsole")] static extern bool AttachConsoleNative(int pid);
    [DllImport("kernel32.dll", EntryPoint = "AllocConsole")] static extern bool AllocConsoleNative();

    public bool AttachConsole(int pid) => AttachConsoleNative(pid);
    public void AllocateConsole() => AllocConsoleNative();
}
