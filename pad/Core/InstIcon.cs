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
        if (homeParent is not null) Push(Path.Combine(homeParent, "pack"));

        // 实例来源包自己的目录（pack.json 里 icon/logo 相对它解析）。
        var packPath = inst.Source?.PackPath;
        if (!string.IsNullOrWhiteSpace(packPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(packPath!));
                if (!string.IsNullOrWhiteSpace(dir)) Push(dir);
            }
            catch { /* 坏路径不挡其它候选 */ }
        }

        // 投影出来的整合包目录。
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

    /// <summary>
    /// 侧栏 / 卡片 / 关系图共用的一枚：整合包图 → 载体图标（TUI/桌面端组合包自己的图）。
    /// null = 调用方自己兜底（鲸鱼或状态点）。项目 logo 不在候选里——它可能是字母（D38）。
    /// </summary>
    public static string? MarkFile(Instance inst)
    {
        var pack = PackImage(inst);
        if (!string.IsNullOrWhiteSpace(pack)) return pack;
        try
        {
            var state = AppState.Current;
            return BundleIcon.First(state.Launcher.Root, state.Launcher.Profiles(inst));
        }
        catch { return null; }
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
