using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>商店搜索的本地相关性：精确命中优先于 npm 原顺序，且不改变“无命中”的原顺序。</summary>
public sealed class MarketSearchTests
{
    static MarketRow Row(string name, string desc = "", params string[] keywords) => new()
    {
        Name = name,
        Description = desc,
        Keywords = [.. keywords],
        Publisher = "tester",
    };

    [Fact]
    public void QueryScore_NameBeatsDescription()
    {
        var nameHit = Row("dshmarket");
        var descHit = Row("other", "a visual dshmarket wrapper");
        Assert.True(Market.QueryScore(nameHit, "dshmarket") > Market.QueryScore(descHit, "dshmarket"));
    }

    [Fact]
    public void QueryScore_IgnoresHyphenCaseAndScope()
    {
        var row = Row("@scope/dsh-whale-widget");
        Assert.True(Market.QueryScore(row, "dsh whale widget") > 0);
        Assert.True(Market.QueryScore(row, "DSH-WHALE-WIDGET") > 0);
    }

    [Fact]
    public void QueryScore_NoMatchIsZero() =>
        Assert.Equal(0, Market.QueryScore(Row("dsh-pet", "desktop pet"), "量子计算"));

    [Fact]
    public void RankForQuery_PutsHitsFirstAndKeepsOrderOfEquals()
    {
        var a = Row("alpha", "unrelated");
        var b = Row("dsh-whale-widget", "鲸鱼挂件");
        var c = Row("beta", "also unrelated");
        var ranked = Market.RankForQuery([a, b, c], "whale");
        Assert.Equal("dsh-whale-widget", ranked[0].Name);
        Assert.Equal(["alpha", "beta"], ranked.Skip(1).Select(r => r.Name));
    }

    [Fact]
    public void RankForQuery_BlankQueryKeepsInputOrder()
    {
        var rows = new List<MarketRow> { Row("b"), Row("a") };
        Assert.Equal(["b", "a"], Market.RankForQuery(rows, "   ").Select(r => r.Name));
    }
}
