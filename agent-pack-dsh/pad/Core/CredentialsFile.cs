using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// DSH credentials.yaml: a REF map. The only key PAD's launch path must
/// surface is DEEPSEEK_API_KEY — a blank YAML editor is not a config UI.
/// </summary>
public static class CredentialsFile
{
    public const string DeepseekRef = "DEEPSEEK_API_KEY";
    public const string Code = "PA116";

    public sealed record ApiSpec(string Ref, string Company, string BaseUrl, string IconFile);

    /// <summary>
    /// Company APIs. Rows stay on the API page even when the ref is empty.
    /// Env names follow each company's current apiKeyEnv; BaseUrl is for GET /models.
    /// </summary>
    public static IReadOnlyList<ApiSpec> Catalog { get; } =
    [
        new(DeepseekRef, "DeepSeek", "https://api.deepseek.com", "deepseek.png"),
        new("SILICONFLOW_API_KEY", "硅基流动", "https://api.siliconflow.cn/v1", "siliconflow.png"),
        new("OPENROUTER_API_KEY", "OpenRouter", "https://openrouter.ai/api/v1", "openrouter.png"),
        new("OPENAI_API_KEY", "OpenAI", "https://api.openai.com/v1", "openai.png"),
        new("ANTHROPIC_API_KEY", "Anthropic", "https://api.anthropic.com", "anthropic.png"),
        new("MOONSHOT_API_KEY", "Kimi", "https://api.moonshot.cn/v1", "kimi.png"),
        new("DASHSCOPE_API_KEY", "通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen.png"),
        new("ZHIPUAI_API_KEY", "智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm.png"),
        new("ARK_API_KEY", "豆包", "https://ark.cn-beijing.volces.com/api/v3", "doubao.png"),
        new("GEMINI_API_KEY", "Gemini", "https://generativelanguage.googleapis.com/v1beta", "gemini.png"),
        new("HUNYUAN_API_KEY", "混元", "https://api.hunyuan.cloud.tencent.com/v1", "hunyuan.png"),
        new("ACME_GATEWAY_API_KEY", "Acme", "https://gateway.acme.example/v1", "acme.png"),
    ];

