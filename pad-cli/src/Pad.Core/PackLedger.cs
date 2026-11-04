using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// 账本里记录的一个插件：包当初要求/加了什么（D105 §3.2）。
/// <see cref="Spec"/> 是包的声明（npm spec / <c>file:&lt;绝对路径&gt;</c>）；
/// <see cref="AddedVersion"/> 只记“当初我加的版本”，对账时以 profile 为准；
/// 包内本地插件（Q6）先复制到实例目录再装，所以 <see cref="LocalDir"/> 是相对实例目录的稳定路径。
/// </summary>
public sealed class LedgerPlugin
{
    [JsonPropertyName("spec")] public string Spec { get; set; } = "";
    [JsonPropertyName("addedVersion")] public string? AddedVersion { get; set; }
    /// <summary>pack = 由整合包 import 添加；manual = 用户手工添加（不进账本，只留字段给未来）。</summary>
    [JsonPropertyName("origin")] public string Origin { get; set; } = "pack";
    [JsonPropertyName("localDir")] public string? LocalDir { get; set; }

    [JsonIgnore]
    public bool IsLocal =>
        !string.IsNullOrWhiteSpace(LocalDir)
        || Spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
        || Spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase);
}

/// <summary>包内容（投影/白名单）是实例级事实，和 profile 无关。</summary>
public sealed class LedgerContent
{
    /// <summary>投影目录，相对实例 workspace：<c>.agent-pack/modpacks/pack-agent-modpack-&lt;slug&gt;</c>。</summary>
    [JsonPropertyName("dir")] public string Dir { get; set; } = "";
    [JsonPropertyName("allow")] public bool Allow { get; set; } = true;
}

/// <summary>一个 profile 里这包插件部分的记录（Q2：插件按 profile 分账）。</summary>
public sealed class LedgerProfile
{
    /// <summary>installed | disabled | incomplete（<see cref="PackLedger"/> 上的常量）。</summary>
    [JsonPropertyName("state")] public string State { get; set; } = PackLedger.StateInstalled;
    [JsonPropertyName("plugins")] public List<LedgerPlugin> Plugins { get; set; } = [];
}

/// <summary>账本里一个已装整合包。</summary>
public sealed class LedgerPack
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    /// <summary>同一 pack id 只保留一个版本（Q3）；版本变化走 diff，不并排。</summary>
    [JsonPropertyName("installedVersion")] public string InstalledVersion { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("installedAt")] public string InstalledAt { get; set; } = "";
    [JsonPropertyName("content")] public LedgerContent Content { get; set; } = new();
    [JsonPropertyName("profiles")] public Dictionary<string, LedgerProfile> Profiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>落在 <c>instances/&lt;id&gt;/pack-ledger.json</c> 的文档本体（schema: pack-agent.pack-ledger/v1）。</summary>
