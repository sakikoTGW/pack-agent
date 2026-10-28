using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows;
using Pad.Core;

namespace Pad.Views;

/// <summary>
/// 便携包导出的共同步骤：先估算体量，复制时显示 已复制/总量、MB/s、剩余时间；
/// 压缩优先用自带的 7-Zip（多线程 deflate，比 .NET 单线程快），缺了就退回 .NET。
/// 也支持"不压缩、直接给文件夹"。
/// </summary>
public static class PortableExport
{
    /// <summary>便携包临时目录（按实例固定）：取消/失败后保留，下次接着复制（断点续传）。</summary>
    public static string StageDir(string instanceId) =>
        Path.Combine(Path.GetTempPath(), "pad-portable-stage", SafeName(instanceId));

    /// <summary>导出成功后清掉续传缓存；失败/取消保留。junction 只删链接本身，失败重试（防止留空目录）。</summary>
    public static void ClearStage(string instanceId)
    {
        var dir = StageDir(instanceId);
        for (var attempt = 0; attempt < 4 && Directory.Exists(dir); attempt++)
        {
            try
            {
                DeleteTree(dir);
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
    }

    static void DeleteTree(string dir)
    {
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            try
            {
                var attr = File.GetAttributes(sub);
                if ((attr & FileAttributes.ReparsePoint) != 0) Directory.Delete(sub);
                else DeleteTree(sub);
            }
            catch { /* 单个目录失败继续删别的 */ }
        }
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            try { File.Delete(file); } catch { /* 被占用 */ }
        }
        Directory.Delete(dir);
    }

    public static string DefaultFolderName(string title, string version) =>
        SafeName(title) + (string.IsNullOrWhiteSpace(version) ? "" : "-" + SafeName(version)) + "-整合";

