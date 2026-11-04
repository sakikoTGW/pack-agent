using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>导出参数。默认只导「声明 + patch + 数据」，不带第三方包体（source=registry）。</summary>
public sealed record PackExportOptions(
    string Name,
    string Version,
    string Description = "",
    string Author = "",
    string KernelMode = "pin",          // inherit | pin
    bool IncludeData = true,
    string? WorkspaceTemplate = null,   // 传目录 = workspace.mode=copy + 把它塞进 overrides/workspace/
    string? IconPath = null,
    IReadOnlyList<string>? BundledSpecs = null);   // 包体随包的第三方 bundle（按包名）

public sealed record PackExportResult(PackSpec Spec, string Path, int Files);

/// <summary>
/// 把实例导出成 PACK_SPEC v3 包：manifest（pack.json）+ overrides + integrity。
/// 只读实例现状，不改任何东西。
/// </summary>
public static class PackExport
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static PackExportResult Write(Launcher launcher, Instance inst, PackExportOptions opt, string dest)
    {
        var files = new List<(string ZipPath, string SourcePath)>();

        var spec = new PackSpec
        {
            Schema = PackSpec.SchemaV3,
            Name = opt.Name,
            Version = opt.Version,
            Description = opt.Description,
            Meta = new PackSpec.PackMeta
            {
                Author = opt.Author,
                ExportedAt = DateTimeOffset.Now.ToString("o"),
                Portable = true,
                Source = "instances/" + inst.Id,
            },
            Kernel = new PackSpec.PackKernel
            {
                Mode = opt.KernelMode == "inherit" ? "inherit" : "pin",
                Version = inst.Dsh.Version,
                OnMismatch = "refuse",
            },
            Runtime = new PackSpec.PackRuntime { Node = "inherit" },
            Home = new PackSpec.PackHome { Mode = "inherit" },
            Workspace = new PackSpec.PackWorkspace { Mode = "copy" },
            Requires = new PackSpec.PackRequires
            {
                Dsh = string.IsNullOrWhiteSpace(inst.Dsh.Version) ? "" : ">=" + inst.Dsh.Version,
            },
        };

        // profiles：bundles 按来源归类；patch 有就带上。
        foreach (var profile in launcher.Profiles(inst))
        {
            var patchRel = $"profiles/{profile.Name}/cordis.patch.yml";
            var patchSrc = System.IO.Path.Combine(profile.Dir, "cordis.patch.yml");
            var hasPatch = File.Exists(patchSrc);

            var bundled = new HashSet<string>(opt.BundledSpecs ?? [], StringComparer.OrdinalIgnoreCase);

            spec.Profiles.Add(new PackSpec.PackProfile
            {
                Name = profile.Name,
                Bundles = profile.Bundles.Select(spec2 => new PackSpec.PackBundle
                {
                    Spec = spec2,
                    Source = spec2.StartsWith("@deepseek-ai/", StringComparison.OrdinalIgnoreCase) ? "official"
                        : bundled.Contains(LaunchPolicy.BundleBareName(spec2)) ? "bundled"
                        : "registry",
                    Required = true,
                }).ToList(),
                Patch = hasPatch ? patchRel : "",
            });

            if (hasPatch) files.Add((patchRel, patchSrc));

            // 包体：把该 profile 的 pnpm store 一起带上（顶层 junction 由安装时 Relink 重建）。
            if (spec.Profiles[^1].Bundles.Any(b => b.Source == "bundled"))
            {
                var store = System.IO.Path.Combine(profile.Dir, "node_modules", ".pnpm");
                if (Directory.Exists(store)) AddTree(files, store, $"profiles/{profile.Name}/node_modules/.pnpm");
            }
        }

        // 官方 bundle 清单跟着内核走：从各 profile 收集（校验时 official 必须在这张表里）。
        spec.Kernel.Bundles = spec.Profiles
            .SelectMany(p => p.Bundles)
            .Where(b => b.Source == "official")
            .Select(b => b.Spec)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // data：workspace/.agent-pack
        if (opt.IncludeData)
        {
            var data = System.IO.Path.Combine(inst.Workspace.Path, ".agent-pack");
            if (Directory.Exists(data))
            {
                spec.Data.Add(new PackSpec.PackData { Path = ".agent-pack", Scope = "workspace" });
                AddTree(files, data, ".agent-pack");
            }
        }

        // workspace：给了模板目录才打成 copy；否则留空模板（copy 模式，不携带用户文件）。
        if (!string.IsNullOrWhiteSpace(opt.WorkspaceTemplate) && Directory.Exists(opt.WorkspaceTemplate))
        {
            spec.Workspace = new PackSpec.PackWorkspace { Mode = "copy", Template = "workspace/" };
            AddTree(files, opt.WorkspaceTemplate!, "workspace");
        }

        // 图标
        if (!string.IsNullOrWhiteSpace(opt.IconPath) && File.Exists(opt.IconPath))
        {
            spec.Icon = "icon.png";
            files.Add(("icon.png", opt.IconPath!));
        }

        // integrity：每个 overrides 文件的 sha256 + 汇总 root
        spec.Integrity = new PackSpec.PackIntegrity { Algorithm = "sha256", Check = "report" };
        foreach (var (zipPath, source) in files)
        {
            var info = new FileInfo(source);
            spec.Integrity.Files.Add(new PackSpec.PackFile
            {
                Path = zipPath,
                Sha256 = HashFile(source),
                Bytes = info.Length,
            });
        }
        spec.Integrity.Root = Root(spec.Integrity.Files);

        WriteZip(dest, spec, files);
        return new PackExportResult(spec, dest, files.Count);
    }

    static void AddTree(List<(string ZipPath, string SourcePath)> files, string root, string prefix)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
            files.Add((prefix.TrimEnd('/') + "/" + rel, path));
        }
    }

    static void WriteZip(string dest, PackSpec spec, List<(string ZipPath, string SourcePath)> files)
    {
        var dir = System.IO.Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(dest)) File.Delete(dest);

        using var zip = ZipFile.Open(dest, ZipArchiveMode.Create);
        var manifest = zip.CreateEntry("pack.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
            writer.Write(JsonSerializer.Serialize(spec, Json));

        foreach (var (zipPath, source) in files)
            zip.CreateEntryFromFile(source, zipPath, CompressionLevel.Optimal);
    }

    public static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>integrity.root：按 path 排序拼接 `path\0sha256\n` 取 sha256（不含 pack.json 自身）。</summary>
    public static string Root(IEnumerable<PackSpec.PackFile> files) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Concat(files
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .Select(f => f.Path + "\0" + f.Sha256 + "\n"))))).ToLowerInvariant();
}
