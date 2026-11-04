using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

public enum PluginExpect { Present, Absent }

public sealed class VerifyCheck
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    /// <summary><c>pass</c> / <c>warn</c> / <c>fail</c> / <c>skip</c>.</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "skip";
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    [JsonPropertyName("ms")] public long Ms { get; init; }
}

public sealed class PluginVerifyResult
{
    public const string SchemaName = "pad.plugin-verify/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = SchemaName;
    [JsonPropertyName("instance")] public string Instance { get; init; } = "";
    [JsonPropertyName("profile")] public string Profile { get; init; } = "";
    [JsonPropertyName("spec")] public string Spec { get; init; } = "";
    [JsonPropertyName("expect")] public string Expect { get; init; } = "present";
    /// <summary><c>pass</c> / <c>fail</c> / <c>inconclusive</c>.</summary>
    [JsonPropertyName("result")] public string Result { get; init; } = "inconclusive";
    [JsonPropertyName("exitCode")] public int ExitCode { get; init; } = 3;
    [JsonPropertyName("checks")] public List<VerifyCheck> Checks { get; init; } = [];
}

/// <summary>
/// V1–V4: the only honest way to know a change landed, because the official HMR
/// swallows plugin apply failures (verified 2026-11-04). Every check reads disk or
/// the launch ledger; none of them parses HMR logs.
/// </summary>
public static class PluginVerify
{
    public static async Task<PluginVerifyResult> RunAsync(
        Launcher launcher, Instance inst, string profile, string spec, PluginExpect expect,
        int? expectedTools = null, int? priorPid = null, CancellationToken ct = default)
    {
        var checks = new List<VerifyCheck>();

        // V1: the composed tree still compiles.
        var sw = Stopwatch.StartNew();
        try
        {
            await launcher.DumpConfig(inst.Id, null, ct);
            checks.Add(new VerifyCheck { Id = "config-compiles", Status = "pass", Ms = sw.ElapsedMilliseconds });
        }
        catch (PadError ex)
        {
            checks.Add(new VerifyCheck
            {
                Id = "config-compiles", Status = "fail", Ms = sw.ElapsedMilliseconds,
                Detail = $"{ex.Code} {ex.Headline}",
            });
        }
        catch (Exception ex)
        {
            checks.Add(new VerifyCheck
            {
                Id = "config-compiles", Status = "fail", Ms = sw.ElapsedMilliseconds,
                Detail = ex.Message,
            });
        }

        // V2: the bundle row is present/absent exactly as asked.
        sw.Restart();
        var present = RowMatches(launcher, inst, profile, spec);
        var wantPresent = expect == PluginExpect.Present;
        checks.Add(new VerifyCheck
        {
            Id = "bundle-row",
            Status = present == wantPresent ? "pass" : "fail",
            Ms = sw.ElapsedMilliseconds,
            Detail = $"{(present ? "present" : "absent")} (want {(wantPresent ? "present" : "absent")})",
        });

        // V3: tools really changed when the caller knows the expected count.
        sw.Restart();
        if (expectedTools is null)
        {
            checks.Add(new VerifyCheck { Id = "tools-inventory", Status = "skip", Ms = 0, Detail = "no expected count" });
        }
        else
        {
            var report = Inspect.Measure(inst.Home);
            checks.Add(new VerifyCheck
            {
                Id = "tools-inventory",
                Status = report.Tools == expectedTools.Value ? "pass" : "fail",
                Ms = sw.ElapsedMilliseconds,
                Detail = $"tools {report.Tools} (want {expectedTools.Value})",
            });
        }

        // V4: the hot path must not have silently restarted the instance.
        sw.Restart();
        if (priorPid is null)
        {
            checks.Add(new VerifyCheck { Id = "runtime-pid", Status = "skip", Ms = 0, Detail = "no prior pid" });
        }
        else
        {
            var now = LivePid(launcher, inst.Id, profile);
            checks.Add(new VerifyCheck
            {
                Id = "runtime-pid",
                Status = now == priorPid ? "pass" : "fail",
                Ms = sw.ElapsedMilliseconds,
                Detail = $"pid {now?.ToString() ?? "none"} (was {priorPid})",
            });
        }

        var failed = checks.Count(c => c.Status == "fail");
        return new PluginVerifyResult
        {
            Instance = inst.Id,
            Profile = profile,
            Spec = spec,
            Expect = wantPresent ? "present" : "absent",
            Result = failed == 0 ? "pass" : "fail",
            ExitCode = failed == 0 ? 0 : 3,
            Checks = checks,
        };
    }

    public static bool RowMatches(Launcher launcher, Instance inst, string profile, string spec)
    {
        var want = LaunchPolicy.BundleBareName(spec);
        return launcher.ListBundles(inst).Any(r =>
            r.Profile == profile && LaunchPolicy.BundleBareName(r.Spec) == want);
    }

    public static int? LivePid(Launcher launcher, string instanceId, string profile) =>
        launcher.Runtime().Entries
            .FirstOrDefault(e => e.Instance == instanceId && e.Profile == profile)?.Pid;

    public static string ToJson(PluginVerifyResult result) => JsonSerializer.Serialize(result, Pretty);

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
}
