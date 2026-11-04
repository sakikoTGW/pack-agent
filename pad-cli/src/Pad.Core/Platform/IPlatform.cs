using System.Diagnostics;

namespace Pad.Core;

/// <summary>进程表里的一行（平台无关的最小集）：pid + 命令行。</summary>
public sealed record PlatformProcess(int Pid, string? CommandLine = null);

/// <summary>
/// 平台原语：PAD 核心逻辑不直接碰 Win32 / cmd.exe / junction，全部走这里。
///
/// 四个原语对应 PAD 仅有的 Windows 依赖：
/// 1. 链接（Windows junction / Unix symlink）；
/// 2. 终端（cmd /k、wt、conhost → sh / Terminal.app）；
/// 3. 进程表（WMI + PEB → ps）；
/// 4. 控制台附着（AttachConsole / AllocConsole → no-op）。
/// </summary>
public interface IPlatform
{
    /// <summary>"win32" / "macos" / "linux"。</summary>
    string Name { get; }

    bool IsWindows { get; }

    // ---- 链接 ----------------------------------------------------------

    /// <summary>建目录链接：Windows 用 junction（不需要开发者模式），Unix 用 symlink。</summary>
    void CreateDirectoryLink(string link, string target);

    /// <summary>建文件链接（软链）。</summary>
    void CreateFileLink(string link, string target);

    /// <summary>读链接目标；不是链接或读不到返回 null。</summary>
    string? ReadLinkTarget(string path);

    // ---- 终端 ----------------------------------------------------------

    /// <summary>
    /// 在新终端里跑一条命令并保持窗口（Windows: cmd /d /k；macOS: 生成 .command 交给 Terminal.app）。
    /// 返回终端进程；起不来返回 null。
    /// </summary>
    Process? StartConsole(string command, string workingDirectory, string? title = null);

    /// <summary>跑命令行的 shell：Windows 是 cmd.exe /d /c，macOS/Linux 是 /bin/sh -c。</summary>
    (string Exe, string ArgsPrefix) Shell();

    // ---- 进程表 --------------------------------------------------------

    /// <summary>当前进程表快照（pid + 命令行）；读不到返回空表，不抛。</summary>
    IReadOnlyList<PlatformProcess> Processes(int cap = 900);

    // ---- 控制台 --------------------------------------------------------

    /// <summary>把当前进程附着到 pid 的控制台（Windows 专用；Unix 返回 false）。</summary>
    bool AttachConsole(int pid);

    /// <summary>分配一个控制台（Windows 专用；Unix no-op）。</summary>
    void AllocateConsole();
}
