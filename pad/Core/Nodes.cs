using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pad.Core;

/// <summary>
/// Starts one instance in one surface. The web surface announces its endpoint on
/// stdout as <c>dsh web: &lt;url&gt;</c> — and that url carries a per-run token, so the
/// line is the only source of truth. A port probe can succeed before the token exists.
///
/// Output goes to a file, not a pipe: the server outlives PAD, and a pipe whose reader
/// closed turns the next log write into EPIPE and kills the instance.
///
/// PAD owns the process and the window; DSH owns the endpoint (see DECISIONS.md D16).
/// </summary>
public static class Nodes
{
    public sealed record Opened(
        string Home,
        string Profile,
        string Surface,
        int Port,
        string Url,
        int Pid,
        string LogPath,
        bool Verified = false);

    /// <summary>One instance PAD started and still believes is running.</summary>
    public sealed record Running(
        string Profile,
        string Surface,
        int Port,
        string Url,
        int Pid,
        string StartedAt,
        string LogPath,
        bool Verified = false);

    /// <summary>
    /// PAD's own ledger of what it started. Lives beside the instance, not in the DSH
    /// home's runtime files, so DSH never sees it.
    /// </summary>
    public static class Ledger
    {
        static readonly JsonSerializerOptions Camel = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        public static string PathFor(string home) => System.IO.Path.Combine(home, ".pad", "nodes.json");

        public static IReadOnlyList<Running> Load(string home)
        {
            var path = PathFor(home);
            if (!File.Exists(path)) return [];
            try
            {
                // ReadAllText honours a BOM; parsing raw bytes does not, and a BOM-prefixed
                // ledger then reads as empty — silently losing every running node. Hand-edited
                // and tool-written ledgers both turn up here.
                var text = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<Running>>(nodes.GetRawText(), Camel) ?? [];
                return [];
            }
            catch
            {
                return [];
            }
        }

