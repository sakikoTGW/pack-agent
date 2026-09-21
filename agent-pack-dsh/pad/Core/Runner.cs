using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pad.Core;

public sealed record LaunchResult(int Pid, string Script, string? WebUrl);

/// <summary>
/// Starts one profile. PAD does not host DSH: a terminal profile gets a real
/// terminal window, and web gets the browser. What PAD runs is written to a
/// <c>launch-&lt;profile&gt;.cmd</c> beside the instance so it is inspectable and
/// double-clickable on its own.
/// </summary>
public sealed class Runner(Launcher launcher)
{
    readonly Launcher _l = launcher;

    public string ScriptPath(Instance inst, string profile) =>
        Path.Combine(_l.InstanceDir(inst.Id), $"launch-{profile}.cmd");

    public string WriteScript(Instance inst, string profile, PadSettings? settings = null)
    {
        var bin = _l.DshBin(inst.Dsh.Version);
        var ov = inst.Launch;

        // Node in the script: kit-relative if this release shipped one, else PATH
        // `node`. Never bake C:\Program Files\nodejs so a unzipped copy still runs.
        var nodeAbs = EmitNodeAbs(inst, settings);

        // Trials ride along as --patch overlays. They are in the script rather than
        // hidden in PAD, so the same double-click reproduces the same run.
        var trials = new Trials(_l).PatchArgs(inst, profile);
        var patch = trials.Length > 0 ? " " + trials : "";

        var effTitle = FormatTitle(
            NonEmpty(ov?.Title) ?? settings?.Launch.Title, inst.Name, profile);
        var effTelemetry = ov?.TelemetryDisabled ?? settings?.Launch.TelemetryDisabled ?? true;
        var effPause = ov?.PauseOnError ?? settings?.Launch.PauseOnError ?? true;
        var preCommand = NonEmpty(ov?.PreCommand) ?? NonEmpty(settings?.Launch.PreCommand);
        var preWait = ov?.PreCommandWait ?? settings?.Launch.PreCommandWait ?? true;
        var priority = settings?.Launch.ProcessPriority ?? "normal";

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine($"title {effTitle}");
        sb.AppendLine(LaunchPolicy.CmdPadInst);
        sb.AppendLine(LaunchPolicy.CmdPadRoot);
        sb.AppendLine(LaunchPolicy.CmdSetDshHome(inst.Adopted, inst.Home));
        sb.AppendLine(LaunchPolicy.CmdCleanNodeEnv);
        sb.AppendLine(LaunchPolicy.CmdClearProfilesFallback);
        if (effTelemetry)
            sb.AppendLine("set \"DSH_TELEMETRY_DISABLED=1\"");
        if (settings is not null)
            foreach (var (key, val) in settings.Launch.ParseExtraEnv())
            {
                if (LaunchPolicy.IsSecretEnvKey(key)) continue;
                sb.AppendLine($"set \"{key}={val.Replace("\"", "")}\"");
            }
        // Instance extraEnv lands after global, so a repeated key overrides the global one.
        if (!string.IsNullOrWhiteSpace(ov?.ExtraEnv))
            foreach (var (key, val) in LaunchPrefs.ParseEnv(ov.ExtraEnv))
            {
                if (LaunchPolicy.IsSecretEnvKey(key)) continue;
                sb.AppendLine($"set \"{key}={val.Replace("\"", "")}\"");
            }

        if (settings is not null)
            CredentialsGate.CopyDistributorToHome(_l, inst, settings);

        var credSrc = CredentialSource(inst, settings);
        PnpmHeal.Relink(Path.Combine(_l.VersionDir(inst.Dsh.Version), "node_modules"));
        PnpmHeal.ClearProfilesFallback(inst.Home);
        var profiles = Path.Combine(inst.Home, "profiles");
        if (Directory.Exists(profiles))
            foreach (var dir in Directory.EnumerateDirectories(profiles))
            {
                if (string.Equals(Path.GetFileName(dir), "node_modules", StringComparison.OrdinalIgnoreCase))
                    continue;
                PnpmHeal.Relink(Path.Combine(dir, "node_modules"));
            }
        if (credSrc is not null && File.Exists(credSrc))
        {
            var credDst = Path.Combine(inst.Home, ".credentials.yaml");
            if (!LaunchPolicy.SamePath(credSrc, credDst))
            {
                var srcCmd = LaunchPolicy.CmdRelativeToRoot(credSrc, _l.Root) ?? credSrc;
                sb.AppendLine($"if exist \"{srcCmd}\" copy /y \"{srcCmd}\" \"%DSH_HOME%\\.credentials.yaml\" >nul");
            }
        }
        foreach (var line in CredentialsFile.CmdLoadFromHome().Replace("\r\n", "\n").Split('\n'))
            sb.AppendLine(line);

        sb.AppendLine(LaunchPolicy.CmdCdWorkspace(inst.Adopted, inst.Workspace.Kind, inst.Workspace.Path));

        if (!string.IsNullOrWhiteSpace(preCommand))
        {
            if (preWait) sb.AppendLine(preCommand);
            else sb.AppendLine($"start \"\" {preCommand}");
        }

        var nodeCmd = EmitNode(nodeAbs, _l.Root);
        var binCmd = LaunchPolicy.CmdRelativeToRoot(bin, _l.Root)
            ?? "%PAD_ROOT%versions\\" + inst.Dsh.Version
               + "\\node_modules\\@deepseek-ai\\dsh\\lib\\bin.js";
        sb.AppendLine($"set \"PAD_NODE={nodeCmd.Trim('"')}\"");
        sb.AppendLine("if not exist \"%PAD_NODE%\" set \"PAD_NODE=node\"");
        sb.AppendLine($"set \"PAD_BIN={binCmd}\"");

        if (priority is "belownormal" or "high")
            sb.AppendLine($"start \"\" /b /wait /{priority} \"%PAD_NODE%\" \"%PAD_BIN%\" --profile {profile}{patch} %*");
        else
            sb.AppendLine($"\"%PAD_NODE%\" \"%PAD_BIN%\" --profile {profile}{patch} %*");
        sb.AppendLine("set EXIT=%ERRORLEVEL%");
        if (effPause)
        {
            sb.AppendLine("if not \"%EXIT%\"==\"0\" (");
            sb.AppendLine("  echo.");
            sb.AppendLine("  echo [PAD] dsh exited with %EXIT%");
            sb.AppendLine("  pause");
            sb.AppendLine(")");
        }

        var path = ScriptPath(inst, profile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // cmd.exe reads .cmd as ANSI unless a UTF-8 BOM is present. Chinese
        // DSH_HOME / workspace paths otherwise become garbage and node never starts.
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    string? EmitNodeAbs(Instance inst, PadSettings? settings) =>
        _l.ResolveLaunchNode(inst, settings);

    static string EmitNode(string? nodeAbs, string launcherRoot)
    {
        if (string.IsNullOrWhiteSpace(nodeAbs)) return "node";
        var rel = LaunchPolicy.CmdRelativeToRoot(nodeAbs, launcherRoot);
        if (rel is not null) return rel;
        return nodeAbs;
    }

    string ResolveNode(string? path, string? dshVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(path) && !LaunchPolicy.IsNodeAuto(path))
        {
            var p = path.Trim().Trim('"');
            if (File.Exists(p)) return Path.GetFullPath(p);
            var byName = Proc.Which(Path.GetFileNameWithoutExtension(p));
            if (byName is not null) return byName;
        }
        if (!string.IsNullOrWhiteSpace(dshVersion) && _l.NodeForRelease(dshVersion) is { } pinned)
            return pinned;
        if (string.IsNullOrWhiteSpace(dshVersion) && _l.FindRuntimeNode() is { } any)
            return any;
        return Proc.Which("node")
            ?? throw new PadError("PA011", "`node` was not found on PATH", "PATH", "node",
                ["装 Node.js 22.19+ 或 24+，或放进 runtime/node/"]);
    }

    /// <summary>Which credential file to copy into <c>$DSH_HOME/.credentials.yaml</c>.
    /// Instance override wins; otherwise the global default. Null = leave home's own.</summary>
    string? CredentialSource(Instance inst, PadSettings? settings)
    {
        var set = NonEmpty(inst.Launch?.CredentialsSet);
        if (set is null)
        {
            var def = settings?.Launch.CredentialsDefault ?? "global";
            if (def == "none") return null;
            return _l.NamedCredentialsPath(def);
        }
        if (set == "instance") return null;
        return _l.NamedCredentialsPath(set);
    }

    static string FormatTitle(string? template, string instance, string profile)
    {
        var t = string.IsNullOrWhiteSpace(template) ? "{instance} · {profile}" : template.Trim();
        return t.Replace("{instance}", instance, StringComparison.OrdinalIgnoreCase)
                .Replace("{profile}", profile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Launch a terminal profile in whichever terminal the user configured.</summary>
    public LaunchResult Launch(Instance inst, string profile, PadSettings settings)
    {
        var meta = _l.Profiles(inst).FirstOrDefault(p => p.Name == profile);
        var web = LaunchPolicy.WaitForWebReady(meta?.IsWeb == true);
        return web ? LaunchWeb(inst, profile, settings) : LaunchTerminal(inst, profile, settings);
    }

    /// <summary>Launch a terminal profile in whichever terminal the user configured.</summary>
    public LaunchResult LaunchTerminal(Instance inst, string profile, PadSettings settings)
    {
        var script = WriteScript(inst, profile, settings);
        var title = FormatTitle(
            NonEmpty(inst.Launch?.Title) ?? settings.Launch.Title, inst.Name, profile);
        var cwd = inst.Workspace.Path;

        Process? proc = settings.Kind switch
        {
            TerminalKind.Custom when !string.IsNullOrWhiteSpace(settings.TerminalCommand)
                => StartCustom(settings.TerminalCommand, script, title, cwd),
            TerminalKind.Conhost => StartConhost(script, title, cwd),
            _ => StartWindowsTerminal(script, title, cwd, settings.Launch.ReuseWtWindow)
                 ?? StartConhost(script, title, cwd),
        };

        if (proc is null)
            throw new PadError("PA025", "no terminal could be started", $"instance `{inst.Id}`",
                settings.Terminal, ["到设置页换一个终端", "或填自定义命令行"]);

        _l.TrackRun(new RunEntry
        {
            Instance = inst.Id,
            Profile = profile,
            Version = inst.Dsh.Version,
            Pid = proc.Id,
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
        });

        inst.LastProfile = profile;
        _l.SaveInstance(inst);
        return new LaunchResult(proc.Id, script, null);
    }

    /// <summary>
    /// Web binds a port. PAD starts it hidden, reads the log for <c>dsh web: http</c>,
    /// and opens the browser. The process is not killed if the line is late.
    /// </summary>
    public LaunchResult LaunchWeb(Instance inst, string profile, PadSettings settings)
    {
        var savedPause = settings.Launch.PauseOnError;
        settings.Launch.PauseOnError = false;
        var script = WriteScript(inst, profile, settings);
        settings.Launch.PauseOnError = savedPause;

        var logs = _l.LogsDir(inst.Id);
        Directory.CreateDirectory(logs);
        var logPath = Path.Combine(logs, $"{profile}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");

        var comspec = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        var psi = new ProcessStartInfo
        {
            FileName = comspec,
            Arguments = $"/d /c \"{script}\"",
            WorkingDirectory = inst.Workspace.Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var buf = new StringBuilder();
        var opened = false;
        void OnLine(string? line)
        {
            if (line is null) return;
            try { File.AppendAllText(logPath, line + Environment.NewLine); }
            catch { /* disk */ }
            buf.AppendLine(line);
            if (opened) return;
            var url = LaunchPolicy.ParseWebUrl(buf.ToString());
            if (url is null) return;
            opened = true;
            Reveal.Url(url);
        }
        proc.OutputDataReceived += (_, e) => OnLine(e.Data);
        proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
        proc.Exited += (_, _) =>
        {
            try
            {
                File.AppendAllText(logPath, $"[PAD] dsh exited {proc.ExitCode}{Environment.NewLine}");
            }
            catch { /* disk */ }
            if (!Proc.Alive(proc.Id)) _l.UntrackRun(inst.Id, profile);
        };
        if (!proc.Start())
            throw new PadError("PA025", "web profile did not start", $"instance `{inst.Id}`",
                script, ["看实例日志目录"]);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        _l.TrackRun(new RunEntry
        {
            Instance = inst.Id,
            Profile = profile,
            Version = inst.Dsh.Version,
            Pid = proc.Id,
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
        });
        inst.LastProfile = profile;
        _l.SaveInstance(inst);
        return new LaunchResult(proc.Id, script, null);
    }

    static Process? StartWindowsTerminal(string script, string title, string cwd, bool reuse)
    {
        if (Proc.Which("wt") is null) return null;
        // `-w 0 nt` reuses the existing window as a new tab, so launching three
        // instances gives three tabs instead of three stray windows.
        // `/q` turns echo off. launch-*.cmd is UTF-8 with BOM so Chinese paths
        // parse; that BOM also prefixes `@echo off`, and cmd.exe then leaves
        // echo on and prints every `set`, including the runtime API key.
        var args = reuse
            ? $"-w 0 nt --title \"{title}\" -d \"{cwd}\" cmd /d /q /k call \"{script}\""
            : $"nt --title \"{title}\" -d \"{cwd}\" cmd /d /q /k call \"{script}\"";
        try { return Proc.Start("wt", args, cwd); }
        catch { return null; }
    }

    static Process? StartConhost(string script, string title, string cwd)
    {
        var comspec = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
        var psi = new ProcessStartInfo
        {
            FileName = comspec,
            Arguments = $"/d /c start \"{title}\" cmd /d /q /k call \"{script}\"",
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        return Process.Start(psi);
    }

    static Process? StartCustom(string template, string script, string title, string cwd)
    {
        // {script} {title} {cwd} are the only placeholders; anything else is passed through.
        var line = template
            .Replace("{script}", script)
            .Replace("{title}", title)
            .Replace("{cwd}", cwd);
        var (exe, args) = SplitCommand(line);
        try { return Proc.Start(exe, args, cwd, newConsole: true); }
        catch { return null; }
    }

    static (string Exe, string Args) SplitCommand(string line)
    {
        line = line.Trim();
        if (line.StartsWith('"'))
        {
            var end = line.IndexOf('"', 1);
            if (end > 0) return (line[1..end], line[(end + 1)..].Trim());
        }
        var space = line.IndexOf(' ');
        return space < 0 ? (line, "") : (line[..space], line[(space + 1)..]);
    }

    public void Stop(string instanceId, string profile)
    {
        var inst = _l.Instance(instanceId);
        var pid = Proc.FindDshPid(profile, inst is null ? null : _l.DshBin(inst.Dsh.Version));
        if (pid <= 0)
        {
            var entry = _l.Runtime().Entries.FirstOrDefault(e => e.Instance == instanceId && e.Profile == profile);
            if (entry is not null && Proc.Alive(entry.Pid)) pid = entry.Pid;
        }
        if (pid > 0)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
        }
        _l.UntrackRun(instanceId, profile);
    }
}
