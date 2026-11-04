using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pad.Core;

public sealed class CrashReport
{
    public string Instance { get; init; } = "";
    public string Category { get; init; } = "unknown";
    public string? Matched { get; init; }
    public List<string> Logs { get; init; } = [];
    public string? FaqTitle { get; init; }
    public string? FaqBody { get; init; }

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("类别 " + Category);
        if (!string.IsNullOrEmpty(Matched)) sb.AppendLine("对上了 " + Matched);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(FaqTitle))
        {
            sb.AppendLine(FaqTitle);
            if (!string.IsNullOrEmpty(FaqBody)) sb.AppendLine(FaqBody.Trim());
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("日志里没有对上已知规则。打开日志目录看原文。");
            sb.AppendLine();
        }
        sb.AppendLine("日志 " + Logs.Count + " 个");
        foreach (var f in Logs) sb.AppendLine(f);
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Match instance logs against crash-rules.json. The rules ship with PAD,
/// so this does not call packagent or need the git working copy.
/// </summary>
public static class CrashAnalyzer
{
    public static CrashReport Analyze(Launcher launcher, string instanceId)
    {
        var logDir = launcher.LogsDir(instanceId);
        var files = Directory.Exists(logDir)
            ? Directory.GetFiles(logDir, "*.log").OrderBy(f => f, StringComparer.Ordinal).ToList()
            : [];
        var hay = new StringBuilder();
        foreach (var f in files)
        {
            try { hay.AppendLine(File.ReadAllText(f)); }
            catch { /* unreadable log still listed */ }
        }

        var rules = Load("crash-rules.json", launcher);
        var faqs = Load("faq.json", launcher);
        string category = "unknown";
        string? matched = null;
        string? faqId = null;
        foreach (var rule in Rules(rules))
        {
            var pattern = Str(rule, "pattern");
            if (pattern.Length == 0) continue;
            try
            {
                if (!Regex.IsMatch(hay.ToString(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    continue;
            }
            catch { continue; }
            category = Str(rule, "category");
            if (category.Length == 0) category = "unknown";
            matched = Str(rule, "id");
            if (matched.Length == 0) matched = null;
            faqId = Str(rule, "faq");
            break;
        }

        string? title = null;
        string? body = null;
        if (!string.IsNullOrEmpty(faqId))
        {
            foreach (var faq in Rules(faqs))
            {
                if (Str(faq, "id") != faqId) continue;
                title = Str(faq, "title");
                body = Str(faq, "markdown");
                break;
            }
        }

        return new CrashReport
        {
            Instance = instanceId,
            Category = category,
            Matched = matched,
            Logs = files,
            FaqTitle = title,
            FaqBody = body,
        };
    }

    static IEnumerable<JsonElement> Rules(JsonDocument? doc)
    {
        if (doc is null) yield break;
        if (!doc.RootElement.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var el in entries.EnumerateArray())
        {
            if (el.TryGetProperty("disabled", out var dis) && dis.ValueKind == JsonValueKind.True)
                continue;
            yield return el;
        }
    }

    static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static JsonDocument? Load(string file, Launcher launcher)
    {
        foreach (var dir in Probe(launcher))
        {
            var path = Path.Combine(dir, file);
            if (!File.Exists(path)) continue;
            try { return JsonDocument.Parse(File.ReadAllText(path)); }
            catch { /* next candidate */ }
        }

        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        if (res is null) return null;
        using var stream = asm.GetManifestResourceStream(res);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        try { return JsonDocument.Parse(reader.ReadToEnd()); }
        catch { return null; }
    }

    static IEnumerable<string> Probe(Launcher launcher)
    {
        yield return Path.Combine(launcher.Root, "library", "registries");
        yield return Path.Combine(AppContext.BaseDirectory, "registries");
        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && walk is not null; i++, walk = walk.Parent)
        {
            yield return Path.Combine(walk.FullName, "agent-pack-dsh", "modpack", "registries");
            yield return Path.Combine(walk.FullName, "modpack", "registries");
        }
    }
}
