using System.Net.Http;
using System.Text.Json;

namespace Pad.Core;

/// <summary>One package as the npm registry reports it.</summary>
public sealed class MarketRow
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Date { get; set; } = "";
    public string Homepage { get; set; } = "";
    public List<string> Keywords { get; set; } = [];

    /// <summary>npm's own popularity score, 0..1. Shown as-is; PAD does not re-rank.</summary>
    public double Popularity { get; set; }

    /// <summary>True when the package declares itself a DSH profile layer.</summary>
    public bool IsBundle { get; set; }

    public string Title => Name;

    public string DescriptionText => string.IsNullOrWhiteSpace(Description)
        ? "包里没写描述。" : Description.Trim();

    public string VersionText => string.IsNullOrWhiteSpace(Version) ? "" : "v" + Version;

    public string DateText => Market.RelTime(Date);

    public string TagText => Keywords.Count > 0 ? Keywords[0] : "";

    public bool HasTag => TagText.Length > 0;

    /// <summary>A monogram for the row, since npm has no icon field.</summary>
    public string Monogram
    {
        get
        {
            var bare = Name.StartsWith('@') && Name.Contains('/')
                ? Name[(Name.IndexOf('/') + 1)..]
                : Name;
            foreach (var ch in bare)
                if (char.IsAsciiLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
            return "?";
        }
    }
}

public sealed record MarketPage(List<MarketRow> Rows, int Total, int From, int Size)
{
    public int PageIndex => Size <= 0 ? 0 : From / Size;
    public int PageCount => Size <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)Size));
    public bool HasPrev => From > 0;
    public bool HasNext => From + Size < Total;
}

/// <summary>
/// The plugin market. DSH ships no market index of its own — its documented discovery
/// channel is the npm registry plus the GitHub <c>dsh-plugin</c> topic, and
/// <c>dsh plugin add</c> forwards to pnpm. So the market is a view over npm search,
/// not a catalog PAD invents.
/// </summary>
public sealed class Market
{
    static readonly HttpClient Http = new();

    public const int DefaultPageSize = 20;

    /// <summary>Shelves come from pad.json; empty/invalid falls back to the built-in four.</summary>
    public static IReadOnlyList<(string Id, string Label, string Query)> Shelves
    {
        get
        {
            try
            {
                var list = AppState.Current.Settings.Download.Shelves;
                if (list is { Count: > 0 })
                    return list.Select(s => (s.Id, s.Label, s.Query)).ToList();
            }
            catch { /* boot */ }
            return DownloadPrefs.DefaultShelves().Select(s => (s.Id, s.Label, s.Query)).ToList();
        }
    }

    public static string RegistryBase
    {
        get
        {
            try
            {
                var r = AppState.Current.Settings.Download.NpmRegistry;
                return string.IsNullOrWhiteSpace(r) ? "https://registry.npmjs.org" : r.TrimEnd('/');
            }
            catch { return "https://registry.npmjs.org"; }
        }
    }

    public static int PageSize
    {
        get
        {
            try { return Math.Clamp(AppState.Current.Settings.Download.MarketPageSize, 5, 100); }
            catch { return DefaultPageSize; }
        }
    }

    static int TimeoutSec
    {
        get
        {
            try { return AppState.Current.Settings.Download.MarketTimeoutSec; }
            catch { return 15; }
        }
    }

    /// <summary>
    /// One page of a shelf. <paramref name="text"/> is the user's words, appended to the
    /// shelf qualifier rather than replacing it.
    /// </summary>
    public static async Task<MarketPage> Search(string shelfQuery, string text, int from,
        CancellationToken ct)
    {
        var size = PageSize;
        var q = string.IsNullOrWhiteSpace(text) ? shelfQuery : $"{shelfQuery} {text.Trim()}";
        var host = RegistryBase;
        var url = $"{host}/-/v1/search"
                  + $"?text={Uri.EscapeDataString(q)}&size={size}&from={Math.Max(0, from)}";

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(TimeoutSec));

