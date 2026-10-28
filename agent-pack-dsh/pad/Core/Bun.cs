using System.IO;

namespace Pad.Core;

/// <summary>
/// The projection compiler and pack-index CLI are TypeScript/Rust driven by Bun.
/// PAD cannot assume Bun is on PATH: user installs move, PATH changes after updates.
/// Resolve it from the usual places and, if it is really absent, install it once from
/// npm (the same place DSH releases come from) instead of failing the catalog.
/// </summary>
public static class Bun
{
    static readonly string[] KnownDirs =
    [
        @"%USERPROFILE%\.bun\bin",
        @"%APPDATA%\npm",                       // `npm i -g bun`
        @"%LOCALAPPDATA%\pack-agent-dsh\tools\bun",
        @"%LOCALAPPDATA%\Programs\bun",
        @"%ProgramFiles%\Bun",
    ];

    /// <summary>Directories that may hold bun.exe / bun.cmd, in probe order.</summary>
    public static IEnumerable<string> Dirs()
    {
        var env = Environment.GetEnvironmentVariable("BUN_INSTALL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return Path.Combine(env!, "bin");
            yield return env!;
        }
        foreach (var raw in KnownDirs)
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            if (expanded.Length > 0 && !expanded.Contains('%')) yield return expanded;
        }
        yield return Path.Combine(AppContext.BaseDirectory, "bun");
    }

    public static string? Exe(string dir)
    {
        foreach (var name in new[] { "bun.exe", "bun.cmd", "bun.bat", "bun" })
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

    /// <summary>Full path of bun, or null when none of the known places has it.</summary>
    public static string? Locate()
    {
        var onPath = Proc.Which("bun");
        if (onPath is not null) return onPath;
        foreach (var dir in Dirs())
        {
            if (Exe(dir) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>Directory to prepend to child PATH so `node → spawnSync('bun')` resolves.</summary>
    public static string? Dir()
    {
        var exe = Locate();
        if (exe is null) return null;
        try { return Path.GetDirectoryName(exe); }
        catch { return null; }
    }

    /// <summary>
    /// Make sure bun exists. Installs it from npm when missing (no shell-outs beyond the
    /// npm that ships with the node PAD already requires). Callers pass their job log so
    /// a slow install is visible instead of looking hung.
    /// </summary>
    public static async Task EnsureAsync(IProgress<string>? log, CancellationToken ct)
    {
        if (Locate() is not null) return;

        var npm = Proc.Which("npm.cmd") ?? Proc.Which("npm");
        if (npm is null)
            throw new PadError("PA028", "bun was not found and npm is missing too",
                "bun", "npm", ["先装 node/npm，再装 bun：npm i -g bun"]);

        log?.Report("[PAD] 没找到 bun，正在用 npm 安装（装包链需要它）…");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var res = await Proc.Run(npm, "install -g bun", home, log, ct);
        Proc.Forget("bun");
        if (res.ExitCode != 0 || Locate() is null)
        {
            log?.Report(res.Tail);
            throw new PadError("PA028", "bun install failed",
                "npm i -g bun", res.Tail,
                ["在终端手动跑 npm i -g bun，装好后点刷新"]);
        }
        log?.Report("[PAD] bun 装好了");
    }
}
