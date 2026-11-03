using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    /// <summary>预检用的 npm 元数据源；单测注入假实现，CLI/UI 用共享真源。</summary>
    readonly INpmMetaSource _meta;

    public Packs(Launcher launcher) : this(launcher, null) { }

    /// <summary>测试/嵌入用：替换 npm 元数据源（单测绝不打网络）。</summary>
    public Packs(Launcher launcher, INpmMetaSource? meta)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
        _meta = meta ?? NpmMetaSource.Shared;
    }

    /// <summary>
    /// 测试注入：替换安装里的「应用」步骤（生产为 null = 真 <see cref="ApplyPackToInstance(Instance, string, string, IProgress{string}?, CancellationToken)"/>）。
    /// 单测用它验证 DryRun 不落盘 / AcceptRisk→Exempted / SkipMissing→Skipped，不跑 dsh/pnpm。
    /// </summary>
    public Func<Instance, string, IReadOnlyCollection<string>?, IProgress<string>?, CancellationToken,
        Task<PackApplyResult>>? ApplyHook { get; set; }

    /// <summary>测试注入：替换批量 allow-version（生产为 null = <see cref="Launcher.AllowVersions"/>）。</summary>
    public Func<Instance, string, IReadOnlyList<(string PackageVersion, string DshVersion)>,
        IProgress<string>?, CancellationToken, Task<List<string>>>? AllowVersionsHook { get; set; }

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
        // 便携包没有仓库也能「看 / 启停」已投影的整合包（原生读投影目录）；
        // 投影/导入才需要仓库，由对应方法自己抛出明确的错误。
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

    /// <summary>
    /// 便携包自带的 pack 运行时：exe 旁边 <c>pack-runtime/</c>（bun.exe + packagent.bundle.js + pack-index.exe）。
    /// 有它就不需要仓库、不需要系统装 bun/cargo。
    /// </summary>
    /// <summary>
    /// 单文件 exe 的 <see cref="AppContext.BaseDirectory"/> 指向自解压临时目录，
    /// 不是 exe 所在目录；找旁边资源要用 <see cref="Environment.ProcessPath"/>。
    /// </summary>
    static string ExeDir()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) return dir;
        }
        return AppContext.BaseDirectory;
    }

    static bool LooksLikeRuntime(string? dir) =>
        !string.IsNullOrWhiteSpace(dir)
        && File.Exists(Path.Combine(dir, "packagent.bundle.js"))
        && File.Exists(Path.Combine(dir, "bun.exe"))
        && File.Exists(Path.Combine(dir, "pack-index.exe"));

    string? RuntimeDir()
    {
        try
        {
            var exeDir = ExeDir();
            // 安装器/发布目录旁边装好的运行时；其次是单文件 exe 自带的（首次用时解压到 .pack-runtime）。
            foreach (var name in new[] { "pack-runtime", ".pack-runtime" })
            {
                var dir = Path.Combine(exeDir, name);
                if (LooksLikeRuntime(dir)) return dir;
            }
            var extracted = SelfBundle.EnsureRuntime();
            if (LooksLikeRuntime(extracted)) return extracted;
        }
        catch { }
        return null;
    }

    Dictionary<string, string>? RuntimeEnv()
    {
        var dir = RuntimeDir();
        return dir is null
            ? null
            : new Dictionary<string, string> { ["PACK_INDEX_BIN"] = Path.Combine(dir, "pack-index.exe") };
    }

    /// <summary>仓库模式（node + 包源码）或便携运行时模式（bun + 打包后的 bundle）。</summary>
    (string Exe, string Line, string Cwd, Dictionary<string, string>? Env) Entry(string tail)
    {
        var runtime = RuntimeDir();
        if (runtime is not null)
            return (Path.Combine(runtime, "bun.exe"),
                $"\"{Path.Combine(runtime, "packagent.bundle.js")}\" {tail}",
                runtime, RuntimeEnv());
        var repo = RepoRoot()
            ?? throw new PadError("PA027", "the pack-agent repo was not found",
                "PACK_AGENT_REPO", AppContext.BaseDirectory,
                ["设置 PACK_AGENT_REPO", "或用带 pack-runtime 的便携包"]);
        return ("node", $"\"{Path.Combine(repo, "bin", "packagent.js")}\" {tail}", repo, null);
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
        if (RuntimeDir() is null) await Bun.EnsureAsync(log, ct);
        var (exe, line, cwd, env) = Entry($"dsh launcher --root \"{_launcher.Root}\" --json {args}");
        var res = await Proc.Run(exe, line, cwd, log, ct, env);
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
        if (RuntimeDir() is null) await Bun.EnsureAsync(null, ct);
        var (exe, line, cwd, entryEnv) = Entry($"dsh launcher --root \"{_launcher.Root}\" --json {args}");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = line,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (entryEnv is not null)
            foreach (var (k, v) in entryEnv) psi.Environment[k] = v;
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

    /// <summary>Capture all stdout of a <c>packagent dsh …</c> tail (used by JSON-heavy
    /// subcommands such as <c>packforge …</c> whose JSON envelope can be long).</summary>
    async Task<string> RunCapture(string tail, CancellationToken ct)
    {
        if (RuntimeDir() is null) await Bun.EnsureAsync(null, ct);
        var (exe, line, cwd, entryEnv) = Entry(tail);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = line,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (entryEnv is not null)
            foreach (var (k, v) in entryEnv) psi.Environment[k] = v;
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
            throw new PadError("PA028", "the packforge command failed",
                $"packagent dsh {tail}",
                string.IsNullOrWhiteSpace(errText) ? text.Trim() : errText.Trim(),
                ["看上面的输出"]);
        return text;
    }

    /// <summary>Run <c>packagent dsh packforge …</c> and return its stdout (JSON).</summary>
    public async Task<string> PackforgeJson(string tail, CancellationToken ct) =>
        await RunCapture($"dsh packforge {tail}", ct);

    public async Task<string> PackforgeInspect(string file, CancellationToken ct) =>
        await PackforgeJson($"inspect \"{file}\" --json", ct);

    public async Task<string> PackforgeMarket(CancellationToken ct) =>
        await PackforgeJson("market --json", ct);

    /// <summary>把实例 home 反向导出为 PackForge `.dspack`（profile 或 dshhome 自动判别）。</summary>
    public async Task<string> PackforgeExportInstance(string home, string outPath, string? profile, string? name, string? version, string? dshVersion, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("export-instance \"").Append(home).Append("\" --out \"").Append(outPath).Append("\" --json");
        if (!string.IsNullOrWhiteSpace(profile)) sb.Append(" --profile \"").Append(profile).Append('"');
        if (!string.IsNullOrWhiteSpace(name)) sb.Append(" --name \"").Append(name).Append('"');
        if (!string.IsNullOrWhiteSpace(version)) sb.Append(" --version \"").Append(version).Append('"');
        if (!string.IsNullOrWhiteSpace(dshVersion)) sb.Append(" --dsh-version \"").Append(dshVersion).Append('"');
        return await PackforgeJson(sb.ToString(), ct);
    }

    /// <summary>把实例的 workspace + sessions 导出成工作区快照 <c>.workspace.zip</c>（MODPACK-STATE.md）。</summary>
    public async Task<string> PackforgeExportWorkspace(string home, string outPath, string? workspace, int? maxSessions, string? name, string? version, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("export-workspace \"").Append(home).Append("\" --out \"").Append(outPath).Append("\" --json");
        if (!string.IsNullOrWhiteSpace(workspace)) sb.Append(" --workspace \"").Append(workspace).Append('"');
        if (maxSessions is > 0) sb.Append(" --max-sessions ").Append(maxSessions.Value);
        if (!string.IsNullOrWhiteSpace(name)) sb.Append(" --name \"").Append(name).Append('"');
        if (!string.IsNullOrWhiteSpace(version)) sb.Append(" --version \"").Append(version).Append('"');
        return await PackforgeJson(sb.ToString(), ct);
    }

    public async Task<string> PackforgeRegistry(string? id, CancellationToken ct) =>
        await PackforgeJson(string.IsNullOrWhiteSpace(id) ? "registry --json" : $"registry --json --id \"{id}\"", ct);

    /// <summary>compat 安装（PackForge 语义）：overrides→profile、home/→home、写 bundles、可选依赖安装。</summary>
    public async Task<string> PackforgeInstall(string file, string home, string? profile, bool apply, bool deps, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("install \"").Append(file).Append("\" --home \"").Append(home).Append("\" --json");
        if (!string.IsNullOrWhiteSpace(profile)) sb.Append(" --profile \"").Append(profile).Append('"');
        if (apply) sb.Append(" --apply");
        if (deps) sb.Append(" --deps");
        return await PackforgeJson(sb.ToString(), ct);
    }

    /// <summary>Run a <c>packagent dsh</c> subcommand that targets a workspace via --cwd,
    /// i.e. the named allow-set commands that are not part of the <c>launcher</c> group.</summary>
    async Task<string> CliDsh(string args, string cwd, IProgress<string>? log, CancellationToken ct)
    {
        if (RuntimeDir() is null) await Bun.EnsureAsync(log, ct);
        var (exe, line, _, env) = Entry($"dsh {args}");
        var res = await Proc.Run(exe, line, cwd, log, ct, env);
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
        List<PackRow> rows;
        try
        {
            rows = await ListViaCli(inst, ct);
        }
        catch (Exception) when (RepoRoot() is null && RuntimeDir() is null)
        {
            // 便携包没有 pack 运行时：直接读投影目录（目录 + catalog.json + 本地启用表）。
            rows = ListNative(inst);
        }
        WriteNativeState(inst, rows);
        rows.Sort((a, b) => string.CompareOrdinal(a.Title, b.Title));
        return rows;
    }

    async Task<List<PackRow>> ListViaCli(Instance inst, CancellationToken ct)
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
        return rows;
    }

    // ---- 便携模式：不依赖仓库的原生整合包读写 --------------------------

    string NativeStatePath(Instance inst) =>
        Path.Combine(inst.Workspace.Path, ".agent-pack", "pad-packs.json");

    HashSet<string> ReadNativeEnabled(Instance inst)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var file = NativeStatePath(inst);
            if (!File.Exists(file)) return set;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("enabled", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var el in arr.EnumerateArray())
                    if (el.GetString() is { Length: > 0 } s) set.Add(s);
        }
        catch { }
        return set;
    }

    void WriteNativeState(Instance inst, IEnumerable<PackRow> rows)
    {
        try
        {
            var file = NativeStatePath(inst);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var enabled = rows.Where(r => r.Enabled).Select(r => r.Id).ToArray();
            var json = JsonSerializer.Serialize(
                new { schema = "pack-agent.pad-packs/v1", enabled },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
        catch { }
    }

    /// <summary>读 <c>workspace/.agent-pack/modpacks/*</c>：catalog.json 或 preset.yml 里的名字/版本。</summary>
    public List<PackRow> ListNative(Instance inst)
    {
        var rows = new List<PackRow>();
        var root = Path.Combine(inst.Workspace.Path, ".agent-pack", "modpacks");
        if (!Directory.Exists(root)) return rows;
        var enabled = ReadNativeEnabled(inst);
        foreach (var dir in Directory.GetDirectories(root))
        {
            var id = Path.GetFileName(dir);
            var name = id;
            var version = "";
            var desc = "";
            try
            {
                var cat = Path.Combine(dir, "catalog.json");
                if (File.Exists(cat))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cat));
                    var r = doc.RootElement;
                    if (Str(r, "id") is { Length: > 0 } i) id = i;
                    if (Str(r, "name") is { Length: > 0 } n) name = n;
                    version = Str(r, "version");
                    desc = Str(r, "description");
                }
                else
                {
                    var preset = Path.Combine(dir, "preset.yml");
                    if (File.Exists(preset))
                        foreach (var line in File.ReadAllLines(preset))
                        {
                            var s = line.Trim();
                            if (s.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) name = s[5..].Trim();
                            else if (s.StartsWith("description:", StringComparison.OrdinalIgnoreCase)) desc = s[12..].Trim();
                        }
                }
            }
            catch { }
            rows.Add(new PackRow
            {
                Id = id, Name = name, Version = version, Description = desc, Dir = dir,
                Enabled = enabled.Contains(id),
            });
        }
        return rows;
    }

    void SetNativeEnabled(Instance inst, string id, bool on)
    {
        var rows = ListNative(inst);
        var hit = rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new PadError("PA012", "unknown pack id", id,
                rows.Count == 0 ? "这个实例下还没有投影的整合包" : "可用：" + string.Join(" / ", rows.Select(r => r.Id)),
                ["pad cli pack list " + inst.Id]);
        hit.Enabled = on;
        WriteNativeState(inst, rows);
    }

    /// <summary>允许目录 id / 名字 / name@version 三种写法；错误里列出可用的投影 id。</summary>
    public async Task<string> ResolvePackId(Instance inst, string input, CancellationToken ct)
    {
        var want = (input ?? "").Trim();
        var rows = await List(inst, ct);
        PackRow? hit = rows.FirstOrDefault(r => string.Equals(r.Id, want, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(r => string.Equals(r.Title, want, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(r => string.Equals($"{r.Title}@{r.Version}", want, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(r => string.Equals($"{r.Title}-{r.Version}", want, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(r => r.Id.EndsWith("-" + want, StringComparison.OrdinalIgnoreCase));
        if (hit is not null) return hit.Id;
        throw new PadError("PA012", "unknown pack id", want,
            rows.Count == 0 ? "这个实例下还没有投影的整合包" : "可用：" + string.Join(" / ", rows.Select(r => r.Id)),
            ["pad cli pack list " + inst.Id]);
    }

    static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>
    /// Import a <c>.pack.json</c> / <c>.pack.zip</c> / <c>.pinst.zip</c>. Import is the
    /// whole chain — sniff, pin the release, build the home, project, allow — so PAD does
    /// not re-implement any step of it. This is the drag/drop + sidecar path; the ledger is
    /// written afterwards from the pack manifest (C# is the only ledger writer).
    /// </summary>
    public async Task<string> Import(string packPath, IProgress<string>? log, CancellationToken ct)
    {
        var before = _launcher.Instances().Select(i => i.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var abs = Path.GetFullPath((packPath ?? "").Trim().Trim('"'));
        var job = _launcher.StartJob("import", null);
        var token = _launcher.TrackCancel(job.Id, ct);
        log = _launcher.TeeJob(job.Id, log);
        try
        {
            var r = await Cli($"import \"{abs}\"", log, token);
            var inst = FindImportedInstance(r, before);
            if (inst is not null) RecordImportLedger(inst, abs, null, log);
            else log?.Report("账本：导入成功但没认出新建的实例，这次不登记（不影响导入结果）");
            _launcher.FinishJob(job.Id, "done");
            return r;
        }
        catch { _launcher.FinishJob(job.Id, "failed"); throw; }
    }

    /// <summary>
    /// Build an instance from a catalog pack through the TypeScript import chain — the same
    /// chain as <c>packagent dsh launcher import</c>: create home + workspace, copy
    /// overrides, project + allow, install the manager, then <c>dsh plugin add</c> every
    /// <c>dsh.plugins[]</c> 组合包 (docs/modpack.md §3.2/§3.4). Projection alone is not
    /// enough: an integration pack whose only content is declared plugins would create an
    /// instance that does not do its job. The command prints the new instance record as its
    /// last JSON value; the id is read from there and the instance re-loaded from disk so the
    /// caller gets the full record, including <c>source.packId</c>.
    /// </summary>
    public async Task<Instance> ImportPack(PackCard card, string? name,
        IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.Path))
            throw new PadError("PA012", "pack has no path",
                string.IsNullOrWhiteSpace(card.Id) ? "pack" : card.Id, null, ["pad cli pack list"]);
        var packPath = Path.GetFullPath(card.Path.Trim().Trim('"'));
        if (!File.Exists(packPath))
            throw new PadError("PA012", "no such pack file", packPath, null, ["pad cli pack list"]);

        // The record JSON can be longer than a 40-line tail when a pack carries many
        // plugins; keep all of stdout (and stream it to the job log) instead of guessing.
        var before = _launcher.Instances().Select(i => i.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var label = string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title;
        var job = _launcher.StartJob("import", null, null, label);
        var token = _launcher.TrackCancel(job.Id, ct);
        log = _launcher.TeeJob(job.Id, log);
        try
        {
            var args = new StringBuilder("import \"").Append(packPath.Replace("\"", "")).Append('"');
            if (!string.IsNullOrWhiteSpace(name))
                args.Append(" --name \"").Append(name.Trim().Replace("\"", "")).Append('"');
            var output = await CliCapture(args.ToString(), log, token);
            var inst = ImportedInstance(output, card, before);
            // P2：import 链成功后写账本（C# 唯一写者；失败只提示，不改导入结果）。
            RecordImportLedger(inst, packPath, card, log);
            _launcher.FinishJob(job.Id, "done");
            return inst;
        }
        catch (OperationCanceledException)
        {
            _launcher.FinishJob(job.Id, "cancelled");
            throw;
        }
        catch
        {
            _launcher.FinishJob(job.Id, "failed");
            throw;
        }
    }

    /// <summary>
    /// Resolve the instance an import just created. The chain prints the instance record as
    /// its last JSON value; if a stray line eats it, fall back to the only new instance that
    /// carries this pack as its source.
    /// </summary>
    Instance ImportedInstance(string output, PackCard card, HashSet<string> before)
    {
        string? id = null;
        using (var doc = LastJson(output))
        {
            if (doc?.RootElement.ValueKind == JsonValueKind.Object)
                id = Str(doc.RootElement, "id");
        }
        if (string.IsNullOrWhiteSpace(id))
        {
            var fresh = _launcher.Instances().Where(i => !before.Contains(i.Id)).ToList();
            var hit = fresh.FirstOrDefault(i =>
                          string.Equals(i.Source?.PackId, card.Id, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(i.Source?.PackPath, card.Path, StringComparison.OrdinalIgnoreCase))
                      ?? (fresh.Count == 1 ? fresh[0] : null);
            id = hit?.Id;
        }
        if (string.IsNullOrWhiteSpace(id))
            throw new PadError("PA028", "import finished but the new instance was not found",
                "packagent dsh launcher import", Tail(output, 20), ["pad cli instance list"]);
        var inst = _launcher.Instance(id!);
        if (inst is null)
            throw new PadError("PA026", "import finished but the instance record is unreadable",
                $"instances/{id}", Tail(output, 20), ["pad cli instance list"]);
        return inst;
    }

    static string Tail(string text, int lines)
    {
        var rows = (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, rows.Length <= lines ? rows : rows[^lines..]);
    }

    /// <summary>
    /// Run the launcher CLI and keep every stdout line (the read-only <see cref="CliJson"/>
    /// path cannot be reused: the import chain spawns pnpm grandchildren, so waiting on
    /// stdout EOF can hang long after the process is gone — wait on the process itself).
    /// Lines still stream to <paramref name="log"/> so the job log shows the install.
    /// </summary>
    async Task<string> CliCapture(string args, IProgress<string>? log, CancellationToken ct)
    {
        if (RuntimeDir() is null) await Bun.EnsureAsync(log, ct);
        var (exe, line, cwd, entryEnv) = Entry($"dsh launcher --root \"{_launcher.Root}\" --json {args}");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = line,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (entryEnv is not null)
            foreach (var (k, v) in entryEnv) psi.Environment[k] = v;
        Proc.PrependToolPath(psi);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var gate = new object();
        async Task ReadLines(StreamReader stream, StringBuilder sink)
        {
            while (await stream.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                log?.Report(line);
                lock (gate) sink.AppendLine(line);
            }
        }
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        proc.Exited += (_, _) =>
        {
            try { exited.TrySetResult(proc.ExitCode); }
            catch { exited.TrySetResult(-1); }
        };
        proc.Start();
        var readers = Task.WhenAll(
            ReadLines(proc.StandardOutput, stdout),
            ReadLines(proc.StandardError, stderr));

        await using var registration = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        });

        var code = await exited.Task;
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        // The chain prints the new instance record as its last JSON value, right before
        // exiting. Wait on the process first (pnpm grandchildren must never hang the job),
        // then let the pipe readers reach EOF so the record cannot be half-captured. EOF is
        // immediate here because dsh/pnpm children get their own pipes, not these handles;
        // the bound is only a safety valve, not the normal path.
        if (!readers.IsCompleted)
        {
            await Task.WhenAny(readers, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None));
            if (!readers.IsCompleted)
                _ = readers.ContinueWith(static t => t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        string outText, errText;
        lock (gate) { outText = stdout.ToString(); errText = stderr.ToString(); }
        if (code != 0)
            throw new PadError("PA028", "the import command failed",
                $"packagent dsh launcher {args}",
                Tail(string.IsNullOrWhiteSpace(errText) ? outText : errText, 40),
                ["看上面的输出"]);
        return outText;
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

    public async Task<string> Allow(Instance inst, string packId, IProgress<string>? log, CancellationToken ct)
    {
        var id = await ResolvePackId(inst, packId, ct);
        if (RepoRoot() is null && RuntimeDir() is null)
        {
            SetNativeEnabled(inst, id, true);
            return id;
        }
        await Cli($"pack allow {inst.Id} {id}", log, ct);
        await List(inst, ct);
        return id;
    }

    public async Task<string> Deny(Instance inst, string packId, IProgress<string>? log, CancellationToken ct)
    {
        var id = await ResolvePackId(inst, packId, ct);
        if (RepoRoot() is null && RuntimeDir() is null)
        {
            SetNativeEnabled(inst, id, false);
            return id;
        }
        await Cli($"pack deny {inst.Id} {id}", log, ct);
        await List(inst, ct);
        return id;
    }

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

    // ---- P2: 账本写入 + diff 应用 + 停用联动 + 卸载/修复 -------------------

    /// <summary>装包管理器插件（profile 里没有才装；remove/disable 的 PA110 守卫在 UI 层）。</summary>
    public const string ManagerSpec = "@sakikotgw/pack-agent-dsh";

    /// <summary>包文件名 → 目录行 id，与 TS <c>pack-catalog.packIdFromPath</c> 同源。</summary>
    public static string PackIdFromPath(string path)
    {
        var name = Path.GetFileName((path ?? "").Trim().Trim('"'));
        foreach (var ext in PackExtensions)
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return name[..^ext.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>投影目录名的 slug，与 TS <c>compile.ts</c> 的 npmNameForPack/slug 同规则。</summary>
    public static string NpmNameForPack(string name) => "pack-agent-modpack-" + SlugOf(name);

    static string SlugOf(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in (name ?? "").Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "pack" : slug;
    }

    static string ContentDirFor(PackManifest? manifest)
    {
        var name = !string.IsNullOrWhiteSpace(manifest?.Name) ? manifest!.Name : "pack";
        return ".agent-pack/modpacks/" + NpmNameForPack(name);
    }

    static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return null;
    }

    static bool Same(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// import 链成功后按包 manifest 记一条账本（C# 是唯一写者）。失败只写日志：
    /// 导入本身已经成功，账本登记不该把它变成失败。
    /// </summary>
    void RecordImportLedger(Instance inst, string packPath, PackCard? card, IProgress<string>? log)
    {
        try
        {
            var abs = Path.GetFullPath((packPath ?? "").Trim().Trim('"'));
            var manifest = PackManifest.TryRead(abs);
            if (manifest is null)
            {
                // pinst / 旧格式：整实例快照自带账本（Q4），这里不重算、不认领。
                log?.Report("账本：读不出包清单（pinst 或旧格式），这次不登记");
                return;
            }
            var profile = PackLedger.DefaultProfile(_launcher, inst);
            var packId = FirstNonEmpty(inst.Source?.PackId, card?.Id, PackIdFromPath(abs))!;
            var title = FirstNonEmpty(inst.Source?.PackTitle, card?.Title, manifest?.Name, packId)!;
            var version = FirstNonEmpty(inst.Source?.PackVersion, card?.Version, manifest?.Version) ?? "";
            var ledger = PackLedger.Load(_launcher, inst);
            ledger.RecordPack(packId, title, version, abs, ContentDirFor(manifest), allow: true);
            var plugins = BuildLedgerPlugins(inst, profile, packId, manifest);
            ledger.RecordInstall(packId, profile, plugins);
            var measured = ledger.EffectiveState(profile, packId);
            var state = measured switch
            {
                PackLedger.StateAbsent => PackLedger.StateIncomplete,
                PackLedger.StateDisabled => PackLedger.StateInstalled,
                _ => measured,
            };
            ledger.SetState(packId, profile, state);
            ledger.Save();
            log?.Report($"账本：已登记 {packId} {version} · {plugins.Count} 个插件（profile {profile}）");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or ArgumentException or NotSupportedException)
        {
            log?.Report($"账本登记失败（不影响导入结果）：{ex.Message}");
        }
    }

    /// <summary>
    /// 从包 manifest + profile 实际依赖构造账本插件的记录（只记真装上的；必需但缺的也记，
    /// 这样对账会如实标 incomplete）。
    /// </summary>
    List<LedgerPlugin> BuildLedgerPlugins(Instance inst, string profile, string packId,
        PackManifest? manifest)
    {
        var list = new List<LedgerPlugin>();
        if (manifest is null) return list;
        var facts = PackLedger.ReadProfile(inst, profile);
        foreach (var plugin in manifest.Plugins)
        {
            var declared = plugin.EffectiveSpec;
            if (declared.Length == 0) continue;
            var recordSpec = declared;
            string? localDirRel = null;
            string? key;
            if (!string.IsNullOrWhiteSpace(plugin.Local))
            {
                var name = Path.GetFileName(plugin.Local.TrimEnd('/', '\\'));
                if (name.Length == 0) continue;
                localDirRel = $"home/local-plugins/{packId}/{name}".Replace('\\', '/');
                var absDir = Path.Combine(_launcher.InstanceDir(inst.Id),
                    localDirRel.Replace('/', Path.DirectorySeparatorChar));
                recordSpec = "file:" + absDir;
                key = LocalDepKey(facts, absDir) ?? name;
            }
            else key = PackLedger.PluginKey(declared);
            if (key.Length == 0) continue;

            if (!facts.HasDependency(key))
            {
                if (plugin.Required)
                    list.Add(new LedgerPlugin { Spec = recordSpec, Origin = "pack", LocalDir = localDirRel });
                continue;
            }
            list.Add(new LedgerPlugin
            {
                Spec = recordSpec,
                AddedVersion = PackLedger.InstalledVersion(inst, profile, key),
                Origin = "pack",
                LocalDir = localDirRel,
            });
        }
        return list;
    }

    /// <summary>import 输出里认新建实例：最后一段 JSON 的 id → 找不到就用“唯一新实例”兜底。</summary>
    Instance? FindImportedInstance(string output, HashSet<string> before)
    {
        string? id = null;
        using (var doc = LastJson(output))
            if (doc?.RootElement.ValueKind == JsonValueKind.Object) id = Str(doc.RootElement, "id");
        var hit = string.IsNullOrWhiteSpace(id) ? null : _launcher.Instance(id!);
        if (hit is not null) return hit;
        var fresh = _launcher.Instances().Where(i => !before.Contains(i.Id)).ToList();
        return fresh.Count == 1 ? fresh[0] : null;
    }

    /// <summary><c>pack project</c> 输出最后一段 JSON 的 <c>id</c>（投影目录名，即 pack list 行 id）。</summary>
    static string? ProjectionIdFromOutput(string output)
    {
        using var doc = LastJson(output);
        if (doc?.RootElement.ValueKind != JsonValueKind.Object) return null;
        return Str(doc.RootElement, "id") is { Length: > 0 } id ? id : null;
    }

    /// <summary>账本里的投影目录 → pack list 的行 id（<c>.agent-pack/modpacks/&lt;npmName&gt;</c>）。</summary>
    static string ProjectionIdOf(LedgerPack pack)
    {
        var dir = (pack.Content.Dir ?? "").Trim().TrimEnd('/', '\\');
        if (dir.Length == 0) return "";
        return Path.GetFileName(dir.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// 把整合包装到已有实例（R8 统一入口的服务端实现）：投影 → 内容启用 → 确保管理器 →
    /// 按 diff 逐 spec <c>dsh plugin add</c>。版本冲突返回结构化结果，不静默覆盖（R6）。
    /// </summary>
    public Task<PackApplyResult> ApplyPackToInstance(Instance inst, PackCard card, string profile,
        IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        return ApplyPackToInstance(inst, card.Path, profile, card, log, ct, null);
    }

    /// <summary>同 <see cref="ApplyPackToInstance(Instance, PackCard, string, IProgress{string}?, CancellationToken)"/>，认路径。</summary>
    public Task<PackApplyResult> ApplyPackToInstance(Instance inst, string packPath, string profile,
        IProgress<string>? log, CancellationToken ct) =>
        ApplyPackToInstance(inst, packPath, profile, null, log, ct, null);

    async Task<PackApplyResult> ApplyPackToInstance(Instance inst, string packPath, string profile,
        PackCard? card, IProgress<string>? log, CancellationToken ct,
        IReadOnlyCollection<string>? skipSpecs = null, bool heal = true)
    {
        ArgumentNullException.ThrowIfNull(inst);
        var abs = Path.GetFullPath((packPath ?? "").Trim().Trim('"'));
        if (!File.Exists(abs))
            throw new PadError("PA012", "no such pack file", abs, null, ["pad cli pack list"]);
        var manifest = PackManifest.TryRead(abs)
            ?? throw new PadError("PA012", "pack manifest is unreadable", abs, "读不出 pack.json",
                ["确认这是 .pack.json / .pack.zip"]);
        var prof = (profile ?? "").Trim();
        if (prof.Length == 0)
            throw new PadError("PA029", "profile is required", $"instance `{inst.Id}`", "", ["指定 profile 名"]);

        // PnpmHeal.Relink 一次 60–70s（release node_modules 1 万多个目录），批量安装只在第一次 add 前做。
        var healed = !heal;
        var packId = PackIdFromPath(abs);
        var result = new PackApplyResult
        {
            PackId = packId,
            Title = FirstNonEmpty(manifest.Name, card?.Title, packId)!,
            Version = FirstNonEmpty(manifest.Version, card?.Version) ?? "",
            Profile = prof,
        };

        // 1) 内容投影：与版本页「新建实例」同一条 project 链（R8）。
        var projectOutput = await Project(inst, abs, log, ct);
        // pack list 的行 id 是投影目录名（npmName），不是包文件名 stem；allow/deny 用这个 id。
        var projectionId = ProjectionIdFromOutput(projectOutput) ?? NpmNameForPack(manifest.Name);
        result.ProjectionId = projectionId;

        // 2) 内容启用。失败只记警告，插件轴仍按 diff 处理（投影已成功）。
        var contentAllowed = true;
        try { await Allow(inst, projectionId, log, ct); }
        catch (Exception ex) when (ex is PadError or IOException or UnauthorizedAccessException)
        {
            contentAllowed = false;
            result.Warnings.Add("内容启用失败：" + PadError.Describe(ex));
        }

        // 3) 管理器：没有才装。
        if (!HasManagerBundle(inst, prof))
        {
            log?.Report($"这个 profile 没有 {ManagerSpec}，先装管理器");
            await _launcher.AddBundles(inst, prof, [ManagerSpec], log, ct, heal: !healed);
            healed = true;
            result.ManagerInstalled = true;
        }

        // 4) 按 diff 逐 spec；包内本地插件先复制到实例目录再 file: 装（Q6）。
        var skip = (skipSpecs ?? []).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var facts = PackLedger.ReadProfile(inst, prof);
        foreach (var plugin in manifest.Plugins)
        {
            var declared = plugin.EffectiveSpec;
            if (declared.Length == 0) continue;
            // 预检确认装不上、用户选择跳过的插件：不 add，但账本仍按“声明过”记（包标 incomplete，可修复）。
            if (skip.Contains(declared))
            {
                result.Skipped.Add(declared);
                result.Warnings.Add($"已跳过插件：{declared}");
                continue;
            }
            var target = declared;
            var key = PackLedger.PluginKey(declared);
            if (!string.IsNullOrWhiteSpace(plugin.Local))
            {
                var name = Path.GetFileName(plugin.Local.TrimEnd('/', '\\'));
                if (name.Length == 0)
                {
                    result.Warnings.Add($"插件 {declared} 的 local 目录不合法，跳过");
                    continue;
                }
                var absDir = PrepareLocalPlugin(inst, abs, packId, plugin.Local, log);
                target = "file:" + absDir;
                key = LocalDepKey(facts, absDir) ?? name;
            }
            if (key.Length == 0) continue;

            if (facts.HasDependency(key))
            {
                var pinned = PinnedVersion(declared);
                var actual = PackLedger.InstalledVersion(inst, prof, key);
                if (pinned is not null && actual is not null
                    && !string.Equals(pinned, actual, StringComparison.OrdinalIgnoreCase))
                {
                    result.Conflicts.Add(new PackPluginConflict
                    {
                        Spec = declared, Expected = pinned, Actual = actual,
                        Reason = $"已存在不同版本 {actual}（包要 {pinned}），没有静默覆盖",
                    });
                    continue;
                }
                if (facts.BundleName(key) is null)
                {
                    // 停用/手工改过：依赖在，只把 bundle 行补回来，不重新下载（Q1 机制）。
                    var bundles = PackLedger.MergeBundles(facts.Bundles, [key]);
                    PackLedger.WriteBundles(inst, prof, bundles);
                    result.BundlesRestored.Add(key);
                    facts = PackLedger.ReadProfile(inst, prof);
                }
                else result.Kept.Add(key);
                continue;
            }

            log?.Report($"补装插件 {target}");
            try
            {
                await _launcher.AddBundles(inst, prof, [QuoteSpec(target)], log, ct, heal: !healed);
                healed = true;
                result.Added.Add(target);
            }
            catch (PadError ex)
            {
                // 必选插件装不上：包标 incomplete 并回结构化失败（P3 的 allow-version 通道负责“接受风险”）；
                // 可选插件装不上只提醒，不影响包状态（与 import 链 add-plugins 的 required 语义一致）。
                result.Failures.Add(new PackPluginFailure
                {
                    Spec = declared,
                    Required = plugin.Required,
                    Reason = ex.Headline + (string.IsNullOrWhiteSpace(ex.Detail) ? "" : "：" + ex.Detail),
                });
                if (plugin.Required)
                    result.Warnings.Add($"必选插件没装上：{declared}");
                else
                    result.Warnings.Add($"可选插件跳过：{declared}");
                continue;
            }
            facts = PackLedger.ReadProfile(inst, prof);
        }

        // 5) 写账本：内容事实 + 这次真正登记的插件。
        var ledger = PackLedger.Load(_launcher, inst);
        ledger.RecordPack(packId, result.Title, result.Version, abs, ContentDirFor(manifest),
            allow: contentAllowed);
        ledger.RecordInstall(packId, prof, BuildLedgerPlugins(inst, prof, packId, manifest));
        var requiredFailed = result.Failures.Any(f => f.Required);
        result.State = result.Conflicts.Count == 0 && !requiredFailed && result.Skipped.Count == 0
            ? PackLedger.StateInstalled : PackLedger.StateIncomplete;
        ledger.SetState(packId, prof, result.State);
        ledger.Save();
        log?.Report(result.Text);
        return result;
    }

    // ---- P2 install concept：预检 → 一次授权 → 安装报告 ---------------------

    /// <summary>解析 <c>--pack &lt;id|path&gt;</c>：先当文件路径，再查目录行（id/title/name@version）。</summary>
    public async Task<PackCard?> ResolveCard(string packRef, IProgress<string>? log, CancellationToken ct)
    {
        var raw = (packRef ?? "").Trim().Trim('"');
        if (raw.Length == 0) return null;
        if (File.Exists(raw))
            return new PackCard
            {
                Id = PackIdFromPath(raw),
                Title = Path.GetFileNameWithoutExtension(raw),
                Path = Path.GetFullPath(raw),
                Kind = raw.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "pack.zip" : "pack.json",
            };
        List<PackCard> cards;
        try { cards = await Catalog("", null, log, ct); }
        catch (Exception) { return null; }
        return cards.FirstOrDefault(c => Same(c.Id, raw))
               ?? cards.FirstOrDefault(c => Same(c.Title, raw))
               ?? cards.FirstOrDefault(c => Same($"{c.Title}-{c.Version}", raw))
               ?? cards.FirstOrDefault(c => LaunchPolicy.SamePath(c.Path, raw));
    }

    /// <summary>
    /// 安装前预检（按 DSH 0.2.1-rc.2 的 <c>evaluatePluginCompatibility</c> 规则）：
    /// 逐个声明插件解析 npm 元数据/本地 package.json，算兼容性、缺失与版本冲突；内核不匹配只警告。
    /// 预检不写实例（只读 profile/账本/包），可以反复跑。
    /// </summary>
    public Task<PackPreflight> PreflightPack(Instance inst, string profile, PackCard card,
        IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        return PreflightPack(inst, profile, card.Path, card, log, ct);
    }

    /// <summary>同 <see cref="PreflightPack(Instance, string, PackCard, IProgress{string}?, CancellationToken)"/>，认路径。</summary>
    public Task<PackPreflight> PreflightPack(Instance inst, string profile, string packPath,
        IProgress<string>? log, CancellationToken ct) =>
        PreflightPack(inst, profile, packPath, null, log, ct);

    async Task<PackPreflight> PreflightPack(Instance inst, string profile, string packPath, PackCard? card,
        IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inst);
        var abs = Path.GetFullPath((packPath ?? "").Trim().Trim('"'));
        if (!File.Exists(abs))
            throw new PadError("PA012", "no such pack file", abs, null, ["pad cli pack list"]);
        var manifest = PackManifest.TryRead(abs)
            ?? throw new PadError("PA012", "pack manifest is unreadable", abs, "读不出 pack.json",
                ["确认这是 .pack.json / .pack.zip"]);
        var prof = (profile ?? "").Trim();
        if (prof.Length == 0)
            throw new PadError("PA029", "profile is required", $"instance `{inst.Id}`", "", ["指定 profile 名"]);

        var packId = PackIdFromPath(abs);
        var instanceKernel = (inst.Dsh.Version ?? "").Trim();
        var packKernel = FirstNonEmpty(manifest.DshVersion, card?.Kernel) ?? "";
        var ledger = PackLedger.Load(_launcher, inst);
        var preflight = new PackPreflight
        {
            PackId = packId,
            Title = FirstNonEmpty(manifest.Name, card?.Title, packId)!,
            Version = FirstNonEmpty(manifest.Version, card?.Version) ?? "",
            Path = abs,
            InstanceId = inst.Id,
            Profile = prof,
            PackKernel = packKernel,
            InstanceKernel = instanceKernel,
            ManagerInstalled = HasManagerBundle(inst, prof),
            AlreadyInstalled = ledger.Get(packId) is not null,
            LedgerState = ledger.EffectiveState(prof, packId),
        };
        preflight.KernelMatches = KernelMatches(packKernel, instanceKernel);
        if (!preflight.KernelMatches)
            preflight.Warnings.Add($"包钉内核 {packKernel}，实例是 {instanceKernel}（只提醒、不阻塞；装完可能启动不了）");
        else if (packKernel.Length == 0)
            preflight.Warnings.Add("包没写 dsh.version（内核未声明）");
        if (!preflight.ManagerInstalled)
            preflight.Warnings.Add($"这个 profile 没有 {ManagerSpec}（安装时会自动补上）");

        // 并发解析元数据；NpmMetaSource 自己限并发（≤4）+ 15s 超时 + 进程内缓存。
        var facts = PackLedger.ReadProfile(inst, prof);
        var rows = await Task.WhenAll(manifest.Plugins
            .Select(plugin => PreflightOne(inst, prof, abs, packId, plugin, facts, ct)));
        preflight.Plugins = rows.ToList();
        preflight.OkCount = rows.Count(r => r.Status == PackPluginStatus.Ok);
        preflight.ExemptionCount = rows.Count(r => r.Status == PackPluginStatus.NeedsExemption);
        preflight.MissingCount = rows.Count(r => r.Status == PackPluginStatus.Missing);
        preflight.ConflictCount = rows.Count(r => r.Status == PackPluginStatus.VersionConflict);
        preflight.UnknownCount = rows.Count(r => r.Status == PackPluginStatus.Unknown);
        preflight.NeedsDecision = preflight.ExemptionCount > 0
                                  || preflight.MissingCount > 0
                                  || preflight.ConflictCount > 0;
        preflight.CanInstall = !preflight.NeedsDecision;
        preflight.Summary = BuildPreflightSummary(preflight);
        log?.Report(preflight.Summary);
        return preflight;
    }

    async Task<PreflightPlugin> PreflightOne(Instance inst, string profile, string packPath, string packId,
        PackManifestPlugin declared, ProfileFacts facts, CancellationToken ct)
    {
        var spec = declared.EffectiveSpec;
        var isLocal = !string.IsNullOrWhiteSpace(declared.Local);
        var localName = isLocal ? Path.GetFileName(declared.Local.TrimEnd('/', '\\')) : "";
        var name = isLocal ? localName : LaunchPolicy.BundleBareName(spec);
        var pinned = isLocal ? null : PinnedVersion(spec);
        var row = new PreflightPlugin
        {
            Spec = spec,
            Name = name,
            Requested = isLocal ? "" : VersionSelector(spec),
            Required = declared.Required,
            IsLocal = isLocal,
            InstalledVersion = name.Length > 0 ? PackLedger.InstalledVersion(inst, profile, name) ?? "" : "",
        };

        NpmPackageMeta meta;
        if (isLocal)
        {
            row.LatestVersion = "";
            meta = ReadLocalPluginMeta(inst, packPath, packId, declared.Local);
            if (!meta.Found)
            {
                row.Status = PackPluginStatus.Missing;
                row.Requirement = "包内本地插件不存在（plugins/ 目录没打进包）";
                return row;
            }
            row.Name = meta.Name.Length > 0 ? meta.Name : name;
            row.LatestVersion = meta.Version;
        }
        else
        {
            meta = await _meta.Get(name, pinned, ct);
            row.LatestVersion = meta.Version;
            if (!meta.Found)
            {
                row.Status = PackPluginStatus.Missing;
                row.Requirement = pinned is null
                    ? "npm registry 404（已下架）"
                    : $"npm registry 404（{name}@{pinned} 不存在）";
                return row;
            }
            if (!meta.Resolved)
            {
                row.Status = PackPluginStatus.Unknown;
                row.Requirement = "查不到 npm 元数据（网络/超时），未验证兼容性；安装时按 DSH 实际结果处理";
                return row;
            }
        }

        // 版本冲突优先于兼容性：已装的版本和包声明对不上时，先让用户决定，别把不兼容的 add 上去。
        var conflict = VersionConflictFor(spec, row.InstalledVersion);
        if (conflict is not null)
        {
            row.Status = PackPluginStatus.VersionConflict;
            row.Requirement = conflict;
            return row;
        }
        var (compatible, requirement) = EvaluateCompat(meta, inst.Dsh.Version);
        if (!compatible)
        {
            row.Status = PackPluginStatus.NeedsExemption;
            row.Requirement = requirement;
            return row;
        }
        row.Status = PackPluginStatus.Ok;
        return row;
    }

    /// <summary>spec 里声明了版本选择器时返回它（<c>^0.79.0</c> / <c>1.2.3</c>）；裸名返回空。</summary>
    static string VersionSelector(string spec)
    {
        var s = (spec ?? "").Trim();
        if (s.Length == 0) return "";
        var bare = LaunchPolicy.BundleBareName(s);
        if (bare.Length == 0 || string.Equals(bare, s, StringComparison.Ordinal)) return "";
        return s[bare.Length..].TrimStart('@').Trim();
    }

    /// <summary>
    /// 版本冲突判定（R6）：包钉精确版本 ≠ 已装版本；或已装版本不满足包给的 range。
    /// 返回人话原因，没有冲突返回 null。
    /// </summary>
    static string? VersionConflictFor(string spec, string? installedVersion)
    {
        var installed = (installedVersion ?? "").Trim();
        if (installed.Length == 0) return null;
        var pinned = PinnedVersion(spec);
        if (pinned is not null)
            return string.Equals(pinned, installed, StringComparison.OrdinalIgnoreCase)
                ? null
                : $"包钉 {pinned}，实例里是 {installed}";
        var selector = VersionSelector(spec);
        if (selector.Length == 0 || selector is "latest" or "*" or "x" or "X") return null;
        if (PackSemver.Satisfies(installed, selector)) return null;
        return $"包要 {selector}，实例里是 {installed}（不满足 range）";
    }

    /// <summary>
    /// DSH 兼容门（dsh-app-boot <c>evaluatePluginCompatibility</c>）：有 <c>engines.dsh</c>
    /// 就只比它；否则所有 <c>@deepseek-ai/dsh</c> / <c>@deepseek-ai/dsh-*</c> peers 都必须匹配。
    /// peers 的 <c>workspace:^|~|*</c> 视为当前内核（DSH 同规则）。
    /// </summary>
    static (bool Ok, string Requirement) EvaluateCompat(NpmPackageMeta meta, string? runtimeVersion)
    {
        var runtime = (runtimeVersion ?? "").Trim();
        if (runtime.Length == 0) return (true, "");
        if (!string.IsNullOrWhiteSpace(meta.EnginesDsh))
        {
            var range = meta.EnginesDsh!.Trim();
            if (range.Length == 0) return (false, "engines.dsh: (empty)");
            return PackSemver.Satisfies(runtime, range)
                ? (true, "")
                : (false, $"engines.dsh: {range}（当前内核 {runtime}）");
        }
        var unmet = new List<string>();
        foreach (var peer in meta.PeerDependencies)
        {
            if (peer.Key != "@deepseek-ai/dsh"
                && !peer.Key.StartsWith("@deepseek-ai/dsh-", StringComparison.OrdinalIgnoreCase)) continue;
            var range = peer.Value ?? "";
            var effective = range is "workspace:^" or "workspace:~" or "workspace:*" ? runtime : range;
            if (!PackSemver.Satisfies(runtime, effective)) unmet.Add($"{peer.Key} {range}");
        }
        if (unmet.Count == 0) return (true, "");
        return (false, "peerDependencies: " + string.Join(", ", unmet) + $"（当前内核 {runtime}）");
    }

    static bool KernelMatches(string packKernel, string instanceKernel)
    {
        if (packKernel.Length == 0) return true;
        if (string.Equals(packKernel, instanceKernel, StringComparison.OrdinalIgnoreCase)) return true;
        if (PackSemver.IsExact(packKernel)) return false;
        return PackSemver.Satisfies(instanceKernel, packKernel);
    }

    /// <summary>包内本地插件的 package.json（先包内，再已落盘的实例副本）；不查 npm。</summary>
    NpmPackageMeta ReadLocalPluginMeta(Instance inst, string packPath, string packId, string local)
    {
        var rel = (local ?? "").Trim().Replace('\\', '/').TrimStart('.', '/').TrimEnd('/');
        var name = Path.GetFileName(rel);
        var json = "";
        try
        {
            var abs = Path.GetFullPath(packPath);
            if (abs.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(abs);
                var wanted = rel + "/package.json";
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(NormalizeZipEntry(e.FullName), wanted, StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                    json = reader.ReadToEnd();
                }
            }
            else
            {
                var file = Path.Combine(Path.GetDirectoryName(abs)!,
                    rel.Replace('/', Path.DirectorySeparatorChar), "package.json");
                if (File.Exists(file)) json = File.ReadAllText(file);
            }
            if (json.Length == 0)
            {
                // Q6：已复制到实例目录的副本（重复预检/修复时包可能被挪走）。
                var copied = Path.Combine(inst.Home, "local-plugins", packId, name, "package.json");
                if (File.Exists(copied)) json = File.ReadAllText(copied);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or JsonException)
        {
            return NpmPackageMeta.Missing(name);
        }
        if (json.Length == 0) return NpmPackageMeta.Missing(name);
        return ParsePluginMeta(name, json);
    }

    static string NormalizeZipEntry(string fullName) =>
        (fullName ?? "").Replace('\\', '/').TrimStart('.').TrimStart('/');

    /// <summary>从插件 package.json 读 name/version/engines.dsh/peerDependencies。</summary>
    static NpmPackageMeta ParsePluginMeta(string fallbackName, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return NpmPackageMeta.Unknown(fallbackName);
        var name = root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString() ?? ""
            : "";
        var version = root.TryGetProperty("version", out var verEl) && verEl.ValueKind == JsonValueKind.String
            ? verEl.GetString() ?? ""
            : "";
        string? enginesDsh = null;
        if (root.TryGetProperty("engines", out var engines) && engines.ValueKind == JsonValueKind.Object
            && engines.TryGetProperty("dsh", out var dshEl))
            enginesDsh = dshEl.ValueKind == JsonValueKind.String ? dshEl.GetString() : dshEl.GetRawText();
        var peers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("peerDependencies", out var peersEl) && peersEl.ValueKind == JsonValueKind.Object)
            foreach (var peer in peersEl.EnumerateObject())
                peers[peer.Name] = peer.Value.ValueKind == JsonValueKind.String
                    ? peer.Value.GetString() ?? ""
                    : peer.Value.GetRawText();
        return new NpmPackageMeta
        {
            Name = name.Length > 0 ? name : fallbackName,
            Version = version,
            EnginesDsh = enginesDsh,
            PeerDependencies = peers,
        };
    }

    static string BuildPreflightSummary(PackPreflight preflight)
    {
        var kernel = preflight.KernelMatches
            ? "内核匹配"
            : $"内核不匹配（包 {preflight.PackKernel} / 实例 {preflight.InstanceKernel}）";
        var parts = new List<string>();
        if (preflight.Plugins.Count == 0) parts.Add("没有声明插件");
        if (preflight.OkCount > 0) parts.Add($"{preflight.OkCount} 可装");
        if (preflight.ExemptionCount > 0) parts.Add($"{preflight.ExemptionCount} 需授权");
        if (preflight.MissingCount > 0) parts.Add($"{preflight.MissingCount} 已下架/缺包");
        if (preflight.ConflictCount > 0) parts.Add($"{preflight.ConflictCount} 版本冲突");
        if (preflight.UnknownCount > 0) parts.Add($"{preflight.UnknownCount} 未验证");
        return $"{kernel} · 插件 {preflight.Plugins.Count} 个：" + string.Join(" · ", parts);
    }

    static List<string> DecisionBlockers(PackPreflight preflight, PackInstallOptions opts)
    {
        var blockers = new List<string>();
        if (preflight.ConflictCount > 0)
            blockers.Add($"{preflight.ConflictCount} 个插件版本冲突（不会自动覆盖；先手工处理或换包版本）");
        if (preflight.ExemptionCount > 0 && !opts.AcceptRisk)
            blockers.Add($"{preflight.ExemptionCount} 个插件需要兼容豁免（--accept-risk / 界面勾选「接受风险」）");
        if (preflight.MissingCount > 0 && !opts.SkipMissing)
            blockers.Add($"{preflight.MissingCount} 个插件在 npm 上找不到（--skip-missing / 界面勾选「跳过」）");
        return blockers;
    }

    /// <summary>
    /// 安装整合包到已有实例（安装概念的后端）：先预检 → 需要用户决定而未带对应选项时**不落盘**并
    /// 返回 needs-decision；AcceptRisk → 先批量写 DSH allow-version 豁免；SkipMissing → 跳过缺失插件
    /// 并把包标 incomplete；DryRun → 只返回预检汇总、不写任何东西。应用复用 ApplyPackToInstance。
    /// </summary>
    public Task<PackInstallReport> InstallPackToInstance(Instance inst, string profile, PackCard card,
        PackInstallOptions opts, IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        return InstallPackToInstance(inst, profile, card.Path, card, opts, log, ct);
    }

    /// <summary>同 <see cref="InstallPackToInstance(Instance, string, PackCard, PackInstallOptions, IProgress{string}?, CancellationToken)"/>，认路径。</summary>
    public Task<PackInstallReport> InstallPackToInstance(Instance inst, string profile, string packPath,
        PackInstallOptions opts, IProgress<string>? log, CancellationToken ct) =>
        InstallPackToInstance(inst, profile, packPath, null, opts, log, ct);

    async Task<PackInstallReport> InstallPackToInstance(Instance inst, string profile, string packPath,
        PackCard? card, PackInstallOptions? opts, IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inst);
        opts ??= new PackInstallOptions();
        var prof = (profile ?? "").Trim();
        if (prof.Length == 0)
            throw new PadError("PA029", "profile is required", $"instance `{inst.Id}`", "", ["指定 profile 名"]);

        var preflight = await PreflightPack(inst, prof, packPath, card, log, ct);
        var report = new PackInstallReport
        {
            PackId = preflight.PackId,
            Title = preflight.Title,
            Profile = prof,
        };
        report.Warnings.AddRange(preflight.Warnings);
        log?.Report($"[install] 预检完成：{preflight.Summary}");

        var risk = preflight.Plugins.Where(p => p.Status == PackPluginStatus.NeedsExemption).ToList();
        var missing = preflight.Plugins.Where(p => p.Status == PackPluginStatus.Missing).ToList();
        var blockers = DecisionBlockers(preflight, opts);

        if (opts.DryRun)
        {
            // 只回汇总，不落盘；exit code 由 CLI 按 dry-run 恒 0 处理。
            report.State = preflight.NeedsDecision && blockers.Count > 0 ? "needs-decision" : "dry-run";
            report.Ok = blockers.Count == 0;
            report.Warnings.AddRange(blockers);
            report.Text = blockers.Count > 0
                ? "预检（未安装，需要决定）：" + string.Join("；", blockers)
                : "预检（未安装）：" + preflight.Summary;
            log?.Report(report.Text);
            return report;
        }

        if (blockers.Count > 0)
        {
            report.State = "needs-decision";
            report.Ok = false;
            report.Warnings.AddRange(blockers);
            report.Text = "需要先决定：" + string.Join("；", blockers);
            log?.Report(report.Text);
            return report;
        }

        // 一次授权：批量写精确版本豁免（失败只记日志；真正装不上的会在 apply 里报 Failure）。
        if (opts.AcceptRisk && risk.Count > 0)
        {
            var fixes = risk
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.LatestVersion))
                .Select(p => ($"{p.Name}@{p.LatestVersion}", inst.Dsh.Version))
                .ToList();
            report.Exempted = AllowVersionsHook is null
                ? await _launcher.AllowVersions(inst, prof, fixes, log, ct)
                : await AllowVersionsHook(inst, prof, fixes, log, ct);
        }

        var skipSpecs = opts.SkipMissing
            ? missing.Select(p => p.Spec).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
            : [];
        // allow-version 已经做过一次 relink；应用阶段就不用再花 60–70s 重复一次。
        var healApply = report.Exempted.Count == 0;
        log?.Report($"[install] 开始应用：{preflight.Plugins.Count - skipSpecs.Count} 个插件"
                    + (skipSpecs.Count > 0 ? $"，跳过 {skipSpecs.Count} 个" : ""));
        var apply = ApplyHook is null
            ? await ApplyPackToInstance(inst, preflight.Path, prof, null, log, ct, skipSpecs, healApply)
            : await ApplyHook(inst, preflight.Path, skipSpecs, log, ct);

        report.Added = apply.Added;
        report.Kept = apply.Kept;
        report.BundlesRestored = apply.BundlesRestored;
        report.Skipped = apply.Skipped;
        report.Failures = apply.Failures;
        report.Warnings.AddRange(apply.Warnings);
        // 预检之后又冒出来的（并发/元数据不准）也当失败处理。
        foreach (var conflict in apply.Conflicts)
            report.Failures.Add(new PackPluginFailure
            {
                Spec = conflict.Spec, Required = true, Reason = conflict.Reason,
            });
        report.State = apply.State;
        if (report.State != PackLedger.StateIncomplete
            && (report.Skipped.Count > 0 || report.Failures.Any(f => f.Required)))
            report.State = PackLedger.StateIncomplete;
        report.Ok = report.State is PackLedger.StateInstalled or PackLedger.StateIncomplete;
        report.Text = BuildInstallText(report);
        log?.Report(report.Text);
        return report;
    }

    static string BuildInstallText(PackInstallReport report)
    {
        var state = report.State switch
        {
            PackLedger.StateInstalled => "installed",
            PackLedger.StateIncomplete => "incomplete（不完整，可修复）",
            _ => report.State,
        };
        return $"包 {report.Title} → profile {report.Profile}：{state} · 新增 {report.Added.Count}"
               + $" · 保留 {report.Kept.Count} · 豁免 {report.Exempted.Count}"
               + $" · 跳过 {report.Skipped.Count} · 失败 {report.Failures.Count(f => f.Required)}";
    }

    /// <summary>spec 钉了精确版本（foo@1.2.3 / @s/p@1.2.3）时返回版本号；range/latest/本地路径返回 null。</summary>
    static string? PinnedVersion(string spec)
    {
        var s = (spec ?? "").Trim();
        if (s.Length == 0) return null;
        var bare = LaunchPolicy.BundleBareName(s);
        if (bare.Length == 0 || string.Equals(bare, s, StringComparison.Ordinal)) return null;
        var ver = s[bare.Length..].TrimStart('@').Trim();
        if (ver.Length == 0 || !char.IsAsciiDigit(ver[0])) return null;   // ^ ~ > < = * x latest ...
        foreach (var ch in ver)
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '-' or '+')) return null;
        return ver;
    }

    static string QuoteSpec(string spec) =>
        spec.Contains(' ') || spec.Contains('\t') ? "\"" + spec.Replace("\"", "") + "\"" : spec;

    bool HasManagerBundle(Instance inst, string profile)
    {
        var facts = PackLedger.ReadProfile(inst, profile);
        var key = PackLedger.PluginKey(ManagerSpec);
        return facts.HasDependency(key) || facts.BundleName(key) is not null;
    }

    /// <summary>
    /// Q6：包内本地插件先复制到 <c>&lt;instance&gt;/home/local-plugins/&lt;packId&gt;/&lt;name&gt;/</c>
    /// 再以 <c>file:</c> 装；路径稳定、卸包可回滚。返回目标绝对路径。
    /// </summary>
    string PrepareLocalPlugin(Instance inst, string packPath, string packId, string local,
        IProgress<string>? log)
    {
        var rel = local.Trim().Replace('\\', '/').TrimStart('.', '/').TrimEnd('/');
        var name = Path.GetFileName(rel);
        if (name.Length == 0)
            throw new PadError("PA014", "pack local plugin dir is empty", local, null,
                ["检查 pack.json 的 dsh.plugins[].local"]);
        var target = Path.Combine(_launcher.InstanceDir(inst.Id), "home", "local-plugins", packId, name);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        var abs = Path.GetFullPath(packPath);
        if (abs.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZipSubtree(abs, rel, target);
        }
        else
        {
            var source = Path.Combine(Path.GetDirectoryName(abs)!,
                rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(source))
                throw new PadError("PA014", "pack local plugin dir is missing", source, null,
                    ["包没带这个 plugins/ 目录"]);
            CopyDir(source, target);
        }
        log?.Report($"本地插件 {name} → {target}");
        return target;
    }

    static void ExtractZipSubtree(string zipPath, string dirRel, string target)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var prefix = dirRel.Trim('/') + "/";
        var found = false;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/').TrimStart('.').TrimStart('/');
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = name[prefix.Length..];
            if (rel.Length == 0 || rel.EndsWith('/')) continue;
            var dest = Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!LaunchPolicy.PathUnder(dest, target)) continue;    // 防 zip 路径穿越
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            found = true;
        }
        if (!found)
            throw new PadError("PA014", "pack zip has no local plugin dir", dirRel, zipPath,
                ["检查 pack.json 的 dsh.plugins[].local"]);
    }

    static void CopyDir(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
    }

    /// <summary>file: 依赖在 profile dependencies 里的真实 key（dirname 与路径对不上时别猜）。</summary>
    static string? LocalDepKey(ProfileFacts facts, string absDir)
    {
        var want = SafeFullPath(absDir);
        if (want is null) return null;
        foreach (var dep in facts.Deps)
        {
            if (!dep.Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                && !dep.Value.StartsWith("link:", StringComparison.OrdinalIgnoreCase)) continue;
            var raw = dep.Value[(dep.Value.IndexOf(':') + 1)..].Trim().Trim('"');
            var got = SafeFullPath(raw);
            if (got is not null && string.Equals(got.TrimEnd('\\', '/'), want.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase)) return dep.Key;
        }
        return null;
    }

    static string? SafeFullPath(string? path)
    {
        var raw = (path ?? "").Trim().Trim('"');
        if (raw.Length == 0) return null;
        try { return Path.GetFullPath(raw); }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    /// <summary>
    /// 停用/启用一个包（§3.3 动作矩阵）：内容白名单跟着切，插件从 <c>dsh.profile.bundles</c>
    /// 摘掉/加回，dependencies 保留（可逆、不重下载）。缺依赖的插件不自动下载，列在
    /// <see cref="PackToggleResult.Missing"/>，包标 incomplete。
    /// </summary>
    public async Task<PackToggleResult> SetPackPluginsActive(Instance inst, string packId, string profile,
        bool active, IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inst);
        var prof = string.IsNullOrWhiteSpace(profile)
            ? PackLedger.DefaultProfile(_launcher, inst)
            : profile.Trim();
        var ledger = PackLedger.Load(_launcher, inst);
        if (ledger.Get(packId) is null)
            throw new PadError("PA012", "this pack is not in the instance ledger", packId,
                $"instances/{inst.Id}/pack-ledger.json", ["先装一次这个包（会登记账本）"]);
        var result = new PackToggleResult { PackId = packId, Profile = prof, Active = active };

        // 内容轴：allow/deny 是实例级（只改白名单，不删投影）。pack list 的行 id 是投影目录名
        // （npmName），账本里的 packId 是包文件 stem，两者不能混用。
        var contentRef = ProjectionIdOf(ledger.Get(packId)!);
        if (contentRef.Length == 0) contentRef = packId;
        try
        {
            if (active) await Allow(inst, contentRef, log, ct);
            else await Deny(inst, contentRef, log, ct);
        }
        catch (Exception ex) when (ex is PadError or IOException or UnauthorizedAccessException)
        {
            result.Warnings.Add((active ? "内容启用失败：" : "内容停用失败：") + PadError.Describe(ex));
        }

        // 插件轴：只动 bundles，dependencies 保留。
        var facts = PackLedger.ReadProfile(inst, prof);
        var keys = ledger.RecordedKeys(prof, packId);
        if (active)
        {
            var missing = keys.Where(k => !facts.HasDependency(k)).ToList();
            var present = keys.Where(k => facts.HasDependency(k)).ToList();
            var wanted = PackLedger.MergeBundles(facts.Bundles, present);
            if (!wanted.SequenceEqual(facts.Bundles, StringComparer.OrdinalIgnoreCase))
            {
                PackLedger.WriteBundles(inst, prof, wanted);
                result.BundlesAdded = present.Where(k => facts.BundleName(k) is null).ToList();
            }
            result.Missing = missing;
            if (missing.Count > 0)
                result.Warnings.Add("这些插件依赖不在 profile 里，没自动重装：" +
                                    string.Join("、", missing) + "（用「按包修复」）");
            result.State = missing.Count > 0 ? PackLedger.StateIncomplete : PackLedger.StateInstalled;
            ledger.SetContent(packId, allow: true);
        }
        else
        {
            var present = keys.Where(k => facts.BundleName(k) is not null).ToList();
            if (present.Count > 0)
            {
                PackLedger.WriteBundles(inst, prof, PackLedger.MergeBundles(facts.Bundles, [], present));
                result.BundlesRemoved = present;
            }
            var after = PackLedger.ReadProfile(inst, prof);
            result.Missing = keys.Where(k => !after.HasDependency(k)).ToList();
            result.State = result.Missing.Count > 0 ? PackLedger.StateIncomplete : PackLedger.StateDisabled;
            ledger.SetContent(packId, allow: false);
        }
        ledger.SetState(packId, prof, result.State);
        ledger.Save();
        log?.Report(result.Text);
        return result;
    }

    /// <summary>
    /// 卸载一个包（§3.3 动作矩阵）：删投影目录 + 只回滚 owners==1 的插件（共享的保留并写进
    /// <see cref="PackUninstallResult.SkippedShared"/>）+ 清本地插件目录 + 更新账本。
    /// 账本里没有的旧投影：只删目录，插件一律不动。
    /// </summary>
    public async Task<PackUninstallResult> UninstallPack(Instance inst, string packId, string profile,
        IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inst);
        var prof = string.IsNullOrWhiteSpace(profile)
            ? PackLedger.DefaultProfile(_launcher, inst)
            : profile.Trim();
        var ledger = PackLedger.Load(_launcher, inst);
        var result = new PackUninstallResult { PackId = packId, Profile = prof };

        var pack = ledger.Get(packId);
        if (pack is null)
        {
            var row = (await List(inst, ct)).FirstOrDefault(r =>
                string.Equals(r.Id, packId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Name, packId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SlugOf($"{r.Name}-{r.Version}"), SlugOf(packId),
                    StringComparison.OrdinalIgnoreCase));
            if (row is not null) result.ProjectionDeleted = DeleteProjection(inst, row.Dir, result);
            result.Warnings.Add("账本里没有这个包：只删了投影目录，插件没动（无法判断归属）");
            log?.Report(result.Text);
            return result;
        }

        result.SkippedShared = ledger.SharedFor(prof, packId);
        foreach (var key in ledger.ExclusiveFor(prof, packId))
        {
            try
            {
                await _launcher.PluginOp(inst.Id, "remove", key, log, ct, prof);
                result.RemovedPlugins.Add(key);
            }
            catch (Exception ex) when (ex is PadError or IOException)
            {
                result.Warnings.Add($"回滚插件 {key} 失败：{PadError.Describe(ex)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(pack.Content.Dir))
            result.ProjectionDeleted = DeleteProjection(inst,
                Path.Combine(inst.Workspace.Path,
                    pack.Content.Dir.Replace('/', Path.DirectorySeparatorChar)), result);

        var localRoot = Path.Combine(_launcher.InstanceDir(inst.Id), "local-plugins", packId);
        try
        {
            if (Directory.Exists(localRoot))
            {
                Directory.Delete(localRoot, recursive: true);
                result.LocalCleaned = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Warnings.Add("本地插件目录没删掉：" + ex.Message);
        }

        ledger.RemovePack(packId);
        ledger.Save();
        log?.Report(result.Text);
        return result;
    }

    bool DeleteProjection(Instance inst, string dir, PackUninstallResult result)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        var full = Path.GetFullPath(dir.Trim());
        var packRoot = Path.Combine(inst.Workspace.Path, ".agent-pack");
        if (!LaunchPolicy.PathUnder(full, packRoot))
        {
            result.Warnings.Add("投影目录不在 workspace/.agent-pack 下，没有删：" + full);
            return false;
        }
        try
        {
            if (!Directory.Exists(full)) return false;
            Directory.Delete(full, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Warnings.Add("投影目录没删掉：" + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 按包修复：照账本补缺失插件（缺依赖才 <c>dsh plugin add</c>；只缺 bundle 行就只写行），
    /// 本地插件目录丢了会先从包里复制回来（Q6）。版本偏差不算缺件，不静默升降级。
    /// </summary>
    public async Task<PackRepairResult> RepairPack(Instance inst, string packId, string profile,
        IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inst);
        var prof = string.IsNullOrWhiteSpace(profile)
            ? PackLedger.DefaultProfile(_launcher, inst)
            : profile.Trim();
        var ledger = PackLedger.Load(_launcher, inst);
        var pack = ledger.Get(packId)
            ?? throw new PadError("PA012", "this pack is not in the instance ledger", packId,
                $"instances/{inst.Id}/pack-ledger.json", ["先装一次这个包"]);
        var entry = ledger.ProfileOf(packId, prof)
            ?? throw new PadError("PA012", "this pack is not recorded for that profile", packId, prof,
                ["换一个 profile", "或先在这个 profile 里装这个包"]);
        var result = new PackRepairResult { PackId = packId, Profile = prof };
        var enabled = pack.Content.Allow;
        var gaps = ledger.GapsFor(prof, packId);

        foreach (var plugin in entry.Plugins.ToList())
        {
            var key = ledger.KeyOf(prof, plugin);
            if (key.Length == 0) continue;
            var target = plugin.Spec;
            var localPath = ledger.LocalDirPath(plugin);
            var localGap = gaps.Any(g => g.Kind == "local-dir" && Same(g.Spec, plugin.Spec));
            if (plugin.IsLocal && localPath is not null && (localGap || !Directory.Exists(localPath)))
            {
                var restored = TryRestoreLocalPlugin(inst, pack, packId, plugin, localPath, log);
                if (restored is null)
                {
                    result.StillMissing.Add(plugin.Spec);
                    result.Warnings.Add($"本地插件目录重建失败：{plugin.Spec}");
                    continue;
                }
                target = "file:" + restored;
            }
            var depGap = gaps.Any(g => g.Kind == "dependency" && Same(g.Spec, plugin.Spec));
            var bundleGap = gaps.Any(g => g.Kind == "bundle" && Same(g.Spec, plugin.Spec));
            if (depGap)
            {
                log?.Report($"按包修复：补装 {target}");
                await _launcher.AddBundles(inst, prof, [QuoteSpec(target)], log, ct);
                result.Added.Add(target);
            }
            else if (bundleGap && enabled)
            {
                var now = PackLedger.ReadProfile(inst, prof);
                if (now.BundleName(key) is null)
                {
                    PackLedger.WriteBundles(inst, prof,
                        PackLedger.MergeBundles(now.Bundles, [key]));
                    result.Added.Add(key);
                }
            }
        }

        result.StillMissing = ledger.MissingFor(prof, packId);
        result.State = result.StillMissing.Count > 0
            ? PackLedger.StateIncomplete
            : enabled ? PackLedger.StateInstalled : PackLedger.StateDisabled;
        ledger.SetState(packId, prof, result.State);
        ledger.Save();
        log?.Report(result.Text);
        return result;
    }

    string? TryRestoreLocalPlugin(Instance inst, LedgerPack pack, string packId, LedgerPlugin plugin,
        string? localPath, IProgress<string>? log)
    {
        var packPath = (pack.Path ?? "").Trim();
        if (packPath.Length == 0 || !File.Exists(packPath)) return null;
        var manifest = PackManifest.TryRead(packPath);
        if (manifest is null) return null;
        var name = localPath is null ? "" : Path.GetFileName(localPath.TrimEnd('\\', '/'));
        if (name.Length == 0) return null;
        var match = manifest.Plugins.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.Local)
            && string.Equals(Path.GetFileName(p.Local.TrimEnd('/', '\\')), name,
                StringComparison.OrdinalIgnoreCase));
        return match is null ? null : PrepareLocalPlugin(inst, packPath, packId, match.Local, log);
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

/// <summary>
/// 「装包到实例」的结构化结果（D105 R2/R6）：按 diff 只补缺的；版本冲突收集在
/// <see cref="Conflicts"/> 里，不静默覆盖。
/// </summary>
public sealed class PackApplyResult
{
    public string PackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public string Profile { get; set; } = "";
    public string State { get; set; } = "";
    /// <summary>投影目录名（= pack list 行 id），UI 拿它做 allow/deny 或定位投影。</summary>
    public string ProjectionId { get; set; } = "";
    public bool ManagerInstalled { get; set; }
    /// <summary>这次真的 <c>dsh plugin add</c> 了的 spec。</summary>
    public List<string> Added { get; set; } = [];
    /// <summary>依赖已在、只把 bundle 行补回来的插件 key（不重新下载）。</summary>
    public List<string> BundlesRestored { get; set; } = [];
    /// <summary>已存在且版本一致/未钉版本的插件 key（跳过 add）。</summary>
    public List<string> Kept { get; set; } = [];
    /// <summary>调用方要求跳过、因此没 add 的插件 spec（安装预检的 skip-missing）。</summary>
    public List<string> Skipped { get; set; } = [];
    public List<PackPluginConflict> Conflicts { get; set; } = [];
    /// <summary>这次没装上的插件（必选 = 包标 incomplete；可选 = 只提醒）。</summary>
    public List<PackPluginFailure> Failures { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public bool Ok => Conflicts.Count == 0 && Failures.All(f => !f.Required);

    public string Text => Ok
        ? $"包 {Title} 已应用到 profile {Profile}：新增 {Added.Count} 个插件、保留 {Kept.Count} 个、补 bundle 行 {BundlesRestored.Count} 个"
        : $"包 {Title} 有 {Conflicts.Count} 个版本冲突 / {Failures.Count(f => f.Required)} 个必选插件失败 / {Skipped.Count} 个按选择跳过，没有静默处理；包已标 incomplete";
}

/// <summary>一次装包时没装上的插件（必选/可选分开，可选不算包缺损）。</summary>
public sealed class PackPluginFailure
{
    public string Spec { get; set; } = "";
    public bool Required { get; set; } = true;
    public string Reason { get; set; } = "";
    public string Text => $"{(Required ? "必选" : "可选")}插件 {Spec}：{Reason}";
}

/// <summary>一个插件版本冲突（R6：包要的版本和实例里已有的不同）。</summary>
public sealed class PackPluginConflict
{
    public string Spec { get; set; } = "";
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Text => Reason.Length > 0 ? Reason : $"{Spec}: 包要 {Expected}，实例里是 {Actual}";
}

/// <summary>停用/启用包的结果（内容 allow + <c>dsh.profile.bundles</c> 联动）。</summary>
public sealed class PackToggleResult
{
    public string PackId { get; set; } = "";
    public string Profile { get; set; } = "";
    public bool Active { get; set; }
    public string State { get; set; } = "";
    public List<string> BundlesAdded { get; set; } = [];
    public List<string> BundlesRemoved { get; set; } = [];
    /// <summary>该包要求、但依赖已经不在 profile 里的插件（启用时不会自动下载）。</summary>
    public List<string> Missing { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string Text => Active
        ? Missing.Count == 0
            ? $"包 {PackId} 已在 profile {Profile} 重新启用（bundle 行 +{BundlesAdded.Count}，没有重新下载）"
            : $"包 {PackId} 已启用，但 {Missing.Count} 个插件依赖缺失（标 incomplete，可「按包修复」）"
        : $"包 {PackId} 已在 profile {Profile} 停用（摘掉 {BundlesRemoved.Count} 个 bundle 行，依赖保留）";
}

/// <summary>卸载包的结果：回滚了哪些独占插件、保留了哪些共享插件、删了什么。</summary>
public sealed class PackUninstallResult
{
    public string PackId { get; set; } = "";
    public string Profile { get; set; } = "";
    public List<string> RemovedPlugins { get; set; } = [];
    /// <summary>还被别的包要求、因此保留的插件 key（R4）。</summary>
    public List<string> SkippedShared { get; set; } = [];
    public bool ProjectionDeleted { get; set; }
    public bool LocalCleaned { get; set; }
    public List<string> Warnings { get; set; } = [];

    public string Text => $"包 {PackId} 已卸载：回滚插件 {RemovedPlugins.Count} 个、保留共享插件 {SkippedShared.Count} 个"
                          + (ProjectionDeleted ? "、投影已删" : "")
                          + (Warnings.Count > 0 ? $"、{Warnings.Count} 条提醒" : "");
}

/// <summary>按包修复的结果。</summary>
public sealed class PackRepairResult
{
    public string PackId { get; set; } = "";
    public string Profile { get; set; } = "";
    public string State { get; set; } = "";
    public List<string> Added { get; set; } = [];
    public List<string> StillMissing { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public string Text => StillMissing.Count == 0
        ? $"包 {PackId} 修复完成：补了 {Added.Count} 项，状态 {State}"
        : $"包 {PackId} 仍缺 {StillMissing.Count} 项：" + string.Join("、", StillMissing);
}

// ---- P2 install concept：预检 / 安装报告的数据形状（pad-ui task-12 共用）--------

/// <summary>预检里一个插件的状态值（字符串契约，JSON 原样输出）。</summary>
public static class PackPluginStatus
{
    public const string Ok = "ok";
    public const string NeedsExemption = "needs-exemption";
    public const string Missing = "missing";
    public const string Unknown = "unknown";
    public const string VersionConflict = "version-conflict";
}

/// <summary>预检结果里的一行插件。</summary>
public sealed class PreflightPlugin
{
    /// <summary>包里的声明原文（npm spec / file: 路径）。</summary>
    public string Spec { get; set; } = "";
    /// <summary>包名（owners/dependencies 用的 key）。</summary>
    public string Name { get; set; } = "";
    /// <summary>包声明的版本选择器（<c>^0.79.0</c> / <c>1.2.3</c>）；裸名为空。</summary>
    public string Requested { get; set; } = "";
    /// <summary>npm 会解析到的版本 / 本地插件 package.json 的 version。</summary>
    public string LatestVersion { get; set; } = "";
    /// <summary>profile 里已装的版本；没装为空。</summary>
    public string InstalledVersion { get; set; } = "";
    /// <summary>ok | needs-exemption | missing | unknown | version-conflict</summary>
    public string Status { get; set; } = PackPluginStatus.Ok;
    /// <summary>needs-exemption 时的要求文本（engines.dsh / peerDependencies）。</summary>
    public string Requirement { get; set; } = "";
    public bool Required { get; set; } = true;
    /// <summary>包内本地插件（Q6）：不查 npm，读包内 package.json。</summary>
    public bool IsLocal { get; set; }

    public bool IsOk => Status == PackPluginStatus.Ok;
    public bool NeedsExemption => Status == PackPluginStatus.NeedsExemption;
}

/// <summary>安装前预检结果：一次列清内核、缺失、需授权、版本冲突（原型 03 左图的数据）。</summary>
public sealed class PackPreflight
{
    public string PackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public string Profile { get; set; } = "";
    public string PackKernel { get; set; } = "";
    public string InstanceKernel { get; set; } = "";
    public bool KernelMatches { get; set; } = true;
    public bool ManagerInstalled { get; set; }
    public bool AlreadyInstalled { get; set; }
    /// <summary>读时对账后的账本状态（installed / disabled / incomplete / absent）。</summary>
    public string LedgerState { get; set; } = PackLedger.StateAbsent;
    public List<PreflightPlugin> Plugins { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public int OkCount { get; set; }
    public int ExemptionCount { get; set; }
    public int MissingCount { get; set; }
    public int ConflictCount { get; set; }
    /// <summary>元数据查不到、兼容性未验证的插件数（不阻塞安装）。</summary>
    public int UnknownCount { get; set; }
    /// <summary>= 需授权 / 缺包 / 版本冲突任一存在：需要用户先决定。</summary>
    public bool NeedsDecision { get; set; }
    /// <summary>不带任何额外选项就能直接装（= !NeedsDecision）。</summary>
    public bool CanInstall { get; set; } = true;
    public string Summary { get; set; } = "";

    public string Text => Summary;
}

/// <summary>安装选项（界面上的两个勾选 + dry-run 预演）。</summary>
public sealed class PackInstallOptions
{
    /// <summary>接受 needs-exemption 插件的兼容风险：先批量写 DSH allow-version 豁免。</summary>
    public bool AcceptRisk { get; set; }
    /// <summary>跳过 npm 上找不到的插件（包标 incomplete，不阻断）。</summary>
    public bool SkipMissing { get; set; }
    /// <summary>只预检、不落盘。</summary>
    public bool DryRun { get; set; }
}

/// <summary>安装报告（原型 03 右图的数据）：装了什么、豁免了什么、跳过了什么、什么失败了。</summary>
public sealed class PackInstallReport
{
    public string PackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Profile { get; set; } = "";
    /// <summary>installed | incomplete | needs-decision | dry-run</summary>
    public string State { get; set; } = "";
    public List<string> Added { get; set; } = [];
    public List<string> Kept { get; set; } = [];
    public List<string> BundlesRestored { get; set; } = [];
    /// <summary>实际写成功的 allow-version 豁免（<c>name@version</c>）。</summary>
    public List<string> Exempted { get; set; } = [];
    /// <summary>按用户选择跳过的插件 spec。</summary>
    public List<string> Skipped { get; set; } = [];
    public List<PackPluginFailure> Failures { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool Ok { get; set; }
    public string Text { get; set; } = "";
}