public sealed class LedgerData
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = PackLedger.SchemaId;
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("packs")] public Dictionary<string, LedgerPack> Packs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>反向索引：profile → 插件 key → 要求它的包（引用计数，R4）。</summary>
    [JsonPropertyName("owners")] public Dictionary<string, Dictionary<string, List<string>>> Owners { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>账本 vs profile 真实状态的一处差异（读时对账，不自动改，§3.2）。</summary>
public sealed class LedgerGap
{
    /// <summary>projection | dependency | bundle | bundle-extra | version | local-dir。</summary>
    public string Kind { get; init; } = "";
    public string Spec { get; init; } = "";
    public string Expected { get; init; } = "";
    public string Actual { get; init; } = "";
    public string Text { get; init; } = "";
}

/// <summary>包声明过、但 profile 里版本和“当初加的版本”不一样的插件。</summary>
public sealed class PluginVersionConflict
{
    public string Spec { get; init; } = "";
    public string Expected { get; init; } = "";
    public string Actual { get; init; } = "";
    public string Text => $"{Spec}：账本 {Expected} / 实际 {Actual}";
}

/// <summary>profile 的插件真相（package.json + node_modules），只读快照。</summary>
public sealed class ProfileFacts
{
    public string Profile { get; init; } = "";
    public string Dir { get; init; } = "";
    public bool Exists { get; init; }
    /// <summary>dependencies：包名 → spec（file: 路径保留原样）。</summary>
    public Dictionary<string, string> Deps { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>dsh.profile.bundles 原文（保持 DSH 写入的顺序与大小写）。</summary>
    public List<string> Bundles { get; init; } = [];
    public bool HasBundleList { get; init; }

    public bool HasDependency(string name)
    {
        var want = (name ?? "").Trim();
        if (want.Length == 0) return false;
        if (Deps.ContainsKey(want)) return true;
        return Deps.Keys.Any(k => string.Equals(k, want, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>bundles 里真实那一行的写法（大小写对齐用）；没有返回 null。</summary>
    public string? BundleName(string name)
    {
        var want = (name ?? "").Trim();
        if (want.Length == 0) return null;
        return Bundles.FirstOrDefault(b => string.Equals(b, want, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>整合包 manifest 里声明的一个组合包（<c>dsh.plugins[]</c>）。</summary>
public sealed class PackManifestPlugin
{
    /// <summary>声明原文（缺省取 name）。</summary>
    public string Spec { get; init; } = "";
    /// <summary>包名（声明没有 spec 时用它）。</summary>
    public string Name { get; init; } = "";
    public bool Required { get; init; } = true;
    /// <summary>包内本地插件目录（相对包根，Q6）。空 = 走 npm spec。</summary>
    public string Local { get; init; } = "";

    /// <summary>实际交给 <c>dsh plugin add</c> 的 spec 依据：spec 优先，其次 name。</summary>
    public string EffectiveSpec => Spec.Length > 0 ? Spec : Name;
}

/// <summary>
/// PAD 侧直接读的包清单事实：name / version / dsh.profile / dsh.plugins[]。
/// 只读，不解压整包；<c>.pack.json</c> 直接读，<c>.pack.zip</c>/<c>.zip</c> 读 zip 里的
/// 根 <c>pack.json</c>（System.IO.Compression，不依赖 TS/仓库）。
/// </summary>
public sealed class PackManifest
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Profile { get; init; } = "";
    /// <summary>包钉的 DSH 内核版本/范围（<c>dsh.version</c>）；没写为空。</summary>
    public string DshVersion { get; init; } = "";
    public List<PackManifestPlugin> Plugins { get; init; } = [];

    /// <summary>读不出来（不存在/坏 zip/坏 JSON/pinst）返回 null，不抛。</summary>
    public static PackManifest? TryRead(string packPath)
    {
        var path = (packPath ?? "").Trim().Trim('"');
        if (path.Length == 0 || !File.Exists(path)) return null;
        try
        {
            var lower = path.ToLowerInvariant();
            if (!lower.EndsWith(".zip")) return Parse(File.ReadAllText(path));
            using var zip = ZipFile.OpenRead(path);
            var entry = FindPackJson(zip);
            if (entry is null) return null;
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    static ZipArchiveEntry? FindPackJson(ZipArchive zip)
    {
        ZipArchiveEntry? loose = null;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            var trimmed = name.TrimStart('.', '/');
            if (string.Equals(trimmed, "pack.json", StringComparison.OrdinalIgnoreCase)) return entry;
            if (loose is null
                && string.Equals(Path.GetFileName(trimmed), "pack.json", StringComparison.OrdinalIgnoreCase))
                loose = entry;
        }
        return loose;
    }

    static PackManifest? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        var plugins = new List<PackManifestPlugin>();
        var profile = "";
        var dshVersion = "";
        if (root.TryGetProperty("dsh", out var dsh) && dsh.ValueKind == JsonValueKind.Object)
        {
            if (dsh.TryGetProperty("version", out var kv) && kv.ValueKind == JsonValueKind.String)
                dshVersion = kv.GetString()?.Trim() ?? "";
            if (dsh.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.String)
                profile = p.GetString() ?? "";
            if (dsh.TryGetProperty("plugins", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    var spec = Str(el, "spec");
                    var name = Str(el, "name");
                    if (spec.Length == 0 && name.Length == 0) continue;
                    var required = true;
                    if (el.TryGetProperty("required", out var req)
                        && req.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        required = req.ValueKind == JsonValueKind.True;
                    plugins.Add(new PackManifestPlugin
                    {
                        Spec = spec,
                        Name = name,
                        Required = required,
                        Local = Str(el, "local"),
                    });
                }
            }
        }
        return new PackManifest
        {
            Name = Str(root, "name"),
            Version = Str(root, "version"),
            Profile = profile,
            DshVersion = dshVersion,
            Plugins = plugins,
        };
    }

    static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim()
            : "";
}

/// <summary>
/// 整合包 × 插件的归属账本（D105 §3.2/§7）。每个实例一份独立文件
/// <c>&lt;launcher-root&gt;/instances/&lt;id&gt;/pack-ledger.json</c> —— 绝不写进
/// <c>instance.json</c>（C# 的 <see cref="Instance"/> 模型不认识 TS 写的 packs/plugins 字段，
/// 任何 <c>SaveInstance</c> 都会把它们抹掉）。
///
/// 账本不是真相：读的时候以（a）投影目录/allow、（b）profile 的
/// <c>package.json</c> + <c>dsh.profile.bundles</c> 为准；账本只回答“这个插件是谁要的”
/// “这个包当初要求什么版本”。对账失败只报 <see cref="LedgerGap"/>，不自动改用户文件。
/// </summary>
public sealed class PackLedger
{
    public const string SchemaId = "pack-agent.pack-ledger/v1";
    public const string StateInstalled = "installed";
    public const string StateDisabled = "disabled";
    public const string StateIncomplete = "incomplete";
    public const string StateAbsent = "absent";

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly Launcher _launcher;
    readonly Instance _instance;

    /// <summary><c>instances/&lt;id&gt;/pack-ledger.json</c> 的绝对路径。</summary>
    public string FilePath { get; }
    public LedgerData Data { get; private set; } = new();
    /// <summary>文件坏了/读不动时的原因（此时 Data 是空账本，UI 可以提示“未登记”）。</summary>
    public string? LoadError { get; private set; }

    PackLedger(Launcher launcher, Instance instance, string path)
    {
        _launcher = launcher;
        _instance = instance;
        FilePath = path;
    }

    /// <summary>读账本；文件不存在/坏掉时返回空账本（旧实例 = 未登记，不报错）。</summary>
    public static PackLedger Load(Launcher launcher, Instance instance)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(instance);
        var path = Path.Combine(launcher.InstanceDir(instance.Id), "pack-ledger.json");
        var ledger = new PackLedger(launcher, instance, path);
        if (!File.Exists(path)) return ledger;
        try
        {
            var data = JsonSerializer.Deserialize<LedgerData>(File.ReadAllText(path));
            if (data is null)
            {
                ledger.LoadError = "pack-ledger.json 是空的";
                return ledger;
            }
            ledger.Data = Normalize(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ledger.LoadError = ex.Message;
        }
        return ledger;
    }

    /// <summary>JSON 反序列化不会保留我们初始化时的大小写不敏感比较器，这里补回来。</summary>
    static LedgerData Normalize(LedgerData data)
    {
        data.Schema = string.IsNullOrWhiteSpace(data.Schema) ? SchemaId : data.Schema;
        data.Packs ??= new Dictionary<string, LedgerPack>(StringComparer.OrdinalIgnoreCase);
        data.Owners ??= new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in data.Packs.Values)
        {
            pack.Content ??= new LedgerContent();
            pack.Profiles ??= new Dictionary<string, LedgerProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var prof in pack.Profiles.Values)
                prof.Plugins ??= [];
        }
        foreach (var slice in data.Owners.Values)
        {
            if (slice is null) continue;
            foreach (var key in slice.Keys.ToList())
                slice[key] ??= [];
        }
        return data;
    }

    /// <summary>账本里的时间戳统一成 <c>2026-11-04T00:00:00.000Z</c>（schema 示例的形状）。</summary>
    static string UtcStamp() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    /// <summary>原子写：写临时文件再整体替换（不写 instance.json，不碰 home）。</summary>
    public void Save()
    {
        Data.Schema = SchemaId;
        Data.Updated = UtcStamp();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = $"{FilePath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json) + "\n", new UTF8Encoding(false));
        File.Move(tmp, FilePath, overwrite: true);
    }

    // ---- 包 / profile 查找 -------------------------------------------------

    static bool Same(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    string? PackKey(string packId)
    {
        var want = (packId ?? "").Trim();
        if (want.Length == 0) return null;
        if (Data.Packs.ContainsKey(want)) return want;
        return Data.Packs.Keys.FirstOrDefault(k => Same(k, want));
    }

    /// <summary>按 pack id 取条目（大小写不敏感）；没登记返回 null。</summary>
    public LedgerPack? Get(string packId) =>
        PackKey(packId) is { } key ? Data.Packs[key] : null;

    /// <summary>取某 profile 的记录（大小写不敏感）；没有返回 null。</summary>
    public LedgerProfile? ProfileOf(string packId, string profile)
    {
        var pack = Get(packId);
        if (pack is null) return null;
        return TryProfile(pack, (profile ?? "").Trim(), out var prof) ? prof : null;
    }

    /// <summary>根据当前 profile 事实算出这个插件在 dependencies/owners 里的 key。</summary>
    public string KeyOf(string profile, LedgerPlugin plugin)
    {
        if (plugin is null) return "";
        var facts = ReadProfile(_instance, (profile ?? "").Trim());
        return ResolvePluginKey(plugin, facts);
    }

    LedgerPack EnsurePack(string packId, string? title = null)
    {
        var id = (packId ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("packId is required", nameof(packId));
        if (PackKey(id) is { } key && Data.Packs.TryGetValue(key, out var hit)) return hit;
        var pack = new LedgerPack { Title = string.IsNullOrWhiteSpace(title) ? id : title!.Trim() };
        Data.Packs[id] = pack;
        return pack;
    }

    public static string DefaultProfile(Launcher launcher, Instance instance)
    {
        // TS 写的 instance.json 有 profile.name（C# 的 Instance 模型没有这个字段）。
        try
        {
            var file = Path.Combine(launcher.InstanceDir(instance.Id), "instance.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("profile", out var prof)
                    && prof.ValueKind == JsonValueKind.Object
                    && prof.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() is { Length: > 0 } got)
                    return got.Trim();
            }
        }
        catch { /* 读不了就往下退 */ }
        if (!string.IsNullOrWhiteSpace(instance.LastProfile)) return instance.LastProfile!;
        return launcher.Profiles(instance).FirstOrDefault()?.Name ?? "web";
    }

    // ---- 写：装 / 改状态 / 移除 -------------------------------------------

    /// <summary>
    /// 记一条已装包（内容投影是实例级）。同一 pack id 重复调用 = 更新版本/路径；
    /// 版本变了才刷新 <c>installedAt</c>（Q3：不并排）。
    /// </summary>
    public LedgerPack RecordPack(string packId, string title, string installedVersion, string path,
        string? contentDir = null, bool? allow = null)
    {
        var pack = EnsurePack(packId, title);
        var version = (installedVersion ?? "").Trim();
        var versionChanged = !string.IsNullOrWhiteSpace(version)
                             && !string.Equals(pack.InstalledVersion, version, StringComparison.OrdinalIgnoreCase);
        pack.Title = string.IsNullOrWhiteSpace(title) ? pack.Title : title.Trim();
        if (version.Length > 0) pack.InstalledVersion = version;
        pack.Path = (path ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(contentDir))
            pack.Content.Dir = contentDir!.Replace('\\', '/').Trim();
        if (allow is { } on) pack.Content.Allow = on;
        if (pack.InstalledAt.Length == 0 || versionChanged)
            pack.InstalledAt = UtcStamp();
        return pack;
    }

    /// <summary>内容投影/白名单变化（实例级，不涉及 profile 插件）。</summary>
    public void SetContent(string packId, string? dir = null, bool? allow = null)
    {
        var pack = Get(packId) ?? throw new ArgumentException($"pack not in ledger: {packId}", nameof(packId));
        if (!string.IsNullOrWhiteSpace(dir)) pack.Content.Dir = dir!.Replace('\\', '/').Trim();
        if (allow is { } on) pack.Content.Allow = on;
    }

    /// <summary>设置某 profile 的包状态；非法值抛 <see cref="ArgumentException"/>。</summary>
    public void SetState(string packId, string profile, string state)
    {
        var want = (state ?? "").Trim().ToLowerInvariant();
        if (want is not (StateInstalled or StateDisabled or StateIncomplete))
            throw new ArgumentException($"state must be installed|disabled|incomplete, got '{state}'", nameof(state));
        var pack = Get(packId) ?? throw new ArgumentException($"pack not in ledger: {packId}", nameof(packId));
        EnsureProfile(pack, RequireProfile(profile)).State = want;
    }

    static string RequireProfile(string? profile)
    {
        var name = (profile ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("profile is required", nameof(profile));
        return name;
    }

    static LedgerProfile EnsureProfile(LedgerPack pack, string profile)
    {
        if (TryProfile(pack, profile, out var hit)) return hit!;
        var created = new LedgerProfile();
        pack.Profiles[profile] = created;
        return created;
    }

    static bool TryProfile(LedgerPack pack, string profile, out LedgerProfile? found)
    {
        found = null;
        if (pack.Profiles.TryGetValue(profile, out var direct)) { found = direct; return true; }
        foreach (var kv in pack.Profiles)
        {
            if (!Same(kv.Key, profile)) continue;
            found = kv.Value;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 记一次安装：requirements 是这一次真正加进 profile 的插件（成功才记）；
    /// <paramref name="installedVersions"/> 给了就按 key 覆盖 addedVersion，否则从
    /// profile 的 <c>node_modules/&lt;pkg&gt;/package.json</c> 读当前版本。
    /// 返回这些插件在 owners 里的 key。
    /// </summary>
    public List<string> RecordInstall(string packId, string profile,
        IEnumerable<LedgerPlugin> requirements,
        IReadOnlyDictionary<string, string?>? installedVersions = null)
    {
        var pack = EnsurePack(packId);
        var profName = RequireProfile(profile);
        var prof = EnsureProfile(pack, profName);
        var facts = ReadProfile(_instance, profName);

        var next = new List<LedgerPlugin>();
        var keys = new List<string>();
        foreach (var req in requirements ?? [])
        {
            var spec = (req.Spec ?? "").Trim();
            if (spec.Length == 0) continue;
            var plugin = new LedgerPlugin
            {
                Spec = spec,
                Origin = string.IsNullOrWhiteSpace(req.Origin) ? "pack" : req.Origin.Trim(),
                LocalDir = string.IsNullOrWhiteSpace(req.LocalDir) ? null : req.LocalDir!.Trim(),
                AddedVersion = string.IsNullOrWhiteSpace(req.AddedVersion) ? null : req.AddedVersion!.Trim(),
            };
            var key = ResolvePluginKey(plugin, facts);
            if (plugin.AddedVersion is null)
            {
                plugin.AddedVersion = LookupVersion(installedVersions, key)
                                      ?? InstalledVersion(_instance, profName, key);
            }
            next.RemoveAll(p => Same(p.Spec, plugin.Spec) || Same(PluginKey(p.Spec), key));
            next.Add(plugin);
            if (!keys.Any(k => Same(k, key))) keys.Add(key);
        }
        prof.Plugins = next;
        RebuildOwners(profName, PackKey((packId ?? "").Trim()) ?? (packId ?? "").Trim(), prof, facts);
        return keys;
    }

    static string? LookupVersion(IReadOnlyDictionary<string, string?>? versions, string key)
    {
        if (versions is null || key.Length == 0) return null;
        if (versions.TryGetValue(key, out var direct) && !string.IsNullOrWhiteSpace(direct)) return direct;
        foreach (var kv in versions)
        {
            if (Same(kv.Key, key) && !string.IsNullOrWhiteSpace(kv.Value)) return kv.Value;
        }
        return null;
    }

    /// <summary>插件页 remove 之后调：从账本摘掉这一条、更新 owners、包标 incomplete。</summary>
    public bool RecordPluginRemoved(string packId, string profile, string spec)
    {
        var pack = Get(packId);
        if (pack is null) return false;
        var profName = RequireProfile(profile);
        if (!TryProfile(pack, profName, out var prof) || prof is null) return false;
        var facts = ReadProfile(_instance, profName);

        var key = PluginKey(spec);
        LedgerPlugin? hit = null;
        foreach (var plugin in prof.Plugins)
        {
            if (Same(plugin.Spec, spec)) { hit = plugin; break; }
            if (Same(ResolvePluginKey(plugin, facts), key)) { hit = plugin; break; }
        }
        if (hit is null) return false;
        prof.Plugins.Remove(hit);
        RemoveOwner(profName, ResolvePluginKey(hit, facts), PackKey(packId) ?? packId);
        prof.State = StateIncomplete;
        return true;
    }

    /// <summary>删掉整个包的账本条目（卸载用），同时清掉它的 owners 引用。</summary>
    public bool RemovePack(string packId)
    {
        var key = PackKey(packId);
        if (key is null || !Data.Packs.TryGetValue(key, out var pack)) return false;
        foreach (var prof in pack.Profiles.Keys.ToList())
        {
            var facts = ReadProfile(_instance, prof);
            foreach (var plugin in pack.Profiles[prof].Plugins)
                RemoveOwner(prof, ResolvePluginKey(plugin, facts), key);
        }
        // owners 里可能有本包但 plugins 列表已空的残影：全量扫一遍更稳。
        foreach (var slice in Data.Owners.Values)
        {
            foreach (var pluginKey in slice.Keys.ToList())
            {
                slice[pluginKey].RemoveAll(id => Same(id, key));
                if (slice[pluginKey].Count == 0) slice.Remove(pluginKey);
            }
        }
        return Data.Packs.Remove(key);
    }

    // ---- owners / 引用计数 -------------------------------------------------

    Dictionary<string, List<string>>? OwnerSlice(string profile, bool create)
    {
        var name = (profile ?? "").Trim();
        if (name.Length == 0) return null;
        if (Data.Owners.TryGetValue(name, out var hit)) return hit;
        foreach (var kv in Data.Owners)
            if (Same(kv.Key, name)) return kv.Value;
        if (!create) return null;
        var slice = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        Data.Owners[name] = slice;
        return slice;
    }

    void RemoveOwner(string profile, string pluginKey, string packId)
    {
        var slice = OwnerSlice(profile, create: false);
        if (slice is null || pluginKey.Length == 0) return;
        foreach (var key in slice.Keys.ToList())
        {
            if (!Same(key, pluginKey)) continue;
            slice[key].RemoveAll(id => Same(id, packId));
            if (slice[key].Count == 0) slice.Remove(key);
        }
    }

    void RebuildOwners(string profile, string packKey, LedgerProfile prof, ProfileFacts facts)
    {
        var slice = OwnerSlice(profile, create: true);
        if (slice is null || packKey.Length == 0) return;
        // 这个包在这个 profile 的旧引用先清空（重装/修复都可能改了插件集合）。
        foreach (var key in slice.Keys.ToList())
        {
            slice[key].RemoveAll(id => Same(id, packKey));
            if (slice[key].Count == 0) slice.Remove(key);
        }
        foreach (var plugin in prof.Plugins)
        {
            var key = ResolvePluginKey(plugin, facts);
            if (key.Length == 0) continue;
            var actual = slice.Keys.FirstOrDefault(k => Same(k, key)) ?? key;
            if (!slice.TryGetValue(actual, out var list))
            {
                list = [];
                slice[actual] = list;
            }
            if (!list.Any(id => Same(id, packKey))) list.Add(packKey);
        }
    }

    /// <summary>这个插件被哪些已装包要求（按 profile，R4）。</summary>
    public List<string> OwnersOf(string profile, string spec)
    {
        var key = PluginKey(spec);
        var slice = OwnerSlice(profile, create: false);
        if (slice is null || key.Length == 0) return [];
        foreach (var kv in slice)
            if (Same(kv.Key, key)) return kv.Value.ToList();
        return [];
    }

    /// <summary>只被这一个包要求的插件 key（卸载时才可以回滚）。</summary>
    public List<string> ExclusiveFor(string profile, string packId)
    {
        var keys = RecordedKeys(profile, packId);
        return keys.Where(k =>
        {
            var owners = OwnersOf(profile, k);
            return owners.Count == 0 || (owners.Count == 1 && Same(owners[0], packId));
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>还被别的包要求的插件 key（卸载时保留，R4/动作矩阵）。</summary>
    public List<string> SharedFor(string profile, string packId)
    {
        var keys = RecordedKeys(profile, packId);
        return keys.Where(k => OwnersOf(profile, k).Count > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>这个包在这个 profile 里记录过的插件 key。</summary>
    public List<string> RecordedKeys(string profile, string packId)
    {
        var pack = Get(packId);
        if (pack is null || !TryProfile(pack, (profile ?? "").Trim(), out var prof) || prof is null) return [];
        var facts = ReadProfile(_instance, profile);
        return prof.Plugins.Select(p => ResolvePluginKey(p, facts))
            .Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---- 读时对账 ---------------------------------------------------------

    /// <summary>账本记录 vs profile 实际（dependencies/bundles/node_modules）的差异。只报告，不修。</summary>
    public List<LedgerGap> GapsFor(string profile, string packId)
    {
        var gaps = new List<LedgerGap>();
        var pack = Get(packId);
        if (pack is null) return gaps;
        var profName = (profile ?? "").Trim();
        if (!TryProfile(pack, profName, out var prof) || prof is null) return gaps;

        if (!string.IsNullOrWhiteSpace(pack.Content.Dir))
        {
            var dir = Path.Combine(_instance.Workspace.Path,
                pack.Content.Dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir))
                gaps.Add(new LedgerGap
                {
                    Kind = "projection", Spec = "", Expected = pack.Content.Dir,
                    Text = $"投影目录不在了：{pack.Content.Dir}",
                });
        }

        var facts = ReadProfile(_instance, profName);
        foreach (var plugin in prof.Plugins)
        {
            var key = ResolvePluginKey(plugin, facts);
            if (plugin.IsLocal)
            {
                var localPath = LocalDirPath(plugin);
                if (localPath is not null && !Directory.Exists(localPath))
                    gaps.Add(new LedgerGap
                    {
                        Kind = "local-dir", Spec = plugin.Spec, Expected = localPath,
                        Text = $"本地插件目录不在了：{localPath}",
                    });
            }
            var depPresent = key.Length > 0 && facts.HasDependency(key);
            var bundle = key.Length > 0 ? facts.BundleName(key) : null;
            if (!depPresent)
            {
                gaps.Add(new LedgerGap
                {
                    Kind = "dependency", Spec = plugin.Spec, Expected = key,
                    Text = $"依赖不在 profile 里：{key}",
                });
                continue;
            }
            if (prof.State == StateDisabled)
            {
                // 停用 = 从 bundles 摘掉、dependencies 保留；bundle 行回来了才是异常。
                if (bundle is not null)
                    gaps.Add(new LedgerGap
                    {
                        Kind = "bundle-extra", Spec = plugin.Spec, Expected = "不在 bundles", Actual = bundle,
                        Text = $"包已停用，但 bundle 行又出现了：{bundle}",
                    });
            }
            else if (bundle is null)
            {
                gaps.Add(new LedgerGap
                {
                    Kind = "bundle", Spec = plugin.Spec, Expected = key,
                    Text = $"插件没在 dsh.profile.bundles 里：{key}",
                });
            }

            var actual = InstalledVersion(_instance, profName, key);
            if (!string.IsNullOrWhiteSpace(plugin.AddedVersion) && !string.IsNullOrWhiteSpace(actual)
                && !string.Equals(plugin.AddedVersion, actual, StringComparison.OrdinalIgnoreCase))
            {
                gaps.Add(new LedgerGap
                {
                    Kind = "version", Spec = plugin.Spec, Expected = plugin.AddedVersion!, Actual = actual!,
                    Text = $"版本和装包时不一样：{key} {plugin.AddedVersion} → {actual}",
                });
            }
        }
        return gaps;
    }

    /// <summary>按包修复要补的插件（声明 spec 列表）；版本偏差不算“缺失”，RepairPack 不碰。</summary>
    public List<string> MissingFor(string profile, string packId)
    {
        var specs = new List<string>();
        foreach (var gap in GapsFor(profile, packId))
        {
            if (gap.Kind is not ("dependency" or "bundle" or "local-dir")) continue;
            if (!specs.Any(s => Same(s, gap.Spec))) specs.Add(gap.Spec);
        }
        return specs;
    }

    /// <summary>包声明过、但 profile 里版本和“当初加的版本”不一致的插件（R6 冲突提示用）。</summary>
    public List<PluginVersionConflict> VersionConflicts(string profile, string packId)
    {
        var list = new List<PluginVersionConflict>();
        foreach (var gap in GapsFor(profile, packId))
        {
            if (gap.Kind != "version") continue;
            list.Add(new PluginVersionConflict
            {
                Spec = gap.Spec, Expected = gap.Expected, Actual = gap.Actual,
            });
        }
        return list;
    }

    /// <summary>
    /// 读时对账后的真实状态：无账本/无投影 = absent；缺件 = incomplete；
    /// 内容停用 = disabled；否则 installed。账本里的 <c>state</c> 只是上次写入的意图。
    /// </summary>
    public string EffectiveState(string profile, string packId)
    {
        var pack = Get(packId);
        if (pack is null) return StateAbsent;
        if (!TryProfile(pack, (profile ?? "").Trim(), out var prof) || prof is null) return StateAbsent;
        if (!string.IsNullOrWhiteSpace(pack.Content.Dir))
        {
            var dir = Path.Combine(_instance.Workspace.Path,
                pack.Content.Dir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) return StateAbsent;
        }
        var gaps = GapsFor(profile ?? "", packId);
        if (gaps.Any(g => g.Kind is "projection" or "dependency" or "local-dir")) return StateIncomplete;
        if (!pack.Content.Allow)
            return gaps.Any(g => g.Kind == "bundle-extra") ? StateIncomplete : StateDisabled;
        if (gaps.Any(g => g.Kind is "bundle" or "version" or "bundle-extra")) return StateIncomplete;
        return StateInstalled;
    }

    /// <summary>投影目录是否还在（实例级）。</summary>
    public bool ProjectionExists(string packId)
    {
        var pack = Get(packId);
        if (pack is null || string.IsNullOrWhiteSpace(pack.Content.Dir)) return false;
        return Directory.Exists(Path.Combine(_instance.Workspace.Path,
            pack.Content.Dir.Replace('/', Path.DirectorySeparatorChar)));
    }

    // ---- 路径 / 版本 / 插件 key -------------------------------------------

    /// <summary>本地插件的实例内绝对路径（Q6：&lt;instance&gt;/home/local-plugins/&lt;packId&gt;/&lt;name&gt;）。</summary>
    public string? LocalDirPath(LedgerPlugin plugin)
    {
        var rel = plugin.LocalDir;
        if (!string.IsNullOrWhiteSpace(rel))
        {
            var clean = rel!.Trim().Replace('/', Path.DirectorySeparatorChar);
            return Path.IsPathRooted(clean) ? clean : Path.Combine(_launcher.InstanceDir(_instance.Id), clean);
        }
        var spec = plugin.Spec ?? "";
        if (spec.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || spec.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
        {
            var path = spec[(spec.IndexOf(':') + 1)..].Trim().Trim('"').TrimEnd('\\', '/');
            return path.Length == 0 ? null : Path.GetFullPath(path);
        }
        return null;
    }

    /// <summary>插件在 profile dependencies / owners 里的 key：npm spec 去版本，file: 取目录名。</summary>
    public static string PluginKey(string? spec)
    {
        var s = (spec ?? "").Trim();
        if (s.Length == 0) return "";
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
        {
            var path = s[(s.IndexOf(':') + 1)..].Trim().Trim('"').TrimEnd('\\', '/');
            var name = Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
            return name.Trim();
        }
        return LaunchPolicy.BundleBareName(s);
    }

    string ResolvePluginKey(LedgerPlugin plugin, ProfileFacts facts)
    {
        if (plugin.IsLocal)
        {
            var dir = LocalDirPath(plugin);
            if (dir is not null)
            {
                foreach (var dep in facts.Deps)
                {
                    if (!dep.Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                        && !dep.Value.StartsWith("link:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var target = dep.Value[(dep.Value.IndexOf(':') + 1)..].Trim().Trim('"');
                    if (target.Length == 0) continue;
                    try
                    {
                        if (SameDir(Path.GetFullPath(target), dir)) return dep.Key;
                    }
                    catch (ArgumentException) { /* 坏路径忽略 */ }
                }
            }
        }
        return PluginKey(plugin.Spec);
    }

    static bool SameDir(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>类 node_modules/&lt;pkg&gt;/package.json 的 version；没有返回 null。</summary>
    public static string? InstalledVersion(Instance instance, string profile, string depName)
    {
        var name = (depName ?? "").Trim();
        if (name.Length == 0 || string.IsNullOrWhiteSpace(profile)) return null;
        var pkg = Path.Combine(instance.Home, "profiles", profile, "node_modules",
            name.Replace('/', Path.DirectorySeparatorChar), "package.json");
        return PackageVersion(pkg);
    }

    /// <summary>任意 package.json 的 version 字段；读不出来返回 null。</summary>
    public static string? PackageVersion(string packageJsonPath)
    {
        if (!File.Exists(packageJsonPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            return doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // ---- profile 读写（试停用/启用联动用，与 Trial.WriteBundles 同机制）----

    /// <summary>读 profile 的 dependencies + dsh.profile.bundles（只读快照）。</summary>
    public static ProfileFacts ReadProfile(Instance instance, string? profile)
    {
        var name = (profile ?? "").Trim();
        var dir = name.Length == 0 ? "" : Path.Combine(instance.Home, "profiles", name);
        var pkg = name.Length == 0 ? "" : Path.Combine(dir, "package.json");
        var deps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bundles = new List<string>();
        var hasBundleList = false;
        if (pkg.Length > 0 && File.Exists(pkg))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var dep in d.EnumerateObject())
                        {
                            var spec = dep.Value.ValueKind switch
                            {
                                JsonValueKind.String => dep.Value.GetString() ?? "",
                                _ => dep.Value.GetRawText(),
                            };
                            deps[dep.Name] = spec;
                        }
                    }
                    if (root.TryGetProperty("dsh", out var dsh) && dsh.ValueKind == JsonValueKind.Object
                        && dsh.TryGetProperty("profile", out var prof) && prof.ValueKind == JsonValueKind.Object
                        && prof.TryGetProperty("bundles", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        hasBundleList = true;
                        foreach (var item in arr.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                                bundles.Add(s);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                /* 坏 package.json 当空 profile 看；账本对账会报缺件 */
            }
        }
        return new ProfileFacts
        {
            Profile = name,
            Dir = dir,
            Exists = pkg.Length > 0 && File.Exists(pkg),
            Deps = deps,
            Bundles = bundles,
            HasBundleList = hasBundleList,
        };
    }

    /// <summary>
    /// 只改 <c>dsh.profile.bundles</c>，其它键（dependencies 等）原样保留（与 Trial.WriteBundles 同口径，
    /// 只是放到账本工具里给 P2 停用/启用联动共用）。profile 不存在时抛 <see cref="FileNotFoundException"/>。
    /// </summary>
    public static void WriteBundles(Instance instance, string profile, IEnumerable<string> bundles)
    {
        var name = RequireProfile(profile);
        var pkg = Path.Combine(instance.Home, "profiles", name, "package.json");
        if (!File.Exists(pkg))
            throw new FileNotFoundException($"profile `{name}` has no package.json", pkg);
        var node = JsonNode.Parse(File.ReadAllText(pkg))!.AsObject();
        var dsh = node["dsh"]?.AsObject();
        if (dsh is null)
        {
            dsh = new JsonObject();
            node["dsh"] = dsh;
        }
        var prof = dsh["profile"]?.AsObject();
        if (prof is null)
        {
            prof = new JsonObject();
            dsh["profile"] = prof;
        }
        var arr = new JsonArray();
        foreach (var b in bundles)
            if (!string.IsNullOrWhiteSpace(b)) arr.Add(b.Trim());
        prof["bundles"] = arr;
        File.WriteAllText(pkg, node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n", new UTF8Encoding(false));
    }

    /// <summary>把 bundles 列表按 DSH 顺序去重（大小写不敏感）后再写。</summary>
    public static List<string> MergeBundles(IEnumerable<string> current, IEnumerable<string> add,
        IEnumerable<string>? remove = null)
    {
        var list = current.Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).ToList();
        if (remove is not null)
        {
            var drop = remove.Where(b => !string.IsNullOrWhiteSpace(b))
                .Select(b => b.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            list.RemoveAll(b => drop.Contains(b));
        }
        foreach (var b in add)
        {
            var name = (b ?? "").Trim();
            if (name.Length == 0) continue;
            if (!list.Any(x => Same(x, name))) list.Add(name);
        }
        return list;
    }

    /// <summary>这个实例是否已经有账本文件（旧实例 = false → UI 标「未登记」）。</summary>
    public bool Exists => File.Exists(FilePath);
}
