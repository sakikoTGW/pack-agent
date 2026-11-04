using System.Collections.Generic;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// surface 不是宿主模式，而是 profile 里装了哪个界面 bundle：web-app 是浏览器界面，
/// dsh-tui 是终端界面。两个一起装只能是「混合」，不能替用户挑一个。
/// </summary>
public sealed class SurfaceBundleTests
{
    static ProfileVm Row(string profileName, params string[] bundles) =>
        new(new Instance { Id = "t", Name = "t", Home = "C:\\tmp" },
            new DshProfile { Name = profileName, Bundles = [.. bundles] });

    [Fact]
    public void WebAppBundle_IsWebSurface()
    {
        var row = Row("web", "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app");
        Assert.Equal(["web"], row.Profile.UiSurfaces);
        Assert.Equal("网页", row.Kind);
    }

    [Fact]
    public void TuiBundle_IsTerminalSurface()
    {
        var row = Row("dsh-tui", "@deepseek-ai/dsh-base", "@deepseek-harness-tui/dsh-tui");
        Assert.Equal(["tui"], row.Profile.UiSurfaces);
        Assert.Equal("终端", row.Kind);
    }

    [Fact]
    public void BothBundles_OffersBothSurfaces()
    {
        var row = Row("web", "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app", "@deepseek-harness-tui/dsh-tui");
        Assert.Equal(["web", "tui"], row.Profile.UiSurfaces);
        // 行首只写这次实际会启动的那个 bundle；两个都装时按 web 优先。\r\n        Assert.Equal("网页", row.Kind);
    }

    [Fact]
    public void NoUiBundle_IsCustom()
    {
        var row = Row("headless", "@deepseek-ai/dsh-base");
        Assert.Empty(row.Profile.UiSurfaces);
        Assert.Equal("自定义", row.Kind);
    }

    [Fact]
    public void ScopedTuiPackageStillCounts()
    {
        var row = Row("pad-op-tui", "@deepseek-harness-tui/dsh-tui@0.9.3");
        Assert.Equal(["tui"], row.Profile.UiSurfaces);
    }
}
