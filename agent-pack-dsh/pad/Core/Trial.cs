using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// One bundle being tried before it is kept. The package is installed in the profile
/// but deliberately absent from <c>dsh.profile.bundles</c> — DSH's own "installed as a
/// plain dependency, not a profile layer" state — and reaches the run only through a
/// <c>--patch</c> overlay. So a plain launch does not carry it: the trial really is
/// this-run-only until it is committed.
/// </summary>
public sealed class TrialRecord
{
    [JsonPropertyName("profile")] public string Profile { get; set; } = "";
    [JsonPropertyName("spec")] public string Spec { get; set; } = "";
    [JsonPropertyName("bundles")] public List<string> Bundles { get; set; } = [];
    [JsonPropertyName("patch")] public string Patch { get; set; } = "";
    [JsonPropertyName("created")] public string Created { get; set; } = "";

    public string Title => Bundles.Count == 1 ? Bundles[0] : Spec;
    public string Detail => $"{Profile} · 仅本次运行 · 还没固化";
}

public sealed class TrialFile
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.pad.trials/v1";
    [JsonPropertyName("trials")] public List<TrialRecord> Trials { get; set; } = [];
}

/// <summary>
/// Try-then-commit for profile bundles. DSH applies <c>dsh.profile.bundles</c> at boot
/// and will not re-read it while running, so "hot reload" here means: launch once with
/// the candidate layered on by <c>--patch</c>, then either write it into the profile or
/// throw it away. Every step is a documented DSH operation; nothing is hot-injected into
/// a live process, because the official apiproxy exposes no plugin or loader rpc.
/// </summary>
public sealed class Trials(Launcher launcher)
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    readonly Launcher _l = launcher;

    string Path_(Instance inst) => System.IO.Path.Combine(_l.InstanceDir(inst.Id), "trials.json");

    public TrialFile Load(Instance inst)
    {
        var path = Path_(inst);
        if (!File.Exists(path)) return new TrialFile();
        try { return JsonSerializer.Deserialize<TrialFile>(File.ReadAllText(path)) ?? new TrialFile(); }
        catch { return new TrialFile(); }
    }

    void Save(Instance inst, TrialFile file) =>
        File.WriteAllText(Path_(inst), JsonSerializer.Serialize(file, Json), new UTF8Encoding(false));

    public List<TrialRecord> Pending(Instance inst, string profile) =>
        Load(inst).Trials.Where(t => t.Profile == profile).ToList();

    /// <summary>The <c>--patch</c> arguments a launch of this profile must carry.</summary>
    public string PatchArgs(Instance inst, string profile)
    {
        var parts = Pending(inst, profile)
            .Where(t => File.Exists(t.Patch))
            .Select(t => $"--patch \"{t.Patch}\"");
        return string.Join(' ', parts);
    }

    // ---- the profile's bundle list ---------------------------------------

    static string ProfilePkg(Instance inst, string profile) =>
        System.IO.Path.Combine(inst.Home, "profiles", profile, "package.json");

    static List<string> ReadBundles(string pkgPath)
    {
        var list = new List<string>();
        if (!File.Exists(pkgPath)) return list;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkgPath));
            if (doc.RootElement.TryGetProperty("dsh", out var dsh)
                && dsh.TryGetProperty("profile", out var prof)
                && prof.TryGetProperty("bundles", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in arr.EnumerateArray())
                    if (b.GetString() is { } s) list.Add(s);
            }
        }
        catch { /* a damaged manifest is handled by the caller's diff being empty */ }
        return list;
    }

    static bool HasDependency(string pkgPath, string name)
    {
        if (!File.Exists(pkgPath)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkgPath));
            return doc.RootElement.TryGetProperty("dependencies", out var deps)
                   && deps.TryGetProperty(name, out _);
        }
        catch { return false; }
    }

    /// <summary>
    /// Rewrite only <c>dsh.profile.bundles</c>, leaving every other key and pnpm's
    /// dependency block exactly as DSH wrote them.
    /// </summary>
    static void WriteBundles(string pkgPath, List<string> bundles)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(pkgPath))!.AsObject();
        var dsh = node["dsh"]?.AsObject();
        if (dsh is null)
        {
            dsh = new System.Text.Json.Nodes.JsonObject();
            node["dsh"] = dsh;
        }
        var prof = dsh["profile"]?.AsObject();
        if (prof is null)
        {
            prof = new System.Text.Json.Nodes.JsonObject();
            dsh["profile"] = prof;
        }
        var arr = new System.Text.Json.Nodes.JsonArray();
        foreach (var b in bundles) arr.Add(b);
        prof["bundles"] = arr;
        File.WriteAllText(pkgPath, node.ToJsonString(Json) + "\n", new UTF8Encoding(false));
    }

    // ---- try -------------------------------------------------------------

    /// <summary>
    /// Install a candidate and stage it as this-run-only. Uses DSH's own
    /// <c>dsh plugin add</c> to resolve and install, then moves the rows DSH appended out
    /// of the profile layer and into a <c>--patch</c> overlay.
    /// </summary>
    public async Task<TrialRecord> Begin(Instance inst, string profile, string spec,
        IProgress<string>? log, CancellationToken ct)
    {
        var pkgPath = ProfilePkg(inst, profile);
        if (!File.Exists(pkgPath))
            throw new PadError("PA029", "that profile does not exist yet",
                $"instance `{inst.Id}` / profile `{profile}`", pkgPath,
                ["先在版本选择里给这个实例建 profile"]);

        var before = ReadBundles(pkgPath);
        var name = PackageName(spec);

        // A bundle already in the layer cannot be trialled: the diff below would come
        // back empty and look exactly like "declares no dsh.bundle". Say so before
        // installing anything, so a mis-click never uninstalls a working bundle.
        if (before.Contains(name))
            throw new PadError("PA032", "that bundle is already in this profile's layer",
                $"profile `{profile}`", name,
                ["它已经固化了，不用试验", "要换掉就先在这个 profile 里移除它"]);

        var wasDependency = HasDependency(pkgPath, name);
        await _l.AddBundles(inst, profile, [spec], log, ct);
        var after = ReadBundles(pkgPath);

        var added = after.Where(b => !before.Contains(b)).ToList();
        if (added.Count == 0)
        {
            // Nothing to try. Undo the download, but only if this add is what brought
            // it in — never uninstall something the profile already depended on.
            if (!wasDependency) await Remove(inst, profile, spec, log, ct);
            throw new PadError("PA105", "that package is a plain dependency, not a profile layer",
                $"profile `{profile}`", spec,
                ["它没声明 dsh.bundle，没有可以试验的层", "确认包名"]);
        }

        // Take the new rows back out of the profile layer: a plain launch must not
        // carry a trial.
        WriteBundles(pkgPath, before);
        log?.Report($"从 dsh.profile.bundles 摘回 {string.Join(", ", added)}，改由 --patch 只挂本次");

        var patch = System.IO.Path.Combine(_l.InstanceDir(inst.Id), $"trial-{profile}.patch.yml");
        File.WriteAllText(patch, PatchBody(added), new UTF8Encoding(false));

        var rec = new TrialRecord
        {
            Profile = profile,
            Spec = spec,
            Bundles = added,
            Patch = patch,
            Created = DateTimeOffset.UtcNow.ToString("o"),
        };
        var file = Load(inst);
        file.Trials.RemoveAll(t => t.Profile == profile && t.Spec == spec);
        file.Trials.Add(rec);
        Save(inst, file);
        return rec;
    }

    /// <summary>
    /// An overlay in the same shape as a bundle's own <c>cordis.patch.yml</c>: a list of
    /// insert ops. The id is derived from the package name so a second trial of the same
    /// package replaces its own row instead of stacking.
    /// </summary>
    static string PatchBody(IEnumerable<string> bundles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PAD trial overlay: this-run-only bundles, applied with --patch.");
        sb.AppendLine("# Committing writes these into dsh.profile.bundles and deletes this file.");
        sb.AppendLine("- insert:");
        foreach (var name in bundles)
        {
            sb.AppendLine($"    - id: pad-trial-{Launcher.Slug(name)}");
            sb.AppendLine($"      name: '{name}'");
        }
        return sb.ToString();
    }

    // ---- commit / discard ------------------------------------------------

    /// <summary>Keep it: write the rows into the profile layer, effective next launch.</summary>
    public void Commit(Instance inst, TrialRecord rec, IProgress<string>? log)
    {
        var pkgPath = ProfilePkg(inst, rec.Profile);
        var bundles = ReadBundles(pkgPath);
        foreach (var b in rec.Bundles)
            if (!bundles.Contains(b)) bundles.Add(b);
        WriteBundles(pkgPath, bundles);
        log?.Report($"固化 {string.Join(", ", rec.Bundles)} 进 dsh.profile.bundles，下次启动生效");

        Forget(inst, rec);
    }

    /// <summary>Throw it away: remove the package and the overlay.</summary>
    public async Task Discard(Instance inst, TrialRecord rec, IProgress<string>? log, CancellationToken ct)
    {
        await Remove(inst, rec.Profile, rec.Spec, log, ct);
        Forget(inst, rec);
    }

    /// <summary>Uninstall through DSH's own plugin command, which forwards to pnpm.</summary>
    async Task Remove(Instance inst, string profile, string spec, IProgress<string>? log, CancellationToken ct)
    {
        var bin = _l.DshBin(inst.Dsh.Version);
        if (!File.Exists(bin)) return;

        // pnpm removes by package name; a version range or a local path is not one.
        var name = PackageName(spec);
        var env = new Dictionary<string, string>
        {
            ["DSH_HOME"] = inst.Home,
            ["npm_config_ignore_workspace_root_check"] = "true",
        };
        log?.Report($"dsh plugin --profile {profile} remove {name}");
        await Proc.Run("node", $"\"{bin}\" plugin --profile {profile} remove {name}",
            inst.Workspace.Path, log, ct, env);
    }

    /// <summary>Strip a version range from a spec, keeping the scope's leading @.</summary>
    public static string PackageName(string spec)
    {
        var at = spec.LastIndexOf('@');
        return at > 0 ? spec[..at] : spec;
    }

    void Forget(Instance inst, TrialRecord rec)
    {
        try { if (File.Exists(rec.Patch)) File.Delete(rec.Patch); } catch { /* stale overlay */ }
        var file = Load(inst);
        file.Trials.RemoveAll(t => t.Profile == rec.Profile && t.Spec == rec.Spec);
        Save(inst, file);
    }
}
