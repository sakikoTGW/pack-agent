using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// macOS/Linux 的安装发现：没有注册表、没有开始菜单、没有 .lnk。
/// 所以扫的是：PATH 上的 dsh、全局 node_modules、/Applications 里的 .app、
/// 常见 DSH_HOME，以及调用方给的深扫根目录。
/// </summary>
public static class DiscoverPosix
{
    public static Inventory Run(Launcher launcher, IReadOnlyList<string> deepRoots, CancellationToken ct)
    {
        var kernels = new List<Kernel>();
        var shells = new List<Shell>();
        var clis = new List<DshCli>();
        var homes = new List<DshHome>();
        var kernelSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        void AddKernel(string pkgDir, string source, string? owner = null)
        {
            ct.ThrowIfCancellationRequested();
            var pkg = Path.Combine(pkgDir, "package.json");
            if (!File.Exists(pkg) || !kernelSeen.Add(pkgDir)) return;
            string? version = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
                version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            }
            catch { /* 坏 package.json 跳过 */ }
            var bin = Path.Combine(pkgDir, "lib", "bin.js");
            kernels.Add(new Kernel(version ?? "?", pkgDir, source,
                File.Exists(bin) ? bin : null, pkgDir, owner));
        }

        void AddHome(string? dir, string found)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            var profilesDir = Path.Combine(dir, "profiles");
            var storagesDir = Path.Combine(dir, "storages");
            if (!Directory.Exists(profilesDir) && !Directory.Exists(storagesDir)) return;
            if (homes.Any(h => string.Equals(h.Path, dir, StringComparison.OrdinalIgnoreCase))) return;

            var profiles = new List<string>();
            if (Directory.Exists(profilesDir))
                foreach (var d in Directory.EnumerateDirectories(profilesDir))
                {
                    var name = Path.GetFileName(d);
                    if (name is "node_modules") continue;
                    profiles.Add(name);
                }
            var sessions = Directory.Exists(storagesDir) ? Directory.EnumerateDirectories(storagesDir).Count() : 0;
            homes.Add(new DshHome(dir, profiles.OrderBy(n => n, StringComparer.Ordinal).ToList(), sessions, false, found));
        }

        // 1) PATH 上的 dsh（多半是指向全局包的 node 脚本）
        var dsh = Proc.Which("dsh");
        if (dsh is not null)
        {
            clis.Add(new DshCli(dsh, null, "PATH", null));
            var real = ResolveLink(dsh);
            if (real is not null)
            {
                var dir = Path.GetDirectoryName(real);
                if (dir is not null) AddKernel(Path.GetFullPath(Path.Combine(dir, "..")), "npm global");
            }
        }

        // 2) 全局 node_modules
        foreach (var p in new[]
                 {
                     "/usr/local/lib/node_modules/@deepseek-ai/dsh",
                     "/opt/homebrew/lib/node_modules/@deepseek-ai/dsh",
                     Path.Combine(userHome, ".npm-global/lib/node_modules/@deepseek-ai/dsh"),
                     Path.Combine(userHome, ".local/share/pnpm/global/5/node_modules/@deepseek-ai/dsh"),
                     Path.Combine(userHome, "Library/pnpm/global/5/node_modules/@deepseek-ai/dsh"),
                 })
            if (Directory.Exists(p)) AddKernel(p, "npm global");

        // 3) 桌面端 .app
        foreach (var appsDir in new[] { "/Applications", Path.Combine(userHome, "Applications") })
        {
            if (!Directory.Exists(appsDir)) continue;
            foreach (var app in Directory.EnumerateDirectories(appsDir, "*.app"))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(app);
                if (!LooksLikeShell(name)) continue;
                var exe = Path.Combine(app, "Contents", "MacOS", name);
                var userData = Path.Combine(userHome, "Library", "Application Support", name);
                var bundled = Path.Combine(app, "Contents", "Resources", "app.asar.unpacked", "node_modules", "@deepseek-ai", "dsh");
                string? kernelVersion = null;
                if (Directory.Exists(bundled)) AddKernel(bundled, "desktop bundle", File.Exists(exe) ? exe : app);
                shells.Add(new Shell(
                    Exe: File.Exists(exe) ? exe : app,
                    Version: ReadBundleVersion(app),
                    UserData: Directory.Exists(userData) ? userData : null,
                    Home: null,
                    Running: false,
                    Found: ".app",
                    KernelVersion: kernelVersion,
                    KernelIdentity: Directory.Exists(bundled) ? bundled : null));
            }
        }

        // 4) 常见 DSH_HOME
        AddHome(Environment.GetEnvironmentVariable("DSH_HOME"), "DSH_HOME");
        AddHome(Path.Combine(userHome, ".dsh"), "~/.dsh");
        AddHome(Path.Combine(userHome, ".deepseek"), "~/.deepseek");
        var docs = Path.Combine(userHome, "Documents", "deepseek-harness");
        if (Directory.Exists(docs))
            foreach (var d in Directory.EnumerateDirectories(docs)) AddHome(d, "~/Documents/deepseek-harness");

        // 5) 用户指定的深扫根
        foreach (var root in deepRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var pkgDir in FindDshPackages(root, depth: 4)) AddKernel(pkgDir, "deep root");
        }

        return new Inventory(kernels, shells, [], clis, homes);
    }

    static bool LooksLikeShell(string name) =>
        name.Contains("DSH", StringComparison.OrdinalIgnoreCase)
        || name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Harness", StringComparison.OrdinalIgnoreCase);

    static string? ResolveLink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is { } target)
                return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, target));
            return path;
        }
        catch { return path; }
    }

    static string? ReadBundleVersion(string app)
    {
        var plist = Path.Combine(app, "Contents", "Info.plist");
        if (!File.Exists(plist)) return null;
        try
        {
            var text = File.ReadAllText(plist);
            var m = System.Text.RegularExpressions.Regex.Match(
                text, "<key>CFBundleShortVersionString</key>\\s*<string>([^<]+)</string>");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }

    static IEnumerable<string> FindDshPackages(string root, int depth)
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root); }
        catch { yield break; }

        foreach (var d in dirs)
        {
            var pkg = Path.Combine(d, "package.json");
            if (File.Exists(pkg))
            {
                var isDsh = false;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
                    isDsh = doc.RootElement.TryGetProperty("name", out var n)
                            && n.GetString() == "@deepseek-ai/dsh";
                }
                catch { /* 忽略坏 json */ }
                if (isDsh) yield return d;
            }

            if (depth > 1)
                foreach (var sub in FindDshPackages(d, depth - 1)) yield return sub;
        }
    }
}
