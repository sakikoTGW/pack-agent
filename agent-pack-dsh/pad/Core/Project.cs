using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>DSH release/profile a project pins. Empty = follow pad.json.</summary>
public sealed class ProjectDsh
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("profile")] public string? Profile { get; set; }
}

/// <summary>How the project's window is opened. <c>web</c> = Edge app window; <c>shell</c> = third-party DSH Desktop.</summary>
public sealed class ProjectWindow
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "web";
    [JsonPropertyName("shellExe")] public string? ShellExe { get; set; }
}

/// <summary>
/// A project is the binding layer: a set of workspace directories plus the agent presets
/// bound to them. The runtime body stays an <see cref="Instance"/> (one DSH_HOME); a project
/// points at it through <see cref="Instance"/>. See the design doc §3.
/// </summary>
public sealed class Project
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.launcher.project/v1";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("workspaces")] public List<string> Workspaces { get; set; } = [];
    [JsonPropertyName("agents")] public List<string> Agents { get; set; } = [];
    [JsonPropertyName("dsh")] public ProjectDsh Dsh { get; set; } = new();
    [JsonPropertyName("window")] public ProjectWindow Window { get; set; } = new();
    [JsonPropertyName("instance")] public string? Instance { get; set; }
    [JsonPropertyName("lastRun")] public string? LastRun { get; set; }
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
}