        public static void Save(string home, IEnumerable<Running> entries)
        {
            var path = PathFor(home);
            var dir = System.IO.Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            var payload = new Dictionary<string, object> { ["version"] = 1, ["nodes"] = entries.ToList() };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, Camel));
        }

        public static void Record(string home, Running entry) =>
            Save(home, Load(home).Where(e => !string.Equals(e.Profile, entry.Profile, StringComparison.OrdinalIgnoreCase)).Append(entry));

        public static Running? Find(string home, string? profile, int? port) =>
            Load(home).FirstOrDefault(e =>
                (profile is null || string.Equals(e.Profile, profile, StringComparison.OrdinalIgnoreCase)) &&
                (port is null || e.Port == port));

        public static void Forget(string home, Running entry) =>
            Save(home, Load(home).Where(e => !(e.Port == entry.Port &&
                string.Equals(e.Profile, entry.Profile, StringComparison.OrdinalIgnoreCase))));
    }


    const string Announcement = "dsh web: ";

    static readonly string[] EdgeCandidates =
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    ];

    /// <summary>Where a system's layer keeps its DSH home. Derived, never stored (D11).</summary>
    public static string LayerHome(string systemDir, string layer) =>
        Path.Combine(systemDir, "layers", layer, "dsh-home");

    public static IReadOnlyList<string> Layers(string systemDir)
    {
        var dir = Path.Combine(systemDir, "layers");
        if (!Directory.Exists(dir)) return [];
        return Directory
            .EnumerateDirectories(dir)
            .Select(d => Path.GetFileName(d))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>What <see cref="MaterializeProfile"/> did, including any half-state it removed.</summary>
    public sealed record ProfileMaterialization(
        bool Ok,
        bool DirWasNew,
        bool DirGone,
        string ProfileDir,
        string ManifestPath,
        string LogPath,
        string LogTail);

    /// <summary>
    /// DSH auto-initializes only its five shipped profiles (web/headless/sdk/sdk-minimal/acp).
    /// A layer profile has a custom name, so PAD asks DSH to copy a template into it — using
    /// a dump mode, which composes and writes the profile without booting anything.
    ///
    /// The dump is not atomic: it builds <c>profiles/&lt;name&gt;/</c> before the manifest exists.
    /// On failure a directory this call created is removed, while a directory that was already
    /// there is left alone, and the dump log is returned so the caller can quote its tail.
    /// </summary>
    public static async Task<ProfileMaterialization> MaterializeProfile(
        string home, string profile, string template, string bin, string nodeExe, TimeSpan timeout, CancellationToken ct)
    {
        var profileDir = Path.Combine(home, "profiles", profile);
        var manifest = Path.Combine(profileDir, "package.json");
        if (ManifestUsable(manifest))
            return new(true, false, false, profileDir, manifest, "", "");

        var dirWasNew = !Directory.Exists(profileDir);

        var padDir = Path.Combine(home, ".pad");
        Directory.CreateDirectory(padDir);
        var logPath = Path.Combine(padDir, $"profile-{profile}.log");

        ProfileMaterialization Failed()
        {
            var tail = Tail(logPath, 4000);
            if (dirWasNew)
            {
                // layer list reads this directory as "has a profile": a half-built one
                // would make the failure look like success on the next list. A directory
                // that was already there belongs to the user and is never removed here.
                try
                {
                    if (Directory.Exists(profileDir))
                        Directory.Delete(profileDir, recursive: true);
                }
                catch { /* best effort; the error says whether it went away */ }
            }
            return new(false, dirWasNew, !Directory.Exists(profileDir), profileDir, manifest, logPath, tail);
        }

        var command = $"\"{nodeExe}\" \"{bin}\" --profile {profile} --from-default-profile {template} --dump-config > \"{logPath}\" 2>&1";
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + command + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = home,
        };
        psi.Environment["DSH_HOME"] = home;

        Process? proc;
        try { proc = Process.Start(psi); }
        catch { return Failed(); }
        if (proc is null) return Failed();

        using (proc)
        {
            // A cancelled or broken wait must not leave the dump running, nor leave the
            // half-built profile behind for layer list to mistake for a real one.
            try
            {
                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    ct.ThrowIfCancellationRequested();
                    if (proc.HasExited) break;
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
                if (!proc.HasExited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* nothing left to stop */ }
                    try { proc.WaitForExit(3000); } catch { /* it exited under us */ }
                }
            }
            catch
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                try { proc.WaitForExit(3000); } catch { /* already gone */ }
                try { Failed(); } catch { /* keep the original exception */ }
                throw;
            }
        }

        return ManifestUsable(manifest)
            ? new(true, dirWasNew, false, profileDir, manifest, logPath, "")
            : Failed();
    }

    /// <summary>
    /// A complete dump ends with a parseable package.json. Existence alone is not proof:
    /// a process killed mid-write leaves a truncated manifest that would boot as a bad
    /// profile and only fail later, after the announcement timeout.
    /// </summary>
    static bool ManifestUsable(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    public static int FreePort()    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// The same bind dsh will attempt a moment later. An explicit --port that cannot be
    /// bound fails here instead of as PA080 after the ninety-second announcement wait.
    /// The probe socket is released at once; a concurrent grab after this check is the
    /// race dsh loses on its own (see FreePort above for the same trade-off).
    /// </summary>
    static bool PortTaken(int port, out int ownerPid)
    {
        ownerPid = 0;
        if (port is < 1 or > 65535) return false; // let dsh reject a nonsensical --port
        try
        {
            var probe = new TcpListener(IPAddress.Loopback, port);
            probe.Start();
            probe.Stop();
            return false;
        }
        catch (SocketException)
        {
            ownerPid = PortOwnerPid(port);
            return true;
        }
    }

    /// <summary>
    /// Best-effort owner of a listening port. netstat is the one stock Windows tool that
    /// maps port to pid without a WMI round-trip. Any failure keeps the pid at 0: the
    /// caller still reports the port as taken, just without naming the process.
    /// </summary>
    static int PortOwnerPid(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p tcp",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var netstat = Process.Start(psi);
            if (netstat is null) return 0;
            var text = netstat.StandardOutput.ReadToEnd();
            if (!netstat.WaitForExit(3000))
            {
                try { netstat.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return 0;
            }
            foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var cols = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 5) continue;
                if (!cols[1].EndsWith(":" + port, StringComparison.Ordinal)) continue;
                if (!string.Equals(cols[3], "LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(cols[^1], out var pid) && pid > 0) return pid;
            }
        }
        catch { /* netstat missing, blocked, or unreadable: no pid */ }
        return 0;
    }

    /// <summary>" · pid 1234 (node)" when the owner is known, empty when it is not.</summary>
    static string OwnerText(int pid)
    {
        if (pid <= 0) return "";
        try
        {
            using var owner = Process.GetProcessById(pid);
            return $" · pid {pid} ({owner.ProcessName})";
        }
        catch
        {
            return " · pid " + pid;
        }
    }

    /// <summary>
    /// Boot <c>--profile &lt;name&gt; --port &lt;n&gt; --no-open</c> detached, and wait for the
    /// announced endpoint by tailing its log. Throws with that log's tail when it never arrives.
    /// </summary>
    public static async Task<Opened> OpenWeb(
        string home, string profile, string bin, string nodeExe, int? port, TimeSpan timeout, CancellationToken ct)
    {
        var chosen = port ?? FreePort();

        // An explicit --port is a promise. If something already holds it, dsh would
        // only admit that as PA080 after the announcement wait, with the real cause
        // buried in a log nobody has read yet.
        if (port is { } asked && PortTaken(asked, out var ownerPid))
            throw new PadError("PA004", "that port is already taken", $"127.0.0.1:{asked}",
                $"port {asked} is already in use on 127.0.0.1{OwnerText(ownerPid)}",
                ["pad cli node open " + home + " --port <other port>",
                 "pad cli node open " + home + " --surface web --profile " + profile]);

        var logDir = Path.Combine(home, ".pad", "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"{profile}-{chosen}.log");
        File.WriteAllText(logPath, "");

        // cmd.exe is the only way to hand a detached child a file as its stdout.
        // The doubled-quote form is what cmd needs when the command itself is quoted.
        var command = $"\"{nodeExe}\" \"{bin}\" --profile {profile} --port {chosen} --no-open > \"{logPath}\" 2>&1";
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c \"" + command + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = home,
        };
        psi.Environment["DSH_HOME"] = home;

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex)
        {
            throw new PadError("PA079", "could not start the dsh process", nodeExe, ex.Message,
                ["pad cli node open <home> --bin <bin.js>"]);
        }

        var deadline = DateTime.UtcNow + timeout;
        string? url = null;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var text = ReadShared(logPath);
            var at = text.IndexOf(Announcement, StringComparison.Ordinal);
            if (at >= 0)
            {
                var line = text[(at + Announcement.Length)..].Split('\n')[0].Trim();
                if (line.Length > 0) { url = line; break; }
            }
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        if (url is null)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            var tail = ReadShared(logPath);
            throw new PadError("PA080", "the profile never announced its endpoint", logPath,
                tail.Length > 4000 ? tail[^4000..] : tail,
                ["pad cli node open <home> --bin <bin.js>",
                 "dsh --profile " + profile + " --port " + chosen + " --no-open"]);
        }

        return new Opened(home, profile, "web", chosen, url, proc.Id, logPath, Verified: true);
    }

    /// <summary>
    /// True when a desktop shell already holds its single-instance lockfile.
    ///
    /// The shell is one process tree with one fixed endpoint, so "already running" is
    /// not a warning: a second launch cannot produce a second isolated instance. PAD
    /// reports it instead of starting one that would either be absorbed or fight for
    /// the endpoint.
    /// </summary>
    public static bool DesktopShellRunning(string? userDataDir = null)
    {
        var dir = userDataDir ?? DefaultDesktopUserData();
        var lockPath = Path.Combine(dir, "lockfile");
        if (!File.Exists(lockPath)) return false;
        try
        {
            using var fs = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string DefaultDesktopUserData() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsh-desktop");

    /// <summary>
    /// Boot the desktop shell as the <c>app</c> surface, on its own Electron userData.
    ///
    /// The shell derives its DSH home as <c>&lt;userData&gt;/harness</c>, so pointing
    /// <c>--user-data-dir</c> at a per-project directory gives that project its own home,
    /// its own single-instance lock, and its own cache. Readiness is the harness's own
    /// line: <c>[harness-node] DSH_HOME=&lt;userData&gt;\harness</c> — PAD asserts on it
    /// rather than assuming the switch was honoured.
    /// </summary>
    public static async Task<Opened> OpenApp(
        string home, string exePath, string? userDataDir, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            throw new PadError("PA087", "no desktop shell executable at that path", exePath ?? "(none)", null,
                ["pad cli node open <home> --surface app --exe <DSH Desktop.exe>"]);

        var dataDir = Path.GetFullPath(userDataDir ?? Path.Combine(home, ".pad", "desktop"));
        Directory.CreateDirectory(dataDir);

        if (DesktopShellRunning(dataDir))
            throw new PadError("PA086", "a desktop shell is already running for that user data dir", dataDir,
                "one shell per user data dir — pick another project, or close that window first",
                ["pad cli node list " + home,
                 "close that DSH Desktop window, then retry"]);

        Process proc;
        try
        {
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--user-data-dir \"{dataDir}\"",
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex)
        {
            throw new PadError("PA088", "could not start the desktop shell", exePath, ex.Message,
                ["pad cli node open <home> --surface app --exe <DSH Desktop.exe>"]);
        }

        var logPath = Path.Combine(dataDir, "logs", "harness.log");
        var expected = Path.Combine(dataDir, "harness");
        var deadline = DateTime.UtcNow + timeout;
        var verified = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (proc.HasExited)
                throw new PadError("PA089", "the desktop shell exited immediately", exePath,
                    "exit code " + proc.ExitCode,
                    ["pad cli node open <home> --surface app --exe <DSH Desktop.exe>"]);

            if (DesktopShellRunning(dataDir) && ShellHome(logPath) is { } seen)
            {
                verified = string.Equals(seen, expected, StringComparison.OrdinalIgnoreCase);
                if (verified) break;
                throw new PadError("PA090", "the shell ignored the user data dir", dataDir,
                    "it reported DSH_HOME=" + seen + " but PAD expected " + expected,
                    ["close that DSH Desktop window",
                     "pad cli node open <home> --surface app --exe <DSH Desktop.exe> --user-data <dir>"]);
            }

            await Task.Delay(400, ct).ConfigureAwait(false);
        }

        return new Opened(home, "app", "app", 0, "", proc.Id, logPath, verified);
    }

    /// <summary>The DSH home the shell itself reported, or null when it has not said yet.</summary>
    static string? ShellHome(string logPath)
    {
        var text = ReadShared(logPath);
        const string marker = "[harness-node] DSH_HOME=";
        var at = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        var line = text[(at + marker.Length)..].Split('\n')[0].Trim();
        return line.Length == 0 ? null : line;
    }

    /// <summary>
    /// Boot the terminal surface. There is no endpoint to wait for, so readiness is
    /// "the console process survived the first few seconds" — DSH gives a terminal app
    /// no handshake to wait for.
    /// </summary>
    public static async Task<Opened> OpenTerminal(
        string home, string profile, string bin, string nodeExe, string? title, TimeSpan timeout, CancellationToken ct)
    {
        var padDir = Path.Combine(home, ".pad");
        Directory.CreateDirectory(padDir);
        var logPath = Path.Combine(padDir, "logs", $"tui-{profile}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        // A fresh console: UseShellExecute=false + CreateNoWindow=false gives the child
        // its own console window, so cmd /k keeps the TUI alive after PAD exits.
        var command = $"\"{nodeExe}\" \"{bin}\" --profile {profile}";
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/k \"" + (string.IsNullOrWhiteSpace(title) ? command : $"title {title} && {command}") + "\"",
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = home,
        };
        psi.Environment["DSH_HOME"] = home;

        Process term;
        try
        {
            term = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch (Exception ex)
        {
            throw new PadError("PA079", "could not start the terminal", "cmd.exe", ex.Message,
                ["pad cli node open <home> --surface tui --bin <bin.js>"]);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (term.HasExited)
                throw new PadError("PA083", "the terminal exited immediately", home,
                    "exit code " + term.ExitCode + "; the profile may not exist yet",
                    ["pad cli node open <home> --surface tui --profile <p>",
                     "dsh plugin --profile <p> add <package>",
                     "pad cli node list " + home]);
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        // The harness pid is not discoverable by process name here: the desktop ships an
        // Electron-based node shim, so the child is "DSH Desktop.exe", not "node.exe".
        // PAD records the console process instead and closes the whole tree.
        return new Opened(home, profile, "tui", 0, "", term.Id, logPath);
    }

    /// <summary>
    /// Give the announced url a window of its own. Edge's app mode is a chromeless
    /// window with no address bar; fall back to the default browser when missing.
    /// </summary>
    public static bool TryOpenWindow(string url, out string how)
    {
        foreach (var edge in new[] { Proc.Which("msedge") }.Concat(EdgeCandidates).Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!File.Exists(edge!)) continue;
            try
            {
                Process.Start(new ProcessStartInfo(edge!, "--app=" + url) { UseShellExecute = false });
                how = "msedge --app";
                return true;
            }
            catch { /* try the next candidate */ }
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            how = "default browser";
            return true;
        }
        catch
        {
            how = "none";
            return false;
        }
    }

    /// <summary>The last <paramref name="limit"/> characters of a log, empty when unreadable.</summary>
    static string Tail(string path, int limit)
    {
        var text = ReadShared(path).TrimEnd('\r', '\n');
        return text.Length <= limit ? text : text[^limit..];
    }

    static string ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }
}
