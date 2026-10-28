using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// Carrier marks for DSH plugins. Carriers are plugins (there can be several TUIs), so a
/// plugin that ships its own icon wins; otherwise a small database answers by package name,
/// and finally the DSH whale is used.
///
/// The database is files, not code: an entry names a package fragment and an image.
///   packaged  pad/Assets/carriers.json      (ships with PAD)
///   user      &lt;launcher root&gt;/icons/carriers.json  (you add entries; wins over packaged)
/// Images resolve against the file that declared them, so a user entry can say "dsh-tui.png"
/// and drop that PNG next to their own carriers.json.
/// </summary>
public static class Carriers
{
    public sealed record Entry(string Match, string Icon);

    static readonly List<(Entry Entry, string BaseDir)> Tables = [];
    static string _loaded = "";

    /// <summary>Reload both tables for this launcher root (cheap; only when the root changes).</summary>
    static void Ensure(string padRoot)
    {
        if (_loaded == padRoot) return;
        _loaded = padRoot;
        Tables.Clear();

        var appDir = AppContext.BaseDirectory;
        Add(Path.Combine(appDir, "Assets", "carriers.json"));
        Add(Path.Combine(appDir, "Assets", "carriers"));
        Add(Path.Combine(padRoot, "icons", "carriers.json"));
        Add(Path.Combine(padRoot, "icons"));
    }

    static void Add(string path)
    {
        // A bare directory means "every image in here is a mark" — the drop-in case.
        if (Directory.Exists(path))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.png"))
                    Tables.Add((new Entry(Path.GetFileNameWithoutExtension(file), file), path));
            }
            catch { /* unreadable */ }
            return;
        }

        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            var baseDir = Path.GetDirectoryName(path) ?? "";
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var match = item.TryGetProperty("match", out var m) ? m.GetString() : null;
                var icon = item.TryGetProperty("icon", out var i) ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(match) || string.IsNullOrWhiteSpace(icon)) continue;
                Tables.Add((new Entry(match!, icon!), baseDir));
            }
        }
        catch { /* bad table: skip it */ }
    }

    /// <summary>
    /// The mark for a bundle list: first the package's own icon, then the database entry
    /// whose <c>match</c> is contained in the package name.
    /// </summary>
    public static string? Resolve(string home, string padRoot, IEnumerable<string> bundleSpecs)
    {
        Ensure(padRoot);

        foreach (var spec in bundleSpecs)
        {
            var own = Logos.Plugin(PackageDir(home, spec));
            if (own is not null) return own;

            var hit = Match(spec);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>Where a bundle is installed inside a profile, if it is there at all.</summary>
    static string? PackageDir(string home, string spec)
    {
        try
        {
            var profiles = Path.Combine(home, "profiles");
            if (!Directory.Exists(profiles)) return null;
            foreach (var profile in Directory.EnumerateDirectories(profiles))
            {
                var dir = Path.Combine(profile, "node_modules", spec.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(dir)) return dir;
            }
        }
        catch { /* unreadable */ }
        return null;
    }

    static string? Match(string spec)
    {
        foreach (var (entry, baseDir) in Tables)
        {
            if (!spec.Contains(entry.Match, StringComparison.OrdinalIgnoreCase)) continue;
            var at = Path.IsPathRooted(entry.Icon) ? entry.Icon : Path.Combine(baseDir, entry.Icon);
            if (File.Exists(at)) return at;
        }
        return null;
    }

    /// <summary>The bundles a home declares, read straight from its profile manifests.</summary>
    public static IEnumerable<string> Bundles(string home)
    {
        var profiles = Path.Combine(home, "profiles");
        if (!Directory.Exists(profiles)) yield break;

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(profiles); }
        catch { yield break; }

        foreach (var dir in dirs)
        {
            var manifest = Path.Combine(dir, "package.json");
            if (!File.Exists(manifest)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(manifest)); }
            catch { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("dsh", out var dsh)) continue;
                if (!dsh.TryGetProperty("profile", out var profile)) continue;
                if (!profile.TryGetProperty("bundles", out var bundles)) continue;
                if (bundles.ValueKind != JsonValueKind.Array) continue;

                foreach (var b in bundles.EnumerateArray())
                {
                    var name = b.ValueKind == JsonValueKind.String ? b.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name)) yield return name!;
                }
            }
        }
    }
}
