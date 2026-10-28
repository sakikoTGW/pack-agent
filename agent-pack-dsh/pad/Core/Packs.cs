using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>How many units of each kind one pack carries.</summary>
public sealed class PackComponents
{
    [JsonPropertyName("skill")] public int Skill { get; set; }
    [JsonPropertyName("mcp")] public int Mcp { get; set; }
    [JsonPropertyName("plugin")] public int Plugin { get; set; }
    [JsonPropertyName("rule")] public int Rule { get; set; }
    [JsonPropertyName("command")] public int Command { get; set; }
    [JsonPropertyName("hook")] public int Hook { get; set; }

    [JsonIgnore] public int Total => Skill + Mcp + Plugin + Rule + Command + Hook;
}

/// <summary>
/// One pack on this machine that PAD can build an instance from — the row of
/// <c>packagent dsh launcher packs --json</c>. Discovery lives in TypeScript; this type
/// only carries the facts PAD shows and the create flow needs.
/// </summary>
public sealed class PackCard
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    /// <summary>pack.json | pack.zip | pinst.zip | projected.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    /// <summary>Pinned DSH release the pack declares. Null/empty = the pack has no dsh.version.</summary>
    [JsonPropertyName("kernel")] public string? Kernel { get; set; }
    [JsonPropertyName("profile")] public string? Profile { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    /// <summary>Where the row was found (library | drop | scan).</summary>
    [JsonPropertyName("source")] public string? Source { get; set; }
    /// <summary>Instance ids that already have this pack projected.</summary>
    [JsonPropertyName("installed")] public List<string> Installed { get; set; } = [];
    /// <summary>Null = the pack index cannot be read (e.g. a pinst zip, or a corrupt pack).</summary>
    [JsonPropertyName("components")] public PackComponents? Components { get; set; }

    /// <summary>「内核 0.1.0-rc.7」/「内核缺失」.</summary>
    [JsonIgnore]
    public string KernelText => string.IsNullOrWhiteSpace(Kernel) ? "内核缺失" : $"内核 {Kernel.Trim()}";

    /// <summary>「插件 2 · 技能 3 · MCP 1」/「组件未知」——整合包里装了什么，说人话。</summary>
    [JsonIgnore]
    public string ComponentsText
    {
        get
        {
            if (Components is null) return "组件未知";
            var kinds = new List<string>(6);
            if (Components.Plugin > 0) kinds.Add($"插件 {Components.Plugin}");
            if (Components.Skill > 0) kinds.Add($"技能 {Components.Skill}");
            if (Components.Mcp > 0) kinds.Add($"MCP {Components.Mcp}");
            if (Components.Rule > 0) kinds.Add($"规则 {Components.Rule}");
            if (Components.Command > 0) kinds.Add($"命令 {Components.Command}");
            if (Components.Hook > 0) kinds.Add($"钩子 {Components.Hook}");
            return kinds.Count == 0 ? "没有组件" : string.Join(" · ", kinds);
        }
    }

    /// <summary>一行只留「内核版本 · 组件」；来源/路径/profile 放 tooltip，不摊在列表里。</summary>
    [JsonIgnore]
    public string SummaryText
    {
        get
        {
            var kernel = string.IsNullOrWhiteSpace(Kernel) ? "缺内核" : Kernel.Trim();
            var components = Components is null ? "" : ComponentsText;
            return string.IsNullOrWhiteSpace(components) ? kernel : $"{kernel} · {components}";
        }
    }

    public override string ToString() => $"{Id} {Version} ({Path})";
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
        // Bun is not a hard prerequisite check anymore: <see cref="Bun.EnsureAsync"/> finds
        // it in the usual install dirs or installs it once from npm before the first call.
        if (RepoRoot() is null)
            return new PackFailure("找不到 pack-agent 源码", "把仓库路径写进 PACK_AGENT_REPO，或在启动器根目录放 .pack-agent-repo。");
        return null;
    }

    /// <summary>This launcher's own copies of pack files (<c>&lt;root&gt;/library/packs</c>),
    /// so a pack picked out of Downloads keeps living with the instance list.</summary>
    public string LibraryDir => Path.Combine(_launcher.Root, "library", "packs");

    /// <summary>
    /// Every pack this machine can turn into an instance. <paramref name="roots"/> are
    /// extra scan roots joined by <c>;</c> (empty = the TypeScript default set, which
    /// already includes the launcher root); <paramref name="deep"/> is one extra root to
    /// walk deeply. Discovery, manifest parsing and the empty-state copy stay in
    /// TypeScript — PAD only parses the last JSON the CLI prints.
    /// </summary>
    public async Task<List<PackCard>> Catalog(string roots, string? deep,
        IProgress<string>? log, CancellationToken ct)
    {
        var args = new StringBuilder("packs");
        var scanRoots = (roots ?? "").Trim();
        if (scanRoots.Length > 0)
            args.Append(" --roots \"").Append(scanRoots.Replace("\"", "")).Append('"');
        var deepDir = (deep ?? "").Trim();
        if (deepDir.Length > 0)
            args.Append(" --deep \"").Append(deepDir.Replace("\"", "")).Append('"');

        // The catalog JSON easily runs past Proc.Run's 40-line tail (a machine can hold
        // dozens of packs), and only the head of the document carries the envelope, so this
        // call captures the whole stdout instead of a log tail.
        var text = await CliJson(args.ToString(), ct);
        var cards = new List<PackCard>();
        using var doc = FirstJson(text);
        if (doc is null) return cards;

        var root = doc.RootElement;
        // Broken packs do not fail the scan (ok stays true); surface them so a vanished
        // row is never silent. The UI/CLI decides whether to show the lines.
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("errors", out var errors)
            && errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var err in errors.EnumerateArray())
                log?.Report("packs: " + ErrorLine(err));
        }

        JsonElement rows;
        if (root.ValueKind == JsonValueKind.Array) rows = root;
        else if (root.ValueKind == JsonValueKind.Object
                 && root.TryGetProperty("packs", out var packs)
                 && packs.ValueKind == JsonValueKind.Array) rows = packs;
        else return cards;

        foreach (var el in rows.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            try { cards.Add(ReadCard(el)); }
            catch (JsonException ex)
            {
                log?.Report($"packs: 跳过一条读不懂的记录 — {ex.Message}");
            }
        }
        // The CLI reads directories in filesystem order; a stable list keeps the picker still.
        cards.Sort((a, b) =>
        {
            var byTitle = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            return byTitle != 0 ? byTitle : string.CompareOrdinal(a.Path, b.Path);
        });
        return cards;
    }

    /// <summary>One <c>packs[]</c> row. Read field by field so a missing/odd value can
    /// never take the whole catalog down with it.</summary>
    static PackCard ReadCard(JsonElement el) => new()
    {
        Id = Str(el, "id"),
        Title = Str(el, "title"),
        Version = Str(el, "version"),
        Path = Str(el, "path"),
        Kind = Str(el, "kind"),
        Kernel = NullStr(el, "kernel"),
        Profile = NullStr(el, "profile"),
        Description = NullStr(el, "description"),
        Source = NullStr(el, "source"),
        Installed = Strings(el, "installed"),
        Components = ComponentsOf(el),
    };

    static PackComponents? ComponentsOf(JsonElement el)
    {
        if (!el.TryGetProperty("components", out var c) || c.ValueKind != JsonValueKind.Object)
            return null;
        try { return c.Deserialize<PackComponents>(); }
        catch (JsonException) { return null; }
    }

    static string? NullStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>「path: message」for one <c>errors[]</c> entry; plain strings pass through.</summary>
    static string ErrorLine(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
        if (el.ValueKind != JsonValueKind.Object) return el.GetRawText();
        var path = NullStr(el, "path");
        var message = NullStr(el, "message");
        return path is null ? message ?? el.GetRawText()
            : message is null ? path
            : $"{path}: {message}";
    }

    static List<string> Strings(JsonElement el, string name)
    {
        var list = new List<string>();
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                list.Add(s);
        return list;
    }

    /// <summary>
    /// Copy one pack into <see cref="LibraryDir"/> and return the file that landed there.
    /// Collecting twice is idempotent; the same name with different bytes gets <c>-1</c>,
    /// <c>-2</c>, … so an older revision is never silently overwritten.
    /// </summary>
    public string CollectToLibrary(string packPath)
    {
        var src = Path.GetFullPath((packPath ?? "").Trim().Trim('"'));
        if (!File.Exists(src))
            throw new PadError("PA012", "no such pack file", src, null, ["pad cli pack list"]);
        Directory.CreateDirectory(LibraryDir);

        var name = Path.GetFileName(src);
        var dest = Path.Combine(LibraryDir, name);
        if (!File.Exists(dest))
        {
            File.Copy(src, dest);
            return dest;
        }
        if (SameContent(src, dest)) return dest;

        // Pack names carry compound extensions (<c>.pack.json</c>, <c>.pinst.zip</c>), so
        // make it <c>demo-1.pack.json</c> rather than <c>demo.pack-1.json</c>.
        var (stem, ext) = SplitPackName(name);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(LibraryDir, $"{stem}-{i}{ext}");
            if (!File.Exists(candidate))
            {
                File.Copy(src, candidate);
                return candidate;
            }
            if (SameContent(src, candidate)) return candidate;
        }
        throw new PadError("PA012", "too many different copies of this pack in the library",
            LibraryDir, name, ["清掉 library/packs 里不用的旧版本"]);
    }

    static (string Stem, string Ext) SplitPackName(string name)
    {
        foreach (var compound in PackExtensions)
            if (name.EndsWith(compound, StringComparison.OrdinalIgnoreCase))
                return (name[..^compound.Length], name[^compound.Length..]);
        return (Path.GetFileNameWithoutExtension(name), Path.GetExtension(name));
    }

    /// <summary>Compound extensions first: <c>demo.pack.json</c> must keep its <c>.pack</c>.</summary>
    static readonly string[] PackExtensions = [".pinst.zip", ".pack.json", ".pack.zip"];

    static bool SameContent(string a, string b)
    {
        try
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The node entry point every projection call shares: repo root, launcher
    /// root and the JSON flag, in the exact shape <c>packagent dsh launcher</c> expects.</summary>
    (string Repo, string Line) Command(string args)
    {
        var repo = RepoRoot()
            ?? throw new PadError("PA027", "the pack-agent repo was not found",
                "PACK_AGENT_REPO", AppContext.BaseDirectory,
                ["设置 PACK_AGENT_REPO", "或在启动器根目录放 .pack-agent-repo"]);
        var entry = Path.Combine(repo, "bin", "packagent.js");
        return (repo, $"\"{entry}\" dsh launcher --root \"{_launcher.Root}\" --json {args}");
    }

    async Task<string> Cli(string args, IProgress<string>? log, CancellationToken ct)
    {
        await Bun.EnsureAsync(log, ct);
        var (repo, line) = Command(args);
        var res = await Proc.Run("node", line, repo, log, ct);
        if (res.ExitCode != 0)
            throw new PadError("PA028", "the projection command failed",
                $"packagent dsh launcher {args}", res.Tail, ["看上面的输出"]);
        return res.Tail;
    }

    /// <summary>
    /// Run the launcher CLI and keep <em>all</em> of stdout. <see cref="Proc.Run"/> keeps
    /// only the last 40 lines — fine as a log tail, fatal for a catalog JSON whose
    /// envelope sits at the head (a machine with several packs runs to hundreds of lines).
    /// Used by read-only calls only; anything long-running stays on <see cref="Cli"/> so
    /// its output keeps streaming to the job log.
    /// </summary>
    async Task<string> CliJson(string args, CancellationToken ct)
    {
        await Bun.EnsureAsync(null, ct);
        var (repo, line) = Command(args);
        var node = Proc.Which("node")
            ?? throw new PadError("PA011", "`node` was not found on PATH", "PATH", "node",
                ["安装 node 并确认它在 PATH 里"]);
        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = line,
            WorkingDirectory = repo,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        Proc.PrependToolPath(psi);
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        string text, errText;
        try
        {
            await proc.WaitForExitAsync(ct);
            text = await stdout;
            errText = await stderr;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        if (proc.ExitCode != 0)
            throw new PadError("PA028", "the catalog command failed",
                $"packagent dsh launcher {args}",
                string.IsNullOrWhiteSpace(errText) ? text.Trim() : errText.Trim(),
                ["看上面的输出"]);
        return text;
    }

    /// <summary>Run a <c>packagent dsh</c> subcommand that targets a workspace via --cwd,
    /// i.e. the named allow-set commands that are not part of the <c>launcher</c> group.</summary>
    async Task<string> CliDsh(string args, string cwd, IProgress<string>? log, CancellationToken ct)
    {
        await Bun.EnsureAsync(log, ct);
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

    /// <summary>
    /// The first complete JSON value in <paramref name="text"/>. Scanning from the end
    /// (<see cref="LastJson"/>) is wrong for a catalog whose last rows end in nested
    /// objects; the outer envelope is the first `{` that parses consuming the whole rest,
    /// so scan forward past the human lines the CLI may print first.
    /// </summary>
    static JsonDocument? FirstJson(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '[' && text[i] != '{') continue;
            try { return JsonDocument.Parse(text[i..]); }
            catch { /* plain human line, or an inner fragment — keep scanning right */ }
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