    public static string SafeName(string text)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = (text ?? "").Trim().Select(c => bad.Contains(c) ? '_' : c).ToArray();
        return chars.Length == 0 ? "pack" : new string(chars);
    }

    static string Mb(double bytes) => (bytes / (1024d * 1024d)).ToString("F1");

    /// <summary>速度自动换单位：MB/s → KB/s → B/s，别老显示 0.0 MB/s。</summary>
    static string Rate(double bytesPerSec) =>
        bytesPerSec >= 1024 * 1024 ? $"{bytesPerSec / (1024 * 1024):F1} MB/s"
        : bytesPerSec >= 1024 ? $"{bytesPerSec / 1024:F0} KB/s"
        : $"{bytesPerSec:F0} B/s";

    /// <summary>
    /// 目录字节数。followLinks=true 时把 junction 目标内容也算进去——robocopy 复制会展开 junction，
    /// 便携包真实体积要用这个数，否则进度会显示成 490MB 实际 2.2GB。
    /// </summary>
    static long DirBytes(string dir, bool followLinks = false)
    {
        if (!Directory.Exists(dir)) return 0;
        long sum = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(dir, seen, ref sum, followLinks);
        return sum;
    }

    static void Walk(string dir, HashSet<string> seen, ref long sum, bool followLinks)
    {
        string canonical;
        try { canonical = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar); }
        catch { return; }
        if (!seen.Add(canonical)) return;

        List<string> files, subs;
        try
        {
            files = Directory.EnumerateFiles(dir).ToList();
            subs = Directory.EnumerateDirectories(dir).ToList();
        }
        catch { return; }

        foreach (var file in files)
        {
            try { sum += new FileInfo(file).Length; } catch { /* raced */ }
        }
        foreach (var sub in subs)
        {
            try
            {
                var attr = File.GetAttributes(sub);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    var target = Directory.ResolveLinkTarget(sub, true)?.FullName ?? sub;
                    var canonicalTarget = target.TrimEnd(Path.DirectorySeparatorChar);
                    if (followLinks && Directory.Exists(canonicalTarget) && seen.Add(canonicalTarget))
                        Walk(canonicalTarget, seen, ref sum, followLinks: true); // robocopy 会展开它
                    else
                        seen.Add(canonicalTarget);
                    continue;
                }
                Walk(sub, seen, ref sum, followLinks);
            }
            catch { /* 权限/竞态 */ }
        }
    }

    static long HostBytes()
    {
        try
        {
            var env = Environment.GetEnvironmentVariable("PACK_PAD_HOST_EXE");
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env!);
            var tmp = Environment.GetEnvironmentVariable("AGENT_PACK_TMP") ?? @"E:\tmp\pack-agent";
            candidates.Add(Path.Combine(tmp, "pad-win-x64-onefile", "pack-agent-for DSH.exe"));
            var host = LaunchPolicy.ResolvePortableHost(Environment.ProcessPath, candidates);
            return host is not null && File.Exists(host) ? new FileInfo(host).Length : 130L * 1024 * 1024;
        }
        catch { return 130L * 1024 * 1024; }
    }

    /// <summary>估算便携目录总字节（PAD exe + 版本库 + 实例 + runtime node）。</summary>
    public static long Estimate(Launcher launcher, string version, string instanceId)
    {
        long total = HostBytes();
        total += DirBytes(Path.Combine(launcher.Root, "instances", instanceId));
        total += DirBytes(Path.Combine(launcher.Root, "versions", version));
        total += DirBytes(Path.Combine(launcher.Root, "runtime", "node"));
        return total;
    }

    /// <summary>复制便携目录，期间用进度卡报 已复制/总量、速度/剩余；同时写进 jobs.json（下载任务里能看）。</summary>
    public static async Task<JobRecord> ExportAsync(MainWindow win, Launcher launcher, string destDir,
        string instanceId, string label, long estimate, JobRecord? job = null, bool zipPhase = true,
        CancellationToken ct = default)
    {
        job ??= launcher.StartJob("portable-export", instanceId,
            zipPhase
                ? [new JobLeaf { Id = "copy", Weight = 3 }, new JobLeaf { Id = "zip", Weight = 1 }]
                : [new JobLeaf { Id = "copy", Weight = 1 }],
            label);
        launcher.UpdateLeaf(job.Id, "copy", 0, 100);
        var token = launcher.TrackCancel(job.Id, ct);
        var copySpan = zipPhase ? 0.75 : 1.0;
        var clock = Stopwatch.StartNew();
        var phaseProgress = new Progress<string>(text => win.Progress(text, null, label, job));
        var copyProgress = new Progress<(int Done, int Total)>(p =>
        {
            var frac = p.Total > 0 ? copySpan * Math.Min(1.0, (double)p.Done / p.Total) : (double?)null;
            var sec = Math.Max(0.2, clock.Elapsed.TotalSeconds);
            var rate = p.Done / sec;
            var eta = rate > 0 && p.Done > 20
                ? TimeSpan.FromSeconds(Math.Max(0, p.Total - p.Done) / rate)
                : TimeSpan.Zero;
            var text = $"复制 {p.Done}/{p.Total} 个文件 · {rate:0} 个/秒";
            if (eta > TimeSpan.Zero) text += $" · 剩余 {eta:mm\\:ss}";
            win.Progress(text, frac, label, job);
            launcher.UpdateLeaf(job.Id, "copy", p.Done, Math.Max(1, p.Total));
        });

        try
        {
            await Task.Run(() => launcher.ExportPortable(destDir, instanceId, token,
                text => ((IProgress<string>)phaseProgress).Report(text), copyProgress, job.Id), token);
            launcher.UpdateLeaf(job.Id, "copy", 100, 100);
            return job;
        }
        catch (OperationCanceledException)
        {
            try { launcher.FinishJob(job.Id, "cancelled"); } catch { /* ignore */ }
            throw;
        }
        catch
        {
            try { launcher.FinishJob(job.Id, "failed"); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>
    /// 传输优化：把 kit 里的 node_modules 折叠成同名 *.pad-nm.zip，外层压缩条目骤减；
    /// 首次启动 PAD 会把它们解出来（PortableBootstrap）。折叠失败就保留原目录。
    /// </summary>
    public static async Task FoldAsync(MainWindow win, Launcher launcher, string kitDir,
        JobRecord? job, CancellationToken ct)
    {
        // 枚举几十万目录很慢，必须离开 UI 线程（之前直接卡出 PA040）。
        var dirs = await Task.Run(() => FindNodeModules(kitDir), ct);
        if (dirs.Count == 0) return;
        var launcherRoot = Path.Combine(kitDir, ".pack-launcher");
        var folded = new List<string>();
        for (var i = 0; i < dirs.Count; i++)
        {
            var dir = dirs[i];
            var zip = dir + ".pad-nm.zip";
            win.Progress($"整理依赖 {i + 1}/{dirs.Count} · {Path.GetFileName(Path.GetDirectoryName(dir) ?? dir)}",
                null, "整理整合包", job);
            var ok = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(zip)) File.Delete(zip);
                    ZipFile.CreateFromDirectory(dir, zip, CompressionLevel.Fastest, false);
                    Directory.Delete(dir, true);
                    return true;
                }
                catch
                {
                    try { if (File.Exists(zip)) File.Delete(zip); } catch { /* ignore */ }
                    return false;
                }
            }, ct);
            if (ok && zip.StartsWith(launcherRoot, StringComparison.OrdinalIgnoreCase))
                folded.Add(Path.GetRelativePath(launcherRoot, zip).Replace('\\', '/'));
        }
        // 写清单：首次启动 PAD 只读这个小文件，不再递归扫描整个 .pack-launcher。
        if (folded.Count > 0)
        {
            var manifest = Path.Combine(launcherRoot, "pad-unpack.json");
            Directory.CreateDirectory(launcherRoot);
            await File.WriteAllTextAsync(manifest,
                System.Text.Json.JsonSerializer.Serialize(new { zips = folded }), ct);
        }
    }

    static List<string> FindNodeModules(string root)
    {
        List<string> all;
        try { all = Directory.EnumerateDirectories(root, "node_modules", SearchOption.AllDirectories).ToList(); }
        catch { return []; }
        var top = new List<string>();
        foreach (var dir in all.OrderBy(d => d.Length))
        {
            if (!top.Any(t => dir.StartsWith(t + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                top.Add(dir);
        }
        return top;
    }
    /// <summary>整合包单文件后缀：有 7-Zip 时是 .7z，否则退回 .zip。</summary>
    public static string SingleFileExtension => SevenZip.Found ? ".7z" : ".zip";

    /// <summary>
    /// 打整合包：用 7z 容器（没有 ZIP64 上限），没有 7-Zip 时退回 .zip。
    /// 没有就退回 zip，再没有 7-Zip 就退回 .NET 压缩。
    /// </summary>
    public static async Task PackAsync(MainWindow win, Launcher launcher, string srcDir, string outPath,
        long estimate, JobRecord? job = null, CancellationToken ct = default)
    {
        var seven = SevenZip.Locate();
        if (seven is not null)
        {
            try
            {
                await Zip7zAsync(win, launcher, seven, srcDir, outPath, estimate, job, ct);
                return;
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { /* ignore */ }
                throw;
            }
            catch
            {
                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { /* ignore */ }
                // 7-Zip 用不了就退回 .NET zip（后缀也会是 .zip）
                throw;
            }
        }
        await ZipDotNetAsync(win, launcher, srcDir, outPath, estimate, job, ct);
    }

    static async Task Zip7zAsync(MainWindow win, Launcher launcher, string seven, string srcDir,
        string zipPath, long estimate, JobRecord? job, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var lastPct = -1;
        var lastZipBytes = 0L;
        var lastZipSec = 0.0;
        var tail = new System.Text.StringBuilder();
        var psi = new ProcessStartInfo(seven)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = srcDir,
        };
        psi.ArgumentList.Add("a");
        psi.ArgumentList.Add("-t7z"); // 7z 容器：没有 ZIP64 上限，Win11 资源管理器/7-Zip 都能打开
        psi.ArgumentList.Add(zipPath);
        psi.ArgumentList.Add(".");
        psi.ArgumentList.Add("-mx=1");
        psi.ArgumentList.Add("-mmt=on");
        psi.ArgumentList.Add("-snl"); // junction/符号链接按链接存，别展开成几 GB
        psi.ArgumentList.Add("-bsp1");
        psi.ArgumentList.Add("-bso0");
        psi.ArgumentList.Add("-y");


        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("7-Zip 启动失败");
        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { /* gone */ } });
        var buffer = new char[512];
        var line = new System.Text.StringBuilder();
        while (true)
        {
            if (job is not null && launcher.IsPaused(job.Id))
            {
                Reparse.SuspendProcess(proc);
                try { while (launcher.IsPaused(job.Id) && !ct.IsCancellationRequested) Thread.Sleep(150); }
                finally { Reparse.ResumeProcess(proc); }
                ct.ThrowIfCancellationRequested();
            }
            var read = await proc.StandardOutput.ReadAsync(buffer.AsMemory(), ct);
            if (read <= 0) break;
            for (var i = 0; i < read; i++)
            {
                var ch = buffer[i];
                if (ch is '\r' or '\n')
                {
                    HandleLine(line.ToString());
                    line.Clear();
                }
                else line.Append(ch);
            }
        }
        HandleLine(line.ToString());
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (tail.Length == 0 && stderr.Length > 0) tail.Append(stderr);
        if (proc.ExitCode != 0 || !File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
            throw new InvalidOperationException("7-Zip 压缩失败：" + tail.ToString().Trim());
        if (job is not null) launcher.UpdateLeaf(job.Id, "zip", 100, 100);

        void HandleLine(string raw)
        {
            var text = (raw ?? "").Trim();
            if (text.Length == 0) return;
            var m = Regex.Match(text, @"(\d{1,3})%");
            if (!m.Success)
            {
                if (text.Contains("Error", StringComparison.OrdinalIgnoreCase)) tail.AppendLine(text);
                return;
            }
            if (!int.TryParse(m.Groups[1].Value, out var pct) || pct == lastPct) return;
            lastPct = pct;
            var sec = clock.Elapsed.TotalSeconds;
            var eta = pct > 2 ? TimeSpan.FromSeconds(sec * (100 - pct) / pct) : TimeSpan.Zero;
            long zipBytes = 0;
            try { zipBytes = File.Exists(zipPath) ? new FileInfo(zipPath).Length : 0; } catch { /* 正在写 */ }
            var rate = lastZipSec > 0 ? Math.Max(0, zipBytes - lastZipBytes) / Math.Max(0.3, sec - lastZipSec) : 0;
            lastZipBytes = zipBytes;
            lastZipSec = sec;
            var lineText = $"7-Zip 多线程 · 已用 {TimeSpan.FromSeconds(sec):mm\\:ss}";
            if (rate > 0) lineText += $" · {Rate(rate)}";
            if (eta > TimeSpan.Zero) lineText += $" · 剩余 {eta:mm\\:ss}";
            win.Progress($"{lineText} · 整体 {0.75 + 0.25 * pct / 100.0:P0}", 0.75 + 0.25 * pct / 100.0, "压缩整合包", job);
            if (job is not null) launcher.UpdateLeaf(job.Id, "zip", pct, 100);
        }
    }
    static async Task ZipDotNetAsync(MainWindow win, Launcher launcher, string srcDir, string zipPath,
        long estimate, JobRecord? job, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            long total = 0;
            var sizes = new List<(string File, long Size)>(files.Count);
            foreach (var f in files)
            {
                long size = 0;
                try { size = new FileInfo(f).Length; } catch { /* gone */ }
                total += size;
                sizes.Add((f, size));
            }
            if (total <= 0) total = Math.Max(1, estimate);

            var clock = Stopwatch.StartNew();
            long done = 0;
            long lastReport = 0;
            long lastDone = 0;
            var lastAt = 0.0;
            using var fs = File.Create(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var buffer = new byte[1 << 20];
            foreach (var (file, size) in sizes)
            {
                if (job is not null) launcher.WaitWhilePaused(job.Id, ct);
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(srcDir, file).Replace('\\', '/');
                // 已经压过的大家伙（PAD 单文件 exe）再 deflate 纯浪费时间：直接 store。
                var level = size >= 16L * 1024 * 1024 ? CompressionLevel.NoCompression : CompressionLevel.Fastest;
                var entry = zip.CreateEntry(rel, level);
                using var source = File.OpenRead(file);
                using var target = entry.Open();
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    target.Write(buffer, 0, read);
                    done += read;
                    if (done - lastReport < 8L * 1024 * 1024) continue;
                    lastReport = done;
                    var sec = clock.Elapsed.TotalSeconds;
                    var dt = Math.Max(0.3, sec - lastAt);
                    var speed = Math.Max(0, done - lastDone) / dt;
                    lastDone = done;
                    lastAt = sec;
                    var eta = speed > 1024 ? TimeSpan.FromSeconds(Math.Max(0, total - done) / speed) : TimeSpan.Zero;
                    var text = $"{Mb(done)}/{Mb(total)} MB · {Rate(speed)} · {done * 100 / Math.Max(1, total)}%";
                    if (eta > TimeSpan.Zero) text += $" · 剩余 {eta:mm\\:ss}";
                    win.Progress(text, 0.75 + 0.25 * Math.Min(1.0, total > 0 ? (double)done / total : 0), "压缩整合包", job);
                    if (job is not null)
                        launcher.UpdateLeaf(job.Id, "zip", (int)(done / 1024), (int)(total / 1024));
                }
            }
        }, ct);
    }
}
