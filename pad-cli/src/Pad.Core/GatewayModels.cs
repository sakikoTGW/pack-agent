using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// One workspace as DSH's registry reports it. Workspaces are a real registry in
/// DSH, not a path convention, so PAD lists them instead of inferring them.
/// </summary>
public sealed class WorkspaceRow
{
    [JsonPropertyName("workspaceId")] public string WorkspaceId { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("sessionIds")] public List<string> SessionIds { get; set; } = [];
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
}

/// <summary>
/// One session row from <c>session.list</c>. <see cref="Running"/> is the live agent's
/// status and <see cref="Blank"/> is the lock that decides whether its preset can
/// still be switched.
/// </summary>
public sealed class LiveSession
{
    [JsonPropertyName("sessionId")] public string SessionId { get; set; } = "";
    [JsonPropertyName("updatedAt")] public long UpdatedAt { get; set; }
    [JsonPropertyName("running")] public bool Running { get; set; }
    [JsonPropertyName("blank")] public bool Blank { get; set; }
    [JsonPropertyName("agentPreset")] public string? AgentPreset { get; set; }

    public string StatusText => Running ? "在跑" : Blank ? "空会话" : "空转";
    public string PresetText => string.IsNullOrEmpty(AgentPreset) ? "默认 preset" : AgentPreset!;

    public string UpdatedText => UpdatedAt <= 0
        ? ""
        : DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAt).ToLocalTime().ToString("MM-dd HH:mm");

    /// <summary>Presets can only be swapped while a session is blank; DSH answers
    /// <c>agent-preset-locked</c> otherwise.</summary>
    public bool CanSwitchPreset => Blank;
}

/// <summary>A workspace with its sessions resolved, for the management tree.</summary>
public sealed class WorkspaceNode(WorkspaceRow row, List<LiveSession> sessions) : Observable
{
    public WorkspaceRow Row { get; } = row;
    public List<LiveSession> Sessions { get; } = sessions;

    public string Title => Row.Title.Length > 0 ? Row.Title : Row.Path;
    public string Path => Row.Path;
    public int RunningCount => Sessions.Count(s => s.Running);

    public string Summary
    {
        get
        {
            var live = RunningCount;
            var total = Sessions.Count;
            if (total == 0) return "没有 session";
            return live > 0 ? $"{total} 条 session · {live} 个在跑" : $"{total} 条 session · 都空转";
        }
    }
}

public static class GatewayJson
{
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static List<T> Items<T>(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return items.Deserialize<List<T>>(Opts) ?? [];
    }
}
