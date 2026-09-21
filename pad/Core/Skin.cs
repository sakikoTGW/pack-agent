using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pad.Core;

/// <summary>Wallpaper / logo paths from pad.json. Relative paths hang off the launcher root.</summary>
public static class Skin
{
    public static string Resolve(string? path)
    {
        path = (path ?? "").Trim();
        if (path.Length == 0) return "";
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(AppState.Current.Launcher.Root, path));
    }

    public static BitmapImage? LoadBitmap(string? path, Uri? packFallback = null)
    {
        try
        {
            var abs = Resolve(path);
            if (abs.Length > 0 && File.Exists(abs))
            {
                if (abs.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var png = Path.Combine(Path.GetDirectoryName(abs) ?? "",
                        Path.GetFileNameWithoutExtension(abs) + ".png");
                    if (!File.Exists(png)) BundleIcon.TryRasterizeSvg(File.ReadAllText(abs), png);
                    abs = png;
                }
                if (File.Exists(abs) && !abs.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.UriSource = new Uri(abs, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            if (packFallback is not null)
            {
                var bmp = new BitmapImage(packFallback);
                bmp.Freeze();
                return bmp;
            }
        }
        catch { /* bad file */ }
        return null;
    }

    public static Stretch Fit(string fit) => fit switch
    {
        "fill" => Stretch.Fill,
        "contain" => Stretch.Uniform,
        _ => Stretch.UniformToFill,
    };

    public static SolidColorBrush Wash(Brush? baseBrush, int opacityPct)
    {
        var c = Colors.White;
        if (baseBrush is SolidColorBrush sb) c = sb.Color;
        var a = (byte)Math.Clamp((int)Math.Round(opacityPct / 100.0 * 255), 0, 255);
        var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
