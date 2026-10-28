using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pad.Core;

/// <summary>
/// 本机 DSH 全量探测：桌面壳 / TUI / CLI / home / 内核，每条都带「是谁找到它的」。
///
/// 目标不是认这台机器，而是**装到别人电脑上也能自己找全**：安装位置不可枚举，
/// 所以问四类线索——运行中的进程、开始菜单快捷方式（含 cmd/ps1 脚本链）、卸载注册表、
/// 已知 userData；再叠加固定盘根命名启发、PAD 自己的 pad.json 与版本库、
/// 以及测试用的 <c>PAD_DISCOVER_EXTRA</c>。深度扫描只认显式 <c>--deep</c> 根。
///
/// 约束（D21/D22/D25）：
/// <list type="bullet">
/// <item>不 shell out：不跑 which / npm / wmic，进程只读 <see cref="Process"/> 元数据。</item>
/// <item>不无界遍历：每一类线索都有上限，每 ~50 项检查一次取消令牌。</item>
/// <item>单条坏数据只跳过它：进程、注册表、快捷方式、脚本、JSON 全部 try/catch。</item>
/// <item>能在 worker thread 上直接跑：任何异常都不向调用方抛（取消除外）。</item>
/// </list>
/// 返回 <see cref="Inventory"/>：Kernels / Shells / Tuis / Clis / Homes。
/// </summary>
public static class Discover
{
    // ---- 上限：全部来自 D21/D22 的性能决策 ---------------------------------

    /// <summary>进程表最多看这么多条（含 MainModule 读取）。</summary>
    const int ProcessCap = 600;

    /// <summary>开始菜单里最多解析这么多 .lnk。</summary>
    const int ShortcutCap = 800;

    /// <summary>Uninstall 注册表最多看这么多键（三个根合计）。</summary>
    const int UninstallKeyCap = 2000;

    /// <summary>HKCU\Software 直接子键最多看这么多（NSIS 按用户安装）。</summary>
    const int UserSoftwareKeyCap = 500;

    /// <summary>一个目录里最多探测这么多 *.exe。</summary>
    const int ExeCap = 40;

    /// <summary>一个目录里最多解析这么多启动脚本。</summary>
    const int ScriptCapPerDir = 20;

    /// <summary>单个脚本最多读这么多字节；脚本链最多跟 3 层。</summary>
    const int ScriptByteCap = 64 * 1024;

    const int ScriptDepthCap = 3;

    /// <summary>一个 home 最多看这么多 profile 目录。</summary>
    const int ProfileCap = 50;

    /// <summary>%APPDATA% / %LOCALAPPDATA%\Programs 的候选目录上限。</summary>
    const int UserDataCap = 200;

    /// <summary>PAD_DISCOVER_EXTRA：每个根 / 全部根最多走这么多目录。</summary>
    const int ExtraDirCapPerRoot = 1500;

    const int ExtraDirCapTotal = 4000;

    /// <summary>固定盘最多看 8 个，每盘根下直接子目录最多 400 个。</summary>
    const int DriveCap = 8;

    const int DriveChildCap = 400;

    /// <summary>--deep：深度 5、总目录 20000（与旧实现一致）。</summary>
    const int DeepDepthCap = 5;

    const int DeepDirCap = 20000;

    /// <summary>一个 home 的 profiles 目录枚举上限。</summary>
    const int ProfilesDirCap = 1000;

    /// <summary>sessions 目录计数上限。</summary>
    const int SessionsCap = 10000;

    /// <summary>package.json 读取上限（正常只有几 KB）。</summary>
    const long MaxPackageJsonBytes = 2L * 1024 * 1024;

    /// <summary>测试注入点：分号分隔的额外根（fixture 用，也方便用户手工指向）。</summary>
    public const string ExtraEnv = "PAD_DISCOVER_EXTRA";

    /// <summary>TUI 的状态目录；没有任何 profile 声明时用它兜一条。</summary>
    public const string TuiStateDirName = ".dsh-tui";

    static readonly string[] TuiHomeChildNames = ["home", "dsh-home", "harness"];

    static readonly string[] ScriptExtensions = [".cmd", ".bat", ".ps1"];

    // ---- 入口 --------------------------------------------------------------

    public static Inventory Run(Launcher launcher, IReadOnlyList<string> deepRoots, CancellationToken ct)
    {
        var scan = new Scan(launcher, ct);

        // 进程现场只取一次（PEB 读命令行/环境）：壳的 UserData/Home、TUI 与 CLI 是否在跑
        // 都只能从这儿拿到，但快照本身约 1s，绝不能在每个壳上重扫。
        Guard(scan, () => scan.Procs = ProcTable.Snapshot(900, ct));

        // 所有线索互不依赖；某一类抛了不能拖垮其它类。
        Guard(scan, () => ScanKnownLocations(scan));
        Guard(scan, () => ScanProcesses(scan));
        Guard(scan, () => ScanShortcuts(scan));
        Guard(scan, () => ScanUninstallRegistry(scan));
        Guard(scan, () => ScanLocalPrograms(scan));
        Guard(scan, () => ScanPadConfig(scan));
        Guard(scan, () => ScanExtraRoots(scan));
        Guard(scan, () => ScanDriveRoots(scan));
        Guard(scan, () => ScanDeepRoots(scan, deepRoots));

        var shells = new List<Shell>();
        var shellKernels = new List<Kernel>();
        Guard(scan, () => BuildShells(scan, shells, shellKernels));

        // home 要先于 TUI：TUI 是挂在某个 home 的 profile 上的。
        var homes = new List<DshHome>();
        Guard(scan, () => BuildHomes(scan, shells, homes));

        var tuis = new List<Tui>();
        Guard(scan, () => BuildTuis(scan, homes, tuis));

        var kernels = new List<Kernel>();
        Guard(scan, () => BuildKernels(scan, shellKernels, kernels));

        var clis = new List<DshCli>();
        Guard(scan, () => BuildClis(scan, shells, kernels, clis));

        return new Inventory(kernels, shells, tuis, clis, homes);
    }

