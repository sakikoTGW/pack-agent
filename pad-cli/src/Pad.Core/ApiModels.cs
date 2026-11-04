using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// Live model ids from a company's OpenAI-compatible GET /models.
/// Never returns or logs the secret.
/// </summary>
public static class ApiModels
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static List<string> ParseIds(string json)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return ids;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var data = root;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d))
            data = d;
        if (data.ValueKind != JsonValueKind.Array) return ids;
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) ids.Add(s);
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("id", out var id) && !item.TryGetProperty("model", out id))
                continue;
            var name = id.GetString();
            if (!string.IsNullOrWhiteSpace(name)) ids.Add(name);
        }
        return ids;
    }

    public static async Task<(bool Ok, string Detail, List<string> Ids)> ListAsync(
        string? baseUrl, string? secret, CancellationToken ct)
    {
        var root = (baseUrl ?? "").Trim().TrimEnd('/');
        if (root.Length == 0) return (false, "这条没有 baseURL", []);
        var url = root.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? root
            : root + "/models";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(secret))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                req.Headers.TryAddWithoutValidation("x-api-key", secret);
            }
            using var res = await Http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                return (false, $"上游 /models {(int)res.StatusCode}", []);
            var ids = ParseIds(body);
            return (true, ids.Count + " 个模型", ids);
        }
        catch (OperationCanceledException)
        {
            return (false, "检测超时", []);
        }
        catch (Exception ex)
        {
            return (false, "检测失败：" + ex.Message, []);
        }
    }
}
