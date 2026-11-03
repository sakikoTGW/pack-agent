using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>npm package icon 的解析与 CDN URL（不触网）。</summary>
public sealed class NpmPackageIconTests
{
    [Fact]
    public void ParseLatest_ReadsVersionAndIcon() =>
        Assert.Equal(("1.2.3", "assets/logo.png"),
            NpmPackageIcon.ParseLatest("""{"name":"x","version":"1.2.3","icon":"assets/logo.png"}"""));

    [Fact]
    public void ParseLatest_NoIconIsNull() =>
        Assert.Null(NpmPackageIcon.ParseLatest("""{"name":"x","version":"1.0.0"}"""));

    [Fact]
    public void ParseLatest_BadJsonIsNull() =>
        Assert.Null(NpmPackageIcon.ParseLatest("not json"));

    [Theory]
    [InlineData("dsh-pet", "1.0.0", "assets/logo.png", "https://cdn.jsdelivr.net/npm/dsh-pet@1.0.0/assets/logo.png")]
    [InlineData("dsh-pet", "", "./icon.png", "https://cdn.jsdelivr.net/npm/dsh-pet/icon.png")]
    [InlineData("@scope/pkg", "2.0.0", "img/a b.png", "https://cdn.jsdelivr.net/npm/@scope/pkg@2.0.0/img/a%20b.png")]
    public void BuildUrl_ResolvesRelativeIcon(string name, string version, string icon, string expected) =>
        Assert.Equal(expected, NpmPackageIcon.BuildUrl(name, version, icon));
}
