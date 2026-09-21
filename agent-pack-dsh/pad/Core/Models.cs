using System.Text.Json.Serialization;

namespace Pad.Core;

// ---- display metadata (instance.json display object) ---------------------

/// <summary>Star / category / logo / info on an instance, the counterpart of
/// PCL's version display fields.</summary>
public sealed class InstanceDisplay
{
    [JsonPropertyName("info")] public string Info { get; set; } = "";
    [JsonPropertyName("intro")] public string Intro { get; set; } = "";
    [JsonPropertyName("logo")] public string? Logo { get; set; }
    [JsonPropertyName("star")] public bool Star { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
}

// ---- agent-preset roster -------------------------------------------------

public enum PresetTrust { System, User }

/// <summary>One agent-preset row on an instance. System = shipped with the
/// pinned release; User = copied to the instance home's .agent-presets.</summary>
public sealed class AgentPresetRow
{
    public string Id { get; set; } = "";
    public PresetTrust Trust { get; set; }
    public string Path { get; set; } = "";
    public string? Broken { get; set; }

    public string TrustText => Trust == PresetTrust.System ? "随附" : "用户层";
    public bool CanRemove => Trust == PresetTrust.User;
}

// ---- job queue -----------------------------------------------------------

/// <summary>One task in the job queue (jobs.json). Mirrors the TS JobRecord.</summary>
public sealed class JobRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("instance")] public string? Instance { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "queued";
    [JsonPropertyName("pid")] public int? Pid { get; set; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("leaves")] public List<JobLeaf>? Leaves { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }

    public string StatusText => Status switch
    {
        "running" => "进行中",
        "done" => "完成",
        "failed" => "失败",
        "cancelled" => "已取消",
        _ => "排队中",
    };

    public string KindText => Kind switch
    {
        "install-version" => "装发行号",
        "clone" => "克隆实例",
        "import" => "装整合包",
        "project" => "投影整合包",
        "plugin-add" => "装组合包",
        "plugin-remove" => "移除组合包",
        "plugin-update" => "更新组合包",
        _ => Kind,
    };

    public double Percent
    {
        get
        {
            if (Leaves is null || Leaves.Count == 0)
                return Status == "done" ? 100 : 0;
            var total = Leaves.Sum(l => l.Weight);
            if (total == 0) return 0;
            var acc = Leaves.Sum(l => l.Weight * (l.Total > 0 ? (double)l.Done / l.Total : 0));
            return acc / total * 100;
        }
    }

    public bool CanCancel => Status is "queued" or "running";

    public string Title => string.IsNullOrWhiteSpace(Label) ? KindText : Label;
}

public sealed class JobLeaf
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("weight")] public int Weight { get; set; }
    [JsonPropertyName("done")] public int Done { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}

public sealed class JobsFile
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.launcher.jobs/v1";
    [JsonPropertyName("jobs")] public List<JobRecord> Jobs { get; set; } = [];
}

// ---- credentials ---------------------------------------------------------

/// <summary>A credential set name. "global" is library/credentials.yaml.</summary>
public sealed class CredentialSet
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

// ---- doctor report (subset) ----------------------------------------------

public sealed class DoctorReport
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("root")] public string Root { get; set; } = "";
    [JsonPropertyName("node")] public string Node { get; set; } = "";
    [JsonPropertyName("pnpm")] public string? Pnpm { get; set; }
    [JsonPropertyName("writable")] public bool Writable { get; set; }
    [JsonPropertyName("primitives")] public List<string> Primitives { get; set; } = [];
    [JsonPropertyName("registry")] public DoctorRegistry Registry { get; set; } = new();
    [JsonPropertyName("warnings")] public List<DoctorWarning> Warnings { get; set; } = [];
    [JsonPropertyName("errors")] public List<DoctorError> Errors { get; set; } = [];
}

public sealed class DoctorRegistry
{
    [JsonPropertyName("names")] public List<string> Names { get; set; } = [];
}

