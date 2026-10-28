using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;

namespace Pad.Core;

/// <summary>
/// 整合包首次启动：把导出时折叠的 node_modules（*.pad-nm.zip）解出来，再删掉内层包。
/// 只读导出时写好的 <c>.pack-launcher/pad-unpack.json</c>，不做全盘递归扫描（那会让启动卡死）。
/// </summary>
public static class PortableBootstrap
{
    sealed class Manifest
    {
        public List<string> Zips { get; set; } = [];
    }

    public static void RunIfNeeded()
    {
        var root = Path.Combine(AppContext.BaseDirectory, ".pack-launcher");
        var manifestPath = Path.Combine(root, "pad-unpack.json");
        if (!File.Exists(manifestPath)) return;

        List<string> zips;
        try
        {
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
            zips = (manifest?.Zips ?? [])
                .Select(rel => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists)
                .ToList();
        }
        catch { return; }
        if (zips.Count == 0)
        {
            try { File.Delete(manifestPath); } catch { /* ignore */ }
            return;
        }

        var window = new Views.UnpackWindow(zips.Count);
        window.Show();
        try
        {
            for (var i = 0; i < zips.Count; i++)
            {
                var zip = zips[i];
                var target = zip[..^".pad-nm.zip".Length];
                Directory.CreateDirectory(target);
                using var archive = ZipFile.OpenRead(zip);
                long total = archive.Entries.Count;
                long done = 0;
                foreach (var entry in archive.Entries)
                {
                    done++;
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(Path.Combine(target, entry.FullName));
                        continue;
                    }
                    var dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
                    if (!dest.StartsWith(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
                    var dir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    try { entry.ExtractToFile(dest, overwrite: true); } catch { /* 被杀软占用就下次覆盖 */ }
                    if (done % 100 == 0)
                        window.Tick($"解压依赖 {i + 1}/{zips.Count} · {done}/{total}", done, (int)total);
                }
                try { File.Delete(zip); } catch { /* 下次再删 */ }
                window.Tick($"解压依赖 {i + 1}/{zips.Count} 完成", 1, 1);
            }
            try { File.Delete(manifestPath); } catch { /* ignore */ }
        }
        finally
        {
            window.Close();
        }
    }
}
