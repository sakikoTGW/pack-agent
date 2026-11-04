using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>PCL 式 PAD 自身更新（单文件 exe）：latest.json → 下载 → 校验 → 下次启动替换自己。</summary>
public sealed class PadUpdateInfo
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.pad-update/v1";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
}

public sealed class PadUpdatePending
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = "pack-agent.pad-update.pending/v1";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
}

public static class PadUpdate
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static string Feed(Launcher launcher)
    {
        var env = Environment.GetEnvironmentVariable("PAD_UPDATE_FEED");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        return launcher.Settings().Other.UpdateFeed.Trim();
    }

    public static bool Enabled(Launcher launcher) =>
        launcher.Settings().Other.UpdateChannel != "off" && Feed(launcher).Length > 0;

    static string UpdatesDir(Launcher launcher) => Path.Combine(launcher.Root, "library", "updates");
    public static string PendingFile(Launcher launcher) => Path.Combine(UpdatesDir(launcher), "pending.json");

    public static PadUpdatePending? Pending(Launcher launcher)
    {
        try
        {
            var file = PendingFile(launcher);
            if (!File.Exists(file)) return null;
            var pending = JsonSerializer.Deserialize<PadUpdatePending>(File.ReadAllText(file));
            if (pending is null || pending.Version.Length == 0 || !File.Exists(pending.Path)) return null;
            return pending;
        }
        catch { return null; }
    }

    /// <summary>读 feed（http/https 或本地文件路径），按版本判断有没有新版。</summary>
    public static async Task<PadUpdateInfo?> Check(Launcher launcher, CancellationToken ct)
    {
        var feed = Feed(launcher);
        if (!Enabled(launcher) || feed.Length == 0) return null;
        try
        {
            string json;
            if (feed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                json = await Http.GetStringAsync(feed, ct);
            else
                json = await File.ReadAllTextAsync(Path.GetFullPath(feed), ct);
            var info = JsonSerializer.Deserialize<PadUpdateInfo>(json);
            return info is null || info.Version.Length == 0 || info.Url.Length == 0 ? null : info;
        }
        catch
        {
            return null;
        }
    }

    public static string CurrentVersion() =>
        typeof(PadUpdate).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    public static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out var l) && Version.TryParse(current, out var c) && l > c;

    /// <summary>下载并校验，写 pending 标记；下次启动替换。</summary>
    public static async Task<string> Download(Launcher launcher, PadUpdateInfo info, IProgress<string>? log,
        CancellationToken ct)
    {
        var dir = Path.Combine(UpdatesDir(launcher), info.Version);
        Directory.CreateDirectory(dir);
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "pack-agent-for DSH.exe");
        var dest = Path.Combine(dir, exeName);
        var tmp = dest + ".part";
        log?.Report($"下载 {info.Version} …");
        try
        {
            if (info.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || info.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                await using var src = await Http.GetStreamAsync(info.Url, ct);
                await using var dst = File.Create(tmp);
                await src.CopyToAsync(dst, ct);
            }
            else
            {
                File.Copy(Path.GetFullPath(info.Url), tmp, overwrite: true);
            }
            if (info.Size > 0 && new FileInfo(tmp).Length != info.Size)
                throw new PadError("PA120", "update size mismatch", info.Url,
                    $"expected {info.Size}, got {new FileInfo(tmp).Length}", ["重新发布 feed"]);
            if (info.Sha256.Length > 0)
            {
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tmp)));
                if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new PadError("PA120", "update sha256 mismatch", info.Url, hash, ["重新发布 feed"]);
            }
            File.Move(tmp, dest, overwrite: true);
            var pending = new PadUpdatePending
            {
                Version = info.Version,
                Path = dest,
                Sha256 = info.Sha256,
            };
            File.WriteAllText(PendingFile(launcher),
                JsonSerializer.Serialize(pending, new JsonSerializerOptions { WriteIndented = true }));
            log?.Report($"已下载 {info.Version}，重启 PAD 生效");
            return dest;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>启动时调用：有 pending 就把新 exe 放好，并用一个 cmd 等本进程退出后替换、重启。</summary>
    public static bool TryApplyPending(Launcher launcher, out string helper, bool dryRun = false)
    {
        helper = "";
        var pending = Pending(launcher);
        var host = Environment.ProcessPath;
        if (pending is null || host is null || !File.Exists(host)) return false;

        var hostPath = Path.GetFullPath(host);
        var staged = hostPath + ".new";
        try
        {
            File.Copy(pending.Path, staged, overwrite: true);
        }
        catch
        {
            return false;
        }

        helper = Path.Combine(Path.GetTempPath(), $"pad-update-{Guid.NewGuid():N}.cmd");
        // 测试环境可以只替换、不自动重启（PAD_UPDATE_NO_RESTART=1）。
        var noRestart = Environment.GetEnvironmentVariable("PAD_UPDATE_NO_RESTART") == "1";
        var lines = new List<string>
        {
            "@echo off",
            "chcp 65001 >nul",
            "ping -n 3 127.0.0.1 >nul",
            $"move /Y \"{staged}\" \"{hostPath}\" >nul",
            "del \"%~f0\" >nul",
        };
        if (!noRestart) lines.Add($"start \"\" \"{hostPath}\"");
        lines.Add("");
        var script = string.Join("\r\n", lines);
        try
        {
            File.WriteAllText(helper, script);
            if (!dryRun)
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c start \"\" \"{helper}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                try { File.Delete(PendingFile(launcher)); } catch { /* ignore */ }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
