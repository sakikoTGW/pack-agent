using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>One file captured before a mutating operation, stored under the checkpoint dir.</summary>
public sealed class CheckpointFile
{
    /// <summary>Path relative to the instance directory, forward slashes.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    /// <summary>File name inside <c>checkpoints/&lt;id&gt;/files</c>.</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";
}

/// <summary>
/// A minimal pre-change snapshot: only the declaration layer that hot/cold install
/// can touch. Sessions, attachments and node_modules are deliberately excluded —
/// node_modules is rebuilt by pnpm, not restored from a copy.
/// </summary>
public sealed class Checkpoint
{
    public const string SchemaName = "pad.checkpoint/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = SchemaName;
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("instance")] public string Instance { get; init; } = "";
    [JsonPropertyName("profile")] public string Profile { get; init; } = "";
    [JsonPropertyName("trigger")] public string Trigger { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("files")] public List<CheckpointFile> Files { get; init; } = [];
}

public static class Checkpoints
{
    public const int Keep = 20;

    public static string Root(Launcher launcher, string instanceId) =>
        Path.Combine(launcher.InstanceDir(instanceId), "checkpoints");

    /// <summary>Snapshot the declaration layer before a change. Never throws for a
    /// missing optional file; a checkpoint with fewer files is still a checkpoint.</summary>
    public static Checkpoint Create(Launcher launcher, Instance inst, string? profile, string trigger)
    {
        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var dir = Path.Combine(Root(launcher, inst.Id), id);
        var filesDir = Path.Combine(dir, "files");
        Directory.CreateDirectory(filesDir);

        var rels = new List<string> { "instance.json", "pack-ledger.json", "trials.json" };
        if (!string.IsNullOrWhiteSpace(profile))
        {
            rels.Add($"home/profiles/{profile}/package.json");
            rels.Add($"home/profiles/{profile}/cordis.patch.yml");
        }

        var files = new List<CheckpointFile>();
        var index = 0;
        foreach (var rel in rels)
        {
            var source = Path.Combine(launcher.InstanceDir(inst.Id), rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source)) continue;
            var name = $"{index:D2}-{Path.GetFileName(rel)}";
            File.Copy(source, Path.Combine(filesDir, name), overwrite: true);
            files.Add(new CheckpointFile { Path = rel, Name = name });
            index++;
        }

        var ck = new Checkpoint
        {
            Id = id,
            Instance = inst.Id,
            Profile = profile ?? "",
            Trigger = trigger,
            CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
            Files = files,
        };
        WriteJson(Path.Combine(dir, "checkpoint.json"), ck);
        Prune(launcher, inst.Id);
        return ck;
    }

    public static List<Checkpoint> List(Launcher launcher, string instanceId)
    {
        var root = Root(launcher, instanceId);
        if (!Directory.Exists(root)) return [];
        var list = new List<Checkpoint>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var file = Path.Combine(dir, "checkpoint.json");
            if (!File.Exists(file)) continue;
            try
            {
                var ck = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(file));
                if (ck is not null && ck.Schema == Checkpoint.SchemaName) list.Add(ck);
            }
            catch { /* a damaged checkpoint must not hide the others */ }
        }
        list.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
        return list;
    }

    public static Checkpoint? Latest(Launcher launcher, string instanceId) =>
        List(launcher, instanceId).FirstOrDefault();

    /// <summary>The newest pre-change checkpoint. <c>pre-restore</c> snapshots are
    /// internal bookkeeping for undoing a rollback; a user-facing undo must skip
    /// them, otherwise "undo" would re-apply the state that was just rolled back.</summary>
    public static Checkpoint? LatestBeforeChange(Launcher launcher, string instanceId) =>
        List(launcher, instanceId).FirstOrDefault(c =>
            !c.Trigger.StartsWith("pre-restore", StringComparison.Ordinal)) ?? Latest(launcher, instanceId);

    public static Checkpoint? Get(Launcher launcher, string instanceId, string id) =>
        List(launcher, instanceId).FirstOrDefault(c => c.Id == id);

    /// <summary>Write a checkpoint's files back. Takes a <c>pre-restore</c> snapshot
    /// first so an undo is itself undoable.</summary>
    public static Checkpoint Restore(Launcher launcher, Instance inst, string checkpointId)
    {
        var ck = Get(launcher, inst.Id, checkpointId)
            ?? throw new PadError("PA203", "checkpoint not found", $"instances/{inst.Id}/checkpoints", checkpointId,
                ["pad cli checkpoint list <instance>"]);

        Checkpoints.Create(launcher, inst, ck.Profile == "" ? null : ck.Profile, $"pre-restore {ck.Id}");

        var dir = Path.Combine(Root(launcher, inst.Id), ck.Id, "files");
        foreach (var file in ck.Files)
        {
            var source = Path.Combine(dir, file.Name);
            if (!File.Exists(source)) continue;
            var target = Path.Combine(launcher.InstanceDir(inst.Id), file.Path.Replace('/', Path.DirectorySeparatorChar));
            WriteAtomic(target, File.ReadAllBytes(source));
        }

        // The in-memory instance came from before the restore; drop it so callers reload.
        return ck;
    }

    public static void Prune(Launcher launcher, string instanceId)
    {
        try
        {
            var root = Root(launcher, instanceId);
            if (!Directory.Exists(root)) return;
            var ordered = Directory.EnumerateDirectories(root)
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var stale in ordered.Skip(Keep))
            {
                try { Directory.Delete(stale, recursive: true); }
                catch { /* a stale checkpoint is not worth failing the caller */ }
            }
        }
        catch { /* pruning is opportunistic */ }
    }

    static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Pretty));
        File.Move(tmp, path, overwrite: true);
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
}
