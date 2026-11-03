using System.IO;
using System.Text.Json;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// Hot-reload integration seams that are pure enough to pin without dsh/pnpm:
/// the capability decision table, the disk probe (including the launch ledger and
/// the manager detector), and the minimal checkpoint round trip.
/// </summary>
public sealed class HotReloadTests : IDisposable
{
    public sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public Launcher Launcher { get; }
        public Instance Instance { get; }
        public string ProfileDir { get; }

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "pad-hot-tests", Guid.NewGuid().ToString("N"));
            Launcher = new Launcher(Root);
            ProfileDir = Path.Combine(Root, "instances", "ev-test", "home", "profiles", "web");
            Directory.CreateDirectory(ProfileDir);

            Instance = new Instance
            {
                Id = "ev-test",
                Name = "ev-test",
                Home = Path.Combine(Root, "instances", "ev-test", "home"),
                Dsh = new InstanceDsh { Version = "0.2.1-rc.2" },
            };
            Instance.Workspace.Path = Path.Combine(Root, "ws");
            Directory.CreateDirectory(Instance.Workspace.Path);
            Launcher.SaveInstance(Instance);

            File.WriteAllText(Path.Combine(ProfileDir, "package.json"),
                """{"dsh":{"profile":{"bundles":["@deepseek-ai/dsh-base"]}}}""");
            File.WriteAllText(Path.Combine(ProfileDir, "cordis.patch.yml"), "[]");
        }

        public void WritePatch(string yaml) =>
            File.WriteAllText(Path.Combine(ProfileDir, "cordis.patch.yml"), yaml);

        public void InstallHmr()
        {
            var dir = Path.Combine(ProfileDir, "node_modules", "@deepseek-ai", "dsh-hmr");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "package.json"), """{"name":"@deepseek-ai/dsh-hmr"}""");
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { /* temp cleanup is best effort */ }
        }
    }

    [Theory]
    [InlineData("pad", "on", "none", true)]
    [InlineData("pad", "config-only", "none", false)]
    [InlineData("pad", "off", "none", false)]
    [InlineData("pad", "absent", "none", false)]
    [InlineData("external", "on", "none", false)]
    [InlineData("none", "on", "none", false)]
    [InlineData("pad", "on", "mygo", false)]
    [InlineData("pad", "on", "other", false)]
    public void Classify_PinsEveryBranch(string launch, string hmr, string manager, bool allowed)
    {
        var (ok, reason) = HotCapability.Classify(launch, hmr, manager);
        Assert.Equal(allowed, ok);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Detect_NotRunning_HmrAbsent_IsCold()
    {
        using var fx = new Fixture();
        var cap = HotCapabilityProbe.Detect(fx.Launcher, fx.Instance, "web", refresh: true);

        Assert.Equal("none", cap.LaunchSource);
        Assert.Equal("absent", cap.Hmr);
        Assert.Equal("none", cap.Manager);
        Assert.False(cap.HotAllowed);
        Assert.Contains("launchSource=none", cap.Reason);
        Assert.True(File.Exists(Path.Combine(fx.Launcher.InstanceDir(fx.Instance.Id), HotCapabilityProbe.FileName)));
    }

    [Fact]
    public void Detect_MygoInstalled_IsManagerMygo()
    {
        using var fx = new Fixture();
        var mygo = Path.Combine(fx.ProfileDir, "node_modules", "dsh-mygo");
        Directory.CreateDirectory(mygo);
        File.WriteAllText(Path.Combine(mygo, "package.json"), """{"name":"dsh-mygo"}""");

        var cap = HotCapabilityProbe.Detect(fx.Launcher, fx.Instance, "web", refresh: true);
        Assert.Equal("mygo", cap.Manager);
        Assert.False(cap.HotAllowed);
        Assert.StartsWith("manager=mygo", cap.Reason);
    }

    [Fact]
    public void Detect_PadLaunched_HmrOn_IsHot()
    {
        using var fx = new Fixture();
        fx.InstallHmr();
        fx.WritePatch("- id: hmr\n  disabled: false\n  config:\n    root: ['.']\n");
        fx.Launcher.TrackRun(new RunEntry
        {
            Instance = fx.Instance.Id,
            Profile = "web",
            Pid = Environment.ProcessId,
            Port = 3999,
            StartedAt = DateTimeOffset.UtcNow.ToString("o"),
        });

        var cap = HotCapabilityProbe.Detect(fx.Launcher, fx.Instance, "web", refresh: true);
        Assert.Equal("pad", cap.LaunchSource);
        Assert.Equal("on", cap.Hmr);
        Assert.True(cap.HotAllowed);
        Assert.Equal("hot", cap.Reason);
    }

    [Fact]
    public void Detect_HmrEntryDisabledByPatch_IsOff()
    {
        using var fx = new Fixture();
        fx.InstallHmr();
        fx.WritePatch("- id: hmr\n  disabled: true\n");

        var cap = HotCapabilityProbe.Detect(fx.Launcher, fx.Instance, "web", refresh: true);
        Assert.Equal("off", cap.Hmr);
        Assert.False(cap.HotAllowed);
        Assert.Contains("hmr=off", cap.Reason);
    }

    [Fact]
    public void Checkpoint_RoundTripsPackageAndPatch()
    {
        using var fx = new Fixture();
        var package = Path.Combine(fx.ProfileDir, "package.json");
        var patch = Path.Combine(fx.ProfileDir, "cordis.patch.yml");
        var beforePackage = File.ReadAllText(package);
        var beforePatch = File.ReadAllText(patch);

        var ck = Checkpoints.Create(fx.Launcher, fx.Instance, "web", "test change");
        Assert.NotEmpty(ck.Files);

        File.WriteAllText(package, """{"dsh":{"profile":{"bundles":["@deepseek-ai/dsh-base","bad@1.0.0"]}}}""");
        File.WriteAllText(patch, "- insert:\n    - id: bad\n      name: bad\n");

        Checkpoints.Restore(fx.Launcher, fx.Instance, ck.Id);

        Assert.Equal(beforePackage, File.ReadAllText(package));
        Assert.Equal(beforePatch, File.ReadAllText(patch));
        // Restore snapshots the pre-restore state first, so an undo is undoable.
        Assert.True(Checkpoints.List(fx.Launcher, fx.Instance.Id).Count >= 2);
    }

    [Fact]
    public void Checkpoint_Latest_IsEmptyOnFreshInstance()
    {
        using var fx = new Fixture();
        Assert.Null(Checkpoints.Latest(fx.Launcher, fx.Instance.Id));
    }

    [Fact]
    public void LatestBeforeChange_SkipsPreRestoreSnapshots()
    {
        using var fx = new Fixture();
        var real = Checkpoints.Create(fx.Launcher, fx.Instance, "web", "plugin add x");
        Checkpoints.Restore(fx.Launcher, fx.Instance, real.Id); // creates a pre-restore snapshot

        Assert.NotNull(Checkpoints.Latest(fx.Launcher, fx.Instance.Id));
        var beforeChange = Checkpoints.LatestBeforeChange(fx.Launcher, fx.Instance.Id);
        Assert.Equal(real.Id, beforeChange!.Id);
    }

    [Fact]
    public void PluginVerify_RowMatches_UsesBareName()
    {
        using var fx = new Fixture();
        Assert.False(PluginVerify.RowMatches(fx.Launcher, fx.Instance, "web", "@acme/x@1.0.0"));
    }

    public void Dispose()
    {
        // fixtures own their cleanup
    }
}
