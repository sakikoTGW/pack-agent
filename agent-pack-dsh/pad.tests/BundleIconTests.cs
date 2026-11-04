using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using Pad.Core;
using Xunit;

namespace Pad.Tests;

/// <summary>
/// 官方插件图标是包根 <c>icon.svg</c>（<c>dsh-experimental-*</c> 全这样）。两个退化点各钉一例：
/// 1) <see cref="LaunchPolicy.BundleIconFiles"/> 必须包含 <c>icon.svg</c>；
/// 2) 光栅化必须处理「根 fill=none + g transform + 渐变 fill + stroke」，不能出空白 png。
/// WPF RenderTargetBitmap 需要 STA，统一用 STA 线程跑。
/// </summary>
public sealed class BundleIconTests
{
    const string OfficialStyleSvg = """
        <svg width="36" height="36" viewBox="0 0 36 36" fill="none" xmlns="http://www.w3.org/2000/svg">
          <g transform="translate(1.5 1.5) scale(0.9166666667)">
            <path d="M18 6L28.5 9.5V17.2C28.5 23.1 24.08 28.4 18 30C11.92 28.4 7.5 23.1 7.5 17.2V9.5L18 6Z"
                  fill="url(#a)" fill-opacity="0.8" />
            <path d="M14.2 18L16.84 20.63L22.2 15.27" stroke="#FFFFFF" stroke-width="2.2"
                  stroke-linecap="round" stroke-linejoin="round" />
          </g>
          <defs>
            <linearGradient id="a" x1="18" y1="6" x2="18" y2="30" gradientUnits="userSpaceOnUse">
              <stop stop-color="#7AC2FF" />
              <stop offset="1" stop-color="#4A65E8" />
            </linearGradient>
          </defs>
        </svg>
        """;

    static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "pad-bundle-icon-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

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

    /// <summary>数一下 png 里不透明的像素；空白图标 = 0。</summary>
    static int OpaquePixels(string png)
    {
        var decoder = BitmapDecoder.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);
        var count = 0;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] > 8) count++;
        return count;
    }

    [Fact]
    public void Resolve_FindsOfficialIconSvg_AndRasterizesToCache()
    {
        var launcher = Temp();
        var packageDir = Path.Combine(launcher, "profiles", "pkg");
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(Path.Combine(packageDir, "icon.svg"), OfficialStyleSvg);

        var resolved = OnSta(() => BundleIcon.Resolve(launcher, packageDir, "demo-official"));

        Assert.NotNull(resolved);
        Assert.EndsWith(".png", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("library", "plugins-meta"), resolved);
        Assert.True(File.Exists(resolved));
        Assert.True(new FileInfo(resolved!).Length > 800, "空白 png 会非常小，说明 SVG 没画出来");
        Assert.True(OnSta(() => OpaquePixels(resolved!)) > 200, "图标应有可见像素");
    }

    [Fact]
    public void Rasterize_OfficialStyleSvg_IsNotBlank()
    {
        var dir = Temp();
        var png = Path.Combine(dir, "icon.png");

        var ok = OnSta(() => BundleIcon.TryRasterizeSvg(OfficialStyleSvg, png, 64));

        Assert.True(ok, BundleIcon.LastRasterizeError);
        Assert.True(File.Exists(png));
        Assert.True(OnSta(() => OpaquePixels(png)) > 200, "根 fill=none 不能导致整棵树被跳过");
    }
}
