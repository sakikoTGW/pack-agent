using System.IO;
using System.Text;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// 这台机器上所有的 <c>@deepseek-ai/dsh</c> 内核。内核 = 磁盘上的一份 DSH 包；
/// 桌面端 / TUI / CLI 只是把它拉起来的载具。
///
/// 三条证据链，按可信度排序：
/// <list type="number">
/// <item>真实包目录（npm global、npx 缓存、PAD 版本库……）：Identity 是绝对路径。</item>
/// <item>桌面端 asar 里捆的包：Identity 是 <c>&lt;archive&gt;!&lt;innerDir&gt;</c>，Bin 是 asar 内路径。</item>
/// <item>只有启动脚本里的声明（<c>npx @deepseek-ai/dsh@ver</c>）：Identity 是
/// <c>declared:@deepseek-ai/dsh@&lt;ver&gt;</c>，Bin/PackageDir 都是 null。</item>
/// </list>
///
/// 约束：不 shell out（不跑 which/npm/wmic），不无界遍历（固定清单 + <see cref="Cap"/> 上限），
/// 每个候选都 try/catch 跳过，任何异常都不向调用方抛；可在 worker thread 上直接跑。
/// </summary>
public static class DshKernels
{
    /// <summary>内核包名。</summary>
    public const string PackageName = "@deepseek-ai/dsh";

    /// <summary>一次扫描最多看这么多候选目录，防止无界遍历。</summary>
    public const int Cap = 200;

    /// <summary>PAD 版本库里的内核。</summary>
    const string VersionStoreSource = "pad version store";

    /// <summary>只有声明、没有实体包时的 Identity 前缀。</summary>
    const string DeclaredPrefix = "declared:";

    /// <summary>package.json 的读取上限；正常只有几 KB。</summary>
    const long MaxPackageJsonBytes = 8L * 1024 * 1024;

