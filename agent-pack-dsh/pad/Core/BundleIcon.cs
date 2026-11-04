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
            if (!File.Exists(p)) continue;
            // 官方插件图标是 SVG：光栅到 library 缓存再返回，别把 png 写进包目录。
            return p.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? RasterizeToCache(launcherRoot, spec, p) ?? p
                : p;
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

    static string? RasterizeToCache(string launcherRoot, string spec, string svgPath)
    {
        try
        {
            var cache = CacheDir(launcherRoot, spec);
            Directory.CreateDirectory(cache);
            var png = Path.Combine(cache, "icon.png");
            if (File.Exists(png) &&
                File.GetLastWriteTimeUtc(png) >= File.GetLastWriteTimeUtc(svgPath))
                return png;
            if (!TryRasterizeSvg(File.ReadAllText(svgPath), png, 128)) return null;
            Notify();
            return png;
        }
        catch { return null; }
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

        // 本地包没带 icon：先从 npm 官方 package icon 取一次，再退回仓库 logo。
        if (await NpmPackageIcon.EnsureAsync(launcherRoot, LaunchPolicy.BundleBareName(spec),
                ver ?? "", ct) is not null)
        {
            Notify();
            return;
        }
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

    /// <summary>WPF cannot bind an SVG file. Draw the path fills onto a PNG. STA only.
    /// 光栅化失败时把原因留在 <see cref="LastRasterizeError"/>，便于回归测试和排障。</summary>
    public static string LastRasterizeError { get; private set; } = "";

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

            var gradients = SvgGradients(root);
            var drawing = new DrawingGroup
            {
                ClipGeometry = new RectangleGeometry(new Rect(minX, minY, vw, vh)),
            };
            drawing.Children.Add(WalkSvg(root, Colors.Black, gradients));

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
            // 必须先关流再读长度：FileInfo.Length 在缓冲未 flush 时是 0，会把成功当失败。
            using (var fs = File.Create(pngPath)) enc.Save(fs);
            LastRasterizeError = "";
            return new FileInfo(pngPath).Length > 0;
        }
        catch (Exception ex)
        {
            LastRasterizeError = ex.ToString();
            return false;
        }
    }

    /// <summary>
    /// SVG → WPF 绘制树。官方插件图标（<c>dsh-experimental-*</c>）的常见写法必须过：
    /// 根节点 <c>fill="none"</c>、<c>&lt;g transform&gt;</c>、<c>fill="url(#grad)"</c> 渐变、
    /// 以及 <c>stroke</c> 描边。早期实现把根 <c>fill="none"</c> 当“整棵子树不画”，
    /// 于是所有官方图标都被光栅成空白 png。
    /// </summary>
    static DrawingGroup WalkSvg(XElement element, Color inherited,
        IReadOnlyDictionary<string, Brush> gradients)
    {
        var group = new DrawingGroup();
        if (SvgTransform((string?)element.Attribute("transform")) is { } transform)
            group.Transform = transform;

        var fillPaint = (string?)element.Attribute("fill")
                        ?? SvgStyle((string?)element.Attribute("style"), "fill");
        // fill="none" 只表示“本层不填”，子节点可各自覆盖；不能据此跳过子树。
        var inheritedFill = fillPaint is null ? inherited : SvgPaintColor(fillPaint, inherited);

        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            if (name is "g" or "svg")
            {
                group.Children.Add(WalkSvg(child, inheritedFill, gradients));
                continue;
            }
            if (name is "defs" or "linearGradient" or "radialGradient" or "stop"
                or "clipPath" or "mask" or "filter" or "title" or "desc" or "style")
                continue;

            var geometry = SvgGeometry(child);
            if (geometry is null) continue;

            var shapeFillText = (string?)child.Attribute("fill")
                                ?? SvgStyle((string?)child.Attribute("style"), "fill");
            var fill = shapeFillText is null
                ? BrushOf(inheritedFill)
                : SvgBrush(shapeFillText, gradients, inheritedFill, child, "fill-opacity");

            var strokeText = (string?)child.Attribute("stroke")
                             ?? SvgStyle((string?)child.Attribute("style"), "stroke");
            var strokeBrush = SvgBrush(strokeText, gradients, inheritedFill, child, "stroke-opacity");
            Pen? pen = null;
            if (strokeBrush is not null)
            {
                pen = new Pen(strokeBrush, SvgNum(child, "stroke-width", 1))
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                pen.Freeze();
            }
            if (fill is null && pen is null) continue;

            var shape = new DrawingGroup();
            if (SvgTransform((string?)child.Attribute("transform")) is { } st) shape.Transform = st;
            shape.Children.Add(new GeometryDrawing(fill, pen, geometry));
            group.Children.Add(shape);
        }
        return group;
    }

    static Brush? BrushOf(Color color) =>
        color == Colors.Transparent ? null : new SolidColorBrush(color);

    /// <summary>SVG paint 文本 → 画刷：none/null、纯色、url(#id) 渐变。</summary>
    static Brush? SvgBrush(string? paint, IReadOnlyDictionary<string, Brush> gradients,
        Color inherited, XElement el, string opacityKey)
    {
        if (string.IsNullOrWhiteSpace(paint)) return BrushOf(inherited);
        var t = paint.Trim();
        if (t.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

        var opacity = SvgNum(el, opacityKey, 1);
        opacity = Math.Clamp(opacity, 0, 1);

        if (t.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var id = t[(t.IndexOf('(') + 1)..t.LastIndexOf(')')].Trim().TrimStart('#');
            if (gradients.TryGetValue(id, out var gradient))
            {
                if (opacity >= 0.999) return gradient;
                var clone = (Brush)gradient.Clone();
                clone.Opacity = opacity;
                clone.Freeze();
                return clone;
            }
            return BrushOf(inherited);
        }

        var color = SvgColor(t);
        if (color is not Color baseColor) return BrushOf(inherited);
        if (opacity < 0.999)
            return new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255),
                baseColor.R, baseColor.G, baseColor.B));
        return new SolidColorBrush(baseColor);
    }

    static Color SvgPaintColor(string paint, Color inherited)
    {
        var t = paint.Trim();
        if (t.Equals("none", StringComparison.OrdinalIgnoreCase)) return Colors.Transparent;
        return SvgColor(t) ?? inherited;
    }

    /// <summary>把 &lt;defs&gt; 里的 linear/radialGradient 收集成可复用画刷。</summary>
    static IReadOnlyDictionary<string, Brush> SvgGradients(XElement root)
    {
        var map = new Dictionary<string, Brush>(StringComparer.Ordinal);
        foreach (var el in root.Descendants())
        {
            var kind = el.Name.LocalName;
            if (kind is not ("linearGradient" or "radialGradient")) continue;
            var id = (string?)el.Attribute("id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var stops = new GradientStopCollection();
            foreach (var stop in el.Elements().Where(e => e.Name.LocalName == "stop"))
            {
                var color = SvgColor((string?)stop.Attribute("stop-color") ?? "#000000") ?? Colors.Black;
                var alpha = Math.Clamp(SvgNum(stop, "stop-opacity", 1), 0, 1);
                stops.Add(new GradientStop(
                    Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B),
                    ParseOffset((string?)stop.Attribute("offset"))));
            }
            if (stops.Count == 0) continue;

            var absolute = ((string?)el.Attribute("gradientUnits") ?? "objectBoundingBox")
                .Equals("userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            Brush brush;
            if (kind == "linearGradient")
            {
                var b = new LinearGradientBrush(stops);
                if (absolute) b.MappingMode = BrushMappingMode.Absolute;
                b.StartPoint = new Point(SvgNum(el, "x1"), SvgNum(el, "y1"));
                b.EndPoint = new Point(SvgNum(el, "x2", 1), SvgNum(el, "y2"));
                brush = b;
            }
            else
            {
                var cx = SvgNum(el, "cx", 0.5);
                var cy = SvgNum(el, "cy", 0.5);
                var r = SvgNum(el, "r", 0.5);
                var b = new RadialGradientBrush(stops);
                if (absolute)
                {
                    b.MappingMode = BrushMappingMode.Absolute;
                    b.Center = new Point(cx, cy);
                    b.GradientOrigin = new Point(cx, cy);
                    b.RadiusX = b.RadiusY = r;
                }
                else
                {
                    b.Center = new Point(cx, cy);
                    b.GradientOrigin = new Point(cx, cy);
                    b.RadiusX = b.RadiusY = r;
                }
                brush = b;
            }
            brush.Freeze();
            map[id] = brush;
        }
        return map;
    }

    static double ParseOffset(string? text)
    {
        var t = (text ?? "0").Trim();
        if (t.EndsWith('%') &&
            double.TryParse(t[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            return Math.Clamp(pct / 100, 0, 1);
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, 0, 1)
            : 0;
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
