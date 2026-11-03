using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// 安装预检的 semver 子集（D107）：DSH 用
/// <c>semver.satisfies(runtime, range, { includePrerelease: true })</c>，
/// 这里覆盖 ^ / ~ / &gt;= / &lt; / || / 连字符 / 通配 / 预发布参与比较 / 异常范围不抛。
/// </summary>
public sealed class PackSemverTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("0.2.1-rc.2")]
    [InlineData("v0.1.0-rc.6")]
    [InlineData("1.2.3+build.5")]
    [InlineData("1.2.3-rc.1+build.5")]
    public void Parse_AcceptsValidVersions(string text)
    {
        var version = PackSemver.Parse(text);
        Assert.NotNull(version);
        Assert.True(PackSemver.IsExact(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("^1.2.3")]
    [InlineData("latest")]
    [InlineData("1.2.3.4")]
    [InlineData("not a version")]
    public void Parse_RejectsRangesAndGarbage(string text)
    {
        Assert.Null(PackSemver.Parse(text));
        Assert.False(PackSemver.IsExact(text));
    }

    [Fact]
    public void Compare_OrdersPrereleasesBeforeReleases()
    {
        Assert.True(PackSemver.Compare(PackSemver.Parse("0.2.1-rc.2")!, PackSemver.Parse("0.2.1")!) < 0);
        Assert.True(PackSemver.Compare(PackSemver.Parse("0.2.1-rc.10")!, PackSemver.Parse("0.2.1-rc.2")!) > 0);
        Assert.True(PackSemver.Compare(PackSemver.Parse("1.0.0-alpha")!, PackSemver.Parse("1.0.0-alpha.1")!) < 0);
        Assert.True(PackSemver.Compare(PackSemver.Parse("1.0.0-alpha.beta")!, PackSemver.Parse("1.0.0-beta")!) < 0);
        Assert.Equal(0, PackSemver.Compare(PackSemver.Parse("1.2.3+build.1")!, PackSemver.Parse("1.2.3+build.2")!));
    }

    // ---- DSH 实测形状：这些必须和 semver.satisfies 一致 --------------------

    [Fact]
    public void DshPeerRange_DshFindSkill_IsIncompatibleWith_021Rc2()
    {
        // dsh-find-skill@0.2.1 的 peers：@deepseek-ai/dsh-commands 等 ^0.1.0-rc.6
        Assert.False(PackSemver.Satisfies("0.2.1-rc.2", "^0.1.0-rc.6"));
        Assert.True(PackSemver.Satisfies("0.1.0-rc.6", "^0.1.0-rc.6"));
        Assert.True(PackSemver.Satisfies("0.1.9", "^0.1.0-rc.6"));
        Assert.False(PackSemver.Satisfies("0.2.0", "^0.1.0-rc.6"));
    }

    [Theory]
    [InlineData("0.2.1-rc.2", ">=0.2.0-rc.2 || >=0.2.1-rc.2", true)]
    [InlineData("0.2.0-rc.2", ">=0.2.0-rc.2 || >=0.2.1-rc.2", true)]
    [InlineData("0.2.0-rc.1", ">=0.2.0-rc.2 || >=0.2.1-rc.2", false)]
    [InlineData("0.3.0", ">=0.2.0-rc.2 || >=0.2.1-rc.2", true)]
    public void OrRanges(string version, string range, bool expected) =>
        Assert.Equal(expected, PackSemver.Satisfies(version, range));

    [Theory]
    // ^
    [InlineData("1.5.0", "^1.2.3", true)]
    [InlineData("2.0.0", "^1.2.3", false)]
    [InlineData("2.0.0-alpha", "^1.2.3", false)]      // includePrerelease 也挡 <2.0.0-0
    [InlineData("1.2.4-beta", "^1.2.3", true)]        // includePrerelease：预发布参与比较
    [InlineData("1.2.3-beta", "^1.2.3", false)]       // 低于下界
    [InlineData("0.2.9", "^0.2.1", true)]
    [InlineData("0.3.0", "^0.2.1", false)]
    [InlineData("0.0.3", "^0.0.3", true)]
    [InlineData("0.0.4", "^0.0.3", false)]
    [InlineData("0.5.0", "^0", true)]
    [InlineData("1.0.0", "^0", false)]
    // ~
    [InlineData("1.2.9", "~1.2.3", true)]
    [InlineData("1.3.0", "~1.2.3", false)]
    [InlineData("1.9.9", "~1", true)]
    [InlineData("2.0.0", "~1", false)]
    // >= / > / < / <= 的部分版本
    [InlineData("1.2.0", ">=1.2", true)]
    [InlineData("1.1.9", ">=1.2", false)]
    [InlineData("1.3.0", ">1.2", true)]
    [InlineData("1.2.9", ">1.2", false)]
    [InlineData("1.1.9", "<1.2", true)]
    [InlineData("1.2.0", "<1.2", false)]
    [InlineData("1.2.9", "<=1.2", true)]
    [InlineData("1.3.0", "<=1.2", false)]
    // 空格 AND
    [InlineData("0.2.1-rc.2", ">=0.2.0-rc.2 <0.3.0", true)]
    [InlineData("0.3.0", ">=0.2.0-rc.2 <0.3.0", false)]
    // 连字符范围
    [InlineData("2.0.0", "1.2.3 - 2.3.4", true)]
    [InlineData("2.3.4", "1.2.3 - 2.3.4", true)]
    [InlineData("2.3.5", "1.2.3 - 2.3.4", false)]
    [InlineData("1.2.2", "1.2.3 - 2.3.4", false)]
    // 通配
    [InlineData("1.2.7", "1.2.x", true)]
    [InlineData("1.3.0", "1.2.x", false)]
    [InlineData("1.9.0", "1.x", true)]
    [InlineData("2.0.0", "1.x", false)]
    [InlineData("0.0.1", "*", true)]
    // 精确
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.4", "1.2.3", false)]
    [InlineData("0.2.1-rc.2", "0.2.1-rc.2", true)]
    public void RangeShapes(string version, string range, bool expected) =>
        Assert.Equal(expected, PackSemver.Satisfies(version, range));

    [Theory]
    [InlineData("1.2.3", "", false)]
    [InlineData("1.2.3", "not-a-range", false)]
    [InlineData("1.2.3", "workspace:^", false)]
    [InlineData("1.2.3", "||", false)]
    [InlineData("", "1.2.3", false)]
    [InlineData("latest", "1.2.3", false)]
    public void BadInputs_NeverThrowAndNeverSatisfy(string version, string range, bool expected) =>
        Assert.Equal(expected, PackSemver.Satisfies(version, range));
}
