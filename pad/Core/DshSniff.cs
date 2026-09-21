using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// Find a DSH CLI and a DSH_HOME already on this machine.
/// PAD's version store is separate; sniffing only reports what is already installed.
/// </summary>
public static class DshSniff
{
    const int Cap = 200;

    public sealed record Report(
        string? Bin,
        string? Version,
        string? Tui,
        string Home,
        bool HomeExists,
        string? SourceCli);

    public static bool LooksLikeHome(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        return Directory.Exists(Path.Combine(path, "profiles"))
            || Directory.Exists(Path.Combine(path, "sessions"))
            || File.Exists(Path.Combine(path, ".credentials.yaml"));
    }

    /// <summary>A PAD launcher root on disk: instances, versions, or pad.json.</summary>
    public static bool LooksLikeLauncherRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        return LaunchPolicy.LooksLikeLauncherRoot(
            CountInstances(path), CountVersions(path),
            File.Exists(Path.Combine(path, "pad.json")));
    }

    public static int CountInstances(string path)
    {
        var inst = Path.Combine(path, "instances");
        if (!Directory.Exists(inst)) return 0;
        var n = 0;
        foreach (var dir in Take(SafeDirs(inst)))
            if (File.Exists(Path.Combine(dir, "instance.json"))) n++;
        return n;
    }

    public static int CountVersions(string path)
    {
        var ver = Path.Combine(path, "versions");
        if (!Directory.Exists(ver)) return 0;
        var n = 0;
        foreach (var dir in Take(SafeDirs(ver)))
            if (File.Exists(Path.Combine(dir, "version.json"))) n++;
        return n;
    }

    /// <summary>
    /// Other <c>.pack-launcher</c> folders on this machine.
    /// Named paths only: tmp's own <c>.pack-launcher</c>, checkout Debug/Release.
    /// Does not enumerate <c>AGENT_PACK_TMP</c> children (hundreds of pad-build
    /// dirs would freeze the UI thread → PA040).
    /// </summary>
    public static IEnumerable<string> LauncherRootCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in LauncherRootCandidateRaw())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string abs;
            try { abs = Path.GetFullPath(raw); }
            catch { continue; }
            if (!seen.Add(abs)) continue;
            if (Directory.Exists(abs)) yield return abs;
        }
    }

    static IEnumerable<string> LauncherRootCandidateRaw()
    {
        var tmp = Environment.GetEnvironmentVariable("AGENT_PACK_TMP");
        if (string.IsNullOrWhiteSpace(tmp)) tmp = @"E:\tmp\pack-agent";
        yield return Path.Combine(tmp, ".pack-launcher");
        foreach (var repo in RepoHints(tmp))
        {
            yield return Path.Combine(repo, "agent-pack-dsh", "pad", "bin", "Debug", "net9.0-windows", ".pack-launcher");
            yield return Path.Combine(repo, "agent-pack-dsh", "pad", "bin", "Release", "net9.0-windows", ".pack-launcher");
            yield return Path.Combine(repo, "pad", "bin", "Debug", "net9.0-windows", ".pack-launcher");
            yield return Path.Combine(repo, "pad", "bin", "Release", "net9.0-windows", ".pack-launcher");
        }
    }

    static IEnumerable<string> RepoHints(string tmp)
    {
        var env = Environment.GetEnvironmentVariable("PACK_AGENT_REPO");
        if (!string.IsNullOrWhiteSpace(env)) yield return env.Trim();

        foreach (var start in new[]
        {
            Path.GetDirectoryName(Environment.ProcessPath),
            AppContext.BaseDirectory,
        })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var cur = start;
            for (var i = 0; i < 10 && !string.IsNullOrWhiteSpace(cur); i++)
            {
                var marker = Path.Combine(cur, ".pack-agent-repo");
                if (File.Exists(marker))
                {
                    string text;
                    try { text = File.ReadAllText(marker).Trim(); }
                    catch { text = ""; }
                    if (!string.IsNullOrWhiteSpace(text)) yield return text;
                }
                if (Directory.Exists(Path.Combine(cur, "agent-pack-dsh", "pad")))
                    yield return cur;
                if (string.Equals(Path.GetFileName(cur), "agent-pack-dsh", StringComparison.OrdinalIgnoreCase)
                    && Directory.GetParent(cur) is { } packParent)
                    yield return packParent.FullName;
                cur = Directory.GetParent(cur)?.FullName;
            }
        }

        string? sibling = null;
        try
        {
            var grand = Directory.GetParent(tmp)?.Parent;
            if (grand is not null)
                sibling = Path.Combine(grand.FullName, "pack-agent");
        }
        catch { /* ignore */ }
        if (!string.IsNullOrWhiteSpace(sibling)) yield return sibling;
    }

    static string? _bin;
    static bool _binLooked;

    public static string? Bin()
    {
        if (_binLooked) return _bin;
        _binLooked = true;
        foreach (var candidate in BinCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                _bin = candidate;
                return _bin;
            }
        }
        return null;
    }

    /// <summary>Turn a picked file or folder into <c>lib/bin.js</c>.</summary>
    public static string? ResolveBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { path = Path.GetFullPath(path.Trim().Trim('"')); }
        catch { return null; }

        if (Directory.Exists(path))
        {
            foreach (var nested in new[]
            {
                Path.Combine(path, "lib", "bin.js"),
                Path.Combine(path, "apps", "cli", "lib", "bin.js"),
                Path.Combine(path, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"),
                Path.Combine(path, "@deepseek-ai", "dsh", "lib", "bin.js"),
            })
            {
                if (File.Exists(nested)) return nested;
            }
            return null;
        }

        if (!File.Exists(path)) return null;
        var name = Path.GetFileName(path);
        if (name.Equals("bin.js", StringComparison.OrdinalIgnoreCase)) return path;
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is null) return null;
            var bin = Path.Combine(dir, "lib", "bin.js");
            return File.Exists(bin) ? bin : null;
        }
        return BinBeside(path);
    }

    public static string? TuiPackage()
    {
        foreach (var root in ModuleRoots())
        {
            var dir = Path.Combine(root, "@deepseek-harness-tui", "dsh-tui");
            if (Directory.Exists(dir)) return dir;
        }
        foreach (var root in CheckoutRoots())
        {
            var dir = Path.Combine(root, "node_modules", "@deepseek-harness-tui", "dsh-tui");
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    public static string? SourceCli()
    {
        foreach (var root in CheckoutRoots())
        {
            var cli = Path.Combine(root, "apps", "cli");
            var pkg = Path.Combine(cli, "package.json");
            if (IsDshPackage(pkg) && !File.Exists(Path.Combine(cli, "lib", "bin.js")))
                return cli;
        }
        return null;
    }

    public static Report Probe(string? configuredHome, string? configuredTui = null)
    {
        var home = LaunchPolicy.DefaultDshHome(configuredHome);
        var bin = Bin();
        string? version = null;
        if (bin is not null)
        {
            var pkg = Path.Combine(Path.GetDirectoryName(bin)!, "..", "package.json");
            version = ReadPackageVersion(Path.GetFullPath(pkg));
        }
        var tui = !string.IsNullOrWhiteSpace(configuredTui) && Directory.Exists(configuredTui)
            ? Path.GetFullPath(configuredTui)
            : TuiPackage();
        return new Report(bin, version, tui, home, LooksLikeHome(home), SourceCli());
    }

    static IEnumerable<string?> BinCandidates()
    {
        var cli = Proc.Which("dsh");
        if (cli is not null)
        {
            yield return BinBeside(cli);
            yield return ResolveBin(cli);
        }

        foreach (var root in ModuleRoots())
            yield return Path.Combine(root, "@deepseek-ai", "dsh", "lib", "bin.js");

        var packRoot = Environment.GetEnvironmentVariable("PACK_LAUNCHER_ROOT");
        if (!string.IsNullOrWhiteSpace(packRoot))
            yield return FindPinnedBin(packRoot);
        yield return FindPinnedBin(Path.Combine(AppContext.BaseDirectory, ".pack-launcher"));

        foreach (var root in CheckoutRoots())
            yield return Path.Combine(root, "apps", "cli", "lib", "bin.js");
    }

    static string? FindPinnedBin(string launcherRoot)
    {
        var versions = Path.Combine(launcherRoot, "versions");
        if (!Directory.Exists(versions)) return null;
        foreach (var dir in Take(SafeDirs(versions)))
        {
            var rec = Path.Combine(dir, "version.json");
            if (File.Exists(rec))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(rec));
                    if (doc.RootElement.TryGetProperty("bin", out var b))
                    {
                        var bin = b.GetString();
                        if (!string.IsNullOrWhiteSpace(bin) && File.Exists(bin)) return bin;
                    }
                }
                catch { /* unreadable version.json */ }
            }
            var fallback = Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            if (File.Exists(fallback)) return fallback;
        }
        return null;
    }

    static string? BinBeside(string cli)
    {
        var dir = Path.GetDirectoryName(cli);
        if (dir is null) return null;
        var nearby = Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        if (File.Exists(nearby)) return nearby;
        return null;
    }

    static IEnumerable<string> ModuleRoots()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(appdata, "npm", "node_modules");
        yield return Path.Combine(home, ".bun", "install", "global", "node_modules");
        yield return Path.Combine(local, "Yarn", "Data", "global", "node_modules");

        var pnpmGlobal = Path.Combine(local, "pnpm", "global");
        if (Directory.Exists(pnpmGlobal))
        {
            foreach (var dir in Take(SafeDirs(pnpmGlobal)))
                yield return Path.Combine(dir, "node_modules");
        }

        foreach (var cache in new[]
        {
            Path.Combine(local, "npm-cache"),
            Path.Combine(appdata, "npm-cache"),
        })
        {
            var npx = Path.Combine(cache, "_npx");
            if (!Directory.Exists(npx)) continue;
            foreach (var dir in Take(SafeDirs(npx)))
                yield return Path.Combine(dir, "node_modules");
        }

        var nvm = Path.Combine(appdata, "nvm");
        if (Directory.Exists(nvm))
        {
            foreach (var dir in Take(SafeDirs(nvm)))
                yield return Path.Combine(dir, "node_modules");
        }
    }

    static IEnumerable<string> CheckoutRoots()
    {
        var tmp = Environment.GetEnvironmentVariable("AGENT_PACK_TMP");
        if (string.IsNullOrWhiteSpace(tmp)) tmp = @"E:\tmp\pack-agent";
        yield return Path.Combine(tmp, "dsh-src", "deepseek-harness-master");
        yield return Path.Combine(tmp, "dsh-src");
    }

    static IEnumerable<string> SafeDirs(string root)
    {
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root); }
        catch { yield break; }
        foreach (var dir in dirs) yield return dir;
    }

    static IEnumerable<string> Take(IEnumerable<string> dirs)
    {
        var n = 0;
        foreach (var dir in dirs)
        {
            yield return dir;
            if (++n >= Cap) yield break;
        }
    }

    static bool IsDshPackage(string pkg)
    {
        if (!File.Exists(pkg)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            return doc.RootElement.TryGetProperty("name", out var n)
                && n.GetString() == "@deepseek-ai/dsh";
        }
        catch { return false; }
    }

    static string? ReadPackageVersion(string pkg)
    {
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }
}
