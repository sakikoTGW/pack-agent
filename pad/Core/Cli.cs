using System.Runtime.InteropServices;
using System.Text.Json;

namespace Pad.Core;

/// <summary>
/// PAD is one binary. Launched with <c>cli</c> it drives the same launcher code the
/// window drives, so scripts and the UI can never disagree about what an operation does.
/// </summary>
public static class Cli
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll")] static extern bool AllocConsole();

    const int AttachParent = -1;

    static void EnsureConsole()
    {
        // A WinExe has no console of its own; borrow the caller's, or make one.
        if (!AttachConsole(AttachParent)) AllocConsole();
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    static string? _logFile;

    /// <summary>
    /// A WinExe cannot rely on borrowing the caller's console, so everything the CLI
    /// prints is also appended to a log under the launcher root.
    /// </summary>
    static void Say(string line)
    {
        Console.WriteLine(line);
        if (_logFile is null) return;
        try { System.IO.File.AppendAllText(_logFile, line + Environment.NewLine); }
        catch { /* console still got it */ }
    }

    static void Out(object value) =>
        Say(value is string s ? s : JsonSerializer.Serialize(value, Pretty));

    public static async Task<int> Run(string[] args)
    {
        EnsureConsole();
        var launcher = Launcher.Default();
        _logFile = System.IO.Path.Combine(launcher.Root, "pad-cli.log");
        Say($"--- {DateTimeOffset.Now:HH:mm:ss} pad cli {string.Join(' ', args)}");
        if (launcher.Settings().Other.Debug)
            Say($"[debug] root={launcher.Root} schema={launcher.Settings().Schema}");
        var group = args.Length > 0 ? args[0] : "help";
        var cmd = args.Length > 1 ? args[1] : "";
        var rest = args.Skip(2).ToArray();
        var log = new Progress<string>(Say);

        try
        {
            switch (group)
            {
                case "release" when cmd == "list":
                    Out(launcher.Releases());
                    return 0;

                case "release" when cmd == "install":
                    if (rest.Length < 1) return Usage("pad cli release install <version>");
                    Out(await launcher.InstallRelease(rest[0], log, CancellationToken.None));
                    return 0;

                case "release" when cmd == "remove":
                    if (rest.Length < 1) return Usage("pad cli release remove <version>");
                    launcher.RemoveRelease(rest[0]);
                    Out($"removed {rest[0]}");
                    return 0;

                case "release" when cmd == "pin":
                    if (rest.Length < 1) return Usage("pad cli release pin <bin.js>");
                    Out(await launcher.PinExisting(rest[0], log, CancellationToken.None));
                    return 0;

                case "instance" when cmd == "list":
                    Out(launcher.Instances());
                    return 0;

                case "instance" when cmd == "create":
                    if (rest.Length < 2) return Usage("pad cli instance create <name> <version>");
                    Out(launcher.CreateInstance(rest[0], rest[1]));
                    return 0;

                case "instance" when cmd == "remove":
                    if (rest.Length < 1) return Usage("pad cli instance remove <id>");
                    launcher.RemoveInstance(rest[0]);
                    Out($"removed {rest[0]}");
                    return 0;

                case "instance" when cmd == "pin":
                {
                    if (rest.Length < 2) return Usage("pad cli instance pin <id> <version>");
                    var inst = Need(launcher, rest[0]);
                    if (!launcher.Releases().Any(r => r.Version == rest[1]))
                        throw new PadError("PA001", "that release is not installed",
                            $"versions/{rest[1]}", rest[1], ["pad cli release install " + rest[1]]);
                    inst.Dsh.Version = rest[1];
                    launcher.SaveInstance(inst);
                    Out(inst);
                    return 0;
                }

                case "instance" when cmd == "clone":
                {
                    if (rest.Length < 2) return Usage("pad cli instance clone <id> <newName>");
                    Out(launcher.CloneInstance(rest[0], rest[1]));
                    return 0;
                }

                case "instance" when cmd == "workspace":
                {
                    if (rest.Length < 2) return Usage("pad cli instance workspace isolate|share <id> [dir]");
                    var mode = rest[0];
                    var id = rest[1];
                    if (mode == "isolate")
                    {
                        Out(launcher.SetWorkspaceIndie(id, true));
                        return 0;
                    }
                    if (mode == "share")
                    {
                        if (rest.Length < 3) return Usage("pad cli instance workspace share <id> <dir>");
                        Out(launcher.SetWorkspaceIndie(id, false, rest[2]));
                        return 0;
                    }
                    if (mode == "set")
                    {
                        if (rest.Length < 3) return Usage("pad cli instance workspace set <id> <dir>");
                        Out(launcher.SetWorkspacePath(id, rest[2]));
                        return 0;
                    }
                    return Usage("pad cli instance workspace isolate|share|set <id> [dir]");
                }

                case "rewrite-scripts":
                    launcher.RewriteLaunchScripts();
                    Out("rewrote");
                    return 0;

                case "profile" when cmd == "list":
                {
                    if (rest.Length < 1) return Usage("pad cli profile list <instance>");
                    var inst = Need(launcher, rest[0]);
                    Out(launcher.Profiles(inst).Select(p => new
                    {
                        p.Name,
                        p.Bundles,
                        p.HasApiProxy,
                        p.HasTui,
                    }));
                    return 0;
                }

                case "profile" when cmd == "add":
                {
                    if (rest.Length < 3) return Usage("pad cli profile add <instance> <profile> <tui|web|gateway>");
                    var inst = Need(launcher, rest[0]);
                    var specs = Recipe(rest[2], inst.Dsh.Version);
                    Say($"profile {rest[1]} <- {string.Join(", ", specs)}");
                    await launcher.AddBundles(inst, rest[1], specs, log, CancellationToken.None);
                    Out(launcher.Profiles(inst).Select(p => new { p.Name, p.Bundles }));
                    return 0;
                }

                case "plugin" when cmd == "list":
                {
                    if (rest.Length < 1) return Usage("pad cli plugin list <instance> [profile]");
                    var inst = Need(launcher, rest[0]);
                    IEnumerable<BundleRow> rows = launcher.ListBundles(inst);
                    if (rest.Length > 1)
                        rows = rows.Where(r => r.Profile == rest[1]);
                    Out(rows);
                    return 0;
                }

                case "plugin" when cmd is "add" or "remove" or "update":
                {
                    if (rest.Length < 3) return Usage($"pad cli plugin {cmd} <instance> <profile> <spec>");
                    var spec = string.Join(" ", rest.Skip(2));
                    Out(await launcher.PluginOp(rest[0], cmd, spec, log, CancellationToken.None, rest[1]));
                    return 0;
                }

                // `run`, `ps`, `gateway` and `where` are single-level commands, so their
                // first argument lands in `cmd`, not in `rest`.
                case "run":
                {
                    if (cmd.Length == 0) return Usage("pad cli run <instance> [profile]");
                    var inst = Need(launcher, cmd);
                    var profile = rest.Length > 0 ? rest[0]
                        : inst.LastProfile ?? launcher.Profiles(inst).FirstOrDefault()?.Name;
                    if (profile is null) return Usage("this instance has no profile yet");
                    var runner = new Runner(launcher);
                    await launcher.EnsureRuntimeNode(log, CancellationToken.None, inst.Dsh.Version);
                    Out(runner.Launch(inst, profile, launcher.Settings()));
                    return 0;
                }

                case "script":
                {
                    if (cmd.Length == 0) return Usage("pad cli script <instance> [profile]");
                    var inst = Need(launcher, cmd);
                    var profile = rest.Length > 0 ? rest[0]
                        : inst.LastProfile ?? launcher.Profiles(inst).FirstOrDefault()?.Name ?? "dsh-tui";
                    Out(new Runner(launcher).WriteScript(inst, profile, launcher.Settings()));
                    return 0;
                }

                case "stop":
                {
                    if (cmd.Length == 0) return Usage("pad cli stop <instance> <profile>");
                    if (rest.Length < 1) return Usage("pad cli stop <instance> <profile>");
                    new Runner(launcher).Stop(cmd, rest[0]);
                    Out($"stopped {cmd} · {rest[0]}");
                    return 0;
                }

                case "ps":
                    Out(launcher.Runtime().Entries);
                    return 0;

                case "gateway":
                {
                    if (cmd.Length == 0) return Usage("pad cli gateway <instance> [rpc-method]");
                    var inst = Need(launcher, cmd);
                    var probe = await Gateway.Probe(inst, anyRunning: true, CancellationToken.None);
                    Say($"{probe.State}: {probe.Message}");
                    if (probe.Ad is null) return 1;
                    Say($"url {probe.Ad.Url} pid {probe.Ad.Pid}");
                    if (rest.Length > 0)
                        Out(await Gateway.Call(probe.Ad, rest[0], new { }, CancellationToken.None));
                    return probe.State == GatewayState.Ready ? 0 : 1;
                }

                case "market":
                {
                    var shelf = Market.Shelves.FirstOrDefault(s => s.Id == cmd);
                    if (shelf.Query is null)
                        return Usage($"pad cli market <{string.Join('|', Market.Shelves.Select(s => s.Id))}> [text]");
                    var page = await Market.Search(shelf.Query, string.Join(' ', rest), 0,
                        CancellationToken.None);
                    Say($"{shelf.Label}: 共 {page.Total} 个，第 1/{page.PageCount} 页");
                    Out(page.Rows);
                    return 0;
                }

                // Try a bundle for one run, then keep or drop it.
                case "trial" when cmd == "list":
                {
                    if (rest.Length < 1) return Usage("pad cli trial list <instance>");
                    Out(new Trials(launcher).Load(Need(launcher, rest[0])).Trials);
                    return 0;
                }

                case "trial" when cmd == "add":
                {
                    if (rest.Length < 3) return Usage("pad cli trial add <instance> <profile> <spec>");
                    var inst = Need(launcher, rest[0]);
                    Out(await new Trials(launcher).Begin(inst, rest[1], rest[2], log, CancellationToken.None));
                    return 0;
                }

                case "trial" when cmd is "commit" or "drop":
                {
                    if (rest.Length < 3) return Usage($"pad cli trial {cmd} <instance> <profile> <spec>");
                    var inst = Need(launcher, rest[0]);
                    var trials = new Trials(launcher);
                    var rec = trials.Load(inst).Trials
                        .FirstOrDefault(t => t.Profile == rest[1] && t.Spec == rest[2]);
                    if (rec is null) return Usage($"no pending trial: {rest[1]} / {rest[2]}");
                    if (cmd == "commit") trials.Commit(inst, rec, log);
                    else await trials.Discard(inst, rec, log, CancellationToken.None);
                    Out(trials.Load(inst).Trials);
                    return 0;
                }

                // The projection axis stays in TS/Rust; PAD drives it and reports.
                case "pack" when cmd == "list":
                {
                    if (rest.Length < 1) return Usage("pad cli pack list <instance>");
                    var packs = new Packs(launcher);
                    if (packs.Unavailable() is { } missing)
                    {
                        Say($"{missing.Reason}: {missing.Detail}");
                        return 1;
                    }
                    Out(await packs.List(Need(launcher, rest[0]), CancellationToken.None));
                    return 0;
                }

                case "pack" when cmd == "project":
                {
                    if (rest.Length < 2) return Usage("pad cli pack project <instance> <pack>");
                    var packs = new Packs(launcher);
                    Say(await packs.Project(Need(launcher, rest[0]), rest[1], log, CancellationToken.None));
                    return 0;
                }

                case "pack" when cmd is "allow" or "deny":
                {
                    if (rest.Length < 2) return Usage($"pad cli pack {cmd} <instance> <pack-id>");
                    var packs = new Packs(launcher);
                    var inst = Need(launcher, rest[0]);
                    if (cmd == "allow") await packs.Allow(inst, rest[1], log, CancellationToken.None);
                    else await packs.Deny(inst, rest[1], log, CancellationToken.None);
                    Out(await packs.List(inst, CancellationToken.None));
                    return 0;
                }

                case "pack" when cmd == "scan":
                    Say(await new Packs(launcher).ScanDrop(log, CancellationToken.None));
                    return 0;

                case "import":
                {
                    if (cmd.Length == 0) return Usage("pad cli import <pack.zip>");
                    Say(await new Packs(launcher).Import(cmd, log, CancellationToken.None));
                    return 0;
                }

                case "adopt":
                {
                    var home = cmd.Length > 0 ? cmd : LaunchPolicy.DefaultDshHome(launcher.Settings().Launch.DshHome);
                    var version = rest.Length > 0
                        ? rest[0]
                        : await launcher.EnsureRelease(log, CancellationToken.None);
                    Out(launcher.AdoptHome(home, version));
                    return 0;
                }

                case "crash":
                {
                    if (cmd.Length == 0) return Usage("pad cli crash <instance>");
                    Say(CrashAnalyzer.Analyze(launcher, cmd).Render());
                    return 0;
                }

                case "update":
                    Out(await Updates.Check(launcher, launcher.Settings().Download.NpmRegistry,
                        CancellationToken.None));
                    return 0;

                case "shortcut":
                {
                    if (cmd.Length == 0) return Usage("pad cli shortcut <instance> [profile]");
                    var inst = Need(launcher, cmd);
                    var profile = rest.Length > 0 ? rest[0]
                        : inst.LastProfile ?? launcher.Profiles(inst).FirstOrDefault()?.Name;
                    if (profile is null) return Usage("this instance has no profile yet");
                    Out(launcher.WriteShortcut(inst, profile));
                    return 0;
                }

                case "logs":
                {
                    if (cmd.Length == 0) return Usage("pad cli logs <instance>");
                    Out(new { dir = launcher.LogsDir(cmd) });
                    return 0;
                }

                case "where":
                    Out(new
                    {
                        root = launcher.Root,
                        settings = launcher.SettingsPath,
                        repo = new Packs(launcher).RepoRoot(),
                    });
                    return 0;

                case "sniff":
                {
                    var launch = launcher.Settings().Launch;
                    Out(DshSniff.Probe(launch.DshHome, launch.TuiPackage));
                    return 0;
                }

                case "credentials" when cmd == "has":
                {
                    Instance? inst = rest.Length > 0 ? Need(launcher, rest[0]) : null;
                    var probe = CredentialsGate.Inspect(launcher, inst, launcher.Settings());
                    Out(new { configured = probe.Configured, via = probe.Via, location = probe.Location });
                    if (probe.Configured) return 0;
                    Say(CredentialsFile.MissingError(probe.Location).Render());
                    return 1;
                }

                case "credentials" when cmd == "list":
                    Out(launcher.ListCredentials());
                    return 0;

                case "credentials" when cmd == "get":
                    if (rest.Length < 1) return Usage("pad cli credentials get <name>");
                    Out(launcher.GetCredentials(rest[0]) ?? "");
                    return 0;

                case "credentials" when cmd == "set":
                    if (rest.Length < 2) return Usage("pad cli credentials set <name> <file.yaml>");
                    Out(launcher.SetCredentials(rest[0], System.IO.File.ReadAllText(rest[1])));
                    return 0;

                case "credentials" when cmd == "remove":
                    if (rest.Length < 1) return Usage("pad cli credentials remove <name>");
                    launcher.RemoveCredentials(rest[0]);
                    Out($"removed {rest[0]}");
                    return 0;

                case "portable-export":
                {
                    if (cmd.Length == 0) return Usage("pad cli portable-export <dest> [instanceId]");
                    Out(launcher.ExportPortable(cmd, rest.Length > 0 ? rest[0] : null));
                    return 0;
                }

                default:
                    Console.WriteLine("""
                        pad cli import <pack.zip>
                        pad cli run <instance> [profile]
                        pad cli script <instance> [profile]
                        pad cli rewrite-scripts
                        pad cli release  list | install <ver> | pin <bin.js> | remove <ver>
                        pad cli instance list | create <name> <ver> | pin <id> <ver> | clone <id> <name> | remove <id>
                        pad cli instance workspace isolate <id> | share <id> <dir>
                        pad cli profile  list <instance> | add <instance> <profile> <tui|web|gateway>
                        pad cli plugin   list <instance> [profile] | add|remove|update <instance> <profile> <spec>
                        pad cli market   plugin|tui|skill|host [text]
                        pad cli trial    list <instance> | add <instance> <profile> <spec>
                                         | commit <instance> <profile> <spec> | drop <instance> <profile> <spec>
                        pad cli pack     list <instance> | project <instance> <pack>
                                         | allow <instance> <pack-id> | deny <instance> <pack-id> | scan
                        pad cli credentials list | has [instance] | get <name> | set <name> <file.yaml> | remove <name>
                        pad cli portable-export <dest> [instanceId]
                        pad cli stop <instance> <profile>
                        pad cli ps
                        pad cli gateway <instance> [rpc-method]
                        pad cli adopt [home] [version]
                        pad cli crash <instance>
                        pad cli update
                        pad cli shortcut <instance> [profile]
                        pad cli logs <instance>
                        pad cli where
                        pad cli sniff
                        """);
                    return group == "help" ? 0 : 1;
            }
        }
        catch (Exception ex)
        {
            Say(PadError.Describe(ex));
            Say(ex.StackTrace ?? "");
            return 1;
        }
    }

    /// <summary>
    /// Where the management side door comes from. Override with PAD_GATEWAY_SPEC to
    /// point at a working copy instead of the published package.
    /// </summary>
    public static string GatewaySpec =>
        Environment.GetEnvironmentVariable("PAD_GATEWAY_SPEC") is { Length: > 0 } s
            ? s
            : "@sakikotgw/pad-gateway";

    /// <summary>
    /// The bundle sets PAD installs, kept identical to what the window installs.
    /// The gateway package pulls apiproxy and the directory picker itself, so a
    /// terminal profile needs exactly two adds.
    /// </summary>
    public static string[] Recipe(string kind, string release) => kind switch
    {
        "tui" => ["@deepseek-harness-tui/dsh-tui", GatewaySpec],
        "web" => [$"@deepseek-ai/dsh-web-app@{release}"],
        _ => [GatewaySpec],
    };

    static Instance Need(Launcher l, string id) =>
        l.Instance(id) ?? throw new PadError("PA026", "no such instance", $"instances/{id}", id,
            ["pad cli instance list"]);

    static int Usage(string line)
    {
        Console.Error.WriteLine("usage: " + line);
        return 2;
    }
}
