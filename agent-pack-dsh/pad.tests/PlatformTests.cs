using System.IO;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>平台抽象层：Windows 上必须仍走 junction，且对外只暴露 IPlatform。</summary>
public sealed class PlatformTests : System.IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "pad-platform-" + Guid.NewGuid().ToString("N"));

    public PlatformTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Current_MatchesHostOs()
    {
        Assert.Equal(OperatingSystem.IsWindows(), Platform.Current.IsWindows);
        Assert.Equal(OperatingSystem.IsWindows() ? "win32" : Platform.Current.Name, Platform.Current.Name);
    }

    [Fact]
    public void Junction_CreatesDirectoryLink()
    {
        var target = Path.Combine(_root, "target");
        var link = Path.Combine(_root, "link");
        Directory.CreateDirectory(Path.Combine(target, "sub"));

        PnpmHeal.Junction(link, target, isDir: true);

        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(Path.Combine(link, "sub")));
        Assert.NotNull(Platform.Current.ReadLinkTarget(link));
    }

    [Fact]
    public void Junction_FileLink_CreatesFileLink()
    {
        var target = Path.Combine(_root, "file.txt");
        var link = Path.Combine(_root, "file.link");
        File.WriteAllText(target, "x");

        PnpmHeal.Junction(link, target, isDir: false);

        Assert.True(File.Exists(link));
        Assert.Equal("x", File.ReadAllText(link));
    }
}
