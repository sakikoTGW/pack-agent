namespace Pad.Core;

/// <summary>
/// CLI 版的最小 RunVm：RunFacts 只需要 Profile 与「有没有 PAD 启动记录」。
/// 窗口版的 RunVm 带显示用字段（Uptime、标题等），在 AppState.cs 里。
/// </summary>
public sealed class RunVm
{
    public string Profile { get; init; } = "";

    public static RunVm? FromEntry(Nodes.Running? entry) =>
        entry is null ? null : new RunVm { Profile = entry.Profile };
}
