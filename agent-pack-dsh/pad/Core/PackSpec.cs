using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// 整合包 manifest（docs/PACK_SPEC.md，schema <c>pack-agent.pack/v3</c>）。
///
/// 读取侧同时兼容旧的 <c>ccui-pack/v0.2</c>：按 §8 的映射表归一成 v3，
/// 这样 PAD 能在生成侧还没升级时先认新包、也能继续读旧包。
/// </summary>
public sealed class PackSpec
{
    public const string SchemaV3 = "pack-agent.pack/v3";
    public const string SchemaLegacy = "ccui-pack/v0.2";

    [JsonPropertyName("schema")] public string Schema { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";
    [JsonPropertyName("meta")] public PackMeta Meta { get; set; } = new();
    [JsonPropertyName("kernel")] public PackKernel Kernel { get; set; } = new();
    [JsonPropertyName("runtime")] public PackRuntime Runtime { get; set; } = new();
    [JsonPropertyName("home")] public PackHome Home { get; set; } = new();
    [JsonPropertyName("workspace")] public PackWorkspace Workspace { get; set; } = new();
    [JsonPropertyName("profiles")] public List<PackProfile> Profiles { get; set; } = [];
    [JsonPropertyName("agents")] public List<PackAgent> Agents { get; set; } = [];
    [JsonPropertyName("data")] public List<PackData> Data { get; set; } = [];
    [JsonPropertyName("requires")] public PackRequires Requires { get; set; } = new();
    [JsonPropertyName("integrity")] public PackIntegrity Integrity { get; set; } = new();

    /// <summary>读取时发现的错误；空 = 可用。</summary>
    [JsonIgnore] public List<string> Errors { get; } = [];

    [JsonIgnore] public bool Valid => Errors.Count == 0;

    [JsonIgnore] public bool IsLegacy => Schema == SchemaLegacy;

    /// <summary>读取时是不是旧 schema（归一后 <see cref="IsLegacy"/> 已为 false）。</summary>
    [JsonIgnore] public bool WasLegacy { get; private set; }

    public sealed class PackMeta
    {
        [JsonPropertyName("author")] public string Author { get; set; } = "";
        [JsonPropertyName("homepage")] public string Homepage { get; set; } = "";
        [JsonPropertyName("license")] public string License { get; set; } = "";
        [JsonPropertyName("exportedAt")] public string ExportedAt { get; set; } = "";
        [JsonPropertyName("portable")] public bool Portable { get; set; }
        [JsonPropertyName("fidelity")] public string Fidelity { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    }

    /// <summary>inherit = 用本机；pin = 装机时按版本装；bundled = 随包（单平台或 platforms 多平台）。</summary>
    public sealed class PackKernel
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "inherit";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("platform")] public string Platform { get; set; } = "";
        [JsonPropertyName("bundled")] public string Bundled { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("files")] public int Files { get; set; }
        [JsonPropertyName("platforms")] public Dictionary<string, PackPlatformKernel> Platforms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        [JsonPropertyName("bundles")] public List<string> Bundles { get; set; } = [];
        [JsonPropertyName("onMismatch")] public string OnMismatch { get; set; } = "refuse";
    }

    public sealed class PackPlatformKernel
    {
        [JsonPropertyName("bundled")] public string Bundled { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("files")] public int Files { get; set; }
    }

    public sealed class PackRuntime
    {
        [JsonPropertyName("node")] public string Node { get; set; } = "inherit";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("platform")] public string Platform { get; set; } = "";
        [JsonPropertyName("bundled")] public string Bundled { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    }