public sealed class DoctorWarning
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class DoctorError
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("location")] public string Location { get; set; } = "";
}

/// <summary>A pinned <c>@deepseek-ai/dsh</c> release in the version store.</summary>
public sealed class DshRelease
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.launcher.version/v1";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("verifyOutput")] public string VerifyOutput { get; set; } = "";
    [JsonPropertyName("installedAt")] public string InstalledAt { get; set; } = "";
    /// <summary>x.y.z parsed from this release's <c>engines.node</c>. Empty = unknown.</summary>
    [JsonPropertyName("enginesNode")] public string? EnginesNode { get; set; }
    /// <summary>Absolute override. Empty = <c>versions/&lt;ver&gt;/node_modules/@deepseek-ai/dsh/lib/bin.js</c>.</summary>
    [JsonPropertyName("bin")] public string? Bin { get; set; }
}

public sealed class InstanceWorkspace
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "owned";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

public sealed class InstanceDsh
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
}

/// <summary>
/// Per-instance launch overrides stored in <c>instance.json</c>. A missing or blank
/// field means "follow the global <c>pad.json</c> launch knob", like PCL's per-version
/// <c>PCL\Setup.ini</c>.
/// </summary>
public sealed class InstanceLaunch
{
    /// <summary>Empty = global <c>{instance} · {profile}</c>.</summary>
    [JsonPropertyName("title")] public string? Title { get; set; }
    /// <summary>keep | minimize | hide. Empty = global.</summary>
    [JsonPropertyName("afterLaunch")] public string? AfterLaunch { get; set; }
    /// <summary>Appended after global extraEnv; a repeated key overrides the global one.</summary>
    [JsonPropertyName("extraEnv")] public string? ExtraEnv { get; set; }
    [JsonPropertyName("pauseOnError")] public bool? PauseOnError { get; set; }
    [JsonPropertyName("telemetryDisabled")] public bool? TelemetryDisabled { get; set; }
    /// <summary>Empty = the global nodePath, then <c>node</c> on PATH.</summary>
    [JsonPropertyName("nodePath")] public string? NodePath { get; set; }
    /// <summary>One command line run before dsh. Empty = none.</summary>
    [JsonPropertyName("preCommand")] public string? PreCommand { get; set; }
    /// <summary>Whether the launch script waits for preCommand to finish.</summary>
    [JsonPropertyName("preCommandWait")] public bool? PreCommandWait { get; set; }
    /// <summary>Which credential file to use. Empty = global default; "instance" = this
    /// home's own <c>.credentials.yaml</c>; anything else = <c>library/credentials/&lt;name&gt;.yaml</c>.</summary>
    [JsonPropertyName("credentialsSet")] public string? CredentialsSet { get; set; }
}

/// <summary>
/// One isolated <c>DSH_HOME</c> — the counterpart of a <c>.minecraft</c> folder.
/// The profile roster is not stored here; it is discovered from
/// <c>$DSH_HOME/profiles/*/package.json</c> so PAD and DSH cannot disagree.
/// </summary>
public sealed class Instance
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.launcher.instance/v2";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("dsh")] public InstanceDsh Dsh { get; set; } = new();
    [JsonPropertyName("home")] public string Home { get; set; } = "";
    [JsonPropertyName("workspace")] public InstanceWorkspace Workspace { get; set; } = new();
    [JsonPropertyName("status")] public string Status { get; set; } = "ready";
    [JsonPropertyName("lastProfile")] public string? LastProfile { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
    [JsonPropertyName("display")] public InstanceDisplay? Display { get; set; }
    [JsonPropertyName("launch")] public InstanceLaunch? Launch { get; set; }
    [JsonPropertyName("adopted")] public bool Adopted { get; set; }
    [JsonPropertyName("adoptedFingerprint")] public string? AdoptedFingerprint { get; set; }
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
}

