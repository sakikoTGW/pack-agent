using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Pad.Core;

/// <summary>One agent preset a release/profile ships (0.2.x preset row).</summary>
public sealed record PresetInfo(string Id, int Order, string RowId);

public sealed record PresetProbe(IReadOnlyList<PresetInfo> Presets, string? DefaultId);

/// <summary>
/// Agent presets are DSH's own composition rows (<c>preset-standard/ptc/minimal/cordis</c>),
/// and the active one is <c>agent-preset-registry.config.default</c>. PAD never edits DSH code:
/// it writes a marked block into the **profile's own patch layer**
/// (<c>&lt;home&gt;/profiles/&lt;profile&gt;/cordis.patch.yml</c>), which is the file DSH itself
/// documents as the user override layer. The block is stripped and rewritten on every bind, so a
/// user's other patch entries survive untouched.
/// </summary>
public static class AgentPresets
{
    public const string ManagedBegin = "# >>> pad project preset (managed) >>>";
    public const string ManagedEnd = "# <<< pad project preset <<<";

    static readonly Regex IdSafe = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    static readonly Regex RowStart = new(@"^- id:\s*(\S+)", RegexOptions.Compiled);
    static readonly Regex RegistryDefault = new(@"^\s{4}default:\s*(\S+)", RegexOptions.Compiled);
    static readonly Regex ConfigId = new(@"^\s{4}id:\s*(\S+)", RegexOptions.Compiled);
    static readonly Regex ConfigOrder = new(@"^\s{4}order:\s*(\d+)", RegexOptions.Compiled);

    public static string PatchPath(string home, string profile) =>
        Path.Combine(home, "profiles", profile, "cordis.patch.yml");

    public static bool IsSafeId(string id) => !string.IsNullOrWhiteSpace(id) && IdSafe.IsMatch(id);

    /// <summary>Parse the preset rows out of a <c>--dump-config</c> dump. Null = the dump had no
    /// recognisable preset rows (wrong release shape), which PA125 reports instead of guessing.</summary>
    public static PresetProbe? Parse(string dump)
    {
        if (string.IsNullOrWhiteSpace(dump)) return null;
        var presets = new List<PresetInfo>();
        string? defaultId = null;
        string? rowId = null, cfgId = null;
        int? order = null;
        bool registry = false;

        void Flush()
        {
            if (rowId is not null && cfgId is not null)
                presets.Add(new PresetInfo(Trim(cfgId), order ?? 0, rowId));
            rowId = null;
            cfgId = null;
            order = null;
        }

        foreach (var raw in dump.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var start = RowStart.Match(line);
            if (start.Success)
            {
                Flush();
                var id = Trim(start.Groups[1].Value);
                registry = id == "agent-preset-registry";
                rowId = !registry && id.StartsWith("preset-", StringComparison.Ordinal) ? id : null;
                continue;
            }
            if (registry)
            {
                var m = RegistryDefault.Match(line);
                if (m.Success) defaultId = Trim(m.Groups[1].Value);
                continue;
            }
            if (rowId is null) continue;
            var mi = ConfigId.Match(line);
            if (mi.Success) cfgId = Trim(mi.Groups[1].Value);
            var mo = ConfigOrder.Match(line);
            if (mo.Success && int.TryParse(mo.Groups[1].Value, out var n)) order = n;
        }
        Flush();
        if (presets.Count == 0) return null;
        presets.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        return new PresetProbe(presets, defaultId);
    }

