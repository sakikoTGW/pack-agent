using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pad.Core;

sealed class DevModule(Launcher launcher) : IDevSessionService
{
    public DevSession Open(Instance inst, string profile) => DevSessions.Open(launcher, inst, profile);

    public DevSession? Load(string instanceId) => DevSessions.Load(launcher, instanceId);

    public IReadOnlyList<DevSession> History(string instanceId) =>
        DevSessions.History(launcher, instanceId);

    public Task<DevSession> ApplyAsync(Instance inst, string profile, string action, string spec,
        IProgress<string>? log, CancellationToken ct) =>
        DevOps.ApplyAsync(launcher, inst, profile, action, spec, log, ct);

    public Task<DevVerdict> VerifyAsync(Instance inst, string profile, IProgress<string>? log,
        CancellationToken ct) =>
        DevOps.VerifyAsync(launcher, inst, profile, log, ct);

    public DevSession Commit(Instance inst, bool dryRun, bool force, IProgress<string>? log) =>
        DevOps.Commit(launcher, inst, dryRun, force, log);

    public Task<DevSession> DiscardAsync(Instance inst, IProgress<string>? log, CancellationToken ct) =>
        DevOps.DiscardAsync(launcher, inst, log, ct);
}
