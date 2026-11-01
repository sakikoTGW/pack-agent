using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pad.Core;

/// <summary>
/// <c>pad cli project …</c> — the project axis' command face. Same code the window will call:
/// list / show / add / remove / seed (write the DSH workspace registry for a project)
/// (turn an existing DSH home into candidate projects, read-only until <c>--write</c>).
/// </summary>
public static class ProjectCli
{
    static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> Run(Launcher launcher, string cmd, string[] rest)
    {
        var store = new Projects(launcher);
        switch (cmd)
        {
            case "list":
            {
                var rows = store.List().Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    workspaces = p.Workspaces.Count,
                    agents = p.Agents.Count,
                    instance = p.Instance,
                    version = p.Dsh.Version,
                    profile = p.Dsh.Profile,
                    window = p.Window.Kind,
                    updated = p.Updated,
                });
                Emit(launcher, rows, rest);
                return 0;
            }

            case "show":
            {
                var id = Positionals(rest, "--home").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(id)) return Usage("pad cli project show <id>");
                var p = store.Get(id) ?? throw NoProject(id);
                Emit(launcher, p, rest);
                return 0;
            }

            case "add":
            {
                var pos = Positionals(rest, "--workspace", "--version", "--profile", "--name");
                var name = Flag(rest, "--name") ?? pos.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(name))
                    return Usage("pad cli project add <name> --workspace <dir> [--workspace <dir>…] [--version <v>] [--profile <p>]");
                var workspaces = Multi(rest, "--workspace");
                if (workspaces.Count == 0)
                    return Usage("pad cli project add <name> --workspace <dir> [--workspace <dir>…]");
                var id = Projects.SafeId(name);
                if (store.ReadIndex(id) is not null)
                    throw new PadError("PA122", "a project with this id already exists", $"projects/{id}/project.json",
                        null, ["换个名字，或先 pad cli project remove " + id]);
                var p = new Project
                {
                    Id = id,
                    Name = name.Trim(),
                    Workspaces = workspaces,
                    Dsh = new ProjectDsh { Version = Flag(rest, "--version"), Profile = Flag(rest, "--profile") },
                    Window = new ProjectWindow { Kind = "web" },
                };
                store.Save(p);
                Emit(launcher, p, rest);
                return 0;
            }

            case "remove":
            {
                var id = Positionals(rest).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(id)) return Usage("pad cli project remove <id>");
                if (!store.Remove(id)) throw NoProject(id);
                Cli.Print($"removed {id}");
                return 0;
            }

            case "seed":
            {
                var id = Positionals(rest, "--home").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(id)) return Usage("pad cli project seed <id> [--home <dir>]");
                var p = store.Get(id) ?? throw NoProject(id);
                var seed = await ProjectRunner.SeedAsync(launcher, store, p, Flag(rest, "--home"),
                    new Progress<string>(Cli.Print), CancellationToken.None);
                Emit(launcher, new
                {
                    project = p.Id,
                    home = seed.Home,
                    registry = WorkspaceRegistry.PathFor(seed.Home),
                    kept = seed.Kept,
                    added = seed.Added,
                    preset = seed.BoundPreset,
                    workspaces = seed.Doc.Tables.Workspaces.Values.Select(r => new { r.Path, r.Title, sessions = r.SessionIds.Count }),
                }, rest);
                return 0;
            }

            case "run":
            {
                var pos = Positionals(rest, "--profile", "--out");
                var id = pos.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(id)) return Usage("pad cli project run <id> [profile] [--profile <p>]");
                var p = store.Get(id) ?? throw NoProject(id);
                var profile = Flag(rest, "--profile") ?? (pos.Count > 1 ? pos[1] : null);
                var result = await ProjectRunner.RunAsync(launcher, store, p, profile, CancellationToken.None);
                Emit(launcher, result, rest);
                return 0;
            }

            case "presets":
            {
                var id = Positionals(rest, "--home", "--profile", "--out").FirstOrDefault();
                if (string.IsNullOrWhiteSpace(id))
                    return Usage("pad cli project presets <id> [--home <home>] [--profile <p>]");
                var p = store.Get(id) ?? throw NoProject(id);
                var settings = launcher.Settings();
                var inst = launcher.Instance(p.Instance ?? p.Id);
                if (inst is null && string.IsNullOrWhiteSpace(Flag(rest, "--home")))
                    inst = ProjectRunner.EnsureInstance(launcher, store, p);
                var home = Flag(rest, "--home") ?? inst?.Home;
                if (string.IsNullOrWhiteSpace(home))
                    throw new PadError("PA125", "could not read agent presets for this project",
                        $"projects/{p.Id}/project.json", "project has no instance home yet",
                        ["先 pad cli project seed " + p.Id, "或显式给 --home"]);
                home = Projects.NormPath(home);
                var profile = Flag(rest, "--profile") ?? p.Dsh.Profile ?? inst?.LastProfile
                              ?? settings.Launch.DefaultProfile;
                var version = p.Dsh.Version ?? inst?.Dsh.Version ?? settings.Launch.DefaultRelease;
                var (probe, error) = await AgentPresets.ProbeAsync(
                    launcher, home, version, inst?.Workspace.Path ?? home, profile, CancellationToken.None);
                if (probe is null)
                    throw new PadError("PA125", "could not read agent presets for this project", home,
                        error ?? "dump-config failed",
                        ["确认这个发行号已安装、node 可用"]);
                Emit(launcher, new
                {
                    project = p.Id,
                    home,
                    profile,
                    defaultId = probe.DefaultId,
                    bound = p.Agents,
                    patch = AgentPresets.PatchPath(home, profile),
                    presets = probe.Presets.Select(x => new { id = x.Id, order = x.Order, row = x.RowId }),
                }, rest);
                return 0;
            }

            case "preset":
            {
                var pos = Positionals(rest, "--home", "--profile", "--out");
                var verb = pos.FirstOrDefault() is "add" or "remove" or "use" or "clear" ? pos[0] : "set";
                if (verb != "set") pos.RemoveAt(0);
                var id = pos.FirstOrDefault();
                const string usage =
                    "pad cli project preset <id> <preset-id|--clear> | add|remove|use <id> <preset-id> | clear <id> [--home <home>] [--profile <p>] [--force]";
                if (string.IsNullOrWhiteSpace(id)) return Usage(usage);
                var p = store.Get(id) ?? throw NoProject(id);
                var settings = launcher.Settings();
                var inst = launcher.Instance(p.Instance ?? p.Id);
                if (inst is null && string.IsNullOrWhiteSpace(Flag(rest, "--home")))
                    inst = ProjectRunner.EnsureInstance(launcher, store, p);
                var home = Flag(rest, "--home") ?? inst?.Home;
                var profile = Flag(rest, "--profile") ?? p.Dsh.Profile ?? inst?.LastProfile
                              ?? settings.Launch.DefaultProfile;
                var clear = verb == "clear" || rest.Contains("--clear", StringComparer.Ordinal);
                var target = pos.Count > 1 ? pos[1] : null;
                if (!clear && string.IsNullOrWhiteSpace(target)) return Usage(usage);
                if (string.IsNullOrWhiteSpace(home))
                    throw new PadError("PA125", "could not bind agent presets for this project",
                        $"projects/{p.Id}/project.json", "project has no instance home yet",
                        ["先 pad cli project seed " + p.Id, "或显式给 --home"]);
                home = Projects.NormPath(home);
                var patch = AgentPresets.PatchPath(home, profile);
                if (clear)
                {
                    AgentPresets.Clear(patch);
                    p.Agents.Clear();
                    store.Save(p);
                    Emit(launcher, new { project = p.Id, cleared = true, agents = p.Agents, patch }, rest);
                    return 0;
                }

                var force = rest.Contains("--force", StringComparer.Ordinal);
                // remove 是解开绑定，离线也要能做；set/add/use 需要发行号确认 preset 存在。
                if (!force && verb != "remove")
                {
                    var version = p.Dsh.Version ?? inst?.Dsh.Version ?? settings.Launch.DefaultRelease;
                    var (probe, error) = await AgentPresets.ProbeAsync(
                        launcher, home, version, inst?.Workspace.Path ?? home, profile, CancellationToken.None);
                    if (probe is null)
                        throw new PadError("PA125", "could not bind agent presets for this project", home,
                            error ?? "dump-config failed",
                            ["确认这个发行号已安装、node 可用"]);
                    if (probe.Presets.All(x => !string.Equals(x.Id, target, StringComparison.OrdinalIgnoreCase)))
                        throw new PadError("PA125", "could not bind agent presets for this project", home,
                            $"no such agent preset: {target} (release ships: {string.Join(", ", probe.Presets.Select(x => x.Id))})",
                            ["pad cli project presets " + p.Id]);
                    target = probe.Presets.First(x => string.Equals(x.Id, target, StringComparison.OrdinalIgnoreCase)).Id;
                }

                var list = verb == "set" ? new List<string>() : p.Agents.ToList();
                if (verb == "add")
                {
                    if (!list.Any(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase)))
                        list.Add(target!);
                }
                else if (verb == "remove")
                {
                    list.RemoveAll(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
                }
                else if (verb == "use")
                {
                    list.RemoveAll(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
                    list.Insert(0, target!);
                }
                else
                {
                    list = [target!];
                }

                // P2：agents[0] 是默认；add 只是把选项挂到项目上，use 才换默认。
                if (list.Count == 0) AgentPresets.Clear(patch);
                else AgentPresets.Apply(patch, list[0]);
                p.Agents = list;
                p.Dsh.Profile ??= profile;
                store.Save(p);
                Emit(launcher, new
                {
                    project = p.Id,
                    verb,
                    agents = list,
                    preset = list.FirstOrDefault(),
                    profile,
                    patch,
                    verified = !force && verb != "remove",
                }, rest);
                return 0;
            }

            default:
                return Usage("pad cli project list | show <id> | add <name> --workspace <dir> | remove <id> | seed <id> [--home <dir>] | run <id> [profile] | presets <id> | preset <id> <preset-id|--clear>");
        }
    }

    /// <summary>Machine-readable output; <c>--out &lt;file&gt;</c> writes it to disk instead (a WinExe
    /// cannot reliably pipe stdout, so tests and scripts use the file).</summary>
    static void Emit(Launcher launcher, object value, string[] rest)
    {
        var json = JsonSerializer.Serialize(value, Pretty);
        var outPath = Flag(rest, "--out");
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Cli.Print(json);
            return;
        }
        var full = Path.IsPathFullyQualified(outPath) ? outPath : Path.Combine(launcher.Root, outPath);
        full = Path.GetFullPath(full);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json, new System.Text.UTF8Encoding(false));
        Cli.Print($"wrote {full}");
    }

    static PadError NoProject(string id) =>
        new("PA122", "no such project", $"projects/{id}/project.json",
            "project.json not found under the launcher root",
            ["pad cli project list", "pad cli project add " + id + " --workspace <dir>"]);

    static int Usage(string line)
    {
        Console.Error.WriteLine("usage: " + line);
        return 2;
    }

    static string? Flag(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    /// <summary>Every value of a repeatable flag (<c>--workspace a --workspace b</c>).</summary>
    static List<string> Multi(string[] args, string name)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) list.Add(args[i + 1]);
        return list;
    }

    static List<string> Positionals(string[] args, params string[] valued)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (Array.IndexOf(valued, args[i]) >= 0) { i++; continue; }
            if (args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            list.Add(args[i]);
        }
        return list;
    }
}
