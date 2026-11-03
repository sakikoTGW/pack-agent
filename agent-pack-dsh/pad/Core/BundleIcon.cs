using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pad.Core;

/// <summary>
/// The mark a combination package publishes. TUI's is
/// <c>docs/assets/logo.svg</c> in <c>ccch1mneyyy/dsh-TUI</c>; the npm tarball
/// usually drops <c>docs/</c>, so the second look is jsDelivr of that file.
/// <c>dsh-plugin.json</c> v0.15 has no icon field.
/// </summary>
public static class BundleIcon
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public static event Action? Changed;

    static BundleIcon()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "pack-agent-for-DSH");
    }

    static void Notify()
    {
        try { Changed?.Invoke(); }
        catch { }
    }

    public static string CacheDir(string launcherRoot, string spec)
    {
        var safe = LaunchPolicy.BundleBareName(spec).Replace('/', '_').Replace('@', '_');
        return Path.Combine(launcherRoot, "library", "plugins-meta", safe);
    }

    public static string? Resolve(string launcherRoot, string packageDir, string spec)
    {
        foreach (var name in LaunchPolicy.BundleIconFiles)
        {
            var p = Path.Combine(packageDir, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return p;
        }
        if (ResolvePackageIcon(launcherRoot, packageDir, spec) is { } npmIcon) return npmIcon;
        var cache = CacheDir(launcherRoot, spec);
        foreach (var name in new[] { "icon.png", "logo.svg", "logo.png" })
        {
            var p = Path.Combine(cache, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// 由 DSH 发行号自带、但包里没有 logo 的插件（base / web-app 之类），用 DSH 自己的品牌图标兜底。
    /// 判断靠“包是从发行号的 pnpm 仓库解析到的”，不靠包名前缀。
    /// </summary>
    public static string? SeedDshBrand(string launcherRoot, string spec)
    {
        try
        {
            var cache = CacheDir(launcherRoot, spec);
            Directory.CreateDirectory(cache);
            var dest = Path.Combine(cache, "icon.png");
            if (File.Exists(dest)) return dest;
            var uri = new Uri("pack://application:,,,/Assets/brand/dsh.png");
            using var s = Application.GetResourceStream(uri)?.Stream;
            if (s is null) return null;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            File.WriteAllBytes(dest, ms.ToArray());
            Notify();
            return dest;
        }
        catch { return null; }
    }

    /// <summary>没有本地 package.json 时，按 GitHub 仓库（npm 元数据里的 repository）取插件自己的图标。</summary>
    public static async Task<string?> EnsureFromRepo(string launcherRoot, string spec, string? repo,
        CancellationToken ct)
    {
        if (Resolve(launcherRoot, "", spec) is { } have) return have;
        var cache = CacheDir(launcherRoot, spec);
        Directory.CreateDirectory(cache);
        foreach (var url in LaunchPolicy.BundleIconUrls(repo, null))
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(cache,
                url.Contains(".svg", StringComparison.OrdinalIgnoreCase) ? "logo.svg" : "icon.png");
            try
            {
                using var res = await Http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) continue;
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 40 || bytes.Length > 400_000) continue;
                await File.WriteAllBytesAsync(dest, bytes, ct);
                Notify();
                return dest;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* next url */ }
        }
        return null;
    }

    public static string? First(string launcherRoot, IEnumerable<DshProfile> profiles)
    {
        string? fallback = null;
        foreach (var p in profiles)
            foreach (var spec in p.Bundles)
            {
                var dir = LaunchPolicy.BundlePackageDir(p.Dir, spec);
                var icon = Resolve(launcherRoot, dir, spec);
                if (icon is null) continue;
                if (spec.Contains("dsh-tui", StringComparison.OrdinalIgnoreCase)) return icon;
                fallback ??= icon;
            }
        return fallback;
    }

    public static async Task Ensure(string launcherRoot, string packageDir, string spec,
        CancellationToken ct)
    {
        if (Resolve(launcherRoot, packageDir, spec) is not null) return;
        var repo = ReadRepository(Path.Combine(packageDir, "package.json"));
        var ver = ReadVersion(Path.Combine(packageDir, "package.json"));
        var cache = CacheDir(launcherRoot, spec);
        Directory.CreateDirectory(cache);
        foreach (var url in LaunchPolicy.BundleIconUrls(repo, ver))
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(url);
            if (string.IsNullOrEmpty(ext)) continue;
            var dest = Path.Combine(cache, ext.Equals(".svg", StringComparison.OrdinalIgnoreCase)
                ? "logo.svg" : "icon.png");
            try
            {
                using var res = await Http.GetAsync(url, ct);
                if (!res.IsSuccessStatusCode) continue;
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length < 40 || bytes.Length > 400_000) continue;
                await File.WriteAllBytesAsync(dest, bytes, ct);
                Notify();
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* next url */ }
        }
    }

    /// <summary>已安装包自带的 npm 官方 icon：读 package.json 的 icon，本地文件优先，SVG 光栅化。</summary>
    static string? ResolvePackageIcon(string launcherRoot, string packageDir, string spec)
    {
        if (string.IsNullOrWhiteSpace(packageDir)) return null;
        var pkg = Path.Combine(packageDir, "package.json");
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            var icon = doc.RootElement.TryGetProperty("icon", out var value) ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(icon)) return null;
            var relative = icon.Trim().Replace('\\', '/').TrimStart('/');
            while (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
            var local = Path.Combine(packageDir,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(local)) return null;

            if (!Path.GetExtension(local).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                return local;

            var cache = CacheDir(launcherRoot, spec);
            Directory.CreateDirectory(cache);
            var png = Path.Combine(cache, "icon-npm.png");
            if (File.Exists(png)) return png;
            return TryRasterizeSvg(File.ReadAllText(local), png) ? png : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string? ReadRepository(string pkg)
    {
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            var root = doc.RootElement;
            if (root.TryGetProperty("repository", out var repo))
            {
                if (repo.ValueKind == JsonValueKind.String) return repo.GetString();
                if (repo.ValueKind == JsonValueKind.Object
                    && repo.TryGetProperty("url", out var u))
                    return u.GetString();
            }
        }
        catch { }
        return null;
    }

    static string? ReadVersion(string pkg)
    {
        if (!File.Exists(pkg)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(pkg));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>WPF cannot bind an SVG file. Draw the path fills onto a PNG. STA only.</summary>
    public static bool TryRasterizeSvg(string svg, string pngPath, int size = 64)
    {
        try
        {
            var root = XDocument.Parse(svg).Root;
            if (root is null) return false;

            double minX = 0, minY = 0, vw = 128, vh = 128;
            var parts = ((string?)root.Attribute("viewBox"))?.Split(
                [' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (parts is { Length: 4 } &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out minX) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out minY) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out vw) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out vh) &&
                vw > 0 && vh > 0)
            {
                // ok
            }
            else
            {
                minX = minY = 0;
                vw = vh = 128;
                if (double.TryParse((string?)root.Attribute("width"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0)
                    vw = w;
                if (double.TryParse((string?)root.Attribute("height"),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0)
                    vh = h;
            }

            var drawing = new DrawingGroup
            {
                ClipGeometry = new RectangleGeometry(new Rect(minX, minY, vw, vh)),
            };
            drawing.Children.Add(WalkSvg(root, Colors.Black));

            var side = Math.Max(vw, vh);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-minX, -minY));
                dc.PushTransform(new ScaleTransform(size / side, size / side));
                dc.DrawDrawing(drawing);
                dc.Pop();
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var dir = Path.GetDirectoryName(pngPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(pngPath);
            enc.Save(fs);
            return new FileInfo(pngPath).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static DrawingGroup WalkSvg(XElement element, Color inherited)
    {
        var group = new DrawingGroup();
        if (SvgTransform((string?)element.Attribute("transform")) is { } transform)
            group.Transform = transform;

        var fillText = (string?)element.Attribute("fill") ?? SvgStyle((string?)element.Attribute("style"), "fill");
        var fill = fillText switch
        {
            null => inherited,
            var t when t.Equals("none", StringComparison.OrdinalIgnoreCase) => Colors.Transparent,
            var t when SvgColor(t) is { } c => c,
            _ => inherited,
        };
        if (fillText is not null && fillText.Equals("none", StringComparison.OrdinalIgnoreCase))
            return group; // 子树全部不画

        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            if (name is "g" or "svg")
            {
                group.Children.Add(WalkSvg(child, fill));
                continue;
            }

            var geometry = SvgGeometry(child);
            if (geometry is null) continue;

            var shapeFillText = (string?)child.Attribute("fill")
                                ?? SvgStyle((string?)child.Attribute("style"), "fill");
            var shapeFill = shapeFillText is null
                ? fill
                : shapeFillText.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? Colors.Transparent
                    : SvgColor(shapeFillText) ?? fill;
            if (shapeFill == Colors.Transparent) continue;

            var shape = new DrawingGroup();
            if (SvgTransform((string?)child.Attribute("transform")) is { } st) shape.Transform = st;
            shape.Children.Add(new GeometryDrawing(new SolidColorBrush(shapeFill), null, geometry));
            group.Children.Add(shape);
        }
        return group;
    }

    static Geometry? SvgGeometry(XElement el) => el.Name.LocalName switch
    {
        "rect" => RectGeometry(el),
        "circle" => CircleGeometry(el),
        "ellipse" => EllipseShape(el),
        "line" => LineGeometry(el),
        "polygon" or "polyline" => PolyGeometry(el, el.Name.LocalName == "polygon"),
        "path" => PathGeometry(el),
        _ => null,
    };

    static Geometry? RectGeometry(XElement el)
    {
        var x = SvgNum(el, "x");
        var y = SvgNum(el, "y");
        var w = SvgNum(el, "width");
        var h = SvgNum(el, "height");
        if (w <= 0 || h <= 0) return null;
        var rx = SvgNum(el, "rx");
        var ry = SvgNum(el, "ry");
        if (rx <= 0 && ry > 0) rx = ry;
        if (ry <= 0 && rx > 0) ry = rx;
        return rx > 0 || ry > 0
            ? new RectangleGeometry(new Rect(x, y, w, h), rx, ry)
            : new RectangleGeometry(new Rect(x, y, w, h));
    }

    static Geometry? CircleGeometry(XElement el)
    {
        var r = SvgNum(el, "r");
        return r <= 0 ? null : new EllipseGeometry(new Point(SvgNum(el, "cx"), SvgNum(el, "cy")), r, r);
    }

    static Geometry? EllipseShape(XElement el)
    {
        var rx = SvgNum(el, "rx");
        var ry = SvgNum(el, "ry");
        return rx <= 0 || ry <= 0
            ? null
            : new System.Windows.Media.EllipseGeometry(new Point(SvgNum(el, "cx"), SvgNum(el, "cy")), rx, ry);
    }

    static Geometry? LineGeometry(XElement el)
    {
        var x1 = SvgNum(el, "x1");
        var y1 = SvgNum(el, "y1");
        var x2 = SvgNum(el, "x2");
        var y2 = SvgNum(el, "y2");
        return Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 0.001
            ? null
            : new LineGeometry(new Point(x1, y1), new Point(x2, y2));
    }

    static Geometry? PolyGeometry(XElement el, bool close)
    {
        var numbers = Regex.Matches((string?)el.Attribute("points") ?? "", @"-?[0-9.]+(?:e[-+]?[0-9]+)?",
                RegexOptions.IgnoreCase)
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToList();
        if (numbers.Count < 4) return null;
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            c.BeginFigure(new Point(numbers[0], numbers[1]), true, close);
            for (var i = 2; i + 1 < numbers.Count; i += 2)
                c.LineTo(new Point(numbers[i], numbers[i + 1]), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    static Geometry? PathGeometry(XElement el)
    {
        var d = (string?)el.Attribute("d");
        if (string.IsNullOrWhiteSpace(d)) return null;
        try { return Geometry.Parse(d); }
        catch { return null; }
    }

    static double SvgNum(XElement el, string name, double fallback = 0) =>
        double.TryParse((string?)el.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value) ? value : fallback;

    static Color? SvgColor(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && t.EndsWith(')'))
        {
            var nums = t[4..^1].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (nums.Length >= 3 &&
                byte.TryParse(nums[0], out var r) && byte.TryParse(nums[1], out var g) &&
                byte.TryParse(nums[2], out var b))
                return Color.FromRgb(r, g, b);
            return null;
        }
        if (t.StartsWith('#') && (t.Length == 4 || t.Length == 7 || t.Length == 9))
        {
            if (t.Length == 4)
                t = $"#{t[1]}{t[1]}{t[2]}{t[2]}{t[3]}{t[3]}";
            if (t.Length == 9) t = t[..7];
            try { return (Color)ColorConverter.ConvertFromString(t)!; }
            catch { return null; }
        }
        try { return (Color)ColorConverter.ConvertFromString(t)!; }
        catch { return null; }
    }

    static string? SvgStyle(string? style, string key)
    {
        if (string.IsNullOrWhiteSpace(style)) return null;
        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && pair[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                return pair[1];
        }
        return null;
    }

    static Transform? SvgTransform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var matches = Regex.Matches(text, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)",
            RegexOptions.IgnoreCase);
        var transforms = new List<Transform>();
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value.ToLowerInvariant();
            var args = match.Groups[2].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0)
                .ToArray();
            Transform? t = name switch
            {
                "translate" => args.Length >= 1
                    ? new TranslateTransform(args[0], args.Length > 1 ? args[1] : 0)
                    : null,
                "scale" => args.Length >= 1
                    ? new ScaleTransform(args[0], args.Length > 1 ? args[1] : args[0])
                    : null,
                "rotate" => args.Length >= 3
                    ? new RotateTransform(args[0], args[1], args[2])
                    : args.Length >= 1 ? new RotateTransform(args[0]) : null,
                "matrix" => args.Length >= 6
                    ? new MatrixTransform(args[0], args[1], args[2], args[3], args[4], args[5])
                    : null,
                "skewx" => args.Length >= 1 ? new SkewTransform(args[0], 0) : null,
                "skewy" => args.Length >= 1 ? new SkewTransform(0, args[0]) : null,
                _ => null,
            };
            if (t is not null) transforms.Add(t);
        }
        if (transforms.Count == 0) return null;
        // SVG 的 transform 列表是“从右往左”作用到点上；WPF TransformGroup 是顺序作用。
        transforms.Reverse();
        var group = new TransformGroup();
        foreach (var t in transforms) group.Children.Add(t);
        return group;
    }
}
