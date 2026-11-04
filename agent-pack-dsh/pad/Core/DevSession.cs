using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// One change inside a development session. `op` is add / remove; later ops (update,
/// config) reuse the same shape. Nothing here writes the real profile — the change
/// rides the existing trial overlay and only `commit` promotes it.
/// </summary>
public sealed class DevChange
{
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("op")] public string Op { get; set; } = "";
    [JsonPropertyName("spec")] public string Spec { get; set; } = "";
    [JsonPropertyName("trial")] public string Trial { get; set; } = "";
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";
}

/// <summary>A development session = one experiment run. `running` until commit/discard;
/// committed/discarded sessions stay in history for replay and repro export.</summary>
public sealed class DevSession
{
    public const string SchemaName = "pad.dev-session/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = SchemaName;
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("instance")] public string Instance { get; set; } = "";
    [JsonPropertyName("profile")] public string Profile { get; set; } = "";
    /// <summary>running | committed | discarded.</summary>
    [JsonPropertyName("state")] public string State { get; set; } = "running";
    [JsonPropertyName("startedAt")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("endedAt")] public string? EndedAt { get; set; }
    [JsonPropertyName("verifiedAt")] public string? VerifiedAt { get; set; }
    [JsonPropertyName("lastResult")] public string? LastResult { get; set; }
    [JsonPropertyName("checkpoint")] public string? Checkpoint { get; set; }
    [JsonPropertyName("sourceHash")] public string SourceHash { get; set; } = "";
    [JsonPropertyName("changes")] public List<DevChange> Changes { get; set; } = [];
}

public sealed class DevCheck
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "skip";
    [JsonPropertyName("ms")] public long Ms { get; init; }
    [JsonPropertyName("detail")] public string Detail { get; init; } = "";
    [JsonPropertyName("evidence")] public string? Evidence { get; init; }
}

public sealed class DevVerdict
{
    public const string SchemaName = "pad.dev-verdict/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = SchemaName;
    [JsonPropertyName("session")] public string Session { get; init; } = "";
    [JsonPropertyName("instance")] public string Instance { get; init; } = "";
    [JsonPropertyName("profile")] public string Profile { get; init; } = "";
    [JsonPropertyName("ts")] public string Ts { get; init; } = "";
    /// <summary>pass | fail | inconclusive</summary>
    [JsonPropertyName("result")] public string Result { get; init; } = "inconclusive";
    [JsonPropertyName("exitCode")] public int ExitCode { get; init; }
    [JsonPropertyName("checks")] public List<DevCheck> Checks { get; init; } = [];
}

public static class DevSessions
{
    public static string Dir(Launcher launcher, string instanceId) =>
        Path.Combine(launcher.InstanceDir(instanceId), "dev");

    public static string FilePath(Launcher launcher, string instanceId) =>
        Path.Combine(Dir(launcher, instanceId), "session.json");

    public static string HistoryPath(Launcher launcher, string instanceId) =>
        Path.Combine(Dir(launcher, instanceId), "history.jsonl");

    public static string VerdictDir(Launcher launcher, string instanceId) =>
        Path.Combine(Dir(launcher, instanceId), "verdicts");

    public static DevSession? Load(Launcher launcher, string instanceId)
    {
        try
        {
            var path = FilePath(launcher, instanceId);
            if (!File.Exists(path)) return null;
            var s = JsonSerializer.Deserialize<DevSession>(File.ReadAllText(path));
            return s?.Schema == DevSession.SchemaName ? s : null;
        }
        catch { return null; }
    }

    public static DevSession Save(Launcher launcher, DevSession session)
    {
        var dir = Dir(launcher, session.Instance);
        Directory.CreateDirectory(dir);
        var tmp = FilePath(launcher, session.Instance) + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        File.Move(tmp, FilePath(launcher, session.Instance), overwrite: true);
        return session;
    }

