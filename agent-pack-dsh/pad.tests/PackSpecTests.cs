using System.IO;
using System.IO.Compression;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// 整合包 manifest：v3 读取 + 旧 ccui-pack/v0.2 归一 + 校验（PACK_SPEC.md）。
/// </summary>
public sealed class PackSpecTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-packspec-" + Guid.NewGuid().ToString("N"));

    const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public PackSpecTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    static readonly string V3 = $$"""
    {
      // 注释和尾逗号都允许
      "schema": "pack-agent.pack/v3",
      "name": "Demo",
      "version": "1.0.0",
      "kernel": { "mode": "pin", "version": "0.2.1-rc.2" },
      "workspace": { "mode": "copy", "template": "workspace" },
      "profiles": [
        {
          "name": "web",
          "bundles": [
            { "spec": "@deepseek-ai/dsh-web-app", "source": "official" },
            { "spec": "@deepseek-harness-tui/dsh-tui@0.18.0", "source": "bundled" },
            { "spec": "dsh-better-sidebar", "source": "registry" }
          ],
          "patch": "profiles/web/cordis.patch.yml"
        }
      ],
      "agents": [ { "id": "standard", "source": "official" } ],
      "requires": { "dsh": ">=0.2.1-rc.2 <0.3.0" },
      "integrity": { "algorithm": "sha256", "root": "{{Sha}}", "files": [] },
    }
    """;

    [Fact]
    public void Parse_V3_IsValid()
    {
        var spec = PackSpec.Parse(V3);

        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.Equal(PackSpec.SchemaV3, spec.Schema);
        Assert.False(spec.WasLegacy);
        Assert.Equal("pin", spec.Kernel.Mode);
        Assert.Equal("copy", spec.Workspace.Mode);
        Assert.Single(spec.Profiles);
        Assert.Equal(3, spec.Profiles[0].Bundles.Count);
        Assert.Contains("standard", spec.Agents.Select(a => a.Id));
    }

    [Fact]
    public void Parse_Legacy_MapsToV3()
    {
        const string legacy = """
        {
          "schema": "ccui-pack/v0.2",
          "name": "Test",
          "version": "0.1.0",
          "dsh": {
            "version": "0.2.1-rc.2",
            "profile": "web",
            "plugins": [
              { "spec": "dsh-find-skill", "required": true },
              { "spec": "@deepseek-ai/dsh-web-app", "required": true }
            ]
          },
          "bundle": { "files": [], "portable": true },
          "meta": { "author": "PAD", "fidelity": "L1" }
        }
        """;

        var spec = PackSpec.Parse(legacy);

        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.True(spec.WasLegacy);
        Assert.Equal(PackSpec.SchemaV3, spec.Schema);
        Assert.Equal("pin", spec.Kernel.Mode);
        Assert.Equal("0.2.1-rc.2", spec.Kernel.Version);
        Assert.Equal("web", spec.Profiles[0].Name);
        Assert.Equal("registry", spec.Profiles[0].Bundles.First(b => b.Spec == "dsh-find-skill").Source);
        Assert.Equal("official", spec.Profiles[0].Bundles.First(b => b.Spec == "@deepseek-ai/dsh-web-app").Source);
        Assert.True(spec.Meta.Portable);
        Assert.Equal("PAD", spec.Meta.Author);
    }

    [Theory]
    [InlineData("\"name\": \"\"", "name is required")]
    [InlineData("\"version\": \"\"", "version is required")]
    public void Validate_RequiredFields(string replacement, string expected)
    {
        var json = V3
            .Replace("\"name\": \"Demo\"", replacement.Contains("name") ? replacement : "\"name\": \"Demo\"")
            .Replace("\"version\": \"1.0.0\"", replacement.Contains("version") ? replacement : "\"version\": \"1.0.0\"");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains(expected));
    }

    [Fact]
    public void Validate_BundledKernelNeedsSha256()
    {
        var json = V3.Replace(
            "{ \"mode\": \"pin\", \"version\": \"0.2.1-rc.2\" }",
            "{ \"mode\": \"bundled\", \"bundled\": \"kernel/\" }");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains("sha256"));
    }

    [Fact]
    public void Validate_BadBundleSource()
    {
        var json = V3.Replace("\"source\": \"registry\"", "\"source\": \"floppy\"");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains("invalid source"));
    }

    [Fact]
    public void Validate_RejectsPathTraversal()
    {
        var json = V3.Replace("\"profiles/web/cordis.patch.yml\"", "\"../escape/cordis.patch.yml\"");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains("unsafe path"));
    }

    [Fact]
    public void Validate_OfficialBundleMustBeInKernelList()
    {
        var json = V3.Replace(
            "{ \"mode\": \"pin\", \"version\": \"0.2.1-rc.2\" }",
            "{ \"mode\": \"pin\", \"version\": \"0.2.1-rc.2\", \"bundles\": [\"@deepseek-ai/dsh-base\"] }");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains("not in kernel.bundles"));
    }

    [Fact]
    public void Validate_BundledAgentNeedsPatch()
    {
        var json = V3.Replace(
            "\"agents\": [ { \"id\": \"standard\", \"source\": \"official\" } ]",
            "\"agents\": [ { \"id\": \"reviewer\", \"source\": \"bundled\" } ]");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains("no patch"));
    }

    [Fact]
    public void Validate_MultiPlatformKernel()
    {
        var json = V3.Replace(
            "{ \"mode\": \"pin\", \"version\": \"0.2.1-rc.2\" }",
            $$"""
            { "mode": "bundled", "version": "0.2.1-rc.2", "platforms": {
                "win32-x64": { "bundled": "kernel/win32-x64/", "sha256": "{{Sha}}" },
                "darwin-arm64": { "bundled": "kernel/darwin-arm64/", "sha256": "{{Sha}}" } } }
            """);
        var spec = PackSpec.Parse(json);
        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.Equal(2, spec.Kernel.Platforms.Count);
    }

    [Fact]
    public void Validate_UnknownPlatformIsRejected()
    {
        var json = V3.Replace(
            "{ \"mode\": \"pin\", \"version\": \"0.2.1-rc.2\" }",
            $$"""{ "mode": "bundled", "version": "0.2.1-rc.2", "platforms": { "beos-x86": { "bundled": "k/", "sha256": "{{Sha}}" } } }""");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains("unknown platform"));
    }

    [Fact]
    public void Validate_UnsignedIntegrityPathIsRejected()
    {
        var json = V3.Replace("\"files\": []",
            "\"files\": [ { \"path\": \"../evil.txt\", \"sha256\": \"" + Sha + "\" } ]");
        var spec = PackSpec.Parse(json);
        Assert.Contains(spec.Errors, e => e.Contains("unsafe integrity path"));
    }

    [Fact]
    public void Load_ReadsPackJsonFromZip()
    {
        var zipPath = Path.Combine(_root, "demo.pack.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("pack.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(V3);
        }

        var spec = PackSpec.Load(zipPath);
        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.Equal("Demo", spec.Name);
    }

    [Fact]
    public void RepoExamplePack_IsValid()
    {
        // pad.tests/bin/Debug/net9.0-windows → 仓库的 docs/schemas/example.pack.json
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var path = Path.Combine(root, "docs", "schemas", "example.pack.json");
        Assert.True(File.Exists(path), "example pack not found: " + path);

        var spec = PackSpec.Load(path);
        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.Equal(2, spec.Profiles.Count);
        Assert.True(spec.Integrity.Files.Count > 0);
    }
}
