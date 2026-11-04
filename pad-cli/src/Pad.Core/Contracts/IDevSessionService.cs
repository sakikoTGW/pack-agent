using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Pad.Core;

/// <summary>开发会话：开 / 应用改动 / 验证 / 固化 / 丢弃 / 历史。</summary>
public interface IDevSessionService
{
    DevSession Open(Instance inst, string profile);
    DevSession? Load(string instanceId);
    IReadOnlyList<DevSession> History(string instanceId);
    Task<DevSession> ApplyAsync(Instance inst, string profile, string action, string spec,
        IProgress<string>? log, CancellationToken ct);
    Task<DevVerdict> VerifyAsync(Instance inst, string profile, IProgress<string>? log,
        CancellationToken ct);
    DevSession Commit(Instance inst, bool dryRun, bool force, IProgress<string>? log);
    Task<DevSession> DiscardAsync(Instance inst, IProgress<string>? log, CancellationToken ct);
}
