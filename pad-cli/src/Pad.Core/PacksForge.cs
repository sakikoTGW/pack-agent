using System.IO;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// PAD-side bridge for PackForge <c>.dspack</c> imports.
///
/// The pack semantics (overrides → profile root, home/ → home root, pinned deps, bundles)
/// belong to pack-runtime's compat engine; PAD's job here is the launcher half:
/// decide the DSH release, create the isolated instance first, then hand its home to
/// <c>packagent dsh packforge install</c>. PAD never installs a pack into an existing home
/// (see modpack.md), so every path here creates a fresh instance.
/// </summary>
public static class PacksForge
{
    public sealed class InspectInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Type { get; set; } = "";
        public string? DshVersion { get; set; }
        public List<string> DshVersions { get; set; } = [];
        public string? ProfileName { get; set; }
        public string? DefaultProfile { get; set; }
    }

    public sealed class ImportResult
    {
        public string Instance { get; set; } = "";
        public string Name { get; set; } = "";
        public string Home { get; set; } = "";
        public string Release { get; set; } = "";
        public string? Profile { get; set; }
        public JsonElement? Inspect { get; set; }
        public JsonElement? Install { get; set; }
        public bool DepsInstalled { get; set; }
    }

    static JsonDocument Parse(string text, string what)
    {
        try { return JsonDocument.Parse(text); }
        catch (JsonException e)
        {
            throw new PadError("PA028", $"packforge {what} returned invalid JSON", "pack-runtime", e.Message,
                ["看上面的输出", "可能是 pack-runtime bundle 太旧：重新 stage/更新 kit"]);
        }
    }

    /// <summary>Read the .dspack header through pack-runtime (single source of truth).</summary>
    public static async Task<InspectInfo> Inspect(Packs packs, string file, CancellationToken ct)
    {
        var full = Path.GetFullPath(file);
        if (!File.Exists(full))
            throw new PadError("PA140", ".dspack was not found", full, null, ["确认路径"]);

        var text = await packs.PackforgeInspect(full, ct);
        using var doc = Parse(text, "inspect");
        var root = doc.RootElement;

        var info = new InspectInfo
        {
            Name = Str(root, "name") ?? "",
            Version = Str(root, "version") ?? "",
            Type = Str(root, "type") ?? "",
            DshVersion = Str(root, "dshVersion"),
            ProfileName = Str(root, "profileName"),
            DefaultProfile = Str(root, "defaultProfile"),
        };
        if (root.TryGetProperty("dshVersions", out var vs) && vs.ValueKind == JsonValueKind.Array)
            foreach (var v in vs.EnumerateArray())
                if (v.GetString() is { Length: > 0 } s) info.DshVersions.Add(s);

        // collection 等类型仍拒绝；profile / dshhome 都支持（dshhome = 整个 home 快照，M4 已落地）。
        if (!string.Equals(info.Type, "profile", StringComparison.Ordinal)
            && !string.Equals(info.Type, "dshhome", StringComparison.Ordinal))
            throw new PadError("PA151", "unsupported pack type", info.Type, full,
                ["当前支持 profile / dshhome 两种 .dspack"]);
        return info;
    }

    static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Default launch profile: profile 形态取 profileName，dshhome 取 defaultProfile。</summary>
    static string? ProfileFor(InspectInfo info) =>
        string.Equals(info.Type, "dshhome", StringComparison.Ordinal) ? info.DefaultProfile : info.ProfileName;

    /// <summary>Explicit name/release import (scripted path).</summary>
    public static async Task<ImportResult> Import(
        Launcher launcher, Packs packs, string file, string name,
        string? releaseOverride, string? profileOverride, bool deps,
        IProgress<string>? log, CancellationToken ct)
    {
        var info = await Inspect(packs, file, ct);
        var full = Path.GetFullPath(file);
        var release = releaseOverride ?? info.DshVersion ?? info.DshVersions.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(release))
            throw new PadError("PA145", "the pack declares no dsh version", full, null,
                ["用 --release <版本> 指定，或换一个 dshVersion 明确的包"]);
        RequireInstalled(launcher, release, full);

        var profile = string.IsNullOrWhiteSpace(profileOverride) ? ProfileFor(info) : profileOverride;
        var instance = launcher.CreateInstance(name, release, profile);
        log?.Report($"[PAD] instance {instance.Id} created → {instance.Home}");

        var installText = await packs.PackforgeInstall(full, instance.Home, profile, apply: true, deps, ct);
        using var installDoc = Parse(installText, "install");
        return new ImportResult
        {
            Instance = instance.Id,
            Name = instance.Name,
            Home = instance.Home,
            Release = release,
            Profile = profile,
            DepsInstalled = deps,
            Inspect = JsonSerializer.SerializeToElement(info),
            Install = installDoc.RootElement.Clone(),
        };
    }

    /// <summary>Drop / open-file path: manifest supplies name/profile, release is decided
    /// from what is installed. Used by the window drag-drop and the 版本页 file dialog.</summary>
    public static async Task<ImportResult> ImportAuto(
        Launcher launcher, Packs packs, string file, bool deps,
        IProgress<string>? log, CancellationToken ct)
    {
        var info = await Inspect(packs, file, ct);
        var full = Path.GetFullPath(file);
        var installed = launcher.Releases()
            .Select(r => r.Version)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string? release = null;
        if (info.DshVersion is { Length: > 0 } pinned && installed.Contains(pinned)) release = pinned;
        else release = info.DshVersions.FirstOrDefault(installed.Contains);
        if (release is null)
        {
            if (info.DshVersion is { Length: > 0 } wanted)
                throw new PadError("PA001", "that release is not installed", $"versions/{wanted}", wanted,
                    [$"pad cli release install {wanted}", "或先在下载页装好这个发行号"]);
            if (installed.Count == 0)
                throw new PadError("PA001", "no DSH release is installed", "versions", null, ["先在下载页装一个发行号"]);
            release = launcher.Releases().First().Version;
        }
        RequireInstalled(launcher, release, full);

        var profile = ProfileFor(info);
        if (string.IsNullOrWhiteSpace(profile)) profile = null;
        Instance? instance = null;
        var baseName = string.IsNullOrWhiteSpace(info.Name) ? "packforge" : info.Name;
        for (var i = 1; i <= 99 && instance is null; i++)
        {
            var candidate = i == 1 ? baseName : $"{baseName}-{i}";
            try { instance = launcher.CreateInstance(candidate, release, profile); }
            catch (PadError e) when (e.Code == "PA023") { /* name taken → next suffix */ }
        }
        instance ??= null;
        if (instance is null)
            throw new PadError("PA023", "could not find a free instance name", baseName, null, ["换个名字再导入"]);
        log?.Report($"[PAD] instance {instance.Id} created → {instance.Home}");

        var installText = await packs.PackforgeInstall(full, instance.Home, profile, apply: true, deps, ct);
        using var installDoc = Parse(installText, "install");
        return new ImportResult
        {
            Instance = instance.Id,
            Name = instance.Name,
            Home = instance.Home,
            Release = release,
            Profile = profile,
            DepsInstalled = deps,
            Inspect = JsonSerializer.SerializeToElement(info),
            Install = installDoc.RootElement.Clone(),
        };
    }

    static void RequireInstalled(Launcher launcher, string release, string file)
    {
        if (launcher.Releases().Any(r => string.Equals(r.Version, release, StringComparison.OrdinalIgnoreCase))) return;
        throw new PadError("PA001", "that release is not installed", $"versions/{release}", release,
            [$"pad cli release install {release}", "或 --release 指定一个已装版本"]);
    }
}