        string body;
        try
        {
            body = await Http.GetStringAsync(url, linked.Token);
        }
        catch (Exception ex)
        {
            throw new PadError("PA033", "npm 搜索没连上", host, ex.Message,
                ["检查网络或代理", "到设置 → 下载改 registry / 超时", "npm registry 不通时市场只能空着"]);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var total = root.TryGetProperty("total", out var t) && t.TryGetInt32(out var n) ? n : 0;

        var rows = new List<MarketRow>();
        if (root.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
        {
            foreach (var obj in objects.EnumerateArray())
            {
                if (!obj.TryGetProperty("package", out var pkg)) continue;
                var row = new MarketRow
                {
                    Name = Str(pkg, "name"),
                    Version = Str(pkg, "version"),
                    Description = Str(pkg, "description"),
                    Date = Str(pkg, "date"),
                };
                if (pkg.TryGetProperty("publisher", out var pub)) row.Publisher = Str(pub, "username");
                if (pkg.TryGetProperty("links", out var links))
                {
                    row.Homepage = Str(links, "homepage");
                    if (row.Homepage.Length == 0) row.Homepage = Str(links, "repository");
                    if (row.Homepage.Length == 0) row.Homepage = Str(links, "npm");
                }
                if (pkg.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
                    foreach (var k in kw.EnumerateArray())
                        if (k.GetString() is { } s) row.Keywords.Add(s);

                if (obj.TryGetProperty("score", out var score)
                    && score.TryGetProperty("detail", out var detail)
                    && detail.TryGetProperty("popularity", out var pop)
                    && pop.TryGetDouble(out var p))
                {
                    row.Popularity = p;
                }
                rows.Add(row);
            }
        }
        return new MarketPage(rows, total, Math.Max(0, from), size);
    }

    static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>对照 PCL 的 1.20 芯片：1.45.1-rc.1 → 1.45。</summary>
    public static string ChipOf(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "";
        var cut = version.IndexOfAny(['-', '+']);
        var core = cut < 0 ? version.Trim() : version[..cut];
        var parts = core.Split('.');
        if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
            return parts[0] + "." + parts[1];
        return parts[0];
    }

    public static string RelTime(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (!DateTimeOffset.TryParse(iso, out var when)) return iso;
        var days = (DateTimeOffset.UtcNow - when).TotalDays;
        if (days < 1) return "今天";
        if (days < 30) return $"{(int)days} 天前";
        if (days < 365) return $"{(int)(days / 30)} 个月前";
        return $"{(int)(days / 365)} 年前";
    }

    /// <summary>
    /// Whether a package declares <c>dsh.bundle</c>, i.e. whether adding it becomes a
    /// profile layer or DSH's "plain dependency, not a profile layer". Answered from the
    /// registry so the UI can say which one it will be before installing.
    /// </summary>
    public static async Task<bool> IsBundle(string name, string version, CancellationToken ct)
    {
        var url = $"{RegistryBase}/{Uri.EscapeDataString(name).Replace("%40", "@")}/{version}";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSec));
            var body = await Http.GetStringAsync(url, cts.Token);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("dsh", out var dsh)
                   && dsh.TryGetProperty("bundle", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>npm packument for one plugin: versions to pick on the detail page.</summary>
    public static async Task<PluginCatalog> FetchPackument(string name, CancellationToken ct)
    {
        var url = $"{RegistryBase}/{Uri.EscapeDataString(name).Replace("%40", "@")}";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSec));
        string body;
        try
        {
            body = await Http.GetStringAsync(url, cts.Token);
        }
        catch (Exception ex)
        {
            throw new PadError("PA033", "npm packument 没连上", url, ex.Message,
                ["检查网络或代理", "到设置 → 下载改 registry / 超时"]);
        }
        return ParsePackument(name, body);
    }

    public static PluginCatalog ParsePackument(string name, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cat = new PluginCatalog { Name = name };
        if (root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
            cat.Description = desc.GetString() ?? "";
        if (root.TryGetProperty("homepage", out var home) && home.ValueKind == JsonValueKind.String)
            cat.Homepage = home.GetString() ?? "";
        if (root.TryGetProperty("dist-tags", out var tags) && tags.ValueKind == JsonValueKind.Object
            && tags.TryGetProperty("latest", out var latestEl) && latestEl.ValueKind == JsonValueKind.String)
            cat.Latest = latestEl.GetString() ?? "";

        var keys = new List<string>();
        JsonElement versions = default;
        var hasVersions = root.TryGetProperty("versions", out versions) && versions.ValueKind == JsonValueKind.Object;
        if (hasVersions)
        {
            foreach (var p in versions.EnumerateObject()) keys.Add(p.Name);
            keys.Reverse();
            cat.Versions.AddRange(keys);
        }

        var pick = cat.Latest;
        if (pick.Length == 0 && keys.Count > 0) pick = keys[0];
        if (hasVersions && pick.Length > 0 && versions.TryGetProperty(pick, out var pkg))
        {
            cat.IsBundle = pkg.TryGetProperty("dsh", out var dsh) && dsh.TryGetProperty("bundle", out _);
            if (pkg.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
                foreach (var k in kw.EnumerateArray())
                    if (k.GetString() is { } s) cat.Keywords.Add(s);
            if (cat.Description.Length == 0 && pkg.TryGetProperty("description", out var pd)
                && pd.ValueKind == JsonValueKind.String)
                cat.Description = pd.GetString() ?? "";
            if (cat.Homepage.Length == 0 && pkg.TryGetProperty("homepage", out var ph)
                && ph.ValueKind == JsonValueKind.String)
                cat.Homepage = ph.GetString() ?? "";
        }
        if (cat.Latest.Length == 0 && keys.Count > 0) cat.Latest = keys[0];

        JsonElement timeEl = default;
        var hasTime = root.TryGetProperty("time", out timeEl) && timeEl.ValueKind == JsonValueKind.Object;
        foreach (var v in keys)
        {
            var when = "";
            if (hasTime && timeEl.TryGetProperty(v, out var t) && t.ValueKind == JsonValueKind.String)
                when = t.GetString() ?? "";
            cat.Files.Add(new PluginFile
            {
                Name = name,
                Version = v,
                Time = when,
                Latest = cat.Latest.Length > 0 && v == cat.Latest,
            });
        }
        return cat;
    }
}

/// <summary>One published file on a plugin's npm packument, one row on the detail list.</summary>
public sealed class PluginFile
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Time { get; set; } = "";
    public bool Latest { get; set; }

    public string FileName => string.IsNullOrEmpty(Version) ? Name : $"{Name}@{Version}";

    public string Chip => Market.ChipOf(Version);

    public string Channel => Version.Contains('-') ? "B" : "R";

    public string Title => $"{Name} {Version}";

    public string TimeText => Market.RelTime(Time);

    public string Line
    {
        get
        {
            var bits = new List<string> { FileName };
            if (TimeText.Length > 0) bits.Add(TimeText);
            if (Latest) bits.Add("latest");
            return string.Join(" · ", bits);
        }
    }
}

/// <summary>One plugin's npm packument, used by the download detail page.</summary>
public sealed class PluginCatalog
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Homepage { get; set; } = "";
    public string Latest { get; set; } = "";
    public bool IsBundle { get; set; }
    public List<string> Versions { get; } = [];
    public List<PluginFile> Files { get; } = [];
    public List<string> Keywords { get; } = [];
}
