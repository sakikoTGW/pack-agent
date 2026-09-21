using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace Pad.Core;

/// <summary>
/// Window-side decisions that must stay the same in the CLI and the WPF shell.
/// Kept free of process and disk writes so the rules can be read in one place.
/// </summary>
public static class LaunchPolicy
{
    public static string DefaultDshHome(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured.Trim());
        var env = Environment.GetEnvironmentVariable("DSH_HOME");
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(env.Trim());
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dsh");
    }

    public static bool IsPackDrop(string path)
    {
        var n = Path.GetFileName(path)?.ToLowerInvariant() ?? "";
        return n.EndsWith(".pack.zip") || n.EndsWith(".pinst.zip") || n.EndsWith(".pack.json");
    }

    /// <summary>PCL sidecar: zip next to the exe, not nested, not <c>.pack.json</c>.</summary>
    public static bool IsSidecarZip(string path)
    {
        var n = Path.GetFileName(path)?.ToLowerInvariant() ?? "";
        return n.EndsWith(".pack.zip") || n.EndsWith(".pinst.zip");
    }

    /// <summary>
    /// Files sitting beside the exe that still need to move into the launcher root
    /// so scan-drop can import them. Same directory → nothing to collect.
    /// </summary>
    public static IReadOnlyList<string> SidecarZips(string exeDir, string launcherRoot)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(exeDir) || !Directory.Exists(exeDir)) return list;
        if (!string.IsNullOrWhiteSpace(launcherRoot) && SamePath(exeDir, launcherRoot)) return list;
        foreach (var file in Directory.EnumerateFiles(exeDir))
        {
            if (!IsSidecarZip(file)) continue;
            if (!string.IsNullOrWhiteSpace(launcherRoot))
            {
                var dest = Path.Combine(launcherRoot, Path.GetFileName(file));
                if (File.Exists(dest)) continue;
            }
            list.Add(file);
        }
        return list;
    }

    public static IReadOnlyList<string> PackPaths(IDataObject data)
    {
        var list = new List<string>();
        if (!data.GetDataPresent(DataFormats.FileDrop)) return list;
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return list;
        foreach (var f in files)
            if (IsPackDrop(f)) list.Add(f);
        return list;
    }

    public static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    public static IReadOnlyList<string> ImagePaths(IDataObject data)
    {
        var list = new List<string>();
        if (!data.GetDataPresent(DataFormats.FileDrop)) return list;
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return list;
        foreach (var f in files)
            if (IsImage(f)) list.Add(f);
        return list;
    }

    /// <summary>Empty machine or no selection: import creates the instance. A selected instance gets project.</summary>
    public static bool DropUsesImport(bool hasSelectedInstance) => !hasSelectedInstance;

    public static bool OfferAdopt(bool homeExists, bool already, bool dismissed) =>
        homeExists && !already && !dismissed;

    /// <summary>
    /// A folder is a launcher root when it already holds instances, a version
    /// store, or <c>pad.json</c>. Empty directories are not.
    /// </summary>
    public static bool LooksLikeLauncherRoot(int instanceRows, int versionRows, bool hasPadJson) =>
        instanceRows > 0 || versionRows > 0 || hasPadJson;

    /// <summary>
    /// Which launcher root a newly opened PAD should use. Explicit
    /// <c>PACK_LAUNCHER_ROOT</c> and a portable kit that already has instances
    /// stay put. An empty exe-side root yields to the last used root, else the
    /// sniffed root with the most instances. Does not adopt <c>~/.dsh</c>.
    /// </summary>
    public static string? PickBootRoot(
        bool envPinned,
        string localRoot,
        int localInstances,
        string? last,
        int lastInstances,
        IEnumerable<(string Path, int Instances)> sniffed)
    {
        if (envPinned) return null;
        if (localInstances > 0) return null;
        if (!string.IsNullOrWhiteSpace(last) && lastInstances > 0
            && !SafeSamePath(last, localRoot))
            return last;
        string? best = null;
        var n = 0;
        foreach (var s in sniffed)
        {
            if (s.Instances <= 0 || string.IsNullOrWhiteSpace(s.Path)) continue;
            if (SafeSamePath(s.Path, localRoot)) continue;
            if (s.Instances > n)
            {
                n = s.Instances;
                best = s.Path;
            }
        }
        return best;
    }

    static bool SafeSamePath(string a, string b)
    {
        try { return SamePath(a, b); }
        catch { return false; }
    }

    /// <summary>Only web prints a browser URL. TUI must never wait for one.</summary>
    public static bool WaitForWebReady(bool isWeb) => isWeb;

    public static string? ParseWebUrl(string text)
    {
        foreach (var raw in text.Split(['\r', '\n']))
        {
            var line = raw.Trim();
            var mark = "dsh web: ";
            var i = line.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var url = line[(i + mark.Length)..].Trim();
            var cut = url.IndexOfAny([' ', '\t']);
            if (cut > 0) url = url[..cut];
            url = url.TrimEnd('.', ',', ';');
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
        }
        return null;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// pnpm records the packer machine's <c>.pnpm</c> in <c>.modules.yaml</c>.
    /// After the kit is copied, that line must name this copy or
    /// <c>dsh plugin add</c> dies with ERR_PNPM_UNEXPECTED_VIRTUAL_STORE.
    /// </summary>
    public static string PatchVirtualStoreDir(string yaml, string virtualStoreDir)
    {
        var line = "virtualStoreDir: " + virtualStoreDir;
        if (string.IsNullOrEmpty(yaml)) return line + "\n";
        var stripped = Regex.Replace(yaml, @"^virtualStoreDir: .*$(\r?\n)?", "", RegexOptions.Multiline);
        if (Regex.IsMatch(stripped, @"^virtualStoreDirMaxLength:", RegexOptions.Multiline))
            return Regex.Replace(stripped, @"^virtualStoreDirMaxLength:",
                line + "\nvirtualStoreDirMaxLength:", RegexOptions.Multiline);
        return stripped.TrimEnd() + "\n" + line + "\n";
    }

    /// <summary>
    /// Owned instances live under <c>instances/id/</c>. After the kit folder is
    /// copied, home/workspace must follow this copy, not the build machine.
    /// Adopted homes stay where they were.
    /// </summary>
    public static bool RelocateOwned(
        bool adopted, string home, string workspaceKind, string workspacePath,
        string instanceDir, bool homeExists, bool workspaceExists,
        out string nextHome, out string nextWorkspace)
    {
        nextHome = home;
        nextWorkspace = workspacePath;
        if (adopted || !homeExists) return false;
        var wantHome = Path.Combine(instanceDir, "home");
        var wantWs = Path.Combine(instanceDir, "workspace");
        var homeMoved = string.IsNullOrWhiteSpace(home) || !SamePath(home, wantHome);
        var wsMoved = workspaceKind == "owned" && workspaceExists
            && (string.IsNullOrWhiteSpace(workspacePath) || !SamePath(workspacePath, wantWs));
        if (!homeMoved && !wsMoved) return false;
        nextHome = wantHome;
        if (workspaceKind == "owned" && workspaceExists) nextWorkspace = wantWs;
        return true;
    }

    public static string LaunchToast(string name, bool web) =>
        web ? $"{name} 正在打开网页" : $"{name} 已在终端里跑起来";

    /// <summary>
    /// <c>wt -w 0 nt</c> exits as soon as the tab is handed off. PAD must not
    /// drop the run in that window: keep it while a Harness pid is found, or
    /// for a short grace after launch so the first refresh does not lie.
    /// </summary>
    public const int RunStartGraceSec = 45;

    public static bool KeepTrackedRun(bool launcherPidAlive, bool harnessAlive,
        DateTimeOffset started, DateTimeOffset now)
    {
        if (launcherPidAlive || harnessAlive) return true;
        return now - started < TimeSpan.FromSeconds(RunStartGraceSec);
    }

    /// <summary>
    /// A node command line that is this profile's DSH. Token-bounded so
    /// <c>dsh-tui</c> does not match <c>dsh-tui-extra</c>.
    /// </summary>
    public static bool CommandLineIsDsh(string? commandLine, string profile, string? bin)
    {
        if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(profile))
            return false;
        if (!HasProfileToken(commandLine, profile)) return false;
        if (!string.IsNullOrWhiteSpace(bin)
            && commandLine.Contains(bin, StringComparison.OrdinalIgnoreCase))
            return true;
        return commandLine.Contains("bin.js", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("@deepseek-ai\\dsh", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("@deepseek-ai/dsh", StringComparison.OrdinalIgnoreCase);
    }

    static bool HasProfileToken(string commandLine, string profile)
    {
        var marks = new[] { "--profile " + profile, "--profile=" + profile };
        foreach (var mark in marks)
        {
            var start = 0;
            while (true)
            {
                var i = commandLine.IndexOf(mark, start, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                var after = i + mark.Length;
                if (after >= commandLine.Length) return true;
                var ch = commandLine[after];
                if (ch is ' ' or '"' or '\'' or '\t') return true;
                start = i + 1;
            }
        }
        return false;
    }

    /// <summary>Task page: the live job, else the one that moved last. Oldest-first lists must not auto-select the first failure forever.</summary>
    public static T? PickJob<T>(IReadOnlyList<T> jobs, Func<T, string> status, Func<T, string> updatedAt)
    {
        if (jobs.Count == 0) return default;
        for (var i = jobs.Count - 1; i >= 0; i--)
            if (status(jobs[i]) == "running") return jobs[i];
        T? best = jobs[0];
        var bestAt = updatedAt(jobs[0]);
        for (var i = 1; i < jobs.Count; i++)
        {
            if (string.CompareOrdinal(updatedAt(jobs[i]), bestAt) > 0)
            {
                best = jobs[i];
                bestAt = updatedAt(jobs[i]);
            }
        }
        return best;
    }

    public static bool PathUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        var a = Path.GetFullPath(path).TrimEnd('\\', '/');
        var b = Path.GetFullPath(root).TrimEnd('\\', '/');
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Owned-instance scripts resolve files from <c>%PAD_ROOT%</c>, not the packer drive.</summary>
    public static string? CmdRelativeToRoot(string absPath, string launcherRoot)
    {
        if (!PathUnder(absPath, launcherRoot)) return null;
        var rel = Path.GetRelativePath(Path.GetFullPath(launcherRoot), Path.GetFullPath(absPath));
        return "%PAD_ROOT%" + rel.Replace('/', '\\');
    }

    public const string CmdPadInst = @"set ""PAD_INST=%~dp0""";
    public const string CmdPadRoot = @"set ""PAD_ROOT=%PAD_INST%..\..\""";
    public const string CmdOwnedDshHome = @"set ""DSH_HOME=%PAD_INST%home""";
    public const string CmdOwnedWorkspace = @"cd /d ""%PAD_INST%workspace""";
    /// <summary>
    /// 7z -snl turns the DSH profiles/node_modules junction into an empty
    /// directory. A leftover real tree double-loads persona. Remove it without
    /// following inner junctions into the version library.
    /// </summary>
    public const string CmdClearProfilesFallback =
        """
        if exist "%DSH_HOME%\profiles\node_modules\" rmdir "%DSH_HOME%\profiles\node_modules" 2>nul
        if exist "%DSH_HOME%\profiles\node_modules\" powershell -NoProfile -ExecutionPolicy Bypass -Command "function D([string]$p){ if(-not [IO.Directory]::Exists($p) -and -not [IO.File]::Exists($p)){ return }; $a=[IO.File]::GetAttributes($p); if(($a -band [IO.FileAttributes]::ReparsePoint) -ne 0){ if(($a -band [IO.FileAttributes]::Directory) -ne 0){ [IO.Directory]::Delete($p) } else { [IO.File]::Delete($p) }; return }; if(($a -band [IO.FileAttributes]::Directory) -ne 0){ foreach($c in [IO.Directory]::EnumerateFileSystemEntries($p)){ D $c }; [IO.Directory]::Delete($p) } else { [IO.File]::Delete($p) } }; D (Join-Path $env:DSH_HOME 'profiles\node_modules')"
        """;
    /// <summary>
    /// Cursor / npm leftover NODE_OPTIONS=--preserve-symlinks makes Windows
    /// junctions load @deepseek-ai/dsh-scope twice; persona then collides.
    /// </summary>
    public const string CmdCleanNodeEnv =
        """
        set "NODE_OPTIONS="
        set "NODE_PATH="
        set "NODE_PRESERVE_SYMLINKS="
        """;


    public static string CmdSetDshHome(bool adopted, string absHome) =>
        adopted ? $"set \"DSH_HOME={absHome}\"" : CmdOwnedDshHome;

    public static string CmdCdWorkspace(bool adopted, string workspaceKind, string absWorkspace) =>
        !adopted && workspaceKind == "owned" ? CmdOwnedWorkspace : $"cd /d \"{absWorkspace}\"";

    public static bool IsSecretEnvKey(string key) =>
        string.Equals(key, CredentialsFile.DeepseekRef, StringComparison.Ordinal);

    public static string StripSecretEnv(string? text)
    {
        var kept = new List<string>();
        foreach (var (key, val) in LaunchPrefs.ParseEnv(text))
        {
            if (IsSecretEnvKey(key)) continue;
            kept.Add($"{key}={val}");
        }
        return string.Join("\n", kept);
    }

    /// <summary>runtime.json is PAD's own TrackRun. Ready means the Harness node pid exists.</summary>
    public static bool LaunchHarnessReady(bool harnessAlive) => harnessAlive;

    public static bool LaunchWaitGiveUp(DateTimeOffset started, DateTimeOffset now) =>
        now - started >= TimeSpan.FromSeconds(RunStartGraceSec);

    public const int LaunchWaitPollMs = 250;

    public static bool ShouldSkipDuplicateLaunch(bool harnessAlive) => harnessAlive;

    /// <summary>
    /// FDD / <c>dotnet build -o</c> leaves the assembly dll beside the exe.
    /// The assembly name is <c>pack-agent-for DSH</c>, not <c>Pad</c>.
    /// </summary>
    public static bool IsFrameworkDependentHost(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return true;
        var dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(dir)) return true;
        var stem = Path.GetFileNameWithoutExtension(exePath);
        return File.Exists(Path.Combine(dir, "Pad.dll"))
            || (!string.IsNullOrWhiteSpace(stem)
                && File.Exists(Path.Combine(dir, stem + ".dll")));
    }

    public static string? ResolvePortableHost(string? processPath, IReadOnlyList<string> candidates)
    {
        if (!string.IsNullOrWhiteSpace(processPath)
            && File.Exists(processPath)
            && !IsFrameworkDependentHost(processPath))
            return Path.GetFullPath(processPath);
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c) || !File.Exists(c)) continue;
            if (IsFrameworkDependentHost(c)) continue;
            return Path.GetFullPath(c);
        }
        return null;
    }

    public static readonly string[] PortableEphemeral = ["pad-cli.log", "runtime.json", "jobs.json"];

    /// <summary>npm spec without the trailing @version. Scoped names keep the leading @.</summary>
    public static string BundleBareName(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return spec;
        var s = spec.Trim();
        if (s[0] == '@')
        {
            var slash = s.IndexOf('/');
            if (slash < 0) return s;
            var at = s.IndexOf('@', slash + 1);
            return at < 0 ? s : s[..at];
        }
        var at2 = s.IndexOf('@');
        return at2 < 0 ? s : s[..at2];
    }

    /// <summary>
    /// Label for the manage page. TUI and the management port are still
    /// <c>dsh.profile.bundles</c> rows; the label only tells a person which row they are.
    /// </summary>
    public static string BundleKind(string spec)
    {
        var s = spec.ToLowerInvariant();
        if (s.Contains("dsh-tui")) return "TUI";
        if (s.Contains("pad-gateway") || s.Contains("host-apiproxy")) return "管理口";
        return "组合包";
    }

    /// <summary>
    /// Profile-template layers, TUI, and the management port. These stay on
    /// the manage page as 组合包. Version-settings 插件 lists everything else.
    /// </summary>
    public static bool IsProfileLayer(string spec)
    {
        var s = (spec ?? "").ToLowerInvariant();
        return s.Contains("dsh-base") || s.Contains("dsh-web-app") || s.Contains("dsh-headless")
            || s.Contains("dsh-tui") || s.Contains("pad-gateway") || s.Contains("host-apiproxy");
    }

    public static string BundlePackageDir(string profileDir, string spec) =>
        Path.Combine(profileDir, "node_modules",
            BundleBareName(spec).Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Files a package may ship as its mark. TUI publishes
    /// <c>docs/assets/logo.svg</c> on GitHub; the npm tarball often omits <c>docs/</c>.
    /// </summary>
    public static readonly string[] BundleIconFiles =
    [
        "icon.png", "icon.jpg", "icon.webp", "logo.png", "logo.svg",
        "docs/assets/logo.png", "docs/assets/logo.svg",
    ];

    public static string? GithubRepo(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) return null;
        var s = repository.Trim();
        if (s.StartsWith("git+", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        var mark = "github.com";
        var i = s.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        s = s[(i + mark.Length)..].TrimStart('/', ':');
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var user = parts[0];
        var repo = parts[1];
        var cut = repo.IndexOfAny(['.', '?', '#']);
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        else if (cut > 0) repo = repo[..cut];
        return user + "/" + repo;
    }

    public static IReadOnlyList<string> BundleIconUrls(string? repository, string? version)
    {
        var repo = GithubRepo(repository);
        if (repo is null) return [];
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(version))
        {
            var v = version.Trim();
            tags.Add(v);
            if (v.Length > 0 && v[0] is not 'v' and not 'V') tags.Add("v" + v);
        }
        tags.Add("main");
        var files = new[] { "docs/assets/logo.svg", "docs/assets/logo.png" };
        var urls = new List<string>();
        foreach (var tag in tags)
            foreach (var file in files)
            {
                urls.Add($"https://cdn.jsdelivr.net/gh/{repo}@{tag}/{file}");
                urls.Add($"https://raw.githubusercontent.com/{repo}/{tag}/{file}");
            }
        return urls;
    }

    /// <summary>Instance launch override: skip the global <c>nodePath</c> and pick runtime/PATH.</summary>
    public const string NodeAuto = "auto";

    public static bool IsNodeAuto(string? path) =>
        string.Equals(path?.Trim(), NodeAuto, StringComparison.OrdinalIgnoreCase);

    public static bool NodeIsCustom(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !IsNodeAuto(path);

    /// <summary>
    /// Combo tag: empty = follow global, <see cref="NodeAuto"/> = auto, <c>custom</c> = a path.
    /// Global settings have no follow option; empty there is auto.
    /// </summary>
    public static string NodeKindTag(string? stored, bool instance) =>
        string.IsNullOrWhiteSpace(stored) ? (instance ? "" : "auto")
        : IsNodeAuto(stored) ? NodeAuto
        : "custom";

    /// <summary>
    /// Path the user pinned. <see cref="NodeAuto"/> and empty mean “search”, not a file named auto.
    /// Instance auto ignores a global custom path.
    /// </summary>
    public static string? EffectiveNodePath(string? instance, string? global)
    {
        var a = string.IsNullOrWhiteSpace(instance) ? null : instance.Trim();
        if (IsNodeAuto(a)) return null;
        if (a is not null) return a;
        var g = string.IsNullOrWhiteSpace(global) ? null : global.Trim();
        if (g is null || IsNodeAuto(g)) return null;
        return g;
    }

    public static string? NodeFileVersion(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            var v = info.ProductVersion ?? info.FileVersion;
            if (string.IsNullOrWhiteSpace(v)) return null;
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus].Trim() : v.Trim();
        }
        catch { return null; }
    }

    public static string DescribeNodeWillUse(string? exe, string? engines)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return string.IsNullOrWhiteSpace(engines)
                ? "找不到 Node。勾选「发行号缺 Node 时装进 runtime/node」，启动时会下载。"
                : $"找不到 Node {engines}。勾选「发行号缺 Node 时装进 runtime/node」，启动时会下载。";
        }
        var label = string.IsNullOrWhiteSpace(engines) ? "node" : engines.Trim();
        return $"将会使用：Node {label}: {exe}";
    }
}
