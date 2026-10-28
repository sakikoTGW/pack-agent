using System.IO;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace Pad.Core;

/// <summary>
/// 关系页的界面状态：哪个视图是当前标签、每个视图展开了哪些节点。
/// 单独存一个文件，不塞进 pad.json —— 它是"看的位置"，不是配置（D44）。
/// </summary>
public sealed class UiState
{
    public sealed class Data
    {
        public string Active { get; set; } = "";
        public Dictionary<string, List<string>> Views { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Focus { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public string Active { get; set; } = "";
    public Dictionary<string, HashSet<string>> Views { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Focus { get; } = new(StringComparer.OrdinalIgnoreCase);

    static string File(string root) => IOPath.Combine(root, "ui-state.json");

    public static UiState Load(string root)
    {
        var state = new UiState();
        try
        {
            var path = File(root);
            if (!System.IO.File.Exists(path)) return state;

            var data = JsonSerializer.Deserialize<Data>(System.IO.File.ReadAllText(path));
            if (data is null) return state;

            state.Active = data.Active ?? "";
            foreach (var (view, id) in data.Focus)
                state.Focus[view] = id;
            foreach (var (view, ids) in data.Views)
                state.Views[view] = new HashSet<string>(ids ?? [], StringComparer.Ordinal);
        }
        catch { /* 坏文件：当作没有，不阻断页面 */ }
        return state;
    }

    public void Save(string root)
    {
        try
        {
            System.IO.Directory.CreateDirectory(root);
            var data = new Data
            {
                Active = Active,
                Focus = Focus.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                Views = Views.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase),
            };
            System.IO.File.WriteAllText(File(root),
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 写不了就算了，下次重来 */ }
    }

    public void Set(string view, IEnumerable<string> ids)
    {
        if (string.IsNullOrWhiteSpace(view)) return;
        Views[view] = new HashSet<string>(ids, StringComparer.Ordinal);
    }
}
