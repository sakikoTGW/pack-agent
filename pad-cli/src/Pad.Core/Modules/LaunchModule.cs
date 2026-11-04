using System.Linq;

namespace Pad.Core;

sealed class LaunchModule(Launcher launcher) : ILaunchService
{
    readonly Runner _runner = new(launcher);

    public Instance? Find(string instanceId) => launcher.Instance(instanceId);

    public RuntimeFile Runtime() => launcher.Runtime();

    public RunEntry? Running(string instanceId, string? profile = null) =>
        launcher.Runtime().Entries.FirstOrDefault(e =>
            e.Instance == instanceId
            && (profile is null || e.Profile == profile)
            && Proc.Alive(e.Pid));

    public LaunchResult Launch(Instance inst, string profile) =>
        _runner.Launch(inst, profile, launcher.Settings());

    public void Stop(string instanceId, string profile) => _runner.Stop(instanceId, profile);
}
