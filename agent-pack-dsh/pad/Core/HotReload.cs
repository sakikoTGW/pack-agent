using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

public sealed class HotApplyResult
{
    public const string SchemaName = "pad.hot-apply/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = SchemaName;
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    [JsonPropertyName("instance")] public string Instance { get; init; } = "";
    [JsonPropertyName("profile")] public string Profile { get; init; } = "";
    [JsonPropertyName("spec")] public string Spec { get; init; } = "";
    /// <summary><c>hot</c> or <c>cold</c>.</summary>
    [JsonPropertyName("mode")] public string Mode { get; init; } = "cold";
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("restartRequired")] public bool RestartRequired { get; init; }
    [JsonPropertyName("restartReason")] public string? RestartReason { get; init; }
    [JsonPropertyName("hotAllowed")] public bool HotAllowed { get; init; }
    [JsonPropertyName("hotReason")] public string HotReason { get; init; } = "";
    [JsonPropertyName("checkpoint")] public string Checkpoint { get; init; } = "";
    [JsonPropertyName("verify")] public PluginVerifyResult? Verify { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

/// <summary>Last hot/cold apply for one instance: what the plugin page needs to show
/// 「已热装 / 待重启」 without re-running the change.</summary>
public sealed class HotState
{
    public const string SchemaName = "pad.hot-state/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = SchemaName;
    [JsonPropertyName("instance")] public string Instance { get; init; } = "";
    [JsonPropertyName("profile")] public string Profile { get; init; } = "";
    [JsonPropertyName("spec")] public string Spec { get; init; } = "";
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    /// <summary><c>hot</c> or <c>cold</c>.</summary>
    [JsonPropertyName("mode")] public string Mode { get; init; } = "cold";
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("at")] public string At { get; init; } = "";
}

public static class HotStates
{
    public const string FileName = "hot-state.json";

    public static string PathOf(Launcher launcher, string instanceId) =>
        Path.Combine(launcher.InstanceDir(instanceId), FileName);

    public static HotState? Read(Launcher launcher, string instanceId)
    {
        try
        {
            var path = PathOf(launcher, instanceId);
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<HotState>(File.ReadAllText(path));
            return state?.Schema == HotState.SchemaName ? state : null;
        }
        catch { return null; }
    }

    public static void Save(Launcher launcher, Instance inst, HotApplyResult result)
    {
        try
        {
            var path = PathOf(launcher, inst.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var state = new HotState
            {
                Instance = inst.Id,
                Profile = result.Profile,
                Spec = result.Spec,
                Action = result.Action,
                Mode = result.Mode,
                Ok = result.Ok,
                At = DateTimeOffset.UtcNow.ToString("o"),
            };
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, Pretty));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* the state file is an affordance, never a reason to fail the change */ }
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
}

/// <summary>
/// Drives the official DSH hot reload instead of reimplementing it: the default is
/// cold (write + restart required), and the hot path only runs when the capability
/// probe says the official HMR owns this instance and the caller asked for it.
/// </summary>
public static class HotReload
{
    public static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SettleAfterRow = TimeSpan.FromMilliseconds(1500);

    public static async Task<HotApplyResult> ApplyAsync(
        Launcher launcher, Instance inst, string profile, string action, string spec,
        bool wantHot, IProgress<string>? log, CancellationToken ct)
    {
        var cap = HotCapabilityProbe.Detect(launcher, inst, profile, refresh: true);
        var useHot = wantHot && cap.HotAllowed;
        var checkpoint = Checkpoints.Create(launcher, inst, profile, $"plugin {action} {spec}");
        var pidBefore = useHot ? PluginVerify.LivePid(launcher, inst.Id, profile) : null;

        log?.Report(useHot
            ? $"hot path: {action} {spec} (checkpoint {checkpoint.Id})"
            : $"cold path: {action} {spec} (checkpoint {checkpoint.Id}; restart required)");

        await launcher.PluginOp(inst.Id, action, spec, log, ct, profile, useHot);

        if (!useHot)
        {
            return Save(launcher, inst, new HotApplyResult
            {
                Action = action,
                Instance = inst.Id,
                Profile = profile,
                Spec = spec,
                Mode = "cold",
                Ok = true,
                RestartRequired = true,
                RestartReason = wantHot ? $"not-hot: {cap.Reason}" : "cold-default",
                HotAllowed = cap.HotAllowed,
                HotReason = cap.Reason,
                Checkpoint = checkpoint.Id,
                Message = "已写入，重启后生效",
            });
        }

        var expect = action == "remove" ? PluginExpect.Absent : PluginExpect.Present;
        await SettleAsync(launcher, inst, profile, spec, expect, ct);

        var verify = await PluginVerify.RunAsync(launcher, inst, profile, spec, expect, null, pidBefore, ct);
        if (verify.Result == "pass")
        {
            inst.Status = "ready";
            launcher.SaveInstance(inst);
            log?.Report("hot path verified");
            return Save(launcher, inst, new HotApplyResult
            {
                Action = action,
                Instance = inst.Id,
                Profile = profile,
                Spec = spec,
                Mode = "hot",
                Ok = true,
                RestartRequired = false,
                HotAllowed = cap.HotAllowed,
                HotReason = cap.Reason,
                Checkpoint = checkpoint.Id,
                Verify = verify,
                Message = "已热生效",
            });
        }

        log?.Report($"hot verify failed; restoring checkpoint {checkpoint.Id}");
        var rollbackOk = true;
        try
        {
            Checkpoints.Restore(launcher, inst, checkpoint.Id);
        }
        catch (Exception ex)
        {
            rollbackOk = false;
            log?.Report("rollback failed: " + ex.Message);
        }

        PluginVerifyResult? rolled = null;
        if (rollbackOk)
        {
            rolled = await PluginVerify.RunAsync(launcher, inst, profile, spec,
                expect == PluginExpect.Present ? PluginExpect.Absent : PluginExpect.Present, ct: ct);
        }

        return Save(launcher, inst, new HotApplyResult
        {
            Action = action,
            Instance = inst.Id,
            Profile = profile,
            Spec = spec,
            Mode = "hot",
            Ok = false,
            RestartRequired = true,
            RestartReason = rollbackOk ? "hot-verify-failed" : "rollback-failed",
            HotAllowed = cap.HotAllowed,
            HotReason = cap.Reason,
            Checkpoint = checkpoint.Id,
            Verify = verify,
            Message = rollbackOk ? "热验证失败，已自动回滚" : "热验证失败且回滚失败，需人工处理",
        });
    }

    static HotApplyResult Save(Launcher launcher, Instance inst, HotApplyResult result)
    {
        HotStates.Save(launcher, inst, result);
        return result;
    }

    /// <summary>Wait for the bundle roster to reach the expected state, then give the
    /// official HMR its settle window (its config watchers use a 2 s write-stability
    /// window), and verify once more.</summary>
    static async Task SettleAsync(Launcher launcher, Instance inst, string profile, string spec,
        PluginExpect expect, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + SettleTimeout;
        var want = expect == PluginExpect.Present;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (PluginVerify.RowMatches(launcher, inst, profile, spec) == want) break;
            try { await Task.Delay(300, ct); }
            catch (OperationCanceledException) { return; }
        }
        try { await Task.Delay(SettleAfterRow, ct); }
        catch (OperationCanceledException) { /* verify still runs */ }
    }

    public static string ToJson(HotApplyResult result) => JsonSerializer.Serialize(result, Pretty);

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
}
