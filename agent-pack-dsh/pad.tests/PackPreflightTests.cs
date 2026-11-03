using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// 安装预检 / 一次授权安装（D107）的纯逻辑测试：注入假 npm 元数据源与 apply/allow 钩子，
/// **绝不打网络、绝不跑 dsh/pnpm**。覆盖：全 ok、peers/engines 需授权、404 缺失、
/// 网络 unknown、版本冲突、本地插件、内核不匹配、管理器、DryRun 不落盘、
/// AcceptRisk→Exempted、SkipMissing→Skipped、无 flag→needs-decision。
/// </summary>
public sealed class PackPreflightTests
{
    // ---- fixtures ---------------------------------------------------------

    sealed record Fixture(Launcher Launcher, Instance Instance, string Root)
    {
        public string InstanceDir => Launcher.InstanceDir(Instance.Id);
        public string ProfileDir => Path.Combine(Instance.Home, "profiles", "web");
        public string LedgerPath => Path.Combine(InstanceDir, "pack-ledger.json");
        public string Workspace => Instance.Workspace.Path;
    }

    static Fixture Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-preflight-tests", Guid.NewGuid().ToString("N"));
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
        return new Fixture(launcher, instance, root);
    }

    /// <summary>写 profile 的 package.json（deps / bundles 都可空）。</summary>
    static void WriteProfile(Fixture f, Dictionary<string, string>? deps = null, string[]? bundles = null)
    {
        var body = new
        {
            name = "dsh-profile-web",
            @private = true,
            dependencies = deps ?? [],
            dsh = new { profile = new { bundles = bundles ?? [] } },
        };
        File.WriteAllText(Path.Combine(f.ProfileDir, "package.json"),
            JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    static void WriteModule(Fixture f, string name, string version)
    {
        var dir = Path.Combine(f.ProfileDir, "node_modules", name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"),
            JsonSerializer.Serialize(new { name, version }), new UTF8Encoding(false));
    }

    /// <summary>写一个 .pack.json（可选 sibling plugins/ 目录给本地插件用）。</summary>
    static string WritePack(Fixture f, string folder, string name, string version, string kernel,
        params object[] plugins)
    {
        var dir = Path.Combine(f.Root, folder);
        Directory.CreateDirectory(dir);
        var body = new
        {
            schema = "ccui-pack/v0.2",
            name,
            version,
            dsh = new
            {
                version = kernel,
                profile = "web",
                plugins,
            },
        };
        // 文件名 stem 就是 catalog row id（真实包叫 Test-0.1.0.pack.json）。
        var path = Path.Combine(dir, $"{folder}.pack.json");
        File.WriteAllText(path, JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return path;
    }

    /// <summary>一个只声明 spec 的插件行。</summary>
    static object Plugin(string spec, bool required = true) => new { spec, required };

    sealed class FakeMeta : INpmMetaSource
    {
        public Dictionary<string, NpmPackageMeta> Packages { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> Calls { get; } = [];

        public FakeMeta With(string name, string version, string? engines = null,
            params (string Name, string Range)[] peers)
        {
            Packages[name] = new NpmPackageMeta
            {
                Name = name,
                Version = version,
                EnginesDsh = engines,
                PeerDependencies = peers.ToDictionary(p => p.Name, p => p.Range,
                    StringComparer.OrdinalIgnoreCase),
            };
            return this;
        }

        public FakeMeta Unknown(string name)
        {
            Packages[name] = NpmPackageMeta.Unknown(name);
            return this;
        }

        public Task<NpmPackageMeta> Get(string packageName, string? version, CancellationToken ct)
        {
            Calls.Add(version is { Length: > 0 } ? $"{packageName}@{version}" : packageName);
            // 没登记的包 = registry 404（和假 registry 的默认行为一致）。
            return Task.FromResult(Packages.TryGetValue(packageName, out var meta)
                ? meta
                : NpmPackageMeta.Missing(packageName));
        }
    }

    // ---- preflight --------------------------------------------------------

    [Fact]
    public async Task Preflight_AllOk_CanInstall()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("pkg-a", "1.2.0").With("pkg-b", "0.9.0");
        var pack = WritePack(f, "demo", "Demo Pack", "0.2.0", "0.2.1-rc.2",
            Plugin("pkg-a"), Plugin("pkg-b@^0.9.0"));
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        Assert.Equal("demo", preflight.PackId);
        Assert.Equal("Demo Pack", preflight.Title);
        Assert.Equal("0.2.0", preflight.Version);
        Assert.True(preflight.KernelMatches);
        Assert.False(preflight.ManagerInstalled);
        Assert.False(preflight.AlreadyInstalled);
        Assert.Equal(2, preflight.Plugins.Count);
        Assert.All(preflight.Plugins, p => Assert.Equal(PackPluginStatus.Ok, p.Status));
        Assert.Equal(2, preflight.OkCount);
        Assert.Equal(0, preflight.ExemptionCount);
        Assert.False(preflight.NeedsDecision);
        Assert.True(preflight.CanInstall);
        Assert.Contains("2 可装", preflight.Summary);
        Assert.Contains(preflight.Warnings, w => w.Contains(Packs.ManagerSpec));
        // 裸名查 latest；range 也查 latest（预检评估“会装到的最新版”）。
        Assert.Contains("pkg-a", meta.Calls);
        Assert.Contains("pkg-b", meta.Calls);
    }

    [Fact]
    public async Task Preflight_PeerRangesUnmet_NeedsExemption()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("dsh-find-skill", "0.2.1",
            peers: [
                ("@deepseek-ai/dsh-commands", "^0.1.0-rc.6"),
                ("@deepseek-ai/dsh-tools", "^0.1.0-rc.6"),
                ("react", "^18.0.0"),                       // 非 dsh peer 不参与判定
            ]);
        var pack = WritePack(f, "legacy", "Legacy", "0.1.0", "0.2.1-rc.2", Plugin("dsh-find-skill"));
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        var row = Assert.Single(preflight.Plugins);
        Assert.Equal(PackPluginStatus.NeedsExemption, row.Status);
        Assert.Equal("0.2.1", row.LatestVersion);
        Assert.Contains("@deepseek-ai/dsh-commands ^0.1.0-rc.6", row.Requirement);
        Assert.Contains("@deepseek-ai/dsh-tools ^0.1.0-rc.6", row.Requirement);
        Assert.DoesNotContain("react", row.Requirement);
        Assert.Equal(1, preflight.ExemptionCount);
        Assert.True(preflight.NeedsDecision);
        Assert.False(preflight.CanInstall);
    }

    [Fact]
    public async Task Preflight_EnginesDshDecidesAlone()
    {
        var f = Temp();
        WriteProfile(f);
        // engines.dsh 不满足 → 只看它，peers 不再参与。
        var meta = new FakeMeta().With("pkg-risk", "2.0.0", engines: "^0.1.0",
            peers: [("@deepseek-ai/dsh-tools", ">=0.2.0")]);
        var pack = WritePack(f, "risk", "Risk", "1.0.0", "0.2.1-rc.2", Plugin("pkg-risk"));
        var packs = new Packs(f.Launcher, meta);

        var row = Assert.Single(
            (await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None)).Plugins);
        Assert.Equal(PackPluginStatus.NeedsExemption, row.Status);
        Assert.StartsWith("engines.dsh:", row.Requirement);
        Assert.DoesNotContain("peerDependencies", row.Requirement);

        // engines.dsh 满足 → ok（peers 再离谱也不看）。
        var okMeta = new FakeMeta().With("pkg-risk", "2.0.0", engines: ">=0.2.0-rc.2 || >=0.2.1-rc.2",
            peers: [("@deepseek-ai/dsh-tools", "^0.1.0")]);
        var ok = Assert.Single((await new Packs(f.Launcher, okMeta)
            .PreflightPack(f.Instance, "web", pack, null, CancellationToken.None)).Plugins);
        Assert.Equal(PackPluginStatus.Ok, ok.Status);
    }

    [Fact]
    public async Task Preflight_Npm404_IsMissing()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("pkg-a", "1.0.0");   // bridge-browser 不登记 = 404
        var pack = WritePack(f, "test", "Test", "0.1.0", "0.2.1-rc.2",
            Plugin("pkg-a"), Plugin("@yuxianglin/dsh-bridge-browser"));
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        var missing = Assert.Single(preflight.Plugins, p => p.Status == PackPluginStatus.Missing);
        Assert.Equal("@yuxianglin/dsh-bridge-browser", missing.Name);
        Assert.Contains("404", missing.Requirement);
        Assert.Equal(1, preflight.MissingCount);
        Assert.True(preflight.NeedsDecision);
    }

    [Fact]
    public async Task Preflight_NetworkUnknown_DoesNotBlock()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().Unknown("pkg-unknown");
        var pack = WritePack(f, "u", "U", "1.0.0", "0.2.1-rc.2", Plugin("pkg-unknown"));
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        var row = Assert.Single(preflight.Plugins);
        Assert.Equal(PackPluginStatus.Unknown, row.Status);
        Assert.Equal(1, preflight.UnknownCount);
        Assert.False(preflight.NeedsDecision);   // unknown 不阻塞
        Assert.True(preflight.CanInstall);
    }

    [Fact]
    public async Task Preflight_VersionConflict_PinnedAndRange()
    {
        var f = Temp();
        WriteProfile(f, new() { ["pkg-a"] = "1.0.0", ["pkg-b"] = "1.5.0" },
            bundles: ["pkg-a", "pkg-b"]);
        WriteModule(f, "pkg-a", "1.0.0");
        WriteModule(f, "pkg-b", "1.5.0");
        var meta = new FakeMeta()
            .With("pkg-a", "2.0.0")
            .With("pkg-b", "2.0.0");
        var pack = WritePack(f, "c", "C", "1.0.0", "0.2.1-rc.2",
            Plugin("pkg-a@2.0.0"),        // 钉死 2.0.0，实例里 1.0.0
            Plugin("pkg-b@^2.0.0"));      // range 不满足，实例里 1.5.0
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        Assert.Equal(2, preflight.ConflictCount);
        Assert.All(preflight.Plugins, p => Assert.Equal(PackPluginStatus.VersionConflict, p.Status));
        Assert.Contains(preflight.Plugins, p => p.Requirement.Contains("包钉 2.0.0"));
        Assert.Contains(preflight.Plugins, p => p.Requirement.Contains("不满足 range"));
        Assert.True(preflight.NeedsDecision);
        Assert.False(preflight.CanInstall);
    }

    [Fact]
    public async Task Preflight_KernelMismatch_WarnsButDoesNotBlock()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("pkg-a", "1.0.0");
        var pack = WritePack(f, "k", "K", "1.0.0", "0.1.0-rc.7", Plugin("pkg-a"));
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        Assert.Equal("0.1.0-rc.7", preflight.PackKernel);
        Assert.Equal("0.2.1-rc.2", preflight.InstanceKernel);
        Assert.False(preflight.KernelMatches);
        Assert.Contains(preflight.Warnings, w => w.Contains("内核"));
        Assert.Equal(PackPluginStatus.Ok, Assert.Single(preflight.Plugins).Status);
        Assert.False(preflight.NeedsDecision);    // 内核不匹配只警告
        Assert.True(preflight.CanInstall);
    }

    [Fact]
    public async Task Preflight_ManagerInstalledFromProfile()
    {
        var f = Temp();
        WriteProfile(f, new() { [Packs.ManagerSpec] = "^0.4.3" }, bundles: [Packs.ManagerSpec]);
        var meta = new FakeMeta().With("pkg-a", "1.0.0");
        var pack = WritePack(f, "m", "M", "1.0.0", "0.2.1-rc.2", Plugin("pkg-a"));
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        Assert.True(preflight.ManagerInstalled);
        Assert.DoesNotContain(preflight.Warnings, w => w.Contains(Packs.ManagerSpec));
    }

    [Fact]
    public async Task Preflight_LocalPlugin_ReadsPackPackageJson_NoNpmLookup()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta();
        var pack = WritePack(f, "local", "Local", "1.0.0", "0.2.1-rc.2",
            new { name = "my-local-plugin", local = "plugins/my-local-plugin", required = true });
        var localDir = Path.Combine(Path.GetDirectoryName(pack)!, "plugins", "my-local-plugin");
        Directory.CreateDirectory(localDir);
        File.WriteAllText(Path.Combine(localDir, "package.json"),
            JsonSerializer.Serialize(new { name = "my-local-plugin", version = "0.1.0", main = "index.js" }),
            new UTF8Encoding(false));
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        var row = Assert.Single(preflight.Plugins);
        Assert.True(row.IsLocal);
        Assert.Equal("my-local-plugin", row.Name);
        Assert.Equal("0.1.0", row.LatestVersion);
        Assert.Equal(PackPluginStatus.Ok, row.Status);
        Assert.Empty(meta.Calls);                 // 本地插件不查 npm
    }

    [Fact]
    public async Task Preflight_AlreadyInstalled_ReportsLedgerState()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("pkg-a", "1.0.0");
        var pack = WritePack(f, "a", "A", "1.0.0", "0.2.1-rc.2", Plugin("pkg-a"));
        // 造一个“已装”的账本 + 投影；profile 里没有 pkg-a → 对账应为 incomplete。
        var projection = Path.Combine(f.Workspace, ".agent-pack", "modpacks", "pack-agent-modpack-a");
        Directory.CreateDirectory(projection);
        var ledger = PackLedger.Load(f.Launcher, f.Instance);
        ledger.RecordPack("a", "A", "1.0.0", pack, ".agent-pack/modpacks/pack-agent-modpack-a");
        ledger.RecordInstall("a", "web", [new LedgerPlugin { Spec = "pkg-a" }]);
        ledger.Save();
        var packs = new Packs(f.Launcher, meta);

        var preflight = await packs.PreflightPack(f.Instance, "web", pack, null, CancellationToken.None);

        Assert.True(preflight.AlreadyInstalled);
        Assert.Equal(PackLedger.StateIncomplete, preflight.LedgerState);
    }

    // ---- install（钩子注入，不跑 dsh/pnpm）--------------------------------

    static PackApplyResult Applied(params string[] added) => new()
    {
        PackId = "demo",
        Title = "Demo Pack",
        Profile = "web",
        State = PackLedger.StateInstalled,
        Added = [.. added],
    };

    [Fact]
    public async Task Install_DryRun_DoesNotWriteAnything()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta();                      // pkg-missing → 404
        var pack = WritePack(f, "d", "D", "1.0.0", "0.2.1-rc.2", Plugin("pkg-missing"));
        var packs = new Packs(f.Launcher, meta);
        var applied = 0;
        packs.ApplyHook = (_, _, _, _, _) => { applied++; return Task.FromResult(Applied()); };

        var report = await packs.InstallPackToInstance(f.Instance, "web", pack,
            new PackInstallOptions { DryRun = true }, null, CancellationToken.None);

        Assert.Equal("needs-decision", report.State);   // 但没带 SkipMissing，预检如实报
        Assert.False(report.Ok);
        Assert.Equal(0, applied);
        Assert.False(File.Exists(f.LedgerPath));
        Assert.False(Directory.Exists(Path.Combine(f.Workspace, ".agent-pack")));
    }

    [Fact]
    public async Task Install_NoFlags_NeedsDecision_NoWrite()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("pkg-risk", "0.2.1",
            peers: [("@deepseek-ai/dsh-commands", "^0.1.0-rc.6")]);
        var pack = WritePack(f, "n", "N", "1.0.0", "0.2.1-rc.2", Plugin("pkg-risk"));
        var packs = new Packs(f.Launcher, meta);
        var applied = 0;
        packs.ApplyHook = (_, _, _, _, _) => { applied++; return Task.FromResult(Applied()); };

        var report = await packs.InstallPackToInstance(f.Instance, "web", pack,
            new PackInstallOptions(), null, CancellationToken.None);

        Assert.Equal("needs-decision", report.State);
        Assert.False(report.Ok);
        Assert.Equal(0, applied);
        Assert.Contains(report.Warnings, w => w.Contains("--accept-risk"));
        Assert.False(File.Exists(f.LedgerPath));
    }

    [Fact]
    public async Task Install_AcceptRisk_WritesExemptionsAndApplies()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("pkg-risk", "0.2.1",
            peers: [("@deepseek-ai/dsh-commands", "^0.1.0-rc.6")]);
        var pack = WritePack(f, "r", "R", "1.0.0", "0.2.1-rc.2", Plugin("pkg-risk"));
        var packs = new Packs(f.Launcher, meta);
        List<(string PackageVersion, string DshVersion)>? fixes = null;
        IReadOnlyCollection<string>? skip = null;
        packs.AllowVersionsHook = (_, profile, rows, _, _) =>
        {
            Assert.Equal("web", profile);
            fixes = rows.ToList();
            return Task.FromResult(rows.Select(r => r.PackageVersion).ToList());
        };
        packs.ApplyHook = (_, path, skips, _, _) =>
        {
            skip = skips;
            Assert.Equal(pack, path);
            return Task.FromResult(Applied("pkg-risk"));
        };

        var report = await packs.InstallPackToInstance(f.Instance, "web", pack,
            new PackInstallOptions { AcceptRisk = true }, null, CancellationToken.None);

        Assert.NotNull(fixes);
        Assert.Equal(["pkg-risk@0.2.1"], fixes!.Select(fx => fx.PackageVersion));
        Assert.Equal("0.2.1-rc.2", fixes[0].DshVersion);
        Assert.Empty(skip!);
        Assert.Equal(["pkg-risk@0.2.1"], report.Exempted);
        Assert.Equal(PackLedger.StateInstalled, report.State);
        Assert.True(report.Ok);
        Assert.Single(report.Added);
    }

    [Fact]
    public async Task Install_SkipMissing_RecordsSkippedAndIncomplete()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta().With("pkg-a", "1.0.0");
        var pack = WritePack(f, "s", "S", "1.0.0", "0.2.1-rc.2",
            Plugin("pkg-a"), Plugin("@yuxianglin/dsh-bridge-browser"));
        var packs = new Packs(f.Launcher, meta);
        IReadOnlyCollection<string>? skip = null;
        packs.ApplyHook = (_, _, skips, _, _) =>
        {
            skip = skips;
            var result = Applied("pkg-a");
            result.Skipped = [.. skips ?? []];
            result.State = PackLedger.StateIncomplete;
            return Task.FromResult(result);
        };

        var report = await packs.InstallPackToInstance(f.Instance, "web", pack,
            new PackInstallOptions { SkipMissing = true }, null, CancellationToken.None);

        Assert.NotNull(skip);
        Assert.Contains("@yuxianglin/dsh-bridge-browser", skip!);
        Assert.Equal(["@yuxianglin/dsh-bridge-browser"], report.Skipped);
        Assert.Equal(PackLedger.StateIncomplete, report.State);
        Assert.True(report.Ok);            // incomplete 也是“已应用”，exit 0
        Assert.Empty(report.Exempted);
    }

    [Fact]
    public async Task Install_AcceptRiskAndSkipMissing_BothFlags()
    {
        var f = Temp();
        WriteProfile(f);
        var meta = new FakeMeta()
            .With("pkg-a", "1.0.0")
            .With("pkg-risk", "0.2.1", peers: [("@deepseek-ai/dsh-tools", "^0.1.0-rc.6")]);
        var pack = WritePack(f, "b", "B", "1.0.0", "0.2.1-rc.2",
            Plugin("pkg-a"), Plugin("pkg-risk"), Plugin("gone-pkg"));
        var packs = new Packs(f.Launcher, meta);
        packs.AllowVersionsHook = (_, _, rows, _, _) =>
            Task.FromResult(rows.Select(r => r.PackageVersion).ToList());
        packs.ApplyHook = (_, _, skips, _, _) =>
        {
            Assert.Contains("gone-pkg", skips!);
            var result = Applied("pkg-a", "pkg-risk");
            result.Skipped = [.. skips!];
            result.State = PackLedger.StateIncomplete;
            return Task.FromResult(result);
        };

        var report = await packs.InstallPackToInstance(f.Instance, "web", pack,
            new PackInstallOptions { AcceptRisk = true, SkipMissing = true }, null,
            CancellationToken.None);

        Assert.Equal(["pkg-risk@0.2.1"], report.Exempted);
        Assert.Equal(["gone-pkg"], report.Skipped);
        Assert.Equal(PackLedger.StateIncomplete, report.State);
        Assert.Equal(2, report.Added.Count);
        Assert.True(report.Ok);
    }

    [Fact]
    public async Task Install_VersionConflict_AlwaysNeedsDecision()
    {
        var f = Temp();
        WriteProfile(f, new() { ["pkg-a"] = "1.0.0" }, bundles: ["pkg-a"]);
        WriteModule(f, "pkg-a", "1.0.0");
        var meta = new FakeMeta().With("pkg-a", "2.0.0");
        var pack = WritePack(f, "v", "V", "1.0.0", "0.2.1-rc.2", Plugin("pkg-a@2.0.0"));
        var packs = new Packs(f.Launcher, meta);
        var applied = 0;
        packs.ApplyHook = (_, _, _, _, _) => { applied++; return Task.FromResult(Applied()); };

        // 冲突没有对应 flag：即使带 accept-risk + skip-missing 也必须挡住。
        var report = await packs.InstallPackToInstance(f.Instance, "web", pack,
            new PackInstallOptions { AcceptRisk = true, SkipMissing = true }, null,
            CancellationToken.None);

        Assert.Equal("needs-decision", report.State);
        Assert.False(report.Ok);
        Assert.Equal(0, applied);
        Assert.Contains(report.Warnings, w => w.Contains("版本冲突"));
    }
}
