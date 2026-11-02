using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 本地收藏夹：像歌单一样收藏插件，跨实例共用。
/// 存 <c>&lt;launcher&gt;/library/favorites.json</c>（字符串数组，按收藏顺序）。
/// </summary>
public static class Favorites
{
    static string PathOf(string root) => Path.Combine(root, "library", "favorites.json");

    public static List<string> Load(string root)
    {
        try
        {
            var p = PathOf(root);
            if (!File.Exists(p)) return [];
            var arr = JsonSerializer.Deserialize<string[]>(File.ReadAllText(p)) ?? [];
            return arr.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }
        catch { return []; }
    }

    public static bool IsFavorite(string root, string spec) =>
        Load(root).Contains(spec, StringComparer.OrdinalIgnoreCase);

    /// <summary>切换收藏；返回切换后是否已收藏。</summary>
    public static bool Toggle(string root, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return false;
        var list = Load(root);
        var hit = list.FindIndex(s => s.Equals(spec, StringComparison.OrdinalIgnoreCase));
        var add = hit < 0;
        if (add) list.Add(spec); else list.RemoveAt(hit);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(root))!);
            File.WriteAllText(PathOf(root), JsonSerializer.Serialize(list));
        }
        catch { /* 写不进去就只影响这次 UI */ }
        return add;
    }
}
