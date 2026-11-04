using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core.Jump;

/// <summary>
/// CLI face of session jump: <c>pad cli session ref|open</c> and
/// <c>pad cli surface list|validate</c>. Shares the planner with the window.
/// </summary>
public static class SessionCli
{
    public static async Task<int> Run(Launcher launcher, string cmd, string[] rest) => cmd switch
    {
        "ref" => Ref(rest),
        "open" => await Open(launcher, rest),
        "" => Usage(),
        _ => Usage(),
    };

    public static int RunSurface(Launcher launcher, string cmd, string[] rest)
    {
        switch (cmd)
        {
            case "list":
            case "validate":
                break;
            default:
                return Usage();
        }

        var instanceId = FlagValue(rest, "--instance");
        string? home = null;
        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            var inst = launcher.Instance(instanceId)
                       ?? throw new PadError("PA102", "unknown instance", $"instances/{instanceId}", instanceId,
                           ["pad cli instance list"]);
            home = inst.Home;
        }

        var descriptors = SurfaceRegistry.Load(launcher, home);
        var json = rest.Contains("--json");
        var errors = descriptors.Where(d => d.Error is not null || d.Kind == "invalid").ToList();
        var warnings = descriptors.SelectMany(d => d.MergeNotes.Select(note => new { id = d.Id, note })).ToList();

        if (json)
        {
            Cli.Print(Cli.PrettyJson(new
            {
                schema = "pad.surface-list/v1",
                home,
                count = descriptors.Count,
                errors = errors.Select(d => new { id = d.Id, source = d.Source, error = d.Error }).ToList(),
                mergeNotes = warnings,
                surfaces = descriptors.Select(d => new
                {
                    id = d.Id,
                    label = d.Label,
                    kind = d.Kind,
                    source = d.Source,
                    layers = d.Layers,
                    transports = d.Transports.Select(t => t.Type).ToList(),
                    notes = d.MergeNotes,
                    error = d.Error,
                }).ToList(),
            }));
        }
        else
        {
            foreach (var d in descriptors)
            {
                var transports = string.Join(", ", d.Transports.Select(t => t.Type));
                var state = d.Error is null ? "" : $"  ERROR: {d.Error}";
                Cli.Print($"{d.Id,-14} {d.Kind,-8} {d.Source,-18} {transports}{state}");
                if (cmd == "validate")
                    foreach (var note in d.MergeNotes)
                        Cli.Print($"  merge: {note}");
            }
            Cli.Print($"{descriptors.Count} surface(s), {errors.Count} error(s), {warnings.Count} merge note(s)");
            if (cmd == "validate" && errors.Count > 0)
                foreach (var d in errors)
                    Cli.Print($"  error: {d.Id}: {d.Error ?? "invalid descriptor"}");
        }