/// <summary>
/// One <c>profiles/&lt;name&gt;</c> directory: the launchable unit, counterpart of a
/// PCL version folder. <see cref="Bundles"/> mirrors <c>dsh.profile.bundles</c>.
/// </summary>
public sealed class DshProfile
{
    public string Name { get; init; } = "";
    public string Dir { get; init; } = "";
    public List<string> Bundles { get; init; } = [];

    /// <summary>Whether the PAD management side door can be reached on this profile.</summary>
    public bool HasApiProxy => Bundles.Any(b =>
        b.Contains("host-apiproxy", StringComparison.OrdinalIgnoreCase)
        || b.Contains("pad-gateway", StringComparison.OrdinalIgnoreCase));

    public bool HasTui => Bundles.Any(b => b.Contains("dsh-tui", StringComparison.OrdinalIgnoreCase));

    /// <summary>Web is the only shipped profile that binds a port and a browser URL.</summary>
    public bool IsWeb => Name == "web"
        || Bundles.Any(b => b.Contains("web-app", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One row of <c>dsh.profile.bundles</c> plus the version sitting in that
/// profile's <c>node_modules</c>. The manage page updates these without a management port.
/// </summary>
public sealed class BundleRow
{
    public string Profile { get; init; } = "";
    public string Spec { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Keywords { get; init; } = [];
    public string PackageDir { get; init; } = "";
    public string Latest { get; set; } = "";
    public string IconPath { get; set; } = "";

    public string VersionText => string.IsNullOrEmpty(Version) ? "未装到磁盘" : Version;
    public bool HasIcon => !string.IsNullOrEmpty(IconPath);
}

/// <summary>A live process PAD started, tracked in <c>runtime.json</c>.</summary>
public sealed class RunEntry
{
    [JsonPropertyName("instance")] public string Instance { get; set; } = "";
    [JsonPropertyName("profile")] public string Profile { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("controlUrl")] public string? ControlUrl { get; set; }
    [JsonPropertyName("startedAt")] public string StartedAt { get; set; } = "";
}

public sealed class RuntimeFile
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.launcher.runtime/v2";
    [JsonPropertyName("entries")] public List<RunEntry> Entries { get; set; } = [];
}

/// <summary>One transcript under <c>$DSH_HOME/sessions</c>.</summary>
public sealed class SessionRow
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public DateTime Updated { get; set; }
    public string? AgentPreset { get; set; }
    public string? Cwd { get; set; }

    public string PresetText => string.IsNullOrEmpty(AgentPreset) ? "未记录 preset" : AgentPreset!;
    public string UpdatedText => Updated.ToString("MM-dd HH:mm");
    public string Size => Bytes < 1024 ? $"{Bytes} B"
        : Bytes < 1024 * 1024 ? $"{Bytes / 1024.0:0.#} KB"
        : $"{Bytes / (1024.0 * 1024):0.#} MB";
}

public enum TerminalKind { WindowsTerminal, Conhost, Custom }

/// <summary>
/// PAD preferences at <c>&lt;launcher-root&gt;/pad.json</c>. Layered like PCL Setup:
/// each object is one settings page, so 「初始化本页」wipes only that object.
/// Defaults live here — nowhere else may invent a magic number for these knobs.
/// </summary>
public sealed class PadSettings
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.pad.settings/v4";

    [JsonPropertyName("lastInstance")] public string? LastInstance { get; set; }
    [JsonPropertyName("lastProfile")] public string? LastProfile { get; set; }

    [JsonPropertyName("terminal")] public string? TerminalLegacy { get; set; }
    [JsonPropertyName("terminalCommand")] public string? TerminalCommandLegacy { get; set; }

    [JsonPropertyName("launch")] public LaunchPrefs Launch { get; set; } = new();
    [JsonPropertyName("ui")] public UiPrefs Ui { get; set; } = new();
    [JsonPropertyName("download")] public DownloadPrefs Download { get; set; } = new();
    [JsonPropertyName("other")] public OtherPrefs Other { get; set; } = new();

    [JsonIgnore]
    public TerminalKind Kind => Launch.Terminal switch
    {
        "conhost" => TerminalKind.Conhost,
        "custom" => TerminalKind.Custom,
        _ => TerminalKind.WindowsTerminal,
    };

    [JsonIgnore]
    public string Terminal
    {
        get => Launch.Terminal;
        set => Launch.Terminal = value;
    }

    [JsonIgnore]
    public string TerminalCommand
    {
        get => Launch.TerminalCommand;
        set => Launch.TerminalCommand = value;
    }

    public void Normalize()
    {
        Launch ??= new LaunchPrefs();
        Ui ??= new UiPrefs();
        Download ??= new DownloadPrefs();
        Other ??= new OtherPrefs();

        if (!string.IsNullOrEmpty(TerminalLegacy) && Launch.Terminal == "wt" && TerminalLegacy != "wt")
            Launch.Terminal = TerminalLegacy!;
        if (!string.IsNullOrEmpty(TerminalCommandLegacy) && Launch.TerminalCommand.Length == 0)
            Launch.TerminalCommand = TerminalCommandLegacy!;
        TerminalLegacy = null;
        TerminalCommandLegacy = null;

        Schema = "pack-agent.pad.settings/v4";
        Launch.Clamp();
        Ui.Clamp();
        Download.Clamp();
        Other.Clamp();
        if (string.IsNullOrWhiteSpace(Other.Identify))
            Other.Identify = Guid.NewGuid().ToString("N")[..16];
    }
}

/// <summary>启动层。</summary>
public sealed class LaunchPrefs
{
    [JsonPropertyName("terminal")] public string Terminal { get; set; } = "wt";
    [JsonPropertyName("terminalCommand")] public string TerminalCommand { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "{instance} · {profile}";
    /// <summary>keep | minimize | hide</summary>
    [JsonPropertyName("afterLaunch")] public string AfterLaunch { get; set; } = "keep";
    [JsonPropertyName("reuseWtWindow")] public bool ReuseWtWindow { get; set; } = true;
    /// <summary>下载页「版本」框的预填；空则不预填。</summary>
    [JsonPropertyName("defaultRelease")] public string DefaultRelease { get; set; } = "0.1.0-rc.8";
    /// <summary>写入 launch-*.cmd 的额外环境变量，每行 KEY=VALUE。</summary>
    [JsonPropertyName("extraEnv")] public string ExtraEnv { get; set; } = "";
    /// <summary>dsh 非零退出时是否 pause。</summary>
    [JsonPropertyName("pauseOnError")] public bool PauseOnError { get; set; } = true;
    [JsonPropertyName("telemetryDisabled")] public bool TelemetryDisabled { get; set; } = true;
    /// <summary>Empty = DSH_HOME env or ~/.dsh.</summary>
    [JsonPropertyName("dshHome")] public string DshHome { get; set; } = "";
    /// <summary>Local folder of <c>@deepseek-harness-tui/dsh-tui</c>. Empty = sniff.</summary>
    [JsonPropertyName("tuiPackage")] public string TuiPackage { get; set; } = "";
    /// <summary>Absolute path to node.exe. Empty = <c>node</c> on PATH.</summary>
    [JsonPropertyName("nodePath")] public string NodePath { get; set; } = "";
    /// <summary>One command line run before dsh. Empty = none.</summary>
    [JsonPropertyName("preCommand")] public string PreCommand { get; set; } = "";
    /// <summary>Whether the launch script waits for preCommand to finish.</summary>
    [JsonPropertyName("preCommandWait")] public bool PreCommandWait { get; set; } = true;
    /// <summary>normal | belownormal | high. Emitted into the launch script.</summary>
    [JsonPropertyName("processPriority")] public string ProcessPriority { get; set; } = "normal";
    /// <summary>Auto-install the release's declared Node into runtime/node when missing.</summary>
    [JsonPropertyName("runtimeInstall")] public bool RuntimeInstall { get; set; } = true;
    /// <summary>Profile name used for a new instance's first plugin add.</summary>
    [JsonPropertyName("defaultProfile")] public string DefaultProfile { get; set; } = "dsh-tui";
    /// <summary>global = library/credentials.yaml; none = don't copy on launch.</summary>
    [JsonPropertyName("credentialsDefault")] public string CredentialsDefault { get; set; } = "global";
    /// <summary>owned = new instances get <c>instances/&lt;id&gt;/workspace</c>.
    /// shared = new instances point at <see cref="SharedWorkspace"/>.</summary>
    [JsonPropertyName("workspaceIndieDefault")] public string WorkspaceIndieDefault { get; set; } = "owned";
    /// <summary>Used when <see cref="WorkspaceIndieDefault"/> is <c>shared</c>. Empty = <c>&lt;root&gt;/workspace-shared</c>.</summary>
    [JsonPropertyName("sharedWorkspace")] public string SharedWorkspace { get; set; } = "";

    public void Clamp()
    {
        if (Terminal is not ("wt" or "conhost" or "custom")) Terminal = "wt";
        if (AfterLaunch is not ("keep" or "minimize" or "hide")) AfterLaunch = "keep";
        if (string.IsNullOrWhiteSpace(Title)) Title = "{instance} · {profile}";
        DefaultRelease = (DefaultRelease ?? "").Trim();
        ExtraEnv ??= "";
        DshHome = (DshHome ?? "").Trim();
        TuiPackage = (TuiPackage ?? "").Trim();
        NodePath = (NodePath ?? "").Trim();
        PreCommand = (PreCommand ?? "").Trim();
        if (ProcessPriority is not ("normal" or "belownormal" or "high")) ProcessPriority = "normal";
        DefaultProfile = (DefaultProfile ?? "").Trim();
        if (string.IsNullOrWhiteSpace(DefaultProfile)) DefaultProfile = "dsh-tui";
        if (CredentialsDefault is not ("global" or "none")) CredentialsDefault = "global";
        if (WorkspaceIndieDefault is not ("owned" or "shared")) WorkspaceIndieDefault = "owned";
        SharedWorkspace = (SharedWorkspace ?? "").Trim();
    }

    public static LaunchPrefs Defaults() => new();

    public IEnumerable<(string Key, string Value)> ParseExtraEnv() => ParseEnv(ExtraEnv);

    public static IEnumerable<(string Key, string Value)> ParseEnv(string? text)
    {
        foreach (var raw in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (key.Length == 0) continue;
            yield return (key, val);
        }
    }
}

/// <summary>个性化 + 窗口几何。主题色不是 PCL 蓝。</summary>
public sealed class UiPrefs
{
    /// <summary>teal | slate | forest | amber | ink | custom</summary>
    [JsonPropertyName("theme")] public string Theme { get; set; } = "teal";
    /// <summary>theme=custom 时的强调色，#RRGGBB。</summary>
    [JsonPropertyName("customAccent")] public string CustomAccent { get; set; } = "#278197";
    [JsonPropertyName("animate")] public bool Animate { get; set; } = true;
    [JsonPropertyName("toastMs")] public int ToastMs { get; set; } = 3600;
    [JsonPropertyName("toastMax")] public int ToastMax { get; set; } = 3;
    [JsonPropertyName("showBrand")] public bool ShowBrand { get; set; } = true;
    [JsonPropertyName("showLogo")] public bool ShowLogo { get; set; } = true;
    /// <summary>顶栏名称。空则显示 PAD。</summary>
    [JsonPropertyName("brandText")] public string BrandText { get; set; } = "PAD";
    /// <summary>动效时长倍率，百分数。100 = 默认，越大越慢。</summary>
    [JsonPropertyName("animSpeed")] public int AnimSpeed { get; set; } = 100;
    /// <summary>空 = 内建 logo；否则相对启动器根或绝对路径。</summary>
    [JsonPropertyName("logoPath")] public string LogoPath { get; set; } = "";
    /// <summary>壁纸路径；空 = 不用壁纸。相对启动器根或绝对路径。</summary>
    [JsonPropertyName("wallpaperPath")] public string WallpaperPath { get; set; } = "";
    /// <summary>壁纸不透明度 0–100。</summary>
    [JsonPropertyName("wallpaperOpacity")] public int WallpaperOpacity { get; set; } = 40;
    /// <summary>内容底洗不透明度 0–100。壁纸时压低才看得见图。</summary>
    [JsonPropertyName("contentOpacity")] public int ContentOpacity { get; set; } = 92;
    /// <summary>顶栏底不透明度 0–100。</summary>
    [JsonPropertyName("chromeOpacity")] public int ChromeOpacity { get; set; } = 96;
    /// <summary>fill | cover | contain</summary>
    [JsonPropertyName("wallpaperFit")] public string WallpaperFit { get; set; } = "cover";
    [JsonPropertyName("windowWidth")] public int WindowWidth { get; set; } = 1000;
    [JsonPropertyName("windowHeight")] public int WindowHeight { get; set; } = 620;
    [JsonPropertyName("windowMargin")] public int WindowMargin { get; set; } = 14;
    [JsonPropertyName("chromeHeight")] public int ChromeHeight { get; set; } = 54;
    /// <summary>整窗不透明度 40–100。</summary>
    [JsonPropertyName("windowOpacity")] public int WindowOpacity { get; set; } = 100;
    /// <summary>壁纸模糊半径 0–60。0 关掉。</summary>
    [JsonPropertyName("wallpaperBlur")] public int WallpaperBlur { get; set; }
    /// <summary>壁纸目录；非空则每次启动随机抽一张。</summary>
    [JsonPropertyName("wallpaperDir")] public string WallpaperDir { get; set; } = "";
    /// <summary>可藏的页：manage / download。启动和设置禁止全藏。</summary>
    [JsonPropertyName("hiddenTabs")] public List<string> HiddenTabs { get; set; } = [];
    /// <summary>藏启动页「版本选择 / 版本设置」入口。F12 临时解除到本次进程。</summary>
    [JsonPropertyName("hideVersionEntry")] public bool HideVersionEntry { get; set; }

