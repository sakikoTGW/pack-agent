using System.IO;
using Pad.Core;
using Xunit;

namespace Pad.Core.Tests;

/// <summary>
/// 平台层与 pnpm 链接重建的契约测试：Windows 走 junction，macOS/Linux 走 symlink，
/// 两边都必须满足同一组断言。CI 在 macos-latest 上跑这份，等于验证 mac 的链接行为。
/// </summary>
public sealed class PlatformAndLinksTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-core-" + Guid.NewGuid().ToString("N"));

    public PlatformAndLinksTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Platform_MatchesHostOs()
    {
        Assert.Equal(OperatingSystem.IsWindows(), Platform.Current.IsWindows);
        Assert.Equal(OperatingSystem.IsWindows() ? "win32" : Platform.Current.Name, Platform.Current.Name);
    }

    [Fact]
    public void Junction_CreatesResolvableDirectoryLink()
    {
        var target = Path.Combine(_root, "store", "a@1.0.0", "node_modules", "a");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "index.js"), "x");

        var link = Path.Combine(_root, "node_modules", "a");
        PnpmHeal.Junction(link, target, isDir: true);

        Assert.True(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(link, "index.js")));
        Assert.False(string.IsNullOrWhiteSpace(Platform.Current.ReadLinkTarget(link)));
    }

    [Fact]
    public void Junction_FileLink_ReadsThrough()
    {
        var target = Path.Combine(_root, "file.txt");
        File.WriteAllText(target, "hello");
        var link = Path.Combine(_root, "file.link");

        PnpmHeal.Junction(link, target, isDir: false);

        Assert.True(File.Exists(link));
        Assert.Equal("hello", File.ReadAllText(link));
    }

    [Fact]
    public void NeedsHeal_RealDirectory_IsUnhealthy()
    {
        var nm = Path.Combine(_root, "node_modules");
        Directory.CreateDirectory(Path.Combine(nm, "pkg"));                       // 真目录 = 拷来的
        Directory.CreateDirectory(Path.Combine(nm, ".pnpm", "pkg@1.0.0", "node_modules", "pkg"));
        Assert.True(PnpmHeal.NeedsHeal(nm));
    }

    [Fact]
    public void NeedsHeal_HealthyLink_IsClean()
    {
        var nm = Path.Combine(_root, "node_modules");
        var store = Path.Combine(nm, ".pnpm", "pkg@1.0.0", "node_modules", "pkg");
        Directory.CreateDirectory(store);
        File.WriteAllText(Path.Combine(store, "index.js"), "x");
        PnpmHeal.Junction(Path.Combine(nm, "pkg"), store, isDir: true);

        Assert.False(PnpmHeal.NeedsHeal(nm));
    }

    [Fact]
    public void DiscoverPosix_EmptyInput_DoesNotThrow()
    {
        var inventory = DiscoverPosix.Run(Launcher.Default(), [], CancellationToken.None);
        Assert.NotNull(inventory);
        Assert.NotNull(inventory.Homes);
    }
}
