using System.IO;
using System.IO.Compression;

namespace Pad.Core;

/// <summary>
/// PCL 式单文件分发：发布时把 <c>pack-runtime.zip</c> 追加到 PAD exe 末尾，
/// 首次真正需要 pack 运行时时，就近解压到 exe 旁边的 <c>.pack-runtime/</c>。
/// 用户只下载一个 exe，数据（.pack-launcher / .pack-runtime）都跟着 exe 走。
/// </summary>
public static class SelfBundle
{
    const string Magic = "PADPACKRT1";
    const int MagicLen = 10;
    const int Footer = 40;

    static string? ExePath()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path) ? null : path;
    }

    /// <summary>exe 末尾追加的 payload：(偏移, 长度)；没有就是 nullptr。</summary>
    public static (long Offset, long Length, string Hash)? Payload()
    {
        var exe = ExePath();
        if (exe is null) return null;
        try
        {
            using var fs = File.OpenRead(exe);
            if (fs.Length < Footer) return null;
            fs.Seek(-Footer, SeekOrigin.End);
            var footer = new byte[Footer];
            fs.ReadExactly(footer);
            if (!footer.AsSpan(0, MagicLen).SequenceEqual("PADPACKRT1"u8)) return null;
            var len = BitConverter.ToInt64(footer, MagicLen);
            var off = BitConverter.ToInt64(footer, MagicLen + 8);
            if (off <= 0 || len <= 0 || off + len > fs.Length) return null;
            var hash = Convert.ToHexString(footer, MagicLen + 16, 8);
            return (off, len, hash);
        }
        catch { return null; }
    }

    public static bool HasPayload() => Payload() is not null;

    /// <summary>把内置运行时解压到 exe 旁 <c>.pack-runtime/</c>；已是最新就复用。</summary>
    public static string? EnsureRuntime()
    {
        var payload = Payload();
        var exe = ExePath();
        if (payload is null || exe is null) return null;
        var dir = Path.Combine(Path.GetDirectoryName(exe)!, ".pack-runtime");
        var stamp = Path.Combine(dir, ".payload");
        var sig = $"{payload.Value.Offset}-{payload.Value.Length}-{payload.Value.Hash}";
        try
        {
            if (File.Exists(stamp)
                && File.ReadAllText(stamp).Trim() == sig
                && File.Exists(Path.Combine(dir, "packagent.bundle.js"))
                && File.Exists(Path.Combine(dir, "bun.exe")))
                return dir;
        }
        catch { /* 损坏就重解 */ }

        var tmp = Path.Combine(Path.GetTempPath(), "pad-runtime-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var src = File.OpenRead(exe))
            using (var dst = File.Create(tmp))
            {
                src.Seek(payload.Value.Offset, SeekOrigin.Begin);
                CopyExactly(src, dst, payload.Value.Length);
            }
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            ZipFile.ExtractToDirectory(tmp, dir);
            File.WriteAllText(stamp, sig);
            return dir;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    static void CopyExactly(Stream src, Stream dst, long count)
    {
        var buf = new byte[1024 * 1024];
        var left = count;
        while (left > 0)
        {
            var want = (int)Math.Min(buf.Length, left);
            var read = src.Read(buf, 0, want);
            if (read <= 0) throw new IOException("自解压 payload 读不完整");
            dst.Write(buf, 0, read);
            left -= read;
        }
    }
}
