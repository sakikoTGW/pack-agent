using System.Collections.Generic;

namespace Pad.Core;

/// <summary>存档点：创建 / 列出 / 回退 / 显式清理。清理永不自动发生。</summary>
public interface ICheckpointService
{
    Checkpoint Create(Instance inst, string? profile, string trigger);
    IReadOnlyList<Checkpoint> List(string instanceId);
    Checkpoint? Latest(string instanceId);
    Checkpoint? LatestBeforeChange(string instanceId);
    Checkpoint Restore(Instance inst, string checkpointId);
    IReadOnlyList<string> Prune(string instanceId, int keep);
}
