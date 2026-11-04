using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>导入 v3 包：overrides 落盘、profile bundle 合并、冲突三策略、篡改检测、账本。</summary>
public sealed class PackInstallTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-packinstall-" + Guid.NewGuid().ToString("N"));

    public PackInstallTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    (Launcher Launcher, string Pack, Instance Target) Scene()
    {
        var launcher = new Launcher(_root);

        var src = new Instance
        {
            Id = "src",
            Name = "Src",
            Home = Path.Combine(_root, "src-home"),
            Dsh = new InstanceDsh { Version = "0.2.1-rc.2" },
            Workspace = new InstanceWorkspace { Path = Path.Combine(_root, "src-ws"), Kind = "owned" },
        };
        var profile = Path.Combine(src.Home, "profiles", "web");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "package.json"), """
        { "name": "dsh-profile-web", "dsh": { "profile": { "bundles": [
            "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app", "dsh-better-sidebar" ] } } }
        """);
        File.WriteAllText(Path.Combine(profile, "cordis.patch.yml"), "- id: demo\n");
        Directory.CreateDirectory(Path.Combine(src.Workspace.Path, ".agent-pack", "skills"));
        File.WriteAllText(Path.Combine(src.Workspace.Path, ".agent-pack", "skills", "a.md"), "# a\n");

        var pack = Path.Combine(_root, "demo.pack.zip");
        PackExport.Write(launcher, src, new PackExportOptions("Demo", "1.0.0"), pack);

        var target = new Instance
        {
            Id = "dst",
            Name = "Dst",
            Home = Path.Combine(_root, "dst-home"),
            Dsh = new InstanceDsh { Version = "0.2.1-rc.2" },
            Workspace = new InstanceWorkspace { Path = Path.Combine(_root, "dst-ws"), Kind = "owned" },
        };
        Directory.CreateDirectory(target.Home);
        Directory.CreateDirectory(target.Workspace.Path);
        return (launcher, pack, target);
    }

    [Fact]
    public void Import_WritesOverridesAndMergesBundles()
    {
        var (launcher, pack, target) = Scene();

        var res = PackInstall.Install(launcher, target, pack, new PackImportOptions(InstallBundles: false));

        Assert.True(res.Ok, string.Join("; ", res.Messages));
        Assert.Equal("- id: demo\n", File.ReadAllText(Path.Combine(target.Home, "profiles", "web", "cordis.patch.yml")));
        Assert.Equal("# a\n", File.ReadAllText(Path.Combine(target.Workspace.Path, ".agent-pack", "skills", "a.md")));
        Assert.True(File.Exists(res.LedgerPath));

        var pkg = JsonNode.Parse(File.ReadAllText(Path.Combine(target.Home, "profiles", "web", "package.json")))!;
        var bundles = pkg["dsh"]!["profile"]!["bundles"]!.AsArray().Select(b => b!.GetValue<string>()).ToList();
        Assert.Contains("@deepseek-ai/dsh-base", bundles);
        Assert.Contains("@deepseek-ai/dsh-web-app", bundles);
        // registry 在 InstallBundles=false 时不写进清单，只记 pending。
        Assert.DoesNotContain("dsh-better-sidebar", bundles);
        Assert.Contains("web:dsh-better-sidebar", res.PendingBundles);
    }

    [Fact]
    public void Import_BundledPack_RelinksPnpmStore()
    {
        var (launcher, pack, target) = Scene();
        // 把包里那个 registry 插件改成 bundled 再重新导出一次。
        // （Scene 里 source 已经有 bundle 清单，这里直接建 store 并导出 bundled 版。）
        var srcPkg = Path.Combine(_root, "src-home", "profiles", "web", "node_modules", ".pnpm",
            "dsh-better-sidebar@0.28.0", "node_modules", "dsh-better-sidebar");
        Directory.CreateDirectory(srcPkg);
        File.WriteAllText(Path.Combine(srcPkg, "package.json"), """{"name":"dsh-better-sidebar","version":"0.28.0"}""");
        var src = new Instance
        {
            Id = "src", Name = "Src", Home = Path.Combine(_root, "src-home"),
            Dsh = new InstanceDsh { Version = "0.2.1-rc.2" },
            Workspace = new InstanceWorkspace { Path = Path.Combine(_root, "src-ws"), Kind = "owned" },
        };
        var bundledPack = Path.Combine(_root, "bundled.pack.zip");
        PackExport.Write(launcher, src,
            new PackExportOptions("Demo", "1.0.0", BundledSpecs: ["dsh-better-sidebar"]), bundledPack);

        var res = PackInstall.Install(launcher, target, bundledPack, new PackImportOptions(InstallBundles: false));

        Assert.True(res.Ok, string.Join("; ", res.Messages));
        Assert.Contains(res.Messages, m => m.StartsWith("relink web", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(target.Home, "profiles", "web", "node_modules", "dsh-better-sidebar")));
    }

    [Fact]
    public void Import_ConflictKeep_WritesPackNew()
    {
        var (launcher, pack, target) = Scene();
        PackInstall.Install(launcher, target, pack, new PackImportOptions(InstallBundles: false));

        var patch = Path.Combine(target.Home, "profiles", "web", "cordis.patch.yml");
        File.WriteAllText(patch, "- id: user\n");

        var res = PackInstall.Install(launcher, target, pack,
            new PackImportOptions("keep", InstallBundles: false));

        Assert.True(res.Ok, string.Join("; ", res.Messages));
        Assert.Equal("- id: user\n", File.ReadAllText(patch));
        Assert.Equal("- id: demo\n", File.ReadAllText(patch + ".pack-new"));
        Assert.True(res.Kept >= 1);
    }

    [Fact]
    public void Import_ConflictReplace_BacksUpUserFile()
    {
        var (launcher, pack, target) = Scene();
        PackInstall.Install(launcher, target, pack, new PackImportOptions(InstallBundles: false));

        var patch = Path.Combine(target.Home, "profiles", "web", "cordis.patch.yml");
        File.WriteAllText(patch, "- id: user\n");

        var res = PackInstall.Install(launcher, target, pack,
            new PackImportOptions("replace", InstallBundles: false));

        Assert.True(res.Ok, string.Join("; ", res.Messages));
        Assert.Equal("- id: demo\n", File.ReadAllText(patch));

        var backups = Directory.GetFiles(Path.Combine(launcher.InstanceDir(target.Id), ".pack-backup"),
            "cordis.patch.yml", SearchOption.AllDirectories);
        Assert.NotEmpty(backups);
        Assert.Contains(backups, b => File.ReadAllText(b) == "- id: user\n");
    }

    [Fact]
    public void Import_TamperedArchive_IsRejected()
    {
        var (launcher, pack, target) = Scene();

        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("profiles/web/cordis.patch.yml")!;
            entry.Delete();
            var replaced = zip.CreateEntry("profiles/web/cordis.patch.yml");
            using var writer = new StreamWriter(replaced.Open(), new UTF8Encoding(false));
            writer.Write("- id: tampered\n");
        }

        var res = PackInstall.Install(launcher, target, pack, new PackImportOptions(InstallBundles: false));

        Assert.False(res.Ok);
        Assert.Contains(res.Messages, m => m.Contains("sha256 mismatch"));
        Assert.False(File.Exists(Path.Combine(target.Home, "profiles", "web", "cordis.patch.yml")));
    }
}
