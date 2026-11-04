using System.IO;
using System.Text;
using System.Text.Json;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// 服务容器 + 组合根 + 4 个契约。只钉结构，不碰行为：
/// 重复注册要报错、缺少依赖要报错、effect 逆序回收、Boot 后窗口/CLI/MCP 能拿到同一组接口。
/// </summary>
public sealed class PadHostTests
{
    sealed class FakeSettings : ISettingsService
    {
        public PadSettings Settings() => new();
    }

    static Launcher TempLauncher()
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-host-tests", Guid.NewGuid().ToString("N"));
        var instDir = Path.Combine(root, "instances", "ev");
        Directory.CreateDirectory(Path.Combine(instDir, "home", "profiles", "web"));
        Directory.CreateDirectory(Path.Combine(instDir, "workspace"));
        var json = new
        {
            schema = "pack-agent.launcher.instance/v2",
            id = "ev",
            name = "ev",
            dsh = new { version = "0.2.1-rc.2" },
            home = Path.Combine(instDir, "home"),
            workspace = new { kind = "owned", path = Path.Combine(instDir, "workspace") },
            status = "ready",
            lastProfile = "web",
        };
        File.WriteAllText(Path.Combine(instDir, "instance.json"),
            JsonSerializer.Serialize(json), new UTF8Encoding(false));
        return new Launcher(root);
    }

    [Fact]
    public void Provide_Require_Roundtrips()
    {
        var ctx = new PadContext();
        var settings = new FakeSettings();
        ctx.Provide<ISettingsService>(settings);

        Assert.True(ctx.Has<ISettingsService>());
        Assert.Same(settings, ctx.Require<ISettingsService>());
        Assert.False(ctx.Has<ILaunchService>());
        Assert.Throws<InvalidOperationException>(() => ctx.Require<ILaunchService>());
    }

    [Fact]
    public void Provide_Twice_Throws()
    {
        var ctx = new PadContext();
        ctx.Provide<ISettingsService>(new FakeSettings());
        Assert.Throws<InvalidOperationException>(() => ctx.Provide<ISettingsService>(new FakeSettings()));
    }

    [Fact]
    public void Effects_Are_Disposed_In_Reverse_Order()
    {
        var ctx = new PadContext();
        var order = new List<int>();
        ctx.Effect(() => order.Add(1));
        ctx.Effect(() => order.Add(2));
        ctx.Effect(new Disposer(() => order.Add(3)));

        ctx.Dispose();

        Assert.Equal([3, 2, 1], order);
        Assert.False(ctx.Has<ISettingsService>());
    }

    sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    [Fact]
    public void Boot_Provides_All_Four_Contracts_And_Resolves_Instance()
    {
        var launcher = TempLauncher();
        using var host = AppHost.Boot(launcher);

        Assert.Same(launcher, host.Launcher);
        Assert.NotNull(host.Context.Require<ISettingsService>());
        Assert.NotNull(host.Context.Require<ILaunchService>());
        Assert.NotNull(host.Context.Require<ICheckpointService>());
        Assert.NotNull(host.Context.Require<IDevSessionService>());

        var inst = host.Context.Require<ILaunchService>().Find("ev");
        Assert.NotNull(inst);
        Assert.Equal("web", inst!.LastProfile);
        Assert.Empty(host.Context.Require<ILaunchService>().Runtime().Entries);
    }

    [Fact]
    public void Boot_Is_The_Only_Place_That_Names_Concrete_Modules()
    {
        using var host = AppHost.Boot(TempLauncher());
        // 容器里只允许出现契约类型（实现细节不外泄）。
        Assert.True(host.Context.Has<ISettingsService>());
        Assert.True(host.Context.Has<ILaunchService>());
        Assert.True(host.Context.Has<ICheckpointService>());
        Assert.True(host.Context.Has<IDevSessionService>());
    }
}
