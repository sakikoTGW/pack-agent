using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace Pad.Core;

/// <summary>One launchable row: an instance's profile, the counterpart of a PCL version.</summary>
public sealed class ProfileVm(Instance inst, DshProfile profile) : Observable
{
    public Instance Instance { get; } = inst;
    public DshProfile Profile { get; } = profile;

    public string Name => Profile.Name;
    public string InstanceName => Instance.Name;
    public string TargetText => $"{InstanceName} · {Name}";
    public string Release => Instance.Dsh.Version;

    public string Kind => Profile.HasTui ? "终端" : Profile.IsWeb ? "网页" : "自定义";

    /// <summary>Sort key so 终端 sits above 网页, like PCL cards.</summary>
    public int GroupOrder => Profile.HasTui ? 0 : Profile.IsWeb ? 1 : 2;

    /// <summary>What this profile actually is, stated in DSH's own words.</summary>
    public string Summary => $"{Kind} · {Release}";

    public string Badge => Profile.HasTui ? "TUI" : Profile.IsWeb ? "WEB" : Name.Length > 0
        ? Name[..1].ToUpperInvariant() : "?";

    public bool Manageable => Profile.HasApiProxy;

    bool _running;
    public bool Running { get => _running; set => Set(ref _running, value); }
}

/// <summary>A live run PAD started.</summary>
public sealed class RunVm(RunEntry entry, string instanceName) : Observable
{
    public RunEntry Entry { get; } = entry;
    public string InstanceName { get; } = instanceName;
    public string Profile => Entry.Profile;
    public int Pid => Entry.Pid;

    public string Uptime
    {
        get
        {
            if (!DateTimeOffset.TryParse(Entry.StartedAt, out var started)) return "";
            var d = DateTimeOffset.UtcNow - started;
            if (d.TotalHours >= 1) return $"{(int)d.TotalHours} 小时 {d.Minutes} 分";
            if (d.TotalMinutes >= 1) return $"{(int)d.TotalMinutes} 分";
            return $"{(int)d.TotalSeconds} 秒";
        }
    }

    int _agents = -2;
    /// <summary>Live agent count; -2 = not probed yet, -1 = side door unreachable.</summary>
    public int Agents { get => _agents; set { if (Set(ref _agents, value)) Raise(nameof(AgentText)); } }

    int _sessions;
    public int Sessions { get => _sessions; set { if (Set(ref _sessions, value)) Raise(nameof(AgentText)); } }

    bool _keyMissing;
    public bool KeyMissing { get => _keyMissing; set { if (Set(ref _keyMissing, value)) Raise(nameof(AgentText)); } }

    public string AgentText => KeyMissing
        ? "缺 DEEPSEEK_API_KEY"
        : Agents switch
    {
        -2 => "正在探测管理口",
        -1 => "管理口未连上",
        0 when Sessions > 0 => $"{Sessions} 条 session · 都空转",
        0 => "没有 session",
        _ => $"{Agents} 个 agent 在跑 · 共 {Sessions} 条 session",
    };

    public void Tick() { Raise(nameof(Uptime)); }
}

/// <summary>One <c>@deepseek-ai/dsh</c> release from the npm packument.</summary>
public sealed class NpmRelease : Observable
{
    public string Version { get; init; } = "";
    public string TimeText { get; init; } = "";
    public bool Latest { get; init; }
    public bool HasTime => TimeText.Length > 0;

    bool _installed;
    public bool Installed
    {
        get => _installed;
        set { if (Set(ref _installed, value)) Raise(nameof(CanInstall)); }
    }

    public bool CanInstall => !Installed;
}

/// <summary>
/// Shared application state. Views bind to this; nothing rebuilds markup by hand.
/// </summary>
public sealed class AppState : Observable
{
    public static AppState Current { get; } = new();

    public Launcher Launcher { get; private set; }
    public Runner Runner { get; private set; }
    public PadSettings Settings { get; private set; }

    public ObservableCollection<Instance> Instances { get; } = [];
    public ObservableCollection<ProfileVm> Launchables { get; } = [];
    public ObservableCollection<RunVm> Running { get; } = [];
    public ObservableCollection<DshRelease> Releases { get; } = [];
    public ObservableCollection<NpmRelease> RemoteReleases { get; } = [];
    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<PackRow> PackRows { get; } = [];

    readonly DispatcherTimer _timer;

    AppState()
    {
        Launcher = Launcher.Default();
        Runner = new Runner(Launcher);
        Settings = Launcher.Settings();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshRunning();
        LoadRoots();
        var switched = PreferSharedRoot();
        _timer.Start();
        if (!switched) Reload();
    }