    static readonly Regex NameOk = new(@"^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant);

    public static bool HasDeepseekKey(string? yaml) => ReadDeepseekKey(yaml).Length > 0;

    public static bool IsRefName(string name) => NameOk.IsMatch((name ?? "").Trim());

    /// <summary>
    /// Company label for an imported ref. Catalog names only; never invent a vendor.
    /// </summary>
    public static string CompanyOf(string? refName) =>
        SpecOf(refName)?.Company ?? "未标注";

    public static string UpsertRef(string? yaml, string name, string value)
    {
        name = (name ?? "").Trim();
        if (!IsRefName(name))
            throw new ArgumentException("ref name must be ENV-style", nameof(name));
        var v = (value ?? "").Trim();
        var line = $"{name}: \"REF: {v}\"";
        var body = yaml ?? "";
        var re = new Regex("^" + Regex.Escape(name) + @"\s*:.*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (re.IsMatch(body)) return re.Replace(body, line, 1);
        if (body.Length > 0 && !body.EndsWith('\n')) body += "\n";
        return body + line + "\n";
    }

    public static string RemoveRef(string? yaml, string name)
    {
        name = (name ?? "").Trim();
        if (!IsRefName(name)) return yaml ?? "";
        var re = new Regex("^" + Regex.Escape(name) + @"\s*:.*\r?\n?",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return re.Replace(yaml ?? "", "", 1);
    }

    public static List<string> ListRefs(string? yaml)
    {
        var names = new List<string>();
        if (string.IsNullOrWhiteSpace(yaml)) return names;
        foreach (var raw in yaml.Split(['\r', '\n']))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            if (!IsRefName(name)) continue;
            var v = line[(colon + 1)..].Trim();
            if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
                v = v[1..^1];
            if (v.StartsWith("REF:", StringComparison.Ordinal)) v = v[4..].Trim();
            if (v.Length > 0) names.Add(name);
        }
        return names;
    }

    public static ApiSpec? SpecOf(string? refName)
    {
        var n = (refName ?? "").Trim();
        foreach (var c in Catalog)
            if (c.Ref == n) return c;
        return null;
    }

    public static string IconPack(string? iconFile)
    {
        var f = (iconFile ?? "").Trim();
        if (f.Length == 0) f = "api.png";
        return "pack://application:,,,/Assets/api/" + f;
    }

    static string LineValue(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0) return "";
        var v = line[(colon + 1)..].Trim();
        if (v.Length >= 2 && ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
            v = v[1..^1];
        if (v.StartsWith("REF:", StringComparison.Ordinal)) v = v[4..].Trim();
        return v;
    }

    /// <summary>Value of one credentials ref. Never for display.</summary>
    public static string ReadRef(string? yaml, string name)
    {
        name = (name ?? "").Trim();
        if (!IsRefName(name) || string.IsNullOrWhiteSpace(yaml)) return "";
        foreach (var raw in yaml.Split(['\r', '\n']))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (line[..colon].Trim() != name) continue;
            return LineValue(line);
        }
        return "";
    }

    public static string ReadDeepseekKey(string? yaml) => ReadRef(yaml, DeepseekRef);

    public static string UpsertDeepseekKey(string? yaml, string value) =>
        UpsertRef(yaml, DeepseekRef, value);

    /// <summary>
    /// cmd.exe fragment that sets DEEPSEEK_API_KEY from
    /// <c>%DSH_HOME%\.credentials.yaml</c> at run time. wt/cmd does not inherit
    /// this PAD process, and DSH reads env before the yaml file — so the child
    /// must set the name. The secret itself stays in yaml, never in launch-*.cmd.
    /// </summary>
    public static string CmdLoadFromHome() => """
        @set "DEEPSEEK_API_KEY="
        @if not exist "%DSH_HOME%\.credentials.yaml" goto :pad_cred_done
        @setlocal EnableDelayedExpansion
        @set "PAD_CRED_LINE="
        @for /f "usebackq tokens=* delims=" %%L in (`findstr /i /b /c:"DEEPSEEK_API_KEY" "%DSH_HOME%\.credentials.yaml"`) do @set "PAD_CRED_LINE=%%L"
        @if not defined PAD_CRED_LINE goto :pad_cred_endlocal
        @for /f "tokens=1* delims=:" %%A in ("!PAD_CRED_LINE!") do @set "PAD_CRED_VAL=%%B"
        @for /f "tokens=* delims= " %%T in ("!PAD_CRED_VAL!") do @set "PAD_CRED_VAL=%%T"
        @set "PAD_CRED_VAL=!PAD_CRED_VAL:"=!"
        @if /i "!PAD_CRED_VAL:~0,4!"=="REF:" @set "PAD_CRED_VAL=!PAD_CRED_VAL:~4!"
        @for /f "tokens=* delims= " %%T in ("!PAD_CRED_VAL!") do @set "PAD_CRED_VAL=%%T"
        @for /f "delims=" %%K in ("!PAD_CRED_VAL!") do (
          @endlocal
          @set "DEEPSEEK_API_KEY=%%K"
          @goto :pad_cred_done
        )
        :pad_cred_endlocal
        @endlocal
        :pad_cred_done
        """;


    public static bool EnvHasDeepseekKey(string? envText)
    {
        foreach (var (key, val) in LaunchPrefs.ParseEnv(envText))
            if (key == DeepseekRef && val.Length > 0) return true;
        return false;
    }

    public static bool LiveConfigured(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        if (!value.TryGetProperty("credentials", out var creds)
            || creds.ValueKind != JsonValueKind.Object)
            return false;
        if (!creds.TryGetProperty(DeepseekRef, out var row)
            || row.ValueKind != JsonValueKind.Object)
            return false;
        return row.TryGetProperty("configured", out var c) && c.ValueKind == JsonValueKind.True;
    }

    public static PadError MissingError(string location) =>
        new(Code, "DEEPSEEK_API_KEY is not configured", location,
            "the deepseek-official adapter fails every turn until this is set",
            ["fill DEEPSEEK_API_KEY in Settings → API Key",
             "pad cli credentials has <instance>"]);
}
