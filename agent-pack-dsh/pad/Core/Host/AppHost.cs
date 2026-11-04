namespace Pad.Core;

/// <summary>
/// 唯一装配点：窗口、CLI、MCP 都从这里取接口。顺序即启动顺序，Dispose 逆序回收。
/// 新模块只允许在这里被点名（new），其它地方只认接口。
/// </summary>
public sealed class AppHost : System.IDisposable
{
    AppHost(Launcher launcher)
    {
        Launcher = launcher;
        Context = new PadContext();
    }

    public Launcher Launcher { get; }
    public PadContext Context { get; }

    public static AppHost Boot(Launcher? launcher = null)
    {
        var host = new AppHost(launcher ?? Launcher.Default());
        host.Context.Provide<ISettingsService>(new SettingsModule(host.Launcher));
        host.Context.Provide<ILaunchService>(new LaunchModule(host.Launcher));
        host.Context.Provide<ICheckpointService>(new CheckpointModule(host.Launcher));
        host.Context.Provide<IDevSessionService>(new DevModule(host.Launcher));
        return host;
    }

    public void Dispose() => Context.Dispose();
}