    public sealed class PackHome
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "inherit";
        [JsonPropertyName("snapshot")] public string Snapshot { get; set; } = "";
        [JsonPropertyName("include")] public List<string> Include { get; set; } = [];
        [JsonPropertyName("credentials")] public bool Credentials { get; set; }
    }

    public sealed class PackWorkspace
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "copy";
        [JsonPropertyName("template")] public string Template { get; set; } = "";
        [JsonPropertyName("path")] public string Path { get; set; } = "";
    }

    public sealed class PackProfile
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("bundles")] public List<PackBundle> Bundles { get; set; } = [];
        [JsonPropertyName("patch")] public string Patch { get; set; } = "";
        [JsonPropertyName("agents")] public List<string> Agents { get; set; } = [];
    }

    public sealed class PackBundle
    {
        [JsonPropertyName("spec")] public string Spec { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "official";
        [JsonPropertyName("required")] public bool Required { get; set; } = true;
    }

    public sealed class PackAgent
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "official";
        [JsonPropertyName("patch")] public string Patch { get; set; } = "";
        [JsonPropertyName("profiles")] public List<string> Profiles { get; set; } = [];
    }

    public sealed class PackData
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("scope")] public string Scope { get; set; } = "workspace";
        [JsonPropertyName("profiles")] public List<string> Profiles { get; set; } = [];
    }

    public sealed class PackRequires
    {
        [JsonPropertyName("dsh")] public string Dsh { get; set; } = "";
        [JsonPropertyName("node")] public string Node { get; set; } = "";
        [JsonPropertyName("os")] public List<string> Os { get; set; } = [];
        [JsonPropertyName("arch")] public List<string> Arch { get; set; } = [];
        [JsonPropertyName("platforms")] public List<string> Platforms { get; set; } = [];
    }

    public sealed class PackIntegrity
    {
        [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = "";
        [JsonPropertyName("check")] public string Check { get; set; } = "report";
        [JsonPropertyName("root")] public string Root { get; set; } = "";
        [JsonPropertyName("signature")] public string Signature { get; set; } = "";
        [JsonPropertyName("files")] public List<PackFile> Files { get; set; } = [];
    }

    public sealed class PackFile
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("bytes")] public long Bytes { get; set; }
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>从 <c>pack.json</c> 文本解析；旧 schema 自动归一成 v3。</summary>
    public static PackSpec Parse(string jsonText)
    {
        PackSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<PackSpec>(jsonText, Json);
        }
        catch (JsonException ex)
        {
            var bad = new PackSpec();
            bad.Errors.Add("invalid json: " + ex.Message);
            return bad;
        }

        if (spec is null)
        {
            var empty = new PackSpec();
            empty.Errors.Add("empty manifest");
            return empty;
        }

        if (spec.IsLegacy)
        {
            spec.WasLegacy = true;
            Legacy.Normalize(jsonText, spec);
        }
        spec.Validate();
        return spec;
    }

    /// <summary>读一个包：<c>.pack.json</c> 直接解析，<c>.pack.zip</c> 取包内 <c>pack.json</c>。</summary>
    public static PackSpec Load(string path)
    {
        try
        {
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry("pack.json")
                    ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/pack.json", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    var missing = new PackSpec();
                    missing.Errors.Add("pack.json not found in archive");
                    return missing;
                }

                using var reader = new StreamReader(entry.Open());
                return Parse(reader.ReadToEnd());
            }

            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            var bad = new PackSpec();
            bad.Errors.Add("cannot read pack: " + ex.Message);
            return bad;
        }
    }

    static readonly string[] PlatformNames =
        ["win32-x64", "win32-arm64", "darwin-x64", "darwin-arm64", "linux-x64", "linux-arm64"];

    void Validate()
    {
        if (Schema.Length == 0) Errors.Add("schema is required");
        else if (Schema != SchemaV3 && Schema != SchemaLegacy)
            Errors.Add($"unsupported schema `{Schema}` (want {SchemaV3})");
        if (Name.Length == 0) Errors.Add("name is required");
        if (Version.Length == 0) Errors.Add("version is required");
        if (Profiles.Count == 0) Errors.Add("at least one profile is required");

        ValidateKernel();
        ValidateRuntime();
        ValidateHome();
        ValidateWorkspace();

        var official = new HashSet<string>(Kernel.Bundles, StringComparer.OrdinalIgnoreCase);
        foreach (var p in Profiles)
        {
            if (p.Name.Length == 0) Errors.Add("profile.name is required");
            foreach (var b in p.Bundles)
            {
                if (b.Spec.Length == 0) { Errors.Add("bundle.spec is required"); continue; }
                if (b.Source is not ("official" or "bundled" or "registry"))
                    Errors.Add($"bundle `{b.Spec}` has invalid source `{b.Source}`");
                // 官方 bundle 必须出现在 kernel.bundles 里；内核没声明清单时跳过（无法判断）。
                if (b.Source == "official" && official.Count > 0 && !official.Contains(BundleName(b.Spec)))
                    Errors.Add($"official bundle `{b.Spec}` is not in kernel.bundles");
            }
            if (p.Patch.Length > 0 && !SafeRelative(p.Patch)) Errors.Add($"unsafe path `{p.Patch}`");
        }

        foreach (var a in Agents)
        {
            if (a.Id.Length == 0) Errors.Add("agent.id is required");
            if (a.Source is not ("official" or "bundled"))
                Errors.Add($"agent `{a.Id}` has invalid source `{a.Source}`");
            if (a.Source == "bundled" && a.Patch.Length == 0)
                Errors.Add($"agent `{a.Id}` is bundled but has no patch");
            if (a.Patch.Length > 0 && !SafeRelative(a.Patch)) Errors.Add($"unsafe path `{a.Patch}`");
        }

        foreach (var d in Data)
        {
            if (d.Path.Length == 0) { Errors.Add("data.path is required"); continue; }
            if (d.Scope is not ("workspace" or "home")) Errors.Add($"data `{d.Path}` has invalid scope `{d.Scope}`");
            if (!SafeRelative(d.Path)) Errors.Add($"unsafe path `{d.Path}`");
        }

        ValidateIntegrity();
    }

    void ValidateKernel()
    {
        if (Kernel.Mode is not ("inherit" or "pin" or "bundled"))
        {
            Errors.Add($"kernel.mode `{Kernel.Mode}` is invalid");
            return;
        }

        if (Kernel.Mode == "pin" && Kernel.Version.Length == 0)
            Errors.Add("kernel.version is required when kernel.mode=pin");

        if (Kernel.Mode != "bundled") return;

        if (Kernel.Version.Length == 0) Errors.Add("kernel.version is required when kernel.mode=bundled");
        if (Kernel.Platforms.Count > 0)
        {
            foreach (var (name, entry) in Kernel.Platforms)
            {
                if (!PlatformNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    Errors.Add($"kernel.platforms has unknown platform `{name}`");
                if (entry.Bundled.Length == 0 || !SafeRelative(entry.Bundled)) Errors.Add($"kernel.platforms[{name}].bundled is missing or unsafe");
                if (!IsSha256(entry.Sha256)) Errors.Add($"kernel.platforms[{name}].sha256 must be 64 hex chars");
            }
        }
        else
        {
            if (Kernel.Bundled.Length == 0) Errors.Add("kernel.bundled is required when kernel.mode=bundled");
            else if (!SafeRelative(Kernel.Bundled)) Errors.Add($"unsafe path `{Kernel.Bundled}`");
            if (!IsSha256(Kernel.Sha256)) Errors.Add("kernel.sha256 must be 64 hex chars when kernel.mode=bundled");
            if (Kernel.Platform.Length == 0) Errors.Add("kernel.platform is required for a single-platform bundled kernel");
            else if (!PlatformNames.Contains(Kernel.Platform, StringComparer.OrdinalIgnoreCase))
                Errors.Add($"kernel.platform `{Kernel.Platform}` is unknown");
        }

        if (Kernel.OnMismatch is not ("refuse" or "pin" or "any"))
            Errors.Add($"kernel.onMismatch `{Kernel.OnMismatch}` is invalid");
    }

    void ValidateRuntime()
    {
        if (Runtime.Node is not ("inherit" or "bundled"))
        {
            Errors.Add($"runtime.node `{Runtime.Node}` is invalid");
            return;
        }
        if (Runtime.Node != "bundled") return;
        if (Runtime.Version.Length == 0) Errors.Add("runtime.version is required when runtime.node=bundled");
        if (Runtime.Bundled.Length == 0 || !SafeRelative(Runtime.Bundled)) Errors.Add("runtime.bundled is missing or unsafe");
        if (!IsSha256(Runtime.Sha256)) Errors.Add("runtime.sha256 must be 64 hex chars");
        if (Runtime.Platform.Length > 0 && !PlatformNames.Contains(Runtime.Platform, StringComparer.OrdinalIgnoreCase))
            Errors.Add($"runtime.platform `{Runtime.Platform}` is unknown");
    }

    void ValidateHome()
    {
        if (Home.Mode is not ("inherit" or "snapshot"))
        {
            Errors.Add($"home.mode `{Home.Mode}` is invalid");
            return;
        }
        if (Home.Mode != "snapshot") return;
        if (Home.Snapshot.Length == 0 || !SafeRelative(Home.Snapshot))
            Errors.Add("home.snapshot is required (safe relative path) when home.mode=snapshot");
        foreach (var part in Home.Include)
            if (part is not ("storages" or "skills" or "sessions" or "cache"))
                Errors.Add($"home.include `{part}` is invalid");
    }

    void ValidateWorkspace()
    {
        if (Workspace.Mode is not ("bundled" or "copy" or "user"))
        {
            Errors.Add($"workspace.mode `{Workspace.Mode}` is invalid");
            return;
        }
        if (Workspace.Mode == "bundled" &&
            (Workspace.Template.Length == 0 || !SafeRelative(Workspace.Template)))
            Errors.Add("workspace.template is required (safe relative path) when workspace.mode=bundled");
        if (Workspace.Mode == "user")
        {
            if (Workspace.Path.Length == 0) Errors.Add("workspace.path is required when workspace.mode=user");
            else if (!Workspace.Path.Contains(':') && !Workspace.Path.StartsWith('\\') && !Workspace.Path.StartsWith('/'))
                Errors.Add("workspace.path must be an absolute path");
        }
    }

    void ValidateIntegrity()
    {
        if (Integrity.Algorithm is not ("sha256" or ""))
        {
            Errors.Add($"integrity.algorithm `{Integrity.Algorithm}` is invalid");
            return;
        }
        if (Integrity.Algorithm.Length == 0)
        {
            // 旧包没有完整性清单：导入时会按包内文件重算，所以这里不判错。
            if (!WasLegacy) Errors.Add("integrity is required (algorithm/root/files)");
            return;
        }
        if (!IsSha256(Integrity.Root))
            Errors.Add("integrity.root must be 64 hex chars");
        if (Integrity.Check is not ("off" or "report" or "strict"))
            Errors.Add($"integrity.check `{Integrity.Check}` is invalid");
        foreach (var f in Integrity.Files)
        {
            if (!SafeRelative(f.Path)) Errors.Add($"unsafe integrity path `{f.Path}`");
            if (!IsSha256(f.Sha256)) Errors.Add($"integrity file `{f.Path}` has invalid sha256");
        }
    }

    /// <summary>`@scope/pkg@1.2.3` → `@scope/pkg`。</summary>
    internal static string BundleName(string spec)
    {
        var at = spec.LastIndexOf('@');
        return at > 0 ? spec[..at] : spec;
    }

    static bool IsSha256(string value) =>
        value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    static bool SafeRelative(string rel)
    {
        var p = rel.Replace('\\', '/').Trim();
        if (p.Length == 0 || p.StartsWith('/') || Path.IsPathRooted(p)) return false;
        return !p.Split('/').Any(seg => seg == "..");
    }
}

