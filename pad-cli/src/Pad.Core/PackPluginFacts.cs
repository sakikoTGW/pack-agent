using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 整合包声明的一个组合包（投影 catalog.json 里的 plugin mod，或老式 mods/*/plugin.json）。
/// P1 只读事实：不写任何文件。
/// </summary>
public sealed class PackPluginFact
{
    /// <summary>包的声明原文（npm spec / 本地路径）；包只给了名字时为空。</summary>
    public string Spec { get; init; } = "";
    /// <summary>归一后的包名：和 profile dependencies / dsh.profile.bundles 对齐用。</summary>
    public string Key { get; init; } = "";
    /// <summary>人看的插件名。</summary>
    public string Display { get; init; } = "";
    public bool Required { get; init; } = true;
}

/// <summary>
/// 一个已装投影包（P1 只读事实）：它声明了哪些组合包、当前 profile 里缺几个。
/// 真相只从 workspace 投影目录读；不写账本（P2 才有账本），也不改配置。
/// </summary>
public sealed class PackFacts
{
    public string PackId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Version { get; init; } = "";
    public string Dir { get; init; } = "";
    public IReadOnlyList<PackPluginFact> Plugins { get; init; } = [];

    /// <summary>当前 profile 里没装的声明数；由 <see cref="PackPluginFacts.Collect"/> 按 profile 填。</summary>
    public int MissingCount { get; set; }
    /// <summary>缺的插件名（顿号分隔），给 tooltip 用。</summary>
    public string MissingText { get; set; } = "";

    public int DeclaredCount => Plugins.Count;
    public bool HasPlugins => DeclaredCount > 0;
    public bool HasMissing => MissingCount > 0;

    /// <summary>P1 包行副标题：这个包带几个插件、几个不在当前 profile。</summary>
    public string SummaryText => !HasPlugins
        ? ""
        : MissingCount <= 0
            ? $"带 {DeclaredCount} 个插件（都在这个 profile）"
            : $"带 {DeclaredCount} 个插件（{MissingCount} 个不在这个 profile）";

    public string SummaryTip => MissingText.Length > 0 ? "这个 profile 缺：" + MissingText : "";
}

/// <summary>一个插件的来历：被哪些已装整合包声明，此刻装没装（P1 只读）。</summary>
public sealed class PluginOrigin
{
    public string Spec { get; init; } = "";
    public IReadOnlyList<string> PackIds { get; init; } = [];
    public IReadOnlyList<string> PackTitles { get; init; } = [];
    public bool Installed { get; init; }

    public bool FromPack => PackIds.Count > 0;
    public bool Missing => FromPack && !Installed;

    /// <summary>插件行小标：「来自整合包 X」（多个包用顿号）。</summary>
    public string OriginText => PackTitles.Count == 0 ? "" : "来自整合包 " + string.Join("、", PackTitles);

    public string OriginTip => Missing ? "这个整合包要求它，但这个 profile 里还没装" : "";
}

/// <summary>
/// 包 ↔ 插件只读事实收集器（D105 P1）：给一个实例 + profile，回答
/// ① 每个已装投影包声明了哪些组合包、缺几个；② 某个插件被哪些包要求。
/// 真相来源：workspace/.agent-pack/modpacks/*/catalog.json（或 mods/*/plugin.json），
/// profile 的 package.json（dependencies + dsh.profile.bundles）与 node_modules。
/// 本类不写文件、不碰 Packs.cs，也不判断"能不能装"。
/// </summary>
public sealed class PackPluginFacts
{
    public static PackPluginFacts Empty { get; } = new("", [], []);

    public string Profile { get; }
    public IReadOnlyList<PackFacts> Packs { get; }
    public IReadOnlyList<PluginOrigin> Plugins { get; }

    PackPluginFacts(string profile, IReadOnlyList<PackFacts> packs, IReadOnlyList<PluginOrigin> plugins)
    {
        Profile = profile;
        Packs = packs;
        Plugins = plugins;
    }

    /// <summary>
    /// 收集这份实例的包/插件事实；<paramref name="profile"/> 为空时按 lastProfile → home/profiles 第一个。
    /// 投影目录不存在（老实例 / 没装过包）返回空结果，不抛。
    /// </summary>
    public static PackPluginFacts Collect(Instance? inst, string? profile = null)
    {
        if (inst is null) return Empty;
        var root = Path.Combine(inst.Workspace.Path, ".agent-pack", "modpacks");
        if (!Directory.Exists(root)) return new(profile ?? "", [], []);

        var chosen = ResolveProfile(inst, profile);
        var (deps, bundles, hasBundleList) = ReadProfile(Path.Combine(inst.Home, "profiles", chosen));
        // 「装没装」以启动时真正加载的 dsh.profile.bundles 为准（P2 的「停用」也是摘 bundles
        // 留 deps：依赖还在磁盘上，但这一 profile 不再加载它 → 对包来说就是不在）。
        // 没有 bundles 列表可读的畸形 profile 才退回 dependencies / node_modules。
        bool Installed(string key) => key.Length > 0 && (hasBundleList
            ? bundles.Contains(key)
            : deps.Contains(key) || HasInNodeModules(inst.Home, chosen, key));

        var packs = new List<PackFacts>();
        var origins = new Dictionary<string, (List<string> Ids, List<string> Titles)>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(root)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var facts = ReadPack(dir);
            if (facts is null) continue;
            var missing = new List<string>();
            foreach (var plugin in facts.Plugins)
            {
                if (!Installed(plugin.Key)) missing.Add(plugin.Display);
                if (!origins.TryGetValue(plugin.Key, out var origin)) origin = ([], []);
                if (!origin.Ids.Contains(facts.PackId, StringComparer.OrdinalIgnoreCase))
                    origin.Ids.Add(facts.PackId);
                var title = facts.Title.Length > 0 ? facts.Title : facts.PackId;
                if (!origin.Titles.Contains(title, StringComparer.OrdinalIgnoreCase))
                    origin.Titles.Add(title);
                origins[plugin.Key] = origin;
            }
            facts.MissingCount = missing.Count;
            facts.MissingText = string.Join("、", missing);
            packs.Add(facts);
        }

        var plugins = origins
            .Select(kv => new PluginOrigin
            {
                Spec = kv.Key,
                PackIds = kv.Value.Ids,
                PackTitles = kv.Value.Titles,
                Installed = Installed(kv.Key),
            })
            .OrderBy(o => o.Spec, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackPluginFacts(chosen, packs, plugins);
    }

    /// <summary>按包 id 或显示名找一份包事实；找不到返回 null。</summary>
    public PackFacts? PackOf(string? packIdOrTitle)
    {
        var key = (packIdOrTitle ?? "").Trim();
        if (key.Length == 0) return null;
        return Packs.FirstOrDefault(p => string.Equals(p.PackId, key, StringComparison.OrdinalIgnoreCase))
            ?? Packs.FirstOrDefault(p => string.Equals(p.Title, key, StringComparison.OrdinalIgnoreCase))
            ?? Packs.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p.Dir), key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按插件 spec / 包名找它的来历；没被任何已装包声明返回 null。</summary>
    public PluginOrigin? OriginOf(string? spec)
    {
        var key = NormalizeKey(spec);
        if (key.Length == 0) return null;
        return Plugins.FirstOrDefault(o => string.Equals(o.Spec, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>声明这个插件的包名列表（给确认文案用）。</summary>
    public IReadOnlyList<string> PackTitlesOf(string? spec) => OriginOf(spec)?.PackTitles ?? [];

    /// <summary>管理器插件（pack-agent）：停用/移除后所有包投影都不可见（PA110）。</summary>
    public static bool IsManagerSpec(string? spec) =>
        (spec ?? "").Contains("pack-agent", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 归一到包名：npm spec 去掉尾部 @version（scoped 名保留 @scope）；file:/link: 取目录名。
    /// 和 profile dependencies 的 key 同口径。
    /// </summary>
    public static string NormalizeKey(string? spec)
    {
        var s = (spec ?? "").Trim();
        if (s.Length == 0) return "";
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
        {
            var path = s[(s.IndexOf(':') + 1)..].Trim().Trim('"').TrimEnd('\\', '/');
            return Path.GetFileName(path).ToLowerInvariant();
        }
        return LaunchPolicy.BundleBareName(s).ToLowerInvariant();
    }

    // ---- 投影目录（包声明）------------------------------------------------

    static PackFacts? ReadPack(string dir)
    {
        var id = Path.GetFileName(dir);
        var title = "";
        var version = "";
        var declared = new List<PackPluginFact>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var catalog = Path.Combine(dir, "catalog.json");
        var parsedCatalog = false;
        if (File.Exists(catalog))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(catalog));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    parsedCatalog = true;
                    if (Str(root, "id") is { Length: > 0 } catalogId) id = catalogId;
                    title = Str(root, "name");
                    version = Str(root, "version");
                    if (root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var mod in mods.EnumerateArray())
                        {
                            if (!string.Equals(Str(mod, "kind"), "plugin", StringComparison.OrdinalIgnoreCase))
                                continue;
                            AddDeclared(declared, seen, Str(mod, "spec"), Str(mod, "name"));
                        }
                    }
                }
            }
            catch { parsedCatalog = false; }
        }
        // catalog.json 读不出来时退回 mods/*/plugin.json（老投影/半成品目录）。
        if (!parsedCatalog) ScanModDirs(dir, declared, seen);
        if (title.Length == 0) title = PresetName(dir);
        if (title.Length == 0) title = id;
        if (version.Length == 0) version = Str(ReadObject(Path.Combine(dir, "package.json")), "version");
        return new PackFacts { PackId = id, Title = title, Version = version, Dir = dir, Plugins = declared };
    }

    static void ScanModDirs(string dir, List<PackPluginFact> declared, HashSet<string> seen)
    {
        var mods = Path.Combine(dir, "mods");
        if (!Directory.Exists(mods)) return;
        foreach (var sub in Directory.EnumerateDirectories(mods)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var manifest = ReadObject(Path.Combine(sub, "mod.json"));
            var payload = ReadObject(Path.Combine(sub, "plugin.json"));
            var kind = Str(manifest, "kind");
            // 有 manifest 就按 kind 判插件；没有 manifest 时只有 plugin.json 也算插件。
            if (kind.Length > 0 && !string.Equals(kind, "plugin", StringComparison.OrdinalIgnoreCase)) continue;
            if (payload is null && kind.Length == 0) continue;
            var name = Str(payload, "name");
            if (name.Length == 0) name = Str(manifest, "name");
            if (name.Length == 0) name = Str(payload, "id");
            var spec = Str(manifest, "spec");
            if (spec.Length == 0) spec = Str(payload, "spec");
            AddDeclared(declared, seen, spec, name);
        }
    }

    static void AddDeclared(List<PackPluginFact> declared, HashSet<string> seen, string spec, string name)
    {
        var raw = (spec ?? "").Trim();
        var display = (name ?? "").Trim();
        var key = NormalizeKey(raw.Length > 0 ? raw : display);
        if (key.Length == 0 || !seen.Add(key)) return;
        declared.Add(new PackPluginFact
        {
            Spec = raw.Length > 0 ? raw : display,
            Key = key,
            Display = display.Length > 0 ? display : key,
        });
    }

    static string PresetName(string dir)
    {
        var file = Path.Combine(dir, "preset.yml");
        if (!File.Exists(file)) return "";
        try
        {
            foreach (var line in File.ReadAllLines(file))
            {
                var s = line.Trim();
                if (!s.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) continue;
                return s[5..].Trim().Trim('"', '\'');
            }
        }
        catch { /* 读不到名字就用目录名 */ }
        return "";
    }

    // ---- profile（插件真相）----------------------------------------------

    static string ResolveProfile(Instance inst, string? profile)
    {
        var want = (profile ?? "").Trim();
        if (want.Length > 0) return want;
        var last = (inst.LastProfile ?? "").Trim();
        if (last.Length > 0) return last;
        try
        {
            var root = Path.Combine(inst.Home, "profiles");
            if (Directory.Exists(root))
            {
                var first = Directory.EnumerateDirectories(root)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (first is not null) return Path.GetFileName(first);
            }
        }
        catch { /* 没有 profile 就按空跑 */ }
        return "";
    }

    static (HashSet<string> Deps, HashSet<string> Bundles, bool HasBundleList) ReadProfile(string profileDir)
    {
        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasBundles = false;
        if (string.IsNullOrWhiteSpace(profileDir)) return (deps, bundles, hasBundles);
        var pkg = Path.Combine(profileDir, "package.json");
        if (!File.Exists(pkg)) return (deps, bundles, hasBundles);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (deps, bundles, hasBundles);
            if (root.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                foreach (var dep in d.EnumerateObject())
                {
                    var key = NormalizeKey(dep.Name);
                    if (key.Length > 0) deps.Add(key);
                }
            }
            if (root.TryGetProperty("dsh", out var dsh) && dsh.ValueKind == JsonValueKind.Object
                && dsh.TryGetProperty("profile", out var prof) && prof.ValueKind == JsonValueKind.Object
                && prof.TryGetProperty("bundles", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                hasBundles = true;
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var key = NormalizeKey(item.GetString());
                    if (key.Length > 0) bundles.Add(key);
                }
            }
        }
        catch { /* 坏 package.json 当作没装 */ }
        return (deps, bundles, hasBundles);
    }

    static bool HasInNodeModules(string home, string profile, string key)
    {
        if (key.Length == 0 || string.IsNullOrWhiteSpace(profile)) return false;
        var pkg = Path.Combine(home, "profiles", profile, "node_modules",
            key.Replace('/', Path.DirectorySeparatorChar), "package.json");
        return File.Exists(pkg);
    }

    // ---- JSON 小工具 -------------------------------------------------------

    static string Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    /// <summary>读一个平面 JSON 对象的字符串/数字字段；读不了返回 null。</summary>
    static Dictionary<string, string>? ReadObject(string file)
    {
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                map[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    _ => "",
                };
            }
            return map;
        }
        catch { return null; }
    }

    static string Str(Dictionary<string, string>? map, string name) =>
        map is not null && map.TryGetValue(name, out var v) ? v ?? "" : "";
}
