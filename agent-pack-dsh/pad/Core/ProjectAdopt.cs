namespace Pad.Core;

/// <summary>One suggested project discovered from an existing DSH home.</summary>
public sealed record AdoptGroup(string Id, string Name, List<string> Workspaces);

/// <summary>
/// 自动收编：read an existing DSH home's workspace registry (read-only) and group its
/// workspaces into candidate projects, so a new user never has to type directories in.
/// Grouping is by directory name, skipping generic container names; it is a suggestion —
/// renaming / merging stays a one-off user action.
/// </summary>
public static class ProjectAdopt
{
    static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "workspace", "harness", "dsh-home", "projects", "project", "app", "src", "code",
    };

    /// <summary>Which project a directory belongs to, from its own path alone.</summary>
    public static string GroupKey(string path)
    {
        string norm;
        try { norm = Projects.NormPath(path); }
        catch { return path; }
        var segs = norm.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.Length > 1 || s[0] != ':')
            .ToArray();
        if (segs.Length == 0) return norm;
        for (var i = segs.Length - 1; i >= Math.Max(0, segs.Length - 3); i--)
            if (!Generic.Contains(segs[i])) return segs[i];
        return segs[^1];
    }

    public static List<AdoptGroup> Scan(WorkspaceRegistry.Doc? doc)
    {
        if (doc is null) return [];
        var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var rec in doc.Tables.Workspaces.Values)
        {
            if (string.IsNullOrWhiteSpace(rec.Path)) continue;
            var key = GroupKey(rec.Path);
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
                order.Add(key);
            }
            list.Add(rec.Path);
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<AdoptGroup>();
        foreach (var key in order.OrderByDescending(k => byKey[k].Count).ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var name = key.Trim();
            var id = Projects.SafeId(name);
            var baseId = id;
            for (var n = 2; !used.Add(id); n++) id = $"{baseId}-{n}";
            groups.Add(new AdoptGroup(id, name, byKey[key]));
        }
        return groups;
    }

    public static List<Project> Materialize(IEnumerable<AdoptGroup> groups, string? version, string? profile)
    {
        var now = DateTimeOffset.UtcNow.ToString("o");
        return groups.Select(g => new Project
        {
            Id = g.Id,
            Name = g.Name,
            Workspaces = [.. g.Workspaces],
            Agents = [],
            Dsh = new ProjectDsh { Version = version, Profile = profile },
            Window = new ProjectWindow { Kind = "web" },
            Created = now,
            Updated = now,
        }).ToList();
    }
}
