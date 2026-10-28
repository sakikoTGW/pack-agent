using System.IO;
using System.Linq;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// Read-only measurement of what a DSH home actually puts in front of the model:
/// how many tools, how many tokens of tool schema, how far the context has grown,
/// and how many turns were already folded into compaction summaries.
///
/// Source of truth is DSH's own session projection cache
/// (<c>&lt;home&gt;/storages/session_projcache/sessions/*.json</c>). Nothing here
/// writes to the home, so it is safe to point at a live instance.
/// </summary>
public static class Inspect
{
    public sealed record ToolCost(string Name, int Tokens);

    public sealed record Report
    {
        public string Home { get; init; } = "";
        public bool Found { get; init; }
        public int Sessions { get; init; }
        public string? NewestSession { get; init; }
        public DateTimeOffset? NewestAt { get; init; }
        public string? Model { get; init; }
        public int Tools { get; init; }
        public int ToolsTokens { get; init; }
        public int SystemTokens { get; init; }
        public long ContextTokens { get; init; }
        public long PressureTokens { get; init; }
        public long ContextWindow { get; init; }
        public int Compactions { get; init; }

        /// <summary>The per-tool token bill, biggest first. Empty for an unmeasured home.</summary>
        public IReadOnlyList<ToolCost> ToolList { get; init; } = [];
    }

    /// <summary>
    /// Every DSH home this machine is likely to have: the one PAD is configured with,
    /// plus the two DSH picks by default. Duplicates and missing dirs are dropped.
    /// </summary>
    public static IReadOnlyList<string> DefaultHomes(string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured!);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidates.Add(Path.Combine(appData, "dsh-desktop", "harness"));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(profile, ".dsh"));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();
        foreach (var c in candidates)
        {
            string full;
            try { full = Path.GetFullPath(c); } catch { continue; }
            if (!seen.Add(full)) continue;
            if (Directory.Exists(full)) kept.Add(full);
        }
        return kept;
    }

    public static Report Measure(string home)
    {
        // A directory that is not a DSH home at all must be distinguishable from a home
        // that simply has no sessions yet. Without this, `inspect E:\nope` printed a
        // perfectly plausible row of zeros.
        var looksLikeHome = Directory.Exists(Path.Combine(home, "profiles"))
                         || Directory.Exists(Path.Combine(home, "storages"));

        var dir = Path.Combine(home, "storages", "session_projcache", "sessions");
        if (!Directory.Exists(dir))
            return new Report { Home = home, Found = looksLikeHome };

        string? bestPath = null;
        var bestAt = DateTimeOffset.MinValue;
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            var at = CreatedAt(file);
            if (at is null) continue;
            count++;
            if (at.Value <= bestAt) continue;
            bestAt = at.Value;
            bestPath = file;
        }

        if (bestPath is null)
            return new Report { Home = home, Found = looksLikeHome, Sessions = count };

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(bestPath));
            return Read(doc.RootElement, home, count, bestPath, bestAt);
        }
        catch (Exception ex)
        {
            throw new PadError("PA041", "could not read the session projection cache",
                bestPath, ex.Message, ["pad cli inspect --json", "pad cli sniff"]);
        }
    }

    static DateTimeOffset? CreatedAt(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var ms = doc.RootElement.GetProperty("record").GetProperty("identity").GetProperty("createdAt").GetInt64();
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
        }
        catch { return null; }
    }

    static Report Read(JsonElement root, string home, int sessions, string path, DateTimeOffset at)
    {
        var rows = root.GetProperty("record").GetProperty("rows");

        var tools = new List<ToolCost>();
        var systemTokens = 0;
        if (Row(rows, "contextHeaders") is { } headers &&
            headers.TryGetProperty("headers", out var hdr) &&
            hdr.ValueKind == JsonValueKind.Array && hdr.GetArrayLength() > 0 &&
            hdr[0].TryGetProperty("tools", out var toolArr) &&
            toolArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in toolArr.EnumerateArray())
            {
                if (!t.TryGetProperty("name", out var n)) continue;
                var tok = t.TryGetProperty("tokens", out var tk) && tk.TryGetInt32(out var v) ? v : 0;
                tools.Add(new ToolCost(n.GetString() ?? "", tok));
            }
        }

        var contextTokens = 0L;
        var pressure = 0L;
        var window = 0L;
        if (Row(rows, "contextPressure") is { } cp)
        {
            contextTokens = Long(cp, "surfaceTokens");
            pressure = Long(cp, "pressureTokens");
            window = Long(cp, "contextWindow");
        }

        var compactions = 0;
        if (Row(rows, "contextTimeline") is { } tl)
        {
            systemTokens = (int)Long(tl, "systemTokens");
            if (tl.TryGetProperty("archived", out var arch) && arch.ValueKind == JsonValueKind.Array)
                compactions = arch.GetArrayLength();
        }

        string? model = null;
        if (Row(rows, "modelSelection") is { } ms &&
            ms.TryGetProperty("lastUsed", out var used) &&
            used.ValueKind == JsonValueKind.Object)
        {
            var p = used.TryGetProperty("provider", out var pv) ? pv.GetString() : null;
            var m = used.TryGetProperty("model", out var mv) ? mv.GetString() : null;
            var e = used.TryGetProperty("reasoningEffort", out var ev) ? ev.GetString() : null;
            model = string.Join('/', new[] { p, m }.Where(x => !string.IsNullOrEmpty(x)));
            if (!string.IsNullOrEmpty(e)) model += " " + e;
        }

        return new Report
        {
            Home = home,
            Found = true,
            Sessions = sessions,
            NewestSession = Path.GetFileNameWithoutExtension(path),
            NewestAt = at,
            Model = model,
            Tools = tools.Count,
            ToolsTokens = tools.Sum(t => t.Tokens),
            SystemTokens = systemTokens,
            ContextTokens = contextTokens,
            PressureTokens = pressure,
            ContextWindow = window,
            Compactions = compactions,
            ToolList = tools.OrderByDescending(t => t.Tokens).ToList(),
        };
    }

    static JsonElement? Row(JsonElement rows, string key) =>
        rows.TryGetProperty(key, out var row) &&
        row.TryGetProperty("val", out var val) &&
        val.ValueKind == JsonValueKind.Object
            ? val
            : null;

    static long Long(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;
}
