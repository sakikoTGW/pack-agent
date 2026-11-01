using System.IO;
using System.Text;

namespace Pad.Core;

/// <summary>Result of seeding a project's DSH workspace registry (and binding its default preset).</summary>
public sealed record ProjectSeed(
    string Home, int Kept, int Added, IReadOnlyList<string> Paths, WorkspaceRegistry.Doc Doc, string? BoundPreset);

/// <summary>
/// The project's runtime path: ensure the instance (one DSH_HOME) exists, seed that home's
/// <c>storages/workspace.json</c>, then start (or focus) its window. This is the only place a
/// project touches DSH data, and it only ever writes the registry the window reads.
/// </summary>
public static class ProjectRunner
{
    public static Instance EnsureInstance(Launcher launcher, Projects store, Project p)
    {
        var instId = p.Instance ?? p.Id;
        var inst = launcher.Instance(instId);
        if (inst is not null) return inst;

        var version = p.Dsh.Version ?? launcher.Settings().Launch.DefaultRelease;
        var profile = p.Dsh.Profile ?? launcher.Settings().Launch.DefaultProfile;
        inst = launcher.CreateInstance(p.Name, version, profile);
        p.Instance = inst.Id;
        p.Dsh.Version ??= version;
        p.Dsh.Profile ??= profile;
        store.Save(p);
        return inst;
    }

