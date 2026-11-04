using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pad.Core;

/// <summary>导入 v3 包时的选项。</summary>
/// <param name="OnConflict">磁盘上已有且内容不同：keep（留用户，写 .pack-new）/ replace（备份后覆盖）/ skip。</param>
/// <param name="InstallBundles">是否顺手用 <c>dsh plugin add</c> 装 registry bundle（需要网络与 dsh 内核）。</param>
public sealed record PackImportOptions(
    string OnConflict = "keep",
    bool InstallBundles = true,
    IReadOnlyDictionary<string, string>? Decisions = null);   // path -> keep|replace|skip

/// <summary>一个「磁盘上被改过」的 overrides 文件，导入窗口据此让用户逐条选。</summary>
public sealed record PackConflict(string Path, string Dest, DateTime LocalWrite, DateTime PackWrite);

public sealed record PackImportResult(
    int Added, int Updated, int Kept, int Skipped,
    List<string> PendingBundles, List<string> Messages, string LedgerPath)
{
    public bool Ok => Messages.All(m => !m.StartsWith("error:", StringComparison.Ordinal));
}

/// <summary>
/// 把 PACK_SPEC v3 包装进一个实例：校验完整性 → 落 overrides（patch / bundled 包体 / data / workspace 模板）
/// → 合并 profile 的 bundle 清单 → 记账本。kernel bundled（P4）与 agent preset 暂不支持，遇到会报错。
/// </summary>
public static class PackInstall
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<string> Verify(PackSpec spec, string archivePath)
    {
        var errors = new List<string>();
        if (!spec.Valid) { errors.AddRange(spec.Errors.Select(e => "error: manifest: " + e)); return errors; }
        if (!File.Exists(archivePath)) { errors.Add("error: pack file not found: " + archivePath); return errors; }

        using var zip = ZipFile.OpenRead(archivePath);
        var byPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries) byPath[e.FullName.Replace('\\', '/')] = e;

        foreach (var f in spec.Integrity.Files)
        {
            if (!byPath.TryGetValue(f.Path, out var entry)) { errors.Add($"error: missing `{f.Path}`"); continue; }
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var sha = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
            if (sha != f.Sha256) errors.Add($"error: sha256 mismatch `{f.Path}`");
        }

        return errors;
    }

    public static PackImportResult Install(Launcher launcher, Instance inst, string packPath, PackImportOptions? opt = null)
    {
        opt ??= new PackImportOptions();
        var spec = PackSpec.Load(packPath);
        var messages = Verify(spec, packPath);

        var added = 0; var updated = 0; var kept = 0; var skipped = 0;
        var pending = new List<string>();
        var ledgerFiles = new List<object>();

        if (messages.Any(m => m.StartsWith("error:", StringComparison.Ordinal)))
            return new PackImportResult(0, 0, 0, 0, pending, messages, "");

        if (spec.Kernel.Mode == "bundled")
            throw new PadError("PA260", "bundled kernels are not supported yet", spec.Name, spec.Kernel.Bundled,
                ["把包改成 kernel.mode=pin 再导入", "P4 会实现 bundled"]);
        if (spec.Agents.Any(a => a.Source == "bundled"))
            throw new PadError("PA261", "bundled agent presets are not supported yet", spec.Name, "",
                ["先用 source=official 的 preset"]);

        // kernel：pin 时把实例钉到包里那个版本（没装会让用户先去装）。
        if (spec.Kernel.Mode == "pin" && spec.Kernel.Version.Length > 0 &&
            !string.Equals(inst.Dsh.Version, spec.Kernel.Version, StringComparison.OrdinalIgnoreCase))
        {
            inst.Dsh.Version = spec.Kernel.Version;
            launcher.SaveInstance(inst);
            messages.Add($"kernel -> {spec.Kernel.Version}");
        }

        // overrides：逐文件校验过之后落盘。
        var backupDir = Path.Combine(launcher.InstanceDir(inst.Id), ".pack-backup",
            $"{spec.Name}-{spec.Version}-{DateTime.Now:yyyyMMdd-HHmmss}".Replace(' ', '_'));

        using var zip = ZipFile.OpenRead(packPath);
        var entries = zip.Entries.ToDictionary(e => e.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);

        foreach (var file in spec.Integrity.Files)
        {
            if (!entries.TryGetValue(file.Path, out var entry)) continue;
            if (DestinationFor(inst, file.Path) is not { } dest)
            {
                skipped++;
                messages.Add($"skip   {file.Path}（不属于本实例结构）");
                continue;
            }

            var bytes = ReadAll(entry);
            var exists = File.Exists(dest);
            var same = exists && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest))).ToLowerInvariant() == file.Sha256;

            if (same) { skipped++; continue; }

            if (exists && !same)
            {
                var decision = opt.Decisions is { } map && map.TryGetValue(file.Path, out var chosen)
                    ? chosen : opt.OnConflict;
                switch (decision)
                {
                    case "skip":
                        skipped++;
                        messages.Add($"skip   {file.Path}（用户已改）");
                        continue;
                    case "keep":
                        WriteAll(dest + ".pack-new", bytes);
                        kept++;
                        messages.Add($"keep   {file.Path}（新版写成 .pack-new）");
                        continue;
                    default: // replace
                        var backup = Path.Combine(backupDir, file.Path.Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(dest, backup, overwrite: true);
                        messages.Add($"backup {file.Path}");
                        break;
                }
            }

            WriteAll(dest, bytes);
            if (exists) updated++; else added++;
            ledgerFiles.Add(new { path = file.Path, sha256 = file.Sha256, decision = exists ? opt.OnConflict : "add" });
        }

        // bundled 包体：ZIP 存不了 pnpm 的 junction，安装后用 .pnpm 重建链接。
        foreach (var profile in spec.Profiles.Where(p => p.Bundles.Any(b => b.Source == "bundled")))
        {
            var nodeModules = Path.Combine(inst.Home, "profiles", profile.Name, "node_modules");
            if (!Directory.Exists(Path.Combine(nodeModules, ".pnpm"))) continue;
            try
            {
                var links = PnpmHeal.RestoreTopLevel(nodeModules) + PnpmHeal.Relink(nodeModules);
                messages.Add($"relink {profile.Name}: {links} 个链接");
            }
            catch (Exception ex) { messages.Add($"error: relink `{profile.Name}`: {ex.Message}"); }
        }
        // profiles：合并 bundle 清单（official 直接写；registry 走 dsh plugin add 或记 pending）。
        foreach (var profile in spec.Profiles)
        {
            var dir = Path.Combine(inst.Home, "profiles", profile.Name);
            var official = profile.Bundles.Where(b => b.Source == "official").Select(b => b.Spec).ToList();
            var bundled = profile.Bundles.Where(b => b.Source == "bundled").Select(b => b.Spec).ToList();
            var registry = profile.Bundles.Where(b => b.Source == "registry").Select(b => b.Spec).ToList();

            MergeBundles(dir, official.Concat(bundled).ToList());

            if (registry.Count > 0)
            {
                if (opt.InstallBundles)
                {
                    try { launcher.AddBundles(inst, profile.Name, registry, null, CancellationToken.None, heal: false).GetAwaiter().GetResult(); }
                    catch (Exception ex) { messages.Add($"error: install bundles `{profile.Name}`: {ex.Message}"); }
                }
                else pending.AddRange(registry.Select(r => profile.Name + ":" + r));
            }
        }

        // home snapshot（seed：只补缺失的文件）
        if (spec.Home.Mode == "snapshot")
        {
            foreach (var file in spec.Integrity.Files.Where(f => f.Path.StartsWith("home/", StringComparison.OrdinalIgnoreCase)))
            {
                if (!entries.TryGetValue(file.Path, out var entry)) continue;
                var rel = file.Path["home/".Length..];
                var dest = Path.Combine(inst.Home, rel.Replace('/', '\\'));
                if (File.Exists(dest)) { skipped++; continue; }
                WriteAll(dest, ReadAll(entry));
                added++;
            }
        }

        var ledgerPath = Path.Combine(launcher.InstanceDir(inst.Id), "pack-ledger.json");
        var ledger = new
        {
            schema = "pack-agent.install-ledger/v1",
            pack = spec.Name,
            version = spec.Version,
            installedAt = DateTimeOffset.Now.ToString("o"),
            files = ledgerFiles,
        };
        WriteAll(ledgerPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ledger, Json)));

        messages.Add($"pack   {spec.Name} {spec.Version} -> {inst.Name}");
        messages.Add($"files  added {added} / updated {updated} / kept {kept} / skipped {skipped}");
        return new PackImportResult(added, updated, kept, skipped, pending, messages, ledgerPath);
    }

    /// <summary>这次导入会撞到的文件（存在且内容不同），给窗口做冲突清单用。</summary>
    public static List<PackConflict> Conflicts(PackSpec spec, Instance inst, string packPath)
    {
        var list = new List<PackConflict>();
        if (!File.Exists(packPath)) return list;
        using var zip = ZipFile.OpenRead(packPath);
        var entries = zip.Entries.ToDictionary(e => e.FullName.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);

        foreach (var file in spec.Integrity.Files)
        {
            if (DestinationFor(inst, file.Path) is not { } dest || !File.Exists(dest)) continue;
            if (PackExport.HashFile(dest) == file.Sha256) continue;
            var packTime = entries.TryGetValue(file.Path, out var entry) ? entry.LastWriteTime.DateTime : DateTime.MinValue;
            list.Add(new PackConflict(file.Path, dest, File.GetLastWriteTime(dest), packTime));
        }
        return list;
    }
    /// <summary>overrides 里的相对路径 → 实例里的绝对路径；null = 不属于实例结构（图标等）。</summary>
    static string? DestinationFor(Instance inst, string rel)
    {
        var p = rel.Replace('\\', '/');
        if (p.Equals("icon.png", StringComparison.OrdinalIgnoreCase)) return null;
        if (p.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = p["profiles/".Length..];
            return Path.Combine(inst.Home, "profiles", rest.Replace('/', '\\'));
        }
        if (p.StartsWith(".agent-pack/", StringComparison.OrdinalIgnoreCase) || p.Equals(".agent-pack", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(inst.Workspace.Path, p.Replace('/', '\\'));
        if (p.StartsWith("workspace/", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(inst.Workspace.Path, p["workspace/".Length..].Replace('/', '\\'));
        if (p.StartsWith("home/", StringComparison.OrdinalIgnoreCase)) return null; // home 快照单独处理
        return null;
    }

    /// <summary>把 bundle 名合并进 profile 的 <c>dsh.profile.bundles</c>（没有 package.json 就建一个）。</summary>
    static void MergeBundles(string profileDir, List<string> specs)
    {
        if (specs.Count == 0) return;
        Directory.CreateDirectory(profileDir);
        var pkgPath = Path.Combine(profileDir, "package.json");
        JsonObject doc;
        if (File.Exists(pkgPath))
        {
            try { doc = JsonNode.Parse(File.ReadAllText(pkgPath)) as JsonObject ?? new JsonObject(); }
            catch { doc = new JsonObject(); }
        }
        else doc = new JsonObject { ["name"] = "dsh-profile-" + Path.GetFileName(profileDir), ["private"] = true };

        var dsh = doc["dsh"] as JsonObject ?? new JsonObject();
        doc["dsh"] = dsh;
        var profile = dsh["profile"] as JsonObject ?? new JsonObject();
        dsh["profile"] = profile;
        var bundles = profile["bundles"] as JsonArray ?? new JsonArray();
        profile["bundles"] = bundles;

        var have = bundles.Select(b => b?.GetValue<string>() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
            if (have.Add(spec)) bundles.Add(spec);

        File.WriteAllText(pkgPath, doc.ToJsonString(Json), new UTF8Encoding(false));
    }

    static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    static void WriteAll(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
