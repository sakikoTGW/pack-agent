using System.IO;
using System.IO.Compression;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>导出 v3 包：manifest 能过校验、overrides 与 integrity 对得上、zip 结构正确。</summary>
public sealed class PackExportTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-packexport-" + Guid.NewGuid().ToString("N"));

    public PackExportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    (Launcher Launcher, Instance Instance) Scene(bool withData = true)
    {
        var launcher = new Launcher(_root);
        var inst = new Instance
        {
            Id = "t",
            Name = "T",
            Home = Path.Combine(_root, "home"),
            Dsh = new InstanceDsh { Version = "0.2.1-rc.2" },
            Workspace = new InstanceWorkspace { Path = Path.Combine(_root, "ws"), Kind = "owned" },
        };

        var profile = Path.Combine(inst.Home, "profiles", "web");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "package.json"), """
        {
          "name": "dsh-profile-web",
          "dsh": { "profile": { "bundles": [
            "@deepseek-ai/dsh-base",
            "@deepseek-ai/dsh-web-app",
            "dsh-better-sidebar"
          ] } }
        }
        """);
        File.WriteAllText(Path.Combine(profile, "cordis.patch.yml"), "- id: demo\n");

        if (withData)
        {
            var skills = Path.Combine(inst.Workspace.Path, ".agent-pack", "skills");
            Directory.CreateDirectory(skills);
            File.WriteAllText(Path.Combine(skills, "a.md"), "# a\n");
        }

        return (launcher, inst);
    }

    [Fact]
    public void Export_ProducesValidV3Pack()
    {
        var (launcher, inst) = Scene();
        var dest = Path.Combine(_root, "out.pack.zip");

        var result = PackExport.Write(launcher, inst, new PackExportOptions("Demo", "1.2.3", "desc", "me"), dest);

        Assert.True(File.Exists(dest));
        var spec = PackSpec.Load(dest);
        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.Equal("Demo", spec.Name);
        Assert.Equal("1.2.3", spec.Version);
        Assert.Equal("pin", spec.Kernel.Mode);
        Assert.Equal("0.2.1-rc.2", spec.Kernel.Version);

        var profile = Assert.Single(spec.Profiles);
        Assert.Equal("web", profile.Name);
        Assert.Equal(2, profile.Bundles.Count(b => b.Source == "official"));
        Assert.Single(profile.Bundles, b => b.Source == "registry");
        Assert.Equal("profiles/web/cordis.patch.yml", profile.Patch);

        Assert.Contains(spec.Integrity.Files, f => f.Path == "profiles/web/cordis.patch.yml");
        Assert.Contains(spec.Integrity.Files, f => f.Path.StartsWith(".agent-pack/"));
        Assert.Equal(spec.Integrity.Root, PackExport.Root(spec.Integrity.Files));
        Assert.Equal(result.Files, spec.Integrity.Files.Count);

        using var zip = ZipFile.OpenRead(dest);
        Assert.NotNull(zip.GetEntry("pack.json"));
        Assert.NotNull(zip.GetEntry("profiles/web/cordis.patch.yml"));
    }

    [Fact]
    public void Export_CanSkipData()
    {
        var (launcher, inst) = Scene();
        var dest = Path.Combine(_root, "nodata.pack.zip");

        PackExport.Write(launcher, inst, new PackExportOptions("Demo", "1.0.0", IncludeData: false), dest);

        var spec = PackSpec.Load(dest);
        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.Empty(spec.Data);
        Assert.DoesNotContain(spec.Integrity.Files, f => f.Path.StartsWith(".agent-pack/"));
    }

    [Fact]
    public void Export_WithBundledSpecs_MarksSourceAndShipsPnpmStore()
    {
        var (launcher, inst) = Scene();
        var pkg = Path.Combine(inst.Home, "profiles", "web", "node_modules", ".pnpm",
            "dsh-better-sidebar@0.28.0", "node_modules", "dsh-better-sidebar");
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(pkg, "package.json"), """{"name":"dsh-better-sidebar","version":"0.28.0"}""");

        var dest = Path.Combine(_root, "bundled.pack.zip");
        PackExport.Write(launcher, inst,
            new PackExportOptions("Demo", "1.0.0", BundledSpecs: ["dsh-better-sidebar"]), dest);

        var spec = PackSpec.Load(dest);
        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        var bundle = Assert.Single(spec.Profiles[0].Bundles, b => b.Spec == "dsh-better-sidebar");
        Assert.Equal("bundled", bundle.Source);
        Assert.Contains(spec.Integrity.Files, f => f.Path.Contains(".pnpm/", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_WithWorkspaceTemplate_IsCopyMode()
    {
        var (launcher, inst) = Scene();
        var template = Path.Combine(_root, "template");
        Directory.CreateDirectory(Path.Combine(template, "docs"));
        File.WriteAllText(Path.Combine(template, "docs", "readme.md"), "hi\n");
        var dest = Path.Combine(_root, "tpl.pack.zip");

        PackExport.Write(launcher, inst, new PackExportOptions("Demo", "1.0.0", WorkspaceTemplate: template), dest);

        var spec = PackSpec.Load(dest);
        Assert.True(spec.Valid, string.Join("; ", spec.Errors));
        Assert.Equal("copy", spec.Workspace.Mode);
        Assert.Equal("workspace/", spec.Workspace.Template);
        Assert.Contains(spec.Integrity.Files, f => f.Path == "workspace/docs/readme.md");
    }
}
