using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pad.Core;

/// <summary>
/// Minimal MCP (Model Context Protocol) server over stdio: an AI coding agent can drive
/// the development-session loop without parsing human text. One JSON object per line in,
/// one per line out; diagnostics go to stderr so stdout stays protocol-clean.
/// </summary>
public static class Mcp
{
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    sealed record ToolDef(string Name, string Desc, JsonObject Schema);

    static readonly ToolDef[] Tools =
    [
        new("dev_start", "打开一个开发会话（不改实例）；返回 session 与 sourceHash",
            Schema(("instance", "string", "实例 id"), ("profile", "string", "profile 名"))),
        new("dev_status", "读取当前开发会话 + 未固化改动 + 运行事实",
            Schema(("instance", "string", "实例 id"))),
        new("dev_apply", "在开发会话里应用一项改动（只影响本次运行）",
            Schema(("instance", "string", "实例 id"), ("profile", "string", "profile 名"),
                ("action", "string", "add | remove"), ("spec", "string", "包名或 file: 路径"))),
        new("dev_verify", "跑验证判据并返回 verdict（config-compiles / plugins-resolve / source-untouched）",
            Schema(("instance", "string", "实例 id"), ("profile", "string", "profile 名"))),
        new("dev_commit", "固化本次会话（自动打点）；验证失败默认拒绝，force=true 才继续",
            Schema(("instance", "string", "实例 id"), ("force", "boolean", "验证失败仍固化"))),
        new("dev_discard", "丢弃本次会话，实例不变",
            Schema(("instance", "string", "实例 id"))),
        new("dev_list", "列出该实例的开发会话历史",
            Schema(("instance", "string", "实例 id"))),
    ];

    static JsonObject Schema(params (string Name, string Type, string Desc)[] props)
    {
        var properties = new JsonObject();
        foreach (var (name, type, desc) in props)
            properties[name] = new JsonObject { ["type"] = type, ["description"] = desc };
        return new JsonObject { ["type"] = "object", ["properties"] = properties };
    }

    public static async Task<int> ServeAsync(Launcher launcher, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await Console.In.ReadLineAsync(ct);
            if (line is null) break;
            line = line.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonObject? request;
            try { request = JsonNode.Parse(line) as JsonObject; }
            catch
            {
                Write(new JsonObject
                {
                    ["jsonrpc"] = "2.0", ["id"] = null,
                    ["error"] = new JsonObject { ["code"] = -32700, ["message"] = "parse error" },
                });
                continue;
            }
            if (request is null) continue;

            var id = request["id"];
            var method = request["method"]?.GetValue<string>() ?? "";
            var notification = id is null;

            JsonNode? result = null;
            JsonObject? error = null;
            try
            {
                result = method switch
                {
                    "initialize" => new JsonObject
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "pad", ["version"] = "0.1.0" },
                    },
                    "notifications/initialized" or "ping" => new JsonObject(),
                    "tools/list" => new JsonObject
                    {
                        ["tools"] = new JsonArray(Tools.Select(t => (JsonNode)new JsonObject
                        {
                            ["name"] = t.Name,
                            ["description"] = t.Desc,
                            ["inputSchema"] = t.Schema,
                        }).ToArray()),
                    },
                    "tools/call" => await CallToolAsync(launcher, request, ct),
                    _ => throw new McpMethodNotFound(method),
                };
            }
            catch (McpMethodNotFound ex)
            {
                error = new JsonObject { ["code"] = -32601, ["message"] = "method not found: " + ex.Method };
            }
            catch (PadError pe)
            {
                result = ToolError(pe.Render());
            }
            catch (Exception ex)
            {
                result = ToolError(ex.Message);
            }

            if (notification) continue;
            var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone() };
            if (error is not null) response["error"] = error;
            else response["result"] = result ?? new JsonObject();
            Write(response);
        }
        return 0;
    }

    static async Task<JsonNode> CallToolAsync(Launcher launcher, JsonObject request, CancellationToken ct)
    {
        var parameters = request["params"] as JsonObject;
        var name = parameters?["name"]?.GetValue<string>() ?? "";
        var args = parameters?["arguments"] as JsonObject ?? new JsonObject();
        var instanceId = Str(args, "instance");
        var profile = Str(args, "profile");

        object payload;
        switch (name)
        {
            case "dev_start":
            {
                var inst = Inst(launcher, instanceId);
                var prof = Profile(launcher, inst, profile);
                payload = DevSessions.Open(launcher, inst, prof);
                break;
            }
            case "dev_status":
            {
                var inst = Inst(launcher, instanceId);
                var session = DevSessions.Load(launcher, inst.Id);
                var pending = session is null
                    ? new List<TrialRecord>()
                    : new Trials(launcher).Pending(inst, session.Profile);
                payload = new
                {
                    session,
                    pending,
                    running = launcher.Runtime().Entries
                        .Where(e => e.Instance == inst.Id && Proc.Alive(e.Pid))
                        .ToList(),
                };
                break;
            }
            case "dev_apply":
            {
                var inst = Inst(launcher, instanceId);
                payload = await DevOps.ApplyAsync(launcher, inst, Profile(launcher, inst, profile),
                    Str(args, "action") ?? "add",
                    Str(args, "spec") ?? throw new PadError("PA244", "missing spec", "dev_apply", null,
                        ["传 arguments.spec"]), null, ct);
                break;
            }
            case "dev_verify":
            {
                var inst = Inst(launcher, instanceId);
                payload = await DevOps.VerifyAsync(launcher, inst, Profile(launcher, inst, profile), null, ct);
                break;
            }
            case "dev_commit":
            {
                var inst = Inst(launcher, instanceId);
                var force = args["force"]?.GetValue<bool>() == true;
                payload = DevOps.Commit(launcher, inst, dryRun: false, force: force, log: null);
                break;
            }
            case "dev_discard":
            {
                var inst = Inst(launcher, instanceId);
                payload = await DevOps.DiscardAsync(launcher, inst, null, ct);
                break;
            }
            case "dev_list":
                payload = DevSessions.History(launcher, Inst(launcher, instanceId).Id);
                break;
            default:
                return ToolError("unknown tool: " + name);
        }
        var text = JsonSerializer.Serialize(payload, Pretty);
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["structuredContent"] = JsonNode.Parse(JsonSerializer.Serialize(payload, Compact)),
            ["isError"] = false,
        };
    }

    static Instance Inst(Launcher launcher, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new PadError("PA240", "missing instance", "arguments.instance", null,
                ["传 arguments.instance"]);
        return launcher.Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);
    }

    static string Profile(Launcher launcher, Instance inst, string? profile) =>
        profile ?? inst.LastProfile ?? launcher.Profiles(inst).FirstOrDefault()?.Name
        ?? throw new PadError("PA240", "instance has no profile", $"instances/{inst.Id}", null,
            ["先给实例一个 profile"]);

    static string? Str(JsonObject args, string name) =>
        args[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static JsonNode ToolError(string message) => new JsonObject
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message }),
        ["isError"] = true,
    };

    static void Write(JsonObject obj)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(obj, Compact));
        Console.Out.Flush();
    }

    sealed class McpMethodNotFound(string method) : Exception("method not found: " + method)
    {
        public string Method { get; } = method;
    }
}
