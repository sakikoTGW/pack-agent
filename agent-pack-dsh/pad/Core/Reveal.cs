using System.Diagnostics;
using System.IO;

namespace Pad.Core;

/// <summary>Open a folder, a file, or a URL with the shell. PAD never hosts those surfaces.</summary>
public static class Reveal
{
    public static void Path(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            { UseShellExecute = true });
            return;
        }
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public static void Url(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
