using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Pad.Core;

public readonly record struct PackageMeta(string Version, string Description, List<string> Keywords);

/// <summary>
/// One row on the version-settings plugin list. Counterpart of a PCL Mod row:
/// an extra <c>dsh plugin</c> package or a this-run trial, not a profile-layer
/// 组合包 and not an 整合包. Search uses package.json description / keywords.
/// </summary>
public sealed class PluginRow : Observable
{
    public string Profile { get; init; } = "";
    public string Spec { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Keywords { get; init; } = [];
    public string PackageDir { get; init; } = "";
    public bool Trial { get; init; }
    public TrialRecord? TrialRec { get; init; }

    string _iconPath = "";
    public string IconPath
    {
        get => _iconPath;
        set
        {
            if (Set(ref _iconPath, value ?? ""))
                Raise(nameof(HasIcon));
        }
    }

    string _latest = "";
    public string Latest
    {
        get => _latest;
        set
        {
            if (Set(ref _latest, value ?? ""))
            {
                Raise(nameof(Updatable));
                Raise(nameof(VersionLine));
            }
        }
    }

    bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    public string Title
    {
        get
        {
            var s = Spec;
            var i = s.LastIndexOf('/');
            return i >= 0 ? s[(i + 1)..] : s;
        }
    }

    public string VersionText => string.IsNullOrEmpty(Version) ? "未装到磁盘" : Version;
    public string VersionLine => Updatable ? $"{VersionText} → {Latest}" : VersionText;
    public bool HasIcon => !string.IsNullOrEmpty(IconPath);
    public bool Updatable => !Trial && Latest.Length > 0 && Latest != Version;
    public bool HasDescription => Description.Length > 0;
    public string Detail => TrialRec?.Detail ?? Description;
    public string SubLine => Description.Length > 0 ? Description : PackageDir;

    public string Monogram
    {
        get
        {
            foreach (var ch in Title)
                if (char.IsAsciiLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
            return "?";
        }
    }

    public IReadOnlyList<string> Tags
    {
        get
        {
            var tags = new List<string>();
            if (Trial) tags.Add("试验");
            if (Updatable) tags.Add("可更新");
            foreach (var k in Keywords)
                if (!tags.Exists(t => t.Equals(k, StringComparison.OrdinalIgnoreCase)))
                    tags.Add(k);
            return tags;
        }
    }
}

/// <summary>
/// Search and filter for the plugin list. Query matches spec / description /
/// keywords / version — the PCL Mod search fields. Kind is not a search field.
/// </summary>
public static class PluginList
{
    public enum Filter { All, Updatable, Trial }

    public static bool Matches(PluginRow row, string query)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return true;
        if (Contains(row.Spec, q) || Contains(row.Description, q)
            || Contains(row.Version, q) || Contains(row.Latest, q) || Contains(row.Title, q))
            return true;
        foreach (var k in row.Keywords)
            if (Contains(k, q)) return true;
        return false;
    }

    public static IEnumerable<PluginRow> Apply(IEnumerable<PluginRow> rows, string query, Filter filter)
    {
        foreach (var r in rows)
        {
            if (!Matches(r, query)) continue;
            if (filter == Filter.Updatable && !r.Updatable) continue;
            if (filter == Filter.Trial && !r.Trial) continue;
            yield return r;
        }
    }

    static bool Contains(string hay, string needle) =>
        hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static Filter Parse(string? tag) => tag switch
    {
        "updatable" => Filter.Updatable,
        "trial" => Filter.Trial,
        _ => Filter.All,
    };

    public static PackageMeta ReadMeta(string pkg)
    {
        if (!File.Exists(pkg)) return new("", "", []);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            var root = doc.RootElement;
            var ver = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
            var desc = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                ? (d.GetString() ?? "").Trim() : "";
            var keys = new List<string>();
            if (root.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in kw.EnumerateArray())
                    if (k.GetString() is { Length: > 0 } s) keys.Add(s);
            }
            return new(ver, desc, keys);
        }
        catch { return new("", "", []); }
    }

    public static PluginRow FromBundle(BundleRow b) => new()
    {
        Profile = b.Profile,
        Spec = b.Spec,
        Kind = b.Kind,
        Version = b.Version,
        Description = b.Description,
        Keywords = b.Keywords,
        PackageDir = b.PackageDir,
        IconPath = b.IconPath,
        Latest = b.Latest,
    };

    public static PluginRow FromTrial(TrialRecord rec, Instance inst, string launcherRoot)
    {
        var spec = rec.Bundles.FirstOrDefault() ?? rec.Spec;
        var profileDir = Path.Combine(inst.Home, "profiles", rec.Profile);
        var dir = LaunchPolicy.BundlePackageDir(profileDir, spec);
        var pkg = Path.Combine(dir, "package.json");
        var meta = ReadMeta(pkg);
        return new PluginRow
        {
            Profile = rec.Profile,
            Spec = spec,
            Kind = LaunchPolicy.BundleKind(spec),
            Version = meta.Version,
            Description = meta.Description,
            Keywords = meta.Keywords,
            PackageDir = dir,
            IconPath = BundleIcon.Resolve(launcherRoot, dir, spec) ?? "",
            Trial = true,
            TrialRec = rec,
        };
    }

    public static bool IsNpmName(string spec)
    {
        var s = (spec ?? "").Trim();
        if (s.Length == 0) return false;
        if (s.StartsWith("link:", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.Contains('\\') || s.Contains('/') && !s.StartsWith('@'))
        {
            if (s.Contains(':') || s.StartsWith('.') || s.StartsWith('/')) return false;
        }
        if (s.Contains(':') && !s.StartsWith('@')) return false;
        return true;
    }

    public static string? ReadCachedLatest(string launcherRoot, string spec)
    {
        var p = Path.Combine(BundleIcon.CacheDir(launcherRoot, spec), "latest.txt");
        if (!File.Exists(p)) return null;
        var t = File.ReadAllText(p).Trim();
        return t.Length == 0 ? null : t;
    }

    public static void WriteCachedLatest(string launcherRoot, string spec, string latest)
    {
        var dir = BundleIcon.CacheDir(launcherRoot, spec);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "latest.txt"), latest);
    }

    public static async Task<string?> FetchLatest(string registry, string spec, CancellationToken ct)
    {
        if (!IsNpmName(spec)) return null;
        var name = LaunchPolicy.BundleBareName(spec);
        var host = string.IsNullOrWhiteSpace(registry) ? "https://registry.npmjs.org" : registry.TrimEnd('/');
        var url = $"{host}/{Uri.EscapeDataString(name)}";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "pack-agent-for-DSH");
            var json = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("dist-tags", out var tags)
                && tags.TryGetProperty("latest", out var latest)
                && latest.ValueKind == JsonValueKind.String)
                return latest.GetString();
        }
        catch { }
        return null;
    }
}
