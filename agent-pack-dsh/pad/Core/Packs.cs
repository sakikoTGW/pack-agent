using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// One projected modpack in an instance workspace — the counterpart of a jar in
/// <c>mods/</c>. <see cref="Enabled"/> is the allow-list, not the presence of files:
/// denying a pack leaves it on disk and only hides it from the session.
/// </summary>
public sealed class PackRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Dir { get; set; } = "";
    public bool Enabled { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Name) ? Id : Name;
    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "无版本号" : $"v{Version}";
    public string StateText => Enabled ? "已启用" : "已停用";
    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "包里没写描述。" : Description;
}

/// <summary>
/// The projection axis stays in TypeScript: the pack compiler, the unit registry and
/// the Rust <c>pack-index</c> catalog are one asset shared with `packagent dsh`, and a
/// second C# implementation would be a second answer to "which packs are enabled".
/// PAD drives it as a subprocess and treats the JSON as the truth.
/// </summary>
public sealed class Packs
{
    readonly Launcher _launcher;

    public Packs(Launcher launcher) => _launcher = launcher;

    /// <summary>
    /// Where the TypeScript side lives. In a portable kit the path sits in
    /// <c>.pack-agent-repo</c> next to the exe; a dev run can point at a working copy
    /// with PACK_AGENT_REPO.
    /// </summary>
    public string? RepoRoot()
    {
        var env = Environment.GetEnvironmentVariable("PACK_AGENT_REPO");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(Path.Combine(env, "bin", "packagent.js")))
            return Path.GetFullPath(env);

