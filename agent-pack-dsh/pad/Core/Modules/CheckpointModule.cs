using System.Collections.Generic;

namespace Pad.Core;

sealed class CheckpointModule(Launcher launcher) : ICheckpointService
{
    public Checkpoint Create(Instance inst, string? profile, string trigger) =>
        Checkpoints.Create(launcher, inst, profile, trigger);

    public IReadOnlyList<Checkpoint> List(string instanceId) =>
        Checkpoints.List(launcher, instanceId);

    public Checkpoint? Latest(string instanceId) => Checkpoints.Latest(launcher, instanceId);

    public Checkpoint? LatestBeforeChange(string instanceId) =>
        Checkpoints.LatestBeforeChange(launcher, instanceId);

    public Checkpoint Restore(Instance inst, string checkpointId) =>
        Checkpoints.Restore(launcher, inst, checkpointId);

    public IReadOnlyList<string> Prune(string instanceId, int keep) =>
        Checkpoints.Prune(launcher, instanceId, keep);
}
