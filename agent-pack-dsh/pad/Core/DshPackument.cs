using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// npm packument for <c>@deepseek-ai/dsh</c>. The download page lists every
/// published release; <c>0.0.1-rc.1</c> is the in-box latest trap and is dropped.
/// </summary>
public sealed record DshCatalog(string? Latest, IReadOnlyList<NpmReleaseRow> Versions);

public sealed record NpmReleaseRow(string Version, string? Time, bool Latest);

public static class DshPackument
{
    public const string Banned = "0.0.1-rc.1";

    public static bool BannedRelease(string version) => version == Banned;

    public static DshCatalog Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement);
    }

    public static DshCatalog Parse(JsonElement root)
    {
        var keys = new List<string>();
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("versions", out var vs)
            && vs.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in vs.EnumerateObject())
                if (!BannedRelease(p.Name)) keys.Add(p.Name);
        }
        keys.Reverse();

        string? latest = null;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("dist-tags", out var tags)
            && tags.ValueKind == JsonValueKind.Object
            && tags.TryGetProperty("latest", out var l)
            && l.ValueKind == JsonValueKind.String)
        {
            var tag = l.GetString();
            if (tag is { Length: > 0 } && !BannedRelease(tag)) latest = tag;
        }

        JsonElement time = default;
        var hasTime = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("time", out time)
            && time.ValueKind == JsonValueKind.Object;

        var rows = new List<NpmReleaseRow>(keys.Count);
        foreach (var version in keys)
        {
            string? when = null;
            if (hasTime && time.TryGetProperty(version, out var t) && t.ValueKind == JsonValueKind.String)
                when = t.GetString();
            rows.Add(new NpmReleaseRow(version, when, latest is not null && version == latest));
        }

        if (latest is null && rows.Count > 0)
        {
            rows[0] = rows[0] with { Latest = true };
            latest = rows[0].Version;
        }

        return new DshCatalog(latest, rows);
    }

    public static string TimeText(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        return DateTimeOffset.TryParse(iso, out var t) ? t.ToString("yyyy-MM-dd") : iso;
    }
}
