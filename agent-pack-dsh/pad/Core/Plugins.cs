using System.IO;
using System.Linq;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// Reads what a plugin <b>declares</b> — never what it does.
///
/// Contract: <c>docs/user/develop/basic/publish.md</c> in
/// <c>deepseek-ai/deepseek-harness</c>. Two manifests, both under <c>package.json#dsh</c>:
/// a bundle declares <c>dsh.bundle.patch</c>, a profile declares <c>dsh.profile.bundles</c>.
///
/// Two rules from the spec shape this file:
/// <list type="bullet">
/// <item>In-box bundle names always resolve from the dsh installation itself; pnpm manages
/// only out-of-tree packages. So "not in dependencies" means in-box, not missing.</item>
/// <item>Installation and profile startup already refuse a bundle whose <c>engines.dsh</c>
/// range the running DSH does not satisfy. PAD reports the range; it does not re-check it.</item>
/// </list>
/// </summary>
public static class Plugins
{
    public sealed record Entry(
        string Name,
        string Version,
        string? ManifestVersion,
        int PatchFiles,
        bool PatchesExist,
        int ClientInject,
        string? ClientPlatform,
        string? EnginesDsh,
        string Origin,
        string Verdict,
        string Renders,
        string? Note);

    public sealed record Audit(
        string Home,
        string Profile,
        string ProfileDir,
        string Surface,
        int Total,
        int Wasted,
        IReadOnlyList<Entry> Entries);

    public static Audit Run(string home, string profile, string? surfaceOverride)
    {
        var profileDir = Path.Combine(home, "profiles", profile);
        var manifestPath = Path.Combine(profileDir, "package.json");
        if (!File.Exists(manifestPath))
            throw new PadError("PA060", "no such profile manifest", manifestPath, null,
                ["pad cli plugin audit <home> <profile>", "pad cli inspect <home>"]);

        using var doc = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = doc.RootElement;
        var bundles = BundleNames(root);
        var outOfTree = DependencyNames(root);
        var surface = surfaceOverride ?? InferSurface(bundles);

        var entries = bundles
            .Select(n => Read(n,
                Path.Combine(profileDir, "node_modules", n.Replace('/', Path.DirectorySeparatorChar)),
                surface, outOfTree.Contains(n)))
            .ToList();

        return new Audit(home, profile, profileDir, surface, entries.Count,
            entries.Count(e => e.Renders == "wasted"), entries);
    }

    /// <summary>
    /// The surface is a property of the profile, not a setting: whichever UI bundle
    /// the bundle set contains is the surface that will actually show up.
    /// </summary>
    static string InferSurface(IReadOnlyList<string> bundles)
    {
        if (bundles.Any(b => b.Contains("dsh-tui", StringComparison.OrdinalIgnoreCase))) return "tui";
        if (bundles.Any(b => b.Contains("dsh-web-app", StringComparison.OrdinalIgnoreCase))) return "web";
        return "unknown";
    }

    static IReadOnlyList<string> BundleNames(JsonElement root)
    {
        var names = new List<string>();
        if (root.TryGetProperty("dsh", out var dsh) &&
            dsh.TryGetProperty("profile", out var prof) &&
            prof.TryGetProperty("bundles", out var bundles) &&
            bundles.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in bundles.EnumerateArray())
            {
                var s = b.ValueKind == JsonValueKind.String ? b.GetString() : null;
                if (!string.IsNullOrWhiteSpace(s) && !names.Contains(s!)) names.Add(s!);
            }
        }
        return names;
    }

    static HashSet<string> DependencyNames(JsonElement root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object)
            foreach (var p in deps.EnumerateObject()) set.Add(p.Name);
        return set;
    }

    static Entry Read(string name, string dir, string surface, bool outOfTree)
    {
        if (!outOfTree)
            // Spec: in-box bundle names resolve from the dsh installation, never from
            // the profile. Not being on disk here is expected, not a fault.
            return new Entry(name, "", null, 0, true, 0, null, null, "in-box", "ok", "host", null);

        var pkg = Path.Combine(dir, "package.json");
        if (!File.Exists(pkg))
            return new Entry(name, "", null, 0, false, 0, null, null, "missing", "not-installed", "none",
                "declared as an out-of-tree bundle but absent from node_modules");

        using var doc = JsonDocument.Parse(File.ReadAllBytes(pkg));
        var root = doc.RootElement;
        var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

        var spec = root.TryGetProperty("dsh", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;

        string? manifestVersion = null;
        if (spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("manifestVersion", out var mv) && mv.TryGetInt32(out var mvn))
            manifestVersion = mvn.ToString();

        // `dsh.bundle.patch` is a path or an ordered list of paths.
        var patchFiles = 0;
        var patchesExist = false;
        string? note = null;
        if (spec.ValueKind == JsonValueKind.Object &&
            spec.TryGetProperty("bundle", out var bundle) &&
            bundle.TryGetProperty("patch", out var patch))
        {
            var paths = patch.ValueKind == JsonValueKind.Array
                ? patch.EnumerateArray().Select(p => p.GetString()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).ToArray()
                : [patch.GetString() ?? ""];

            patchFiles = paths.Length;
            patchesExist = patchFiles > 0 && paths.All(p => File.Exists(Path.Combine(dir, p.Replace('/', Path.DirectorySeparatorChar))));
            if (!patchesExist) note = "bundle.patch points at a missing file";
        }

        var clientInject = 0;
        string? clientPlatform = null;
        if (spec.ValueKind == JsonValueKind.Object &&
            spec.TryGetProperty("client", out var client) &&
            client.ValueKind == JsonValueKind.Object)
        {
            if (client.TryGetProperty("inject", out var inject) && inject.ValueKind == JsonValueKind.Array)
                clientInject = inject.GetArrayLength();
            if (client.TryGetProperty("platform", out var pl)) clientPlatform = pl.GetString();
        }

        string? enginesDsh = null;
        if (root.TryGetProperty("engines", out var engines) &&
            engines.TryGetProperty("dsh", out var ed))
            enginesDsh = ed.GetString();

        var verdict = spec.ValueKind != JsonValueKind.Object
            ? "plain-dependency"      // installs, activates no layer — spec says so explicitly
            : !patchesExist ? "bad-manifest" : "ok";

        var renders = clientInject == 0
            ? "host"
            : surface == "tui" ? "wasted" : "client";

        if (verdict == "plain-dependency")
            note ??= "no dsh.bundle — installs as a plain dependency, contributes no layer";

        return new Entry(name, version, manifestVersion, patchFiles, patchesExist, clientInject,
            clientPlatform, enginesDsh, "out-of-tree", verdict, renders, note);
    }
}
