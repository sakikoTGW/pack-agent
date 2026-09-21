using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// What the in-instance carrier plugin advertises after DSH boots. PAD never guesses
/// a port: the running process writes this file into its own DSH_HOME.
/// </summary>
public sealed class GatewayAd
{
    public const string FileName = "pad-gateway.json";
    public const string Schema = "pack-agent.pad-gateway/v1";

    [JsonPropertyName("schema")] public string SchemaValue { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("release")] public string? Release { get; set; }
    [JsonPropertyName("profile")] public string? Profile { get; set; }

    public bool Valid => SchemaValue == Schema
                         && Uri.TryCreate(Url, UriKind.Absolute, out var u)
                         && u.Scheme == "http"
                         && u.Host is "127.0.0.1" or "localhost"
                         && Token.Length >= 16;
}

public enum GatewayState { NoRun, NoAd, DeadPid, Unreachable, Ready }

public sealed record GatewayProbe(GatewayState State, GatewayAd? Ad, string Message);

/// <summary>
/// Client for the management side door. Health lives on the carrier's own
/// <c>/pad/ping</c>; everything else is DSH's official apiproxy contract on
/// <c>/api</c>, so PAD reads exactly what the DSH Web UI reads.
/// </summary>
public sealed class Gateway
{
    static readonly HttpClient Http = new();

    static TimeSpan Timeout
    {
        get
        {
            try
            {
                return TimeSpan.FromSeconds(
                    Math.Clamp(AppState.Current.Settings.Download.GatewayTimeoutSec, 2, 60));
            }
            catch { return TimeSpan.FromSeconds(6); }
        }
    }

    public static string AdPath(string home) => Path.Combine(home, GatewayAd.FileName);

    public static GatewayAd? ReadAd(string home)
    {
        var path = AdPath(home);
        if (!File.Exists(path)) return null;
        try
        {
            var ad = JsonSerializer.Deserialize<GatewayAd>(File.ReadAllText(path));
            return ad is { Valid: true } ? ad : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Explain the side door's state in terms the user can act on, instead of
    /// surfacing a bare connection error.
    /// </summary>
    public static async Task<GatewayProbe> Probe(Instance inst, bool anyRunning, CancellationToken ct)
    {
        if (!anyRunning)
            return new GatewayProbe(GatewayState.NoRun, null, "这个实例没在跑。先到启动页启动它。");

        var ad = ReadAd(inst.Home);
        if (ad is null)
            return new GatewayProbe(GatewayState.NoAd, null,
                "跑起来了但没有写出管理口。这个 profile 大概没装 apiproxy —— 到版本选择里给它补上。");

        if (!Proc.Alive(ad.Pid))
            return new GatewayProbe(GatewayState.DeadPid, ad,
                $"管理口记的 pid {ad.Pid} 已经不在了。那次运行是上一轮留下的。");

        try
        {
            var root = ad.Url.TrimEnd('/');
            var baseUrl = root.EndsWith("/api", StringComparison.Ordinal) ? root[..^4] : root;
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/pad/ping");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ad.Token);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(Timeout);
            using var res = await Http.SendAsync(req, linked.Token);
            if (!res.IsSuccessStatusCode)
                return new GatewayProbe(GatewayState.Unreachable, ad, $"管理口回了 HTTP {(int)res.StatusCode}。");
            return new GatewayProbe(GatewayState.Ready, ad, "管理口已连上。");
        }
        catch (Exception ex)
        {
            return new GatewayProbe(GatewayState.Unreachable, ad, "连不上管理口：" + ex.Message);
        }
    }

    /// <summary>
    /// One unary call on the official apiproxy contract: <c>POST /api/&lt;method&gt;</c> carrying
    /// a <c>client-request</c> envelope, answered by a <c>server-response</c> whose
    /// <c>result</c> is either <c>{ok:true,value}</c> or <c>{ok:false,error}</c>.
    /// </summary>
    public static async Task<JsonElement> Call(GatewayAd ad, string method, object? payload,
        CancellationToken ct)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            type = "client-request",
            rpcId = Guid.NewGuid().ToString(),
            method,
            payload = payload ?? new { },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ad.Url.TrimEnd('/')}/{method}")
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ad.Token);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(Timeout);
        using var res = await Http.SendAsync(req, linked.Token);
        var body = await res.Content.ReadAsStringAsync(linked.Token);
        if (!res.IsSuccessStatusCode)
            throw new PadError("PA030", $"apiproxy returned HTTP {(int)res.StatusCode}", method,
                body, ["确认这个 profile 装的 apiproxy 版本和发行号一致"]);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var result))
            throw new PadError("PA030", "apiproxy answer has no result", method, body);

        if (result.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
        {
            var err = result.TryGetProperty("error", out var e) ? e.ToString() : "(no error body)";
            throw new PadError("PA031", $"apiproxy refused {method}", method, err);
        }
        return result.TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    /// <summary>The workspaces this Harness has registered.</summary>
    public static async Task<List<WorkspaceRow>> Workspaces(GatewayAd ad, CancellationToken ct) =>
        GatewayJson.Items<WorkspaceRow>(await Call(ad, "workspace.list", new { }, ct));

    /// <summary>Sessions with their live agent status, exactly what the DSH Web UI lists.</summary>
    public static async Task<List<LiveSession>> Sessions(GatewayAd ad, CancellationToken ct) =>
        GatewayJson.Items<LiveSession>(await Call(ad, "session.list", new { }, ct));

    /// <summary>
    /// Join the two lists into the workspace → session tree. Sessions that no
    /// workspace accounts for still get a node, because hiding them would make PAD
    /// disagree with the Harness about what exists.
    /// </summary>
    public static async Task<List<WorkspaceNode>> Tree(GatewayAd ad, CancellationToken ct)
    {
        var spaces = await Workspaces(ad, ct);
        var sessions = await Sessions(ad, ct);
        var byId = sessions.ToDictionary(s => s.SessionId, s => s);
        var claimed = new HashSet<string>();

        var nodes = new List<WorkspaceNode>();
        foreach (var space in spaces)
        {
            var owned = new List<LiveSession>();
            foreach (var id in space.SessionIds)
            {
                if (!byId.TryGetValue(id, out var s)) continue;
                owned.Add(s);
                claimed.Add(id);
            }
            owned.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            nodes.Add(new WorkspaceNode(space, owned));
        }

        var orphans = sessions.Where(s => !claimed.Contains(s.SessionId)).ToList();
        if (orphans.Count > 0)
        {
            orphans.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            nodes.Add(new WorkspaceNode(
                new WorkspaceRow { Title = "未归入 workspace", Path = "" }, orphans));
        }
        return nodes;
    }

    /// <summary>Cancel the turn running on one session.</summary>
    public static Task<JsonElement> CancelSession(GatewayAd ad, string sessionId, CancellationToken ct) =>
        Call(ad, "session.cancel", new { sessionId }, ct);

    /// <summary>
    /// Official credentials.describe for DEEPSEEK_API_KEY. Null = the rpc failed.
    /// False = the running Harness has no key, which PAD must show as PA116.
    /// </summary>
    public static async Task<bool?> DeepseekConfigured(GatewayAd ad, CancellationToken ct)
    {
        try
        {
            var value = await Call(ad, "credentials.describe",
                new { refs = new[] { CredentialsFile.DeepseekRef } }, ct);
            return CredentialsFile.LiveConfigured(value);
        }
        catch
        {
            return null;
        }
    }
}