    Instance? _selectedInstance;
    public Instance? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (!Set(ref _selectedInstance, value)) return;
            PackRows.Clear();
            RaisePackMatch();
            RebuildLaunchables();
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionTitle));
            Raise(nameof(SelectionDetail));
            RaiseStep();
        }
    }

    ProfileVm? _selectedProfile;
    public ProfileVm? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (!Set(ref _selectedProfile, value)) return;
            Raise(nameof(CanLaunch));
            Raise(nameof(SelectionTitle));
            Raise(nameof(SelectionDetail));
            RaiseStep();
        }
    }

    public bool HasSelection => SelectedInstance is not null;
    public bool CanLaunch => SelectedInstance is not null && SelectedProfile is not null;
    public bool NeedsSetup => Instances.Count == 0;
    public bool HasInstanceNoProfile => SelectedInstance is not null && SelectedProfile is null;

    public bool CanOfferAdopt
    {
        get
        {
            var home = LaunchPolicy.DefaultDshHome(Settings.Launch.DshHome);
            return LaunchPolicy.OfferAdopt(
                DshSniff.LooksLikeHome(home),
                Launcher.HomeAlreadyAdopted(home),
                Settings.Other.AdoptDismissed);
        }
    }

    /// <summary>Which page the step button and a click on a disabled launch should open.</summary>
    public string StepTarget =>
        Releases.Count == 0 && DshSniff.Bin() is null ? "download" : "versions";

    void RaiseStep()
    {
        Raise(nameof(NeedsSetup));
        Raise(nameof(HasInstanceNoProfile));
        Raise(nameof(CanLaunch));
        Raise(nameof(StepTarget));
        Raise(nameof(CanOfferAdopt));
    }

    /// <summary>The line under the launch button — PCL puts the version name there.</summary>
    public string SelectionTitle => SelectedProfile is null
        ? (SelectedInstance?.Name ?? "还没有实例")
        : $"{SelectedProfile.InstanceName} · {SelectedProfile.Name}";

    public string SelectionDetail => SelectedInstance is null
        ? "到版本选择里新建一个"
        : SelectedProfile is null
            ? $"{SelectedInstance.Dsh.Version} · 未找到可用的版本"
            : SelectedProfile.Summary;

    public void ReloadReleases()
    {
        Releases.Clear();
        foreach (var r in Launcher.Releases()) Releases.Add(r);
        MarkRemoteInstalled();
        Raise(nameof(HasReleases));
        Raise(nameof(NoReleases));
        Raise(nameof(HasRemoteReleases));
        Raise(nameof(ShowReleaseEmpty));
        RaiseStep();
    }

    public bool HasReleases => Releases.Count > 0;
    public bool NoReleases => Releases.Count == 0;
    public bool HasRemoteReleases => RemoteReleases.Count > 0;
    public bool ShowReleaseEmpty => Releases.Count == 0 && RemoteReleases.Count == 0;

    public void SetRemoteReleases(IEnumerable<NpmRelease> rows)
    {
        RemoteReleases.Clear();
        foreach (var r in rows) RemoteReleases.Add(r);
        MarkRemoteInstalled();
        Raise(nameof(HasRemoteReleases));
        Raise(nameof(ShowReleaseEmpty));
    }

    void MarkRemoteInstalled()
    {
        var have = Releases.Select(r => r.Version).ToHashSet(StringComparer.Ordinal);
        foreach (var r in RemoteReleases) r.Installed = have.Contains(r.Version);
    }

    public void Reload()
    {
        ReloadReleases();
        var wanted = SelectedInstance?.Id ?? Settings.LastInstance;
        Instances.Clear();
        foreach (var i in Launcher.Instances()) Instances.Add(i);

        var pick = Instances.FirstOrDefault(i => i.Id == wanted) ?? Instances.FirstOrDefault();
        _selectedInstance = pick;
        Raise(nameof(SelectedInstance));
        Raise(nameof(HasSelection));
        PackRows.Clear();
        RaisePackMatch();
        RebuildLaunchables();
        RefreshRunning();
        RaiseStep();
    }

    void RebuildLaunchables()
    {
        Launchables.Clear();
        var inst = SelectedInstance;
        if (inst is null)
        {
            SelectedProfile = null;
            Raise(nameof(SelectionTitle));
            Raise(nameof(SelectionDetail));
            return;
        }
        foreach (var p in Launcher.Profiles(inst)) Launchables.Add(new ProfileVm(inst, p));

        var wanted = inst.LastProfile ?? Settings.LastProfile;
        SelectedProfile = Launchables.FirstOrDefault(p => p.Name == wanted) ?? Launchables.FirstOrDefault();
        MarkRunning();
        Raise(nameof(SelectionTitle));
        Raise(nameof(SelectionDetail));
    }

    public void RefreshRunning()
    {
        var live = Launcher.Runtime().Entries;

        for (var i = Running.Count - 1; i >= 0; i--)
        {
            var still = live.Any(e => e.Instance == Running[i].Entry.Instance && e.Profile == Running[i].Profile);
            if (!still) Running.RemoveAt(i);
        }
        foreach (var e in live)
        {
            var known = Running.FirstOrDefault(r => r.Entry.Instance == e.Instance && r.Profile == e.Profile);
            if (known is null)
            {
                var name = Instances.FirstOrDefault(i => i.Id == e.Instance)?.Name ?? e.Instance;
                Running.Add(new RunVm(e, name));
            }
        }
        foreach (var r in Running) r.Tick();
        MarkRunning();
        Raise(nameof(AnyRunning));
        Raise(nameof(NoneRunning));
        _ = ProbeAgents();
    }

    bool _probing;

    /// <summary>
    /// Ask each live Harness how many of its sessions are actually running. Fire and
    /// forget: the launch page must never block on a socket, and a stale count is
    /// better than a frozen window.
    /// </summary>
    async Task ProbeAgents()
    {
        if (_probing) return;
        _probing = true;
        try
        {
            foreach (var run in Running.ToList())
            {
                var inst = Instances.FirstOrDefault(i => i.Id == run.Entry.Instance);
                if (inst is null) continue;
                var ad = Gateway.ReadAd(inst.Home);
                if (ad is null || !Proc.Alive(ad.Pid)) { run.Agents = -1; continue; }
                try
                {
                    var sessions = await Gateway.Sessions(ad, CancellationToken.None);
                    run.Sessions = sessions.Count;
                    run.Agents = sessions.Count(s => s.Running);
                    var key = await Gateway.DeepseekConfigured(ad, CancellationToken.None);
                    if (key is bool configured) run.KeyMissing = !configured;
                }
                catch
                {
                    run.Agents = -1;
                }
            }
        }
        finally
        {
            _probing = false;
        }
    }

    void MarkRunning()
    {
        foreach (var p in Launchables)
            p.Running = Running.Any(r => r.Entry.Instance == p.Instance.Id && r.Profile == p.Name);
    }

    public bool AnyRunning => Running.Count > 0;
    public bool NoneRunning => Running.Count == 0;

    public int EnabledPackCount => PackRows.Count(p => p.Enabled);

    public string PackSummary
    {
        get
        {
            var on = PackRows.Where(p => p.Enabled).Select(p => p.Title).ToList();
            if (on.Count == 0) return "无整合包";
            if (on.Count <= 2) return string.Join(" · ", on);
            return $"{on.Count} 个整合包";
        }
    }

    public async Task ReloadPacks()
    {
        PackRows.Clear();
        RaisePackMatch();
        var inst = SelectedInstance;
        if (inst is null) return;
        var packs = new Packs(Launcher);
        if (packs.Unavailable() is not null)
        {
            RaisePackMatch();
            return;
        }
        try
        {
            foreach (var row in await packs.List(inst, CancellationToken.None))
                PackRows.Add(row);
        }
        catch
        {
            PackRows.Clear();
        }
        RaisePackMatch();
    }

    void RaisePackMatch()
    {
        Raise(nameof(PackSummary));
        Raise(nameof(EnabledPackCount));
    }

    public void SaveSelection()
    {
        Settings.LastInstance = SelectedInstance?.Id;
        Settings.LastProfile = SelectedProfile?.Name;
        Launcher.SaveSettings(Settings);
    }

    public void ApplySettings(PadSettings s)
    {
        s.Normalize();
        Settings = s;
        Launcher.SaveSettings(s);
        Views.Fx.SetAnimate(s.Ui.Animate);
        Views.Fx.SetSpeed(s.Ui.AnimSpeed / 100.0);
        Theme.Apply(s.Ui);
        Raise(nameof(Settings));
        SyncRefreshTimer();
    }

    /// <summary>Apply UI prefs that must land before the first frame paints.</summary>
    public void ApplyUiBoot()
    {
        Settings.Normalize();
        Views.Fx.SetAnimate(Settings.Ui.Animate);
        Views.Fx.SetSpeed(Settings.Ui.AnimSpeed / 100.0);
        Theme.PrepareMutable();
        Theme.Apply(Settings.Ui);
        SyncRefreshTimer();
    }

    void SyncRefreshTimer()
    {
        _timer.Interval = TimeSpan.FromSeconds(Settings.Other.RefreshSec);
    }

    // ---- multiple launcher roots -----------------------------------------

    string? _lastRoot;

    static string RootsFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "pack-agent-dsh", "roots.json");

    void LoadRoots()
    {
        Roots.Clear();
        _lastRoot = null;
        try
        {
            if (System.IO.File.Exists(RootsFile))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(RootsFile));
                if (doc.RootElement.TryGetProperty("last", out var lastEl))
                {
                    var last = lastEl.GetString();
                    if (!string.IsNullOrWhiteSpace(last))
                        _lastRoot = System.IO.Path.GetFullPath(last);
                }
                if (doc.RootElement.TryGetProperty("roots", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var p = el.GetString();
                        if (string.IsNullOrWhiteSpace(p)) continue;
                        var abs = System.IO.Path.GetFullPath(p);
                        if (!Roots.Contains(abs)) Roots.Add(abs);
                    }
                }
            }
        }
        catch { /* a damaged roots file must not brick startup */ }

        var current = System.IO.Path.GetFullPath(Launcher.Root);
        if (DshSniff.CountInstances(current) > 0 && !Roots.Contains(current))
            Roots.Add(current);
    }

    /// <summary>
    /// Empty exe-side <c>.pack-launcher</c> uses the machine roster / sniffed
    /// roots so two PAD folders share instances. Skips when
    /// <c>PACK_LAUNCHER_ROOT</c> is set.
    /// </summary>
    bool PreferSharedRoot()
    {
        var envPinned = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("PACK_LAUNCHER_ROOT"));
        var local = Launcher.Root;
        var localN = DshSniff.CountInstances(local);
        var lastN = string.IsNullOrWhiteSpace(_lastRoot) ? 0 : DshSniff.CountInstances(_lastRoot);
        var sniffed = new List<(string Path, int Instances)>();
        if (!string.IsNullOrWhiteSpace(_lastRoot))
            sniffed.Add((_lastRoot, lastN));
        foreach (var p in Roots)
            sniffed.Add((p, DshSniff.CountInstances(p)));
        foreach (var p in DshSniff.LauncherRootCandidates())
        {
            var n = DshSniff.CountInstances(p);
            sniffed.Add((p, n));
            if (n > 0 && !Roots.Contains(p)) Roots.Add(p);
        }
        var pick = LaunchPolicy.PickBootRoot(envPinned, local, localN, _lastRoot, lastN, sniffed);
        if (pick is not null && !LaunchPolicy.SamePath(pick, local))
        {
            SwitchRoot(pick);
            return true;
        }
        RememberCurrentIfUseful();
        return false;
    }

    void RememberCurrentIfUseful()
    {
        var abs = System.IO.Path.GetFullPath(Launcher.Root);
        if (DshSniff.CountInstances(abs) > 0)
        {
            if (!Roots.Contains(abs)) Roots.Add(abs);
            _lastRoot = abs;
        }
        PersistRoots();
        Raise(nameof(Roots));
    }

    void PersistRoots()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RootsFile)!);
            var json = System.Text.Json.JsonSerializer.Serialize(
                new { schema = "pack-agent.pad.roots/v1", last = _lastRoot, roots = Roots.ToList() },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(RootsFile, json, new System.Text.UTF8Encoding(false));
        }
        catch { /* best effort */ }
    }

    /// <summary>Add a root to the roster without switching to it.</summary>
    public void AddRoot(string root)
    {
        var abs = System.IO.Path.GetFullPath(root);
        if (!System.IO.Directory.Exists(abs)) return;
        if (!Roots.Contains(abs)) Roots.Add(abs);
        PersistRoots();
        Raise(nameof(Roots));
    }

    /// <summary>Create a fresh root (empty versions/ + instances/) and switch to it.</summary>
    public void NewRoot(string root)
    {
        var abs = System.IO.Path.GetFullPath(root);
        Directory.CreateDirectory(abs);
        new Launcher(abs); // its constructor creates versions/ + instances/
        SwitchRoot(abs);
    }

    /// <summary>Point the whole app at another launcher root and reload all state.</summary>
    public void SwitchRoot(string root)
    {
        var abs = System.IO.Path.GetFullPath(root);
        if (string.Equals(abs, Launcher.Root, System.StringComparison.OrdinalIgnoreCase)) return;

        Launcher = new Launcher(abs);
        Runner = new Runner(Launcher);
        Settings = Launcher.Settings();
        if (!Roots.Contains(abs)) Roots.Add(abs);
        if (DshSniff.CountInstances(abs) > 0) _lastRoot = abs;
        PersistRoots();

        Raise(nameof(Settings));
        Raise(nameof(Roots));
        ApplyUiBoot();
        Reload();
    }
}
