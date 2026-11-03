using System.IO;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>图标缓存：已有本地文件时不再走 registry（这就是“图标加载更快”的关键路径）。</summary>
public sealed class NpmPackageIconCacheTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-icon-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task EnsureAsync_ReturnsCachedFile()
    {
        var dir = BundleIcon.CacheDir(_root, "dsh-pet");
        Directory.CreateDirectory(dir);
        var icon = Path.Combine(dir, "icon-npm.png");
        File.WriteAllBytes(icon, [0x89, 0x50, 0x4E, 0x47]);

        var got = await NpmPackageIcon.EnsureAsync(_root, "dsh-pet", "", CancellationToken.None);

        Assert.Equal(icon, got);
    }

    [Fact]
    public async Task EnsureAsync_UsesJpegCacheToo()
    {
        var dir = BundleIcon.CacheDir(_root, "dsh-pet");
        Directory.CreateDirectory(dir);
        var icon = Path.Combine(dir, "icon-npm.jpg");
        File.WriteAllBytes(icon, [0xFF, 0xD8, 0xFF]);

        Assert.Equal(icon, await NpmPackageIcon.EnsureAsync(_root, "dsh-pet", "", CancellationToken.None));
    }
}