    public static void AppendHistory(Launcher launcher, DevSession session)
    {
        try
        {
            var dir = Dir(launcher, session.Instance);
            Directory.CreateDirectory(dir);
            File.AppendAllText(HistoryPath(launcher, session.Instance),
                JsonSerializer.Serialize(session) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { /* history is an affordance; the session file is the truth */ }
    }

    public static List<DevSession> History(Launcher launcher, string instanceId)
    {
        var list = new List<DevSession>();
        try
        {
            var path = HistoryPath(launcher, instanceId);
            if (!File.Exists(path)) return list;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var s = JsonSerializer.Deserialize<DevSession>(line);
                    if (s?.Schema == DevSession.SchemaName) list.Add(s);
                }
                catch { /* skip damaged line */ }
            }
        }
        catch { /* no history */ }
        list.Reverse();
        return list;
    }

    /// <summary>Start (or resume) a session for this instance+profile. A committed or
    /// discarded session is archived; a new one starts on demand.</summary>
    public static DevSession Open(Launcher launcher, Instance inst, string profile)
    {
        var existing = Load(launcher, inst.Id);
        if (existing is not null && existing.State == "running" && existing.Profile == profile)
            return existing;

        if (existing is not null && existing.State == "running")
        {
            // Only one running session per instance: archive the other profile's session.
            existing.State = "discarded";
            existing.EndedAt = DateTimeOffset.UtcNow.ToString("o");
            AppendHistory(launcher, existing);
        }

        var session = new DevSession
        {
            Id = "dev-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"),
            Instance = inst.Id,
            Profile = profile,
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
            SourceHash = DeclarationHash(launcher, inst, profile),
        };
        return Save(launcher, session);
    }

    public static DevSession Require(Launcher launcher, Instance inst)
    {
        var s = Load(launcher, inst.Id);
        if (s is null)
            throw new PadError("PA240", "no development session", $"instances/{inst.Id}",
                "先开一个开发会话", ["pad cli dev start <instance> --profile <p>"]);
        if (s.State != "running")
            throw new PadError("PA243", "development session already closed", $"instances/{inst.Id}",
                s.State, ["pad cli dev start <instance>"]);
        return s;
    }

    /// <summary>Fingerprint of the declaration layer that actually boots: the effective
    /// <c>dsh.profile.bundles</c> list + the profile patch. A trial install only adds a
    /// dependency and rides a --patch overlay, so it must NOT flip this hash; a commit
    /// writes bundles and does — which is exactly when the session is already closed.</summary>
    public static string DeclarationHash(Launcher launcher, Instance inst, string profile)
    {
        var sb = new StringBuilder();
        var pkg = Path.Combine(launcher.InstanceDir(inst.Id), "home", "profiles", profile, "package.json");
        sb.Append("bundles:");
        if (File.Exists(pkg))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
                var bundles = doc.RootElement.TryGetProperty("dsh", out var dsh)
                              && dsh.TryGetProperty("profile", out var prof)
                              && prof.TryGetProperty("bundles", out var arr)
                              && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(e => e.GetString() ?? "").OrderBy(b => b).ToList()
                    : [];
                sb.Append(string.Join(",", bundles));
            }
            catch { /* unparsable package.json = empty declaration; dump-config reports it */ }
        }
        sb.Append('\n').Append("patch:");
        var patch = Path.Combine(launcher.InstanceDir(inst.Id), "home", "profiles", profile, "cordis.patch.yml");
        if (File.Exists(patch)) sb.Append(File.ReadAllText(patch));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

