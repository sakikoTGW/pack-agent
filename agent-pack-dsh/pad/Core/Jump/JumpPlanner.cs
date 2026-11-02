using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core.Jump;

/// <summary>One (surface, transport) candidate with its planner verdict.</summary>
public sealed record JumpCandidate(
    string Surface,
    string Transport,
    string Rank,
    int Score,
    string State,
    string? Code = null,
    string? Why = null);

/// <summary>Explainable jump plan. JSON shape stays stable for scripts/tests.</summary>
public sealed class JumpPlan
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pad.jump-plan/v1";
    [JsonPropertyName("session")] public object? Session { get; set; }
    [JsonPropertyName("chosen")] public JumpCandidate? Chosen { get; set; }
    [JsonPropertyName("candidates")] public List<JumpCandidate> Candidates { get; set; } = [];
    [JsonPropertyName("rejected")] public List<JumpCandidate> Rejected { get; set; } = [];
    [JsonPropertyName("degraded")] public bool Degraded { get; set; }
    [JsonPropertyName("executed")] public bool Executed { get; set; }
    [JsonPropertyName("pa")] public string? Pa { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("clipboard")] public string? Clipboard { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

/// <summary>
/// Picks one (surface, transport) for a session: hard filters first, then the
/// deterministic ranking from docs/session-jump.md §6. Surfaces are data.
/// </summary>
public static class JumpPlanner
{
    /// <summary>Build a plan without touching the UI.</summary>
    /// <exception cref="PadError">PA132 when the session id is not canonical.</exception>
    public static JumpPlan Plan(Launcher launcher, Instance inst, string rawSession, string? surfaceFilter = null)
    {
        var id = SessionRef.Normalize(rawSession)
                 ?? throw new PadError("PA132", "session id is not canonical", $"sessions/{rawSession}", rawSession,
                     ["session-<uuid>, a raw uuid, or a session cache file name"]);

        var plan = new JumpPlan
        {
            Session = new { home = inst.Home, id },
        };
        var descriptors = SurfaceRegistry.Load(launcher, inst.Home);
        var ledger = Nodes.Ledger.Load(inst.Home).Where(e => Proc.Alive(e.Pid)).ToList();
        var ad = GatewayAd.Read(inst.Home);
        var runningWeb = ledger.FirstOrDefault(e => e.Surface == "web" && !string.IsNullOrWhiteSpace(e.Url));

        foreach (var descriptor in descriptors)
        {
            if (!string.IsNullOrWhiteSpace(surfaceFilter)
                && !string.Equals(descriptor.Id, surfaceFilter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(descriptor.Kind, surfaceFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (descriptor.Error is not null || descriptor.Kind == "invalid")
            {
                plan.Rejected.Add(new JumpCandidate(descriptor.Id, "(descriptor)", "-", 0, "rejected", "PA134", descriptor.Error));
                continue;
            }

            foreach (var transport in descriptor.Transports)
            {
                var type = transport.Type.ToLowerInvariant();
                switch (type)
                {
                    case "gateway-event":
                        if (ad is null)
                            plan.Rejected.Add(new JumpCandidate(descriptor.Id, type, "R0", 0, "rejected", "PA136",
                                "pad-gateway.json missing or invalid in this home"));
                        else
                            plan.Candidates.Add(new JumpCandidate(descriptor.Id, type, "R0", 1500, "eligible"));
                        break;

                    case "url-query":
                        if (runningWeb is null)
                            plan.Rejected.Add(new JumpCandidate(descriptor.Id, type, "R1", 0, "rejected", "PA133",
                                "no running web surface for this home (PAD ledger)"));
                        else
                            plan.Candidates.Add(new JumpCandidate(descriptor.Id, type, "R1", 800, "eligible"));
                        break;

                    case "launch-env":
                    case "launch-args":
                        if (!string.Equals(descriptor.Kind, "tui", StringComparison.OrdinalIgnoreCase))
                            plan.Rejected.Add(new JumpCandidate(descriptor.Id, type, "R2", 0, "rejected", "PA134",
                                "launch-env/args is only valid for PAD-launched surfaces"));
                        else if (ledger.Any(e => e.Surface == "tui"))
                            plan.Rejected.Add(new JumpCandidate(descriptor.Id, type, "R2", 0, "rejected", "PA133",
                                "tui profile already running; resume needs a relaunch"));
                        else
                            plan.Candidates.Add(new JumpCandidate(descriptor.Id, type, "R2",
                                type == "launch-env" ? 420 : 400,
                                "eligible"));
                        break;

                    case "focus-clipboard":
                        plan.Candidates.Add(new JumpCandidate(descriptor.Id, type, "R3", 1, "eligible"));
                        break;

                    default:
                        plan.Rejected.Add(new JumpCandidate(descriptor.Id, type, "-", 0, "rejected", "PA134",
                            "unknown transport type"));
                        break;
                }
            }
        }

        plan.Chosen = plan.Candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Surface, StringComparer.Ordinal)
            .ThenBy(c => c.Transport, StringComparer.Ordinal)
            .FirstOrDefault();

        plan.Clipboard = SessionRef.ClipboardText(id);
        if (plan.Chosen is null) plan.Pa = "PA130";
        else if (plan.Chosen.Transport == "focus-clipboard")
        {
            plan.Pa = "PA139";
            plan.Degraded = true;
            plan.Detail = "falls back to focusing the surface window + clipboard";
        }
        else if (plan.Chosen.State == "plan-only")
        {
            plan.Degraded = true;
            plan.Detail = "transport is planned but not executed by M1 (needs a relaunch)";
        }

        return plan;
    }

    /// <summary>Plan + execute the chosen transport. Throws PA130 when nothing is usable.</summary>
    public static async Task<JumpPlan> ExecuteAsync(Launcher launcher, Instance inst, string rawSession,
        string? surfaceFilter = null, bool execute = true, CancellationToken ct = default)
    {
        var plan = Plan(launcher, inst, rawSession, surfaceFilter);
        if (plan.Chosen is null)
            throw new PadError("PA130", "no usable surface for this session", inst.Home, null,
                ["pad cli surface list --json", "install @sakikotgw/pad-session-jump / pad-gateway in this profile",
                 "start the instance first: pad cli web " + inst.Id]);

        if (!execute) return plan;

        var id = SessionRef.Normalize(rawSession)!;
        var chosen = plan.Chosen;
        switch (chosen.Transport)
        {
            case "gateway-event":
            {
                var ad = GatewayAd.Read(inst.Home)
                         ?? throw new PadError("PA136", "pad-gateway.json missing", inst.Home);
                plan.Executed = true;
                plan.Detail = $"posted to pad-gateway (seq {await PostJumpAsync(ad, id, ct)})";
                return plan;
            }

            case "url-query":
            {
                var running = Nodes.Ledger.Load(inst.Home)
                    .FirstOrDefault(e => e.Surface == "web" && !string.IsNullOrWhiteSpace(e.Url));
                if (running is null) throw new PadError("PA133", "no running web surface", inst.Home);
                var url = WithSessionParam(running.Url, id);
                OpenUrl(url);
                plan.Executed = true;
                plan.Url = url;
                plan.Detail = "opened the web URL with ?session= (needs the consumer plugin to act on it)";
                return plan;
            }

            case "launch-env":
            case "launch-args":
            {
                var bin = launcher.DshBin(inst.Dsh.Version);
                if (string.IsNullOrWhiteSpace(bin) || !File.Exists(bin))
                    throw new PadError("PA093", "PAD does not know where the dsh CLI is", inst.Home,
                        $"release {inst.Dsh.Version} is not installed",
                        ["pad cli release install " + inst.Dsh.Version, "pad cli inspect " + inst.Home]);
                var nodeExe = launcher.NodeForRelease(inst.Dsh.Version) ?? "node";
                var useArgs = chosen.Transport == "launch-args";
                var opened = await Nodes.OpenTerminal(inst.Home, "tui", bin, nodeExe,
                    $"{inst.Id} · tui", TimeSpan.FromSeconds(45), ct,
                    useArgs ? null : new Dictionary<string, string> { ["DSH_TUI_RESUME_SESSION"] = id },
                    useArgs ? ["--resume", id] : null);
                plan.Executed = true;
                plan.Detail = useArgs
                    ? $"started tui with --resume {id} (pid {opened.Pid})"
                    : $"started tui with DSH_TUI_RESUME_SESSION={id} (pid {opened.Pid})";
                return plan;
            }

            case "focus-clipboard":
            {
                var running = Nodes.Ledger.Load(inst.Home).Where(e => Proc.Alive(e.Pid)).ToList();
                var target = running.FirstOrDefault(e => e.Surface == "web")
                             ?? running.FirstOrDefault(e => e.Surface == "app")
                             ?? running.FirstOrDefault();
                var titleHints = new[] { "DeepSeek Harness", inst.Id };
                var (focused, ambiguous, detail) = FocusClipboard.TryFocus(target?.Pid, titleHints);
                FocusClipboard.SetClipboard(SessionRef.ClipboardText(id));
                plan.Executed = true;
                plan.Clipboard = SessionRef.ClipboardText(id);
                plan.Detail = focused
                    ? $"focused {detail}; session id copied"
                    : $"no window focused ({detail}); session id copied";
                if (ambiguous) plan.Pa = "PA138";
                plan.Degraded = true;
                return plan;
            }

            default:
            {
                // launch-env (tui): M1 does not relaunch; report the planned env.
                var envName = chosen.Transport == "launch-env" ? "DSH_TUI_RESUME_SESSION" : "(args)";
                plan.Detail = $"planned {chosen.Transport} for {chosen.Surface}: {envName}={id} (M1 plan-only)";
                plan.Degraded = true;
                return plan;
            }
        }
    }

    static async Task<int> PostJumpAsync(GatewayAd ad, string sessionId, CancellationToken ct)
    {
        var origin = new Uri(ad.Url).GetLeftPart(UriPartial.Authority);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ad.Token);
        var body = new StringContent(JsonSerializer.Serialize(new { sessionId }), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(origin + "/pad/jump", body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new PadError("PA136", "pad-gateway rejected the jump request", origin, text);
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("seq", out var seq) ? seq.GetInt32() : 0;
        }
        catch
        {
            return 0;
        }
    }

    static string WithSessionParam(string url, string sessionId)
    {
        var builder = new UriBuilder(url);
        var query = builder.Query.TrimStart('?');
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(query)) parts.AddRange(query.Split('&', StringSplitOptions.RemoveEmptyEntries));
        parts.RemoveAll(p => p.StartsWith("session=", StringComparison.OrdinalIgnoreCase));
        parts.Add("session=" + Uri.EscapeDataString(sessionId));
        builder.Query = string.Join("&", parts);
        // 0.2.x: the token URL is consumed with a redirect, which drops the query
        // but keeps the fragment. Put the session in the fragment too, so the
        // plugin's boot-capture (and direct-open) can still find it.
        builder.Fragment = "session=" + Uri.EscapeDataString(sessionId);
        return builder.ToString();
    }

    static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