    static readonly Lazy<Dictionary<string, string>> KnownRootLabels = new(() =>
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (root, source) in KnownRootCandidates()) map.TryAdd(root, source);
        }
        catch
        {
            // 标签表只是给人看的，建不出来就用路径推断
        }
        return map;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    // ---- 扫描入口 ----------------------------------------------------------

    /// <summary>每个 module root 下找 <c>@deepseek-ai/dsh</c>（root 就是 node_modules 目录）。</summary>
    public static IEnumerable<Kernel> FromModuleRoots(IEnumerable<string> moduleRoots)
    {
        var kernels = new List<Kernel>();
        if (moduleRoots is null) return kernels;
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var count = 0;
            foreach (var root in moduleRoots)
            {
                if (++count > Cap) break;
                var kernel = FromModuleRoot(root, LabelForRoot(root));
                if (kernel is not null && seen.Add(kernel.Identity)) kernels.Add(kernel);
            }
        }
        catch
        {
            // 某个 root 抛了：已找到的照常返回
        }
        return kernels;
    }

    /// <summary>
    /// npm / bun / yarn / pnpm global、npx 缓存（_npx）、nvm、volta 的固定清单。
    /// 不 shell out：只按已知目录结构找，候选目录总数封顶 <see cref="Cap"/>。
    /// </summary>
    public static IEnumerable<Kernel> FromKnownRoots()
    {
        var kernels = new List<Kernel>();
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (root, source) in KnownRootCandidates())
            {
                var kernel = FromModuleRoot(root, source);
                if (kernel is not null && seen.Add(kernel.Identity)) kernels.Add(kernel);
            }
        }
        catch
        {
            // 已找到的照常返回
        }
        return kernels;
    }

    /// <summary>PAD 版本库：优先 version.json 里的 bin，否则版本目录的 node_modules 布局。</summary>
    public static IEnumerable<Kernel> FromLauncherStore(Launcher launcher)
    {
        var kernels = new List<Kernel>();
        if (launcher is null) return kernels;
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var count = 0;
            foreach (var release in launcher.Releases())
            {
                if (++count > Cap) break;
                var kernel = KernelFromRelease(launcher, release);
                if (kernel is not null && seen.Add(kernel.Identity)) kernels.Add(kernel);
            }
        }
        catch
        {
            // 版本库读不动时返回空清单，不炸探测
        }
        return kernels;
    }

    /// <summary>
    /// 桌面端 exe：在 <c>&lt;exeDir&gt;\resources\app.asar</c> 里找
    /// <c>/@deepseek-ai/dsh/package.json</c>，取层级最短的一条。
    /// Source 固定为 <c>"desktop bundle: " + exe</c>。
    /// </summary>
    public static IEnumerable<Kernel> FromShell(string exe, string found)
    {
        var kernels = new List<Kernel>();
        try
        {
            if (string.IsNullOrWhiteSpace(exe)) return kernels;

            string fullExe;
            string? exeDir;
            try
            {
                fullExe = Path.GetFullPath(exe.Trim().Trim('"'));
                exeDir = Path.GetDirectoryName(fullExe);
            }
            catch
            {
                return kernels;
            }
            if (string.IsNullOrWhiteSpace(exeDir)) return kernels;

            var archivePath = Path.Combine(exeDir, "resources", "app.asar");
            if (!File.Exists(archivePath)) return kernels;

            var asar = Asar.Open(archivePath);
            if (asar is null) return kernels;

            var packageJsonPath = ShallowestDshPackageJson(asar);
            if (packageJsonPath is null) return kernels;

            var innerDir = packageJsonPath[..^"/package.json".Length];
            var kernel = FromAsar(asar, innerDir, "desktop bundle: " + fullExe, fullExe);
            if (kernel is not null) kernels.Add(kernel);
        }
        catch
        {
            // 桌面端 asar 可能是任意旧格式：当作没找到
        }
        return kernels;
    }

    // ---- 单个内核 ----------------------------------------------------------

    /// <summary>asar 内目录 → Kernel。Identity = <c>archive.asar!innerDir</c>，PackageDir = null。</summary>
    public static Kernel? FromAsar(Asar asar, string innerDir, string source, string? ownerExe)
    {
        if (asar is null) return null;
        try
        {
            var inner = NormalizeInner(innerDir);
            if (inner is null) return null;
            if (inner.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase))
                inner = inner[..^"/package.json".Length];
            if (inner.Length == 0) return null;

            var (_, version) = ReadPackageFields(asar.ReadText(inner + "/package.json"));
            if (string.IsNullOrWhiteSpace(version)) return null;

            var identity = asar.ArchivePath + "!" + inner;
            // asar 内路径不是真文件；存在性交给 LaunchPolicy.Exists 判断，这里不查。
            var bin = asar.ArchivePath + "\\" + inner.Replace('/', '\\') + "\\lib\\bin.js";
            return new Kernel(version, identity, source, bin, PackageDir: null, OwnerExe: ownerExe);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>真实包目录 → Kernel。Identity = 绝对路径，PackageDir = 目录，Bin = 目录\lib\bin.js。</summary>
    public static Kernel? FromPackageDir(string packageDir, string source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(packageDir)) return null;
            var dir = Path.GetFullPath(packageDir.Trim().Trim('"'));
            if (!Directory.Exists(dir)) return null;

            var (name, version) = ReadPackageFields(ReadShared(Path.Combine(dir, "package.json")));
            if (string.IsNullOrWhiteSpace(version)) return null;
            // package.json 没写 name 也接受（旧打包 / fixture），写了就必须是内核包。
            if (!string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, PackageName, StringComparison.OrdinalIgnoreCase))
                return null;

            return new Kernel(version, dir, source,
                Bin: Path.Combine(dir, "lib", "bin.js"),
                PackageDir: dir,
                OwnerExe: null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 只有声明（启动脚本里的 <c>npx @deepseek-ai/dsh@0.1.0-rc.7</c>），没有实体包。
    /// Identity = <c>declared:@deepseek-ai/dsh@&lt;ver&gt;</c>，Bin/PackageDir 都是 null。
    /// </summary>
    public static Kernel? FromDeclared(string spec, string source, string? launcher = null)
    {
        try
        {
            var version = ParseVersionFromSpec(spec);
            if (string.IsNullOrWhiteSpace(version)) return null;

            var label = !string.IsNullOrWhiteSpace(source)
                ? source
                : string.IsNullOrWhiteSpace(launcher)
                    ? "declared"
                    : "launcher script: " + launcher.Trim();

            return new Kernel(version, DeclaredPrefix + PackageName + "@" + version,
                label, Bin: null, PackageDir: null, OwnerExe: null);
        }
        catch
        {
            return null;
        }
    }

    // ---- 合并 --------------------------------------------------------------

    /// <summary>
    /// 多路探测结果合并：按 Identity（OrdinalIgnoreCase）去重，有 Bin 的真实包优先于纯声明，
    /// 同一份内核从多条路找到时保留第一个 Source。同一版本已有实体包时，声明不再单列。
    /// </summary>
    public static IEnumerable<Kernel> Merge(params IEnumerable<Kernel>[] parts)
    {
        var order = new List<string>();
        var groups = new Dictionary<string, List<Kernel>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (parts is not null)
            {
                foreach (var part in parts)
                {
                    if (part is null) continue;
                    foreach (var kernel in part)
                    {
                        if (kernel is null || string.IsNullOrWhiteSpace(kernel.Identity)) continue;
                        if (!groups.TryGetValue(kernel.Identity, out var list))
                        {
                            list = [];
                            groups[kernel.Identity] = list;
                            order.Add(kernel.Identity);
                        }
                        list.Add(kernel);
                    }
                }
            }
        }
        catch
        {
            // 某个来源枚举到一半抛了：已收集的照常合并
        }

        var merged = new List<Kernel>(order.Count);
        foreach (var identity in order)
        {
            var candidates = groups[identity];
            var winner = candidates[0];
            var best = Preference(winner);
            foreach (var candidate in candidates)
            {
                var score = Preference(candidate);
                if (score > best)
                {
                    winner = candidate;
                    best = score;
                }
            }

            // 保留第一个 Source：去重不改变“最先从哪找到的”这一事实。
            var firstSource = winner.Source;
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Source)) continue;
                firstSource = candidate.Source;
                break;
            }
            if (!string.IsNullOrWhiteSpace(firstSource)
                && !string.Equals(firstSource, winner.Source, StringComparison.Ordinal))
                winner = winner with { Source = firstSource };

            merged.Add(winner);
        }

        // 真实包优先于 declared：同一个版本已经落到磁盘上了，声明就不再是一条独立内核。
        var realVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kernel in merged)
        {
            if (IsReal(kernel) && !string.IsNullOrWhiteSpace(kernel.Version))
                realVersions.Add(kernel.Version);
        }

        var result = new List<Kernel>(merged.Count);
        foreach (var kernel in merged)
        {
            if (!IsReal(kernel) && realVersions.Contains(kernel.Version)) continue;
            result.Add(kernel);
        }
        return result;
    }

    // ---- 版本 / 路径小工具 --------------------------------------------------

    /// <summary>
    /// 从 npm spec 里取出版本号：<c>@deepseek-ai/dsh@0.1.0-rc.7</c>、<c>^0.1.0-rc.7</c>、
    /// <c>&gt;=0.2.0 &lt;0.3.0</c> → <c>0.1.0-rc.7</c> / <c>0.2.0</c>。取不到返回 null。
    /// </summary>
    public static string? ParseVersionFromSpec(string spec)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var text = spec.Trim().Trim('"', '\'');
            if (text.Length == 0) return null;

            // npm 别名：npm:@deepseek-ai/dsh@1.2.3
            if (text.StartsWith("npm:", StringComparison.OrdinalIgnoreCase)) text = text[4..];

            // 也接受整行启动脚本："npx --yes @deepseek-ai/dsh@0.1.0-rc.7 --profile dsh-tui"。
            var marker = PackageName + "@";
            var atMarker = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (atMarker >= 0) text = text[(atMarker + marker.Length)..];

            // 一个字段里可能塞了多个约束（">=1 <2"）或 "||"；只取第一个 token。
            var cut = text.IndexOfAny([' ', '\t', '|', ',']);
            if (cut > 0) text = text[..cut];

            // "@deepseek-ai/dsh@0.1.0-rc.7" / "dsh@0.1.0" → 取最后一个 '@' 之后
            var at = text.LastIndexOf('@');
            if (at > 0) text = text[(at + 1)..];

            // 范围前缀：^ ~ >= <= > < = v
            text = text.TrimStart('^', '~', '>', '=', '<', 'v', 'V', ' ');
            text = text.TrimEnd('"', '\'', ',', ';', ')', ']', '}');
            if (text.Length == 0 || !char.IsDigit(text[0])) return null;
            return text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>版本记录在、实体包没了：仍把版本报成声明，UI 才知道 TUI 打算跑哪个版本。</summary>
    static Kernel? KernelFromRelease(Launcher launcher, DshRelease release)
    {
        try
        {
            if (release is null || string.IsNullOrWhiteSpace(release.Version)) return null;

            var fromBin = FromPackageDir(PackageDirOfBin(release.Bin) ?? "", VersionStoreSource);
            if (fromBin is not null) return fromBin;

            var store = Path.Combine(launcher.VersionDir(release.Version),
                "node_modules", "@deepseek-ai", "dsh");
            var fromStore = FromPackageDir(store, VersionStoreSource);
            if (fromStore is not null) return fromStore;

            return FromDeclared("@" + PackageName + "@" + release.Version, VersionStoreSource);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>bin.js 的包目录 = 往上两级（lib 的父目录）；也接受直接给包目录的情形。</summary>
    static string? PackageDirOfBin(string? bin)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(bin)) return null;
            var full = Path.GetFullPath(bin.Trim().Trim('"'));
            var dir = Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(dir)) return null;
            if (string.Equals(Path.GetFileName(dir), "lib", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(dir);
                if (!string.IsNullOrWhiteSpace(parent)) return parent;
            }
            return File.Exists(Path.Combine(dir, "package.json")) ? dir : null;
        }
        catch
        {
            return null;
        }
    }

    static Kernel? FromModuleRoot(string? moduleRoot, string source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(moduleRoot)) return null;
            var root = Path.GetFullPath(moduleRoot.Trim());
            return FromPackageDir(Path.Combine(root, "@deepseek-ai", "dsh"), source);
        }
        catch
        {
            return null;
        }
    }

    static string? ShallowestDshPackageJson(Asar asar)
    {
        string? best = null;
        var bestDepth = int.MaxValue;
        foreach (var path in asar.Find("/@deepseek-ai/dsh/package.json", 8))
        {
            var depth = path.Split('/').Length;
            if (best is not null && depth >= bestDepth && path.Length >= best.Length) continue;
            if (best is null || depth < bestDepth || (depth == bestDepth && path.Length < best.Length))
            {
                best = path;
                bestDepth = depth;
            }
        }
        return best;
    }

    /// <summary>内部路径归一化：'/' 分隔、无前导斜杠、拒绝 '.'/'..'。</summary>
    static string? NormalizeInner(string? innerPath)
    {
        if (string.IsNullOrWhiteSpace(innerPath)) return null;
        var text = innerPath.Trim().Replace('\\', '/');
        while (text.StartsWith("./", StringComparison.Ordinal)) text = text[2..];
        text = text.Trim('/');
        if (text.Length == 0) return null;
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        foreach (var part in parts)
            if (part is "." or "..") return null;
        return string.Join('/', parts);
    }

    // ---- 读文件 ------------------------------------------------------------

    static (string? Name, string? Version) ReadPackageFields(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        try
        {
            text = text.TrimStart('\uFEFF');
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            var name = root.TryGetProperty("name", out var nameElement)
                       && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            var version = root.TryGetProperty("version", out var versionElement)
                          && versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()
                : null;
            return (name, version);
        }
        catch
        {
            return (null, null);
        }
    }

    static string? ReadShared(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxPackageJsonBytes) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    // ---- 已知目录清单 ------------------------------------------------------

    static List<(string Root, string Source)> KnownRootCandidates()
    {
        var roots = new List<(string Root, string Source)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? root, string source)
        {
            if (roots.Count >= Cap || string.IsNullOrWhiteSpace(root)) return;
            string full;
            try
            {
                full = Path.GetFullPath(root.Trim());
            }
            catch
            {
                return;
            }
            if (seen.Add(full)) roots.Add((full, source));
        }

        var appData = SpecialFolder(Environment.SpecialFolder.ApplicationData);
        var local = SpecialFolder(Environment.SpecialFolder.LocalApplicationData);
        var home = SpecialFolder(Environment.SpecialFolder.UserProfile);
        var programFiles = SpecialFolder(Environment.SpecialFolder.ProgramFiles);

        // npm global：用户 npm 前缀 + node 安装器自带前缀
        if (appData is not null) Add(Path.Combine(appData, "npm", "node_modules"), "npm global");
        if (programFiles is not null) Add(Path.Combine(programFiles, "nodejs", "node_modules"), "npm global");

        // bun global
        var bunInstall = Environment.GetEnvironmentVariable("BUN_INSTALL");
        if (!string.IsNullOrWhiteSpace(bunInstall))
            Add(Path.Combine(bunInstall.Trim(), "install", "global", "node_modules"), "bun global");
        if (home is not null)
            Add(Path.Combine(home, ".bun", "install", "global", "node_modules"), "bun global");

        // yarn global
        if (local is not null)
            Add(Path.Combine(local, "Yarn", "Data", "global", "node_modules"), "yarn global");
        if (home is not null)
            Add(Path.Combine(home, ".config", "yarn", "global", "node_modules"), "yarn global");

        // pnpm global：<pnpm home>/global 与 <pnpm home>/global/<N>/node_modules
        var pnpmHome = Environment.GetEnvironmentVariable("PNPM_HOME");
        foreach (var pnpm in new[]
                 {
                     local is null ? null : Path.Combine(local, "pnpm"),
                     string.IsNullOrWhiteSpace(pnpmHome) ? null : pnpmHome!.Trim(),
                 })
        {
            if (string.IsNullOrWhiteSpace(pnpm)) continue;
            var global = Path.Combine(pnpm!, "global");
            Add(Path.Combine(global, "node_modules"), "pnpm global");
            foreach (var dir in SafeDirs(global, 20))
                Add(Path.Combine(dir, "node_modules"), "pnpm global");
        }

        // npx 缓存：<npm cache>/_npx/<hash>/node_modules
        foreach (var cache in new[]
                 {
                     local is null ? null : Path.Combine(local, "npm-cache"),
                     appData is null ? null : Path.Combine(appData!, "npm-cache"),
                 })
        {
            if (string.IsNullOrWhiteSpace(cache)) continue;
            foreach (var dir in SafeDirs(Path.Combine(cache!, "_npx"), 60))
                Add(Path.Combine(dir, "node_modules"), "npx cache");
        }

        // nvm / nvm-windows：%APPDATA%\nvm\vX.Y.Z\node_modules 等
        var nvmHome = Environment.GetEnvironmentVariable("NVM_HOME");
        foreach (var nvm in new[]
                 {
                     appData is null ? null : Path.Combine(appData!, "nvm"),
                     string.IsNullOrWhiteSpace(nvmHome) ? null : nvmHome!.Trim(),
                 })
        {
            if (string.IsNullOrWhiteSpace(nvm)) continue;
            foreach (var dir in SafeDirs(nvm!, 40))
                Add(Path.Combine(dir, "node_modules"), "nvm");
        }
        if (home is not null)
            foreach (var dir in SafeDirs(Path.Combine(home, ".nvm", "versions", "node"), 40))
                Add(Path.Combine(dir, "lib", "node_modules"), "nvm");

        // volta：~/.volta/tools/image/packages
        if (home is not null)
        {
            var image = Path.Combine(home, ".volta", "tools", "image");
            Add(Path.Combine(image, "packages", "node_modules"), "volta");
            foreach (var dir in SafeDirs(Path.Combine(image, "packages"), 20))
                Add(Path.Combine(dir, "lib", "node_modules"), "volta");
        }
        if (local is not null)
            Add(Path.Combine(local, "Volta", "tools", "image", "packages", "node_modules"), "volta");

        return roots;
    }

    static List<string> SafeDirs(string? root, int cap)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(root)) return list;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                list.Add(dir);
                if (list.Count >= cap) break;
            }
        }
        catch
        {
            // 权限 / 半删目录：收多少算多少
        }
        return list;
    }

    static string? SpecialFolder(Environment.SpecialFolder folder)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
    }

    static string LabelForRoot(string? moduleRoot)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot)) return "module root";
        string full;
        try
        {
            full = Path.GetFullPath(moduleRoot.Trim());
        }
        catch
        {
            return "module root: " + moduleRoot;
        }
        if (KnownRootLabels.Value.TryGetValue(full, out var known)) return known;
        return LabelFromPath(full);
    }

    static string LabelFromPath(string full)
    {
        var text = full.ToLowerInvariant();
        if (text.Contains("_npx")) return "npx cache";
        if (text.Contains(".bun") || text.Contains("bun\\install") || text.Contains("bun/install"))
            return "bun global";
        if (text.Contains("pnpm")) return "pnpm global";
        if (text.Contains("yarn")) return "yarn global";
        if (text.Contains("nvm") || text.Contains("versions\\node")) return "nvm";
        if (text.Contains(".volta") || text.Contains("volta\\")) return "volta";
        if (text.Contains("\\npm\\node_modules") || text.Contains("\\nodejs\\node_modules"))
            return "npm global";
        return "module root: " + full;
    }

    // ---- 判重 --------------------------------------------------------------

    /// <summary>0 = 纯声明，1 = 有实体目录，2 = 有可执行入口（Bin）。</summary>
    static int Preference(Kernel kernel)
    {
        if (!string.IsNullOrWhiteSpace(kernel.Bin)) return 2;
        if (!string.IsNullOrWhiteSpace(kernel.PackageDir)) return 1;
        return 0;
    }

    static bool IsReal(Kernel kernel) => Preference(kernel) > 0;
}