/// <summary>旧 <c>ccui-pack/v0.2</c> → v3 的归一（PACK_SPEC §8）。</summary>
static class Legacy
{
    sealed class LegacyDoc
    {
        [JsonPropertyName("dsh")] public LegacyDsh? Dsh { get; set; }
        [JsonPropertyName("bundle")] public LegacyBundle? Bundle { get; set; }
        [JsonPropertyName("meta")] public PackSpec.PackMeta? Meta { get; set; }
        [JsonPropertyName("schema")] public string Schema { get; set; } = "";
    }

    sealed class LegacyDsh
    {
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("profile")] public string Profile { get; set; } = "";
        [JsonPropertyName("plugins")] public List<LegacyPlugin> Plugins { get; set; } = [];
    }

    sealed class LegacyPlugin
    {
        [JsonPropertyName("spec")] public string Spec { get; set; } = "";
        [JsonPropertyName("required")] public bool Required { get; set; } = true;
    }

    sealed class LegacyBundle
    {
        [JsonPropertyName("portable")] public bool Portable { get; set; }
    }

    public static void Normalize(string jsonText, PackSpec spec)
    {
        LegacyDoc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<LegacyDoc>(jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return; }
        if (doc is null) return;

        var dsh = doc.Dsh;
        spec.Schema = PackSpec.SchemaV3;
        spec.Kernel = new PackSpec.PackKernel
        {
            Mode = dsh is { Version.Length: > 0 } ? "pin" : "inherit",
            Version = dsh?.Version ?? "",
            OnMismatch = "refuse",
        };

        var profileName = dsh is { Profile.Length: > 0 } ? dsh.Profile : "web";
        var profile = new PackSpec.PackProfile { Name = profileName };
        foreach (var plugin in dsh?.Plugins ?? [])
        {
            if (plugin.Spec.Length == 0) continue;
            profile.Bundles.Add(new PackSpec.PackBundle
            {
                Spec = plugin.Spec,
                // 旧包不区分来源：官方包名用 official，其余从 npm 解析。
                Source = plugin.Spec.StartsWith("@deepseek-ai/", StringComparison.OrdinalIgnoreCase)
                    ? "official" : "registry",
                Required = plugin.Required,
            });
        }

        spec.Profiles = [profile];
        spec.Meta = doc.Meta ?? spec.Meta;
        if (doc.Bundle is not null) spec.Meta.Portable = doc.Bundle.Portable;

        // 旧包没有完整性清单：归一后留空，由调用方决定是否重算（导入时会补齐）。
        spec.Integrity = new PackSpec.PackIntegrity { Algorithm = "", Check = "report" };
    }
}
