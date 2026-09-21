using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>
/// pnpm stores packages under <c>.pnpm</c> and junctions them into the
/// top-level <c>node_modules</c>. Copying those junctions as real directories
/// leaves <c>bin.js</c> unable to resolve <c>@deepseek-ai/dsh-app-boot</c>
/// and TUI unable to resolve <c>emoji-regex</c>. The recipient of a portable
/// kit may not have pnpm, so PAD rebuilds the junctions from <c>.pnpm</c>.
/// Nested copies under a long <c>.pnpm/&lt;id&gt;/.../pkg/node_modules</c>
/// also blow Windows MAX_PATH (index.js copied, hashed chunks dropped).
/// </summary>
public static class PnpmHeal
{
    static Dictionary<string, string?>? _physCache;

    public static int Relink(string nodeModules)
    {
        if (!Directory.Exists(nodeModules)) return 0;
        var pnpm = Path.Combine(nodeModules, ".pnpm");
        if (!Directory.Exists(pnpm)) return 0;
        _physCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var n = RelinkChildren(pnpm, nodeModules);
            n += PromoteOrphans(pnpm, nodeModules);
            n += WalkPnpmStore(pnpm);
            RewriteModulesYaml(nodeModules);
            return n;
        }
        finally { _physCache = null; }
    }

    /// <summary>
    /// DSH owns <c>$DSH_HOME/profiles/node_modules</c> as junction fallbacks
    /// into the installation. CopyTree with an empty LinkTarget expands those
    /// into real directories; <c>ensureSymlink</c> then throws. Delete the
    /// fallback so the next boot can recreate it. Never follow junctions into
    /// the version library — recursive delete would try to wipe <c>sdk</c>.
    /// </summary>
    public static void ClearProfilesFallback(string home)
    {
        if (string.IsNullOrWhiteSpace(home)) return;
        var dir = Path.Combine(home, "profiles", "node_modules");
        if (!Directory.Exists(dir)) return;
        try
        {
            DeleteNoFollow(dir);
        }
        catch (Exception)
        {
            var trash = dir + ".pad-trash-" + DateTime.UtcNow.Ticks.ToString();
            Directory.Move(dir, trash);
            try { DeleteNoFollow(trash); }
            catch { /* leftover trash is still not the DSH fallback path */ }
        }
    }

    /// <summary>
    /// A junction that still points at this copy's <c>.pnpm</c> is fine.
    /// A junction whose target is missing or lives on the packer machine must be rebuilt.
    /// </summary>
    public static bool LinkHealthy(string link, string pnpmDir)
    {
        FileAttributes attr;
        try { attr = File.GetAttributes(link); }
        catch { return false; }
        if ((attr & FileAttributes.ReparsePoint) == 0) return false;
        string? target;
        try { target = new DirectoryInfo(link).LinkTarget; }
        catch { return false; }
        if (string.IsNullOrWhiteSpace(target)) return false;
        var parent = Path.GetDirectoryName(link);
        if (string.IsNullOrWhiteSpace(parent)) return false;
        var resolved = Path.IsPathRooted(target)
            ? Path.GetFullPath(target)
            : Path.GetFullPath(Path.Combine(parent, target));
        if (!Directory.Exists(resolved)) return false;
        return LaunchPolicy.PathUnder(resolved, pnpmDir);
    }

    static int RelinkChildren(string pnpm, string modulesDir)
    {
        if (!Directory.Exists(modulesDir)) return 0;
        var n = 0;
        foreach (var scoped in Directory.EnumerateDirectories(modulesDir).ToList())
        {
            var name = Path.GetFileName(scoped);
            if (name.StartsWith('.')) continue;
            if (name.StartsWith('@'))
            {
                foreach (var pkg in Directory.EnumerateDirectories(scoped).ToList())
                    n += RelinkEntry(pnpm, name + "/" + Path.GetFileName(pkg), pkg);
            }
            else
                n += RelinkEntry(pnpm, name, scoped);
        }
        return n;
    }

    static int WalkPnpmStore(string pnpm)
    {
        var n = 0;
        string[] stores;
        try { stores = Directory.GetDirectories(pnpm); }
        catch { return 0; }
        foreach (var storeDir in stores)
        {
            var isolated = Path.Combine(storeDir, "node_modules");
            if (!Directory.Exists(isolated)) continue;
            n += RelinkChildren(pnpm, isolated);
            n += LinkIsolatedSiblings(pnpm, isolated, storeDir);
            n += RelinkChildren(pnpm, isolated);
            n += LinkIsolatedSiblings(pnpm, isolated, storeDir);
            foreach (var pkg in EnumerateRealPackages(isolated))
            {
                var nested = Path.Combine(pkg, "node_modules");
                if (!Directory.Exists(nested)) continue;
                FileAttributes attr;
                try { attr = File.GetAttributes(nested); }
                catch { continue; }
                if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                n += RelinkChildren(pnpm, nested);
                n += PromoteOrphans(pnpm, nested);
            }
        }
        return n;
    }

    static IEnumerable<string> EnumerateRealPackages(string modulesDir)
    {
        if (!Directory.Exists(modulesDir)) yield break;
        foreach (var scoped in Directory.EnumerateDirectories(modulesDir))
        {
            var name = Path.GetFileName(scoped);
            if (name.StartsWith('.')) continue;
            FileAttributes attr;
            try { attr = File.GetAttributes(scoped); }
            catch { continue; }
            if ((attr & FileAttributes.ReparsePoint) != 0) continue;
            if (name.StartsWith('@'))
            {
                foreach (var pkg in Directory.EnumerateDirectories(scoped))
                {
                    try { attr = File.GetAttributes(pkg); }
                    catch { continue; }
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                    yield return pkg;
                }
            }
            else
                yield return scoped;
        }
    }

    static int PromoteOrphans(string pnpm, string modulesDir)
    {
        if (!Directory.Exists(modulesDir)) return 0;
        var n = 0;
        var created = new List<string>();
        foreach (var (spec, path) in EnumerateEntries(modulesDir))
            n += PromoteOne(pnpm, spec, path, created);
        n += LinkIsolatedSiblings(pnpm, modulesDir);
        foreach (var isolated in created.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var pkg in EnumerateRealPackages(isolated))
            {
                var nested = Path.Combine(pkg, "node_modules");
                if (!Directory.Exists(nested)) continue;
                FileAttributes attr;
                try { attr = File.GetAttributes(nested); }
                catch { continue; }
                if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                n += RelinkChildren(pnpm, nested);
                n += PromoteOrphans(pnpm, nested);
            }
        }
        return n;
    }

    static IEnumerable<(string spec, string path)> EnumerateEntries(string modulesDir)
    {
        if (!Directory.Exists(modulesDir)) yield break;
        foreach (var scoped in Directory.EnumerateDirectories(modulesDir).ToList())
        {
            var name = Path.GetFileName(scoped);
            if (name.StartsWith('.')) continue;
            if (name.StartsWith('@'))
            {
                foreach (var pkg in Directory.EnumerateDirectories(scoped).ToList())
                    yield return (name + "/" + Path.GetFileName(pkg), pkg);
            }
            else
                yield return (name, scoped);
        }
    }

    /// <summary>
    /// pnpm isolated layout: every package in a store's <c>node_modules</c>
    /// sees its former siblings as junctions. Moving one nested dep into its
    /// own store without those siblings makes Node (realpath) miss
    /// <c>@dsh-std/core</c> from <c>@dsh-std/manifest</c>.
    /// </summary>
    static int LinkIsolatedSiblings(string pnpm, string modulesDir, string? storeDir = null)
    {
        storeDir ??= Path.GetDirectoryName(modulesDir);
        if (!IsPnpmStoreIsolated(pnpm, storeDir))
            return 0;
        var ownerSpec = StoreOwnerSpec(storeDir);
        string? ownerJson = null;
        if (ownerSpec is not null)
            ownerJson = Path.Combine(modulesDir, ownerSpec.Replace('/', Path.DirectorySeparatorChar), "package.json");
        var n = 0;
        foreach (var (importerSpec, path) in EnumerateEntries(modulesDir))
        {
            var importerJson = Path.Combine(path, "package.json");
            if (!File.Exists(importerJson)) continue;
            var isolated = IsolatedRootOfPackage(path);
            if (string.IsNullOrEmpty(isolated)) continue;
            var isOwner = ownerSpec is not null
                && string.Equals(importerSpec, ownerSpec, StringComparison.OrdinalIgnoreCase);
            foreach (var spec2 in ReadDepNames(importerJson))
            {
                if (!isOwner && ownerJson is not null && File.Exists(ownerJson)
                    && ReadDepRangeFile(ownerJson, spec2) is not null)
                    continue;
                var link = Path.Combine(isolated, spec2.Replace('/', Path.DirectorySeparatorChar));
                var range = ReadDepRangeFile(importerJson, spec2);
                var preferMajor = DepRangeMajor(range);
                var want = IsExactVersion(range)
                    ? FindPhys(pnpm, spec2, range)
                    : FindPhys(pnpm, spec2, null, preferMajor);
                if (want is null) continue;
                if (SamePath(link, want)) continue;
                var resolved = ResolveDir(link);
                if (resolved is not null && SamePath(resolved, want) && LinkHealthy(link, pnpm))
                    continue;
                try
                {
                    if (Directory.Exists(link) || File.Exists(link))
                    {
                        var attr = File.GetAttributes(link);
                        if ((attr & FileAttributes.ReparsePoint) == 0 && range is null)
                            continue;
                        DeleteNoFollow(link);
                    }
                    Junction(link, want, isDir: true);
                    n++;
                }
                catch { /* next dep */ }
            }
        }
        return n;
    }

    static string? StoreOwnerSpec(string? storeDir)
    {
        if (string.IsNullOrWhiteSpace(storeDir)) return null;
        var n = Path.GetFileName(storeDir);
        var at = n.LastIndexOf('@');
        if (at <= 0) return null;
        return n[..at].Replace('+', '/');
    }

    static bool IsPnpmStoreIsolated(string pnpm, string? storeDir)
    {
        if (string.IsNullOrWhiteSpace(storeDir)) return false;
        var parent = Path.GetDirectoryName(storeDir);
        return !string.IsNullOrEmpty(parent) && SamePath(parent, pnpm);
    }

    static string? IsolatedRootOfPackage(string pkg)
    {
        var dir = Path.GetDirectoryName(pkg);
        if (string.IsNullOrEmpty(dir)) return null;
        if (Path.GetFileName(dir).StartsWith('@'))
            dir = Path.GetDirectoryName(dir);
        if (string.IsNullOrEmpty(dir)) return null;
        if (!string.Equals(Path.GetFileName(dir), "node_modules", StringComparison.OrdinalIgnoreCase))
            return null;
        return dir;
    }

    static IEnumerable<string> ReadDepNames(string pkgJson)
    {
        if (string.IsNullOrWhiteSpace(pkgJson) || !File.Exists(pkgJson))
            return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkgJson));
            var names = new List<string>();
            foreach (var bag in new[] { "dependencies", "optionalDependencies", "peerDependencies" })
            {
                if (!doc.RootElement.TryGetProperty(bag, out var deps)) continue;
                if (deps.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in deps.EnumerateObject())
                    names.Add(p.Name);
            }
            return names;
        }
        catch { return Array.Empty<string>(); }
    }

    static int PromoteOne(string pnpm, string spec, string path, List<string> created)
    {
        if (LinkHealthy(path, pnpm)) return 0;
        var phys = FindPhys(pnpm, spec);
        if (phys is null)
        {
            FileAttributes attr;
            try { attr = File.GetAttributes(path); }
            catch { return 0; }
            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                try
                {
                    if ((attr & FileAttributes.Directory) != 0) Directory.Delete(path);
                    else File.Delete(path);
                }
                catch { }
                return 0;
            }
            phys = MoveToStore(pnpm, spec, path);
            created.Add(IsolatedOfPhys(phys, spec));
            if (SamePath(path, phys)) return 0;
            if (Directory.Exists(path))
                return RelinkEntry(pnpm, spec, path);
            Junction(path, phys, isDir: true);
            return 1;
        }
        if (SamePath(path, phys)) return 0;
        return RelinkEntry(pnpm, spec, path);
    }

    static string MoveToStore(string pnpm, string spec, string path)
    {
        var ver = SafeVer(ReadPkgVersion(Path.Combine(path, "package.json")));
        var encoded = spec.Replace('/', '+') + "@" + ver;
        var isolated = Path.Combine(pnpm, encoded, "node_modules");
        var phys = Path.Combine(isolated, spec.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(phys) || File.Exists(phys)) return phys;
        var parent = Path.GetDirectoryName(phys);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        Directory.Move(path, phys);
        return phys;
    }

    static string IsolatedOfPhys(string phys, string spec)
    {
        var full = Path.GetFullPath(phys).TrimEnd('\\', '/');
        var suffix = spec.Replace('/', Path.DirectorySeparatorChar);
        if (full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return full[..^suffix.Length].TrimEnd('\\', '/');
        return Path.GetDirectoryName(full) ?? full;
    }

    static string? FindPhys(string pnpm, string spec, string? preferVer = null, string? preferMajor = null)
    {
        var cacheKey = pnpm + "\0" + spec + "\0" + (preferVer ?? "") + "\0" + (preferMajor ?? "");
        if (_physCache is not null && _physCache.TryGetValue(cacheKey, out var cached))
            return cached;
        var encoded = spec.Replace('/', '+') + "@";
        string? unique = null;
        string? majorBest = null;
        string? majorBestRest = null;
        var nHits = 0;
        var nMajor = 0;
        string? Remember(string? value)
        {
            if (_physCache is not null) _physCache[cacheKey] = value;
            return value;
        }
        foreach (var dir in Directory.EnumerateDirectories(pnpm))
        {
            var n = Path.GetFileName(dir);
            if (!n.StartsWith(encoded, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = n[encoded.Length..];
            var candidate = Path.Combine(dir, "node_modules",
                spec.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(candidate)) continue;
            if (!string.IsNullOrWhiteSpace(preferVer))
            {
                if (!rest.Equals(preferVer, StringComparison.OrdinalIgnoreCase)
                    && !rest.StartsWith(preferVer + "_", StringComparison.OrdinalIgnoreCase)
                    && !rest.StartsWith(preferVer + "+", StringComparison.OrdinalIgnoreCase))
                    continue;
                return Remember(candidate);
            }
            if (!string.IsNullOrWhiteSpace(preferMajor))
            {
                if (!rest.Equals(preferMajor, StringComparison.OrdinalIgnoreCase)
                    && !rest.StartsWith(preferMajor + ".", StringComparison.OrdinalIgnoreCase)
                    && !rest.StartsWith(preferMajor + "_", StringComparison.OrdinalIgnoreCase)
                    && !rest.StartsWith(preferMajor + "+", StringComparison.OrdinalIgnoreCase))
                    continue;
                nMajor++;
                if (majorBestRest is null
                    || string.Compare(rest, majorBestRest, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    majorBest = candidate;
                    majorBestRest = rest;
                }
                continue;
            }
            nHits++;
            unique = candidate;
            if (nHits > 1) return Remember(null);
        }
        if (!string.IsNullOrWhiteSpace(preferMajor)) return Remember(nMajor == 0 ? null : majorBest);
        return Remember(unique);
    }

    static int RelinkEntry(string pnpm, string spec, string top)
    {
        var phys = FindPhysForEntry(pnpm, spec, top);
        if (phys is not null)
        {
            if (Directory.Exists(top) && SamePath(top, phys)) return 0;
            var resolved = ResolveDir(top);
            if (resolved is not null && SamePath(resolved, phys) && LinkHealthy(top, pnpm))
                return 0;
            try
            {
                if (Directory.Exists(top) || File.Exists(top)) DeleteNoFollow(top);
            }
            catch { return 0; }
            Junction(top, phys, isDir: true);
            return 1;
        }
        if (LinkHealthy(top, pnpm)) return 0;
        try
        {
            var attr = File.GetAttributes(top);
            if ((attr & FileAttributes.ReparsePoint) != 0)
            {
                if ((attr & FileAttributes.Directory) != 0) Directory.Delete(top);
                else File.Delete(top);
            }
        }
        catch { /* RelinkOne still tries */ }
        return RelinkOne(pnpm, spec, top);
    }

    static int RelinkOne(string pnpm, string spec, string top)
    {
        FileAttributes attr;
        try { attr = File.GetAttributes(top); }
        catch { return 0; }
        if ((attr & FileAttributes.ReparsePoint) != 0) return 0;
        if ((attr & FileAttributes.Directory) == 0) return 0;

        var preferVer = ReadPkgVersion(Path.Combine(top, "package.json"));
        var phys = FindPhys(pnpm, spec, preferVer);
        if (phys is null) return 0;
        if (SamePath(top, phys)) return 0;
        try { DeleteNoFollow(top); }
        catch { return 0; }
        Junction(top, phys, isDir: true);
        return 1;
    }

    static string? FindPhysForEntry(string pnpm, string spec, string top)
    {
        var range = ReadParentDepRange(top, spec) ?? ReadStoreOwnerDepRange(top, spec);
        var preferMajor = DepRangeMajor(range);
        var childVer = ReadPkgVersion(Path.Combine(top, "package.json"));
        if (IsExactVersion(range))
            return FindPhys(pnpm, spec, range, preferMajor: null);
        if (preferMajor is not null)
        {
            if (childVer is not null && DepRangeMajor(childVer) == preferMajor)
                return FindPhys(pnpm, spec, childVer, preferMajor) ?? FindPhys(pnpm, spec, null, preferMajor);
            return FindPhys(pnpm, spec, null, preferMajor);
        }
        if (childVer is not null)
            return FindPhys(pnpm, spec, childVer, preferMajor: null);
        return FindPhys(pnpm, spec);
    }

    static string? ReadParentDepRange(string top, string spec)
    {
        var pkg = ParentPackageJson(top);
        if (pkg is null) return null;
        return ReadDepRangeFile(pkg, spec);
    }

    /// <summary>
    /// Isolated store root has no parent package.json. The store folder name
    /// is the owner; its range wins over sibling importers like chalk ^4.
    /// </summary>
    static string? ReadStoreOwnerDepRange(string top, string spec)
    {
        var isolated = IsolatedRootOfPackage(top);
        if (string.IsNullOrEmpty(isolated)) return null;
        var owner = StoreOwnerSpec(Path.GetDirectoryName(isolated));
        if (owner is null) return null;
        var ownerJson = Path.Combine(isolated, owner.Replace('/', Path.DirectorySeparatorChar), "package.json");
        return ReadDepRangeFile(ownerJson, spec);
    }

    static string? ReadDepRangeFile(string pkgJson, string spec)
    {
        if (string.IsNullOrWhiteSpace(pkgJson) || !File.Exists(pkgJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkgJson));
            foreach (var bag in new[] { "dependencies", "optionalDependencies", "peerDependencies", "devDependencies" })
            {
                if (!doc.RootElement.TryGetProperty(bag, out var deps)) continue;
                if (deps.ValueKind != JsonValueKind.Object) continue;
                if (deps.TryGetProperty(spec, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            }
        }
        catch { return null; }
        return null;
    }

    static string? ParentPackageJson(string top)
    {
        var dir = Path.GetDirectoryName(top);
        if (string.IsNullOrEmpty(dir)) return null;
        var folder = Path.GetFileName(dir);
        if (folder.StartsWith('@'))
            dir = Path.GetDirectoryName(dir);
        if (string.IsNullOrEmpty(dir)) return null;
        if (!string.Equals(Path.GetFileName(dir), "node_modules", StringComparison.OrdinalIgnoreCase))
            return null;
        var parent = Path.GetDirectoryName(dir);
        return string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, "package.json");
    }

    static string? DepRangeMajor(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return null;
        if (range.Contains("||", StringComparison.Ordinal))
        {
            var best = -1;
            foreach (var part in range.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var m = Regex.Match(part, @"\d+");
                if (m.Success && int.TryParse(m.Value, out var n) && n > best)
                    best = n;
            }
            return best < 0 ? null : best.ToString();
        }
        var one = Regex.Match(range.Trim(), @"\d+");
        return one.Success ? one.Value : null;
    }

    static bool IsExactVersion(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return false;
        return Regex.IsMatch(range.Trim(), @"^\d+\.\d+\.\d+$");
    }

    static string? ResolveDir(string path)
    {
        FileAttributes attr;
        try { attr = File.GetAttributes(path); }
        catch { return null; }
        if ((attr & FileAttributes.ReparsePoint) == 0)
            return Path.GetFullPath(path);
        string? target;
        try { target = new DirectoryInfo(path).LinkTarget; }
        catch { return null; }
        if (string.IsNullOrWhiteSpace(target)) return null;
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent)) return null;
        var resolved = Path.IsPathRooted(target)
            ? Path.GetFullPath(target)
            : Path.GetFullPath(Path.Combine(parent, target));
        return Directory.Exists(resolved) ? resolved : null;
    }

    static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd('\\', '/'),
            Path.GetFullPath(b).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    static string SafeVer(string? ver)
    {
        if (string.IsNullOrWhiteSpace(ver)) return "nested";
        var chars = Path.GetInvalidFileNameChars();
        var buf = new char[ver.Length];
        var i = 0;
        foreach (var c in ver)
            buf[i++] = Array.IndexOf(chars, c) >= 0 ? '_' : c;
        return new string(buf, 0, i);
    }

    static string? ReadPkgVersion(string pkgJson)
    {
        if (!File.Exists(pkgJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkgJson));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    static void DeleteNoFollow(string path)
    {
        FileAttributes attr;
        try { attr = File.GetAttributes(path); }
        catch { return; }
        if ((attr & FileAttributes.ReparsePoint) != 0)
        {
            if ((attr & FileAttributes.Directory) != 0) Directory.Delete(path);
            else File.Delete(path);
            return;
        }
        if ((attr & FileAttributes.Directory) != 0)
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                DeleteNoFollow(child);
            Directory.Delete(path);
            return;
        }
        File.Delete(path);
    }

    public static void Junction(string link, string target, bool isDir)
    {
        var parent = Path.GetDirectoryName(link);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        if (!isDir)
        {
            File.CreateSymbolicLink(link, Path.GetRelativePath(parent!, target));
            return;
        }
        try
        {
            Directory.CreateSymbolicLink(link, Path.GetRelativePath(parent!, target));
            return;
        }
        catch
        {
            // junctions do not need developer mode
        }
        var psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(15_000);
        if (!Directory.Exists(link))
            throw new IOException("mklink /J failed for " + link);
    }

    static void RewriteModulesYaml(string nodeModules)
    {
        var path = Path.Combine(nodeModules, ".modules.yaml");
        if (!File.Exists(path)) return;
        var want = Path.GetFullPath(Path.Combine(nodeModules, ".pnpm"));
        var text = File.ReadAllText(path);
        var next = LaunchPolicy.PatchVirtualStoreDir(text, want);
        if (next != text) File.WriteAllText(path, next);
    }
}
