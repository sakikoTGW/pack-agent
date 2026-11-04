namespace Pad.Core;

/// <summary>
/// CLI 版的最小 AppState：只提供 Launcher 与 Settings。
/// 窗口版的 AppState 带 WPF Dispatcher / 定时器 / 视图状态，CLI 不需要；
/// 这就是 PAD.Core 收敛前，两份代码唯一的差异点之一。
/// </summary>
public sealed class AppState
{
    public static AppState Current { get; } = new();

    public Launcher Launcher { get; } = Launcher.Default();

    PadSettings? _settings;

    public PadSettings Settings => _settings ??= Launcher.Settings();
}
