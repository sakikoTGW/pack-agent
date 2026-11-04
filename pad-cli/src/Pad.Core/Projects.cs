using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// Project store: <c>&lt;launcher-root&gt;/projects/&lt;id&gt;/project.json</c> is the index,
/// and an optional <c>&lt;workspace&gt;/.pad/project.json</c> inside the project itself
/// overrides it (空值 = 跟随索引；项目内优先). Read-only on DSH data.
/// </summary>
public sealed class Projects(Launcher launcher)
{
    public const string Schema = "pack-agent.launcher.project/v1";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);

    public string Root => Path.Combine(launcher.Root, "projects");
    public string DirOf(string id) => Path.Combine(Root, id);
    public string FileOf(string id) => Path.Combine(DirOf(id), "project.json");

    // ---- paths -----------------------------------------------------------

    public static string NormPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return string.Equals(NormPath(a), NormPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>ASCII slug; for names with no ascii characters a stable hash id is used instead
    /// (Chinese project names are normal here, so Slug alone is not enough).</summary>
    public static string SafeId(string name)
    {
        var slug = Launcher.Slug(name);
        if (slug.Length > 0) return slug;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name.Trim()));
        return "p-" + Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    // ---- read / write ----------------------------------------------------

    public List<Project> List()
    {
        var list = new List<Project>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var p = ReadFile(Path.Combine(dir, "project.json"));
            if (p is not null) list.Add(WithLocalOverlay(p));
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public Project? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var file = FileOf(id.Trim());
        var p = ReadFile(file);
        return p is null ? null : WithLocalOverlay(p);
    }

    public void Save(Project p)
    {
        Normalize(p);
        Validate(p);
        WriteAtomic(FileOf(p.Id), JsonSerializer.Serialize(p, Json));
    }

    public bool Remove(string id)
    {
        var dir = DirOf(id.Trim());
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    /// <summary>The file actually written (no local overlay). Used by adopt, which must not
    /// copy a user's in-project overrides back into the index.</summary>
    public Project? ReadIndex(string id) => ReadFile(FileOf(id.Trim()));

    static Project? ReadFile(string file)
    {
        if (!File.Exists(file)) return null;
        try { return JsonSerializer.Deserialize<Project>(File.ReadAllText(file), Json); }
        catch { return null; }
    }

    /// <summary>Overlay <c>&lt;workspace&gt;/.pad/project.json</c> on top of the index.
    /// Only non-empty fields win; a malformed file is ignored (it lives in the user's repo).</summary>
    Project WithLocalOverlay(Project p)
    {
        foreach (var ws in p.Workspaces)
        {
            string? file;
            try { file = Path.Combine(NormPath(ws), ".pad", "project.json"); }
            catch { continue; }
            if (!File.Exists(file)) continue;
            Project? local;
            try { local = JsonSerializer.Deserialize<Project>(File.ReadAllText(file), Json); }
            catch { return p; }
            if (local is null) return p;
            if (local.Workspaces.Count > 0) p.Workspaces = local.Workspaces;
            if (local.Agents.Count > 0) p.Agents = local.Agents;
            if (!string.IsNullOrWhiteSpace(local.Dsh.Version)) p.Dsh.Version = local.Dsh.Version;
            if (!string.IsNullOrWhiteSpace(local.Dsh.Profile)) p.Dsh.Profile = local.Dsh.Profile;
            if (!string.IsNullOrWhiteSpace(local.Window.Kind)) p.Window.Kind = local.Window.Kind;
            if (!string.IsNullOrWhiteSpace(local.Window.ShellExe)) p.Window.ShellExe = local.Window.ShellExe;
            if (!string.IsNullOrWhiteSpace(local.Instance)) p.Instance = local.Instance;
            return p;
        }
        return p;
    }

    // ---- rules -----------------------------------------------------------

    public static void Normalize(Project p)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        p.Schema = Schema;
        p.Name = (p.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(p.Id)) p.Id = SafeId(p.Name);
        p.Id = p.Id.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = p.Id;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workspaces = new List<string>();
        foreach (var w in p.Workspaces)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;
            string norm;
            try { norm = NormPath(w); }
            catch { workspaces.Add(w.Trim()); continue; }
            if (seen.Add(norm)) workspaces.Add(norm);
        }
        p.Workspaces = workspaces;

        var agents = new List<string>();
        var seenAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in p.Agents)
        {
            var t = (a ?? "").Trim();
            if (t.Length > 0 && seenAgents.Add(t)) agents.Add(t);
        }
        p.Agents = agents;

        p.Dsh ??= new ProjectDsh();
        p.Dsh.Version = string.IsNullOrWhiteSpace(p.Dsh.Version) ? null : p.Dsh.Version.Trim();
        p.Dsh.Profile = string.IsNullOrWhiteSpace(p.Dsh.Profile) ? null : p.Dsh.Profile.Trim();
        p.Window ??= new ProjectWindow();
        if (p.Window.Kind is not ("web" or "shell")) p.Window.Kind = "web";
        p.Window.ShellExe = string.IsNullOrWhiteSpace(p.Window.ShellExe) ? null : p.Window.ShellExe.Trim();
        p.Instance = string.IsNullOrWhiteSpace(p.Instance) ? null : p.Instance.Trim();

        if (string.IsNullOrWhiteSpace(p.Created)) p.Created = now;
        p.Updated = now;
    }

    public static void Validate(Project p)
    {
        if (!string.Equals(p.Schema, Schema, StringComparison.Ordinal))
            throw new PadError("PA122", "project definition is invalid", $"projects/{p.Id}/project.json",
                $"schema is '{p.Schema}', expected '{Schema}'", ["修 schema，或删掉这份 project.json"]);
        if (!IdPattern.IsMatch(p.Id ?? ""))
            throw new PadError("PA122", "project definition is invalid", $"projects/{p.Id}/project.json",
                $"id '{p.Id}' is not a lowercase slug", ["id 只用 a-z 0-9 -"]);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw new PadError("PA122", "project definition is invalid", $"projects/{p.Id}/project.json",
                "name is empty", ["给项目起个名"]);
        if (p.Workspaces.Count == 0)
            throw new PadError("PA122", "project definition is invalid", $"projects/{p.Id}/project.json",
                "no workspaces bound", ["至少绑一个工作区目录：pad cli project add <name> --workspace <dir>"]);
        foreach (var w in p.Workspaces)
        {
            if (!Path.IsPathFullyQualified(w))
                throw new PadError("PA122", "project definition is invalid", $"projects/{p.Id}/project.json",
                    $"workspace path is not absolute: {w}", ["用绝对路径"]);
            if (!Directory.Exists(w))
                throw new PadError("PA123", "project workspace path is not a directory", w,
                    "bound workspace is missing or not a directory", ["建这个目录，或改绑定：pad cli project add"]);
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in p.Workspaces)
            if (!seen.Add(NormPath(w)))
                throw new PadError("PA122", "project definition is invalid", $"projects/{p.Id}/project.json",
                    $"duplicate workspace path: {w}", ["同一个目录只绑一次"]);
    }

    // ---- io --------------------------------------------------------------

    public static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
