using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// One DSH <c>llm-pi-ai.providers</c> route. Secrets stay in credentials refs;
/// this file only stores <c>apiKeyEnv</c>, url, and models.
/// </summary>
public sealed class LlmProviderRecipe
{
    [JsonPropertyName("route")] public string Route { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("apiKeyEnv")] public string ApiKeyEnv { get; set; } = "";
    [JsonPropertyName("api")] public string Api { get; set; } = "openai-completions";
    [JsonPropertyName("baseURL")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("models")] public List<string> Models { get; set; } = [];
    [JsonPropertyName("input")] public List<string> Input { get; set; } = ["text"];
}

public sealed class ProviderRow
{
    public LlmProviderRecipe Recipe { get; init; } = new();
    public string Title { get; init; } = "";
    public string Sub { get; init; } = "";
    public string Status { get; init; } = "";
    public bool RefMissing { get; init; }
}

/// <summary>library/llm-providers.json → home settings.yaml <c># &lt;pad-llm-providers&gt;</c>.</summary>
public static class LlmProviders
{
    public const string Begin = "# <pad-llm-providers>";
    public const string End = "# </pad-llm-providers>";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string LibraryFile(string launcherRoot) =>
        Path.Combine(launcherRoot, "library", "llm-providers.json");

    public static List<LlmProviderRecipe> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<LlmProviderRecipe>>(File.ReadAllText(path), Json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(string path, IReadOnlyList<LlmProviderRecipe> recipes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(recipes, Json), new UTF8Encoding(false));
    }

    public static string ToSettingsYaml(IEnumerable<LlmProviderRecipe> recipes)
    {
        var rows = recipes.Where(r => !string.IsNullOrWhiteSpace(r.Route)).ToList();
        if (rows.Count == 0) return "llm-pi-ai:\n  providers: {}\n";
        var sb = new StringBuilder();
        sb.AppendLine("llm-pi-ai:");
        sb.AppendLine("  providers:");
        foreach (var r in rows)
        {
            sb.AppendLine($"    {r.Route}:");
            if (!string.IsNullOrWhiteSpace(r.DisplayName))
                sb.AppendLine($"      displayName: {Scalar(r.DisplayName)}");
            if (!string.IsNullOrWhiteSpace(r.ApiKeyEnv))
                sb.AppendLine($"      apiKeyEnv: {r.ApiKeyEnv.Trim()}");
            if (!string.IsNullOrWhiteSpace(r.Api))
                sb.AppendLine($"      api: {r.Api.Trim()}");
            if (!string.IsNullOrWhiteSpace(r.BaseUrl))
                sb.AppendLine($"      baseURL: {Scalar(r.BaseUrl)}");
            var input = r.Input.Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            if (input.Count > 0)
            {
                sb.AppendLine("      defaultInput:");
                foreach (var m in input) sb.AppendLine($"        - {m}");
            }
            var models = r.Models.Where(s => s.Trim().Length > 0).Select(s => s.Trim()).ToList();
            if (models.Count == 0) continue;
            sb.AppendLine("      models:");
            foreach (var id in models)
                sb.AppendLine($"        - id: {Scalar(id)}");
        }
        return sb.ToString();
    }

    public static string Merge(string existing, string innerYaml)
    {
        var block = Begin + "\n" + innerYaml.TrimEnd() + "\n" + End + "\n";
        var start = existing.IndexOf(Begin, StringComparison.Ordinal);
        var end = existing.IndexOf(End, StringComparison.Ordinal);
        if (start >= 0 && end > start)
        {
            var after = existing[(end + End.Length)..].TrimStart('\r', '\n');
            return existing[..start] + block + after;
        }
        var prefix = existing ?? "";
        if (prefix.Length > 0 && !prefix.EndsWith('\n')) prefix += "\n";
        return prefix + block;
    }

    public static void ApplyToHome(string home, IReadOnlyList<LlmProviderRecipe> recipes)
    {
        if (string.IsNullOrWhiteSpace(home)) return;
        Directory.CreateDirectory(home);
        var path = Path.Combine(home, "settings.yaml");
        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        File.WriteAllText(path, Merge(existing, ToSettingsYaml(recipes)), new UTF8Encoding(false));
    }

    public static string RowTitle(LlmProviderRecipe r)
    {
        var name = (r.DisplayName ?? "").Trim();
        return name.Length > 0 ? name : (r.Route ?? "").Trim();
    }

    public static string RefStatus(LlmProviderRecipe r, IReadOnlyCollection<string> refs)
    {
        var env = (r.ApiKeyEnv ?? "").Trim();
        if (env.Length == 0) return "未写 apiKeyEnv";
        return refs.Contains(env) ? "已配 " + env : "缺 " + env;
    }

    public static string DescribeRow(LlmProviderRecipe r, IReadOnlyCollection<string> refs)
    {
        var n = r.Models.Count(m => m.Trim().Length > 0);
        return $"{RowTitle(r)} · {n} 个模型 · {RefStatus(r, refs)}";
    }

    public static string DescribeWillList(LlmProviderRecipe r)
    {
        var ids = r.Models.Select(m => m.Trim()).Where(s => s.Length > 0).ToList();
        if (ids.Count == 0) return "还没有模型 id。保存后下次启动才会进 session.models。";
        return "将会进入 session.models：" + string.Join("、", ids);
    }

    public static ProviderRow ToRow(LlmProviderRecipe r, IReadOnlyCollection<string> refs)
    {
        var env = (r.ApiKeyEnv ?? "").Trim();
        var n = r.Models.Count(m => m.Trim().Length > 0);
        return new ProviderRow
        {
            Recipe = r,
            Title = RowTitle(r),
            Sub = n + " 个模型 · " + r.Route,
            Status = RefStatus(r, refs),
            RefMissing = env.Length == 0 || !refs.Contains(env),
        };
    }

    /// <summary>DSH README routes: openai, anthropic, acme-gateway. No secrets.</summary>
    public static IReadOnlyList<LlmProviderRecipe> CatalogPresets() =>
    [
        new()
        {
            Route = "openai",
            DisplayName = "OpenAI",
            ApiKeyEnv = "OPENAI_API_KEY",
            Api = "openai-completions",
            Input = ["text"],
        },
        new()
        {
            Route = "anthropic",
            DisplayName = "Anthropic",
            ApiKeyEnv = "ANTHROPIC_API_KEY",
            Input = ["text"],
            Models = ["claude-sonnet-4-5"],
        },
        new()
        {
            Route = "acme-gateway",
            DisplayName = "Acme Gateway",
            ApiKeyEnv = "ACME_GATEWAY_API_KEY",
            Api = "openai-completions",
            BaseUrl = "https://gateway.acme.example/v1",
            Input = ["text"],
            Models = ["acme-think"],
        },
    ];

    static string Scalar(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return "\"\"";
        if (!v.Contains(':') && !v.Contains('#') && !v.Contains(' ') && !v.Contains('"'))
            return v;
        return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
