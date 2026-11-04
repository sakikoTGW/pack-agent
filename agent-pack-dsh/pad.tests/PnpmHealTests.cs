using System.Diagnostics;
using System.IO;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// 启动预检：只有真坏链才值得全量 Relink（那是 30s 级的操作），健康的 junction 必须原样放过。
/// </summary>
public sealed class PnpmHealTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-heal-" + Guid.NewGuid().ToString("N"));

    public PnpmHealTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>junction（mklink /J）不需要管理员；符号链接才需要。</summary>
    static void Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    /// <summary>一份「像 pnpm 铺出来的」node_modules：store 里是真目录，顶层是链接。</summary>
    string Kit()
    {
        var nm = Path.Combine(_root, "node_modules");
        Directory.CreateDirectory(Path.Combine(nm, ".pnpm", "foo@1.0.0", "node_modules", "foo"));
        return nm;
    }

    [Fact]
    public void NeedsHeal_HealthyKit_IsFalse()
    {
        var nm = Kit();
        Junction(Path.Combine(nm, "foo"), Path.Combine(nm, ".pnpm", "foo@1.0.0", "node_modules", "foo"));

        Assert.False(PnpmHeal.NeedsHeal(nm));
        Assert.Equal(0, PnpmHeal.RelinkIfNeeded(nm));
    }

    [Fact]
    public void NeedsHeal_CopiedRealDirectory_IsTrue()
    {
        var nm = Kit();
        Directory.CreateDirectory(Path.Combine(nm, "foo")); // 复制品：顶层是真目录

        Assert.True(PnpmHeal.NeedsHeal(nm));
    }

    [Fact]
    public void NeedsHeal_DanglingJunction_IsTrue()
    {
        var nm = Kit();
        var target = Path.Combine(_root, "gone");
        Directory.CreateDirectory(target);
        Junction(Path.Combine(nm, "foo"), target);
        Directory.Delete(target);

        Assert.True(PnpmHeal.NeedsHeal(nm));
    }

    [Fact]
    public void NeedsHeal_NoStore_IsFalse()
    {
        var nm = Path.Combine(_root, "plain");
        Directory.CreateDirectory(nm);

        Assert.False(PnpmHeal.NeedsHeal(nm));
    }

    [Fact]
    public void ClearProfilesFallback_LeavesDshJunctionAlone()
    {
        var home = Path.Combine(_root, "home");
        var fallback = Path.Combine(home, "profiles", "node_modules");
        Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
        var target = Path.Combine(_root, "store-node-modules");
        Directory.CreateDirectory(target);
        Junction(fallback, target);

        PnpmHeal.ClearProfilesFallback(home);

        Assert.True(Directory.Exists(fallback));
    }

    [Fact]
    public void ClearProfilesFallback_RemovesRealCopy()
    {
        var home = Path.Combine(_root, "home2");
        var fallback = Path.Combine(home, "profiles", "node_modules");
        Directory.CreateDirectory(Path.Combine(fallback, "leftover"));

        PnpmHeal.ClearProfilesFallback(home);

        Assert.False(Directory.Exists(fallback));
    }

    [Theory]
    [InlineData("Progress: resolved 1, reused 0, downloaded 0, added 0", 0, 1)]
    [InlineData("Progress: resolved 104, reused 74, downloaded 3, added 79, done", 79, 104)]
    [InlineData("Progress: resolved 92, reused 0, downloaded 0, added 0", 0, 92)]
    public void ParsePnpmProgress_ReadsAddedOverResolved(string line, int done, int total)
    {
        Assert.Equal((done, total), Launcher.ParsePnpmProgress(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Done in 23.2s using pnpm v11.7.0")]
    [InlineData("Progress: resolved 0, reused 0, downloaded 0, added 0")]
    [InlineData("dsh plugin --profile web add @deepseek-harness-tui/dsh-tui@0.18.0")]
    public void ParsePnpmProgress_IgnoresEverythingElse(string line)
    {
        Assert.Null(Launcher.ParsePnpmProgress(line));
    }

    [Theory]
    [InlineData("@deepseek-harness-tui/dsh-tui@0.18.0", "@deepseek-harness-tui/dsh-tui", "0.18.0")]
    [InlineData("@scope/pkg", "@scope/pkg", "")]
    [InlineData("dsh-pet", "dsh-pet", "")]
    [InlineData("dsh-pet@1.2.3", "dsh-pet", "1.2.3")]
    public void SplitSpec_ParsesScopedAndPlain(string spec, string name, string version)
    {
        Assert.Equal((name, version), Launcher.SplitSpec(spec));
    }
}
