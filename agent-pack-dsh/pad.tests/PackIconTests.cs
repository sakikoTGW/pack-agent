using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// D105 UX：整合包自己的图标解析/缓存（PackIcon）。只在临时目录造包，不碰真实 launcher；
/// 不联网。SVG 光栅化需要 STA（WPF RenderTargetBitmap），用 STA 线程跑那一例。
/// </summary>
public sealed class PackIconTests
{
    // ---- fixture ---------------------------------------------------------

    sealed record Fixture(string Root)
    {
        public string LauncherRoot => Path.Combine(Root, "launcher");
        public string CacheDir => Path.Combine(LauncherRoot, "library", "icons", "packs");

        public string WriteZip(string fileName, params (string Name, byte[] Bytes)[] entries)
        {
            var path = Path.Combine(Root, fileName);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
            return path;
        }

        public string WriteFile(string relative, byte[] bytes)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public string WriteJson(string relative, string json) =>
            WriteFile(relative, Encoding.UTF8.GetBytes(json));
    }

    static Fixture Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-pack-icon-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new Fixture(root);
    }

    static byte[] Png(int size = 64) => [0x89, 0x50, 0x4E, 0x47, .. new byte[size]];

    static T? OnSta<T>(Func<T> body)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = body(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException(error.Message, error);
        return result;
    }

    // ---- zip -------------------------------------------------------------

    [Fact]
    public void Zip_root_icon_is_extracted_and_cached_then_reused()
    {
        var f = Temp();
        var icon = Png();
        var pack = f.WriteZip("Test-0.1.0.pack.zip", ("pack.json", "{}"u8.ToArray()), ("icon.png", icon));

        var first = PackIcon.Resolve(f.LauncherRoot, pack);
        Assert.NotNull(first);
        Assert.True(File.Exists(first));
        Assert.StartsWith(f.CacheDir, first!, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", first, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(icon, File.ReadAllBytes(first));
        var stamp = File.GetLastWriteTimeUtc(first);

        var second = PackIcon.Resolve(f.LauncherRoot, pack);
        Assert.Equal(first, second);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(second!));   // 复用缓存，不重写
    }

    [Fact]
    public void Zip_icon_inside_single_top_level_directory_is_found()
    {
        var f = Temp();
        var pack = f.WriteZip("Nested.pack.zip", ("MyPack/pack.json", "{}"u8.ToArray()), ("MyPack/logo.png", Png()));
        var icon = PackIcon.Resolve(f.LauncherRoot, pack);
        Assert.NotNull(icon);
        Assert.True(File.Exists(icon));
    }

    [Fact]
    public void Zip_without_icon_returns_null()
    {
        var f = Temp();
        var pack = f.WriteZip("NoIcon.pack.zip", ("pack.json", "{}"u8.ToArray()), ("readme.md", "hi"u8.ToArray()));
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, pack));
    }

    [Fact]
    public void Broken_zip_does_not_throw()
    {
        var f = Temp();
        var notZip = f.WriteFile("Broken.pack.zip", Encoding.UTF8.GetBytes("not a zip at all"));
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, notZip));
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, Path.Combine(f.Root, "missing.pack.zip")));
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, null));
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, ""));
    }

    [Fact]
    public void Oversized_zip_icon_is_rejected()
    {
        var f = Temp();
        var big = new byte[2 * 1024 * 1024 + 1];
        var pack = f.WriteZip("Big.pack.zip", ("icon.png", big));
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, pack));
    }

    [Fact]
    public void Webp_zip_icon_falls_back_to_null()
    {
        var f = Temp();
        var pack = f.WriteZip("Webp.pack.zip", ("icon.webp", Png()));
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, pack));
    }

    [Fact]
    public void Cache_key_changes_when_pack_file_changes()
    {
        var f = Temp();
        var pack = f.WriteZip("V1.pack.zip", ("icon.png", Png()));
        var first = PackIcon.Resolve(f.LauncherRoot, pack);
        Assert.NotNull(first);

        // 模拟包更新（mtime 变）：同路径不同时间的包应换缓存文件。
        File.SetLastWriteTimeUtc(pack, DateTime.UtcNow.AddMinutes(5));
        var second = PackIcon.Resolve(f.LauncherRoot, pack);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    // ---- pack.json -------------------------------------------------------

    [Fact]
    public void Pack_json_uses_sibling_icon_file()
    {
        var f = Temp();
        var json = f.WriteJson("Demo.pack.json", """{"name":"Demo"}""");
        var icon = f.WriteFile("icon.png", Png());
        var resolved = PackIcon.Resolve(f.LauncherRoot, json);
        Assert.Equal(icon, resolved);
    }

    [Fact]
    public void Pack_json_honours_icon_field()
    {
        var f = Temp();
        var json = f.WriteJson("Demo.pack.json", """{"name":"Demo","icon":"./assets/pack-logo.png"}""");
        var icon = Path.GetFullPath(f.WriteFile("assets/pack-logo.png", Png()));
        Assert.Equal(icon, PackIcon.Resolve(f.LauncherRoot, json));
    }

    [Fact]
    public void Pack_json_without_any_icon_returns_null()
    {
        var f = Temp();
        var json = f.WriteJson("Demo.pack.json", """{"name":"Demo"}""");
        Assert.Null(PackIcon.Resolve(f.LauncherRoot, json));
    }

    // ---- svg -------------------------------------------------------------

    [Fact]
    public void Zip_svg_icon_is_rasterized_to_png_cache_on_sta()
    {
        var f = Temp();
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128">
              <rect x="8" y="8" width="112" height="112" rx="20" fill="#278197" />
            </svg>
            """;
        var pack = f.WriteZip("Svg.pack.zip", ("icon.svg", Encoding.UTF8.GetBytes(svg)));

        var png = OnSta(() => PackIcon.Resolve(f.LauncherRoot, pack));
        Assert.NotNull(png);
        Assert.EndsWith(".png", png, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(png));
        Assert.True(new FileInfo(png!).Length > 0);
    }

    // ---- 清理 ------------------------------------------------------------

    [Fact]
    public void TrimCache_removes_only_old_files()
    {
        var f = Temp();
        Directory.CreateDirectory(f.CacheDir);
        var old = Path.Combine(f.CacheDir, "deadbeefdeadbeef.png");
        var fresh = Path.Combine(f.CacheDir, "0123456789abcdef.png");
        File.WriteAllBytes(old, Png());
        File.WriteAllBytes(fresh, Png());
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

        PackIcon.TrimCache(f.LauncherRoot);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }
}