    public void Clamp()
    {
        if (Theme is not ("teal" or "slate" or "forest" or "amber" or "ink" or "custom"))
            Theme = "teal";
        if (!LooksHex(CustomAccent)) CustomAccent = "#278197";
        if (CustomAccent.Equals("#1370f3", StringComparison.OrdinalIgnoreCase))
            CustomAccent = "#278197";
        ToastMs = Math.Clamp(ToastMs, 800, 20000);
        ToastMax = Math.Clamp(ToastMax, 1, 8);
        AnimSpeed = Math.Clamp(AnimSpeed, 50, 200);
        BrandText = (BrandText ?? "").Trim();
        if (BrandText.Length > 24) BrandText = BrandText[..24];
        WallpaperOpacity = Math.Clamp(WallpaperOpacity, 0, 100);
        ContentOpacity = Math.Clamp(ContentOpacity, 40, 100);
        ChromeOpacity = Math.Clamp(ChromeOpacity, 40, 100);
        if (WallpaperFit is not ("fill" or "cover" or "contain")) WallpaperFit = "cover";
        LogoPath = (LogoPath ?? "").Trim();
        WallpaperPath = (WallpaperPath ?? "").Trim();
        WindowWidth = Math.Clamp(WindowWidth, 880, 2400);
        WindowHeight = Math.Clamp(WindowHeight, 520, 1600);
        WindowMargin = Math.Clamp(WindowMargin, 0, 40);
        ChromeHeight = Math.Clamp(ChromeHeight, 40, 72);
        WindowOpacity = Math.Clamp(WindowOpacity, 40, 100);
        WallpaperBlur = Math.Clamp(WallpaperBlur, 0, 60);
        WallpaperDir = (WallpaperDir ?? "").Trim();
        HiddenTabs ??= [];
        HiddenTabs.RemoveAll(t => t is not ("manage" or "download"));
    }

