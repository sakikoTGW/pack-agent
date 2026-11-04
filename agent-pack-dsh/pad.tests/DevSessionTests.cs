using System.IO;
using System.Text;
using System.Text.Json;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// Development session (AI-first loop) + the "never silently delete backups" rule.
/// Pure filesystem fixtures: no dsh process, no network.
/// </summary>
public sealed class DevSessionTests
{
    sealed record Fixture(string Root, Launcher Launcher, Instance Instance);

    static Fixture Temp(bool withDeps = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-dev-tests", Guid.NewGuid().ToString("N"));
        var instDir = Path.Combine(root, "instances", "ev");
        var home = Path.Combine(instDir, "home");
        var prof = Path.Combine(home, "profiles", "web");
        Directory.CreateDirectory(prof);
        Directory.CreateDirectory(Path.Combine(instDir, "workspace"));

        var instanceJson = new
        {
            schema = "pack-agent.launcher.instance/v2",
            id = "ev",
            name = "ev",
            dsh = new { version = "0.2.1-rc.2" },
            home,
            workspace = new { kind = "owned", path = Path.Combine(instDir, "workspace") },
            status = "ready",
            lastProfile = "web",
            created = DateTimeOffset.UtcNow.ToString("o"),
            updated = DateTimeOffset.UtcNow.ToString("o"),
        };
        File.WriteAllText(Path.Combine(instDir, "instance.json"),
            JsonSerializer.Serialize(instanceJson), new UTF8Encoding(false));

        var bundles = withDeps
            ? new[] { "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app" }
            : new[] { "@deepseek-ai/dsh-base", "missing-plugin" };
        File.WriteAllText(Path.Combine(prof, "package.json"),
            JsonSerializer.Serialize(new { dsh = new { profile = new { bundles } } }), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(prof, "cordis.patch.yml"), "[]\n", new UTF8Encoding(false));

        var launcher = new Launcher(root);
        var inst = launcher.Instance("ev")!;
        return new Fixture(root, launcher, inst);
    }

    [Fact]
    public void Open_CreatesRunningSession_WithDeclarationHash()
    {
        var f = Temp();

        var session = DevSessions.Open(f.Launcher, f.Instance, "web");

        Assert.Equal("running", session.State);
        Assert.Equal("ev", session.Instance);
        Assert.Equal("web", session.Profile);
        Assert.False(string.IsNullOrWhiteSpace(session.SourceHash));
        Assert.True(File.Exists(DevSessions.FilePath(f.Launcher, "ev")));
        // Re-open resumes the same session instead of starting a second one.
        Assert.Equal(session.Id, DevSessions.Open(f.Launcher, f.Instance, "web").Id);
    }

    [Fact]
    public async Task Verify_FlagsMissingBundle_AsFailWithExit2()
    {
        var f = Temp();
        DevSessions.Open(f.Launcher, f.Instance, "web");

        var verdict = await DevOps.VerifyAsync(f.Launcher, f.Instance, "web", null, CancellationToken.None);

        Assert.Equal("fail", verdict.Result);
        Assert.Equal(2, verdict.ExitCode);
        var resolve = verdict.Checks.First(c => c.Id == "plugins-resolve");
        Assert.Equal("fail", resolve.Status);
        Assert.Contains("missing-plugin", resolve.Detail);
        // config-compiles also fails: no dsh release is installed in this fixture.
        Assert.Equal("fail", verdict.Checks.First(c => c.Id == "config-compiles").Status);
        // The declaration layer was not touched by verification.
        Assert.Equal("pass", verdict.Checks.First(c => c.Id == "source-untouched").Status);
        Assert.True(File.Exists(Path.Combine(DevSessions.VerdictDir(f.Launcher, "ev"), "verdict.json")));
    }

    [Fact]
    public async Task Discard_ClosesSession_AndKeepsHistory()
    {
        var f = Temp();
        var session = DevSessions.Open(f.Launcher, f.Instance, "web");

        var closed = await DevOps.DiscardAsync(f.Launcher, f.Instance, null, CancellationToken.None);

        Assert.Equal("discarded", closed.State);
        Assert.NotNull(closed.EndedAt);
        Assert.Equal("discarded", DevSessions.Load(f.Launcher, "ev")!.State);
        Assert.Contains(DevSessions.History(f.Launcher, "ev"), s => s.Id == session.Id);
        // A closed session cannot be committed.
        Assert.Throws<PadError>(() => DevOps.Commit(f.Launcher, f.Instance, dryRun: false));
    }

    [Fact]
    public void Checkpoints_Create_DoesNotDeleteOldBackups()
    {
        var f = Temp();
        for (var i = 0; i < 25; i++)
            Checkpoints.Create(f.Launcher, f.Instance, "web", "test " + i);

        // 25 > Keep(20): before D111's fix, Create() pruned silently down to 20.
        Assert.Equal(25, Checkpoints.List(f.Launcher, "ev").Count);
    }

    [Fact]
    public void Checkpoints_Prune_IsExplicitAndReturnsRemovedIds()
    {
        var f = Temp();
        for (var i = 0; i < 25; i++)
            Checkpoints.Create(f.Launcher, f.Instance, "web", "test " + i);

        var removed = Checkpoints.Prune(f.Launcher, "ev", keep: 5);

        Assert.Equal(20, removed.Count);
        Assert.Equal(5, Checkpoints.List(f.Launcher, "ev").Count);
    }

    [Theory]
    [InlineData("dsh-vision-ocr@0.3.1", "dsh-vision-ocr")]
    [InlineData("@scope/pkg@1.0.0", "@scope/pkg")]
    [InlineData("file:E:/tmp/plugins/hot-bundle", "hot-bundle")]
    [InlineData("file:/tmp/plugins/hot-bundle/", "hot-bundle")]
    public void PackageName_HandlesVersionedScopedAndFileSpecs(string spec, string expected)
    {
        // A file: spec is a local path; pnpm/DSH can only remove by package name.
        Assert.Equal(expected, Trials.PackageName(spec));
    }
}