    /// <summary>
    /// Seed the project's home: workspace registry first, then the bound agent preset (PA125 when
    /// the preset cannot be read or does not exist in that release). Presets are written into the
    /// profile's own patch layer, so the project's default follows the project, not the machine.
    /// </summary>
    public static async Task<ProjectSeed> SeedAsync(
        Launcher launcher, Projects store, Project p, string? homeOverride,
        IProgress<string>? log, CancellationToken ct)
    {
        Instance? inst = null;
        string home;
        if (string.IsNullOrWhiteSpace(homeOverride))
        {
            inst = EnsureInstance(launcher, store, p);
            home = inst.Home;
        }
        else
        {
            home = Projects.NormPath(homeOverride);
        }
        Directory.CreateDirectory(home);
        var merged = WorkspaceRegistry.Merge(WorkspaceRegistry.Read(home), p.Workspaces);
        WorkspaceRegistry.Write(home, merged.Doc);

        string? bound = null;
        if (p.Agents.Count > 0)
        {
            var settings = launcher.Settings();
            var profile = p.Dsh.Profile ?? inst?.LastProfile ?? settings.Launch.DefaultProfile;
            var version = p.Dsh.Version ?? inst?.Dsh.Version ?? settings.Launch.DefaultRelease;
            var workspace = inst?.Workspace.Path ?? home;
            var (probe, error) = await AgentPresets.ProbeAsync(launcher, home, version, workspace, profile, ct);
            if (probe is null)
                throw new PadError("PA125", "could not bind agent presets for this project", home,
                    error ?? "could not read the release's presets",
                    ["确认这个发行号已安装、node 可用", "pad cli project presets " + p.Id]);
            var missing = p.Agents
                .Where(a => probe.Presets.All(x => !string.Equals(x.Id, a, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (missing.Count > 0)
                throw new PadError("PA125", "could not bind agent presets for this project", home,
                    $"no such agent preset: {string.Join(", ", missing)} " +
                    $"(release ships: {string.Join(", ", probe.Presets.Select(x => x.Id))})",
                    ["pad cli project presets " + p.Id + "  看可选项"]);
            var wanted = probe.Presets.First(x => string.Equals(x.Id, p.Agents[0], StringComparison.OrdinalIgnoreCase));
            // P1: agents[0] is the default; the rest stay declared for the P2 preset picker.
            AgentPresets.Apply(AgentPresets.PatchPath(home, profile), wanted.Id);
            p.Dsh.Profile ??= profile;
            p.Agents = [.. p.Agents.Select(a => probe.Presets.First(x => string.Equals(x.Id, a, StringComparison.OrdinalIgnoreCase)).Id)];
            store.Save(p);
            bound = wanted.Id;
            log?.Report($"agent preset {bound} → {profile}");
        }

        return new ProjectSeed(home, merged.Kept, merged.Added, merged.Paths, merged.Doc, bound);
    }

    /// <summary>
    /// <c>pad cli project run</c> / the window's 启动 button. Already running = focus that window
    /// instead of starting a second process on the same home (Runner would refuse with PA121).
    /// </summary>
    public static async Task<object> RunAsync(
        Launcher launcher, Projects store, Project p, string? profileOverride, CancellationToken ct)
    {
        if (string.Equals(p.Window.Kind, "shell", StringComparison.OrdinalIgnoreCase))
            throw new PadError("PA126", "third-party shell window is not implemented yet",
                $"projects/{p.Id}/project.json",
                "window.kind = shell needs the third-party DSH Desktop with --user-data-dir",
                ["先把项目窗口设成 web：window.kind = \"web\"", "P2 再接 shell 窗口"]);

        var inst = EnsureInstance(launcher, store, p);
        Cli.Print($"[project-run] {DateTimeOffset.Now:HH:mm:ss} instance={inst.Id}");
        var ledger = launcher.Runtime().Entries;
        var phase = RunWatch.Read(launcher, inst, ledger, RunWatch.DeepHomes());
        Cli.Print($"[project-run] {DateTimeOffset.Now:HH:mm:ss} phase={phase.Phase}");
        var settings = launcher.Settings();

        if (phase.Live)
        {
            // Never rewrite the registry behind a live DSH process: the running process owns
            // its in-memory state and would overwrite (or fork) what we just wrote.
            var facts = RunFacts.Collect(inst, phase, null, []);
            var focused = ShellActivate.Try(facts.Pids);
            return new
            {
                status = "already-running",
                instance = inst.Id,
                phase.Say,
                pid = facts.Pid,
                port = facts.Port,
                address = facts.AddressText,
                uptime = facts.UptimeText,
                focused,
            };
        }

        var seed = await SeedAsync(launcher, store, p, null, new Progress<string>(Cli.Print), ct);
        var profile = profileOverride
                      ?? p.Dsh.Profile
                      ?? inst.LastProfile
                      ?? launcher.Profiles(inst).FirstOrDefault()?.Name
                      ?? settings.Launch.DefaultProfile;
        var meta = launcher.Profiles(inst).FirstOrDefault(x => x.Name == profile);
        var isWeb = LaunchPolicy.WaitForWebReady(meta?.IsWeb == true);
        Cli.Print($"[project-run] {DateTimeOffset.Now:HH:mm:ss} launch profile={profile} web={isWeb}");
        await launcher.EnsureRuntimeNode(new Progress<string>(Cli.Print), ct, inst.Dsh.Version);
        // Web starts detached (own console handles + output straight into the instance log), then
        // this process waits for the readiness line so the URL is real and the caller can exit.
        var result = new Runner(launcher).Launch(inst, profile, settings, detachWebOutput: isWeb, healScripts: false);
        Cli.Print($"[project-run] {DateTimeOffset.Now:HH:mm:ss} launched pid={result.Pid}");
        var url = result.WebUrl;
        if (isWeb)
        {
            url = await WaitForWebLog(result.LogPath, ct);
            Cli.Print($"[project-run] {DateTimeOffset.Now:HH:mm:ss} url={url ?? "(none)"}");
            if (url is not null) Reveal.Url(url);
        }
        p.LastRun = DateTimeOffset.UtcNow.ToString("o");
        store.Save(p);
        return new
        {
            status = "started",
            instance = inst.Id,
            profile,
            home = seed.Home,
            workspaces = seed.Doc.Tables.Workspaces.Count,
            preset = seed.BoundPreset,
            pid = result.Pid,
            url,
            script = result.Script,
        };
    }

    /// <summary>Poll the exact log file this launch opened for DSH's readiness line
    /// (<c>dsh web: http…</c>). ReadWrite sharing is required: the detached cmd still holds the
    /// file while the server writes it.</summary>
    static async Task<string?> WaitForWebLog(string? logPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logPath)) return null;
        var deadline = DateTime.UtcNow.AddSeconds(150);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(logPath))
                {
                    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs, Encoding.UTF8);
                    var url = LaunchPolicy.ParseWebUrl(reader.ReadToEnd());
                    if (url is not null) return url;
                }
            }
            catch (IOException) { /* still being written; retry */ }
            catch (UnauthorizedAccessException) { /* retry */ }
            await Task.Delay(500, ct);
        }
        return null;
    }
}