    static bool LooksHex(string? s) =>
        s is { Length: 7 } && s[0] == '#'
        && s[1..].All(Uri.IsHexDigit);

    public static UiPrefs Defaults() => new();
}

/// <summary>一个 npm 货架：左栏一项。</summary>
public sealed class ShelfPref
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("query")] public string Query { get; set; } = "";
}

/// <summary>registry 快捷按钮。</summary>
public sealed class RegistryPreset
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

/// <summary>下载 / 市场 / 网络超时。</summary>
public sealed class DownloadPrefs
{
    [JsonPropertyName("npmRegistry")] public string NpmRegistry { get; set; } = "https://registry.npmjs.org";
    [JsonPropertyName("marketPageSize")] public int MarketPageSize { get; set; } = 20;
    [JsonPropertyName("marketTimeoutSec")] public int MarketTimeoutSec { get; set; } = 15;
    [JsonPropertyName("installTimeoutSec")] public int InstallTimeoutSec { get; set; } = 20;
    [JsonPropertyName("gatewayTimeoutSec")] public int GatewayTimeoutSec { get; set; } = 6;
    [JsonPropertyName("registryPresets")] public List<RegistryPreset> RegistryPresets { get; set; } = DefaultPresets();
    /// <summary>空列表在 Clamp 时填内建四货架；可整表覆盖。</summary>
    [JsonPropertyName("shelves")] public List<ShelfPref> Shelves { get; set; } = [];
    /// <summary>0 = pnpm 默认并发；否则写进该次 pnpm add 的环境。</summary>
    [JsonPropertyName("pnpmNetworkConcurrency")] public int PnpmNetworkConcurrency { get; set; }
    /// <summary>空 = pnpm 全局 store；非空 = 该启动器自己的 store。</summary>
    [JsonPropertyName("cacheDir")] public string CacheDir { get; set; } = "";
    /// <summary>打开下载页是否自动查 npm。默认开，对照 PCL 打开下载就获取版本列表。</summary>
    [JsonPropertyName("autoFetchOnOpen")] public bool AutoFetchOnOpen { get; set; } = true;

