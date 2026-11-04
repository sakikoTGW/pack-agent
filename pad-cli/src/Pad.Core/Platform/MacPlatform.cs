using System.IO;
using System.Diagnostics;

namespace Pad.Core;

/// <summary>
/// macOS 实现（Linux 复用同一套：ln / ps / sh 都有）。
/// 与 Windows 的差别：junction → symlink、cmd /k → Terminal.app、WMI/PEB → ps。
/// </summary>
public sealed class MacPlatform : IPlatform
{
    public string Name => OperatingSystem.IsMacOS() ? "macos" : "linux";
    public bool IsWindows => false;

    // ---- 链接 ----------------------------------------------------------

    public void CreateDirectoryLink(string link, string target)
    {
        var parent = Path.GetDirectoryName(link);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        // -f 覆盖已有、-n 不跟随已有链接（等价于「把链接本身换掉」）。
        var (code, _, err) = Run("/bin/ln", ["-sfn", target, link]);
        if (code != 0 || !Directory.Exists(link))
            throw new IOException($"ln -s failed for {link}: {err.Trim()}");
    }

    public void CreateFileLink(string link, string target)
    {
        var parent = Path.GetDirectoryName(link);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        var (code, _, err) = Run("/bin/ln", ["-sfn", target, link]);
        if (code != 0)
            throw new IOException($"ln -s failed for {link}: {err.Trim()}");
    }

    public string? ReadLinkTarget(string path)
    {
        var (code, output, _) = Run("/usr/bin/readlink", [path]);
        return code == 0 && output.Trim().Length > 0 ? output.Trim() : null;
    }

    // ---- 终端 ----------------------------------------------------------

    /// <summary>
    /// Terminal.app 只能「打开一个 .command 文件」，所以先落一个临时脚本再 open。
    /// 脚本保留（调试用），放在临时目录。
    /// </summary>
    public Process? StartConsole(string command, string workingDirectory, string? title = null)
    {
        try
        {
            var dir = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory;
            var script = Path.Combine(Path.GetTempPath(), $"pad-console-{Guid.NewGuid():N}.command");
            var text = "#!/bin/sh\n" + (title is { Length: > 0 } ? $"echo {ShellQuote(title)}\n" : "") +
                       $"cd {ShellQuote(dir)}\n{command}\n";
            File.WriteAllText(script, text, new System.Text.UTF8Encoding(false));
            Run("/bin/chmod", ["+x", script]);

            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("open")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-a");
                psi.ArgumentList.Add("Terminal");
                psi.ArgumentList.Add(script);
                return Process.Start(psi);
            }

            // Linux：没有 Terminal.app，按常见终端模拟器依次尝试。
            foreach (var (term, useFlag) in new[]
                     {
                         ("x-terminal-emulator", true),
                         ("gnome-terminal", false),
                         ("konsole", true),
                         ("xterm", true),
                     })
            {
                var exe = Proc.Which(term);
                if (exe is null) continue;
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                if (useFlag) psi.ArgumentList.Add("-e");
                else psi.ArgumentList.Add("--");
                psi.ArgumentList.Add(script);
                try { return Process.Start(psi); } catch { /* 下一个 */ }
            }
            return null;
        }
        catch { return null; }
    }

    public (string Exe, string ArgsPrefix) Shell() => ("/bin/sh", "-c");

    // ---- 进程表 --------------------------------------------------------

    public IReadOnlyList<PlatformProcess> Processes(int cap = 900)
    {
        var list = new List<PlatformProcess>();
        var (code, output, _) = Run("/bin/ps", ["-eo", "pid=,command="]);
        if (code != 0) return list;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var space = line.IndexOf(' ');
            var pidText = space < 0 ? line : line[..space];
            if (!int.TryParse(pidText, out var pid)) continue;
            list.Add(new PlatformProcess(pid, space < 0 ? "" : line[(space + 1)..].Trim()));
            if (list.Count >= cap) break;
        }
        return list;
    }

    // ---- 控制台 --------------------------------------------------------

    public bool AttachConsole(int pid) => false;
    public void AllocateConsole() { }

    // ---- 工具 ----------------------------------------------------------

    static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    static (int Code, string Output, string Error) Run(string exe, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return (-1, "", "start failed");
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);
            return (p.HasExited ? p.ExitCode : -1, output, error);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
