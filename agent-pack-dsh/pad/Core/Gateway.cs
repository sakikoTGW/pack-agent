using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

    /// <summary>
    /// 0.2.x: the host's own web listener, which serves the same `client-request`
    /// gateway protocol under <c>/api/&lt;method&gt;</c>. Present only when the
    /// carrier learned it from the host; older carriers leave it empty and PAD
    /// talks to the carrier (which proxies apiproxy). 
    /// </summary>
    [JsonPropertyName("webUrl")] public string? WebUrl { get; set; }
    [JsonPropertyName("apiStyle")] public string? ApiStyle { get; set; }

    /// <summary>True when the ad points PAD at the host web API instead of a proxy.</summary>
    public bool ModernApi => !string.IsNullOrWhiteSpace(WebUrl);

    /// <summary>
    /// Browser-session cookie minted for <see cref="WebUrl"/>. PAD cannot read the
    /// browser's cookie jar, but it can sign its own with the home's persistent
    /// <c>client-connection/browser-session</c> secret (see <see cref="GatewayAuth"/>).
    /// </summary>
    [JsonIgnore] public string? WebCookie { get; set; }

    /// <summary>The DSH home this ad was read from (lets PAD read local registries).</summary>
    [JsonIgnore] public string? Home { get; set; }

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
            if (ad is not { Valid: true }) return null;
            ad.Home = home;
            if (ad.ModernApi)
                ad.WebCookie = GatewayAuth.MintBrowserCookie(home, ad.WebUrl!);
            return ad;
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
        if (ad.ModernApi)
            return await CallWebApi(ad, method.Replace('.', '/'), ModernArgs(method, payload), ct);

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

    /// <summary>
    /// 0.2.x host web API: the same `client-request` envelope, but the endpoint is
    /// <c>/api/&lt;namespace&gt;/&lt;method&gt;</c> and the payload is a single <c>args</c>
    /// field (some endpoints also require an <c>_request</c>/<c>request</c> wrapper).
    /// </summary>
    static async Task<JsonElement> CallWebApi(GatewayAd ad, string endpoint, object? args, CancellationToken ct)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            type = "client-request",
            rpcId = Guid.NewGuid().ToString(),
            method = endpoint,
            payload = new { args = args ?? new { } },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ad.WebUrl!.TrimEnd('/')}/api/{endpoint}")
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(ad.WebCookie))
            req.Headers.TryAddWithoutValidation("Cookie", ad.WebCookie);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(Timeout);
        using var res = await Http.SendAsync(req, linked.Token);
        var body = await res.Content.ReadAsStringAsync(linked.Token);
        if (!res.IsSuccessStatusCode)
            throw new PadError("PA030", $"web api returned HTTP {(int)res.StatusCode}", endpoint,
                body, ["确认这个实例的 DSH 版本支持 /api gateway"]);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var result))
            throw new PadError("PA030", "web api answer has no result", endpoint, body);

        if (result.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
        {
            var err = result.TryGetProperty("error", out var e) ? e.ToString() : "(no error body)";
            throw new PadError("PA031", $"web api refused {endpoint}", endpoint, err);
        }
        return result.TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    /// <summary>Translate the old dotted method + payload into 0.2.x args.</summary>
    static object? ModernArgs(string method, object? payload) => method switch
    {
        "session.list" => new { _request = new { } },
        "session.cancel" => new { request = new { sessionId = SessionIdOf(payload) } },
        _ => payload ?? new { },
    };

    static string SessionIdOf(object? payload)
    {
        try
        {
            var element = JsonSerializer.SerializeToElement(payload);
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("sessionId", out var id)
                && id.ValueKind == JsonValueKind.String)
                return id.GetString() ?? "";
        }
        catch
        {
            // fall through to empty
        }
        return "";
    }

    /// <summary>Map 0.2.x <c>session/list</c> items onto PAD's row model.</summary>
    static List<LiveSession> MapModernSessions(JsonElement value)
    {
        var list = new List<LiveSession>();
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in items.EnumerateArray())
        {
            var row = new LiveSession
            {
                SessionId = item.TryGetProperty("sessionId", out var id) && id.ValueKind == JsonValueKind.String
                    ? id.GetString() ?? ""
                    : "",
                UpdatedAt = item.TryGetProperty("updatedAt", out var updated) && updated.ValueKind == JsonValueKind.Number
                    ? updated.GetInt64()
                    : 0,
                Running = item.TryGetProperty("running", out var running) && running.ValueKind == JsonValueKind.True,
                Blank = item.TryGetProperty("blank", out var blank) && blank.ValueKind == JsonValueKind.True,
            };
            if (item.TryGetProperty("projections", out var projections)
                && projections.TryGetProperty("values", out var values)
                && values.TryGetProperty("agentPreset", out var preset)
                && preset.ValueKind == JsonValueKind.String)
                row.AgentPreset = preset.GetString();
            list.Add(row);
        }
        return list;
    }

    /// <summary>The workspaces this Harness has registered.</summary>
    public static async Task<List<WorkspaceRow>> Workspaces(GatewayAd ad, CancellationToken ct)
    {
        if (!ad.ModernApi)
            return GatewayJson.Items<WorkspaceRow>(await Call(ad, "workspace.list", new { }, ct));

        // 0.2.x has no workspace.list RPC, but the workspace registry is a plain
        // file in the home — read the same data the Web UI groups by.
        var registry = ad.Home is { Length: > 0 } home ? ReadWorkspaceRegistry(home) : null;
        return registry is { Count: > 0 }
            ? registry
            : [new WorkspaceRow { Title = "会话", Path = "" }];
    }

    /// <summary>Parse <c>&lt;home&gt;/storages/workspace.json</c> (unit v2: tables.workspaces).</summary>
    static List<WorkspaceRow>? ReadWorkspaceRegistry(string home)
    {
        try
        {
            var path = Path.Combine(home, "storages", "workspace.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("tables", out var tables)
                || !tables.TryGetProperty("workspaces", out var workspaces)
                || workspaces.ValueKind != JsonValueKind.Object)
                return null;

            var rows = new List<WorkspaceRow>();
            foreach (var entry in workspaces.EnumerateObject())
            {
                var row = new WorkspaceRow { WorkspaceId = entry.Name };
                var value = entry.Value;
                if (value.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                    row.Path = p.GetString() ?? "";
                if (value.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                    row.Title = t.GetString() ?? "";
                if (value.TryGetProperty("sessionIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    row.SessionIds = ids.EnumerateArray()
                        .Where(id => id.ValueKind == JsonValueKind.String)
                        .Select(id => id.GetString() ?? "")
                        .Where(id => id.Length > 0)
                        .ToList();
                if (value.TryGetProperty("createdAt", out var created) && created.ValueKind == JsonValueKind.String)
                    row.CreatedAt = created.GetString();
                if (value.TryGetProperty("updatedAt", out var updated) && updated.ValueKind == JsonValueKind.String)
                    row.UpdatedAt = updated.GetString();
                rows.Add(row);
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.Title, b.Title));
            return rows;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sessions with their live agent status, exactly what the DSH Web UI lists.</summary>
    public static async Task<List<LiveSession>> Sessions(GatewayAd ad, CancellationToken ct) =>
        ad.ModernApi
            ? MapModernSessions(await Call(ad, "session.list", new { }, ct))
            : GatewayJson.Items<LiveSession>(await Call(ad, "session.list", new { }, ct));

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

/// <summary>
/// Mint the same browser-session cookie DSH's web server hands out after the
/// token redirect. The cookie name is <c>dsh-auth-</c> + base64url(sha256(authority));
/// the value is <c>v1.&lt;base64url(payload)&gt;.&lt;base64url(hmac)&gt;</c> signed with the
/// persistent <c>client-connection/browser-session</c> secret stored in
/// <c>&lt;home&gt;/.credentials.yaml</c>. This lets PAD read the 0.2.x web API with
/// the same rights the browser has, without touching the browser's cookie jar.
/// </summary>
static class GatewayAuth
{
    /// <summary>Full <c>name=value</c> Cookie header for the given web origin, or null.</summary>
    public static string? MintBrowserCookie(string home, string webUrl)
    {
        try
        {
            if (!Uri.TryCreate(webUrl, UriKind.Absolute, out var uri)) return null;
            var secret = ReadSecret(home);
            if (secret is null) return null;

            var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            var name = "dsh-auth-" + Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(authority)));

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var json = "{\"version\":1,\"authority\":\"" + authority + "\",\"issuedAt\":" + now
                       + ",\"expiresAt\":" + (now + 7 * 24 * 3600_000L) + "}";
            var body = Base64Url(Encoding.UTF8.GetBytes(json));
            using var hmac = new HMACSHA256(secret);
            var signature = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
            return $"{name}=v1.{body}.{signature}";
        }
        catch
        {
            return null;
        }
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Read and decode the browser-session secret from the home credentials file.</summary>
    static byte[]? ReadSecret(string home)
    {
        var path = Path.Combine(home, ".credentials.yaml");
        if (!File.Exists(path)) return null;

        var inBlock = false;
        string? encoded = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.TrimEnd();
            if (line.Contains("client-connection/browser-session:", StringComparison.Ordinal))
            {
                inBlock = true;
                continue;
            }
            if (!inBlock) continue;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("secret:", StringComparison.Ordinal))
            {
                encoded = trimmed["secret:".Length..].Trim().Trim('"', '\'');
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(encoded)) return null;

        var padded = encoded.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        var bytes = Convert.FromBase64String(padded);
        return bytes.Length == 32 ? bytes : null;
    }
}
