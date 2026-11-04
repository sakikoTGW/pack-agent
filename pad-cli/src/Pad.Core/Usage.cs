using System.IO;
using System.Linq;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// What actually happened, per DSH home: tokens spent, and how often each tool was
/// called. Two tables of facts, no projections.
///
/// Deliberately absent: anything of the shape "removing X would save Y". Savings are
/// counterfactual and cannot be measured; a launcher that prints them is giving advice,
/// not reporting (see DECISIONS.md D15).
/// </summary>
public static class Usage
{
    public sealed record ToolUse(string Tool, int Calls, long Tokens);

    public sealed record Report(
        string Home,
        int Sessions,
        long Turns,
        long Steps,
        long InputTokens,
        long OutputTokens,
        long CacheReadTokens,
        long UserTokens,
        long InjectTokens,
        long AssistantTokens,
        long ToolTokens,
        IReadOnlyList<ToolUse> Tools);

    public static Report Measure(string home)
    {
        var dir = Path.Combine(home, "storages", "session_projcache", "sessions");
        if (!Directory.Exists(dir))
            throw new PadError("PA070", "no session projection cache in that home", dir, null,
                ["pad cli inspect <home>", "pad cli usage <home>"]);

        long turns = 0, steps = 0, input = 0, output = 0, cacheRead = 0;
        long user = 0, inject = 0, assistant = 0, toolTok = 0;
        var sessions = 0;
        var calls = new Dictionary<string, (int Calls, long Tokens)>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(file)); }
            catch { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("record", out var rec) ||
                    !rec.TryGetProperty("rows", out var rows)) continue;

                sessions++;

                if (Val(rows, "tokenUsage") is { } tu && tu.TryGetProperty("totals", out var totals))
                {
                    input += Long(totals, "uncachedInputTokens");
                    output += Long(totals, "outputTokens");
                    cacheRead += Long(totals, "cacheReadTokens");
                }

                if (Val(rows, "sessionStats") is { } st)
                {
                    turns += Long(st, "turns");
                    steps += Long(st, "steps");
                }

                if (Val(rows, "contextTimeline") is { } tl)
                {
                    if (tl.TryGetProperty("sums", out var sums))
                    {
                        user += Long(sums, "user");
                        inject += Long(sums, "inject");
                        assistant += Long(sums, "assistant");
                        toolTok += Long(sums, "tool");
                    }

                    // Per-call facts live in the surface list: one entry per context item,
                    // tool results carry `cat: "tool"` and the tool name.
                    if (tl.TryGetProperty("surface", out var surface) && surface.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in surface.EnumerateArray())
                        {
                            if (!item.TryGetProperty("cat", out var cat) || cat.GetString() != "tool") continue;
                            if (!item.TryGetProperty("tool", out var t)) continue;
                            var name = t.GetString();
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            var tok = Long(item, "tokens");
                            var prev = calls.TryGetValue(name!, out var e) ? e : (0, 0L);
                            calls[name!] = (prev.Item1 + 1, prev.Item2 + tok);
                        }
                    }
                }
            }
        }

        var tools = calls
            .Select(kv => new ToolUse(kv.Key, kv.Value.Item1, kv.Value.Item2))
            .OrderByDescending(t => t.Calls)
            .ThenByDescending(t => t.Tokens)
            .ToList();

        return new Report(home, sessions, turns, steps, input, output, cacheRead,
            user, inject, assistant, toolTok, tools);
    }

    static JsonElement? Val(JsonElement rows, string key) =>
        rows.TryGetProperty(key, out var row) &&
        row.TryGetProperty("val", out var val) &&
        val.ValueKind == JsonValueKind.Object
            ? val
            : null;

    static long Long(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;
}
