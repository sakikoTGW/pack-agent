using System.IO;

namespace Pad.Core;

/// <summary>
/// Finds the image a project already ships: a brand mark for the sidebar chip, a UI shot
/// for the card cover. Looked up by common names in common places — never by scanning the
/// whole tree, because this runs on every repaint.
/// </summary>
public static class Logos
{
    static readonly string[] MarkNames = ["logo", "icon", "avatar", "brand", "mark"];
    static readonly string[] UiNames = ["ui", "screenshot", "preview", "cover", "banner", "hero"];
    static readonly string[] Exts = [".png", ".jpg", ".jpeg", ".webp", ".ico"];

    /// <summary>A brand mark: logo.png / icon.png / avatar.png in the project root or .pad.</summary>
    public static string? Mark(string? root) => Find(root, MarkNames);

    /// <summary>A UI shot: ui.png / screenshot.png / preview.png — used as the card cover.</summary>
    public static string? Ui(string? root) => Find(root, UiNames);

    /// <summary>
    /// A plugin's own mark. Carriers are plugins here (there can be several TUIs), so the
    /// package itself is the authority: its manifest field first, then its files.
    /// </summary>
    public static string? Plugin(string? packageDir)
    {
        if (string.IsNullOrWhiteSpace(packageDir)) return null;
        var dir = packageDir!.TrimEnd('\\', '/');
        if (!Directory.Exists(dir)) return null;

        // package.json may point at the file directly.
        try
        {
            var manifest = Path.Combine(dir, "package.json");
            if (File.Exists(manifest))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;
                foreach (var key in new[] { "icon", "logo", "brand" })
                {
                    if (root.TryGetProperty(key, out var direct) && direct.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var rel = direct.GetString();
                        if (!string.IsNullOrWhiteSpace(rel))
                        {
                            var full = Path.Combine(dir, rel!);
                            if (File.Exists(full)) return full;
                        }
                    }
                }
            }
        }
        catch { /* bad manifest: fall through to files */ }

        return Find(dir, MarkNames);
    }

    static string? Find(string? root, string[] names)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        var dir = root!.TrimEnd('\\', '/');
        if (!Directory.Exists(dir)) return null;

        foreach (var sub in new[] { "", ".pad", "assets", "public", "static", "docs", "images" })
        {
            var at = sub.Length == 0 ? dir : Path.Combine(dir, sub);
            if (!Directory.Exists(at)) continue;

            foreach (var name in names)
            foreach (var ext in Exts)
            {
                var file = Path.Combine(at, name + ext);
                if (File.Exists(file)) return file;

                // logo-light.png / icon@2x.png both count.
                try
                {
                    var hit = Directory.EnumerateFiles(at, name + "*" + ext, SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();
                    if (hit is not null) return hit;
                }
                catch { /* unreadable dir */ }
            }
        }
        return null;
    }
}
