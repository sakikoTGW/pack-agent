using System.Net.Http;
using System.Text.Json;

namespace Pad.Core;

public sealed class UpdateSnapshot
{
    public string PadVersion { get; set; } = "";
    public string PackAgentLatest { get; set; } = "";
    public string DshInstalled { get; set; } = "";
    public string DshLatest { get; set; } = "";
    public bool DshNewer { get; set; }
    public bool PackAgentKnown { get; set; }
}

/// <summary>
/// Two independent version axes: the PAD exe / pack-agent npm line, and the pinned DSH release.
/// Applying a DSH release is InstallRelease. Applying a new PAD exe is replacing the binary.
/// </summary>
public static class Updates
{
    public static async Task<UpdateSnapshot> Check(Launcher launcher, string registry, CancellationToken ct)
    {
        var snap = new UpdateSnapshot
        {
            PadVersion = typeof(Updates).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
            DshInstalled = launcher.Releases().Select(r => r.Version).FirstOrDefault() ?? "",
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        snap.DshLatest = await Latest(http, registry, "@deepseek-ai/dsh", ct) ?? "";
        snap.DshNewer = snap.DshLatest.Length > 0
                        && !launcher.Releases().Any(r => r.Version == snap.DshLatest);

        snap.PackAgentLatest = await Latest(http, registry, "@sakikotgw/pack-agent", ct) ?? "";
        snap.PackAgentKnown = snap.PackAgentLatest.Length > 0;
        return snap;
    }

    static async Task<string?> Latest(HttpClient http, string registry, string pkg, CancellationToken ct)
    {
        var host = string.IsNullOrWhiteSpace(registry) ? "https://registry.npmjs.org" : registry.TrimEnd('/');
        var url = $"{host}/{pkg}";
        try
        {
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("dist-tags", out var tags)
                && tags.TryGetProperty("latest", out var latest))
                return latest.GetString();
        }
        catch
        {
            return null;
        }
        return null;
    }
}
