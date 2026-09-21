using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var cache = CacheDir(launcherRoot, spec);
        foreach (var name in new[] { "icon.png", "logo.svg", "logo.png" })
        {
            var p = Path.Combine(cache, name);
            if (File.Exists(p)) return p;
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
            var paths = new List<(Color Fill, string D)>();
            Color inherited = Colors.Black;
            var gFill = Regex.Match(svg, "<g\\b[^>]*fill=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            if (gFill.Success)
            {
                try { inherited = (Color)ColorConverter.ConvertFromString(gFill.Groups[1].Value)!; }
                catch { /* keep black */ }
            }
            foreach (Match tag in Regex.Matches(svg, "<path\\b[^>]*/?>", RegexOptions.IgnoreCase))
            {
                var fill = Regex.Match(tag.Value, "fill=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                var d = Regex.Match(tag.Value, "\\bd=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                if (!d.Success) continue;
                Color c = inherited;
                if (fill.Success)
                {
                    if (fill.Groups[1].Value.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                    try { c = (Color)ColorConverter.ConvertFromString(fill.Groups[1].Value)!; }
                    catch { continue; }
                }
                paths.Add((c, d.Groups[1].Value));
            }
            if (paths.Count == 0) return false;

            double vw = 32, vh = 32;
            var vb = Regex.Match(svg,
                "viewBox=[\"']([0-9.]+)\\s+[0-9.]+\\s+([0-9.]+)\\s+([0-9.]+)[\"']");
            if (vb.Success)
            {
                vw = double.Parse(vb.Groups[2].Value, CultureInfo.InvariantCulture);
                vh = double.Parse(vb.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            var side = Math.Max(1, Math.Min(vw, vh));

            var inner = new DrawingGroup();
            var tr = Regex.Match(svg,
                "transform=[\"']translate\\(([0-9.]+)\\s+([0-9.]+)\\)[\"']");
            if (tr.Success)
            {
                inner.Transform = new TranslateTransform(
                    double.Parse(tr.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(tr.Groups[2].Value, CultureInfo.InvariantCulture));
            }
            foreach (var (fill, d) in paths)
            {
                try
                {
                    inner.Children.Add(new GeometryDrawing(new SolidColorBrush(fill), null,
                        Geometry.Parse(d)));
                }
                catch { /* skip a bad path */ }
            }
            var outer = new DrawingGroup
            {
                ClipGeometry = new RectangleGeometry(new Rect(0, 0, side, side)),
            };
            outer.Children.Add(inner);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / side, size / side));
                dc.DrawDrawing(outer);
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
}
