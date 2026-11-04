using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pad.Core;

/// <summary>
/// Whether this instance/profile can take hot changes, from disk facts only.
/// No runtime probe: the launch ledger says who started it, profile files say
/// whether the official HMR entry is enabled, and the installed packages say
/// whether another manager owns the plugin tree.
/// </summary>
public sealed class HotCapability
{
    public const string SchemaName = "pad.hot-capability/v1";

    [JsonPropertyName("schema")] public string Schema { get; init; } = SchemaName;
    [JsonPropertyName("instance")] public string Instance { get; init; } = "";
    [JsonPropertyName("profile")] public string Profile { get; init; } = "";

    /// <summary><c>pad</c> = PAD started this run, <c>external</c> = someone else did, <c>none</c> = not running.</summary>
    [JsonPropertyName("launchSource")] public string LaunchSource { get; init; } = "none";

    /// <summary><c>on</c> / <c>config-only</c> / <c>off</c> / <c>absent</c>.</summary>
    [JsonPropertyName("hmr")] public string Hmr { get; init; } = "absent";

    /// <summary><c>none</c> / <c>mygo</c> / <c>other</c>.</summary>
    [JsonPropertyName("manager")] public string Manager { get; init; } = "none";

    [JsonPropertyName("hotAllowed")] public bool HotAllowed { get; init; }
    [JsonPropertyName("reason")] public string Reason { get; init; } = "";
    [JsonPropertyName("checkedAt")] public string CheckedAt { get; init; } = "";

    /// <summary>Pure decision table; kept separate so tests can pin every branch.
    /// Reports every blocker, not just the first, so a user sees the whole picture.
    /// <c>config-only</c> still counts as hot: the official HMR reconciles the profile
    /// manifest/patch layers even with module watching off (<c>root: []</c>); only
    /// source-code edits need the module roots.</summary>
    public static (bool Allowed, string Reason) Classify(string launchSource, string hmr, string manager)
    {
        var reasons = new List<string>();
        if (manager != "none") reasons.Add($"manager={manager}");
        if (hmr is "off" or "absent") reasons.Add($"hmr={hmr}");
        if (launchSource != "pad") reasons.Add($"launchSource={launchSource}");
        if (reasons.Count > 0) return (false, string.Join("; ", reasons));
        return hmr switch
        {
            "on" => (true, "hot"),
            "config-only" => (true, "hot-config"),
            _ => (false, $"hmr={hmr}"),
        };
    }
}

/// <summary>
/// Reads the facts behind <see cref="HotCapability"/> and caches them next to the
/// instance. A cache miss is cheap (a handful of file stats); the TTL only keeps
/// repeated CLI calls from re-walking node_modules.
/// </summary>
public static class HotCapabilityProbe
{
    public const string FileName = "hot-capability.json";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public static HotCapability Detect(Launcher launcher, Instance inst, string? profile = null, bool refresh = false)
    {
        profile = ResolveProfile(launcher, inst, profile);
        var path = Path.Combine(launcher.InstanceDir(inst.Id), FileName);
        if (!refresh && ReadCache(path, inst.Id, profile) is { } cached) return cached;

        var cap = Compute(launcher, inst, profile);
        TryWrite(path, cap);
        return cap;
    }

    public static HotCapability Compute(Launcher launcher, Instance inst, string profile)
    {
        var launchSource = LaunchSource(launcher, inst, profile);
        var manager = ManagerOf(launcher, inst, profile);
        var hmr = HmrState(launcher, inst, profile);
        var (allowed, reason) = HotCapability.Classify(launchSource, hmr, manager);
        return new HotCapability
        {
            Instance = inst.Id,
            Profile = profile,
            LaunchSource = launchSource,
            Hmr = hmr,
            Manager = manager,
            HotAllowed = allowed,
            Reason = reason,
            CheckedAt = DateTimeOffset.UtcNow.ToString("o"),
        };
    }

    public static string ResolveProfile(Launcher launcher, Instance inst, string? profile) =>
        !string.IsNullOrWhiteSpace(profile) ? profile!
        : inst.LastProfile ?? launcher.Profiles(inst).FirstOrDefault()?.Name ?? "";