    /// <summary>Read the profile's shipped presets by asking the pinned release itself
    /// (<c>--dump-config</c> also materializes a not-yet-created profile). Runs outside PAD's
    /// launch path, so it is the only source of truth for preset ids.</summary>
    public static async Task<(PresetProbe? Probe, string? Error)> ProbeAsync(
        Launcher launcher, string home, string? version, string workspace, string profile,
        CancellationToken ct)
    {
        var bin = launcher.DshBin(version ?? "");
        if (!File.Exists(bin)) return (null, $"that release is not installed: {version}");
        var node = launcher.ResolveLaunchNode(
            new Instance
            {
                Id = "probe",
                Home = home,
                Dsh = new InstanceDsh { Version = version ?? "" },
                Workspace = new InstanceWorkspace { Kind = "owned", Path = workspace },
            },
            launcher.Settings()) ?? Proc.Which("node");
        if (string.IsNullOrWhiteSpace(node)) return (null, "no node runtime for that release");

        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = $"\"{bin}\" --profile {profile} --dump-config",
            WorkingDirectory = Directory.Exists(workspace) ? workspace : Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        psi.Environment["DSH_HOME"] = home;
        psi.Environment["DSH_TELEMETRY_DISABLED"] = "1";

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return (null, "could not start the release's node");
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(150));
            try { await proc.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return (null, "dump-config timed out");
            }
            var text = await stdout;
            var err = await stderr;
            if (proc.ExitCode != 0)
                return (null, $"dump-config exited {proc.ExitCode}: {Tail(err, 400)}");
            var probe = Parse(text);
            return probe is null
                ? (null, "dump-config did not contain preset rows (unsupported release shape)")
                : (probe, null);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (null, "could not run dump-config: " + ex.Message);
        }
    }

    /// <summary>Write the project's preset as the profile's <c>agent-preset-registry</c> default,
    /// keeping every other line of the user's patch layer intact.</summary>
    public static void Apply(string patchPath, string presetId)
    {
        if (!IsSafeId(presetId))
            throw new PadError("PA125", "could not bind agent presets for this project", patchPath,
                $"preset id '{presetId}' is not a lowercase slug", ["preset id 只用 a-z 0-9 -"]);
        Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
        var text = File.Exists(patchPath) ? File.ReadAllText(patchPath) : "";
        Projects.WriteAtomic(patchPath, Merge(text, presetId));
    }

    /// <summary>Remove PAD's managed block, leaving the patch layer a valid empty array again.</summary>
    public static void Clear(string patchPath)
    {
        if (!File.Exists(patchPath)) return;
        var stripped = Strip(File.ReadAllText(patchPath));
        if (HasNoEntries(stripped)) stripped = EnsureArray(stripped);
        Projects.WriteAtomic(patchPath, stripped);
    }

    public static string Merge(string existing, string presetId)
    {
        var lines = Strip(existing).Replace("\r\n", "\n").Split('\n').ToList();
        // The freshly-initialized patch layer is a single `[]` YAML array; drop it.
        if (HasNoEntries(string.Join("\n", lines)))
            lines.RemoveAll(l => l.Trim() == "[]");
        var body = string.Join("\n", lines).TrimEnd();
        var sb = new StringBuilder();
        if (body.Length > 0) sb.Append(body).Append('\n');
        sb.Append(ManagedBegin).Append('\n');
        sb.Append("- id: agent-preset-registry\n");
        sb.Append("  config:\n");
        sb.Append("    default: ").Append(presetId).Append('\n');
        sb.Append(ManagedEnd).Append('\n');
        return sb.ToString();
    }

    static string Strip(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var begin = lines.FindIndex(l => l.Trim() == ManagedBegin);
        if (begin < 0) return text;
        var end = lines.FindIndex(begin, l => l.Trim() == ManagedEnd);
        if (end < begin) return text;
        lines.RemoveRange(begin, end - begin + 1);
        return string.Join("\n", lines);
    }

    static bool HasNoEntries(string patchText) =>
        patchText.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .All(l => l == "[]");

    static string EnsureArray(string text)
    {
        var body = text.TrimEnd();
        return (body.Length == 0 ? "" : body + "\n") + "[]\n";
    }

    static string Trim(string value) => value.Trim().Trim('"', '\'');

    static string Tail(string text, int max)
    {
        var t = (text ?? "").Trim();
        return t.Length <= max ? t : "…" + t[^max..];
    }
}