        return cmd == "validate" && errors.Count > 0 ? 1 : 0;
    }

    static int Ref(string[] rest)
    {
        if (rest.Length < 1) return Usage();
        var normalized = SessionRef.Normalize(rest[0])
                         ?? throw new PadError("PA132", "session id is not canonical", $"sessions/{rest[0]}", rest[0],
                             ["session-<uuid>, a raw uuid, or a session cache file name"]);
        Cli.Print(Cli.PrettyJson(new { schema = "pad.session-ref/v1", id = normalized, clipboard = normalized }));
        return 0;
    }

    static async Task<int> Open(Launcher launcher, string[] rest)
    {
        var positional = Positional(rest, ["--surface"]);
        if (positional.Count < 2) return Usage();

        var instanceId = positional[0];
        var session = positional[1];
        var instance = launcher.Instance(instanceId)
                       ?? throw new PadError("PA102", "unknown instance", $"instances/{instanceId}", instanceId,
                           ["pad cli instance list"]);

        var surface = FlagValue(rest, "--surface");
        var json = rest.Contains("--json");
        var noExec = rest.Contains("--no-exec");
        var explain = rest.Contains("--explain");

        JumpPlan plan;
        if (explain)
        {
            var planned = JumpPlanner.Plan(launcher, instance, session, surface);
            if (json)
            {
                // `--explain --json` is the stable machine format of the plan itself.
                Cli.Print(Cli.PrettyJson(planned));
                return planned.Chosen is null ? 1 : 0;
            }
            Explain(instance, planned);
            if (noExec) return planned.Chosen is null ? 1 : 0;
            Cli.Print("---- execute ----");
            plan = await JumpPlanner.ExecuteAsync(launcher, instance, session, surface, execute: true);
        }
        else
        {
            plan = await JumpPlanner.ExecuteAsync(launcher, instance, session, surface, execute: !noExec);
        }

        if (json)
        {
            Cli.Print(Cli.PrettyJson(plan));
            return 0;
        }

        Cli.Print($"session  {plan.Clipboard}");
        Cli.Print($"instance {instance.Id}  home {instance.Home}");
        if (plan.Chosen is null)
        {
            Cli.Print($"error[PA130]: no usable surface (rejected {plan.Rejected.Count})");
            foreach (var r in plan.Rejected) Cli.Print($"  - {r.Surface}/{r.Transport}: {r.Code} {r.Why}");
            return 1;
        }

        Cli.Print($"chosen   {plan.Chosen.Surface}/{plan.Chosen.Transport}  {plan.Chosen.Rank}  score={plan.Chosen.Score}");
        Cli.Print($"executed {plan.Executed}  degraded {plan.Degraded}{(plan.Pa is null ? "" : "  " + plan.Pa)}");
        if (plan.Detail is not null) Cli.Print($"detail   {plan.Detail}");
        foreach (var c in plan.Candidates.Where(c => !ReferenceEquals(c, plan.Chosen)))
            Cli.Print($"  candidate {c.Surface}/{c.Transport} {c.Rank} score={c.Score} {c.State}");
        foreach (var r in plan.Rejected)
            Cli.Print($"  rejected  {r.Surface}/{r.Transport}: {r.Code} {r.Why}");
        return 0;
    }

    /// <summary>Human-readable explanation of a plan: every candidate, why it lost, and the fallback.</summary>
    static void Explain(Instance instance, JumpPlan plan)
    {
        Cli.Print($"session   {plan.Clipboard}");
        Cli.Print($"instance  {instance.Id} · {instance.Dsh.Version}  home {instance.Home}");
        if (plan.Chosen is null)
        {
            Cli.Print("chosen    (none)  PA130");
        }
        else
        {
            Cli.Print($"chosen    {plan.Chosen.Surface}/{plan.Chosen.Transport}  " +
                      $"{plan.Chosen.Rank} score={plan.Chosen.Score} state={plan.Chosen.State}");
        }
        Cli.Print($"degraded  {plan.Degraded}{(plan.Pa is null ? "" : "  " + plan.Pa)}"
                  + (plan.Detail is null ? "" : $"  ({plan.Detail})"));
        Cli.Print("candidates:");
        foreach (var c in plan.Candidates)
        {
            var mark = ReferenceEquals(c, plan.Chosen) ? "->" : "  ";
            Cli.Print($"  {mark} {c.Surface}/{c.Transport,-16} {c.Rank} score={c.Score,-5} {c.State}");
        }
        if (plan.Rejected.Count > 0)
        {
            Cli.Print("rejected:");
            foreach (var r in plan.Rejected)
                Cli.Print($"     {r.Surface}/{r.Transport,-16} {r.Code,-6} {r.Why}");
        }
    }

    static int Usage()
    {
        Cli.Print("""
            usage:
              pad cli session ref <session-id>
              pad cli session open <instance> <session-id> [--surface <id|kind>] [--explain] [--json] [--no-exec]
              pad cli surface list   [--instance <id>] [--json]
              pad cli surface validate [--instance <id>] [--json]
            """);
        return 2;
    }

    static List<string> Positional(string[] args, string[] valuedFlags)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (Array.IndexOf(valuedFlags, args[i]) >= 0) { i++; continue; }
            if (args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            list.Add(args[i]);
        }
        return list;
    }

    static string? FlagValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..];
    }
}
