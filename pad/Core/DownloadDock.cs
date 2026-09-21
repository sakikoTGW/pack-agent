namespace Pad.Core;

/// <summary>
/// 下载页「下载任务」：右边正在下载的 / 下载完成的。只列装发行号和 dsh plugin add|update|remove。
/// 数据仍是 jobs.json。下载货架左栏只放分类，分类底下一项进这一页。
/// </summary>
public static class DownloadDock
{
    static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
    {
        "plugin-add", "plugin-update", "plugin-remove", "install-version",
    };

    public static bool IsDownload(JobRecord job) => Kinds.Contains(job.Kind);

    public static IReadOnlyList<JobRecord> Running(IEnumerable<JobRecord> jobs) =>
        RunningAll(jobs.Where(IsDownload));

    public static IReadOnlyList<JobRecord> Done(IEnumerable<JobRecord> jobs, int take = 12) =>
        DoneAll(jobs.Where(IsDownload), take);

    public static IReadOnlyList<JobRecord> RunningAll(IEnumerable<JobRecord> jobs) =>
        jobs.Where(j => j.CanCancel)
            .OrderByDescending(j => j.UpdatedAt)
            .ToList();

    public static IReadOnlyList<JobRecord> DoneAll(IEnumerable<JobRecord> jobs, int take = 40) =>
        jobs.Where(j => j.Status is "done" or "failed" or "cancelled")
            .OrderByDescending(j => j.UpdatedAt)
            .Take(take)
            .ToList();

    /// <summary>Chrome/Edge：进行中的排最前，其余按时间新到旧，可搜名称。</summary>
    public static IReadOnlyList<JobRecord> ChromeList(IReadOnlyList<JobRecord> jobs, string query)
    {
        var head = LaunchPolicy.PickJob(jobs, j => j.Status, j => j.UpdatedAt);
        IEnumerable<JobRecord> seq = jobs;
        if (head is not null)
            seq = new[] { head }.Concat(jobs.Where(j => j.Id != head.Id)
                .OrderByDescending(j => j.UpdatedAt));
        var q = query.Trim();
        if (q.Length > 0)
            seq = seq.Where(j =>
                j.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || j.KindText.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (j.Instance ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        return seq.ToList();
    }

    public static string Title(JobRecord job) => job.Title;

    public static string SpeedText(JobRecord job, DateTimeOffset now)
    {
        if (job.Status != "running") return job.StatusText;
        if (!DateTimeOffset.TryParse(job.CreatedAt, out var start)) return $"{job.Percent:0}%";
        var sec = Math.Max(0.8, (now - start).TotalSeconds);
        var rate = job.Percent / sec;
        return $"{job.Percent:0}% · {rate:0.0}%/s";
    }

    public static DockItem Item(JobRecord job, DateTimeOffset now) => new()
    {
        Title = Title(job),
        Line = SpeedText(job, now),
    };
}

public sealed class DockItem
{
    public string Title { get; init; } = "";
    public string Line { get; init; } = "";
}

/// <summary>One row on the Chrome-style downloads list.</summary>
public sealed class JobCard
{
    public JobRecord Job { get; init; } = null!;
    public string Title => Job.Title;
    public string StatusText => Job.StatusText;
    public double Percent => Job.Percent;
    public bool CanCancel => Job.CanCancel;
    public bool CanForget => !Job.CanCancel;
    public bool ShowMeter => Job.CanCancel;
    public string Speed => DownloadDock.SpeedText(Job, DateTimeOffset.UtcNow);
    public string Line
    {
        get
        {
            if (CanCancel) return Speed;
            var when = Market.RelTime(Job.UpdatedAt);
            var bits = new List<string> { Job.KindText, Job.StatusText };
            if (when.Length > 0) bits.Add(when);
            return string.Join(" · ", bits);
        }
    }
    public string Monogram
    {
        get
        {
            foreach (var ch in Title)
                if (char.IsAsciiLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
            return "D";
        }
    }
}
