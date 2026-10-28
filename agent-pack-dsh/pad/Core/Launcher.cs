using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// The on-disk launcher root — the counterpart of <c>.minecraft</c>.
/// <code>
/// &lt;root&gt;/versions/&lt;dsh-version&gt;/     pinned @deepseek-ai/dsh
/// &lt;root&gt;/instances/&lt;id&gt;/home/       one DSH_HOME
/// &lt;root&gt;/instances/&lt;id&gt;/workspace/  default project dir
/// &lt;root&gt;/runtime.json                 live processes
/// &lt;root&gt;/pad.json                     PAD preferences
/// </code>
/// </summary>
public sealed class Launcher
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Root { get; }

    public Launcher(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Path.Combine(Root, "versions"));
        Directory.CreateDirectory(Path.Combine(Root, "instances"));
    }

    public static Launcher Default()
    {
        var env = Environment.GetEnvironmentVariable("PACK_LAUNCHER_ROOT");
        if (!string.IsNullOrWhiteSpace(env)) return new Launcher(env);
        var here = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(here)) here = AppContext.BaseDirectory;
        return new Launcher(Path.Combine(here, ".pack-launcher"));
    }

    public string VersionsDir => Path.Combine(Root, "versions");
    public string InstancesDir => Path.Combine(Root, "instances");
    public string VersionDir(string version) => Path.Combine(VersionsDir, version);
    public string InstanceDir(string id) => Path.Combine(InstancesDir, id);

    /// <summary>The pinned release's CLI entry. Runs under node, as DSH ships it.</summary>
    public string DshBin(string version)
    {
        var store = Path.Combine(VersionDir(version), "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        var rec = ReadJson<DshRelease>(Path.Combine(VersionDir(version), "version.json"));
        if (!string.IsNullOrWhiteSpace(rec?.Bin)
            && File.Exists(rec.Bin)
            && LaunchPolicy.PathUnder(rec.Bin, VersionDir(version)))
            return rec.Bin;
        return store;
    }

    static string NowIso() => DateTimeOffset.UtcNow.ToString("o");

    static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    static T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)); }
        catch { return null; }
    }

    // ---- version store ----------------------------------------------------

    public List<DshRelease> Releases()
    {
        var list = new List<DshRelease>();
        if (!Directory.Exists(VersionsDir)) return list;
        foreach (var dir in Directory.EnumerateDirectories(VersionsDir))
        {
            var rec = ReadJson<DshRelease>(Path.Combine(dir, "version.json"));
            if (rec is not null) list.Add(rec);
        }
        list.Sort((a, b) => string.CompareOrdinal(b.Version, a.Version));
        return list;
    }

    public void SaveRelease(DshRelease rec) =>
        WriteAtomic(Path.Combine(VersionDir(rec.Version), "version.json"), JsonSerializer.Serialize(rec, Json));

    /// <summary>pnpm network concurrency / store dir from pad.json, or nothing when
    /// the user left the defaults. An empty store dir keeps pnpm's global store.</summary>
    Dictionary<string, string> PnpmEnv()
    {
        var env = new Dictionary<string, string>();
        var d = Settings().Download;
        if (d.PnpmNetworkConcurrency > 0)
            env["npm_config_network_concurrency"] = d.PnpmNetworkConcurrency.ToString();
        if (!string.IsNullOrWhiteSpace(d.CacheDir))
        {
            var store = Path.IsPathRooted(d.CacheDir)
                ? d.CacheDir
                : Path.GetFullPath(Path.Combine(Root, d.CacheDir));
            env["npm_config_store_dir"] = store;
            env["PNPM_STORE_DIR"] = store;
        }
        return env;
    }

    void AddPnpmEnv(Dictionary<string, string> env)
    {
        foreach (var (k, v) in PnpmEnv()) env[k] = v;
    }

    /// <summary>
    /// Install a pinned release with pnpm, exactly how DSH expects to be installed.
    /// Returns the verify output so the caller can show what the release reported.
    /// </summary>
    public async Task<DshRelease> InstallRelease(string version, IProgress<string>? log, CancellationToken ct)
    {
        var job = StartJob("install-version", null, InstallLeaves(), version);
        var token = TrackCancel(job.Id, ct);
        log = TeeJob(job.Id, log);
        try
        {
            var dir = VersionDir(version);
            Directory.CreateDirectory(dir);
            var pkg = Path.Combine(dir, "package.json");
            // Never rewrite an existing manifest: it holds the dependency pnpm resolved,
            // and blanking it turns a re-run into a full reinstall.
            if (!File.Exists(pkg))
            {
                WriteAtomic(pkg, JsonSerializer.Serialize(new { name = $"dsh-pin-{version}", @private = true }, Json));
            }
            WriteAtomic(Path.Combine(dir, ".npmrc"),
                "ignore-workspace-root-check=true\nstrict-dep-builds=false\ndangerously-allow-all-builds=true\n");
            UpdateLeaf(job.Id, "resolve-meta", 5, 5);

            // No --store-dir by default: pnpm's own global store is already warm on the
            // user's machine; only an explicit cacheDir routes it to the launcher's store.
            // pnpm >=10 默认拦依赖的 build scripts（node-pty/koffi 等原生包会装坏）；
            // 显式放开，否则 pnpm 以 ERR_PNPM_IGNORED_BUILDS 退出、PAD 误判安装失败。
            await Pnpm.EnsureAsync(log, token);
            log?.Report($"pnpm add @deepseek-ai/dsh@{version}");
            var add = await Proc.Run("pnpm",
                $"add @deepseek-ai/dsh@{version} --config.strictDepBuilds=false --config.dangerouslyAllowAllBuilds=true",
                dir, log, token, PnpmEnv());
            if (token.IsCancellationRequested)
            {
                FinishJob(job.Id, "cancelled");
                throw new OperationCanceledException();
            }
            if (add.ExitCode != 0)
            {
                var err = add.Tail
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(l => l.Contains("ERR_", StringComparison.OrdinalIgnoreCase))
                    ?? add.Tail.Trim();
                throw new PadError("PA010", "pnpm add 失败" + (err.Length > 0 ? "：" + err : ""),
                    $"versions/{version}", add.Tail, ["检查网络与 registry", "确认 pnpm 可用"]);
            }
            UpdateLeaf(job.Id, "download", 80, 80);

            var rec = new DshRelease { Version = version, InstalledAt = NowIso() };
            var bin = DshBin(version);
            if (!File.Exists(bin))
                throw new PadError("PA001", "release installed but the CLI entry is missing",
                    $"versions/{version}", bin, ["重装该发行号"]);

            log?.Report("node bin.js --version");
            var probe = await Proc.Run("node", $"\"{bin}\" --version", dir, null, token);
            UpdateLeaf(job.Id, "verify", 10, 10);
            rec.Verified = probe.ExitCode == 0;
            rec.VerifyOutput = probe.Tail.Trim();
            rec.EnginesNode = ParseEnginesNode(Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "package.json"));
            SaveRelease(rec);
            UpdateLeaf(job.Id, "record", 5, 5);
            FinishJob(job.Id, "done");
            return rec;
        }
        catch
        {
            FinishJob(job.Id, "failed");
            throw;
        }
    }

    public void RemoveRelease(string version)
    {
        var pinned = Instances().FirstOrDefault(i => i.Dsh.Version == version);
        if (pinned is not null)
            throw new PadError("PA006", "cannot remove a release still pinned by an instance",
                $"versions/{version}", $"instance `{pinned.Id}`", ["先改那个实例的发行号，或删掉它"]);
        var dir = VersionDir(version);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ---- instances --------------------------------------------------------

    public List<Instance> Instances()
    {
        var list = new List<Instance>();
        if (!Directory.Exists(InstancesDir)) return list;
        foreach (var dir in Directory.EnumerateDirectories(InstancesDir))
        {
            var rec = ReadJson<Instance>(Path.Combine(dir, "instance.json"));
            if (rec is null) continue;
            if (RelocateIfNeeded(rec, dir)) SaveInstance(rec);
            list.Add(rec);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public Instance? Instance(string id)
    {
        var dir = InstanceDir(id);
        var rec = ReadJson<Instance>(Path.Combine(dir, "instance.json"));
        if (rec is null) return null;
        if (RelocateIfNeeded(rec, dir)) SaveInstance(rec);
        return rec;
    }

    bool RelocateIfNeeded(Instance inst, string dir)
    {
        var homeExists = Directory.Exists(Path.Combine(dir, "home"));
        var wsExists = Directory.Exists(Path.Combine(dir, "workspace"));
        if (!LaunchPolicy.RelocateOwned(inst.Adopted, inst.Home, inst.Workspace.Kind,
                inst.Workspace.Path, dir, homeExists, wsExists, out var home, out var ws))
            return false;
        inst.Home = home;
        inst.Workspace.Path = ws;
        var sessions = Path.Combine(inst.Home, "sessions").Replace("\\", "\\\\");
        WriteAtomic(Path.Combine(inst.Home, "launcher.patch.yml"),
            $"- id: session-persistence-jsonl\n  config:\n    root: '{sessions}'\n");
        return true;
    }

    public void SaveInstance(Instance inst)
    {
        inst.Updated = NowIso();
        WriteAtomic(Path.Combine(InstanceDir(inst.Id), "instance.json"), JsonSerializer.Serialize(inst, Json));
        RewriteLaunchScripts(inst);
    }

    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>
    /// Make an instance for a pinned release. <paramref name="profile"/> is the instance's
    /// default profile (what the launch button picks before anyone touches the selector);
    /// blank keeps the old behaviour — follow the global <c>pad.json</c> choice.
    /// </summary>
    public Instance CreateInstance(string name, string version, string? profile = null)
    {
        var id = Slug(name);
        if (id.Length == 0)
            throw new PadError("PA022", "instance name has no usable ascii characters",
                "instance", name, ["用字母或数字给它起个名"]);
        var dir = InstanceDir(id);
        if (File.Exists(Path.Combine(dir, "instance.json")))
            throw new PadError("PA023", "an instance with this id already exists",
                $"instances/{id}", id, ["换个名字"]);
        if (!File.Exists(DshBin(version)))
            throw new PadError("PA001", "that release is not installed",
                $"versions/{version}", version, ["先在下载页装这个发行号"]);

        var home = Path.Combine(dir, "home");
        Directory.CreateDirectory(Path.Combine(home, "sessions"));
        Directory.CreateDirectory(LogsDir(id));

        var s = Settings();
        InstanceWorkspace ws;
        if (s.Launch.WorkspaceIndieDefault == "shared")
        {
            var shared = string.IsNullOrWhiteSpace(s.Launch.SharedWorkspace)
                ? Path.Combine(Root, "workspace-shared")
                : Path.GetFullPath(s.Launch.SharedWorkspace);
            Directory.CreateDirectory(shared);
            ws = new InstanceWorkspace { Kind = "existing", Path = shared };
        }
        else
        {
            var workspace = Path.Combine(dir, "workspace");
            Directory.CreateDirectory(workspace);
            ws = new InstanceWorkspace { Kind = "owned", Path = workspace };
        }

        // Keep every session inside this instance's own home, so two instances
        // never write the same transcript directory.
        var sessions = Path.Combine(home, "sessions").Replace("\\", "\\\\");
        WriteAtomic(Path.Combine(home, "launcher.patch.yml"),
            $"- id: session-persistence-jsonl\n  config:\n    root: '{sessions}'\n");

        var inst = new Core.Instance
        {
            Id = id,
            Name = name.Trim(),
            Dsh = new InstanceDsh { Version = version },
            Home = home,
            Workspace = ws,
            LastProfile = string.IsNullOrWhiteSpace(profile) ? null : profile.Trim(),
            Created = NowIso(),
            Updated = NowIso(),
        };
        SaveInstance(inst);
        CredentialsGate.CopyDistributorToHome(this, inst, s);
        LlmProviders.ApplyToHome(home, LoadLlmProviders());
        return inst;
    }

    /// <summary>
    /// Build an instance out of a pack: pin the kernel it declares, remember where it came
    /// from, then hand the whole projection + allow chain to TypeScript. Projection failing
    /// leaves the instance on disk with its source already written — the caller shows the
    /// original error instead of deleting the user's half-built instance.
    /// </summary>
    public async Task<Instance> CreateFromPack(PackCard card, string? name,
        IProgress<string>? log, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrWhiteSpace(card.Path))
            throw new PadError("PA012", "pack has no path", string.IsNullOrWhiteSpace(card.Id) ? "pack" : card.Id,
                null, ["pad cli pack list"]);

        // `pack project` now reads both manifests: JSON packs directly, `.pack.zip` by
        // unpacking first (TS pack-ops). `.pinst.zip` is an instance export — building an
        // instance from it belongs to the import chain, so refuse that before anything is
        // written instead of letting it die as a JSON parse error later.
        if (!IsProjectable(card))
            throw new PadError("PA012", "this pack must be imported, not projected", card.Path,
                string.IsNullOrWhiteSpace(card.Kind) ? "not a pack" : $"kind {card.Kind}",
                ["pad cli import \"" + card.Path + "\"", "pinst / 实例导出包走「导入整合包」入口"]);

        var kernel = (card.Kernel ?? "").Trim();
        if (kernel.Length == 0)
            throw new PadError("PA012", "pack is missing dsh.version", card.Path,
                "包里没有内核版本，无法建实例",
                ["给 pack.json 补 dsh.version", "pad cli pack list"]);

        // A kernel that is not in the version store is CreateInstance's own PA001 (same
        // error the download page throws) — reuse it instead of inventing a second answer.
        var displayName = !string.IsNullOrWhiteSpace(name) ? name!
            : !string.IsNullOrWhiteSpace(card.Title) ? card.Title
            : card.Id;
        var inst = CreateInstance(displayName, kernel, card.Profile);
        inst.Source = new InstanceSource
        {
            Kind = "pack",
            PackId = card.Id,
            PackTitle = card.Title,
            PackVersion = card.Version,
            PackPath = card.Path,
        };
        SaveInstance(inst);
        await new Packs(this).Project(inst, card.Path, log, ct);
        return inst;
    }

    /// <summary>
    /// <c>pack project</c> takes <c>.pack.json</c> and <c>.pack.zip</c> (the TS side unpacks
    /// the zip itself). <c>.pinst.zip</c> is an instance export and goes through import.
    /// A catalog row with no <see cref="PackCard.Kind"/> (older payloads) falls back to the
    /// file extension instead of refusing on a missing field.
    /// </summary>
    static bool IsProjectable(PackCard card) =>
        string.Equals(card.Kind, "pack.json", StringComparison.OrdinalIgnoreCase)
        || string.Equals(card.Kind, "pack.zip", StringComparison.OrdinalIgnoreCase)
        || (string.IsNullOrWhiteSpace(card.Kind)
            && string.Equals(Path.GetExtension(card.Path), ".json", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// PCL version-isolation analog for the workspace. <c>$DSH_HOME</c> stays
    /// isolated. Switching retargets the pointer; files are not copied.
    /// </summary>
    public Instance SetWorkspaceIndie(string id, bool indie, string? sharePath = null)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        if (indie)
            return SetWorkspacePath(id, Path.Combine(InstanceDir(id), "workspace"));
        if (string.IsNullOrWhiteSpace(sharePath))
            throw new PadError("PA034", "share path missing", $"instances/{id}", id,
                ["pad cli instance workspace share <id> <dir>"]);
        return SetWorkspacePath(id, sharePath);
    }

    /// <summary>Set this instance's project directory. The default
    /// <c>instances/id/workspace</c> is owned isolation; any other folder is shared.</summary>
    public Instance SetWorkspacePath(string id, string path)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        path = Path.GetFullPath((path ?? "").Trim().Trim('"'));
        if (path.Length == 0)
            throw new PadError("PA034", "workspace path missing", $"instances/{id}", id,
                ["填一个目录或点浏览"]);
        Directory.CreateDirectory(path);
        var owned = Path.Combine(InstanceDir(id), "workspace");
        var indie = LaunchPolicy.SamePath(path, owned);
        inst.Workspace = new InstanceWorkspace { Kind = indie ? "owned" : "existing", Path = path };
        SaveInstance(inst);
        RewriteLaunchScripts(inst);
        return inst;
    }

    /// <summary>Existing <c>launch-*.cmd</c> beside the instance, the double-clickable truth.</summary>
    public IReadOnlyList<string> LaunchScripts(Instance inst)
    {
        var dir = InstanceDir(inst.Id);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "launch-*.cmd")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Instance SetNote(string id, string? note)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        inst.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        SaveInstance(inst);
        return inst;
    }

    public void RemoveInstance(string id)
    {
        var dir = InstanceDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    public string LogsDir(string id) => Path.Combine(InstanceDir(id), "logs");

    static T? Dup<T>(T? src) where T : class
    {
        if (src is null) return null;
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(src, Json), Json);
    }

    /// <summary>
    /// <c>launch-*.cmd</c> is the double-clickable truth. After <c>pad.json</c> or
    /// <c>instance.json</c> launch knobs change, rewrite every script that already exists.
    /// </summary>
    public void RewriteLaunchScripts(Instance? only = null)
    {
        var settings = Settings();
        var runner = new Runner(this);
        foreach (var inst in only is null ? Instances() : (IEnumerable<Instance>)[only])
        {
            var dir = InstanceDir(inst.Id);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "launch-*.cmd"))
            {
                var file = Path.GetFileNameWithoutExtension(path);
                if (file.Length <= 7 || !file.StartsWith("launch-", StringComparison.Ordinal)) continue;
                var profile = file["launch-".Length..];
                try { runner.WriteScript(inst, profile, settings, heal: false); }
                catch (PadError) { /* no node yet; next launch will write */ }
                catch (Exception) { /* best effort: a broken script must not break the caller (D22) */ }
            }
        }
    }

    // ---- instance ops (clone / rename / display / pin) -------------------

    /// <summary>Clone an instance: copy home and launch overlay. Workspace stays
    /// on the original path so old session header.cwd still attaches (PA007).</summary>
    public Instance CloneInstance(string id, string newName)
    {
        var src = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var job = StartJob("clone", id);
        AppendJobLog(job.Id, $"clone {id} → {newName}");
        try
        {
            var newId = Slug(newName);
            var destDir = InstanceDir(newId);
            if (File.Exists(Path.Combine(destDir, "instance.json")))
                throw new PadError("PA023", "an instance with this id already exists", $"instances/{newId}", newId,
                    ["换个名字"]);

            var home = Path.Combine(destDir, "home");
            Directory.CreateDirectory(Path.Combine(destDir, "logs"));
            Directory.CreateDirectory(home);
            if (Directory.Exists(src.Home))
                CopyTree(src.Home, home);

            // Rewrite the session patch to point at the cloned home's sessions dir.
            var sessions = Path.Combine(home, "sessions").Replace("\\", "\\\\");
            WriteAtomic(Path.Combine(home, "launcher.patch.yml"),
                $"- id: session-persistence-jsonl\n  config:\n    root: '{sessions}'\n");

            var rec = new Instance
            {
                Id = newId,
                Name = newName.Trim(),
                Dsh = new InstanceDsh { Version = src.Dsh.Version },
                Home = Path.GetFullPath(home),
                Workspace = new InstanceWorkspace { Kind = "existing", Path = src.Workspace.Path },
                Status = "ready",
                LastProfile = src.LastProfile,
                Note = src.Note,
                Display = Dup(src.Display),
                Launch = Dup(src.Launch),
                Created = NowIso(),
                Updated = NowIso(),
            };
            SaveInstance(rec);
            FinishJob(job.Id, "done");
            return Instance(newId)!;
        }
        catch
        {
            FinishJob(job.Id, "failed");
            throw;
        }
    }

    /// <summary>Rename: change the display name without moving directories.</summary>
    public Instance RenameInstance(string id, string newName)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        inst.Name = newName.Trim();
        SaveInstance(inst);
        return inst;
    }

    /// <summary>Set star / category / logo / info / intro on an instance.</summary>
    public Instance SetDisplay(string id, bool? star = null, string? category = null,
        string? logo = null, string? info = null, string? intro = null)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        inst.Display ??= new InstanceDisplay();
        if (star is { } s) inst.Display.Star = s;
        if (category is not null) inst.Display.Category = category;
        if (logo is not null) inst.Display.Logo = string.IsNullOrWhiteSpace(logo) ? null : logo;
        if (info is not null) inst.Display.Info = info;
        if (intro is not null) inst.Display.Intro = intro;
        SaveInstance(inst);
        return inst;
    }

    public string ReadmeFile(string id) => Path.Combine(InstanceDir(id), "readme.md");

    public string ReadReadme(string id)
    {
        var p = ReadmeFile(id);
        return File.Exists(p) ? File.ReadAllText(p) : "";
    }

    public void WriteReadme(string id, string text)
    {
        _ = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var p = ReadmeFile(id);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (File.Exists(p)) File.Delete(p);
            return;
        }
        File.WriteAllText(p, text.Replace("\r\n", "\n"));
    }

    /// <summary>Pin a release for an instance. The release must already be installed.</summary>
    public Instance PinInstance(string id, string version)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        if (!Releases().Any(r => r.Version == version))
            throw new PadError("PA001", "that release is not installed", $"versions/{version}", version,
                ["pad cli release install " + version]);
        inst.Dsh.Version = version;
        SaveInstance(inst);
        return inst;
    }

    /// <summary>Export an instance as a .pinst.zip. Strips .credentials.yaml.</summary>
    public string ExportInstance(string id, string? outPath = null)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var dir = InstanceDir(id);
        outPath ??= Path.Combine(dir, $"{inst.Id}.pinst.zip");
        var tmpDir = Path.Combine(Path.GetTempPath(), $"pinst-{inst.Id}-{Environment.ProcessId}");
        if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        Directory.CreateDirectory(tmpDir);

        // Copy instance.json
        File.Copy(Path.Combine(dir, "instance.json"), Path.Combine(tmpDir, "instance.json"), overwrite: true);

        // Copy home, stripping credentials
        var homeDest = Path.Combine(tmpDir, "home");
        if (Directory.Exists(inst.Home))
        {
            CopyTree(inst.Home, homeDest);
            var cred = Path.Combine(homeDest, ".credentials.yaml");
            if (File.Exists(cred)) File.Delete(cred);
        }

        // Copy workspace .agent-pack whitelist
        var apDir = Path.Combine(inst.Workspace.Path, ".agent-pack");
        if (Directory.Exists(apDir))
            CopyTree(apDir, Path.Combine(tmpDir, "workspace", ".agent-pack"));

        // Write manifest
        var manifest = new { schema = "pack-agent.pinst/v1", id = inst.Id, name = inst.Name };
        File.WriteAllText(Path.Combine(tmpDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, Json), new UTF8Encoding(false));

        // Zip
        if (File.Exists(outPath)) File.Delete(outPath);
        System.IO.Compression.ZipFile.CreateFromDirectory(tmpDir, outPath);
        Directory.Delete(tmpDir, recursive: true);
        return outPath;
    }

    // ---- session ops (delete / backup) -----------------------------------

    /// <summary>Sessions root for an instance.</summary>
    string SessionsRoot(Instance inst) => Path.Combine(inst.Home, "sessions");

    /// <summary>Find a session directory by sid (or encoded segment).</summary>
    string? FindSessionDir(Instance inst, string sid)
    {
        var root = SessionsRoot(inst);
        if (!Directory.Exists(root)) return null;
        foreach (var projDir in Directory.EnumerateDirectories(root))
        {
            foreach (var sidDir in Directory.EnumerateDirectories(projDir))
            {
                var name = Path.GetFileName(sidDir);
                if (name.Equals(sid, StringComparison.OrdinalIgnoreCase)) return sidDir;
                // Also match by session id inside the jsonl header
                var log = Path.Combine(sidDir, "session.jsonl");
                if (File.Exists(log))
                {
                    foreach (var line in File.ReadLines(log))
                    {
                        if (line.Length == 0) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            if (doc.RootElement.TryGetProperty("id", out var idEl)
                                && idEl.GetString() is { } hid
                                && hid.Equals(sid, StringComparison.OrdinalIgnoreCase))
                                return sidDir;
                        }
                        catch { break; }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>Delete one session directory.</summary>
    public void DeleteSession(string id, string sid)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var dir = FindSessionDir(inst, sid)
            ?? throw new PadError("PA007", $"session `{sid}` not found", $"instances/{id}/sessions", sid,
                ["确认 session id"]);
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>Backup sessions (all or one) to instances/&lt;id&gt;/backups/.</summary>
    public string BackupSessions(string id, string? sid = null)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var dest = Path.Combine(InstanceDir(id), "backups", $"sessions-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(dest);

        if (sid is not null)
        {
            var src = FindSessionDir(inst, sid);
            if (src is null)
                throw new PadError("PA007", $"session `{sid}` not found", $"instances/{id}/sessions", sid,
                    ["确认 session id"]);
            var projName = Path.GetFileName(Path.GetDirectoryName(src)!);
            Directory.CreateDirectory(Path.Combine(dest, "sessions", projName));
            CopyTree(src, Path.Combine(dest, "sessions", projName, Path.GetFileName(src)));
        }
        else if (Directory.Exists(SessionsRoot(inst)))
        {
            CopyTree(SessionsRoot(inst), Path.Combine(dest, "sessions"));
        }

        var att = Path.Combine(inst.Home, "attachments");
        if (Directory.Exists(att))
            CopyTree(att, Path.Combine(dest, "attachments"));

        return dest;
    }

    // ---- agent-preset roster ---------------------------------------------

    static readonly Regex PresetIdRe = new(@"^[a-z0-9][a-z0-9-]*$", RegexOptions.Compiled);
    const string PresetCompositionFile = "agent.cordis.yml";
    const string UserPresetDir = ".agent-presets";

    /// <summary>Shipped presets live next to the DSH binary's config dir.</summary>
    string ShippedPresetRoot(string version)
    {
        var bin = DshBin(version);
        var dir = Path.GetDirectoryName(bin)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "config", "agent-presets"));
    }

    /// <summary>List agent-presets: user layer first, shipped overrides by id.</summary>
    public List<AgentPresetRow> ListAgentPresets(string id)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var byId = new Dictionary<string, AgentPresetRow>();

        // User layer first
        var userRoot = Path.Combine(inst.Home, UserPresetDir);
        if (Directory.Exists(userRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(userRoot))
            {
                var name = Path.GetFileName(dir);
                if (!PresetIdRe.IsMatch(name)) continue;
                var comp = Path.Combine(dir, PresetCompositionFile);
                var row = new AgentPresetRow { Id = name, Trust = PresetTrust.User, Path = comp };
                if (!File.Exists(comp)) row.Broken = $"missing {PresetCompositionFile}";
                byId[name] = row;
            }
        }

        // Shipped layer (overridden by user)
        var shippedRoot = ShippedPresetRoot(inst.Dsh.Version);
        if (Directory.Exists(shippedRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(shippedRoot))
            {
                var name = Path.GetFileName(dir);
                if (!PresetIdRe.IsMatch(name)) continue;
                if (byId.ContainsKey(name)) continue;
                var comp = Path.Combine(dir, PresetCompositionFile);
                var row = new AgentPresetRow { Id = name, Trust = PresetTrust.System, Path = comp };
                if (!File.Exists(comp)) row.Broken = $"missing {PresetCompositionFile}";
                byId[name] = row;
            }
        }

        return byId.Values.OrderBy(r => r.Id).ToList();
    }

    /// <summary>Copy a preset to the user layer.</summary>
    public AgentPresetRow CopyAgentPreset(string id, string fromId, string toId)
    {
        if (!PresetIdRe.IsMatch(toId))
            throw new PadError("PA113", $"agent-preset id `{toId}` is not a legal directory name",
                $"instance `{id}` / agent-preset", toId, ["use a lowercase id matching [a-z0-9][a-z0-9-]*"]);

        var presets = ListAgentPresets(id);
        if (presets.Any(p => p.Id == toId))
            throw new PadError("PA113", $"agent-preset `{toId}` already exists",
                $"instance `{id}` / agent-preset", toId, ["remove the user-layer preset first", "choose another id"]);

        var from = presets.FirstOrDefault(p => p.Id == fromId)
            ?? throw new PadError("PA113", $"agent-preset `{fromId}` is not on this instance roster",
                $"instance `{id}` / agent-preset", fromId, ["pad cli agent-preset list " + id]);

        var inst = Instance(id)!;
        var srcDir = Path.GetDirectoryName(from.Path)!;
        if (!Directory.Exists(srcDir))
            throw new PadError("PA113", $"agent-preset `{fromId}` directory is missing",
                $"instance `{id}` / agent-preset");

        var destDir = Path.Combine(inst.Home, UserPresetDir, toId);
        Directory.CreateDirectory(Path.Combine(inst.Home, UserPresetDir));
        CopyTree(srcDir, destDir);
        return new AgentPresetRow { Id = toId, Trust = PresetTrust.User, Path = Path.Combine(destDir, PresetCompositionFile) };
    }

    /// <summary>Remove a user-layer preset. Shipped presets cannot be removed.</summary>
    public void RemoveAgentPreset(string id, string presetId)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var presets = ListAgentPresets(id);
        var row = presets.FirstOrDefault(p => p.Id == presetId)
            ?? throw new PadError("PA113", $"agent-preset `{presetId}` is not on this instance roster",
                $"instance `{id}` / agent-preset", presetId, ["pad cli agent-preset list " + id]);

        if (row.Trust == PresetTrust.System)
            throw new PadError("PA112", $"cannot remove shipped agent-preset `{presetId}`",
                $"instance `{id}` / agent-preset", presetId, ["copy it to a user-layer id, then edit the copy"]);

        var dir = Path.Combine(inst.Home, UserPresetDir, presetId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ---- job queue --------------------------------------------------------

    string JobsPath => Path.Combine(Root, "jobs.json");

    public List<JobRecord> ListJobs()
    {
        if (!File.Exists(JobsPath)) return [];
        try
        {
            var file = ReadJson<JobsFile>(JobsPath);
            return file?.Jobs ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// PAD 重启后，上个进程留下的 running/queued 任务已经没人在跑：标成失败，
    /// 否则下载任务里会永远挂着一条 0.0%/s 的假任务（用户 2026-10-28 截图遇到两条）。
    /// 两分钟内还在更新的任务不动（可能另一个 PAD 实例真的在跑）。
    /// </summary>
    public int SweepStaleJobs(TimeSpan? staleAfter = null)
    {
        var file = ReadJson<JobsFile>(JobsPath);
        if (file is null) return 0;
        var limit = DateTimeOffset.UtcNow - (staleAfter ?? TimeSpan.FromMinutes(2));
        var n = 0;
        foreach (var row in file.Jobs)
        {
            if (!row.CanCancel) continue;
            if (DateTimeOffset.TryParse(row.UpdatedAt, out var updated) && updated > limit) continue;
            row.Status = "failed";
            row.UpdatedAt = NowIso();
            row.Label = string.IsNullOrWhiteSpace(row.Label) ? row.KindText + "（PAD 重启后中断）" : row.Label + "（PAD 重启后中断）";
            n++;
        }
        if (n > 0) WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
        return n;
    }
    readonly HashSet<string> _pausedJobs = new(StringComparer.Ordinal);

    /// <summary>暂停任务：worker 在复制/压缩循环里看到 paused 就停住等继续（不丢进度）。</summary>
    public void PauseJob(string jobId)
    {
        _pausedJobs.Add(jobId);
        var file = ReadJson<JobsFile>(JobsPath);
        var row = file?.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (row is not null)
        {
            row.Paused = true;
            row.UpdatedAt = NowIso();
            WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
        }
    }

    public void ResumeJob(string jobId)
    {
        _pausedJobs.Remove(jobId);
        var file = ReadJson<JobsFile>(JobsPath);
        var row = file?.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (row is not null)
        {
            row.Paused = false;
            row.UpdatedAt = NowIso();
            WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
        }
    }

    public bool IsPaused(string jobId) => _pausedJobs.Contains(jobId);

    /// <summary>在复制/压缩循环里调用：暂停时 Thread.Sleep 等继续；取消时抛 OperationCanceledException。</summary>
    public void WaitWhilePaused(string jobId, CancellationToken ct)
    {
        while (_pausedJobs.Contains(jobId))
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(200);
        }
        ct.ThrowIfCancellationRequested();
    }
    public JobRecord? CancelJob(string jobId)
    {
        if (!File.Exists(JobsPath)) return null;
        var file = ReadJson<JobsFile>(JobsPath) ?? new JobsFile();
        var row = file.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (row is null) return null;
        row.Status = "cancelled";
        row.UpdatedAt = NowIso();
        WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
        if (_activeJobs.TryGetValue(jobId, out var cts)) cts.Cancel();
        return row;
    }

    /// <summary>Chrome「从列表中删除」：不删已装的文件，只从 jobs.json 拿掉已结束的记录。</summary>
    public void ForgetJob(string jobId)
    {
        if (!File.Exists(JobsPath)) return;
        var file = ReadJson<JobsFile>(JobsPath) ?? new JobsFile();
        var row = file.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (row is null) return;
        if (row.CanCancel)
        {
            CancelJob(jobId);
            return;
        }
        file.Jobs.RemoveAll(j => j.Id == jobId);
        WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
    }

    /// <summary>Chrome「全部清除」：只清已结束的，正在下的留下。</summary>
    /// <summary>删除任务行：正在跑的先取消并结束进程，再从 jobs.json 移除；不动已装的文件。</summary>
    public void DeleteJob(string jobId)
    {
        var file = ReadJson<JobsFile>(JobsPath);
        if (file is null) return;
        var row = file.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (row is null) return;
        if (row.CanCancel)
        {
            CancelJob(jobId);
            if (row.Pid is int pid && pid > 0)
            {
                try { using var proc = System.Diagnostics.Process.GetProcessById(pid); proc.Kill(true); }
                catch { /* 进程已经没了 */ }
            }
        }
        file.Jobs.RemoveAll(j => j.Id == jobId);
        WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
        try { File.Delete(JobLogPath(jobId)); } catch { /* 没日志就算了 */ }
    }
    public void ForgetFinishedJobs()
    {
        if (!File.Exists(JobsPath)) return;
        var file = ReadJson<JobsFile>(JobsPath) ?? new JobsFile();
        file.Jobs.RemoveAll(j => !j.CanCancel);
        WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
    }

    // ---- job queue (write side) ------------------------------------------

    readonly Dictionary<string, CancellationTokenSource> _activeJobs = [];

    static string NewJobId(string kind) =>
        $"{kind}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Random.Shared.Next(1000, 9999)}";

    public JobRecord StartJob(string kind, string? instance, List<JobLeaf>? leaves = null,
        string? label = null, string? iconPath = null)
    {
        if (string.IsNullOrWhiteSpace(iconPath) && !string.IsNullOrWhiteSpace(instance))
        {
            try
            {
                var inst = Instances().FirstOrDefault(i => i.Id == instance);
                if (inst is not null) iconPath = InstIcon.MarkFile(inst);
            }
            catch { /* 图标只是装饰，取不到就兜底 */ }
        }
        var rec = new JobRecord
        {
            Id = NewJobId(kind),
            Kind = kind,
            Instance = instance,
            Status = "running",
            CreatedAt = NowIso(),
            UpdatedAt = NowIso(),
            Leaves = leaves,
            Label = label,
            Icon = iconPath,
        };
        UpsertJob(rec);
        AppendJobLog(rec.Id, $"start {kind}");
        return rec;
    }

    public string JobLogPath(string jobId) => Path.Combine(Root, "jobs", jobId + ".log");

    public void AppendJobLog(string jobId, string line)
    {
        var path = JobLogPath(jobId);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.AppendAllText(path, line.TrimEnd() + Environment.NewLine);
    }

    public string ReadJobLog(string jobId)
    {
        var path = JobLogPath(jobId);
        if (!File.Exists(path)) return "";
        try { return File.ReadAllText(path); }
        catch { return ""; }
    }

    sealed class RelayProgress(Action<string> act) : IProgress<string>
    {
        public void Report(string value) => act(value);
    }

    public IProgress<string> TeeJob(string jobId, IProgress<string>? inner) =>
        new RelayProgress(line =>
        {
            AppendJobLog(jobId, line);
            inner?.Report(line);
        });

    void UpsertJob(JobRecord rec)
    {
        var file = ReadJson<JobsFile>(JobsPath) ?? new JobsFile();
        file.Jobs.RemoveAll(j => j.Id == rec.Id);
        file.Jobs.Add(rec);
        WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
    }

    public void UpdateLeaf(string jobId, string leafId, int done, int total)
    {
        var file = ReadJson<JobsFile>(JobsPath);
        if (file is null) return;
        var row = file.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (row is null) return;
        row.Leaves ??= [];
        var leaf = row.Leaves.FirstOrDefault(l => l.Id == leafId);
        if (leaf is null) { leaf = new JobLeaf { Id = leafId, Weight = 1 }; row.Leaves.Add(leaf); }
        leaf.Done = done;
        leaf.Total = total;
        row.UpdatedAt = NowIso();
        WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
    }

    public void FinishJob(string jobId, string status)
    {
        var file = ReadJson<JobsFile>(JobsPath);
        if (file is null) return;
        var row = file.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (row is null) return;
        if (row.Status == "cancelled") return; // a user cancel already flipped it; don't clobber
        row.Status = status;
        row.UpdatedAt = NowIso();
        WriteAtomic(JobsPath, JsonSerializer.Serialize(file, Json));
        _activeJobs.Remove(jobId);
    }

    /// <summary>A token that CancelJob can cancel, linked to the caller's own token.</summary>
    public CancellationToken TrackCancel(string jobId, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeJobs[jobId] = cts;
        return cts.Token;
    }

    static List<JobLeaf> InstallLeaves() =>
    [
        new() { Id = "resolve-meta", Weight = 5, Total = 5 },
        new() { Id = "download", Weight = 80, Total = 80 },
        new() { Id = "verify", Weight = 10, Total = 10 },
        new() { Id = "record", Weight = 5, Total = 5 },
    ];

    // ---- node runtime ----------------------------------------------------

    public string RuntimeNodeDir(string version) => Path.Combine(Root, "runtime", "node", version);

    /// <summary>The newest node.exe under <c>runtime/node/</c>, or null when absent.</summary>
    public string? FindRuntimeNode()
    {
        var dir = Path.Combine(Root, "runtime", "node");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "node.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    /// <summary>
    /// Node the launch script will bake. <c>auto</c> is not a path.
    /// A custom path is used even outside the launcher root; runtime fallback stays kit-relative.
    /// </summary>
    public string? ResolveLaunchNode(Instance inst, PadSettings? settings)
    {
        var ov = LaunchPolicy.EffectiveNodePath(inst.Launch?.NodePath, settings?.Launch.NodePath);
        if (ov is not null)
        {
            var p = ov.Trim().Trim('"');
            if (File.Exists(p)) return Path.GetFullPath(p);
        }
        if (!string.IsNullOrWhiteSpace(inst.Dsh.Version) && NodeForRelease(inst.Dsh.Version) is { } pinned)
            return pinned;
        if (FindRuntimeNode() is { } any && LaunchPolicy.PathUnder(any, Root))
            return any;
        return null;
    }

    /// <summary>What the hint bar should name. Custom missing file does not silently fall to PATH.</summary>
    public string? PreviewNode(string? overridePath, string? dshVersion, PadSettings? settings)
    {
        if (LaunchPolicy.NodeIsCustom(overridePath))
        {
            var p = overridePath!.Trim().Trim('"');
            return File.Exists(p) ? Path.GetFullPath(p) : null;
        }
        var ov = LaunchPolicy.EffectiveNodePath(overridePath, settings?.Launch.NodePath);
        if (ov is not null)
        {
            var p = ov.Trim().Trim('"');
            return File.Exists(p) ? Path.GetFullPath(p) : null;
        }
        if (!string.IsNullOrWhiteSpace(dshVersion) && NodeForRelease(dshVersion) is { } pinned)
            return pinned;
        return FindRuntimeNode() ?? Proc.Which("node");
    }

    /// <summary>
    /// Make a node.exe available. Returns a usable path, or null when the user disabled
    /// runtime install and none is installed. When <c>runtimeInstall</c> is on and the
    /// pinned release's <c>engines.node</c> is missing, that version is downloaded into
    /// <c>runtime/node/&lt;ver&gt;/</c>.
    /// </summary>
    public async Task<string?> EnsureRuntimeNode(IProgress<string>? log, CancellationToken ct, string? dshVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(dshVersion) && NodeForRelease(dshVersion) is { } matched)
            return matched;
        if (dshVersion is null && FindRuntimeNode() is { } have) return have;
        if (Proc.Which("node") is { } onPath) return onPath;

        var s = Settings();
        if (!s.Launch.RuntimeInstall) return null;

        var ver = RequiredNodeVersion(dshVersion) ?? "22.19.0";
        var zip = Path.Combine(Path.GetTempPath(), $"node-v{ver}-win-x64.zip");
        if (!File.Exists(zip))
        {
            log?.Report($"下载 Node {ver} …");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var resp = await http.GetAsync($"https://nodejs.org/dist/v{ver}/node-v{ver}-win-x64.zip",
                HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zip);
            await resp.Content.CopyToAsync(fs, ct);
        }

        log?.Report($"解压 Node 到 runtime/node/{ver}");
        Directory.CreateDirectory(RuntimeNodeDir(ver));
        System.IO.Compression.ZipFile.ExtractToDirectory(zip, RuntimeNodeDir(ver), true);

        if (!string.IsNullOrWhiteSpace(dshVersion) && NodeForRelease(dshVersion) is { } after)
            return after;
        return FindRuntimeNode();
    }

    /// <summary>
    /// Node matching this release's <c>engines.node</c> under <c>runtime/node/&lt;ver&gt;/</c>.
    /// Does not fall back to a different Node sitting next to it.
    /// </summary>
    public string? NodeForRelease(string version)
    {
        var req = RequiredNodeVersion(version);
        if (req is null) return FindRuntimeNode();
        var dir = RuntimeNodeDir(req);
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "node.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>The concrete x.y.z the pinned release declares in engines.node, or null.</summary>
    public string? RequiredNodeVersion(string? dshVersion = null)
    {
        if (!string.IsNullOrWhiteSpace(dshVersion))
        {
            var rec = ReadJson<DshRelease>(Path.Combine(VersionDir(dshVersion), "version.json"));
            if (!string.IsNullOrWhiteSpace(rec?.EnginesNode)) return rec.EnginesNode;
            return ParseEnginesNode(Path.Combine(VersionDir(dshVersion), "node_modules", "@deepseek-ai", "dsh", "package.json"));
        }
        foreach (var r in Releases())
        {
            var v = RequiredNodeVersion(r.Version);
            if (v is not null) return v;
        }
        return null;
    }

    static string? ParseEnginesNode(string pkg)
    {
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            if (doc.RootElement.TryGetProperty("engines", out var eng)
                && eng.TryGetProperty("node", out var node)
                && Regex.Match(node.GetString() ?? "", @"\d+\.\d+\.\d+") is { Success: true } m)
                return m.Value;
        }
        catch { /* missing or junk package.json */ }
        return null;
    }

    // ---- credentials ------------------------------------------------------

    public string GlobalCredentialsPath => Path.Combine(Root, "library", "credentials.yaml");
    public string NamedCredentialsDir => Path.Combine(Root, "library", "credentials");

    public string NamedCredentialsPath(string name) =>
        name == "global" ? GlobalCredentialsPath : Path.Combine(NamedCredentialsDir, $"{name}.yaml");

    public List<CredentialSet> ListCredentials()
    {
        var names = new List<CredentialSet>();
        // global is always listed even if the file is missing, so Settings has
        // a DEEPSEEK_API_KEY box before anyone has created credentials.yaml.
        names.Add(new CredentialSet { Name = "global", Path = GlobalCredentialsPath });
        if (Directory.Exists(NamedCredentialsDir))
        {
            foreach (var f in Directory.EnumerateFiles(NamedCredentialsDir, "*.yaml"))
            {
                var id = Path.GetFileNameWithoutExtension(f);
                if (!names.Any(n => n.Name == id))
                    names.Add(new CredentialSet { Name = id, Path = f });
            }
        }
        return names.OrderBy(n => n.Name).ToList();
    }

    public string? GetCredentials(string name)
    {
        var path = NamedCredentialsPath(name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public string SetCredentials(string name, string yaml)
    {
        var path = NamedCredentialsPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml, new UTF8Encoding(false));
        CredentialsGate.DistributeToHomes(this, Settings(), path);
        return path;
    }

    public string LlmProvidersPath => LlmProviders.LibraryFile(Root);

    public List<LlmProviderRecipe> LoadLlmProviders() => LlmProviders.Load(LlmProvidersPath);

    public void SaveLlmProviders(IReadOnlyList<LlmProviderRecipe> recipes)
    {
        LlmProviders.Save(LlmProvidersPath, recipes);
        ApplyLlmProvidersToHomes(recipes);
    }

    public void ApplyLlmProvidersToHomes(IReadOnlyList<LlmProviderRecipe>? recipes = null)
    {
        recipes ??= LoadLlmProviders();
        foreach (var inst in Instances())
        {
            if (inst.Adopted) continue;
            if (string.IsNullOrWhiteSpace(inst.Home) || !Directory.Exists(inst.Home)) continue;
            LlmProviders.ApplyToHome(inst.Home, recipes);
        }
    }

    /// <summary>Delete a named credential set. The default "global" key is not removable.</summary>
    public void RemoveCredentials(string name)
    {
        if (name == "global") return;
        var path = NamedCredentialsPath(name);
        if (File.Exists(path)) File.Delete(path);
    }

    // ---- portable distribution -------------------------------------------

    static string HostExeName() => "pack-agent-for DSH.exe";

    /// <summary>Bundle the running PAD plus a <c>.pack-launcher</c> root into a
    /// portable directory. The play folder is the PAD exe plus folders, same
    /// shape as PCL's <c>PCL.exe</c> + <c>.minecraft</c>. Runtime dlls stay
    /// inside a single-file exe. When an instance id is given, that release and
    /// instance are copied in. Credentials are stripped. Paths are rewritten
    /// to the copy.</summary>
    public string ExportPortable(string destDir, string? includeInstanceId = null, CancellationToken ct = default,
        Action<string>? phase = null, IProgress<(int Done, int Total)>? copyProgress = null, string? jobId = null)
    {
        var timing = Path.Combine(Path.GetTempPath(), "pad-export-timing.log");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string step)
        {
            try { File.AppendAllText(timing, $"{DateTime.Now:HH:mm:ss.fff} {step} +{clock.ElapsedMilliseconds}ms\n"); }
            catch { /* 只是诊断日志 */ }
        }
        try { File.Delete(timing); } catch { /* ignore */ }
        Mark("start");
        destDir = Path.GetFullPath(destDir);
        Directory.CreateDirectory(destDir);
        CopyHostExe(destDir);
        Mark("host-exe");
        var repoMark = Path.Combine(destDir, ".pack-agent-repo");
        if (File.Exists(repoMark)) File.Delete(repoMark);

        var destRoot = new Launcher(Path.Combine(destDir, ".pack-launcher"));
        CopyLauncherRegistries(destRoot);
        Mark("registries");

        if (!string.IsNullOrWhiteSpace(includeInstanceId))
        {
            var inst = Instance(includeInstanceId)
                ?? throw new PadError("PA026", "no such instance", $"instances/{includeInstanceId}",
                    includeInstanceId, ["pad cli instance list"]);
            var ver = inst.Dsh.Version;
            var srcVer = VersionDir(ver);
            Mark("version-copy-start");
            phase?.Invoke("复制版本库…");
            if (Directory.Exists(srcVer)) CopyTreeLink(srcVer, destRoot.VersionDir(ver), ct, copyProgress, jobId);
            Mark("version-copy");
            phase?.Invoke("复制实例…");
            CopyTreeLink(InstanceDir(inst.Id), destRoot.InstanceDir(inst.Id), ct);
            Mark("instance-copy");
            var cred = Path.Combine(destRoot.InstanceDir(inst.Id), "home", ".credentials.yaml");
            if (File.Exists(cred)) File.Delete(cred);
            var libCred = Path.Combine(destRoot.Root, "library", "credentials.yaml");
            if (File.Exists(libCred)) File.Delete(libCred);
            Mark("strip");
            destRoot.StripPortableEphemeral(inst.Id);
            Mark("strip-done");
            var destHome = Path.Combine(destRoot.InstanceDir(inst.Id), "home");
            PnpmHeal.ClearProfilesFallback(destHome);
            Mark("relink-start");
            phase?.Invoke("修复依赖链接…");
            PnpmHeal.Relink(Path.Combine(destRoot.VersionDir(ver), "node_modules"));
            Mark("relink-done");
            var destProfiles = Path.Combine(destHome, "profiles");
            if (Directory.Exists(destProfiles))
                foreach (var dir in Directory.EnumerateDirectories(destProfiles))
                {
                    if (string.Equals(Path.GetFileName(dir), "node_modules", StringComparison.OrdinalIgnoreCase))
                        continue;
                    PnpmHeal.Relink(Path.Combine(dir, "node_modules"));
                }
            destRoot.Instance(inst.Id);
            var copied = destRoot.Instance(inst.Id);
            if (copied?.Launch?.ExtraEnv is { } env
                && env.Contains(CredentialsFile.DeepseekRef, StringComparison.Ordinal))
            {
                copied.Launch.ExtraEnv = LaunchPolicy.StripSecretEnv(env);
                destRoot.SaveInstance(copied);
            }
            destRoot.RewriteLaunchScripts(copied);
            Mark("scripts");
            phase?.Invoke("整理启动脚本…");
            var rec = destRoot.Releases().FirstOrDefault(r => r.Version == ver);
            if (rec is not null && string.IsNullOrWhiteSpace(rec.EnginesNode))
            {
                rec.EnginesNode = destRoot.RequiredNodeVersion(ver) ?? "22.19.0";
                destRoot.SaveRelease(rec);
            }
            if (!string.IsNullOrWhiteSpace(rec?.EnginesNode))
            {
                var srcNode = Path.Combine(Root, "runtime", "node", rec.EnginesNode);
                if (!Directory.Exists(srcNode))
                    srcNode = Path.Combine(Root, "runtime", "node", "22.19.0");
                if (Directory.Exists(srcNode))
                    CopyTreeLink(srcNode, Path.Combine(destRoot.Root, "runtime", "node", rec.EnginesNode), ct);
                    Mark("runtime-copy");
            }
        }

        return destDir;
    }

    /// <summary>
    /// The play folder is the PAD exe plus <c>.pack-launcher</c>. Runtime
    /// dlls stay inside a single-file exe; they must not sit next to it.
    /// </summary>
    static void CopyHostExe(string destDir)
    {
        var name = HostExeName();
        var dest = Path.Combine(destDir, name);
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("PACK_PAD_HOST_EXE");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
        var tmp = Environment.GetEnvironmentVariable("AGENT_PACK_TMP") ?? @"E:\tmp\pack-agent";
        candidates.Add(Path.Combine(tmp, "pad-win-x64-onefile", name));
        var src = LaunchPolicy.ResolvePortableHost(Environment.ProcessPath, candidates)
            ?? throw new PadError("PA117", "portable-export needs a single-file PAD",
                destDir, Environment.ProcessPath ?? "",
                ["dotnet publish -p:PublishSingleFile=true", "set PACK_PAD_HOST_EXE to that exe"]);
        if (File.Exists(dest) && new FileInfo(dest).Length == new FileInfo(src).Length) return; // 续传跳过
        File.Copy(src, dest, overwrite: true);
    }

    void StripPortableEphemeral(string? instanceId)
    {
        foreach (var name in LaunchPolicy.PortableEphemeral)
        {
            var p = Path.Combine(Root, name);
            if (File.Exists(p)) File.Delete(p);
        }
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        var logs = LogsDir(instanceId);
        if (Directory.Exists(logs)) Directory.Delete(logs, recursive: true);
    }

    void CopyLauncherRegistries(Launcher dest)
    {
        var destReg = Path.Combine(dest.Root, Settings().Other.RegistriesRel);
        Directory.CreateDirectory(destReg);
        foreach (var dir in PadRegistryProbe())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                File.Copy(file, Path.Combine(destReg, Path.GetFileName(file)), overwrite: true);
            break;
        }
    }

    static IEnumerable<string> PadRegistryProbe()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "registries");
        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && walk is not null; i++, walk = walk.Parent)
        {
            yield return Path.Combine(walk.FullName, "agent-pack-dsh", "modpack", "registries");
            yield return Path.Combine(walk.FullName, "modpack", "registries");
        }
    }

    // ---- dump-config / plugin ops (shell out to node) --------------------

    /// <summary>Run `dsh --dump-config` and write the output to the instance directory.</summary>
    public async Task<string> DumpConfig(string id, IProgress<string>? log, CancellationToken ct)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var bin = DshBin(inst.Dsh.Version);
        if (!File.Exists(bin))
            throw new PadError("PA001", "the pinned release is not installed", $"instance `{id}`", bin,
                ["先装这个发行号"]);

        var patchPath = Path.Combine(inst.Home, "launcher.patch.yml");
        var patchArg = File.Exists(patchPath) ? $" --patch \"{patchPath}\"" : "";
        log?.Report("node bin.js --dump-config");
        var res = await Proc.Run("node", $"\"{bin}\" --dump-config{patchArg}",
            inst.Workspace.Path, log, ct, new Dictionary<string, string> { ["DSH_HOME"] = inst.Home });
        if (res.ExitCode != 0)
            throw new PadError("PA030", "dsh --dump-config failed", $"instance `{id}`", res.Tail,
                ["看上面的输出"]);

        var outPath = Path.Combine(InstanceDir(id), "dump-config.yml");
        File.WriteAllText(outPath, res.Tail, new UTF8Encoding(false));
        return outPath;
    }

    /// <summary>Run dsh plugin add/remove/update on an instance's profile.</summary>
    public async Task<List<string>> PluginOp(string id, string action, string spec,
        IProgress<string>? log, CancellationToken ct, string? profile = null)
    {
        var inst = Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
        var bin = DshBin(inst.Dsh.Version);
        if (!File.Exists(bin))
            throw new PadError("PA001", "the pinned release is not installed", $"instance `{id}`", bin,
                ["先装这个发行号"]);

        profile ??= inst.LastProfile ?? Profiles(inst).FirstOrDefault()?.Name
            ?? throw new PadError("PA029", "this instance has no profile yet",
                $"instance `{id}`", inst.Id, ["先在版本选择里给这个实例建 profile"]);

        EnsureProfileNpmrc(inst, profile);
        var env = new Dictionary<string, string>
        {
            ["DSH_HOME"] = inst.Home,
            ["npm_config_ignore_workspace_root_check"] = "true",
            ["NPM_CONFIG_IGNORE_WORKSPACE_ROOT_CHECK"] = "true",
        };
        AddPnpmEnv(env);

        var job = StartJob($"plugin-{action}", id);
        var token = TrackCancel(job.Id, ct);
        log = TeeJob(job.Id, log);
        try
        {
            await Task.Run(() =>
            {
                PnpmHeal.Relink(Path.Combine(VersionDir(inst.Dsh.Version), "node_modules"));
                PnpmHeal.Relink(Path.Combine(inst.Home, "profiles", profile, "node_modules"));
            }, token);
            await Pnpm.EnsureAsync(log, token);
            log?.Report($"dsh plugin --profile {profile} {action} {spec}");
            var res = await Proc.Run("node", $"\"{bin}\" plugin --profile {profile} {action} {spec}",
                inst.Workspace.Path, log, token, env);
            if (token.IsCancellationRequested)
            {
                FinishJob(job.Id, "cancelled");
                throw new OperationCanceledException();
            }
            if (res.ExitCode != 0)
                throw new PadError("PA014", $"plugin {action} failed: {spec}", $"instance `{id}`",
                    res.Tail, ["看上面的 pnpm 输出", "确认包名与网络"]);

            // Mark restart required
            if (inst.Status != "import-failed")
            {
                inst.Status = "restart-required";
                SaveInstance(inst);
            }

            var bundles = Profiles(inst).FirstOrDefault(p => p.Name == profile)?.Bundles ?? [];
            FinishJob(job.Id, "done");
            return bundles;
        }
        catch
        {
            FinishJob(job.Id, "failed");
            throw;
        }
    }

    // ---- doctor (shell out to TS subprocess) ------------------------------

    /// <summary>Run doctor via the pack-agent CLI, like Packs.cs does for projection.</summary>
    public async Task<DoctorReport> RunDoctor(IProgress<string>? log, CancellationToken ct)
    {
        var packs = new Packs(this);
        var repo = packs.RepoRoot()
            ?? throw new PadError("PA027", "the pack-agent repo was not found",
                "PACK_AGENT_REPO", AppContext.BaseDirectory,
                ["设置 PACK_AGENT_REPO", "或在启动器根目录放 .pack-agent-repo"]);

        var entry = Path.Combine(repo, "bin", "packagent.js");
        var line = $"\"{entry}\" dsh launcher --root \"{Root}\" --json doctor";
        log?.Report("node packagent.js dsh launcher doctor");
        var res = await Proc.Run("node", line, repo, log, ct);
        if (res.ExitCode != 0)
            throw new PadError("PA030", "doctor failed", "doctor", res.Tail, ["看上面的输出"]);

        // The CLI may print human lines before JSON; take the last balanced JSON.
        var json = res.Tail;
        for (var i = json.Length - 1; i >= 0; i--)
        {
            if (json[i] != '{') continue;
            try
            {
                var slice = json[i..].Trim();
                return JsonSerializer.Deserialize<DoctorReport>(slice, Json)
                    ?? new DoctorReport { Ok = false, Errors = [new() { Code = "PA030", Message = "doctor returned empty" }] };
            }
            catch { }
        }
        throw new PadError("PA030", "doctor returned no JSON", "doctor", json, ["看上面的输出"]);
    }

    // ---- profiles dir / projection dir accessors -------------------------

    public string ProfilesDir(Instance inst) => Path.Combine(inst.Home, "profiles");

    /// <summary>The .agent-pack whitelist dir in the workspace.</summary>
    public string ProjectionDir(Instance inst) => Path.Combine(inst.Workspace.Path, ".agent-pack");

    /// <summary>
    /// Point at an existing DSH_HOME. Does not write into that home.
    /// </summary>
    public Instance AdoptHome(string home, string version, string? name = null, string? id = null)
    {
        home = Path.GetFullPath(home);
        if (!Directory.Exists(home))
            throw new PadError("PA009", "that DSH_HOME does not exist", home, home,
                ["确认路径", "或先装一个发行号再新建实例"]);
        if (!File.Exists(DshBin(version)))
            throw new PadError("PA001", "that release is not installed",
                $"versions/{version}", version, ["先在下载页装这个发行号"]);

        foreach (var existing in Instances())
        {
            if (LaunchPolicy.SamePath(existing.Home, home))
                throw new PadError("PA020", "another instance already uses this home",
                    $"instance `{existing.Id}`", home, ["打开那个实例即可"]);
        }

        var instName = string.IsNullOrWhiteSpace(name) ? "本机 DSH" : name.Trim();
        var instId = string.IsNullOrWhiteSpace(id) ? Slug(instName) : Slug(id);
        if (instId.Length == 0) instId = "local-dsh";
        var dir = InstanceDir(instId);
        if (File.Exists(Path.Combine(dir, "instance.json")))
            throw new PadError("PA023", "an instance with this id already exists",
                $"instances/{instId}", instId, ["换个名字"]);

        var workspace = Path.Combine(dir, "workspace");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(LogsDir(instId));

        var cred = Path.Combine(home, ".credentials.yaml");
        var fingerprint = File.Exists(cred)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(cred)))
                .ToLowerInvariant()
            : "";

        var inst = new Instance
        {
            Id = instId,
            Name = instName,
            Dsh = new InstanceDsh { Version = version },
            Home = home,
            Workspace = new InstanceWorkspace { Kind = "existing", Path = workspace },
            Adopted = true,
            AdoptedFingerprint = fingerprint,
            Created = NowIso(),
            Updated = NowIso(),
        };
        SaveInstance(inst);
        return inst;
    }

    public string WriteShortcut(Instance inst, string profile)
    {
        var dir = Path.Combine(Root, "library", "shortcuts");
        Directory.CreateDirectory(dir);
        var script = new Runner(this).WriteScript(inst, profile, Settings());
        var path = Path.Combine(dir, $"{inst.Id}-{profile}.bat");
        var body = $"""
            @echo off
            set PACK_LAUNCHER_ROOT={Root}
            call "{script}"
            """;
        File.WriteAllText(path, body.Replace("\n", "\r\n"), new UTF8Encoding(false));
        return path;
    }

    public bool HomeAlreadyAdopted(string home) =>
        Instances().Any(i => i.Adopted || LaunchPolicy.SamePath(i.Home, home));

    /// <summary>
    /// Adopt needs a pinned release so later launches have a CLI entry.
    /// If the version store is empty, install the default release first.
    /// </summary>
    public async Task<string> EnsureRelease(IProgress<string>? log, CancellationToken ct)
    {
        var have = Releases().FirstOrDefault(r => File.Exists(DshBin(r.Version)));
        if (have is not null) return have.Version;
        var existing = DshSniff.Bin();
        if (existing is not null)
        {
            var pinned = await PinExisting(existing, log, ct);
            return pinned.Version;
        }
        var want = Settings().Launch.DefaultRelease;
        if (string.IsNullOrWhiteSpace(want)) want = "0.1.0-rc.8";
        var rec = await InstallRelease(want, log, ct);
        return rec.Version;
    }

    /// <summary>
    /// Copy a machine-local DSH CLI into the version store so the kit can be given to someone else.
    /// Does not keep an absolute path into npm-cache / PATH.
    /// </summary>
    public async Task<DshRelease> PinExisting(string bin, IProgress<string>? log, CancellationToken ct)
    {
        bin = DshSniff.ResolveBin(bin) ?? Path.GetFullPath(bin);
        if (!File.Exists(bin))
            throw new PadError("PA001", "that DSH CLI does not exist", bin, bin,
                ["在设置里选一份 bin.js"]);

        var pkgDir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(bin)!, ".."));
        var ver = ReadPackageVersion(Path.Combine(pkgDir, "package.json"));
        if (string.IsNullOrWhiteSpace(ver))
        {
            log?.Report("node \"" + bin + "\" --version");
            var probe = await Proc.Run("node", $"\"{bin}\" --version",
                Path.GetDirectoryName(bin)!, null, ct);
            ver = (probe.Tail ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0) ?? "";
            if (ver.StartsWith('v') || ver.StartsWith('V')) ver = ver[1..];
        }
        if (string.IsNullOrWhiteSpace(ver)) ver = "local";
        foreach (var c in Path.GetInvalidFileNameChars()) ver = ver.Replace(c, '-');

        var storeBin = Path.Combine(VersionDir(ver), "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        if (!LaunchPolicy.SamePath(bin, storeBin))
        {
            var modulesSrc = Path.GetFullPath(Path.Combine(pkgDir, "..", ".."));
            var dshInModules = Path.Combine(modulesSrc, "@deepseek-ai", "dsh", "lib", "bin.js");
            if (Path.GetFileName(modulesSrc).Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                && File.Exists(dshInModules))
            {
                log?.Report("copy versions/" + ver);
                CopyTree(modulesSrc, Path.Combine(VersionDir(ver), "node_modules"));
            }
            else
            {
                return await InstallRelease(ver, log, ct);
            }
        }

        if (!File.Exists(storeBin))
            throw new PadError("PA001", "release copied but the CLI entry is missing",
                $"versions/{ver}", storeBin, ["重选一份 bin.js"]);

        log?.Report("node \"" + storeBin + "\" --version");
        var check = await Proc.Run("node", $"\"{storeBin}\" --version",
            Path.GetDirectoryName(storeBin)!, null, ct);
        var rec = new DshRelease
        {
            Version = ver,
            Verified = check.ExitCode == 0,
            VerifyOutput = (check.Tail ?? "").Trim(),
            InstalledAt = NowIso(),
            EnginesNode = ParseEnginesNode(Path.Combine(pkgDir, "package.json"))
                ?? ParseEnginesNode(Path.Combine(VersionDir(ver), "node_modules", "@deepseek-ai", "dsh", "package.json")),
        };
        Directory.CreateDirectory(VersionDir(rec.Version));
        SaveRelease(rec);
        return rec;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLinkW(string newFileName, string existingFileName, IntPtr securityAttributes);

    /// <summary>
    /// 并行 staging：串行只做目录/链接（便宜），文件收集成清单后用多线程建硬链接/复制。
    /// 实测串行逐文件只有 59 个/秒（杀软逐文件延迟），并行 16 路能跑满杀软窗口。
    /// 保留 junction（不展开：展开会把 .pnpm 复制成好几份），收件方启动时会 PnpmHeal.Relink。
    /// </summary>
    void CopyTreeLink(string from, string to, CancellationToken ct,
        IProgress<(int Done, int Total)>? progress = null, string? jobId = null)
    {
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        Directory.CreateDirectory(to);
        var files = new List<(string Src, string Dst)>();
        var links = new List<(string Link, string Target)>();
        var skipped = 0;
        var stack = new Stack<(string Src, string Dst)>();
        stack.Push((from, to));
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (srcDir, dstDir) = stack.Pop();
            Directory.CreateDirectory(dstDir);
            List<string> fileNames, dirs;
            try
            {
                fileNames = Directory.EnumerateFiles(srcDir).ToList();
                dirs = Directory.EnumerateDirectories(srcDir).ToList();
            }
            catch { continue; }

            foreach (var file in fileNames)
            {
                var real = file;
                try
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        real = new FileInfo(file).ResolveLinkTarget(true)?.FullName ?? file;
                }
                catch { /* 用原路径 */ }
                var dstFile = Path.Combine(dstDir, Path.GetFileName(file));
                // 断点续传：上次已复制/已硬链接好的文件直接跳过（大小 + 修改时间都一致）。
                if (ExistingFileMatches(real, dstFile)) { skipped++; continue; }
                files.Add((real, dstFile));
            }
            foreach (var dir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                var destDir = Path.Combine(dstDir, Path.GetFileName(dir));
                FileAttributes attr;
                try { attr = File.GetAttributes(dir); }
                catch { continue; }
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    // 先收集，后面并行建 junction（串行 mklink 是导出慢的元凶）。
                    try
                    {
                        var target = new DirectoryInfo(dir).LinkTarget;
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            var full = Path.IsPathRooted(target)
                                ? target
                                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dir)!, target));
                            if (ExistingJunctionMatches(destDir, full)) { skipped++; continue; }
                            links.Add((destDir, full));
                        }
                    }
                    catch { /* 坏链接就跳过 */ }
                    continue;
                }
                stack.Push((dir, destDir));
            }
        }

        var total = skipped + files.Count + links.Count;
        var done = skipped;
        if (progress is not null && total > 0) progress.Report((done, total));
        void Report()
        {
            if (progress is null) return;
            var n = Interlocked.Increment(ref done);
            if (n % 128 == 0 || n == total) progress.Report((n, total));
        }

        Parallel.ForEach(links, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 8, 32),
            CancellationToken = ct,
        }, pair =>
        {
            if (jobId is not null) WaitWhilePaused(jobId, ct);
            try { EnsureJunction(pair.Link, pair.Target); } catch { /* 坏链接 */ }
            Report();
        });

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 8, 32),
            CancellationToken = ct,
        };
        Parallel.ForEach(files, options, pair =>
        {
            if (jobId is not null) WaitWhilePaused(jobId, ct);
            try
            {
                if (!CreateHardLinkW(pair.Dst, pair.Src, IntPtr.Zero))
                    File.Copy(pair.Src, pair.Dst, overwrite: true);
            }
            catch
            {
                try { File.Copy(pair.Src, pair.Dst, overwrite: true); } catch { /* 跳过坏文件 */ }
            }
            Report();
        });
    }

    static bool ExistingFileMatches(string src, string dst)
    {
        try
        {
            var s = new FileInfo(src);
            var d = new FileInfo(dst);
            return d.Exists && d.Length == s.Length &&
                   Math.Abs((d.LastWriteTimeUtc - s.LastWriteTimeUtc).TotalSeconds) < 1;
        }
        catch { return false; }
    }

    static bool ExistingJunctionMatches(string link, string target)
    {
        try
        {
            if (!Directory.Exists(link)) return false;
            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0) return false;
            var existing = new DirectoryInfo(link).LinkTarget;
            if (string.IsNullOrWhiteSpace(existing)) return false;
            return string.Equals(Path.GetFullPath(existing), Path.GetFullPath(target),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    static void EnsureJunction(string link, string target)
    {
        if (ExistingJunctionMatches(link, target)) return;
        try
        {
            if (Directory.Exists(link))
            {
                if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Directory.Delete(link);
                else return; // 真实目录不碰
            }
        }
        catch { /* 交给创建逻辑 */ }
        PnpmHeal.Junction(link, target, isDir: true);
    }
    /// <summary>
    /// 大批量复制用 robocopy（多线程，比逐文件 File.Copy 快一个数量级；实测 490MB/3.2 万文件
    /// 逐文件复制 0.3MB/s，robocopy 展开 junction 后 2.2GB/22.9 万文件只要 ~3.5 分钟）。
    /// 注意：默认会展开 junction，便携包换机器后才能用（保留链接会指向本机全局 store）。
    /// </summary>
    static void CopyTreeFast(string from, string to, CancellationToken ct)
    {
        Directory.CreateDirectory(to);
        var psi = new System.Diagnostics.ProcessStartInfo("robocopy")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { from, to, "/E", "/MT:16", "/R:1", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS", "/NP" })
            psi.ArgumentList.Add(arg);
        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new PadError("PA028", "robocopy 启动失败", from + " → " + to, from, ["确认系统里有 robocopy"]);
        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { /* 已经退出 */ } });
        var outputTask = proc.StandardOutput.ReadToEndAsync();
        var errorTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        var log = outputTask.GetAwaiter().GetResult() + errorTask.GetAwaiter().GetResult();
        if (proc.ExitCode > 7)
            throw new PadError("PA028", "复制失败（robocopy " + proc.ExitCode + "）",
                from + " → " + to, log.Trim(), ["看目标目录权限/磁盘空间"]);
    }
    static void CopyTree(string from, string to, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        FileAttributes attr;
        try { attr = File.GetAttributes(from); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }

        // Followed junctions become real directories; Node then cannot see
        // pnpm siblings such as @deepseek-ai/dsh-app-boot.
        if ((attr & FileAttributes.ReparsePoint) != 0)
        {
            RecreateReparsePoint(from, to, ct);
            return;
        }

        if ((attr & FileAttributes.Directory) != 0)
        {
            Directory.CreateDirectory(to);
            foreach (var dir in Directory.EnumerateDirectories(from))
            {
                var name = Path.GetFileName(dir);
                if (name is "." or "..") continue;
                CopyTree(dir, Path.Combine(to, name), ct);
            }
            foreach (var file in Directory.EnumerateFiles(from))
                CopyTree(file, Path.Combine(to, Path.GetFileName(file)), ct);
            return;
        }

        var parent = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        // 断点续传：上次已经复制好、大小和时间都一致的文件直接跳过。
        try
        {
            var srcInfo = new FileInfo(from);
            var dstInfo = new FileInfo(to);
            if (dstInfo.Exists && dstInfo.Length == srcInfo.Length &&
                dstInfo.LastWriteTimeUtc == srcInfo.LastWriteTimeUtc)
                return;
        }
        catch { /* 读不到就照常覆盖复制 */ }
        File.Copy(from, to, overwrite: true);
    }

    static void RecreateReparsePoint(string from, string to, CancellationToken ct = default)
    {
        var isDir = (File.GetAttributes(from) & FileAttributes.Directory) != 0;
        var target = isDir ? new DirectoryInfo(from).LinkTarget : new FileInfo(from).LinkTarget;
        if (string.IsNullOrWhiteSpace(target))
        {
            if (isDir)
            {
                Directory.CreateDirectory(to);
                foreach (var dir in Directory.EnumerateDirectories(from))
                    CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
                foreach (var file in Directory.EnumerateFiles(from))
                    CopyTree(file, Path.Combine(to, Path.GetFileName(file)), ct);
            }
            else
            {
                var fallbackParent = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(fallbackParent)) Directory.CreateDirectory(fallbackParent);
                File.Copy(from, to, overwrite: true);
            }
            return;
        }

        var fromParent = Path.GetDirectoryName(from)!;
        var toParent = Path.GetDirectoryName(to)!;
        Directory.CreateDirectory(toParent);
        var destTarget = Path.IsPathRooted(target)
            ? Path.GetFullPath(Path.Combine(toParent, Path.GetRelativePath(fromParent, target)))
            : Path.GetFullPath(Path.Combine(toParent, target));

        if (Directory.Exists(to) || File.Exists(to))
        {
            var existing = File.GetAttributes(to);
            if ((existing & FileAttributes.ReparsePoint) != 0)
            {
                if ((existing & FileAttributes.Directory) != 0) Directory.Delete(to);
                else File.Delete(to);
            }
            else if ((existing & FileAttributes.Directory) != 0)
                Directory.Delete(to, recursive: true);
            else
                File.Delete(to);
        }

        PnpmHeal.Junction(to, destTarget, isDir);
    }

    static string? ReadPackageVersion(string pkg)
    {
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    // ---- profiles (the launchable unit) ----------------------------------

    /// <summary>
    /// Discover the profile roster from disk. A profile exists when
    /// <c>$DSH_HOME/profiles/&lt;name&gt;/package.json</c> exists; its bundles are that
    /// file's <c>dsh.profile.bundles</c>.
    /// </summary>
    public List<DshProfile> Profiles(Instance inst)
    {
        var list = new List<DshProfile>();
        var root = Path.Combine(inst.Home, "profiles");
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var pkg = Path.Combine(dir, "package.json");
            if (!File.Exists(pkg)) continue;
            var bundles = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
                if (doc.RootElement.TryGetProperty("dsh", out var dsh)
                    && dsh.TryGetProperty("profile", out var prof)
                    && prof.TryGetProperty("bundles", out var arr)
                    && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var b in arr.EnumerateArray())
                        if (b.GetString() is { } s) bundles.Add(s);
                }
            }
            catch { /* a damaged profile still occupies its name; show it empty */ }
            list.Add(new DshProfile { Name = Path.GetFileName(dir), Dir = dir, Bundles = bundles });
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    /// <summary>
    /// Roster for the manage page: every <c>dsh.profile.bundles</c> row plus the
    /// version in that profile's <c>node_modules</c>. Does not talk to pad-gateway.
    /// </summary>
    public List<BundleRow> ListBundles(Instance inst)
    {
        var rows = new List<BundleRow>();
        foreach (var p in Profiles(inst))
        {
            foreach (var spec in p.Bundles)
            {
                var dir = LaunchPolicy.BundlePackageDir(p.Dir, spec);
                var pkg = Path.Combine(dir, "package.json");
                var meta = PluginList.ReadMeta(pkg);
                rows.Add(new BundleRow
                {
                    Profile = p.Name,
                    Spec = spec,
                    Kind = LaunchPolicy.BundleKind(spec),
                    Version = meta.Version,
                    Description = meta.Description,
                    Keywords = meta.Keywords,
                    PackageDir = dir,
                    Latest = PluginList.ReadCachedLatest(Root, spec) ?? "",
                    IconPath = BundleIcon.Resolve(Root, dir, spec) ?? "",
                });
            }
        }
        return rows;
    }

    public async Task EnsureBundleIcons(Instance inst, CancellationToken ct)
    {
        foreach (var p in Profiles(inst))
            foreach (var spec in p.Bundles)
            {
                var dir = LaunchPolicy.BundlePackageDir(p.Dir, spec);
                await BundleIcon.Ensure(Root, dir, spec, ct);
            }
    }

    /// <summary>
    /// Create a profile by running DSH's own <c>dsh plugin --profile &lt;name&gt; add</c>,
    /// so the bundle set is written the way DSH writes it.
    /// </summary>
    public async Task AddBundles(Instance inst, string profile, IEnumerable<string> specs,
        IProgress<string>? log, CancellationToken ct)
    {
        var bin = DshBin(inst.Dsh.Version);
        if (!File.Exists(bin))
            throw new PadError("PA001", "the pinned release is not installed", $"instance `{inst.Id}`",
                bin, ["先装这个发行号"]);

        // DSH writes a pnpm-workspace.yaml into each profile, so the profile directory
        // *is* a workspace root and pnpm refuses to add there without an explicit
        // opt-in. Pass it as pnpm config rather than patching DSH's files or
        // bypassing `dsh plugin add`.
        var env = new Dictionary<string, string>
        {
            ["DSH_HOME"] = inst.Home,
            ["npm_config_ignore_workspace_root_check"] = "true",
            ["NPM_CONFIG_IGNORE_WORKSPACE_ROOT_CHECK"] = "true",
        };
        AddPnpmEnv(env);

        var specList = specs.ToList();
        var job = StartJob("plugin-add", inst.Id, null, specList.FirstOrDefault());
        var token = TrackCancel(job.Id, ct);
        log = TeeJob(job.Id, log);
        try
        {
            await Task.Run(() =>
            {
                PnpmHeal.Relink(Path.Combine(VersionDir(inst.Dsh.Version), "node_modules"));
                PnpmHeal.Relink(Path.Combine(inst.Home, "profiles", profile, "node_modules"));
            }, token);
            foreach (var spec in specList)
            {
                EnsureProfileNpmrc(inst, profile);
                log?.Report($"dsh plugin --profile {profile} add {spec}");
                var res = await Proc.Run("node", $"\"{bin}\" plugin --profile {profile} add {spec}",
                    inst.Workspace.Path, log, token, env);
                if (token.IsCancellationRequested)
                {
                    FinishJob(job.Id, "cancelled");
                    throw new OperationCanceledException();
                }
                if (res.ExitCode != 0)
                    throw new PadError("PA024", "dsh plugin add failed",
                        $"instance `{inst.Id}` / profile `{profile}`", res.Tail,
                        ["看上面的 pnpm 输出", "确认包名与网络"]);
            }
            FinishJob(job.Id, "done");
        }
        catch
        {
            FinishJob(job.Id, "failed");
            throw;
        }
    }

    /// <summary>Belt-and-braces for the workspace-root check, for pnpm builds that
    /// read the setting from .npmrc but not from the environment.</summary>
    static void EnsureProfileNpmrc(Instance inst, string profile)
    {
        var dir = Path.Combine(inst.Home, "profiles", profile);
        if (!Directory.Exists(dir)) return;
        var npmrc = Path.Combine(dir, ".npmrc");
        var text = File.Exists(npmrc) ? File.ReadAllText(npmrc) : "";
        var lines = new[]
        {
            "ignore-workspace-root-check=true",
            "strict-dep-builds=false",
            "dangerously-allow-all-builds=true",
        };
        var missing = lines.Where(l => !text.Contains(l, StringComparison.Ordinal)).ToArray();
        if (missing.Length == 0) return;
        File.WriteAllText(npmrc,
            (text.Length > 0 ? text.TrimEnd() + "\n" : "") + string.Join("\n", missing) + "\n");
    }

    // ---- sessions (the save files) ---------------------------------------

    /// <summary>
    /// Sessions live under the instance's own home. The header is a creation fact;
    /// a later <c>agent-preset/selected</c> event overrides the preset, so both are read.
    /// </summary>
    public List<SessionRow> Sessions(Instance inst)
    {
        var rows = new List<SessionRow>();
        var root = Path.Combine(inst.Home, "sessions");
        if (!Directory.Exists(root)) return rows;

        foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
        {
            var row = new SessionRow
            {
                Id = Path.GetFileNameWithoutExtension(file),
                Path = file,
                Bytes = new FileInfo(file).Length,
                Updated = File.GetLastWriteTime(file),
            };
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (line.Length == 0) continue;
                    using var doc = JsonDocument.Parse(line);
                    var root2 = doc.RootElement;
                    if (!root2.TryGetProperty("type", out var type)) continue;
                    var t = type.GetString();
                    if (t == "session")
                    {
                        if (root2.TryGetProperty("agentPreset", out var p)) row.AgentPreset = p.GetString();
                        if (root2.TryGetProperty("cwd", out var c)) row.Cwd = c.GetString();
                    }
                    else if (t == "agent-preset/selected"
                             && root2.TryGetProperty("data", out var d)
                             && d.TryGetProperty("agentPreset", out var sel))
                    {
                        row.AgentPreset = sel.GetString();
                    }
                }
            }
            catch { /* a damaged transcript still deserves a row */ }
            rows.Add(row);
        }
        rows.Sort((a, b) => b.Updated.CompareTo(a.Updated));
        return rows;
    }

    // ---- runtime ---------------------------------------------------------

    public string RuntimePath => Path.Combine(Root, "runtime.json");

    public RuntimeFile Runtime()
    {
        var rt = ReadJson<RuntimeFile>(RuntimePath) ?? new RuntimeFile();
        var changed = false;

        for (var i = rt.Entries.Count - 1; i >= 0; i--)
        {
            var entry = rt.Entries[i];
            var launcherAlive = Proc.Alive(entry.Pid);
            var harness = LiveHarnessPid(entry);
            var harnessAlive = harness > 0 && Proc.Alive(harness);
            if (!launcherAlive && harnessAlive && harness != entry.Pid)
            {
                entry.Pid = harness;
                changed = true;
            }

            DateTimeOffset started;
            if (!DateTimeOffset.TryParse(entry.StartedAt, out started))
                started = DateTimeOffset.UtcNow;
            if (LaunchPolicy.KeepTrackedRun(launcherAlive, harnessAlive, started, DateTimeOffset.UtcNow))
                continue;

            rt.Entries.RemoveAt(i);
            changed = true;
        }

        if (changed) SaveRuntime(rt);
        return rt;
    }

    public bool HarnessAlive(string instanceId, string profile)
    {
        var rec = ReadJson<Instance>(Path.Combine(InstanceDir(instanceId), "instance.json"));
        var bin = rec is null ? null : DshBin(rec.Dsh.Version);
        if (Proc.FindDshPid(profile, bin) > 0) return true;
        var ad = AdvertPid(instanceId);
        return ad > 0 && Proc.Alive(ad);
    }

    /// <summary>The pid the in-instance gateway published, or 0 when there is none.</summary>
    int AdvertPid(string instanceId)
    {
        var inst = Instance(instanceId);
        if (inst is null) return 0;
        var path = Path.Combine(inst.Home, "pad-gateway.json");
        if (!File.Exists(path)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var n) ? n : 0;
        }
        catch { return 0; }
    }

    int LiveHarnessPid(RunEntry entry)
    {
        if (Proc.Alive(entry.Pid)) return entry.Pid;
        var ad = AdvertPid(entry.Instance);
        if (ad > 0 && Proc.Alive(ad)) return ad;
        var inst = Instance(entry.Instance);
        var bin = inst is null ? null : DshBin(inst.Dsh.Version);
        var found = Proc.FindDshPid(entry.Profile, bin);
        return found > 0 && Proc.Alive(found) ? found : 0;
    }

    public void SaveRuntime(RuntimeFile rt) =>
        WriteAtomic(RuntimePath, JsonSerializer.Serialize(rt, Json));

    public void TrackRun(RunEntry entry)
    {
        var rt = Runtime();
        rt.Entries.RemoveAll(e => e.Instance == entry.Instance && e.Profile == entry.Profile);
        rt.Entries.Add(entry);
        SaveRuntime(rt);
    }

    public void UntrackRun(string instance, string profile)
    {
        var rt = Runtime();
        rt.Entries.RemoveAll(e => e.Instance == instance && e.Profile == profile);
        SaveRuntime(rt);
    }

    // ---- PAD preferences -------------------------------------------------

    public string SettingsPath => Path.Combine(Root, "pad.json");

    public PadSettings Settings()
    {
        if (!File.Exists(SettingsPath))
        {
            var fresh = new PadSettings();
            fresh.Normalize();
            return fresh;
        }
        try
        {
            var s = JsonSerializer.Deserialize<PadSettings>(File.ReadAllText(SettingsPath), Json)
                    ?? new PadSettings();
            s.Normalize();
            return s;
        }
        catch (Exception ex)
        {
            // Corrupt pad.json must not brick the launcher. Park the bad file and start clean.
            var backup = true;
            try
            {
                try
                {
                    // Peek without full deserialize: prefer prefs flag if the file still parses enough.
                    // If the whole file is junk, default to backup.
                    var peek = File.ReadAllText(SettingsPath);
                    if (peek.Contains("\"backupCorrupt\": false", StringComparison.OrdinalIgnoreCase)
                        || peek.Contains("\"backupCorrupt\":false", StringComparison.OrdinalIgnoreCase))
                        backup = false;
                }
                catch { /* junk */ }

                if (backup)
                {
                    var dest = SettingsPath + ".bad-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                    File.Copy(SettingsPath, dest, overwrite: true);
                }
            }
            catch { /* nowhere left */ }

            var fallback = new PadSettings();
            fallback.Normalize();
            fallback.Other.LoadWarning = backup
                ? "pad.json 损坏已备份：" + ex.Message
                : "pad.json 损坏已回默认：" + ex.Message;
            return fallback;
        }
    }

    public void SaveSettings(PadSettings s)
    {
        s.Normalize();
        s.Other.LoadWarning = null;
        WriteAtomic(SettingsPath, JsonSerializer.Serialize(s, Json));
        // Deliberately no RewriteLaunchScripts() here (D22): it walks the pnpm store and
        // can block for seconds, and a failure there must not fail a settings save.
        // Launch scripts regenerate on the next launch, or via `pad cli rewrite-scripts`.
    }
}