    /// <summary>跑一段探测；取消除外一律吞掉，保证一个坏线索不会让整份清单消失。</summary>
    static void Guard(Scan scan, Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 已找到的部分保留在 scan 里
        }
    }

    // ---- 扫描状态 ----------------------------------------------------------

    sealed class Scan
    {
        public Scan(Launcher launcher, CancellationToken ct)
        {
            Launcher = launcher;
            Ct = ct;
            AppData = Folder(Environment.SpecialFolder.ApplicationData);
            LocalAppData = Folder(Environment.SpecialFolder.LocalApplicationData);
            UserProfile = Folder(Environment.SpecialFolder.UserProfile);
            ProgramData = Folder(Environment.SpecialFolder.CommonApplicationData);
            SelfExe = SafeProcessPath();
            EnvHome = ReadEnvHome();
        }

        public Launcher Launcher { get; }

        public CancellationToken Ct { get; }

        public string AppData { get; }

        public string LocalAppData { get; }

        public string UserProfile { get; }

        public string ProgramData { get; }

        /// <summary>PAD 自己的 exe：进程线索必须跳过它。</summary>
        public string? SelfExe { get; }

        /// <summary>进程环境或 HKCU\Environment 里的 DSH_HOME（已用 LooksLikeHome 校验）。</summary>
        public string? EnvHome { get; }

        /// <summary>pad.json#launch.dshCli，反推外壳与 CLI 用。</summary>
        public string? SettingsDshCli { get; set; }

        /// <summary>已发现的桌面壳候选，按 exe 完整路径去重。</summary>
        public Dictionary<string, ShellCandidate> Shells { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>正在运行的 Electron+DSH exe 路径（判断 Shell.Running）。</summary>
        public HashSet<string> RunningExes { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>正在运行的 Electron+DSH exe → 第一个 pid（快照读不到进程现场时兜底）。</summary>
        public Dictionary<string, int> RunningPids { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>进程现场快照（ProcTable/PEB）：Run 开始只取一次，全流程共用。</summary>
        public IReadOnlyList<ProcInfo> Procs { get; set; } = [];

        /// <summary>解析过的脚本，按路径去重；也用于 TUI 的 launcher 合并。</summary>
        public Dictionary<string, ScriptFacts> ScriptsByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ScriptFacts> Scripts { get; } = [];

        /// <summary>%APPDATA% 里形如 Electron userData 的候选（含 @scope 下的）。</summary>
        public List<string> UserDataCandidates { get; } = [];

        readonly HashSet<string> _userDataSeen = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>PAD_DISCOVER_EXTRA 里直接找到的内核。</summary>
        public List<Kernel> ExtraKernels { get; } = [];

        /// <summary>PAD_DISCOVER_EXTRA 各根下找到的启动脚本（给壳补 Home 用）。</summary>
        public Dictionary<string, List<ScriptFacts>> ExtraScriptsByRoot { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>home 种子（BuildHomes 再统一校验 + 计 profile/sessions）。</summary>
        public List<(string Path, string Found)> HomeSeeds { get; } = [];

        readonly HashSet<string> _homeSeen = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>asar 是否带 DSH 的判定缓存：一次探测里同一归档只判一次。</summary>
        public Dictionary<string, bool> AsarDshCache { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddHome(string? path, string found)
        {
            var full = NormalizePath(path);
            if (full is null || !Directory.Exists(full)) return;
            if (!_homeSeen.Add(full)) return;
            HomeSeeds.Add((full, found));
        }

        public void AddUserData(string? path)
        {
            var full = NormalizePath(path);
            if (full is null || !Directory.Exists(full)) return;
            if (_userDataSeen.Add(full)) UserDataCandidates.Add(full);
        }
    }

    /// <summary>一个脚本里对 DSH 有用的全部事实（含它引用的脚本）。</summary>
    sealed class ScriptFacts
    {
        public string Path { get; init; } = "";

        /// <summary>脚本链里出现过、且 LooksLikeHome 校验通过的 DSH_HOME。</summary>
        public List<string> Homes { get; } = [];

        /// <summary>--profile / --profile= 后面的 profile 名。</summary>
        public List<string> Profiles { get; } = [];

        /// <summary>npx ... @deepseek-ai/dsh@ver 这种声明。</summary>
        public List<string> Declared { get; } = [];

        /// <summary>脚本链里指向的、Electron+DSH 签名的 exe。</summary>
        public List<string> Exes { get; } = [];

        /// <summary>脚本正文里出现过 tui（用于判断“没有 profile 的 TUI 启动脚本”）。</summary>
        public bool MentionsTui { get; set; }
    }

    /// <summary>一个桌面壳候选：同一个 exe 被多条线索找到时合并事实。</summary>
    sealed class ShellCandidate
    {
        public string Exe { get; init; } = "";

        /// <summary>第一条找到它的线索，保持稳定。</summary>
        public string Found { get; set; } = "";

        /// <summary>注册表 DisplayVersion 之类的版本提示（优先级低于 FileVersionInfo）。</summary>
        public string? DisplayVersion { get; set; }

        public List<ScriptFacts> Facts { get; } = [];
    }

    // ---- 线索 1：运行中的进程 ----------------------------------------------

    /// <summary>
    /// 桌面端装在哪里不可枚举，但它运行时会说出自己的 exe（D25 的核心结论）。
    /// 只认 Electron+DSH 签名：exe 目录下有 <c>resources\app.asar</c> 且归档里有内核。
    /// </summary>
    static void ScanProcesses(Scan scan)
    {
        Process[] all;
        try
        {
            all = Process.GetProcesses();
        }
        catch
        {
            return;
        }

        var limit = Math.Min(all.Length, ProcessCap);
        for (var i = 0; i < limit; i++)
        {
            if ((i & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
            var process = all[i];
            try
            {
                string? exe = null;
                try
                {
                    exe = process.MainModule?.FileName;
                }
                catch
                {
                    // 受保护进程（SYSTEM / PPL）读不到模块：跳过它
                }
                if (string.IsNullOrWhiteSpace(exe)) continue;
                if (scan.SelfExe is not null && SamePath(exe, scan.SelfExe)) continue;

                var exeDir = Path.GetDirectoryName(exe);
                if (!IsElectronDsh(scan, exeDir)) continue;

                var full = FullPath(exe);
                if (full is null) continue;
                scan.RunningExes.Add(full);
                scan.RunningPids.TryAdd(full, process.Id);
                AddShell(scan, full, "running process", verified: true);
            }
            catch
            {
                // 单个进程读失败只跳过它
            }
            finally
            {
                try { process.Dispose(); } catch { /* 已释放 */ }
            }
        }
    }

    // ---- 线索 2：开始菜单快捷方式（含 cmd / powershell 脚本链） -------------

    /// <summary>
    /// 开始菜单的 .lnk 目标可能是 exe，也可能是 <c>cmd.exe /c "launch.cmd"</c> 或
    /// <c>powershell -File launch.ps1</c>。用反射 COM（WScript.Shell）解析，
    /// 再跟脚本链（≤3 层、每脚本 ≤64KB）找 Electron+DSH 的 exe。
    /// 用反射而不是 dynamic：不引入 Microsoft.CSharp，也不把 trim 搞坏。
    /// </summary>
    static void ScanShortcuts(Scan scan)
    {
        var links = new List<string>();
        try
        {
            var roots = new[]
            {
                Path.Combine(scan.AppData, "Microsoft", "Windows", "Start Menu", "Programs"),
                Path.Combine(scan.ProgramData, "Microsoft", "Windows", "Start Menu", "Programs"),
            };
            foreach (var root in roots)
            {
                if (links.Count >= ShortcutCap) break;
                try
                {
                    if (!Directory.Exists(root)) continue;
                    links.AddRange(Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories));
                }
                catch
                {
                    // 权限不足 / 半删目录：收多少算多少
                }
            }
        }
        catch
        {
            return;
        }
        if (links.Count == 0) return;
        if (links.Count > ShortcutCap) links.RemoveRange(ShortcutCap, links.Count - ShortcutCap);

        var comType = Type.GetTypeFromProgID("WScript.Shell");
        if (comType is null) return;
        object? com = null;
        try
        {
            com = Activator.CreateInstance(comType);
        }
        catch
        {
            com = null;
        }
        if (com is null) return;

        try
        {
            for (var i = 0; i < links.Count; i++)
            {
                if ((i & 15) == 0) scan.Ct.ThrowIfCancellationRequested();
                var linkPath = links[i];
                string? target = null;
                string? arguments = null;
                string? workingDir = null;
                try
                {
                    var shortcut = comType.InvokeMember(
                        "CreateShortcut", BindingFlags.InvokeMethod, null, com, new object?[] { linkPath });
                    if (shortcut is null) continue;
                    try
                    {
                        target = comType.InvokeMember(
                            "TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                        arguments = comType.InvokeMember(
                            "Arguments", BindingFlags.GetProperty, null, shortcut, null) as string;
                        workingDir = comType.InvokeMember(
                            "WorkingDirectory", BindingFlags.GetProperty, null, shortcut, null) as string;
                    }
                    finally
                    {
                        ReleaseCom(shortcut);
                    }
                }
                catch
                {
                    // 坏快捷方式：跳过它
                    continue;
                }
                if (string.IsNullOrWhiteSpace(target)) continue;

                var chain = new List<ScriptFacts>();
                var exe = ResolveShortcut(scan, target, arguments, workingDir, chain);
                if (exe is not null)
                    AddShell(scan, exe, "shortcut: " + linkPath, facts: chain, verified: true);
            }
        }
        finally
        {
            ReleaseCom(com);
        }
    }

    /// <summary>快捷方式目标 → 真实桌面壳 exe（可能经一层 cmd/ps1）。</summary>
    static string? ResolveShortcut(Scan scan, string target, string? arguments, string? workingDir,
        List<ScriptFacts> chain)
    {
        var baseDir = FullPath(workingDir) ?? Path.GetDirectoryName(FullPath(target) ?? target);

        if (IsExePath(target))
        {
            var exe = FullPath(target);
            if (exe is not null && IsElectronDsh(scan, Path.GetDirectoryName(exe))) return exe;
        }
        else if (IsScriptPath(target))
        {
            var hit = FollowScript(scan, target, chain);
            if (hit is not null) return hit;
        }

        foreach (var token in PathTokens(arguments))
        {
            var path = ExpandPath(token, baseDir);
            if (path is null || !File.Exists(path)) continue;
            if (IsScriptPath(path))
            {
                var hit = FollowScript(scan, path, chain);
                if (hit is not null) return hit;
            }
            else if (IsExePath(path))
            {
                if (IsElectronDsh(scan, Path.GetDirectoryName(path))) return path;
            }
        }
        return null;
    }

    /// <summary>解析一个启动脚本并返回它（含链）找到的桌面壳 exe。</summary>
    static string? FollowScript(Scan scan, string scriptPath, List<ScriptFacts> chain)
    {
        var facts = ParseScript(scan, scriptPath, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (facts is null) return null;
        chain.Add(facts);
        foreach (var exe in facts.Exes)
            if (IsElectronDsh(scan, Path.GetDirectoryName(exe))) return exe;
        return null;
    }

    // ---- 线索 3：卸载注册表 -------------------------------------------------

    /// <summary>
    /// HKLM/HKCU 的 Uninstall（含 WOW6432Node）里 DisplayName 像 DSH 的项：
    /// 取 InstallLocation 顶层 *.exe / DisplayIcon 指向的 exe。
    /// 另扫 HKCU\Software 直接子键的 InstallLocation（NSIS 按用户安装）。
    /// </summary>
    static void ScanUninstallRegistry(Scan scan)
    {
        var counter = new int[1];
        ProbeUninstall(scan, RegistryHive.LocalMachine, RegistryView.Registry64, counter);
        ProbeUninstall(scan, RegistryHive.LocalMachine, RegistryView.Registry32, counter);
        ProbeUninstall(scan, RegistryHive.CurrentUser, RegistryView.Registry64, counter);
        ProbeUninstall(scan, RegistryHive.CurrentUser, RegistryView.Registry32, counter);
        ProbeUserSoftware(scan, counter);
    }

    static void ProbeUninstall(Scan scan, RegistryHive hive, RegistryView view, int[] counter)
    {
        RegistryKey? baseKey = null;
        RegistryKey? uninstall = null;
        try
        {
            baseKey = RegistryKey.OpenBaseKey(hive, view);
            uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        }
        catch
        {
            return;
        }
        if (uninstall is null)
        {
            baseKey?.Dispose();
            return;
        }

        try
        {
            string[] names;
            try
            {
                names = uninstall.GetSubKeyNames();
            }
            catch
            {
                return;
            }

            for (var i = 0; i < names.Length; i++)
            {
                if (++counter[0] > UninstallKeyCap) return;
                if ((counter[0] & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
                try
                {
                    using var key = uninstall.OpenSubKey(names[i]);
                    if (key is null) continue;
                    var display = key.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display) || !DshDisplayName.IsMatch(display)) continue;

                    var version = key.GetValue("DisplayVersion") as string;
                    var icon = key.GetValue("DisplayIcon") as string;
                    var location = key.GetValue("InstallLocation") as string;
                    ProbeRegistryInstall(scan, location, icon, version, "uninstall registry: " + display);
                }
                catch
                {
                    // 单个键读不动：跳过
                }
            }
        }
        finally
        {
            try { uninstall.Dispose(); } catch { /* 已释放 */ }
            try { baseKey?.Dispose(); } catch { /* 已释放 */ }
        }
    }

    /// <summary>HKCU\Software\&lt;name&gt; 上有 InstallLocation 的（NSIS 按用户安装）。</summary>
    static void ProbeUserSoftware(Scan scan, int[] counter)
    {
        RegistryKey? software = null;
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            software = baseKey.OpenSubKey("Software");
        }
        catch
        {
            return;
        }
        if (software is null) return;

        try
        {
            string[] names;
            try
            {
                names = software.GetSubKeyNames();
            }
            catch
            {
                return;
            }

            var seen = 0;
            foreach (var name in names)
            {
                if (++seen > UserSoftwareKeyCap) break;
                if ((seen & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
                try
                {
                    using var key = software.OpenSubKey(name);
                    if (key is null) continue;
                    var location = key.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(location)) continue;
                    var display = key.GetValue("DisplayName") as string;
                    var version = key.GetValue("DisplayVersion") as string;
                    var icon = key.GetValue("DisplayIcon") as string;
                    ProbeRegistryInstall(scan, location, icon, version,
                        "user install registry: " + (string.IsNullOrWhiteSpace(display) ? name : display));
                }
                catch
                {
                    // 单个键读不动：跳过
                }
            }
        }
        finally
        {
            try { software.Dispose(); } catch { /* 已释放 */ }
        }
    }

    /// <summary>从注册表项推导安装目录 / exe，探测其中的 Electron+DSH 壳。</summary>
    static void ProbeRegistryInstall(Scan scan, string? location, string? icon, string? version, string found)
    {
        var iconExe = ExeFromDisplayIcon(icon);
        if (iconExe is not null) AddShell(scan, iconExe, found, version);

        var installDir = FullPath(location);
        if (installDir is not null && Directory.Exists(installDir))
            ProbeExeDir(scan, installDir, found, version);

        if (iconExe is not null)
        {
            var iconDir = Path.GetDirectoryName(iconExe);
            if (!string.IsNullOrWhiteSpace(iconDir)) ProbeExeDir(scan, iconDir, found, version);
        }
    }

    /// <summary>DisplayIcon 形如 <c>C:\...\App.exe,0</c>；取逗号前的 exe。</summary>
    static string? ExeFromDisplayIcon(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().Trim('"');
        var comma = text.LastIndexOf(',');
        if (comma > 2) text = text[..comma];
        text = text.Trim().Trim('"');
        return IsExePath(text) ? FullPath(text) : null;
    }

    /// <summary>一个目录顶层找 Electron+DSH 的 exe；先确认目录是壳，再挑主 exe。</summary>
    static void ProbeExeDir(Scan scan, string? dir, string found, string? version)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        if (!IsElectronDsh(scan, dir)) return;

        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.exe"))
            {
                if (++count > ExeCap) break;
                if (IsAuxiliaryExe(file)) continue;
                AddShell(scan, file, found, version, verified: true);
            }
        }
        catch
        {
            // 目录读失败：跳过
        }
    }

    // ---- 线索 4：已知 userData / 本地 Programs / pad.json --------------------

    /// <summary>
    /// %APPDATA% 直接子目录里含 <c>harness\</c> 或 <c>desktop\</c> 的，是 Electron userData 的形状；
    /// <c>@scope</c> 下一层同样看。名字归一化后与壳名匹配（如 dsh-desktop ↔ DSH Desktop）。
    /// </summary>
    static void ScanKnownLocations(Scan scan)
    {
        var seen = 0;
        foreach (var dir in SafeDirs(scan.AppData))
        {
            if (++seen > UserDataCap) break;
            if ((seen & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
            if (HasChildDir(dir, "harness") || HasChildDir(dir, "desktop")) scan.AddUserData(dir);
        }

        var scope = Path.Combine(scan.AppData, "@deepseek-ai");
        seen = 0;
        foreach (var dir in SafeDirs(scope))
        {
            if (++seen > UserDataCap) break;
            if ((seen & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
            if (HasChildDir(dir, "harness") || HasChildDir(dir, "desktop")) scan.AddUserData(dir);
        }

        // 两个已知形状即便当前不可写也要作为候选：装了但还没启动过的客户端也在。
        scan.AddUserData(Path.Combine(scan.AppData, "dsh-desktop"));
        scan.AddUserData(Path.Combine(scan.AppData, "@deepseek-ai", "dsh-desktop"));
    }

    /// <summary>%LOCALAPPDATA%\Programs 下名字像 DSH 的安装目录（每用户安装）。</summary>
    static void ScanLocalPrograms(Scan scan)
    {
        var seen = 0;
        foreach (var dir in SafeDirs(Path.Combine(scan.LocalAppData, "Programs")))
        {
            if (++seen > UserDataCap) break;
            if ((seen & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(dir);
            if (!NameLooksDsh(name)) continue;
            ProbeExeDir(scan, dir, "localappdata/Programs: " + name, null);
        }
    }

    /// <summary>pad.json#launch.dshCli：在 asar 里就向上反推安装目录与壳 exe。</summary>
    static void ScanPadConfig(Scan scan)
    {
        PadSettings? settings;
        try
        {
            settings = scan.Launcher.Settings();
        }
        catch
        {
            return;
        }
        if (settings is null) return;

        var cli = settings.Launch.DshCli;
        if (string.IsNullOrWhiteSpace(cli)) return;
        scan.SettingsDshCli = FullPath(cli.Trim());

        var at = cli.IndexOf(".asar", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return;
        var archive = FullPath(cli[..(at + 5)]);
        if (archive is null) return;
        var resourcesDir = Path.GetDirectoryName(archive);
        var installDir = resourcesDir is null ? null : Path.GetDirectoryName(resourcesDir);
        if (!string.IsNullOrWhiteSpace(installDir))
            ProbeExeDir(scan, installDir, "pad.json#launch.dshCli", null);
    }

    // ---- 线索 5：PAD_DISCOVER_EXTRA（测试 / 手工注入） ----------------------

    /// <summary>
    /// 分号分隔的额外根：向下 2 层分类成 shell / home / launcher / kernel。
    /// 这是 fixture 测试的注入点，也让用户能把没装进常规位置的副本指给 PAD。
    /// 注意：不做全盘遍历，只在给定根里走，且总量封顶。
    /// </summary>
    static void ScanExtraRoots(Scan scan)
    {
        var raw = Environment.GetEnvironmentVariable(ExtraEnv);
        if (string.IsNullOrWhiteSpace(raw)) return;

        var total = 0;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var root = FullPath(part.Trim().Trim('"'));
            if (root is null || !Directory.Exists(root)) continue;

            // 根下的 npx 形内核（node_modules 不进 BFS，单独看）。
            ProbeExtraKernel(scan, root);

            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));
            var perRoot = 0;
            while (queue.Count > 0)
            {
                perRoot++;
                total++;
                if (total > ExtraDirCapTotal) return;
                if (perRoot > ExtraDirCapPerRoot) break;
                if ((perRoot & 31) == 0) scan.Ct.ThrowIfCancellationRequested();

                var (dir, depth) = queue.Dequeue();
                ClassifyExtraDir(scan, dir, root);
                if (depth >= 2) continue;

                foreach (var child in SafeDirs(dir))
                {
                    if (SkipExtraDir(Path.GetFileName(child))) continue;
                    queue.Enqueue((child, depth + 1));
                }
            }
        }
    }

    static void ProbeExtraKernel(Scan scan, string root)
    {
        foreach (var relative in new[]
                 {
                     Path.Combine("node_modules", "@deepseek-ai", "dsh"),
                     Path.Combine("@deepseek-ai", "dsh"),
                 })
        {
            var dir = Path.Combine(root, relative);
            if (!Directory.Exists(dir)) continue;
            var kernel = DshKernels.FromPackageDir(dir, "discover extra: " + root);
            if (kernel is not null) scan.ExtraKernels.Add(kernel);
        }
    }

    /// <summary>一个 EXTRA 目录能同时是壳安装目录、home 的父目录、launcher 目录。</summary>
    static void ClassifyExtraDir(Scan scan, string dir, string root)
    {
        var found = "discover extra: " + root;

        if (IsElectronDsh(scan, dir))
        {
            var exe = FindMainExe(dir);
            if (exe is not null) AddShell(scan, exe, found, verified: true);
        }

        if (IsHome(dir))
        {
            scan.AddHome(dir, found);
        }
        else
        {
            foreach (var childName in TuiHomeChildNames)
            {
                var child = Path.Combine(dir, childName);
                if (Directory.Exists(child) && IsHome(child)) scan.AddHome(child, found);
            }
        }

        var scripts = ScanDirScripts(scan, dir, ScriptCapPerDir);
        if (scripts.Count == 0) return;
        if (!scan.ExtraScriptsByRoot.TryGetValue(root, out var known))
            scan.ExtraScriptsByRoot[root] = known = [];
        foreach (var facts in scripts)
            if (!known.Contains(facts)) known.Add(facts);
    }

    /// <summary>EXTRA 根下 node_modules / .git 之类不进 BFS（内核单独看过）。</summary>
    static bool SkipExtraDir(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        return name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
               || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
               || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
               || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Cache", StringComparison.OrdinalIgnoreCase)
               || name.Equals("GPUCache", StringComparison.OrdinalIgnoreCase)
               || name.Equals(".cache", StringComparison.OrdinalIgnoreCase)
               || name.Equals("dist", StringComparison.OrdinalIgnoreCase);
    }

    // ---- 线索 6：固定盘根命名启发 -------------------------------------------

    /// <summary>
    /// 关键取舍（D25）：只扫固定盘根下的**直接子目录**，名字含 dsh/harness 的才看。
    /// 不做全盘遍历；每盘 ≤400 个目录、最多 8 个盘。
    /// 命中 home 本身、或它下一层的 home/dsh-home/harness；含启动脚本的目录记成 launcher。
    /// </summary>
    static void ScanDriveRoots(Scan scan)
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return;
        }

        var used = 0;
        foreach (var drive in drives)
        {
            if (++used > DriveCap) break;
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
            }
            catch
            {
                continue;
            }

            var root = drive.RootDirectory.FullName;
            var count = 0;
            foreach (var dir in SafeDirs(root))
            {
                if (++count > DriveChildCap) break;
                if ((count & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
                if (!NameLooksDsh(Path.GetFileName(dir))) continue;

                if (IsHome(dir))
                {
                    scan.AddHome(dir, "drive root: " + root);
                }
                else
                {
                    foreach (var childName in TuiHomeChildNames)
                    {
                        var child = Path.Combine(dir, childName);
                        if (Directory.Exists(child) && IsHome(child))
                            scan.AddHome(child, "drive root: " + root);
                    }
                }

                ScanDirScripts(scan, dir, ScriptCapPerDir);
            }
        }
    }

    // ---- 线索 7：显式 --deep 根（有界 Walk） --------------------------------

    /// <summary>只有 CLI 显式给了根才走；深度 5、总目录 20000、跳过名单与旧实现一致。</summary>
    static void ScanDeepRoots(Scan scan, IReadOnlyList<string>? deepRoots)
    {
        if (deepRoots is null) return;
        foreach (var raw in deepRoots)
        {
            var root = FullPath(raw?.Trim().Trim('"'));
            if (root is null || !Directory.Exists(root)) continue;
            foreach (var dir in WalkBounded(scan, root, DeepDepthCap, DeepDirCap))
            {
                if (IsHome(dir)) scan.AddHome(dir, "deep: " + root);
            }
        }
    }

    static IEnumerable<string> WalkBounded(Scan scan, string root, int maxDepth, int maxDirs)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "obj", "bin", "Cache", "GPUCache", "drafts", "SteamLibrary", "$Recycle.Bin",
        };
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        var seen = 0;
        while (queue.Count > 0)
        {
            if ((seen & 31) == 0) scan.Ct.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();
            if (++seen > maxDirs) yield break;
            yield return dir;
            if (depth >= maxDepth) continue;
            foreach (var child in SafeDirs(dir))
            {
                if (skip.Contains(Path.GetFileName(child))) continue;
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    // ---- 桌面壳：合并候选 → Shell 记录 -------------------------------------

    /// <summary>
    /// 每个壳解析 UserData / Home / Running / Kernel：
    /// Home 优先级 = 启动脚本的 DSH_HOME（LooksLikeHome 校验）→ userData\harness → 环境 DSH_HOME。
    /// </summary>
    static void BuildShells(Scan scan, List<Shell> shells, List<Kernel> shellKernels)
    {
        var initial = scan.Shells.Values.ToList();
        var pending = new Queue<ShellCandidate>(initial);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queued = new HashSet<string>(initial.Select(candidate => candidate.Exe),
            StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            scan.Ct.ThrowIfCancellationRequested();
            var candidate = pending.Dequeue();
            if (!claimed.Add(candidate.Exe)) continue;

            try
            {
                // 壳目录旁边的 *.cmd / *.ps1 也是它的启动脚本（launch-official.cmd 之类）。
                var exeDir = Path.GetDirectoryName(candidate.Exe) ?? "";
                foreach (var facts in ScanDirScripts(scan, exeDir, ScriptCapPerDir))
                    if (!candidate.Facts.Contains(facts)) candidate.Facts.Add(facts);

                var procs = ProcsForExe(scan, candidate.Exe);
                var userData = UserDataFromProcs(procs) ?? PickUserData(scan, candidate.Exe);

                // Home 优先级：启动脚本 → 进程环境 DSH_HOME → 命令行 \profiles\<name> 前缀
                // → userData\harness → 本进程/注册表 DSH_HOME。进程现场比文件更硬。
                var home = FirstScriptHome(candidate.Facts)
                           ?? HomeFromProcs(procs)
                           ?? HomeFromProfilesCmdLine(procs)
                           ?? HarnessUnder(userData)
                           ?? scan.EnvHome
                           ?? ExtraRootHome(scan, candidate.Exe);

                var pid = FirstProcPid(procs)
                          ?? (scan.RunningPids.TryGetValue(candidate.Exe, out var runningPid)
                              ? runningPid
                              : null);

                var running = pid is not null
                              || scan.RunningExes.Contains(candidate.Exe)
                              || SafeDesktopShellRunning(userData);

                var version = ExeVersion(candidate.Exe)
                              ?? ShellVersionFromAsar(scan, candidate.Exe)
                              ?? candidate.DisplayVersion;

                string? kernelVersion = null;
                string? kernelIdentity = null;
                try
                {
                    var kernels = DshKernels.FromShell(candidate.Exe, candidate.Found).ToList();
                    if (kernels.Count > 0)
                    {
                        kernelVersion = kernels[0].Version;
                        kernelIdentity = kernels[0].Identity;
                        shellKernels.AddRange(kernels);
                    }
                }
                catch
                {
                    // asar 读不动：壳仍然算找到，只是内核未知
                }

                shells.Add(new Shell(candidate.Exe, version, userData, home, running,
                    candidate.Found, kernelVersion, kernelIdentity, pid));
                if (!string.IsNullOrWhiteSpace(home)) scan.AddHome(home, "shell: " + candidate.Found);
            }
            catch
            {
                // 单壳解析失败：跳过它，其它壳继续
            }

            // 解析脚本时可能又发现新的壳 exe；补进队列。
            foreach (var extra in scan.Shells.Values)
            {
                if (claimed.Contains(extra.Exe)) continue;
                if (queued.Add(extra.Exe)) pending.Enqueue(extra);
            }
        }

        shells.Sort((a, b) => string.Compare(a.Exe, b.Exe, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>与壳名归一化匹配的 userData；匹配不上且只有一个候选时用它。</summary>
    static string? PickUserData(Scan scan, string exe)
    {
        if (scan.UserDataCandidates.Count == 0) return null;
        var shellName = NormalizeName(Path.GetFileNameWithoutExtension(exe));
        if (shellName.Length == 0) return null;

        string? best = null;
        var bestScore = 0;
        foreach (var dir in scan.UserDataCandidates)
        {
            var score = UserDataScore(dir, shellName);
            if (score > bestScore)
            {
                best = dir;
                bestScore = score;
            }
        }
        if (best is not null) return best;
        return scan.UserDataCandidates.Count == 1 ? scan.UserDataCandidates[0] : null;
    }

    static int UserDataScore(string dir, string shellName)
    {
        var name = NormalizeName(Path.GetFileName(dir));
        if (name.Length == 0) return 0;

        int score;
        if (name.Equals(shellName, StringComparison.Ordinal)) score = 100;
        else if (name.Contains(shellName, StringComparison.Ordinal)) score = 60;
        else if (shellName.Contains(name, StringComparison.Ordinal)) score = 50;
        else return 0;

        var harness = Path.Combine(dir, "harness");
        if (LooksLikeHome(harness)) score += 30;
        else if (Directory.Exists(harness)) score += 10;
        if (SafeDesktopShellRunning(dir)) score += 5;
        return score;
    }

    static string? FirstScriptHome(IEnumerable<ScriptFacts> facts)
    {
        foreach (var fact in facts)
            foreach (var home in fact.Homes)
                if (DshSniff.LooksLikeHome(home)) return home;
        return null;
    }

    // ---- 进程现场（ProcTable）：壳的 userData/home，TUI/CLI 的在跑 -------

    /// <summary>快照里 exe 与本壳相同的进程（Electron 的 renderer / host 子进程也是同一个 exe）。</summary>
    static List<ProcInfo> ProcsForExe(Scan scan, string exe)
    {
        var list = new List<ProcInfo>();
        foreach (var proc in scan.Procs)
        {
            if (string.IsNullOrWhiteSpace(proc.Exe)) continue;
            if (SamePath(proc.Exe, exe)) list.Add(proc);
        }
        return list;
    }

    static int? FirstProcPid(List<ProcInfo> procs) => procs.Count > 0 ? procs[0].Pid : null;

    /// <summary>Electron 的 <c>--user-data-dir</c> 只出现在进程命令行里，这是 userData 的真值。</summary>
    static string? UserDataFromProcs(List<ProcInfo> procs)
    {
        foreach (var proc in procs)
        {
            var userData = NormalizePath(proc.UserDataDir);
            if (userData is not null) return userData;
        }
        return null;
    }

    /// <summary>同 Exe 进程环境里的 DSH_HOME；多个取出现次数最多的，同票取先出现的。</summary>
    static string? HomeFromProcs(List<ProcInfo> procs)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var proc in procs)
        {
            var home = NormalizePath(proc.DshHome);
            if (home is null || !LooksLikeHome(home)) continue;
            if (counts.TryGetValue(home, out var count))
            {
                counts[home] = count + 1;
            }
            else
            {
                counts[home] = 1;
                order.Add(home);
            }
        }

        string? best = null;
        var bestCount = 0;
        foreach (var home in order)
        {
            var count = counts[home];
            if (count <= bestCount) continue;
            best = home;
            bestCount = count;
        }
        return best;
    }

    /// <summary>子进程命令行里的 <c>\profiles\&lt;name&gt;</c> 前缀就是那个 home。</summary>
    static string? HomeFromProfilesCmdLine(List<ProcInfo> procs)
    {
        foreach (var proc in procs)
        {
            var commandLine = proc.CommandLine;
            if (string.IsNullOrWhiteSpace(commandLine)) continue;
            IReadOnlyList<string> args;
            try
            {
                args = ProcTable.SplitArgs(commandLine);
            }
            catch
            {
                continue;
            }
            foreach (var arg in args)
            {
                var at = ProfilesSegmentIndex(arg);
                if (at <= 0) continue;
                var home = NormalizePath(arg[..at]);
                if (home is not null && LooksLikeHome(home)) return home;
            }
        }
        return null;
    }

    /// <summary><c>\profiles\</c> 或 <c>/profiles/</c> 的起始下标；没有返回 -1。</summary>
    static int ProfilesSegmentIndex(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return -1;
        var back = path.IndexOf("\\profiles\\", StringComparison.OrdinalIgnoreCase);
        var forward = path.IndexOf("/profiles/", StringComparison.OrdinalIgnoreCase);
        if (back >= 0 && forward >= 0) return Math.Min(back, forward);
        return back >= 0 ? back : forward;
    }

    /// <summary>userData\harness，仅当它确实像个 home。</summary>
    static string? HarnessUnder(string? userData)
    {
        if (string.IsNullOrWhiteSpace(userData)) return null;
        var harness = Path.Combine(userData, "harness");
        return LooksLikeHome(harness) ? NormalizePath(harness) : null;
    }

    /// <summary>
    /// PAD_DISCOVER_EXTRA 下的启动脚本也可能给壳指定 DSH_HOME：同一个注入根里的
    /// 脚本（launchers/start.cmd 之类）算这个根里所有壳的候选。
    /// </summary>
    static string? ExtraRootHome(Scan scan, string exe)
    {
        foreach (var (root, scripts) in scan.ExtraScriptsByRoot)
        {
            if (!PathUnder(exe, root)) continue;
            var home = FirstScriptHome(scripts);
            if (home is not null) return home;
        }
        return null;
    }

    static bool PathUnder(string? path, string? root)
    {
        var full = NormalizePath(path);
        var dir = NormalizePath(root);
        if (full is null || dir is null) return false;
        if (full.Equals(dir, StringComparison.OrdinalIgnoreCase)) return true;
        return full.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>壳自己的版本：FileVersion（本机两个壳都是精确的）优先，asar 根 package.json 兜底。</summary>
    static string? ShellVersionFromAsar(Scan scan, string exe)
    {
        var exeDir = Path.GetDirectoryName(exe);
        if (string.IsNullOrWhiteSpace(exeDir)) return null;
        var asar = Asar.Open(Path.Combine(exeDir, "resources", "app.asar"));
        if (asar is null) return null;
        return VersionFromJson(asar.ReadText("package.json"));
    }

    /// <summary>Electron+DSH 签名：&lt;exeDir&gt;\resources\app.asar 里捆着 DSH 内核。</summary>
    static bool IsElectronDsh(Scan scan, string? exeDir)
    {
        if (string.IsNullOrWhiteSpace(exeDir)) return false;
        var archive = Path.Combine(exeDir, "resources", "app.asar");
        if (!File.Exists(archive)) return false;
        if (scan.AsarDshCache.TryGetValue(archive, out var cached)) return cached;
        var ok = false;
        try
        {
            ok = Asar.LooksLikeDsh(archive);
        }
        catch
        {
            ok = false;
        }
        scan.AsarDshCache[archive] = ok;
        return ok;
    }

    /// <summary>一个目录里的主 exe：排除 uninstall / elevate / crashpad / helper 之类。</summary>
    static string? FindMainExe(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.exe"))
            {
                if (++count > ExeCap) break;
                if (IsAuxiliaryExe(file)) continue;
                return FullPath(file);
            }
        }
        catch
        {
            // 读不动就当作没有
        }
        return null;
    }

    static bool IsAuxiliaryExe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) return true;
        return name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
               || name.Contains("elevate", StringComparison.OrdinalIgnoreCase)
               || name.Contains("crashpad", StringComparison.OrdinalIgnoreCase)
               || name.Contains("helper", StringComparison.OrdinalIgnoreCase)
               || name.Contains("updater", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>登记/合并一个桌面壳候选。调用方若已确认签名可传 verified 省一次 asar 判定。</summary>
    static void AddShell(Scan scan, string? exe, string found, string? displayVersion = null,
        IEnumerable<ScriptFacts>? facts = null, bool verified = false)
    {
        var full = FullPath(exe);
        if (full is null || !File.Exists(full)) return;
        if (scan.SelfExe is not null && SamePath(full, scan.SelfExe)) return;
        if (!verified && !IsElectronDsh(scan, Path.GetDirectoryName(full))) return;

        if (!scan.Shells.TryGetValue(full, out var candidate))
        {
            candidate = new ShellCandidate { Exe = full };
            scan.Shells[full] = candidate;
        }
        if (string.IsNullOrWhiteSpace(candidate.Found)) candidate.Found = found;
        if (!string.IsNullOrWhiteSpace(displayVersion) && string.IsNullOrWhiteSpace(candidate.DisplayVersion))
            candidate.DisplayVersion = displayVersion;
        if (facts is not null)
        {
            foreach (var fact in facts)
                if (!candidate.Facts.Contains(fact)) candidate.Facts.Add(fact);
        }
    }

    // ---- home --------------------------------------------------------------

    static void BuildHomes(Scan scan, List<Shell> shells, List<DshHome> homes)
    {
        scan.AddHome(Path.Combine(scan.AppData, "dsh-desktop", "harness"), "known: %APPDATA%/dsh-desktop/harness");
        scan.AddHome(Path.Combine(scan.AppData, "@deepseek-ai", "dsh-desktop", "harness"),
            "known: %APPDATA%/@deepseek-ai/dsh-desktop/harness");
        scan.AddHome(Path.Combine(scan.UserProfile, ".dsh"), "known: ~/.dsh");
        scan.AddHome(scan.UserProfile, "known: user profile (profiles/ at root)");
        scan.AddHome(scan.EnvHome, "environment DSH_HOME");

        List<Instance> instances;
        try
        {
            instances = scan.Launcher.Instances();
        }
        catch
        {
            instances = [];
        }
        foreach (var instance in instances)
            scan.AddHome(instance.Home, "pad instance");
        foreach (var shell in shells)
            scan.AddHome(shell.Home, "shell: " + shell.Found);

        // 在跑的壳的 home 也算 running（桌面端不写 PAD 账本）。
        var runningHomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shell in shells)
        {
            if (!shell.Running || string.IsNullOrWhiteSpace(shell.Home)) continue;
            var full = NormalizePath(shell.Home);
            if (full is not null) runningHomes.Add(full);
        }

        // 进程现场里 DSH_HOME 指向的 home：比账本硬（TUI/CLI 不写账本）。
        var procHomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in scan.Procs)
        {
            var procHome = NormalizePath(proc.DshHome);
            if (procHome is not null) procHomes.Add(procHome);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, found) in scan.HomeSeeds)
        {
            if (!seen.Add(path)) continue;
            if (!IsHome(path)) continue;
            var running = runningHomes.Contains(path) || LedgerRunning(path) || procHomes.Contains(path);
            homes.Add(new DshHome(path, Profiles(path), Sessions(path), running, found));
        }

        // Sessions 降序，再按路径：人先看见用得最多的 home。
        homes.Sort((a, b) =>
        {
            var bySessions = b.Sessions.CompareTo(a.Sessions);
            return bySessions != 0
                ? bySessions
                : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>带 <c>profiles\</c> 或 <c>storages\</c> 的目录就是 home。</summary>
    static bool IsHome(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        return Directory.Exists(Path.Combine(dir, "profiles"))
               || Directory.Exists(Path.Combine(dir, "storages"));
    }

    static bool LooksLikeHome(string? dir) => DshSniff.LooksLikeHome(dir ?? "");

    static bool LedgerRunning(string home)
    {
        try
        {
            foreach (var row in Nodes.Ledger.Load(home))
                if (Proc.Alive(row.Pid)) return true;
        }
        catch
        {
            // 账本读不动就不算在跑
        }
        return false;
    }

    /// <summary>profile 目录名：排除 node_modules 和 '.' 开头的（.generations 不是 profile）。</summary>
    static IReadOnlyList<string> Profiles(string home)
    {
        var names = new List<string>();
        var dir = Path.Combine(home, "profiles");
        if (!Directory.Exists(dir)) return names;
        var count = 0;
        foreach (var child in SafeDirs(dir))
        {
            if (++count > ProfilesDirCap) break;
            var name = Path.GetFileName(child);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.StartsWith(".", StringComparison.Ordinal)) continue;
            if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
            names.Add(name);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    static int Sessions(string home)
    {
        var count = 0;
        var dir = Path.Combine(home, "sessions");
        if (!Directory.Exists(dir)) return 0;
        foreach (var _ in SafeDirs(dir))
        {
            if (++count >= SessionsCap) break;
        }
        return count;
    }

    // ---- TUI ---------------------------------------------------------------

    /// <summary>
    /// TUI = home 的 profiles\&lt;p&gt; 里声明的 <c>dsh-tui</c> 依赖（版本取 node_modules 里的实体包）。
    /// 再把启动脚本里的 DSH_HOME / --profile / @deepseek-ai/dsh@ver 合并进来；
    /// 带 tui 风格 profile 但没有实体 profile 的脚本，补一条 "(launcher)"。
    /// 最后才认 ~/.dsh-tui 状态目录（什么都没找着时兜一条）。
    /// </summary>
    static void BuildTuis(Scan scan, List<DshHome> homes, List<Tui> tuis)
    {
        foreach (var home in homes)
        {
            scan.Ct.ThrowIfCancellationRequested();
            var profilesDir = Path.Combine(home.Path, "profiles");
            if (!Directory.Exists(profilesDir)) continue;

            var count = 0;
            foreach (var profileDir in SafeDirs(profilesDir))
            {
                if (++count > ProfileCap) break;
                var profile = Path.GetFileName(profileDir);
                if (string.IsNullOrWhiteSpace(profile)) continue;
                if (profile.StartsWith(".", StringComparison.Ordinal)) continue;
                if (profile.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;

                var packageJson = Path.Combine(profileDir, "package.json");
                if (!File.Exists(packageJson)) continue;
                foreach (var (package, spec) in TuiDeclarations(packageJson))
                {
                    var packageDir = Path.Combine(profileDir, "node_modules",
                        package.Replace('/', Path.DirectorySeparatorChar));
                    var version = ReadPackageVersion(Path.Combine(packageDir, "package.json"))
                                  ?? DshKernels.ParseVersionFromSpec(spec ?? "");
                    AddTui(tuis, new Tui(package, version,
                        Directory.Exists(packageDir) ? packageDir : null,
                        home.Path, profile, null, null, false, "profile dependency"));
                }
            }
        }

        // 启动脚本：补 Launcher / DeclaredKernel，或把没有实体 profile 的脚本记成一条。
        foreach (var script in scan.Scripts.ToList())
        {
            for (var i = 0; i < script.Homes.Count; i++)
            {
                var scriptHome = script.Homes[i];
                var profile = script.Profiles.Count > 0 ? script.Profiles[0] : null;
                var declaredSpec = script.Declared.Count > 0 ? script.Declared[0] : null;
                var declared = DshKernels.ParseVersionFromSpec(declaredSpec ?? "");

                var joined = false;
                for (var t = 0; t < tuis.Count; t++)
                {
                    if (!SamePath(tuis[t].Home, scriptHome)) continue;
                    if (!string.IsNullOrWhiteSpace(profile)
                        && !string.Equals(tuis[t].Profile, profile, StringComparison.OrdinalIgnoreCase))
                        continue;
                    tuis[t] = tuis[t] with
                    {
                        Launcher = script.Path,
                        DeclaredKernel = declared ?? tuis[t].DeclaredKernel,
                    };
                    joined = true;
                    if (!string.IsNullOrWhiteSpace(profile)) break;
                }
                if (joined) continue;
                if (!LooksLikeTuiScript(script, profile)) continue;
                AddTui(tuis, new Tui("(launcher)", null, null, scriptHome, profile,
                    script.Path, declared, false, "launcher script"));
            }
        }

        var tuiState = Path.Combine(scan.UserProfile, TuiStateDirName);
        if (tuis.Count == 0 && !string.IsNullOrWhiteSpace(scan.UserProfile) && Directory.Exists(tuiState))
            tuis.Add(new Tui("@deepseek-harness-tui/dsh-tui", null, null, null, null, null, null,
                false, "tui state dir"));

        // 进程现场：TUI 的 DSH_HOME + --profile 只存在于进程里，这是判「在跑」的唯一硬证据。
        for (var i = 0; i < tuis.Count; i++)
        {
            var (running, pid) = TuiInProcTable(scan, tuis[i]);
            if (running) tuis[i] = tuis[i] with { Running = true, Pid = pid };
        }

        tuis.Sort((a, b) =>
        {
            var byHome = string.Compare(a.Home ?? "", b.Home ?? "", StringComparison.OrdinalIgnoreCase);
            if (byHome != 0) return byHome;
            var byProfile = string.Compare(a.Profile ?? "", b.Profile ?? "", StringComparison.OrdinalIgnoreCase);
            return byProfile != 0
                ? byProfile
                : string.Compare(a.Package, b.Package, StringComparison.OrdinalIgnoreCase);
        });
    }

    static void AddTui(List<Tui> tuis, Tui tui)
    {
        foreach (var existing in tuis)
        {
            if (!string.Equals(existing.Package, tui.Package, StringComparison.OrdinalIgnoreCase)) continue;
            if (!SamePath(existing.Home, tui.Home)) continue;
            if (!string.Equals(existing.Profile, tui.Profile, StringComparison.OrdinalIgnoreCase)) continue;
            return;
        }
        tuis.Add(tui);
    }

    /// <summary>
    /// TUI 在跑 = 快照里有进程「自己的 DSH_HOME 对上这个 home 且 profile 也对上」。
    /// home 缺失（状态目录 / launcher 兜底）时退一步：命令行同时带 profile 和 dsh。
    /// </summary>
    static (bool Running, int? Pid) TuiInProcTable(Scan scan, Tui tui)
    {
        var home = NormalizePath(tui.Home);
        var profile = string.IsNullOrWhiteSpace(tui.Profile) ? null : tui.Profile.Trim();

        foreach (var proc in scan.Procs)
        {
            if (home is not null)
            {
                var procHome = NormalizePath(proc.DshHome);
                if (procHome is null || !string.Equals(procHome, home, StringComparison.OrdinalIgnoreCase)) continue;
                if (profile is null || ProcHasProfile(proc, profile)) return (true, proc.Pid);
                continue;
            }

            if (profile is null) continue;
            if (!ProcHasProfile(proc, profile)) continue;
            var commandLine = proc.CommandLine ?? "";
            if (commandLine.Contains("dsh", StringComparison.OrdinalIgnoreCase)) return (true, proc.Pid);
        }
        return (false, null);
    }

    /// <summary>进程现场里有没有这个 profile：--profile 参数 / DSH_PROFILE / \profiles\&lt;name&gt;。</summary>
    static bool ProcHasProfile(ProcInfo proc, string profile)
    {
        if (!string.IsNullOrWhiteSpace(proc.ProfileArg)
            && string.Equals(proc.ProfileArg.Trim(), profile, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(proc.DshProfile)
            && string.Equals(proc.DshProfile.Trim(), profile, StringComparison.OrdinalIgnoreCase)) return true;

        var commandLine = proc.CommandLine;
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        if (HasProfileToken(commandLine, profile)) return true;
        if (commandLine.Contains("\\profiles\\" + profile, StringComparison.OrdinalIgnoreCase)) return true;
        return commandLine.Contains("/profiles/" + profile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>token 边界检查，避免 dsh-tui 命中 dsh-tui-extra。</summary>
    static bool HasProfileToken(string commandLine, string profile)
    {
        var marks = new[] { "--profile " + profile, "--profile=" + profile };
        foreach (var mark in marks)
        {
            var start = 0;
            while (true)
            {
                var at = commandLine.IndexOf(mark, start, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                var after = at + mark.Length;
                if (after >= commandLine.Length) return true;
                var ch = commandLine[after];
                if (ch is ' ' or '"' or '\'' or '\t') return true;
                start = at + 1;
            }
        }
        return false;
    }

    static bool LooksLikeTuiScript(ScriptFacts script, string? profile)
    {
        if (!string.IsNullOrWhiteSpace(profile) && profile.Contains("tui", StringComparison.OrdinalIgnoreCase))
            return true;
        if (script.Path.Contains("tui", StringComparison.OrdinalIgnoreCase)) return true;
        return script.MentionsTui;
    }

    static bool IsTuiPackageName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Equals("dsh-tui", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("/dsh-tui", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("@deepseek-harness-tui/", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>profile 的 package.json：dependencies 里的 dsh-tui 系包 + dsh.profile.bundles。</summary>
    static List<(string Package, string? Spec)> TuiDeclarations(string packageJsonPath)
    {
        var list = new List<(string Package, string? Spec)>();
        var text = ReadTextCapped(packageJsonPath, MaxPackageJsonBytes);
        if (string.IsNullOrWhiteSpace(text)) return list;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return list;

            if (root.TryGetProperty("dependencies", out var dependencies)
                && dependencies.ValueKind == JsonValueKind.Object)
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (!IsTuiPackageName(dependency.Name)) continue;
                    var spec = dependency.Value.ValueKind == JsonValueKind.String
                        ? dependency.Value.GetString()
                        : null;
                    AddDeclaration(list, dependency.Name, spec);
                }
            }

            if (root.TryGetProperty("dsh", out var dsh) && dsh.ValueKind == JsonValueKind.Object
                && dsh.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.Object
                && profile.TryGetProperty("bundles", out var bundles) && bundles.ValueKind == JsonValueKind.Array)
            {
                foreach (var bundle in bundles.EnumerateArray())
                {
                    if (bundle.ValueKind != JsonValueKind.String) continue;
                    var name = bundle.GetString();
                    if (name is not null && IsTuiPackageName(name)) AddDeclaration(list, name, null);
                }
            }
        }
        catch
        {
            // package.json 坏了：当作没声明
        }
        return list;
    }

    static void AddDeclaration(List<(string Package, string? Spec)> list, string name, string? spec)
    {
        foreach (var existing in list)
            if (string.Equals(existing.Package, name, StringComparison.OrdinalIgnoreCase)) return;
        list.Add((name, spec));
    }

    // ---- 内核 --------------------------------------------------------------

    static void BuildKernels(Scan scan, List<Kernel> shellKernels, List<Kernel> kernels)
    {
        // 启动脚本里的 @deepseek-ai/dsh@ver：可能没有实体包，但对 TUI 有意义。
        var declared = new List<Kernel>();
        foreach (var script in scan.Scripts.ToList())
        {
            foreach (var spec in script.Declared)
            {
                var kernel = DshKernels.FromDeclared(spec, "launcher script: " + script.Path, script.Path);
                if (kernel is not null) declared.Add(kernel);
            }
        }

        var parts = new List<IEnumerable<Kernel>>
        {
            DshKernels.FromKnownRoots(),
            DshKernels.FromLauncherStore(scan.Launcher),
            shellKernels,
            declared,
            scan.ExtraKernels,
        };
        var padKernel = PadConfigKernel(scan);
        if (padKernel is not null) parts.Add(new[] { padKernel });

        var merged = DshKernels.Merge(parts.ToArray()).ToList();

        // Merge 的规则是「同版本已有实体包时，纯声明不单列」。但启动脚本仍然写着
        // 它要 npx 哪个版本，这份“声明”是独立事实：保留它，TUI 那行才有内核可看。
        foreach (var kernel in declared)
        {
            var seen = false;
            foreach (var existing in merged)
            {
                if (!existing.IsSameKernel(kernel)) continue;
                seen = true;
                break;
            }
            if (!seen) merged.Add(kernel);
        }

        merged.Sort((a, b) =>
        {
            var byVersion = string.Compare(b.Version, a.Version, StringComparison.OrdinalIgnoreCase);
            return byVersion != 0
                ? byVersion
                : string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);
        });
        kernels.AddRange(merged);
    }

    /// <summary>pad.json#launch.dshCli 指向真实包目录时，补一份 FromPackageDir 内核。</summary>
    static Kernel? PadConfigKernel(Scan scan)
    {
        var bin = scan.SettingsDshCli;
        if (string.IsNullOrWhiteSpace(bin)) return null;
        if (bin.Contains(".asar", StringComparison.OrdinalIgnoreCase)) return null;
        var dir = Path.GetDirectoryName(bin);
        if (string.IsNullOrWhiteSpace(dir)) return null;
        var packageDir = string.Equals(Path.GetFileName(dir), "lib", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(dir)
            : dir;
        if (string.IsNullOrWhiteSpace(packageDir) || !Directory.Exists(packageDir)) return null;
        return DshKernels.FromPackageDir(packageDir, "pad.json#launch.dshCli");
    }

    // ---- CLI ---------------------------------------------------------------

    static void BuildClis(Scan scan, List<Shell> shells, List<Kernel> kernels, List<DshCli> clis)
    {
        void Add(string? bin, string? version, string found, string? identity)
        {
            var full = FullPath(bin);
            if (full is null || !LaunchPolicy.Exists(full)) return;
            foreach (var existing in clis)
                if (string.Equals(existing.Bin, full, StringComparison.OrdinalIgnoreCase)) return;
            var (running, pid) = CliRunning(scan, full);
            clis.Add(new DshCli(full, version ?? GuessCliVersion(full), found, identity, running, pid));
        }

        Add(scan.SettingsDshCli, null, "pad.json#launch.dshCli", null);

        List<DshRelease> releases;
        try
        {
            releases = scan.Launcher.Releases();
        }
        catch
        {
            releases = [];
        }
        foreach (var release in releases)
        {
            string? bin = null;
            try
            {
                bin = string.IsNullOrWhiteSpace(release.Bin) ? scan.Launcher.DshBin(release.Version) : release.Bin;
            }
            catch
            {
                // 版本记录坏了：跳过这个 release
            }
            if (string.IsNullOrWhiteSpace(bin)) continue;
            Add(bin, release.Version, "pad version store", null);
        }

        foreach (var kernel in kernels)
        {
            if (string.IsNullOrWhiteSpace(kernel.Bin)) continue;
            if (!LaunchPolicy.Exists(kernel.Bin)) continue;
            Add(kernel.Bin, kernel.Version, "kernel: " + kernel.Source, kernel.Identity);
        }

        var pathCli = SafeWhich("dsh");
        if (pathCli is not null) Add(pathCli, null, "PATH", null);
        var sniffed = SafeSniffBin();
        if (sniffed is not null) Add(sniffed, null, "sniff", null);

        foreach (var shell in shells)
        {
            var exeDir = Path.GetDirectoryName(shell.Exe);
            if (string.IsNullOrWhiteSpace(exeDir)) continue;

            // 桌面端自带的 shim：resources\runtime\cli\bin\dsh.cmd / dsh.exe
            foreach (var name in new[] { "dsh.cmd", "dsh.exe", "dsh" })
            {
                var shim = Path.Combine(exeDir, "resources", "runtime", "cli", "bin", name);
                if (!LaunchPolicy.Exists(shim)) continue;
                Add(shim, shell.KernelVersion, "shim: " + shim, shell.KernelIdentity);
            }

            // 壳 asar 里的 dsh CLI（版本 = 壳内核版本；不作为独立内核重复计）。
            if (!string.IsNullOrWhiteSpace(shell.KernelIdentity))
            {
                var at = shell.KernelIdentity.IndexOf(".asar!", StringComparison.OrdinalIgnoreCase);
                if (at > 0)
                {
                    var archive = shell.KernelIdentity[..(at + 5)];
                    var inner = shell.KernelIdentity[(at + 6)..];
                    var bin = archive + "\\" + inner.Replace('/', '\\') + "\\lib\\bin.js";
                    Add(bin, shell.KernelVersion, "desktop bundle: " + shell.Exe, shell.KernelIdentity);
                }
            }
        }

        clis.Sort((a, b) => string.Compare(a.Bin, b.Bin, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// CLI 在跑 = 快照里某条进程的命令行指向它的 bin（大小写不敏感、/ 与 \ 等价），
    /// 或它的脚本参数正好等于这个 bin。.cmd/.bat shim 自己不会出现在命令行里，
    /// 所以再把 shim 内容里指向的 .js 路径（含 asar 内）当作 needle 匹配一次。
    /// </summary>
    static (bool Running, int? Pid) CliRunning(Scan scan, string bin)
    {
        var needles = CliNeedles(bin);
        if (needles.Count == 0) return (false, null);

        // 第一遍：脚本参数正好是它的 bin（node / Electron-as-node 的真实入口）——这是最可靠的 pid。
        foreach (var proc in scan.Procs)
        {
            var commandLine = proc.CommandLine;
            if (string.IsNullOrWhiteSpace(commandLine)) continue;
            string? script;
            try
            {
                script = ProcTable.ScriptArg(commandLine);
            }
            catch
            {
                script = null;
            }
            if (string.IsNullOrWhiteSpace(script)) continue;

            var normalizedScript = NormalizeMatch(script).TrimEnd('\\');
            foreach (var needle in needles)
            {
                if (needle.Length < 3) continue;
                if (string.Equals(normalizedScript, needle.TrimEnd('\\'), StringComparison.Ordinal))
                    return (true, proc.Pid);
            }
        }

        // 第二遍：某个「参数 token」正好是 bin（shim wrapper / npx 启动的 node）。
        // 不拿整条命令行做子串：一条 PowerShell 命令的脚本文本里也可能出现这个路径，
        // 那只是"提到"不是"在跑"（实测会把 DSH 自己的 subprocess 误判成 CLI 在跑）。
        foreach (var proc in scan.Procs)
        {
            var commandLine = proc.CommandLine;
            if (string.IsNullOrWhiteSpace(commandLine)) continue;
            IReadOnlyList<string> args;
            try { args = ProcTable.SplitArgs(commandLine); }
            catch { continue; }
            foreach (var token in args)
            {
                var normalizedToken = NormalizeMatch(token).TrimEnd('\\');
                if (normalizedToken.Length < 3) continue;
                foreach (var needle in needles)
                {
                    if (needle.Length < 3) continue;
                    if (string.Equals(normalizedToken, needle.TrimEnd('\\'), StringComparison.Ordinal))
                        return (true, proc.Pid);
                }
            }
        }
        return (false, null);
    }

    static List<string> CliNeedles(string bin)
    {
        var needles = new List<string>();
        var normalized = NormalizeMatch(bin);
        if (normalized.Length >= 3) needles.Add(normalized);

        // shim 的文本里写着真正的入口（本机 dsh.cmd → app.asar\...\dsh-desktop-host\lib\cli.js）。
        if (bin.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || bin.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var text = ReadTextCapped(bin, ScriptByteCap);
            if (string.IsNullOrWhiteSpace(text)) return needles;
            var shimDir = Path.GetDirectoryName(bin);
            foreach (Match match in JsTokenRegex.Matches(text!))
            {
                var token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (string.IsNullOrWhiteSpace(token)) continue;
                var expanded = ExpandPath(token, shimDir);
                var needle = NormalizeMatch(expanded ?? token);
                if (needle.Length >= 3 && !needles.Contains(needle, StringComparer.Ordinal)) needles.Add(needle);
            }
        }
        return needles;
    }

    /// <summary>只给「命令行里找路径」用的归一化：分隔符统一、去重复斜杠、小写、trim。</summary>
    static string NormalizeMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Trim().Replace('/', '\\');
        while (text.Contains("\\\\", StringComparison.Ordinal))
            text = text.Replace("\\\\", "\\", StringComparison.Ordinal);
        return text.ToLowerInvariant();
    }

    /// <summary>CLI 版本：asar 内读包 package.json；真实包读 bin.js 上两级的 package.json。</summary>
    static string? GuessCliVersion(string bin)
    {
        try
        {
            var at = bin.IndexOf(".asar", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                var archive = bin[..(at + 5)];
                var inner = bin[(at + 5)..].TrimStart('\\', '/').Replace('\\', '/');
                const string marker = "/lib/bin.js";
                var cut = inner.EndsWith(marker, StringComparison.OrdinalIgnoreCase)
                    ? inner.Length - marker.Length
                    : inner.Length;
                var packageInner = inner[..cut].TrimEnd('/');
                if (packageInner.Length == 0) return null;
                var asar = Asar.Open(archive);
                return asar is null ? null : VersionFromJson(asar.ReadText(packageInner + "/package.json"));
            }

            var dir = Path.GetDirectoryName(bin);
            if (string.IsNullOrWhiteSpace(dir)) return null;
            var packageDir = string.Equals(Path.GetFileName(dir), "lib", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(dir)
                : dir;
            return packageDir is null ? null : ReadPackageVersion(Path.Combine(packageDir, "package.json"));
        }
        catch
        {
            return null;
        }
    }

    static string? SafeWhich(string name)
    {
        try { return Proc.Which(name); } catch { return null; }
    }

    static string? SafeSniffBin()
    {
        try { return DshSniff.Bin(); } catch { return null; }
    }

    // ---- 脚本解析 ----------------------------------------------------------

    /// <summary>
    /// 解析一个启动脚本：DSH_HOME / --profile / @deepseek-ai/dsh@ver，
    /// 并跟进它引用的脚本（≤3 层）和 Electron+DSH 的 exe。
    /// 每个脚本只读一次（按路径缓存），环由 stack 挡住。
    /// </summary>
    static ScriptFacts? ParseScript(Scan scan, string? scriptPath, int depth, HashSet<string> stack)
    {
        var full = FullPath(scriptPath);
        if (full is null || !File.Exists(full)) return null;
        if (scan.ScriptsByPath.TryGetValue(full, out var cached)) return cached;

        var facts = new ScriptFacts { Path = full };
        scan.ScriptsByPath[full] = facts;
        scan.Scripts.Add(facts);

        if (depth > ScriptDepthCap || !stack.Add(full)) return facts;
        try
        {
            var text = ReadTextCapped(full, ScriptByteCap);
            if (string.IsNullOrWhiteSpace(text)) return facts;
            facts.MentionsTui = text.Contains("tui", StringComparison.OrdinalIgnoreCase);

            var baseDir = Path.GetDirectoryName(full);

            foreach (Match match in HomeRegex.Matches(text))
            {
                var value = match.Groups[1].Value.Trim().Trim('"', '\'');
                if (value.Length == 0 || value.Contains('%') || value.Contains('$')) continue;
                var expanded = ExpandPath(value, baseDir);
                if (expanded is null || !DshSniff.LooksLikeHome(expanded)) continue;
                if (!facts.Homes.Contains(expanded, StringComparer.OrdinalIgnoreCase)) facts.Homes.Add(expanded);
            }

            foreach (Match match in ProfileRegex.Matches(text))
            {
                var value = match.Groups[1].Value.Trim();
                if (value.Length == 0) continue;
                if (!facts.Profiles.Contains(value, StringComparer.OrdinalIgnoreCase)) facts.Profiles.Add(value);
            }

            foreach (Match match in DeclaredRegex.Matches(text))
            {
                var spec = match.Value;
                if (!facts.Declared.Contains(spec, StringComparer.OrdinalIgnoreCase)) facts.Declared.Add(spec);
            }

            foreach (Match match in TokenRegex.Matches(text))
            {
                var token = match.Groups[1].Success ? match.Groups[1].Value
                    : match.Groups[2].Success ? match.Groups[2].Value
                    : match.Groups[3].Value;
                if (string.IsNullOrWhiteSpace(token)) continue;

                var target = ExpandPath(token, baseDir);
                if (target is null || !File.Exists(target)) continue;

                if (IsScriptPath(target))
                {
                    if (depth >= ScriptDepthCap) continue;
                    var child = ParseScript(scan, target, depth + 1, stack);
                    if (child is null) continue;
                    MergeFacts(facts, child);
                }
                else if (IsExePath(target))
                {
                    if (!facts.Exes.Contains(target, StringComparer.OrdinalIgnoreCase)) facts.Exes.Add(target);
                    if (IsElectronDsh(scan, Path.GetDirectoryName(target)))
                        AddShell(scan, target, "launcher script: " + full, facts: new[] { facts }, verified: true);
                }
            }
        }
        catch
        {
            // 坏脚本：已经记下的部分照常返回
        }
        finally
        {
            stack.Remove(full);
        }
        return facts;
    }

    static void MergeFacts(ScriptFacts target, ScriptFacts source)
    {
        foreach (var home in source.Homes)
            if (!target.Homes.Contains(home, StringComparer.OrdinalIgnoreCase)) target.Homes.Add(home);
        foreach (var profile in source.Profiles)
            if (!target.Profiles.Contains(profile, StringComparer.OrdinalIgnoreCase)) target.Profiles.Add(profile);
        foreach (var declared in source.Declared)
            if (!target.Declared.Contains(declared, StringComparer.OrdinalIgnoreCase)) target.Declared.Add(declared);
        foreach (var exe in source.Exes)
            if (!target.Exes.Contains(exe, StringComparer.OrdinalIgnoreCase)) target.Exes.Add(exe);
        if (source.MentionsTui) target.MentionsTui = true;
    }

    /// <summary>一个目录顶层的启动脚本（≤20 个）。</summary>
    static List<ScriptFacts> ScanDirScripts(Scan scan, string? dir, int cap)
    {
        var list = new List<ScriptFacts>();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return list;
        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!IsScriptPath(file)) continue;
                if (++count > cap) break;
                var facts = ParseScript(scan, file, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (facts is not null && !list.Contains(facts)) list.Add(facts);
            }
        }
        catch
        {
            // 目录读失败：收多少算多少
        }
        return list;
    }

    // ---- 小工具 ------------------------------------------------------------

    static string Folder(Environment.SpecialFolder folder)
    {
        try { return Environment.GetFolderPath(folder); } catch { return ""; }
    }

    static string? SafeProcessPath()
    {
        try { return Environment.ProcessPath; } catch { return null; }
    }

    /// <summary>进程环境优先，其次 HKCU\Environment（用户级 setx 是常见装法）。</summary>
    static string? ReadEnvHome()
    {
        try
        {
            var process = Environment.GetEnvironmentVariable("DSH_HOME");
            var full = NormalizePath(process);
            if (full is not null && DshSniff.LooksLikeHome(full)) return full;
        }
        catch
        {
            // 环境变量坏了：看注册表
        }
        try
        {
            var key = Registry.CurrentUser.OpenSubKey("Environment");
            if (key is null) return null;
            var text = key.GetValue("DSH_HOME") as string;
            var full = NormalizePath(text);
            return full is not null && DshSniff.LooksLikeHome(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    static bool SafeDesktopShellRunning(string? userDataDir)
    {
        if (string.IsNullOrWhiteSpace(userDataDir)) return false;
        try { return Nodes.DesktopShellRunning(userDataDir); } catch { return false; }
    }

    /// <summary>exe 的 FileVersion，其次 ProductVersion。</summary>
    static string? ExeVersion(string exe)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            var fileVersion = info.FileVersion?.Trim();
            if (!string.IsNullOrWhiteSpace(fileVersion)) return fileVersion;
            var productVersion = info.ProductVersion?.Trim();
            return string.IsNullOrWhiteSpace(productVersion) ? null : productVersion;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>名字归一化：取末段、只留字母数字、小写（dsh-desktop ↔ DSH Desktop）。</summary>
    static string NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var last = raw;
        var slash = last.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) last = last[(slash + 1)..];
        var builder = new StringBuilder(last.Length);
        foreach (var ch in last)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    static bool NameLooksDsh(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Contains("dsh", StringComparison.OrdinalIgnoreCase)
               || name.Contains("harness", StringComparison.OrdinalIgnoreCase)
               || name.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
    }

    static bool HasChildDir(string? dir, string name)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        try { return Directory.Exists(Path.Combine(dir, name)); } catch { return false; }
    }

    static IReadOnlyList<string> SafeDirs(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return [];
        try { return Directory.GetDirectories(dir); } catch { return []; }
    }

    static bool IsScriptPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var extension = Path.GetExtension(path.Trim());
        foreach (var candidate in ScriptExtensions)
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static bool IsExePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Path.GetExtension(path.Trim()).Equals(".exe", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    static string? FullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            return string.IsNullOrWhiteSpace(full) ? null : full;
        }
        catch
        {
            return null;
        }
    }

    static string? NormalizePath(string? path)
    {
        var full = FullPath(path);
        if (full is null) return null;
        var trimmed = full.TrimEnd('\\', '/');
        if (trimmed.Length == 0) return full;
        return trimmed;
    }

    static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return true;
        var left = NormalizePath(a);
        var right = NormalizePath(b);
        return left is not null && right is not null
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    static string? ExpandPath(string? raw, string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var text = raw.Trim().Trim('"', '\'');
            if (text.Length == 0) return null;
            var dir = string.IsNullOrWhiteSpace(baseDir) ? "" : baseDir.TrimEnd('\\', '/');

            text = text.Replace("%~dp0", dir + "\\", StringComparison.OrdinalIgnoreCase)
                .Replace("%~dp", dir, StringComparison.OrdinalIgnoreCase);
            if (text.Contains('%')) text = Environment.ExpandEnvironmentVariables(text);
            text = text.Trim();
            if (text.Length == 0) return null;
            if (text.StartsWith(".\\", StringComparison.Ordinal) || text.StartsWith("./", StringComparison.Ordinal))
                text = text[2..];
            if (!Path.IsPathRooted(text) && dir.Length > 0) text = Path.Combine(dir, text);
            return Path.GetFullPath(text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>脚本里引用的路径 token：双引号、单引号、以及裸的盘符路径。</summary>
    static IEnumerable<string> PathTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        MatchCollection? matches = null;
        try
        {
            matches = TokenRegex.Matches(text);
        }
        catch
        {
            // 病态输入：当作没有路径 token
        }
        if (matches is null) yield break;
        foreach (Match match in matches)
        {
            var token = match.Groups[1].Success ? match.Groups[1].Value
                : match.Groups[2].Success ? match.Groups[2].Value
                : match.Groups[3].Value;
            if (!string.IsNullOrWhiteSpace(token)) yield return token;
        }
    }

    static string? ReadPackageVersion(string? packageJsonPath)
    {
        var text = ReadTextCapped(packageJsonPath, MaxPackageJsonBytes);
        return VersionFromJson(text);
    }

    static string? VersionFromJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text.TrimStart('\uFEFF'));
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("version", out var version)
                   && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读文本文件，最多 maxBytes；失败返回 null（不抛）。</summary>
    static string? ReadTextCapped(string? path, long maxBytes)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var length = (int)Math.Min(info.Length, maxBytes);
            if (length <= 0) return "";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[length];
            var read = 0;
            while (read < length)
            {
                var got = stream.Read(buffer, read, length - read);
                if (got <= 0) break;
                read += got;
            }
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch
        {
            return null;
        }
    }

    static void ReleaseCom(object? value)
    {
        if (value is null) return;
        try
        {
            if (Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // 已释放 / 不是 COM：忽略
        }
    }

    // ---- 正则 --------------------------------------------------------------

    /// <summary>DisplayName 像 DSH 的卸载项：/dsh|deepseek.*harness|harness/i。</summary>
    static readonly Regex DshDisplayName = new(@"dsh|deepseek.*harness|harness",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary><c>set "DSH_HOME=..."</c> / <c>$env:DSH_HOME = '...'</c> / <c>DSH_HOME=...</c>。</summary>
    static readonly Regex HomeRegex = new(
        @"(?:set\s+""?DSH_HOME|set\s+DSH_HOME|\$env:DSH_HOME|(?:^|[\s""'])DSH_HOME)\s*=\s*['""]?([^'""\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled);

    static readonly Regex ProfileRegex = new(@"--profile(?:\s+|=)([A-Za-z0-9._-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    static readonly Regex DeclaredRegex = new(@"@deepseek-ai/dsh@[0-9][^\s""',;)]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>脚本里的路径 token：双引号 / 单引号 / 裸盘符路径（.exe .cmd .bat .ps1）。</summary>
    static readonly Regex TokenRegex = new(
        "\"([^\"\\r\\n]{2,512})\"|'([^'\\r\\n]{2,512})'|([A-Za-z]:\\\\[^\"'\\r\\n\\t<>|*?]+?\\.(?:exe|cmd|bat|ps1))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>shim 文本里指向 .js 的路径（可带引号、%~dp0 前缀；asar 内入口也是 .js）。</summary>
    static readonly Regex JsTokenRegex = new(
        "\"([^\"\\r\\n]+\\.js)\"|'([^'\\r\\n]+\\.js)'|([^\\s\"']+\\.js)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