    public void Clamp()
    {
        if (string.IsNullOrWhiteSpace(NpmRegistry)) NpmRegistry = "https://registry.npmjs.org";
        NpmRegistry = NpmRegistry.Trim().TrimEnd('/');
        if (!Uri.TryCreate(NpmRegistry, UriKind.Absolute, out var u)
            || u.Scheme is not ("http" or "https"))
            NpmRegistry = "https://registry.npmjs.org";

        MarketPageSize = Math.Clamp(MarketPageSize, 5, 100);
        MarketTimeoutSec = Math.Clamp(MarketTimeoutSec, 3, 120);
        InstallTimeoutSec = Math.Clamp(InstallTimeoutSec, 5, 300);
        GatewayTimeoutSec = Math.Clamp(GatewayTimeoutSec, 2, 60);
        PnpmNetworkConcurrency = Math.Clamp(PnpmNetworkConcurrency, 0, 64);
        CacheDir = (CacheDir ?? "").Trim();

        RegistryPresets ??= [];
        if (RegistryPresets.Count == 0) RegistryPresets = DefaultPresets();
        foreach (var p in RegistryPresets)
        {
            p.Label = string.IsNullOrWhiteSpace(p.Label) ? p.Url : p.Label.Trim();
            p.Url = (p.Url ?? "").Trim().TrimEnd('/');
        }
        RegistryPresets.RemoveAll(p => p.Url.Length == 0
            || !Uri.TryCreate(p.Url, UriKind.Absolute, out var x)
            || x.Scheme is not ("http" or "https"));

        Shelves ??= [];
        foreach (var s in Shelves)
        {
            s.Id = (s.Id ?? "").Trim();
            s.Label = (s.Label ?? "").Trim();
            s.Query = (s.Query ?? "").Trim();
        }
        Shelves.RemoveAll(s => s.Id.Length == 0 || s.Query.Length == 0);
        if (Shelves.Count == 0) Shelves = DefaultShelves();
        foreach (var s in Shelves)
            if (s.Label.Length == 0) s.Label = s.Id;
    }