        foreach (var dir in Probe())
        {
            var marker = Path.Combine(dir, ".pack-agent-repo");
            if (File.Exists(marker))
            {
                var target = File.ReadAllText(marker).Trim();
                if (File.Exists(Path.Combine(target, "bin", "packagent.js"))) return target;
            }
            if (File.Exists(Path.Combine(dir, "bin", "packagent.js"))) return dir;
        }
        return null;
    }

    IEnumerable<string> Probe()
    {
        yield return AppContext.BaseDirectory;
        yield return _launcher.Root;
        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && walk is not null; i++, walk = walk.Parent)
            yield return walk.FullName;
    }

    public sealed record PackFailure(string Reason, string Detail);

    /// <summary>Why projection is unavailable, or null when it is ready to use.</summary>
    public PackFailure? Unavailable()
    {
        if (Proc.Which("bun") is null)
            return new PackFailure("装包链需要 bun", "投影编译器与 pack-index 是 TypeScript/Rust 的，PAD 调它们跑。");
        if (RepoRoot() is null)
            return new PackFailure("找不到 pack-agent 源码", "把仓库路径写进 PACK_AGENT_REPO，或在启动器根目录放 .pack-agent-repo。");
        return null;
    }

    async Task<string> Cli(string args, IProgress<string>? log, CancellationToken ct)
    {
        var repo = RepoRoot()
            ?? throw new PadError("PA027", "the pack-agent repo was not found",
                "PACK_AGENT_REPO", AppContext.BaseDirectory,
                ["设置 PACK_AGENT_REPO", "或在启动器根目录放 .pack-agent-repo"]);

        var entry = Path.Combine(repo, "bin", "packagent.js");
        var line = $"\"{entry}\" dsh launcher --root \"{_launcher.Root}\" --json {args}";
        var res = await Proc.Run("node", line, repo, log, ct);
        if (res.ExitCode != 0)
            throw new PadError("PA028", "the projection command failed",
                $"packagent dsh launcher {args}", res.Tail, ["看上面的输出"]);
        return res.Tail;
    }

    /// <summary>Run a <c>packagent dsh</c> subcommand that targets a workspace via --cwd,
    /// i.e. the named allow-set commands that are not part of the <c>launcher</c> group.</summary>
    async Task<string> CliDsh(string args, string cwd, IProgress<string>? log, CancellationToken ct)
    {
        var repo = RepoRoot()
            ?? throw new PadError("PA027", "the pack-agent repo was not found",
                "PACK_AGENT_REPO", AppContext.BaseDirectory,
                ["设置 PACK_AGENT_REPO", "或在启动器根目录放 .pack-agent-repo"]);
        var entry = Path.Combine(repo, "bin", "packagent.js");
        var line = $"\"{entry}\" dsh {args}";
        var res = await Proc.Run("node", line, cwd, log, ct);
        if (res.ExitCode != 0)
            throw new PadError("PA028", "the projection command failed",
                $"packagent dsh {args}", res.Tail, ["看上面的输出"]);
        return res.Tail;
    }

    /// <summary>
    /// The CLI prints human lines before its JSON, so take the last balanced JSON value
    /// rather than assuming the whole tail parses.
    /// </summary>
    static JsonDocument? LastJson(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] != '[' && text[i] != '{') continue;
            var slice = text[i..].Trim();
            try { return JsonDocument.Parse(slice); }
            catch { /* keep scanning left for an earlier opening brace */ }
        }
        return null;
    }

    public async Task<List<PackRow>> List(Instance inst, CancellationToken ct)
    {
        var rows = new List<PackRow>();
        var text = await Cli($"pack list {inst.Id}", null, ct);
        using var doc = LastJson(text);
        if (doc is null) return rows;

        var root = doc.RootElement;
        // `listOrEmpty` wraps an empty result in an envelope carrying the empty-state copy.
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (!root.TryGetProperty("items", out var items)) return rows;
            root = items;
        }
        if (root.ValueKind != JsonValueKind.Array) return rows;

        foreach (var el in root.EnumerateArray())
        {
            rows.Add(new PackRow
            {
                Id = Str(el, "id"),
                Name = Str(el, "name"),
                Version = Str(el, "version"),
                Description = Str(el, "description"),
                Dir = Str(el, "dir"),
                Enabled = el.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True,
            });
        }
        rows.Sort((a, b) => string.CompareOrdinal(a.Title, b.Title));
        return rows;
    }

    static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>
    /// Import a <c>.pack.json</c> / <c>.pack.zip</c> / <c>.pinst.zip</c>. Import is the
    /// whole chain — sniff, pin the release, build the home, project, allow — so PAD does
    /// not re-implement any step of it.
    /// </summary>
    public async Task<string> Import(string packPath, IProgress<string>? log, CancellationToken ct)
    {
        var job = _launcher.StartJob("import", null);
        var token = _launcher.TrackCancel(job.Id, ct);
        log = _launcher.TeeJob(job.Id, log);
        try
        {
            var r = await Cli($"import \"{Path.GetFullPath(packPath)}\"", log, token);
            _launcher.FinishJob(job.Id, "done");
            return r;
        }
        catch { _launcher.FinishJob(job.Id, "failed"); throw; }
    }

    /// <summary>Project a pack into an existing instance and switch it on.</summary>
    public async Task<string> Project(Instance inst, string packPath, IProgress<string>? log, CancellationToken ct)
    {
        var job = _launcher.StartJob("project", inst.Id);
        var token = _launcher.TrackCancel(job.Id, ct);
        log = _launcher.TeeJob(job.Id, log);
        try
        {
            var r = await Cli($"pack project {inst.Id} \"{Path.GetFullPath(packPath)}\"", log, token);
            _launcher.FinishJob(job.Id, "done");
            return r;
        }
        catch { _launcher.FinishJob(job.Id, "failed"); throw; }
    }

    public Task Allow(Instance inst, string packId, IProgress<string>? log, CancellationToken ct) =>
        Cli($"pack allow {inst.Id} {packId}", log, ct);

    public Task Deny(Instance inst, string packId, IProgress<string>? log, CancellationToken ct) =>
        Cli($"pack deny {inst.Id} {packId}", log, ct);

    /// <summary>Save the instance's live whitelist as a named allow-set preset.</summary>
    public Task SetSave(Instance inst, string name, IProgress<string>? log, CancellationToken ct) =>
        CliDsh($"set-save {name} --cwd \"{inst.Workspace.Path}\"", inst.Workspace.Path, log, ct);

    /// <summary>Load a named allow-set preset over the instance's live whitelist.</summary>
    public Task SetLoad(Instance inst, string name, IProgress<string>? log, CancellationToken ct) =>
        CliDsh($"set-load {name} --cwd \"{inst.Workspace.Path}\"", inst.Workspace.Path, log, ct);

    /// <summary>Names of the named allow-set presets for an instance's workspace.</summary>
    public async Task<List<string>> SetList(Instance inst, CancellationToken ct)
    {
        var names = new List<string>();
        var text = await CliDsh($"set-list --cwd \"{inst.Workspace.Path}\"", inst.Workspace.Path, null, ct);
        using var doc = LastJson(text);
        if (doc is null) return names;
        if (doc.RootElement.TryGetProperty("sets", out var sets) && sets.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in sets.EnumerateArray())
                if (Str(el, "name") is { Length: > 0 } n) names.Add(n);
        }
        return names;
    }

    /// <summary>Move zips from beside the exe into the launcher root. PCL sidecar.</summary>
    public static int CollectSidecar(string exeDir, string launcherRoot)
    {
        var n = 0;
        foreach (var src in LaunchPolicy.SidecarZips(exeDir, launcherRoot))
        {
            Directory.CreateDirectory(launcherRoot);
            var dest = Path.Combine(launcherRoot, Path.GetFileName(src));
            File.Move(src, dest);
            n++;
        }
        return n;
    }

    /// <summary>Pick up packs dropped next to the exe or the launcher root.</summary>
    public Task<string> ScanDrop(IProgress<string>? log, CancellationToken ct) =>
        Cli("scan-drop", log, ct);

    public Task<string> Crash(string instanceId, IProgress<string>? log, CancellationToken ct) =>
        Cli($"crash {instanceId}", log, ct);
}
