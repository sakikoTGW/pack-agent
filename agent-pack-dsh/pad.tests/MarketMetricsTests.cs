using System.Text.Json;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>Growth math for the store leaderboards: windows, npm range parsing, star history
/// flattening and repository slug parsing. Network is not touched here.</summary>
public sealed class MarketMetricsTests
{
    static Dictionary<DateOnly, long> Series(params long[] values)
    {
        var map = new Dictionary<DateOnly, long>();
        var start = new DateOnly(2026, 10, 1);
        for (var i = 0; i < values.Length; i++) map[start.AddDays(i)] = values[i];
        return map;
    }

    [Fact]
    public void WindowGrowth_DayComparesLastTwoDays()
    {
        var daily = Series(10, 20, 35);
        var end = new DateOnly(2026, 10, 3);
        var (growth, total) = MarketMetrics.WindowGrowth(daily, 1, end);
        Assert.Equal(15, growth);   // 35 - 20
        Assert.Equal(35, total);
    }

    [Fact]
    public void WindowGrowth_WeekComparesAdjacentWeeks()
    {
        var values = Enumerable.Repeat(10L, 14).ToArray();
        values[^1] = 30;             // last day jumps
        var daily = Series(values);
        var end = new DateOnly(2026, 10, 14);
        var (growth, total) = MarketMetrics.WindowGrowth(daily, 7, end);
        Assert.Equal(20, growth);   // current 90 vs previous 70
        Assert.Equal(90, total);
    }

    [Fact]
    public void WindowGrowth_MonthUsesThirtyDayWindows()
    {
        var values = Enumerable.Repeat(1L, 60).ToArray();
        for (var i = 30; i < 60; i++) values[i] = 2; // current 30 days doubled
        var daily = Series(values);
        var (growth, total) = MarketMetrics.WindowGrowth(daily, 30, null);
        Assert.Equal(30, growth);  // 60 - 30
        Assert.Equal(60, total);
    }

    [Fact]
    public void WindowGrowth_IsZeroForEmptySeries() =>
        Assert.Equal((0L, 0L), MarketMetrics.WindowGrowth(new Dictionary<DateOnly, long>(), 7, null));

    [Fact]
    public void NpmRangeDays_ParsesDayBuckets()
    {
        using var doc = JsonDocument.Parse("""
            {"start":"2026-10-01","end":"2026-10-03","package":"x","downloads":[
              {"downloads":10,"day":"2026-10-01"},
              {"downloads":20,"day":"2026-10-02"},
              {"downloads":30,"day":"2026-10-03"}
            ]}
            """);
        var map = MarketMetrics.NpmRangeDays(doc.RootElement);
        Assert.Equal(3, map.Count);
        Assert.Equal(20, map[new DateOnly(2026, 10, 2)]);
    }

    [Fact]
    public void StarHistoryDays_FlattensWeeklyBuckets()
    {
        // 2026-10-25 is a Sunday; GitHub groups Sunday..Saturday.
        var week = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        using var doc = JsonDocument.Parse(
            "[{\"week\":" + week + ",\"total\":3,\"days\":[0,1,0,0,0,2,0]}]");
        var map = MarketMetrics.StarHistoryDays(doc.RootElement);
        Assert.Equal(7, map.Count); // every day of the week is present, zeros included
        Assert.Equal(1, map[new DateOnly(2026, 10, 26)]);
        Assert.Equal(2, map[new DateOnly(2026, 10, 30)]);
        Assert.Equal(0, map[new DateOnly(2026, 10, 27)]);
    }

    [Theory]
    [InlineData("https://github.com/o/r", "o/r")]
    [InlineData("https://github.com/o/r.git", "o/r")]
    [InlineData("git+https://github.com/o/r", "o/r")]
    [InlineData("github:o/r", "o/r")]
    [InlineData("https://github.com/o/r/tree/main/pkg", "o/r")]
    [InlineData("o/r", "o/r")]
    [InlineData("", null)]
    [InlineData("nonsense", null)]
    public void ParseRepoSlug_HandlesNpmRepositoryForms(string raw, string? expected) =>
        Assert.Equal(expected, MarketMetrics.ParseRepoSlug(raw));
}
