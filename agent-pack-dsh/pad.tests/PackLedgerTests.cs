using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// P2 账本（D105 §3.2/§7）的纯数据/文件语义：schema roundtrip、原子写、owners 引用计数、
/// 独占/共享判定、读时对账（missing/version/disabled/absent）、旧实例无账本、
/// pinst 语义（账本独立于 instance.json，SaveInstance 不碰它）、profile bundle 重写。
/// 不跑 dsh/pnpm；所有 fixture 都在临时目录里。
/// </summary>
public sealed class PackLedgerTests
{
    // ---- fixture ---------------------------------------------------------

    sealed record Fixture(Launcher Launcher, Instance Instance)
    {
        public string Home => Instance.Home;
        public string Workspace => Instance.Workspace.Path;
        public string InstanceDir => Launcher.InstanceDir(Instance.Id);
        public string ProfileDir => Path.Combine(Home, "profiles", "web");
        public string LedgerPath => Path.Combine(InstanceDir, "pack-ledger.json");

        public string Projection(string rel = ".agent-pack/modpacks/pack-agent-modpack-test")
        {
            var dir = Path.Combine(Workspace, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            return rel;
        }
    }

    static Fixture Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-ledger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var launcher = new Launcher(root);
        var dir = launcher.InstanceDir("x");
        var home = Path.Combine(dir, "home");
        var workspace = Path.Combine(dir, "workspace");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.Combine(home, "profiles", "web"));
        var instance = new Instance
        {
            Id = "x",
            Name = "x",
            Home = home,
            Workspace = new InstanceWorkspace { Kind = "owned", Path = workspace },
            Dsh = new InstanceDsh { Version = "0.2.1-rc.2" },
        };
        return new Fixture(launcher, instance);
    }

    /// <summary>写 profile 的 package.json（deps 可以为空）。</summary>
    static void WriteProfile(Fixture f, Dictionary<string, string>? deps = null, string[]? bundles = null,
        string extraDsh = "")
    {
        var depsJson = deps is null
            ? "{}"
            : JsonSerializer.Serialize(deps);
        var bundlesJson = bundles is null ? "[]" : JsonSerializer.Serialize(bundles);
        var body = $$"""
            {
              "name": "dsh-profile-web",
              "private": true,
              "dependencies": {{depsJson}},
              "dsh": {
                "profile": {
                  "bundles": {{bundlesJson}}
                }{{extraDsh}}
              }
            }
            """;
        File.WriteAllText(Path.Combine(f.ProfileDir, "package.json"), body, new UTF8Encoding(false));
    }

    static void WriteModule(Fixture f, string depName, string version)
    {
        var dir = Path.Combine(f.ProfileDir, "node_modules",
            depName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"),
            JsonSerializer.Serialize(new { name = depName, version }), new UTF8Encoding(false));
    }

    static LedgerPlugin Plugin(string spec, string? addedVersion = null, string? localDir = null) => new()
    {
        Spec = spec,
        AddedVersion = addedVersion,
        Origin = "pack",
        LocalDir = localDir,
    };

    static string Sha(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    // ---- schema roundtrip / 原子写 ----------------------------------------

    [Fact]
    public void Load_MissingFile_IsEmptyAndDoesNotWriteAnything()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        Assert.Empty(ledger.Data.Packs);
        Assert.Empty(ledger.Data.Owners);
        Assert.Equal(PackLedger.SchemaId, ledger.Data.Schema);
        Assert.False(ledger.Exists);
        Assert.Null(ledger.LoadError);
        Assert.False(File.Exists(f.LedgerPath));
    }

    [Fact]
    public void Save_RoundTripsTheDocumentedSchema()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        var dir = f.Projection();
        ledger.RecordPack("Test-0.1.0", "Test", "0.1.0",
            @"library/packs/Test-0.1.0.pack.zip", dir, allow: true);
        ledger.RecordInstall("Test-0.1.0", "web", [
            Plugin("dsh-find-skill", "0.2.1"),
            // Q6：本地插件先复制到实例目录，spec 是 file: 绝对路径，localDir 是相对实例目录的稳定路径。
            Plugin(@"file:E:\pack-agent\.tmp\x\instances\x\home\local-plugins\Test-0.1.0\my-local-plugin",
                localDir: "home/local-plugins/Test-0.1.0/my-local-plugin"),
        ]);
        ledger.SetState("Test-0.1.0", "web", PackLedger.StateInstalled);
        ledger.Save();

        // 文件位置 + schema 字符串（外部读者按这个认版本）。
        Assert.True(File.Exists(f.LedgerPath));
        Assert.Equal(Path.Combine(f.InstanceDir, "pack-ledger.json"), ledger.FilePath);
        var raw = File.ReadAllText(f.LedgerPath);
        Assert.Contains("\"schema\": \"pack-agent.pack-ledger/v1\"", raw);
        Assert.Contains("\"updated\":", raw);
        Assert.Contains("\"installedVersion\": \"0.1.0\"", raw);
        Assert.Contains("\"localDir\": \"home/local-plugins/Test-0.1.0/my-local-plugin\"", raw);
        Assert.Contains("\"owners\":", raw);

        // 重新读：字段不丢，大小写不敏感也能查。
        var reloaded = PackLedger.Load(f.Launcher, f.Instance);
        Assert.Null(reloaded.LoadError);
        var pack = reloaded.Get("test-0.1.0");
        Assert.NotNull(pack);
        Assert.Equal("Test", pack!.Title);
        Assert.Equal("0.1.0", pack.InstalledVersion);
        Assert.Equal(@"library/packs/Test-0.1.0.pack.zip", pack.Path);
        Assert.True(pack.Content.Allow);
        Assert.Equal(dir, pack.Content.Dir);
        Assert.NotEqual("", pack.InstalledAt);
        var prof = pack.Profiles["web"];
        Assert.Equal(PackLedger.StateInstalled, prof.State);
        Assert.Equal(2, prof.Plugins.Count);
        Assert.Equal("dsh-find-skill", prof.Plugins[0].Spec);
        Assert.Equal("0.2.1", prof.Plugins[0].AddedVersion);
        Assert.Equal("pack", prof.Plugins[0].Origin);
        Assert.Null(prof.Plugins[0].LocalDir);
        Assert.Equal("home/local-plugins/Test-0.1.0/my-local-plugin", prof.Plugins[1].LocalDir);
        Assert.Equal(["Test-0.1.0"], reloaded.OwnersOf("web", "dsh-find-skill"));
        Assert.Equal(["Test-0.1.0"], reloaded.OwnersOf("web", "file:E:/whatever/my-local-plugin"));

        // 原子写：写完不残留 .tmp。
        Assert.Empty(Directory.GetFiles(f.InstanceDir, "*.tmp"));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyLedgerWithError()
    {
        var f = Temp();
        File.WriteAllText(f.LedgerPath, "{ not json", new UTF8Encoding(false));
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        Assert.Empty(ledger.Data.Packs);
        Assert.NotNull(ledger.LoadError);
    }

    [Fact]
    public void Load_WrongShapeJson_DoesNotThrow()
    {
        var f = Temp();
        File.WriteAllText(f.LedgerPath, "[1,2,3]", new UTF8Encoding(false));
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        Assert.Empty(ledger.Data.Packs);
        Assert.True(ledger.Exists);
    }

    [Fact]
    public void Save_IsAtomicAndSecondSaveOverwrites()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json");
        ledger.Save();
        var first = File.ReadAllText(f.LedgerPath);
        ledger.RecordPack("B-1.0", "B", "1.0", "b.pack.json");
        ledger.Save();
        var second = File.ReadAllText(f.LedgerPath);
        Assert.NotEqual(first, second);
        Assert.Single(PackLedger.Load(f.Launcher, f.Instance).Data.Packs, p => p.Key == "B-1.0");
        Assert.Equal(2, PackLedger.Load(f.Launcher, f.Instance).Data.Packs.Count);
        Assert.Empty(Directory.GetFiles(f.InstanceDir, "*.tmp"));
    }

    // ---- owners 引用计数 / 独占 vs 共享 -----------------------------------

    [Fact]
    public void Owners_RefCountSharedPlugin_ExclusiveVsShared()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json");
        ledger.RecordInstall("A-1.0", "web", [Plugin("shared-plugin"), Plugin("only-a")]);
        ledger.RecordPack("B-1.0", "B", "1.0", "b.pack.json");
        ledger.RecordInstall("B-1.0", "web", [Plugin("shared-plugin"), Plugin("only-b")]);

        Assert.Equal(["A-1.0", "B-1.0"], ledger.OwnersOf("web", "shared-plugin"));
        Assert.Equal(["A-1.0"], ledger.OwnersOf("web", "only-a"));
        Assert.Empty(ledger.OwnersOf("web", "nobody-asked"));
        Assert.Empty(ledger.OwnersOf("tui", "shared-plugin"));

        Assert.Equal(["only-a"], ledger.ExclusiveFor("web", "A-1.0"));
        Assert.Equal(["shared-plugin"], ledger.SharedFor("web", "A-1.0"));
        Assert.Equal(["shared-plugin"], ledger.SharedFor("web", "B-1.0"));
        Assert.Empty(ledger.ExclusiveFor("web", "missing-pack"));

        // 卸载 A：共享插件保留提示、独占插件可回滚。账本侧 RemovePack 摘掉 A 的引用。
        Assert.True(ledger.RemovePack("A-1.0"));
        Assert.Null(ledger.Get("A-1.0"));
        Assert.Equal(["B-1.0"], ledger.OwnersOf("web", "shared-plugin"));
        Assert.Empty(ledger.OwnersOf("web", "only-a"));
        Assert.False(ledger.RemovePack("A-1.0"));
    }

    [Fact]
    public void RecordPluginRemoved_UpdatesOwners_AndMarksIncomplete()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json");
        ledger.RecordInstall("A-1.0", "web", [Plugin("shared-plugin"), Plugin("only-a")]);
        ledger.RecordPack("B-1.0", "B", "1.0", "b.pack.json");
        ledger.RecordInstall("B-1.0", "web", [Plugin("shared-plugin")]);
        ledger.Save();

        Assert.True(ledger.RecordPluginRemoved("A-1.0", "web", "shared-plugin"));
        Assert.Equal(["B-1.0"], ledger.OwnersOf("web", "shared-plugin"));
        Assert.Equal(PackLedger.StateIncomplete, ledger.Get("A-1.0")!.Profiles["web"].State);
        Assert.Equal(["only-a"], ledger.Get("A-1.0")!.Profiles["web"].Plugins.Select(p => p.Spec));

        // 不在账本里的插件：不动 owners、返回 false。
        Assert.False(ledger.RecordPluginRemoved("A-1.0", "web", "never-recorded"));
        Assert.False(ledger.RecordPluginRemoved("no-such-pack", "web", "only-a"));
    }

    [Fact]
    public void RecordInstall_ReplacesThatPacksPluginSet_AndRebuildsOwners()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json");
        ledger.RecordInstall("A-1.0", "web", [Plugin("old-plugin"), Plugin("kept-plugin")]);
        ledger.RecordInstall("A-1.0", "web", [Plugin("kept-plugin")]);
        Assert.Empty(ledger.OwnersOf("web", "old-plugin"));
        Assert.Equal(["A-1.0"], ledger.OwnersOf("web", "kept-plugin"));
        Assert.Equal(["kept-plugin"], ledger.Get("A-1.0")!.Profiles["web"].Plugins.Select(p => p.Spec));
    }

    // ---- 读时对账：missing / version / disabled / absent ------------------

    [Fact]
    public void Gaps_ReportMissingDependencyAndMissingBundle()
    {
        var f = Temp();
        var dir = f.Projection();
        WriteProfile(f,
            new() { ["dsh-present"] = "^1.0.0" },
            bundles: []);
        WriteModule(f, "dsh-present", "1.0.0");

        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", dir);
        ledger.RecordInstall("Probe-0.1.0", "web", [
            Plugin("dsh-present", "1.0.0"),
            Plugin("dsh-absent"),
        ]);

        var gaps = ledger.GapsFor("web", "Probe-0.1.0");
        Assert.Contains(gaps, g => g.Kind == "bundle" && g.Spec == "dsh-present");
        Assert.Contains(gaps, g => g.Kind == "dependency" && g.Spec == "dsh-absent");
        Assert.Equal(["dsh-present", "dsh-absent"], ledger.MissingFor("web", "Probe-0.1.0"));
        Assert.Equal(PackLedger.StateIncomplete, ledger.EffectiveState("web", "Probe-0.1.0"));
    }

    [Fact]
    public void EffectiveState_InstalledWhenDepsAndBundlesMatch()
    {
        var f = Temp();
        var dir = f.Projection();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present"]);
        WriteModule(f, "dsh-present", "1.0.0");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", dir);
        ledger.RecordInstall("Probe-0.1.0", "web", [Plugin("dsh-present", "1.0.0")]);
        ledger.SetState("Probe-0.1.0", "web", PackLedger.StateInstalled);

        Assert.Empty(ledger.GapsFor("web", "Probe-0.1.0"));
        Assert.Empty(ledger.MissingFor("web", "Probe-0.1.0"));
        Assert.Equal(PackLedger.StateInstalled, ledger.EffectiveState("web", "Probe-0.1.0"));
        Assert.True(ledger.ProjectionExists("Probe-0.1.0"));
    }

    [Fact]
    public void EffectiveState_DisabledWhenBundlesRemovedButDepsKept()
    {
        // Q1 的停用形态：dependencies 保留、dsh.profile.bundles 摘掉、allow=false。
        var f = Temp();
        var dir = f.Projection();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: []);
        WriteModule(f, "dsh-present", "1.0.0");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", dir, allow: false);
        ledger.RecordInstall("Probe-0.1.0", "web", [Plugin("dsh-present", "1.0.0")]);
        ledger.SetState("Probe-0.1.0", "web", PackLedger.StateDisabled);

        Assert.Empty(ledger.MissingFor("web", "Probe-0.1.0"));
        Assert.Equal(PackLedger.StateDisabled, ledger.EffectiveState("web", "Probe-0.1.0"));
        Assert.DoesNotContain(ledger.GapsFor("web", "Probe-0.1.0"), g => g.Kind == "bundle");
    }

    [Fact]
    public void EffectiveState_IncompleteWhenDisabledPackLostItsDependency()
    {
        var f = Temp();
        var dir = f.Projection();
        WriteProfile(f, new() { }, bundles: []);
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", dir, allow: false);
        ledger.RecordInstall("Probe-0.1.0", "web", [Plugin("dsh-present", "1.0.0")]);
        ledger.SetState("Probe-0.1.0", "web", PackLedger.StateDisabled);

        Assert.Contains(ledger.GapsFor("web", "Probe-0.1.0"), g => g.Kind == "dependency");
        Assert.Equal(["dsh-present"], ledger.MissingFor("web", "Probe-0.1.0"));
        Assert.Equal(PackLedger.StateIncomplete, ledger.EffectiveState("web", "Probe-0.1.0"));
    }

    [Fact]
    public void EffectiveState_IncompleteWhenDisabledPackBundleRowCameBack()
    {
        var f = Temp();
        var dir = f.Projection();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present"]);
        WriteModule(f, "dsh-present", "1.0.0");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", dir, allow: false);
        ledger.RecordInstall("Probe-0.1.0", "web", [Plugin("dsh-present", "1.0.0")]);
        ledger.SetState("Probe-0.1.0", "web", PackLedger.StateDisabled);

        Assert.Contains(ledger.GapsFor("web", "Probe-0.1.0"), g => g.Kind == "bundle-extra");
        Assert.Equal(PackLedger.StateIncomplete, ledger.EffectiveState("web", "Probe-0.1.0"));
    }

    [Fact]
    public void VersionDrift_IsReportedAndMakesPackIncomplete()
    {
        var f = Temp();
        var dir = f.Projection();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present"]);
        WriteModule(f, "dsh-present", "2.0.0");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", dir);
        ledger.RecordInstall("Probe-0.1.0", "web", [Plugin("dsh-present", "1.0.0")]);

        var drift = Assert.Single(ledger.VersionConflicts("web", "Probe-0.1.0"));
        Assert.Equal("dsh-present", drift.Spec);
        Assert.Equal("1.0.0", drift.Expected);
        Assert.Equal("2.0.0", drift.Actual);
        Assert.Contains(ledger.GapsFor("web", "Probe-0.1.0"), g => g.Kind == "version");
        Assert.Equal(PackLedger.StateIncomplete, ledger.EffectiveState("web", "Probe-0.1.0"));
        // 版本偏差不算“缺失”：RepairPack 不碰它。
        Assert.Empty(ledger.MissingFor("web", "Probe-0.1.0"));
    }

    [Fact]
    public void EffectiveState_AbsentWhenProjectionIsGone()
    {
        var f = Temp();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present"]);
        WriteModule(f, "dsh-present", "1.0.0");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        // 不创建投影目录。
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json",
            ".agent-pack/modpacks/pack-agent-modpack-probe");
        ledger.RecordInstall("Probe-0.1.0", "web", [Plugin("dsh-present", "1.0.0")]);

        Assert.False(ledger.ProjectionExists("Probe-0.1.0"));
        Assert.Equal(PackLedger.StateAbsent, ledger.EffectiveState("web", "Probe-0.1.0"));
        Assert.Equal(PackLedger.StateAbsent, ledger.EffectiveState("web", "not-in-ledger"));
        // 另一个 profile 没记录 = 未安装。
        Assert.Equal(PackLedger.StateAbsent, ledger.EffectiveState("tui", "Probe-0.1.0"));
    }

    [Fact]
    public void LocalPlugin_MissingLocalDir_IsReportedAsGap()
    {
        var f = Temp();
        var dir = f.Projection();
        var localRel = "home/local-plugins/Probe-0.1.0/my-local-plugin";
        var localAbs = Path.Combine(f.InstanceDir, localRel.Replace('/', Path.DirectorySeparatorChar));
        var spec = "file:" + localAbs;
        WriteProfile(f, new() { ["my-local-plugin"] = spec }, bundles: ["my-local-plugin"]);
        WriteModule(f, "my-local-plugin", "0.1.0");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", dir);
        ledger.RecordInstall("Probe-0.1.0", "web",
            [Plugin(spec, "0.1.0", localRel)]);

        Assert.Contains(ledger.GapsFor("web", "Probe-0.1.0"), g => g.Kind == "local-dir");
        Assert.Equal(PackLedger.StateIncomplete, ledger.EffectiveState("web", "Probe-0.1.0"));

        Directory.CreateDirectory(localAbs);
        Assert.DoesNotContain(ledger.GapsFor("web", "Probe-0.1.0"), g => g.Kind == "local-dir");
        Assert.Equal(PackLedger.StateInstalled, ledger.EffectiveState("web", "Probe-0.1.0"));
    }

    // ---- 旧实例 / pinst 边界 / 与 instance.json 隔离 ----------------------

    [Fact]
    public void LegacyInstanceWithoutLedger_LoadsEmpty_ThenSaveCreatesIt()
    {
        var f = Temp();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present"]);
        WriteModule(f, "dsh-present", "1.0.0");

        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        Assert.Empty(ledger.Data.Packs);
        Assert.False(ledger.Exists);                       // UI 据此标「未登记」
        Assert.Equal(PackLedger.StateAbsent, ledger.EffectiveState("web", "anything"));

        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", f.Projection());
        ledger.Save();
        Assert.True(File.Exists(f.LedgerPath));
    }

    [Fact]
    public void SaveInstance_DoesNotTouchTheLedger_AndLedgerIsNotInInstanceJsonOrHome()
    {
        // Q4/pinst 语义：账本随实例走（独立文件），C# 的 SaveInstance 绝不写它；
        // pinst 的导出/导入是整实例搬运，账本原样带上，不由 C# 重算。
        var f = Temp();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present"]);
        WriteModule(f, "dsh-present", "1.0.0");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("Probe-0.1.0", "Probe", "0.1.0", "probe.pack.json", f.Projection());
        ledger.RecordInstall("Probe-0.1.0", "web", [Plugin("dsh-present", "1.0.0")]);
        ledger.Save();
        var ledgerSha = Sha(f.LedgerPath);

        // instance.json 里带 TS 才认识、C# 模型不认识的字段（现实里 SaveInstance 会抹掉它们，
        // 这正是账本必须独立文件的原因）。
        var instanceJson = Path.Combine(f.InstanceDir, "instance.json");
        File.WriteAllText(instanceJson, """
            {
              "schema": "pack-agent.launcher.instance/v1",
              "id": "x",
              "name": "x",
              "dsh": { "version": "0.2.1-rc.2" },
              "profile": { "name": "web" },
              "home": "ignored",
              "workspace": { "kind": "owned", "path": "ignored" },
              "packs": { "allowSet": "default" },
              "plugins": { "disabled": [] },
              "credentials": { "kind": "global" }
            }
            """, new UTF8Encoding(false));
        f.Launcher.SaveInstance(f.Instance);
        Assert.Equal(ledgerSha, Sha(f.LedgerPath));

        // 账本不在 instance.json 里，也不在 home 里（Q2：独立文件在 instances/<id>/ 下）。
        Assert.DoesNotContain("pack-ledger", File.ReadAllText(instanceJson));
        Assert.False(File.Exists(Path.Combine(f.Home, "pack-ledger.json")));
        Assert.Equal(Path.Combine(f.InstanceDir, "pack-ledger.json"), ledger.FilePath);
    }

    // ---- profile 读写工具（停用/启用联动用）-------------------------------

    [Fact]
    public void WriteBundles_OnlyRewritesTheBundleList_LeavingDependenciesAlone()
    {
        var f = Temp();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0", ["other"] = "file:C:\\x\\other" },
            bundles: ["dsh-present"], extraDsh: ",\n    \"other\": { \"keep\": true }");
        var pkg = Path.Combine(f.ProfileDir, "package.json");

        PackLedger.WriteBundles(f.Instance, "web", ["keep-me"]);

        using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
        var root = doc.RootElement;
        Assert.Equal("^1.0.0", root.GetProperty("dependencies").GetProperty("dsh-present").GetString());
        Assert.Equal("file:C:\\x\\other", root.GetProperty("dependencies").GetProperty("other").GetString());
        Assert.Equal("keep-me", Assert.Single(
            root.GetProperty("dsh").GetProperty("profile").GetProperty("bundles").EnumerateArray()).GetString());
        Assert.True(root.GetProperty("dsh").GetProperty("other").GetProperty("keep").GetBoolean());
        Assert.Equal("web", PackLedger.DefaultProfile(f.Launcher, f.Instance));
    }

    [Fact]
    public void ReadProfile_ReturnsRawBundlesAndDependencies()
    {
        var f = Temp();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present", "@scope/thing"]);
        var facts = PackLedger.ReadProfile(f.Instance, "web");
        Assert.True(facts.Exists);
        Assert.True(facts.HasBundleList);
        Assert.Equal(["dsh-present", "@scope/thing"], facts.Bundles);
        Assert.True(facts.HasDependency("DSH-PRESENT"));
        Assert.Null(facts.BundleName("nope"));
        Assert.Equal("dsh-present", facts.BundleName("DSH-PRESENT"));

        // profile 不存在 → 空快照，不抛。
        var missing = PackLedger.ReadProfile(f.Instance, "tui");
        Assert.False(missing.Exists);
        Assert.Empty(missing.Deps);
        Assert.Empty(missing.Bundles);
    }

    [Fact]
    public void WriteBundles_ThrowsWhenProfileDoesNotExist()
    {
        var f = Temp();
        Assert.Throws<FileNotFoundException>(() =>
            PackLedger.WriteBundles(f.Instance, "nope", ["x"]));
    }

    [Fact]
    public void MergeBundles_DedupesCaseInsensitivelyAndKeepsOrder()
    {
        var merged = PackLedger.MergeBundles(["a", "B", "c"], ["b", "D"], ["a"]);
        Assert.Equal(["B", "c", "D"], merged);
        Assert.Empty(PackLedger.MergeBundles([], []));
    }

    // ---- 小工具 -----------------------------------------------------------

    [Theory]
    [InlineData("dsh-context", "dsh-context")]
    [InlineData("dsh-context@^0.79.0", "dsh-context")]
    [InlineData("dsh-context@latest", "dsh-context")]
    [InlineData("@sakikotgw/pack-agent-dsh@0.4.3", "@sakikotgw/pack-agent-dsh")]
    [InlineData("@sakikotgw/pack-agent-dsh", "@sakikotgw/pack-agent-dsh")]
    [InlineData("file:E:/x/home/local-plugins/Test-0.1.0/my-local-plugin", "my-local-plugin")]
    [InlineData("link:C:\\x\\home\\my-plugin\\", "my-plugin")]
    [InlineData("", "")]
    public void PluginKey_NormalizesNpmAndFileSpecs(string spec, string expected) =>
        Assert.Equal(expected, PackLedger.PluginKey(spec));

    [Fact]
    public void SetState_RejectsUnknownStateAndUnknownPack()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json");
        Assert.Throws<ArgumentException>(() => ledger.SetState("A-1.0", "web", "broken"));
        Assert.Throws<ArgumentException>(() => ledger.SetState("nope", "web", PackLedger.StateInstalled));
        ledger.SetState("A-1.0", "WEB", PackLedger.StateDisabled);      // profile 名大小写不敏感
        Assert.Equal("disabled", ledger.Get("A-1.0")!.Profiles["web"].State);
    }

    [Fact]
    public void RecordPack_VersionChangeRefreshesInstalledAt()
    {
        var f = Temp();
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json");
        var first = ledger.Get("A-1.0")!.InstalledAt;
        Assert.NotEqual("", first);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json");
        Assert.Equal(first, ledger.Get("A-1.0")!.InstalledAt);
        Thread.Sleep(20);            // 时间戳是毫秒精度：跨版本刷新至少隔一毫秒才看得出差别
        ledger.RecordPack("A-1.0", "A", "2.0", "a.pack.json");
        Assert.NotEqual(first, ledger.Get("A-1.0")!.InstalledAt);
        Assert.Equal("2.0", ledger.Get("A-1.0")!.InstalledVersion);
    }

    [Fact]
    public void RecordInstall_ReadsInstalledVersionFromNodeModules()
    {
        var f = Temp();
        WriteProfile(f, new() { ["dsh-present"] = "^1.0.0" }, bundles: ["dsh-present"]);
        WriteModule(f, "dsh-present", "1.2.3");
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("A-1.0", "A", "1.0", "a.pack.json", f.Projection());
        ledger.RecordInstall("A-1.0", "web", [new LedgerPlugin { Spec = "dsh-present" }]);
        var plugin = Assert.Single(ledger.Get("A-1.0")!.Profiles["web"].Plugins);
        Assert.Equal("1.2.3", plugin.AddedVersion);
        Assert.Equal("1.2.3", PackLedger.InstalledVersion(f.Instance, "web", "dsh-present"));
        Assert.Null(PackLedger.InstalledVersion(f.Instance, "web", "not-installed"));
    }

    // ---- 包清单读取（C# 写账本用；zip/json 两种包）-----------------------

    const string PackJson = """
        {
          "schema": "ccui-pack/v0.2",
          "name": "Local Plugin Pack",
          "version": "0.3.0",
          "dsh": {
            "version": "0.2.1-rc.2",
            "profile": "web",
            "plugins": [
              { "spec": "dsh-context@^0.79.0", "required": true },
              { "name": "my-local-plugin", "local": "plugins/my-local-plugin", "required": false }
            ]
          }
        }
        """;

    [Fact]
    public void PackManifest_ReadsPackJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pad-ledger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "demo.pack.json");
        File.WriteAllText(path, PackJson, new UTF8Encoding(false));

        var manifest = PackManifest.TryRead(path);
        Assert.NotNull(manifest);
        Assert.Equal("Local Plugin Pack", manifest!.Name);
        Assert.Equal("0.3.0", manifest.Version);
        Assert.Equal("web", manifest.Profile);
        Assert.Equal(2, manifest.Plugins.Count);
        Assert.Equal("dsh-context@^0.79.0", manifest.Plugins[0].Spec);
        Assert.True(manifest.Plugins[0].Required);
        Assert.Equal("my-local-plugin", manifest.Plugins[1].EffectiveSpec);
        Assert.Equal("plugins/my-local-plugin", manifest.Plugins[1].Local);
        Assert.False(manifest.Plugins[1].Required);
    }

    [Fact]
    public void PackManifest_ReadsPackZipAndIgnoresGarbage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pad-ledger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "demo.pack.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("pack.json");
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(PackJson);
            zip.CreateEntry("plugins/my-local-plugin/index.js");
        }

        var manifest = PackManifest.TryRead(zipPath);
        Assert.NotNull(manifest);
        Assert.Equal("Local Plugin Pack", manifest!.Name);
        Assert.Equal(2, manifest.Plugins.Count);

        // 坏文件 / 不存在 / pinst 形状的 zip：不抛，返回 null（账本跳过、导入不受影响）。
        var bad = Path.Combine(dir, "bad.pack.zip");
        File.WriteAllText(bad, "not a zip", new UTF8Encoding(false));
        Assert.Null(PackManifest.TryRead(bad));
        Assert.Null(PackManifest.TryRead(Path.Combine(dir, "missing.pack.json")));
        var pinst = Path.Combine(dir, "x.pinst.zip");
        using (var zip = ZipFile.Open(pinst, ZipArchiveMode.Create))
            zip.CreateEntry("instance.json");
        Assert.Null(PackManifest.TryRead(pinst));
    }

    [Fact]
    public void PackIds_MatchTheTsPackCatalogRules()
    {
        Assert.Equal("Test-0.1.0", Packs.PackIdFromPath(@"E:\packs\Test-0.1.0.pack.zip"));
        Assert.Equal("Test-0.1.0", Packs.PackIdFromPath(@"E:\packs\Test-0.1.0.pack.json"));
        Assert.Equal("Test-0.1.0", Packs.PackIdFromPath(@"E:\packs\Test-0.1.0.pinst.zip"));
        Assert.Equal("plain", Packs.PackIdFromPath(@"E:\packs\plain.json"));
        // 与 TS compile.ts 的 npmNameForPack/slug 同规则。
        Assert.Equal("pack-agent-modpack-test", Packs.NpmNameForPack("Test"));
        Assert.Equal("pack-agent-modpack-local-plugin-pack", Packs.NpmNameForPack("Local Plugin Pack"));
        Assert.Equal("pack-agent-modpack-pack", Packs.NpmNameForPack(""));
    }
}