    public static List<RegistryPreset> DefaultPresets() =>
    [
        new() { Label = "官方", Url = "https://registry.npmjs.org" },
        new() { Label = "npmmirror", Url = "https://registry.npmmirror.com" },
    ];

    public static List<ShelfPref> DefaultShelves() =>
    [
        new() { Id = "plugin", Label = "插件", Query = "keywords:dsh-plugin" },
        new() { Id = "tui", Label = "终端", Query = "@deepseek-harness-tui" },
        new() { Id = "skill", Label = "技能与工具", Query = "keywords:dsh-plugin skill" },
        new() { Id = "host", Label = "宿主与官方", Query = "@deepseek-ai/dsh-host" },
    ];

    public static DownloadPrefs Defaults() => new()
    {
        RegistryPresets = DefaultPresets(),
        Shelves = DefaultShelves(),
    };
}

/// <summary>其他：刷新、路径、诊断。</summary>
public sealed class OtherPrefs
{
    [JsonPropertyName("openLogOnError")] public bool OpenLogOnError { get; set; }
    [JsonPropertyName("confirmStop")] public bool ConfirmStop { get; set; } = true;
    [JsonPropertyName("refreshSec")] public int RefreshSec { get; set; } = 2;
    [JsonPropertyName("registriesRel")] public string RegistriesRel { get; set; } = "library/registries";
    [JsonPropertyName("backupCorrupt")] public bool BackupCorrupt { get; set; } = true;
    [JsonPropertyName("shotDelayMs")] public int ShotDelayMs { get; set; } = 3200;
    [JsonPropertyName("shotMarketDelayMs")] public int ShotMarketDelayMs { get; set; } = 5200;
    /// <summary>User already said no to adopting ~/.dsh this machine.</summary>
    [JsonPropertyName("adoptDismissed")] public bool AdoptDismissed { get; set; }
    /// <summary>诊断多打一行 stderr；设置页显示 schema 与根路径。</summary>
    [JsonPropertyName("debug")] public bool Debug { get; set; }
    /// <summary>PAD 自身更新：release | off。</summary>
    [JsonPropertyName("updateChannel")] public string UpdateChannel { get; set; } = "release";
    /// <summary>Local random id, generated once at first boot. Never uploaded.</summary>
    [JsonPropertyName("identify")] public string Identify { get; set; } = "";

    /// <summary>Not persisted; set when a corrupt pad.json was recovered.</summary>
    [JsonIgnore] public string? LoadWarning { get; set; }

    public void Clamp()
    {
        RefreshSec = Math.Clamp(RefreshSec, 1, 30);
        if (string.IsNullOrWhiteSpace(RegistriesRel)) RegistriesRel = "library/registries";
        RegistriesRel = RegistriesRel.Replace('\\', '/').Trim().Trim('/');
        if (RegistriesRel.Contains("..", StringComparison.Ordinal)) RegistriesRel = "library/registries";
        ShotDelayMs = Math.Clamp(ShotDelayMs, 500, 30000);
        ShotMarketDelayMs = Math.Clamp(ShotMarketDelayMs, 500, 60000);
        if (UpdateChannel is not ("release" or "off")) UpdateChannel = "release";
    }

    public static OtherPrefs Defaults() => new();
}
