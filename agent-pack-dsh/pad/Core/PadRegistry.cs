using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// One row in a launcher registry. Same overlay rules as DSH: builtin ids cannot
/// be deleted; a user file may set <c>disabled</c> or add new ids.
/// </summary>
public sealed class RegistryEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Handler { get; set; } = "";
    public bool Disabled { get; set; }
}

/// <summary>
/// Loads <c>modpack/registries/*.json</c> then overlays
/// <c>&lt;root&gt;/library/registries/</c>. This is how the launcher itself is
/// everything-is-a-plugin: pages and handlers are table rows, not hardcoded kinds.
/// </summary>
public static class PadRegistry
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<RegistryEntry> Load(Launcher launcher, string name)
    {
        var builtin = Read(FindBuiltin(name));
        if (builtin.Count == 0) builtin = ReadEmbedded(name);
        var user = Read(Path.Combine(launcher.Root, launcher.Settings().Other.RegistriesRel, name + ".json"));
        return Merge(builtin, user);
    }

    public static bool PageEnabled(Launcher launcher, string id)
    {
        if (id is "launch" or "settings") return true;
        var row = Load(launcher, "pad-pages").FirstOrDefault(e => e.Id == id);
        return row is null || !row.Disabled;
    }

    static List<RegistryEntry> Merge(List<RegistryEntry> builtin, List<RegistryEntry> user)
    {
        var map = new Dictionary<string, RegistryEntry>(StringComparer.Ordinal);
        foreach (var e in builtin)
        {
            if (string.IsNullOrWhiteSpace(e.Id)) continue;
            map[e.Id] = e;
        }
        foreach (var e in user)
        {
            if (string.IsNullOrWhiteSpace(e.Id)) continue;
            if (map.TryGetValue(e.Id, out var have))
            {
                if (!string.IsNullOrWhiteSpace(e.Title)) have.Title = e.Title;
                if (!string.IsNullOrWhiteSpace(e.Handler)) have.Handler = e.Handler;
                have.Disabled = e.Disabled;
            }
            else map[e.Id] = e;
        }
        return [.. map.Values];
    }

    static List<RegistryEntry> Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return [];
        try { return ParseEntries(File.ReadAllText(path)); }
        catch { return []; }
    }

    static List<RegistryEntry> ReadEmbedded(string name)
    {
        var file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        if (res is null) return [];
        using var stream = asm.GetManifestResourceStream(res);
        if (stream is null) return [];
        using var reader = new StreamReader(stream);
        try { return ParseEntries(reader.ReadToEnd()); }
        catch { return []; }
    }

    static List<RegistryEntry> ParseEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("entries", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<RegistryEntry>();
        foreach (var el in arr.EnumerateArray())
        {
            var row = el.Deserialize<RegistryEntry>(Json);
            if (row is not null && !string.IsNullOrWhiteSpace(row.Id)) list.Add(row);
        }
        return list;
    }

    static string? FindBuiltin(string name)
    {
        var file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        foreach (var dir in Probe())
        {
            var path = Path.Combine(dir, file);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    static IEnumerable<string> Probe()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "registries");
        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && walk is not null; i++, walk = walk.Parent)
        {
            yield return Path.Combine(walk.FullName, "agent-pack-dsh", "modpack", "registries");
            yield return Path.Combine(walk.FullName, "modpack", "registries");
        }
    }
}
