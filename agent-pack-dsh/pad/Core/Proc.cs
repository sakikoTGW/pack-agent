using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pad.Core;

public sealed record ProcResult(int ExitCode, string Tail);

/// <summary>Child-process plumbing. Windows-only on purpose: PAD ships as a Windows app.</summary>
public static class Proc
{
    static readonly Dictionary<string, string?> WhichCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolve a command on PATH. Returns null when it is not installed.</summary>
    public static string? Which(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (WhichCache)
        {
            if (WhichCache.TryGetValue(name, out var hit)) return hit;
        }
        var found = WhichUncached(name);
        lock (WhichCache) { WhichCache[name] = found; }
        return found;
    }

    static string? WhichUncached(string name)
    {
        var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in dirs)
        {
            var trimmed = dir.Trim('"');
            if (trimmed.Length == 0) continue;
            foreach (var ext in exts)
            {
                try
                {
                    var candidate = Path.Combine(trimmed, name + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* malformed PATH entry */ }
            }
            try
            {
                var bare = Path.Combine(trimmed, name);
                if (File.Exists(bare)) return bare;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    public static bool Alive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>
    /// Run a command and collect its output. <c>.cmd</c> shims such as pnpm cannot be
    /// launched directly without a shell, so they are routed through cmd.exe.
    /// </summary>
    public static async Task<ProcResult> Run(string exe, string args, string cwd,
        IProgress<string>? log, CancellationToken ct, Dictionary<string, string>? env = null)
    {
        var resolved = await Task.Run(() => Which(exe), ct);
        if (resolved is null)
            throw new PadError("PA011", $"`{exe}` was not found on PATH", "PATH", exe,
                [$"安装 {exe} 并确认它在 PATH 里"]);

        var viaShell = resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                       || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo
        {
            FileName = viaShell ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : resolved,
            Arguments = viaShell ? $"/d /c \"\"{resolved}\" {args}\"" : args,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (env is not null)
            foreach (var (k, v) in env) psi.Environment[k] = v;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tail = new Queue<string>();
        var gate = new object();

        void Collect(string? line)
        {
            if (line is null) return;
            log?.Report(line);
            lock (gate)
            {
                tail.Enqueue(line);
                while (tail.Count > 40) tail.Dequeue();
            }
        }

        // Wait on the process itself, not on stdout reaching EOF. pnpm spawns
        // grandchildren that inherit the redirected pipe handles, so awaiting
        // WaitForExitAsync (which also waits for the streams) hangs long after
        // pnpm is gone.
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.Exited += (_, _) =>
        {
            try { exited.TrySetResult(proc.ExitCode); }
            catch { exited.TrySetResult(-1); }
        };

        proc.OutputDataReceived += (_, e) => Collect(e.Data);
        proc.ErrorDataReceived += (_, e) => Collect(e.Data);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await using var _ = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        var code = await exited.Task;
        // Let trailing output land, but never block on it.
        await Task.Delay(200, CancellationToken.None);
        lock (gate) return new ProcResult(code, string.Join(Environment.NewLine, tail));
    }

    /// <summary>Start a detached process without capturing its console.</summary>
    public static Process? Start(string exe, string args, string cwd, Dictionary<string, string>? env = null,
        bool newConsole = false)
    {
        var resolved = Which(exe) ?? exe;
        var psi = new ProcessStartInfo
        {
            FileName = resolved,
            Arguments = args,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = !newConsole,
        };
        if (env is not null)
            foreach (var (k, v) in env) psi.Environment[k] = v;
        return Process.Start(psi);
    }

    /// <summary>
    /// The Harness node process for this profile. <c>wt</c>'s pid is not this:
    /// Windows Terminal hands off the tab and exits.
    /// </summary>
    public static int FindDshPid(string profile, string? bin)
    {
        foreach (var (pid, cmd) in NodeCommandLines())
        {
            if (!LaunchPolicy.CommandLineIsDsh(cmd, profile, bin)) continue;
            if (Alive(pid)) return pid;
        }
        return 0;
    }

    static readonly object NodeCmdLock = new();
    static List<(int Pid, string Cmd)> NodeCmdCache = [];
    static long NodeCmdAt;
    static int NodeCmdBusy;

    /// <summary>
    /// Snapshot of node.exe command lines. Powershell + WMI can sit for
    /// several seconds; callers on the UI thread must not WaitForExit or
    /// PA040 fires right after 启动.
    /// </summary>
    static List<(int Pid, string Cmd)> NodeCommandLines()
    {
        EnsureNodeCmdCache();
        lock (NodeCmdLock) return [..NodeCmdCache];
    }

    static void EnsureNodeCmdCache()
    {
        var now = Environment.TickCount64;
        lock (NodeCmdLock)
        {
            if (now - NodeCmdAt < 1500) return;
        }
        if (Interlocked.CompareExchange(ref NodeCmdBusy, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (!TryQueryNodeCommandLines(out var rows)) return;
                lock (NodeCmdLock)
                {
                    NodeCmdCache = rows;
                    NodeCmdAt = Environment.TickCount64;
                }
            }
            finally { Interlocked.Exchange(ref NodeCmdBusy, 0); }
        });
    }

    /// <summary>
    /// False means the query was killed with no rows. Do not stamp an empty
    /// cache as fresh: the next FindDshPid must retry, or a live TUI is
    /// reported as PA025.
    /// </summary>
    static bool TryQueryNodeCommandLines(out List<(int, string)> rows)
    {
        rows = [];
        var chunks = new StringBuilder();
        var gate = new object();
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='node.exe'\\\" | ForEach-Object { $_.ProcessId.ToString() + [char]9 + $_.CommandLine }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (gate) chunks.AppendLine(e.Data);
            };
            proc.ErrorDataReceived += (_, _) => { };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            var finished = proc.WaitForExit(20_000);
            if (!finished)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* hung query */ }
            }
            try { proc.WaitForExit(2000); } catch { /* already gone */ }
            string text;
            lock (gate) text = chunks.ToString();
            foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var tab = raw.IndexOf('\t');
                if (tab <= 0) continue;
                if (!int.TryParse(raw[..tab], out var pid) || pid <= 0) continue;
                rows.Add((pid, raw[(tab + 1)..]));
            }
            if (!finished && rows.Count == 0) return false;
            return true;
        }
        catch
        {
            return rows.Count > 0;
        }
    }
}
