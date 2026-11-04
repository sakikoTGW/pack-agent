using System.IO;

namespace Pad.Core;

/// <summary>
/// 便携包 zip 用 7-Zip（多线程 deflate，比 .NET 单线程快）。程序目录自带一份，
/// 找不到就用系统安装的，再没有就退回 .NET 压缩。
/// </summary>
public static class SevenZip
{
    static string? _cached;

    public static string? Locate()
    {
        if (_cached is not null) return _cached.Length == 0 ? null : _cached;
        var env = Environment.GetEnvironmentVariable("PAD_7Z_EXE");
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Assets", "7z", "7z.exe"));
        candidates.Add(Path.Combine(baseDir, "7z", "7z.exe"));
        candidates.Add(Path.Combine(baseDir, "7z.exe"));
        candidates.Add(@"C:\Program Files\7-Zip\7z.exe");
        candidates.Add(@"C:\Program Files (x86)\7-Zip\7z.exe");
        var hit = candidates.FirstOrDefault(p => p is not null && File.Exists(p));
        if (hit is null) hit = ExtractEmbedded();
        _cached = hit ?? "";
        return _cached.Length == 0 ? null : _cached;
    }

    public static bool Found => Locate() is not null;

    /// <summary>
    /// 单文件 PAD：把内嵌的 7z.exe/7z.dll 释放到 LOCALAPPDATA\pack-agent-dsh\tools\7z\，
    /// 两个文件必须同目录（7z.exe 加载同目录 7z.dll）。
    /// </summary>
    static string? ExtractEmbedded()
    {
        try
        {
            var asm = typeof(SevenZip).Assembly;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "pack-agent-dsh", "tools", "7z");
            var exe = Path.Combine(dir, "7z.exe");
            var dll = Path.Combine(dir, "7z.dll");
            using var exeStream = asm.GetManifestResourceStream("Pad.tools.7z.exe");
            using var dllStream = asm.GetManifestResourceStream("Pad.tools.7z.dll");
            if (exeStream is null || dllStream is null) return File.Exists(exe) ? exe : null;
            Directory.CreateDirectory(dir);
            if (!File.Exists(exe) || new FileInfo(exe).Length != exeStream.Length)
            {
                var tmp = exe + ".tmp";
                using (var fs = File.Create(tmp)) exeStream.CopyTo(fs);
                File.Move(tmp, exe, overwrite: true);
            }
            if (!File.Exists(dll) || new FileInfo(dll).Length != dllStream.Length)
            {
                var tmp = dll + ".tmp";
                using (var fs = File.Create(tmp)) dllStream.CopyTo(fs);
                File.Move(tmp, dll, overwrite: true);
            }
            return exe;
        }
        catch { return null; }
    }

    static string? _sfx;

    /// <summary>7z 自解压模块：便携包输出成 .exe，双击就能解压（Windows 自带的 zip 打不开 ZIP64 大包）。</summary>
    public static string? SfxPath()
    {
        if (_sfx is not null) return _sfx.Length == 0 ? null : _sfx;
        var env = Environment.GetEnvironmentVariable("PAD_7Z_SFX");
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Assets", "7z", "7z.sfx"));
        candidates.Add(Path.Combine(baseDir, "7z", "7z.sfx"));
        var seven = Locate();
        if (seven is not null)
        {
            var dir = Path.GetDirectoryName(seven);
            if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir, "7z.sfx"));
        }
        candidates.Add(@"C:\Program Files\7-Zip\7z.sfx");
        candidates.Add(@"C:\Program Files (x86)\7-Zip\7z.sfx");
        _sfx = candidates.FirstOrDefault(p => p is not null && File.Exists(p)) ?? "";
        return _sfx.Length == 0 ? null : _sfx;
    }

    /// <summary>便携包单文件后缀：有 SFX 就 .exe，否则退回 .zip。</summary>
    public static string PortableExtension => SfxPath() is not null ? ".exe" : ".zip";
}
