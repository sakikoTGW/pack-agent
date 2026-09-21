using System.IO;

namespace Pad.Core;

/// <summary>
/// API keys live in the launcher library and are copied into an instance home
/// at save and at launch. DSH still reads <c>$DSH_HOME/.credentials.yaml</c>;
/// that file is a projection, not the place the user manages keys.
/// Never returns the secret.
/// </summary>
public static class CredentialsGate
{
    public sealed record Probe(bool Configured, string Via, string Location);

    public sealed record KeyMatch(string Name, string Status, string Instances);

    /// <summary>Imported refs. Never the secret. Catalog rows may have no key yet.</summary>
    public sealed record ImportedApi(
        string RefName,
        string Company,
        string Status,
        string Instances,
        bool RefMissing,
        string BaseUrl,
        string Icon,
        string Sub);

    public static string YamlPath(Launcher launcher, Instance inst, PadSettings settings)
    {
        var set = Blank(inst.Launch?.CredentialsSet);
        if (set is null)
        {
            var def = settings.Launch.CredentialsDefault ?? "global";
            if (def == "none") return Path.Combine(inst.Home, ".credentials.yaml");
            return launcher.NamedCredentialsPath(def);
        }
        if (set == "instance") return Path.Combine(inst.Home, ".credentials.yaml");
        return launcher.NamedCredentialsPath(set);
    }

    public static Probe Inspect(Launcher launcher, Instance? inst, PadSettings settings)
    {
        if (inst is not null)
        {
            var yaml = YamlPath(launcher, inst, settings);
            if (CredentialsFile.HasDeepseekKey(Read(yaml)))
                return new Probe(true, "yaml", yaml);

            var homeFile = Path.Combine(inst.Home, ".credentials.yaml");
            if (CredentialsFile.HasDeepseekKey(Read(homeFile)))
                return new Probe(true, "yaml", homeFile);

            return new Probe(false, "none", yaml);
        }

        var global = launcher.GlobalCredentialsPath;
        if (CredentialsFile.HasDeepseekKey(Read(global)))
            return new Probe(true, "yaml", global);
        return new Probe(false, "none", global);
    }

    public static void SetDeepseekKey(Launcher launcher, Instance? inst, PadSettings settings, string value)
    {
        var path = inst is null ? launcher.GlobalCredentialsPath : YamlPath(launcher, inst, settings);
        var next = CredentialsFile.UpsertDeepseekKey(Read(path), value);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, next, new System.Text.UTF8Encoding(false));
        DistributeToHomes(launcher, settings, path);
    }

    /// <summary>
    /// Copy this instance's distributor file into its home so DSH can read it.
    /// Adopted homes and <c>credentialsSet=instance</c> are left alone.
    /// </summary>
    public static void CopyDistributorToHome(Launcher launcher, Instance inst, PadSettings settings)
    {
        if (inst.Adopted) return;
        var src = YamlPath(launcher, inst, settings);
        var dst = Path.Combine(inst.Home, ".credentials.yaml");
        if (LaunchPolicy.SamePath(src, dst)) return;
        if (!File.Exists(src)) return;
        if (!CredentialsFile.HasDeepseekKey(Read(src))) return;
        Directory.CreateDirectory(inst.Home);
        File.Copy(src, dst, overwrite: true);
    }

    /// <summary>
    /// Push one library file into every non-adopted instance that uses it.
    /// </summary>
    public static void DistributeToHomes(Launcher launcher, PadSettings settings, string? justWrotePath)
    {
        if (string.IsNullOrWhiteSpace(justWrotePath)) return;
        foreach (var inst in launcher.Instances())
        {
            var src = YamlPath(launcher, inst, settings);
            if (!LaunchPolicy.SamePath(src, justWrotePath)) continue;
            CopyDistributorToHome(launcher, inst, settings);
        }
    }

    /// <summary>Which API Key files exist and which instances use them. Never the secret.</summary>
    public static List<KeyMatch> ListMatches(Launcher launcher, PadSettings settings)
    {
        var rows = new List<KeyMatch>();
        foreach (var set in launcher.ListCredentials())
        {
            var yaml = File.Exists(set.Path) ? File.ReadAllText(set.Path) : null;
            var has = CredentialsFile.HasDeepseekKey(yaml);
            var names = launcher.Instances()
                .Where(i => !i.Adopted && LaunchPolicy.SamePath(YamlPath(launcher, i, settings), set.Path))
                .Select(i => i.Name)
                .ToList();
            rows.Add(new KeyMatch(
                set.Name,
                has ? "已配置" : "未填",
                names.Count == 0 ? "无匹配实例" : string.Join("、", names)));
        }
        return rows;
    }

    /// <summary>
    /// Catalog channels plus any extra refs. Empty channels stay visible as 暂无 Key.
    /// Never the secret. Instances that currently load this ref.
    /// </summary>
    public static List<ImportedApi> ListImported(Launcher launcher, PadSettings settings)
    {
        var have = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in launcher.ListCredentials())
        {
            var yaml = File.Exists(set.Path) ? File.ReadAllText(set.Path) : null;
            foreach (var name in CredentialsFile.ListRefs(yaml))
                have.Add(name);
        }

        var instByRef = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var inst in launcher.Instances().Where(i => !i.Adopted))
        {
            var yaml = Read(YamlPath(launcher, inst, settings));
            foreach (var name in CredentialsFile.ListRefs(yaml))
            {
                have.Add(name);
                if (!instByRef.TryGetValue(name, out var list))
                {
                    list = [];
                    instByRef[name] = list;
                }
                if (!list.Contains(inst.Name)) list.Add(inst.Name);
            }
        }

        var catalogRefs = new HashSet<string>(
            CredentialsFile.Catalog.Select(c => c.Ref), StringComparer.Ordinal);
        var names = CredentialsFile.Catalog.Select(c => c.Ref)
            .Concat(have.Where(n => !catalogRefs.Contains(n)).OrderBy(n => n, StringComparer.Ordinal));

        return names.Select(name =>
        {
            instByRef.TryGetValue(name, out var list);
            var missing = !have.Contains(name);
            var inst = list is { Count: > 0 } ? string.Join("、", list) : "无匹配实例";
            var spec = CredentialsFile.SpecOf(name);
            var company = CredentialsFile.CompanyOf(name);
            var status = missing ? "暂无 Key" : "已配置";
            return new ImportedApi(
                name,
                company,
                status,
                inst,
                missing,
                spec?.BaseUrl ?? "",
                CredentialsFile.IconPack(spec?.IconFile),
                name + " · " + status);
        }).ToList();
    }

    public static List<ImportedApi> ListImported(Launcher launcher) =>
        ListImported(launcher, launcher.Settings());

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
