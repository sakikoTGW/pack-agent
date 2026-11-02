using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>拼音/首字母：汉字转拼音、查询识别、索引排序。</summary>
public sealed class PinyinSearchTests
{
    [Fact]
    public void Convert_ChineseToPinyinAndInitials()
    {
        var (full, initials) = PinyinSearch.Convert("鲸鱼挂件");
        Assert.Contains("jingyu", full);
        Assert.Equal("jygj", initials);
    }

    [Fact]
    public void Convert_MixedTextKeepsAscii()
    {
        var (full, initials) = PinyinSearch.Convert("DSH 鲸鱼 pet");
        Assert.Contains("dsh", full);
        Assert.Contains("jingyu", full);
        Assert.Contains("pet", full);
        Assert.Equal("jy", initials); // 只有汉字进首字母
    }

    [Theory]
    [InlineData("jinyu", true)]
    [InlineData("jy", true)]
    [InlineData("whale", true)]
    [InlineData("鲸鱼", false)]
    [InlineData("a", false)]
    [InlineData("j-y", true)]
    public void LooksLikePinyinQuery(string text, bool expected) =>
        Assert.Equal(expected, PinyinSearch.LooksLikePinyinQuery(text));

    static MarketPinyinIndex.Entry E(string name, string pinyin, string initials, double pop) => new()
    {
        Name = name,
        Pinyin = pinyin,
        Initials = initials,
        Popularity = pop,
    };

    [Fact]
    public void Rank_FullPinyinBeatsInitials()
    {
        var whale = E("dsh-whale-widget", "dshjingyuguajian", "dw", 10);
        var random = E("other", "qitachajian", "jy", 999); // 仅首字母命中
        var rows = MarketPinyinIndex.Rank([random, whale], "jingyu");
        Assert.Equal("dsh-whale-widget", rows[0].Name);
    }

    [Fact]
    public void Rank_NoMatchReturnsEmpty() =>
        Assert.Empty(MarketPinyinIndex.Rank([E("a", "bbb", "b", 1)], "zzz"));
}
