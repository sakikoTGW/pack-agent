namespace Pad.Core;

/// <summary>启动 / 停止 / 运行账本。窗口、CLI、MCP 共用这一份。</summary>
public interface ILaunchService
{
    Instance? Find(string instanceId);
    RuntimeFile Runtime();
    RunEntry? Running(string instanceId, string? profile = null);
    LaunchResult Launch(Instance inst, string profile);
    void Stop(string instanceId, string profile);
}