/// <summary>AI-first development loop: apply → verify → commit/discard. Every method is
/// non-interactive and returns the same JSON-serialisable shapes the CLI/MCP expose.</summary>
public static class DevOps
{
    public static async Task<DevSession> ApplyAsync(Launcher launcher, Instance inst, string profile,
        string action, string spec, IProgress<string>? log, CancellationToken ct)
    {
        var session = DevSessions.Require(launcher, inst);
        var trials = new Trials(launcher);
        switch (action)
        {
            case "add":
            {
                var rec = await trials.Begin(inst, profile, spec, log, ct);
                session.Changes.Add(new DevChange
                {
                    At = DateTimeOffset.UtcNow.ToString("o"),
                    Op = "add",
                    Spec = rec.Spec,
                    Trial = rec.Spec,
                    Detail = "本次运行生效 · 未写实例",
                });
                break;
            }
            case "remove" or "drop":
            {
                var rec = trials.Load(inst).Trials
                    .FirstOrDefault(t => t.Profile == profile &&
                                         (t.Spec == spec || t.Bundles.Contains(spec)))
                    ?? throw new PadError("PA245", "no pending trial for this spec", $"instances/{inst.Id}",
                        spec, ["pad cli dev status <instance>"]);
                await trials.Discard(inst, rec, log, ct);
                session.Changes.Add(new DevChange
                {
                    At = DateTimeOffset.UtcNow.ToString("o"),
                    Op = "remove",
                    Spec = rec.Spec,
                    Trial = rec.Spec,
                    Detail = "已从本次运行移除",
                });
                break;
            }
            default:
                throw new PadError("PA244", "unknown development action", "dev apply",
                    action, ["--action add|remove"]);
        }
        return DevSessions.Save(launcher, session);
    }

