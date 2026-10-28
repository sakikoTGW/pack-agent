using System.IO;

namespace Pad.Core;

/// <summary>
/// PAD 的发行号安装与插件安装都走 pnpm。和 bun 一样，不能假设它在 PATH 里：
/// 用户机器上可能只剩 pnpm 的 store（%LOCALAPPDATA%\pnpm），命令本身不在 PATH；
/// 也可能通过 corepack/npm 装在别处。这里统一解析，真没有就用 npm 装一次。
/// </summary>
public static class Pnpm
{
    static readonly string[] KnownDirs =
    [
        @"%LOCALAPPDATA%\pnpm",
        @"%APPDATA%\npm",
        @"%ProgramFiles%\nodejs",           // corepack 的 pnpm shim
        @"%USERPROFILE%\.local\share\pnpm",
    ];

    public static IEnumerable<string> Dirs()
    {
        var home = Environment.GetEnvironmentVariable("PNPM_HOME");
        if (!string.IsNullOrWhiteSpace(home)) yield return home!;
        foreach (var raw in KnownDirs)
        {
            var dir = Environment.ExpandEnvironmentVariables(raw);
            if (dir.Length > 0 && !dir.Contains('%')) yield return dir;
        }
    }

    static string? Exe(string dir)
    {
        foreach (var name in new[] { "pnpm.exe", "pnpm.cmd", "pnpm.bat", "pnpm" })
        {
            try
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }
            catch { /* malformed dir */ }
        }
        return null;
    }

    /// <summary>pnpm 的完整路径；null = 本机确实没有。</summary>
    public static string? Locate()
    {
        var onPath = Proc.Which("pnpm");
        if (onPath is not null) return onPath;
        foreach (var dir in Dirs())
        {
            if (Exe(dir) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>子进程 PATH 要前置的目录（嵌套的 dsh plugin 也会找 pnpm）。</summary>
    public static string? Dir()
    {
        var exe = Locate();
        if (exe is null) return null;
        try { return Path.GetDirectoryName(exe); }
        catch { return null; }
    }

    /// <summary>缺 pnpm 时用 npm 装一次；装不上给出人话错误。</summary>
    public static async Task EnsureAsync(IProgress<string>? log, CancellationToken ct)
    {
        if (Locate() is not null) return;

        var npm = Proc.Which("npm.cmd") ?? Proc.Which("npm")
            ?? throw new PadError("PA028", "本机没有 pnpm，也没有 npm 可以装它",
                "pnpm", "npm", ["先装 Node.js（带 npm），再回来装发行号"]);

        log?.Report("[PAD] 没找到 pnpm，正在用 npm 安装（装 DSH 发行号/插件要用它）…");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var res = await Proc.Run(npm, "install -g pnpm", home, log, ct);
        Proc.Forget("pnpm");
        if (res.ExitCode != 0 || Locate() is null)
        {
            log?.Report(res.Tail);
            throw new PadError("PA028", "pnpm 安装失败",
                "npm i -g pnpm", res.Tail,
                ["在终端手动跑 npm i -g pnpm，装好后重试"]);
        }
        log?.Report("[PAD] pnpm 装好了");
    }
}