    static string LaunchSource(Launcher launcher, Instance inst, string profile)
    {
        var entry = launcher.Runtime().Entries.FirstOrDefault(e => e.Instance == inst.Id && e.Profile == profile);
        if (entry is not null && Proc.Alive(entry.Pid)) return "pad";
        // The ledger only ever records PAD-started runs. A live harness outside the
        // ledger was started by the desktop shell or by hand.
        return launcher.HarnessAlive(inst.Id, profile) ? "external" : "none";
    }

    static string HmrState(Launcher launcher, Instance inst, string profile)
    {
        var profileDir = Path.Combine(inst.Home, "profiles", profile);
        var patch = ReadText(Path.Combine(profileDir, "cordis.patch.yml"));
        if (PatchHasHmrDisabled(patch)) return "off";
        if (!HmrInstalled(launcher, inst, profileDir)) return "absent";
        return PatchHasHmrRoot(patch) ? "on" : "config-only";
    }

    static string ManagerOf(Launcher launcher, Instance inst, string profile)
    {
        var profileDir = Path.Combine(inst.Home, "profiles", profile);
        var specs = launcher.Profiles(inst).FirstOrDefault(p => p.Name == profile)?.Bundles ?? [];
        if (specs.Any(s => s.Contains("mygo", StringComparison.OrdinalIgnoreCase))
            || Directory.Exists(Path.Combine(profileDir, "node_modules", "dsh-mygo"))
            || Directory.Exists(Path.Combine(profileDir, "node_modules", "@deepseek-ai", "dsh-mygo"))
            || File.Exists(Path.Combine(profileDir, "dsh.lock")))
            return "mygo";

        var other = new[] { "dsh-plugin-manager", "dsh-manager", "dsh-desktop-hub" };
        if (specs.Any(s => other.Any(o => s.Contains(o, StringComparison.OrdinalIgnoreCase)))) return "other";
        foreach (var name in other)
            if (Directory.Exists(Path.Combine(profileDir, "node_modules", name))) return "other";
        return "none";
    }

    static bool HmrInstalled(Launcher launcher, Instance inst, string profileDir)
    {
        var candidates = new[]
        {
            Path.Combine(profileDir, "node_modules", "@deepseek-ai", "dsh-hmr"),
            Path.Combine(inst.Home, "profiles", "node_modules", "@deepseek-ai", "dsh-hmr"),
        };
        foreach (var dir in candidates)
            if (File.Exists(Path.Combine(dir, "package.json"))) return true;

        if (!string.IsNullOrWhiteSpace(inst.Dsh.Version))
        {
            var store = Path.Combine(launcher.VersionsDir, inst.Dsh.Version, "node_modules", "@deepseek-ai", "dsh-hmr");
            if (File.Exists(Path.Combine(store, "package.json"))) return true;
        }
        return false;
    }

    static bool PatchHasHmrDisabled(string yaml) => BlockHas(yaml, "id: hmr", "disabled: true");

    static bool PatchHasHmrRoot(string yaml) => BlockHas(yaml, "id: hmr", "root:") && !BlockHas(yaml, "id: hmr", "root: []");

    /// <summary>True when the block that starts with <paramref name="marker"/> mentions
    /// <paramref name="needle"/> before the next top-level list item.</summary>
    static bool BlockHas(string yaml, string marker, string needle)
    {
        if (string.IsNullOrWhiteSpace(yaml)) return false;
        var lines = yaml.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("- ", StringComparison.Ordinal) && line != "-") continue;
            var item = line[1..].Trim();
            if (!item.StartsWith(marker, StringComparison.Ordinal)) continue;
            for (var j = i; j < lines.Length && j <= i + 8; j++)
            {
                var next = lines[j].Trim();
                if (j > i && (next.StartsWith("- ", StringComparison.Ordinal) || next == "-")) break;
                if (next.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    static string ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch { return ""; }
    }

    static HotCapability? ReadCache(string path, string instance, string profile)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > CacheTtl) return null;
            var cap = JsonSerializer.Deserialize<HotCapability>(File.ReadAllText(path));
            if (cap is null || cap.Schema != HotCapability.SchemaName) return null;
            return cap.Instance == instance && cap.Profile == profile ? cap : null;
        }
        catch { return null; }
    }

    static void TryWrite(string path, HotCapability cap)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cap, Pretty));
            File.Move(tmp, path, overwrite: true);
        }
        catch { /* cache is an optimization */ }
    }

    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