    public static async Task<DevVerdict> VerifyAsync(Launcher launcher, Instance inst, string profile,
        IProgress<string>? log, CancellationToken ct)
    {
        var session = DevSessions.Load(launcher, inst.Id);
        var checks = new List<DevCheck>();
        var profileDir = Path.Combine(inst.Home, "profiles", profile);

        // 1) config compiles
        var sw = Stopwatch.StartNew();
        try
        {
            var dump = await launcher.DumpConfig(inst.Id, log, ct);
            checks.Add(new DevCheck
            {
                Id = "config-compiles", Status = "pass", Ms = sw.ElapsedMilliseconds,
                Detail = "dump-config 退出 0", Evidence = dump,
            });
        }
        catch (PadError pe)
        {
            checks.Add(new DevCheck
            {
                Id = "config-compiles", Status = "fail", Ms = sw.ElapsedMilliseconds,
                Detail = pe.Headline + (string.IsNullOrWhiteSpace(pe.Detail) ? "" : " · " + pe.Detail),
            });
        }
        catch (Exception ex)
        {
            checks.Add(new DevCheck
            {
                Id = "config-compiles", Status = "fail", Ms = sw.ElapsedMilliseconds,
                Detail = ex.Message,
            });
        }

        // 2) every declared bundle resolves on disk (third-party rows only; provided rows are
        // DSH-builtins and live in the release, not the profile).
        sw.Restart();
        var missing = new List<string>();
        try
        {
            var rows = launcher.ListBundles(inst).Where(r => r.Profile == profile).ToList();
            foreach (var row in rows.Where(r => !r.Provided))
            {
                var dir = string.IsNullOrWhiteSpace(row.PackageDir)
                    ? LaunchPolicy.BundlePackageDir(profileDir, row.Spec)
                    : row.PackageDir;
                if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "package.json")))
                    missing.Add(row.Spec);
            }
            var pending = new Trials(launcher).Pending(inst, profile);
            foreach (var t in pending)
                foreach (var spec in t.Bundles)
                {
                    var dir = LaunchPolicy.BundlePackageDir(profileDir, spec);
                    if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "package.json")))
                        missing.Add(spec);
                }
            checks.Add(new DevCheck
            {
                Id = "plugins-resolve", Status = missing.Count == 0 ? "pass" : "fail",
                Ms = sw.ElapsedMilliseconds,
                Detail = missing.Count == 0 ? $"{rows.Count} 个 bundle 全解析" : "missing: " + string.Join(", ", missing),
            });
        }
        catch (Exception ex)
        {
            checks.Add(new DevCheck
            {
                Id = "plugins-resolve", Status = "fail", Ms = sw.ElapsedMilliseconds, Detail = ex.Message,
            });
        }

        // 3) source instance must be untouched (declaration layer fingerprint).
        sw.Restart();
        var nowHash = DevSessions.DeclarationHash(launcher, inst, profile);
        checks.Add(new DevCheck
        {
            Id = "source-untouched",
            Status = session is null || nowHash == session.SourceHash ? "pass" : "fail",
            Ms = sw.ElapsedMilliseconds,
            Detail = session is null ? "没有会话可比对（按 pass 处理）"
                : nowHash == session.SourceHash ? $"指纹一致 · {nowHash}" : $"指纹变了 · {session.SourceHash} → {nowHash}",
        });

        // P1 checks are declared now so the schema never changes shape.
        checks.Add(new DevCheck { Id = "boots", Status = "skip", Detail = "P1：启动并读到 ready" });
        checks.Add(new DevCheck { Id = "stays-up", Status = "skip", Detail = "P1：存活 N 秒" });
        checks.Add(new DevCheck { Id = "tools-inventory", Status = "skip", Detail = "P1：工具数/token 对比基线" });

        var fails = checks.Count(c => c.Status == "fail");
        var result = fails > 0 ? "fail" : "pass";
        var verdict = new DevVerdict
        {
            Session = session?.Id ?? "",
            Instance = inst.Id,
            Profile = profile,
            Ts = DateTimeOffset.UtcNow.ToString("o"),
            Result = result,
            ExitCode = fails > 0 ? 2 : 0,
            Checks = checks,
        };
        WriteVerdict(launcher, inst.Id, verdict);
        if (session is not null)
        {
            session.LastResult = result;
            session.VerifiedAt = verdict.Ts;
            DevSessions.Save(launcher, session);
        }
        return verdict;
    }

    public static DevSession Commit(Launcher launcher, Instance inst, bool dryRun, bool force = false, IProgress<string>? log = null)
    {
        var session = DevSessions.Require(launcher, inst);
        var trials = new Trials(launcher);
        var pending = trials.Pending(inst, session.Profile);
        if (session.Changes.Count == 0 || pending.Count == 0)
            throw new PadError("PA241", "nothing to commit", $"instances/{inst.Id}",
                "本次会话还没有改动", ["pad cli dev apply <instance> --action add --spec <pkg>"]);

        if (session.LastResult == "fail" && !dryRun && !force)
            throw new PadError("PA242", "verification failed; refusing to commit", $"instances/{inst.Id}",
                "先修复，或用 --force 仍然固化（会留记录）",
                ["pad cli dev verify <instance> --json"]);

        if (dryRun) return session;

        var ck = Checkpoints.Create(launcher, inst, session.Profile, $"dev commit {session.Id}");
        foreach (var rec in pending)
        {
            try
            {
                trials.Commit(inst, rec, log);
                session.Changes.Add(new DevChange
                {
                    At = DateTimeOffset.UtcNow.ToString("o"),
                    Op = "commit",
                    Spec = rec.Spec,
                    Trial = rec.Spec,
                    Detail = $"已写入 profile · 打点 {ck.Id}",
                });
            }
            catch (Exception ex)
            {
                log?.Report("commit failed: " + ex.Message);
            }
        }
        session.State = "committed";
        session.EndedAt = DateTimeOffset.UtcNow.ToString("o");
        session.Checkpoint = ck.Id;
        DevSessions.Save(launcher, session);
        DevSessions.AppendHistory(launcher, session);
        return session;
    }

    public static async Task<DevSession> DiscardAsync(Launcher launcher, Instance inst,
        IProgress<string>? log, CancellationToken ct)
    {
        var session = DevSessions.Require(launcher, inst);
        var trials = new Trials(launcher);
        foreach (var rec in trials.Pending(inst, session.Profile).ToList())
        {
            try { await trials.Discard(inst, rec, log, ct); }
            catch (Exception ex) { log?.Report("discard failed: " + ex.Message); }
        }
        session.State = "discarded";
        session.EndedAt = DateTimeOffset.UtcNow.ToString("o");
        DevSessions.Save(launcher, session);
        DevSessions.AppendHistory(launcher, session);
        return session;
    }

    static void WriteVerdict(Launcher launcher, string instanceId, DevVerdict verdict)
    {
        try
        {
            var dir = DevSessions.VerdictDir(launcher, instanceId);
            Directory.CreateDirectory(dir);
            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var json = JsonSerializer.Serialize(verdict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dir, stamp + ".json"), json, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "verdict.json"), json, new UTF8Encoding(false));
        }
        catch { /* evidence is an affordance; the returned object is the truth */ }
    }
}
