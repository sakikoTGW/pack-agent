using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// Instance tile: pack image if the instance has one, else the tui mark, else the
/// web mark, else the bundled ds mark. Never the first letters of the name.
/// </summary>
public static class InstIcon
{
    public enum Kind { Pack, Tui, Web, Ds }

    public static Kind Pick(bool packImage, bool tui, bool web)
    {
        if (packImage) return Kind.Pack;
        if (tui) return Kind.Tui;
        if (web) return Kind.Web;
        return Kind.Ds;
    }

    public static string? PackImage(Instance inst)
    {
        try
        {
            var logo = inst.Display?.Logo;
            if (!string.IsNullOrWhiteSpace(logo))
            {
                var abs = Skin.Resolve(logo);
                if (abs.Length > 0 && File.Exists(abs)) return abs;
                if (File.Exists(logo)) return Path.GetFullPath(logo);
            }

            foreach (var dir in PackDirs(inst))
            {
                foreach (var name in new[] { "icon.png", "icon.jpg", "icon.webp", "logo.png", "pack.png" })
                {
                    var p = Path.Combine(dir, name);
                    if (File.Exists(p)) return p;
                }
                var fromJson = IconFromPackJson(Path.Combine(dir, "pack.json"));
                if (fromJson is not null) return fromJson;
            }
        }
        catch { /* missing home / unreadable pack dir */ }
        return null;
    }

    static IEnumerable<string> PackDirs(Instance inst)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Push(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try { p = Path.GetFullPath(p); }
            catch { return; }
            if (!seen.Add(p)) return;
            ordered.Add(p);
        }

        var homeParent = Path.GetDirectoryName(inst.Home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Push(homeParent);
        if (homeParent is not null) Push(Path.Combine(homeParent, "pack"));
        Push(inst.Workspace.Path);
        var modpacks = Path.Combine(inst.Workspace.Path, ".agent-pack", "modpacks");
        if (Directory.Exists(modpacks))
        {
            var n = 0;
            foreach (var dir in Directory.EnumerateDirectories(modpacks))
            {
                Push(dir);
                if (++n >= 24) break;
            }
        }
        return ordered;
    }

    static string? IconFromPackJson(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            foreach (var key in new[] { "icon", "logo" })
            {
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var rel = v.GetString();
                    if (string.IsNullOrWhiteSpace(rel)) continue;
                    var abs = Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, rel));
                    if (File.Exists(abs)) return abs;
                }
            }
        }
        catch { /* damaged pack.json */ }
        return null;
    }
}
