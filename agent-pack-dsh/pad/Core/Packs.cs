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

    string? RuntimeDir()
    {
        try
        {
            var dir = Path.Combine(ExeDir(), "pack-runtime");
            if (File.Exists(Path.Combine(dir, "packagent.bundle.js"))
                && File.Exists(Path.Combine(dir, "bun.exe"))
                && File.Exists(Path.Combine(dir, "pack-index.exe")))
                return dir;
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
