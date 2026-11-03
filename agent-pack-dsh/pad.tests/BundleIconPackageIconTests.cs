using System.IO;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>已安装包的官方 icon：package.json 的 icon 字段优先于默认图标文件名。</summary>
public sealed class BundleIconPackageIconTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-icon-" + Guid.NewGuid().ToString("N"));
    readonly string _pkg;
    readonly string _launcher;

    public BundleIconPackageIconTests()
    {
        _pkg = Path.Combine(_root, "pkg");
        _launcher = Path.Combine(_root, "launcher");
        Directory.CreateDirectory(Path.Combine(_pkg, "assets"));
        Directory.CreateDirectory(_launcher);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Resolve_UsesPackageJsonIcon()
    {
        var icon = Path.Combine(_pkg, "assets", "logo.png");
        File.WriteAllBytes(icon, [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllText(Path.Combine(_pkg, "package.json"),
            """{"name":"x","version":"1.0.0","icon":"./assets/logo.png"}""");

        Assert.Equal(icon, BundleIcon.Resolve(_launcher, _pkg, "x"));
    }

    [Fact]
    public void Resolve_NoIconFieldFallsThrough()
    {
        File.WriteAllText(Path.Combine(_pkg, "package.json"), """{"name":"x","version":"1.0.0"}""");
        Assert.Null(BundleIcon.Resolve(_launcher, _pkg, "x"));
    }
}
