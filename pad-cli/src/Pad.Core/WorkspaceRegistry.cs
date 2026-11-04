using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// DSH's own workspace registry: <c>&lt;DSH_HOME&gt;/storages/workspace.json</c> (unit
/// <c>workspace</c>, version 2). PAD only ever *reads* DSH data; this file is what makes a
/// project window show exactly the project's workspaces. DSH fails loudly on a bad file, so
/// every write validates first, backs up the old file, and is atomic.
/// Schema source: <c>dsh-workspace/lib/index.js</c> (workspaceDomainSpec) — see the design doc §2.
/// </summary>
public static class WorkspaceRegistry
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly HashSet<string> GenericLeaf = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "workspace", "harness", "dsh-home", "projects", "project", "app", "src", "code",
    };

    public sealed class Unit
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "workspace";
        [JsonPropertyName("version")] public int Version { get; set; } = 2;
    }

    public sealed class Global
    {
        [JsonPropertyName("initialized")] public bool Initialized { get; set; }
        [JsonPropertyName("workspaceIds")] public List<string> WorkspaceIds { get; set; } = [];
        [JsonPropertyName("archivedSessionIds")] public List<string> ArchivedSessionIds { get; set; } = [];
        [JsonPropertyName("pinnedSessionIds")] public List<string> PinnedSessionIds { get; set; } = [];
        [JsonPropertyName("defaultWorkspaceId")] public string? DefaultWorkspaceId { get; set; }
    }

    public sealed class Record
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("sessionIds")] public List<string> SessionIds { get; set; } = [];
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
    }

    public sealed class Tables
    {
        [JsonPropertyName("workspaces")] public Dictionary<string, Record> Workspaces { get; set; } = [];
    }

    public sealed class Doc
    {
        [JsonPropertyName("unit")] public Unit Unit { get; set; } = new();
        [JsonPropertyName("global")] public Global Global { get; set; } = new();
        [JsonPropertyName("tables")] public Tables Tables { get; set; } = new();
    }

    public static string PathFor(string home) => Path.Combine(home, "storages", "workspace.json");

    public static Doc? Read(string home) => ReadFile(PathFor(home));

    public static Doc? ReadFile(string file)
    {
        if (!File.Exists(file)) return null;
        try { return JsonSerializer.Deserialize<Doc>(File.ReadAllText(file), Json); }
        catch (JsonException) { return null; }
    }

    public sealed class MergeResult
    {
        public Doc Doc { get; init; } = new();
        public int Kept { get; init; }
        public int Added { get; init; }
        public List<string> Paths { get; init; } = [];
    }

    /// <summary>
    /// Build the registry a project window should have: exactly the bound workspaces, in the
    /// project's order. Existing records keep their uuid / <c>sessionIds</c> / title so a second
    /// launch never loses session attribution; only new directories get a fresh uuid.
    /// </summary>
    public static MergeResult Merge(Doc? existing, IReadOnlyList<string> workspaces)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        var old = existing?.Tables.Workspaces ?? new Dictionary<string, Record>();

        var byPath = new Dictionary<string, (string Id, Record Rec)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, rec) in old)
        {
            try { byPath[Projects.NormPath(rec.Path)] = (id, rec); }
            catch { /* a malformed old path cannot be matched; it simply drops out */ }
        }

        var doc = new Doc
        {
            Unit = new Unit(),
            Global = new Global
            {
                Initialized = true,
                ArchivedSessionIds = [.. existing?.Global.ArchivedSessionIds ?? []],
                PinnedSessionIds = [.. existing?.Global.PinnedSessionIds ?? []],
            },
        };

        int kept = 0, added = 0;
        var defaultFromOld = existing?.Global.DefaultWorkspaceId;
        var defaultStillBound = false;
        foreach (var path in workspaces)
        {
            var norm = Projects.NormPath(path);
            string id;
            Record rec;
            if (byPath.TryGetValue(norm, out var hit))
            {
                id = hit.Id;
                rec = hit.Rec;
                rec.UpdatedAt = now;
                kept++;
                if (string.Equals(id, defaultFromOld, StringComparison.Ordinal)) defaultStillBound = true;
            }
            else
            {
                id = Guid.NewGuid().ToString();
                rec = new Record { Path = norm, Title = LeafTitle(norm), CreatedAt = now, UpdatedAt = now };
                added++;
            }
            doc.Tables.Workspaces[id] = rec;
            doc.Global.WorkspaceIds.Add(id);
        }

        doc.Global.DefaultWorkspaceId = defaultStillBound
            ? defaultFromOld
            : doc.Global.WorkspaceIds.FirstOrDefault();

        return new MergeResult
        {
            Doc = doc, Kept = kept, Added = added,
            Paths = [.. doc.Global.WorkspaceIds.Select(id => doc.Tables.Workspaces[id].Path)],
        };
    }

    /// <summary>Invariants DSH itself validates on boot; a violation must never be written.</summary>
    public static string? Check(Doc doc)
    {
        if (doc.Unit.Name != "workspace" || doc.Unit.Version != 2)
            return $"unit must be workspace/2, got {doc.Unit.Name}/{doc.Unit.Version}";
        var ids = doc.Global.WorkspaceIds;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id)) return $"workspaceIds repeats {id}";
            if (!doc.Tables.Workspaces.ContainsKey(id)) return $"workspaceIds references missing workspace {id}";
        }
        if (doc.Global.Initialized && seen.Count != doc.Tables.Workspaces.Count)
            return "initialized registry order and table sizes differ";
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in doc.Tables.Workspaces.Values)
        {
            if (string.IsNullOrWhiteSpace(rec.Path)) return "workspace record has no path";
            if (!paths.Add(rec.Path)) return $"duplicate workspace path {rec.Path}";
        }
        var sessions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rec in doc.Tables.Workspaces.Values)
            foreach (var sid in rec.SessionIds)
                if (!sessions.Add(sid)) return $"session {sid} is claimed by two workspaces";
        if (doc.Global.DefaultWorkspaceId is { } def && !doc.Tables.Workspaces.ContainsKey(def))
            return $"defaultWorkspaceId {def} is not in the table";
        return null;
    }

    /// <summary>Validate → back up the old file → atomic write. Throws PA124 and leaves the
    /// old file untouched when anything is off (DSH would refuse to boot).</summary>
    public static void Write(string home, Doc doc)
    {
        var why = Check(doc);
        var file = PathFor(home);
        if (why is not null)
            throw new PadError("PA124", "could not write the workspace registry", file, why,
                ["PAD 不会写坏 DSH 的注册表；先修 projects/<id>/project.json 再 seed"]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        if (File.Exists(file))
        {
            try { File.Copy(file, file + ".bak", overwrite: true); }
            catch (IOException) { /* backup is best effort; the write below is still atomic */ }
        }
        var tmp = file + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, Json), new UTF8Encoding(false));
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>Display title for a directory: its leaf name, skipping generic container names
    /// (same rule as RELATIONS.md labels).</summary>
    public static string LeafTitle(string path)
    {
        string norm;
        try { norm = Projects.NormPath(path); }
        catch { return path; }
        var segs = norm.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) return norm;
        var leaf = segs[^1];
        if (!GenericLeaf.Contains(leaf)) return leaf;
        return segs.Length >= 2 ? segs[^2] : leaf;
    }
}
