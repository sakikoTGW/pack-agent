using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
                {
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    var packRef = FlagValue(all, "--pack");
                    var version = FlagValue(all, "--version");
                    var name = FlagValue(all, "--name");
                    var profile = FlagValue(all, "--profile");
                    var positional = Positionals(all, "--pack", "--version", "--name", "--profile");
                    if (name is null && positional.Count > 0) name = positional[0];
                    if (version is null && positional.Count > 1) version = positional[1];

                    if (!string.IsNullOrWhiteSpace(packRef))
                    {
                        var cards = await new Packs(launcher).Catalog("", null, log, CancellationToken.None);
                        var card = cards.FirstOrDefault(c =>
                                       string.Equals(c.Id, packRef, StringComparison.OrdinalIgnoreCase))
                                   ?? cards.FirstOrDefault(c => SamePath(c.Path, packRef))
                                   ?? cards.FirstOrDefault(c =>
                                       string.Equals(c.Path, packRef, StringComparison.OrdinalIgnoreCase));
                        if (card is null)
                            throw new PadError("PA012", "no such pack", packRef, null, ["pad cli pack list"]);
                        Out(await launcher.CreateFromPack(card, name, log, CancellationToken.None));
                        return 0;
                    }

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
                        return Usage("pad cli instance create <name> <version> | --pack <id|path> [--name <n>] [--profile <p>]");
                    Out(launcher.CreateInstance(name, version, profile));
                    return 0;
                }

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
                // `pack list <instance>` = what that workspace has; bare `pack list` = the
                // machine catalog the instance-create picker reads (`--json` / `--out`).
                case "pack" when cmd == "list":
                {
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    var packs = new Packs(launcher);
                    var instance = Positionals(all, "--out", "--roots", "--deep").FirstOrDefault();
                    if (instance is not null)
                    {
                        if (packs.Unavailable() is { } missing)
                        {
                            Say($"{missing.Reason}: {missing.Detail}");
                            return 1;
                        }
                        Out(await packs.List(Need(launcher, instance), CancellationToken.None));
                        return 0;
                    }

                    if (packs.Unavailable() is { } noChain)
                    {
                        Say($"{noChain.Reason}: {noChain.Detail}");
                        return 1;
                    }
                    var cards = await packs.Catalog(FlagValue(all, "--roots") ?? "", FlagValue(all, "--deep"),
                        log, CancellationToken.None);
                    if (all.Contains("--json"))
                    {
                        var outPath = FlagValue(all, "--out");
                        if (outPath is not null)
                        {
                            var full = System.IO.Path.GetFullPath(outPath);
                            System.IO.File.WriteAllText(full, JsonSerializer.Serialize(cards, Pretty));
                            Say($"wrote {full}");
                        }
                        else Out(cards);
                        return 0;
                    }

                    Say($"packs   {cards.Count}");
                    foreach (var card in cards)
                    {
                        var title = string.IsNullOrWhiteSpace(card.Title) ? card.Id : card.Title;
                        var kernel = string.IsNullOrWhiteSpace(card.Kernel) ? "-" : card.Kernel;
                        var components = card.Components is null ? "?" : card.Components.Total.ToString();
                        Say($"  {title,-28} {card.Version,-12} 内核 {kernel,-14} 组件 {components,-4} {card.Path}");
                    }
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

                // 进程现场（PEB）：调试"谁在跑、认哪个 home"用。discover 内部同一套读取。
                case "procs":
                {
                    var all = new[] { cmd }.Concat(rest).Where(a => a.Length > 0).ToArray();
                    int? only = null;
                    for (var i = 0; i < all.Length; i++)
                        if (all[i] == "--pid" && i + 1 < all.Length && int.TryParse(all[i + 1], out var pid))
                            only = pid;

                    IReadOnlyList<ProcInfo> rows = only is int one
                        ? (ProcTable.Read(one) is { } info ? [info] : [])
                        : ProcTable.Snapshot(900, CancellationToken.None);

                    if (all.Contains("--json"))
                    {
                        var outPath = FlagValue(all, "--out");
                        if (outPath is not null)
                        {
                            var full = System.IO.Path.GetFullPath(outPath);
                            System.IO.File.WriteAllText(full, JsonSerializer.Serialize(rows, Pretty));
                            Say($"wrote {full}");
                        }
                        else Out(rows);
                        return 0;
                    }

                    Say($"procs   {rows.Count}");
                    foreach (var p in rows)
                    {
                        Say($"  {p.Pid,-7} p{p.ParentPid,-7} {p.Exe ?? "-"}");
                        Say($"          home={p.DshHome ?? "-"}  profile={p.ProfileArg ?? p.DshProfile ?? "-"}" +
                            $"  userData={p.UserDataDir ?? "-"}");
                        if (p.CommandLine is { Length: > 0 } cmdline)
                        {
                            var text = cmdline.Length > 180 ? cmdline[..180] + "…" : cmdline;
                            Say($"          cmd={text}");
                        }
                    }
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

                case "experimental" when cmd == "list":
                {
                    var settings = launcher.Settings();
                    Out(KnownExperiments.Select(e => new
                    {
                        id = e.Id,
                        description = e.Description,
                        enabled = settings.Other.Has(e.Id),
                    }));
                    return 0;
                }

                case "experimental" when cmd is "enable" or "disable":
                {
                    var id = rest.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
                    if (id is null) return Usage("pad cli experimental enable|disable <id>");
                    if (!KnownExperiments.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)))
                        throw new PadError("PA097", "no such experimental feature", id, null,
                            ["pad cli experimental list"]);

                    var settings = launcher.Settings();
                    if (cmd == "enable") settings.Other.Experiments.Add(id);
                    else settings.Other.Experiments.RemoveAll(e => string.Equals(e, id, StringComparison.OrdinalIgnoreCase));
                    settings.Other.Clamp();
                    launcher.SaveSettings(settings);
                    Out(new { id = id.ToLowerInvariant(), enabled = cmd == "enable" });
                    return 0;
                }

                case "system" when cmd == "new":
                {
                    RequireExperiment(launcher, "layers", "system & layer");
                    if (rest.Length < 1)
                        return Usage("pad cli system new <name> [--root <dir>] [--layers a,b,c] [--modules m1,m2] [--module-layer <layer>]");
                    var root = FlagValue(rest, "--root") ?? System.IO.Path.Combine(launcher.Root, "systems");
                    var layers = Split(FlagValue(rest, "--layers") ?? Systems.DefaultLayers);
                    if (layers.Length == 0) layers = Split(Systems.DefaultLayers);
                    var modules = Split(FlagValue(rest, "--modules") ?? "");
                    var moduleLayer = FlagValue(rest, "--module-layer") ?? layers[0];
                    Out(Systems.New(root, rest[0], layers, modules, moduleLayer));
                    return 0;
                }

                case "plugin" when cmd == "audit":
                {
                    var surfaceOverride = FlagValue(rest, "--surface");
                    var positional = new List<string>();
                    for (var i = 0; i < rest.Length; i++)
                    {
                        if (rest[i] == "--surface" || rest[i] == "--out") { i++; continue; }
                        if (rest[i].StartsWith("--", StringComparison.Ordinal)) continue;
                        positional.Add(rest[i]);
                    }
                    if (positional.Count < 2)
                        return Usage("pad cli plugin audit <home> <profile> [--surface tui|web|app] [--json] [--out <path>]");

                    var audit = Plugins.Run(positional[0], positional[1], surfaceOverride);
                    if (rest.Contains("--json"))
                    {
                        var outPath = FlagValue(rest, "--out");
                        if (outPath is not null)
                        {
                            var full = System.IO.Path.GetFullPath(outPath);
                            System.IO.File.WriteAllText(full, JsonSerializer.Serialize(audit, Pretty));
                            Say($"wrote {full}");
                        }
                        else Out(audit);
                        return 0;
                    }
                    Say($"surface {audit.Surface}   profile {audit.Profile}   bundles {audit.Total}   wasted {audit.Wasted}");
                    foreach (var e in audit.Entries)
                        Say($"{e.Verdict,-16} {e.Renders,-8} {e.Origin,-12} {e.Name,-42} {e.Version,-10} patch={e.PatchFiles} client={e.ClientInject,-3} dsh={e.EnginesDsh}");
                    return 0;
                }

                case "node" when cmd == "list":
                {
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    var home = all.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
                    if (home is null) return Usage("pad cli node list <home> [--json]");

                    var entries = Nodes.Ledger.Load(home)
                        .Select(e => new
                        {
                            e.Profile, e.Surface, e.Port, e.Pid, e.StartedAt, e.Url,
                            alive = Proc.Alive(e.Pid),
                        })
                        .ToList();

                    if (all.Contains("--json")) Out(new { home, nodes = entries });
                    else
                    {
                        if (entries.Count == 0) Say("no nodes recorded for that home");
                        foreach (var e in entries)
                            Say($"{(e.alive ? "alive" : "dead ")}  {e.Profile,-10} port {e.Port,-6} pid {e.Pid,-7} {e.StartedAt}");
                    }
                    return 0;
                }

                case "node" when cmd == "close":
                {
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    string? home = null, profile = null;
                    int? port = null;
                    for (var i = 0; i < all.Length; i++)
                    {
                        if (all[i] == "--profile") { profile = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--port") { port = i + 1 < all.Length && int.TryParse(all[++i], out var p) ? p : null; continue; }
                        if (all[i].StartsWith("--", StringComparison.Ordinal)) continue;
                        home ??= all[i];
                    }
                    if (home is null) return Usage("pad cli node close <home> [--profile <p>] [--port <n>]");

                    var entry = Nodes.Ledger.Find(home, profile, port);
                    if (entry is null)
                        throw new PadError("PA081", "no such node is recorded for that home",
                            Nodes.Ledger.PathFor(home), null, ["pad cli node list " + home]);

                    var wasAlive = Proc.Alive(entry.Pid);
                    if (wasAlive)
                    {
                        try { System.Diagnostics.Process.GetProcessById(entry.Pid).Kill(entireProcessTree: true); }
                        catch (Exception ex)
                        {
                            throw new PadError("PA082", "could not stop that node", "pid " + entry.Pid, ex.Message,
                                ["pad cli node list " + home]);
                        }
                    }

                    Nodes.Ledger.Forget(home, entry);
                    Say($"closed {entry.Profile} port {entry.Port} pid {entry.Pid} (was {(wasAlive ? "alive" : "already gone")})");
                    return 0;
                }

                case "node" when cmd == "open":
                {
                    // `cmd` is already the subcommand here ("open"), so only `rest` carries arguments.
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    string? home = null, bin = null, profile = null, nodeExe = null, surfaceName = null, title = null, exePath = null, userData = null;
                    int? port = null;
                    for (var i = 0; i < all.Length; i++)
                    {
                        if (all[i] == "--bin") { bin = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--profile") { profile = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--node") { nodeExe = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--surface") { surfaceName = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--title") { title = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--exe") { exePath = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--user-data") { userData = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--port") { port = i + 1 < all.Length && int.TryParse(all[++i], out var p) ? p : null; continue; }
                        if (all[i].StartsWith("--", StringComparison.Ordinal)) continue;
                        home ??= all[i];
                    }

                    if (home is null)
                        return Usage("pad cli node open <home> [--surface tui|web] [--bin <bin.js>] [--json]");

                    bin = ResolveBin(launcher, bin);

                    var opened = await OpenSurface(home, surfaceName, profile, bin, nodeExe, exePath, userData, port, title);
                    return ReportOpened(home, opened, all.Contains("--json"));
                }

                case "system" when cmd == "list":
                {
                    RequireExperiment(launcher, "layers", "system & layer");
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    var root = FlagValue(all, "--root") ?? Path.Combine(launcher.Root, "systems");

                    var found = Directory.Exists(root)
                        ? Directory.EnumerateDirectories(root)
                            .Where(d => File.Exists(Path.Combine(d, "plan.yaml")))
                            .Select(d => new
                            {
                                name = Path.GetFileName(d),
                                path = d,
                                layers = Nodes.Layers(d).Count,
                            })
                            .OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : [];

                    if (all.Contains("--json")) Out(new { root, systems = found });
                    else
                    {
                        Say(root);
                        if (found.Count == 0) Say("  (no systems)");
                        foreach (var s in found)
                            Say($"  {s.name,-24} layers {s.layers}   {s.path}");
                    }
                    return 0;
                }

                case "layer" when cmd == "open":
                {
                    RequireExperiment(launcher, "layers", "system & layer");
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    string? systemDir = null, layer = null, bin = null, nodeExe = null, surfaceName = null, exePath = null, userData = null, title = null;
                    int? port = null;
                    for (var i = 0; i < all.Length; i++)
                    {
                        if (all[i] == "--bin") { bin = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--node") { nodeExe = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--surface") { surfaceName = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--title") { title = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--exe") { exePath = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--user-data") { userData = i + 1 < all.Length ? all[++i] : null; continue; }
                        if (all[i] == "--port") { port = i + 1 < all.Length && int.TryParse(all[++i], out var p) ? p : null; continue; }
                        if (all[i].StartsWith("--", StringComparison.Ordinal)) continue;
                        if (systemDir is null) systemDir = all[i];
                        else layer ??= all[i];
                    }

                    if (systemDir is null || layer is null)
                        return Usage("pad cli layer open <system> <layer> [--surface web|tui] [--bin <bin.js>] [--json]");

                    bin = ResolveBin(launcher, bin);

                    var home = Nodes.LayerHome(systemDir, layer);
                    if (!Directory.Exists(home))
                        throw new PadError("PA091", "no such layer in that system", home, null,
                            ["pad cli system new <name>", "pad cli layer list " + systemDir]);

                    var surface = Surface.For(surfaceName ?? "web")
                        ?? throw new PadError("PA084", "unknown surface", surfaceName!, null, ["pad cli surfaces"]);

                    // DSH only auto-initializes its five shipped profiles. A layer profile has a
                    // custom name, so materialize it from a template without booting (D16).
                    if (surface.Name == "web" && bin is not null)
                    {
                        var made = await Nodes.MaterializeProfile(home, layer, "web", bin, nodeExe ?? "node",
                            TimeSpan.FromSeconds(120), CancellationToken.None);
                        if (!made.Ok)
                        {
                            var detail = new StringBuilder();
                            detail.Append("dsh did not write a usable ").Append(made.ManifestPath);
                            if (made.DirWasNew)
                                detail.AppendLine().Append(made.DirGone
                                    ? "no half-built profile directory is left behind"
                                    : "a half-built profile directory is still on disk: " + made.ProfileDir);
                            detail.AppendLine().Append("log ").Append(made.LogPath).AppendLine(":");
                            detail.Append(made.LogTail.Length > 0 ? made.LogTail : "(empty)");

                            throw new PadError("PA092", "could not materialize the layer profile", home,
                                detail.ToString(),
                                ["dsh --profile " + layer + " --from-default-profile web --dump-config",
                                 "pad cli layer open " + systemDir + " " + layer + " --surface web --bin <bin.js>",
                                 "pad cli layer list " + systemDir]);
                        }
                    }

                    var opened = await OpenSurface(home, surfaceName, layer, bin, nodeExe, exePath, userData, port, title);
                    return ReportOpened(home, opened, all.Contains("--json"));
                }

                case "layer" when cmd == "list":
                {
                    RequireExperiment(launcher, "layers", "system & layer");
                    var all = rest.Where(a => a.Length > 0).ToArray();
                    var systemDir = all.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
                    if (systemDir is null) return Usage("pad cli layer list <system> [--json]");

                    if (!Directory.Exists(Path.Combine(systemDir, "layers")))
                        throw new PadError("PA100", "no such system", systemDir,
                            "expected a directory holding layers/",
                            ["pad cli system list", "pad cli system new <name>"]);

                    var rows = Nodes.Layers(systemDir).Select(l =>
                    {
                        var home = Nodes.LayerHome(systemDir, l);
                        var running = Nodes.Ledger.Load(home);
                        return new
                        {
                            layer = l,
                            home,
                            hasProfile = File.Exists(Path.Combine(home, "profiles", l, "package.json")),
                            running = running.Count,
                        };
                    }).ToList();

                    if (all.Contains("--json")) Out(new { system = systemDir, layers = rows });
                    else
                    {
                        Say($"{systemDir}");
                        foreach (var r in rows)
                            Say($"  {(r.hasProfile ? "profile" : "empty  ")}  {r.layer,-16} home {r.home}  running {r.running}");
                    }
                    return 0;
                }

                case "usage":
                {
                    var all = new[] { cmd }.Concat(rest).Where(a => a.Length > 0).ToArray();
                    string? home = null;
                    string? outPath = null;
                    for (var i = 0; i < all.Length; i++)
                    {
                        if (all[i] == "--out") { if (i + 1 < all.Length) outPath = all[++i]; continue; }
                        if (all[i].StartsWith("--", StringComparison.Ordinal)) continue;
                        home ??= all[i];
                    }

                    var homes = home is not null
                        ? new[] { home }
                        : Inspect.DefaultHomes(launcher.Settings().Launch.DshHome).ToArray();
                    if (homes.Length == 0)
                        throw new PadError("PA071", "no DSH home found on this machine", "~", null,
                            ["pad cli usage <home>", "pad cli sniff"]);

                    // Fully qualified: Cli.Usage(string) is the usage printer and would win the lookup.
                    var reports = homes.Select(h => Pad.Core.Usage.Measure(h)).ToList();

                    if (all.Contains("--json"))
                    {
                        var payload = new { homes = reports };
                        if (outPath is not null)
                        {
                            var full = System.IO.Path.GetFullPath(outPath);
                            System.IO.File.WriteAllText(full, JsonSerializer.Serialize(payload, Pretty));
                            Say($"wrote {full}");
                        }
                        else Out(payload);
                        return 0;
                    }

                    foreach (var r in reports)
                    {
                        Say(r.Home);
                        Say($"  sessions {r.Sessions}   turns {r.Turns}   steps {r.Steps}");
                        Say($"  in {r.InputTokens:N0}   out {r.OutputTokens:N0}   cacheRead {r.CacheReadTokens:N0}");
                        Say($"  context  user {r.UserTokens:N0}   inject {r.InjectTokens:N0}   assistant {r.AssistantTokens:N0}   tool {r.ToolTokens:N0}");
                        Say($"  tool calls {r.Tools.Sum(t => t.Calls)}");
                        foreach (var t in r.Tools.Take(12))
                            Say($"    {t.Calls,4}x {t.Tokens,9:N0} tok  {t.Tool}");
                    }
                    return 0;
                }

                case "relations":
                {
                    var model = Graph.Build(launcher.Instances());
                    var md = rest.Contains("--md");

                    if (md)
                    {
                        Say($"# 关系（自动生成，勿手改）\n");
                        Say($"共 {model.Workspaces.Count} 工作区 · {model.Instances} 实例 · " +
                            $"{model.Agents} agent · {model.Subagents} 子 agent\n");
                        foreach (var ws in model.Workspaces)
                        {
                            Say($"\n## {ws.Label}\n");
                            Say($"`{ws.Path}`\n");
                            foreach (var node in ws.Instances)
                            {
                                Say($"### {node.Inst.Name}  `{node.Code}`  " +
                                    $"{(node.Running ? "在跑" : "未启动")}  " +
                                    $"{node.Total} agent / {node.Subagents} 子\n");
                                Walk(node.Agents, 0);
                            }
                        }
                        return 0;
                    }

                    Say($"{model.Workspaces.Count} 工作区 · {model.Instances} 实例 · " +
                        $"{model.Agents} agent · {model.Subagents} 子 agent");
                    foreach (var ws in model.Workspaces)
                    {
                        Say($"\n{ws.Label}   ({ws.Instances.Count} 实例)   {ws.Path}");
                        foreach (var node in ws.Instances)
                        {
                            Say($"  {node.Inst.Name,-16} {node.Code}  " +
                                $"{(node.Running ? "running" : "idle   ")}  " +
                                $"{node.Total} agent / {node.Subagents} 子");
                            Walk(node.Agents, 2);
                        }
                    }
                    return 0;

                    void Walk(IReadOnlyList<Graph.Agent> agents, int indent)
                    {
                        foreach (var agent in agents)
                        {
                            var pad = new string(' ', indent * 2);
                            var mark = agent.IsChild ? "└ " : "· ";
                            Say($"{pad}{mark}{agent.Title}   {Graph.Ago(agent.Touched)}");
                            Walk(agent.Children, indent + 1);
                        }
                    }
                }

                case "discover":
                {
                    var all = new[] { cmd }.Concat(rest).Where(a => a.Length > 0).ToArray();
                    var deep = new List<string>();
                    for (var i = 0; i < all.Length; i++)
                        if (all[i] == "--deep" && i + 1 < all.Length) deep.Add(all[++i]);

                    var inventory = Discover.Run(launcher, deep, CancellationToken.None);

                    if (all.Contains("--json"))
                    {
                        var outPath = FlagValue(all, "--out");
                        if (outPath is not null)
                        {
                            var full = System.IO.Path.GetFullPath(outPath);
                            System.IO.File.WriteAllText(full, JsonSerializer.Serialize(inventory, Pretty));
                            Say($"wrote {full}");
                        }
                        else Out(inventory);
                        return 0;
                    }

                    Say($"kernels {inventory.Kernels.Count}");
                    foreach (var k in inventory.Kernels)
                        Say($"  {k.Version,-14} {k.Source}   {(k.Bin ?? k.Identity)}");

                    Say($"shells  {inventory.Shells.Count}");
                    foreach (var s in inventory.Shells)
                        Say($"  {(s.Running ? "running" : "stopped")}  {s.Version,-10} {s.Exe}" +
                            $"   home {s.Home ?? "-"}   userData {s.UserData ?? "-"}" +
                            $"   kernel {s.KernelVersion ?? "-"}{(s.Pid is int sp ? $"   pid {sp}" : "")}   ({s.Found})");

                    Say($"tui     {inventory.Tuis.Count}");
                    foreach (var t in inventory.Tuis)
                        Say($"  {(t.Running ? "running" : "idle   ")}  {t.Package} {t.Version ?? ""}" +
                            $"   home {t.Home ?? "-"}   profile {t.Profile ?? "-"}" +
                            $"   kernel {t.DeclaredKernel ?? "-"}{(t.Pid is int tp ? $"   pid {tp}" : "")}" +
                            $"   {(t.Launcher is null ? "" : "launcher " + t.Launcher)}   ({t.Found})");

                    Say($"cli     {inventory.Clis.Count}");
                    foreach (var c in inventory.Clis)
                        Say($"  {(c.Running ? "running" : "idle   ")}  {c.Version,-12} {c.Bin}" +
                            $"{(c.Pid is int cp ? $"   pid {cp}" : "")}   ({c.Found})");

                    Say($"homes   {inventory.Homes.Count}");
                    foreach (var h in inventory.Homes)
                        Say($"  {(h.Running ? "running" : "idle   ")}  [{string.Join(",", h.Profiles)}]  {h.Sessions} sessions  {h.Path}   ({h.Found})");
                    return 0;
                }

                case "inspect":
                {
                    var json = cmd == "--json" || rest.Contains("--json");
                    var all = new[] { cmd }.Concat(rest).Where(a => a.Length > 0).ToArray();
                    string? target = null;
                    string? outPath = null;
                    for (var i = 0; i < all.Length; i++)
                    {
                        if (all[i] == "--json") continue;
                        if (all[i] == "--out") { if (i + 1 < all.Length) outPath = all[++i]; continue; }
                        if (all[i].StartsWith("--", StringComparison.Ordinal)) continue;
                        target ??= all[i];
                    }

                    List<Inspect.Report> reports = target is null
                        ? Inspect.DefaultHomes(launcher.Settings().Launch.DshHome)
                            .Select(Inspect.Measure).ToList()
                        : [Inspect.Measure(target)];

                    if (reports.Count == 0)
                        throw new PadError("PA040", "no DSH home found on this machine", "~", null,
                            ["pad cli inspect <home>", "pad cli sniff"]);

                    var primary = reports
                        .Where(r => r.Found)
                        .OrderByDescending(r => r.NewestAt ?? DateTimeOffset.MinValue)
                        .FirstOrDefault();

                    if (primary is null)
                        throw new PadError("PA098", "that is not a DSH home", reports[0].Home,
                            "expected a directory holding profiles/ or storages/",
                            ["pad cli sniff", "pad cli inspect <home>"]);

                    if (json)
                    {
                        var payload = new
                        {
                            home = primary.Home,
                            found = primary.Found,
                            sessions = primary.Sessions,
                            newestSession = primary.NewestSession,
                            newestAt = primary.NewestAt?.ToString("o"),
                            model = primary.Model,
                            tools = primary.Tools,
                            toolsTokens = primary.ToolsTokens,
                            systemTokens = primary.SystemTokens,
                            contextTokens = primary.ContextTokens,
                            pressureTokens = primary.PressureTokens,
                            contextWindow = primary.ContextWindow,
                            compactions = primary.Compactions,
                            homes = reports.Select(r => new
                            {
                                home = r.Home,
                                found = r.Found,
                                sessions = r.Sessions,
                                tools = r.Tools,
                                toolsTokens = r.ToolsTokens,
                                contextTokens = r.ContextTokens,
                            }).ToArray(),
                        };

                        // A WinExe has no reliable stdout, so scripts get a file.
                        if (outPath is not null)
                        {
                            var full = System.IO.Path.GetFullPath(outPath);
                            System.IO.File.WriteAllText(full, JsonSerializer.Serialize(payload, Pretty));
                            Say($"wrote {full}");
                        }
                        else Out(payload);
                        return 0;
                    }

                    Say($"home      {primary.Home}");
                    Say($"sessions  {primary.Sessions}   newest {primary.NewestAt:yyyy-MM-dd HH:mm}   {primary.Model}");
                    Say($"tools     {primary.Tools}  ({primary.ToolsTokens:N0} tok)   system {primary.SystemTokens:N0} tok");
                    Say($"context   {primary.ContextTokens:N0} / {primary.ContextWindow:N0}   pressure {primary.PressureTokens:N0}   compacted {primary.Compactions}");
                    foreach (var t in primary.ToolList.Take(8))
                        Say($"          {t.Tokens,5}  {t.Name}");
                    foreach (var r in reports.Where(r => !ReferenceEquals(r, primary)))
                        Say($"other     {r.Home}   tools {r.Tools}   context {r.ContextTokens:N0}");
                    return 0;
                }

                case "surfaces":
                {
                    Out(Surface.All.Select(s => new
                    {
                        name = s.Name,
                        shows = s.Shows,
                        exclusive = s.Exclusive,
                        needsEndpoint = s.NeedsEndpoint,
                        requires = s.Requires,
                    }));
                    return 0;
                }

                default:
                    Console.WriteLine("""
                        pad cli import <pack.zip>
                        pad cli run <instance> [profile]
                        pad cli script <instance> [profile]
                        pad cli rewrite-scripts
                        pad cli release  list | install <ver> | pin <bin.js> | remove <ver>
                        pad cli instance list | create <name> <ver> | create --pack <id|path> [--name <n>] [--profile <p>]
                                         | pin <id> <ver> | clone <id> <name> | remove <id>
                        pad cli instance workspace isolate <id> | share <id> <dir>
                        pad cli profile  list <instance> | add <instance> <profile> <tui|web|gateway>
                        pad cli plugin   list <instance> [profile] | add|remove|update <instance> <profile> <spec>
                        pad cli market   plugin|tui|skill|host [text]
                        pad cli trial    list <instance> | add <instance> <profile> <spec>
                                         | commit <instance> <profile> <spec> | drop <instance> <profile> <spec>
                        pad cli pack     list | list <instance> | project <instance> <pack>
                                         | allow <instance> <pack-id> | deny <instance> <pack-id> | scan
                                         list 支持 --json / --out <path> / --roots <a;b> / --deep <dir>
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
                        pad cli inspect [home] [--json] [--out <path>]
                        pad cli surfaces
                        pad cli experimental list | enable <id> | disable <id>
                        pad cli system new <name>                 (experimental: layers)
                        pad cli layer  list|open <system> <layer> (experimental: layers)
                        """);
                    return group == "help" ? 0 : 1;
            }
        }
        catch (PadError ex)
        {
            // A PadError is already the finished diagnostic: code, location, detail, help.
            // A stack trace after it is noise; only an unexpected exception needs one.
            Say(ex.Render());
            return 1;
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

    /// <summary>
    /// One dispatch for every surface. Callers resolve only the home; surface specifics
    /// stay in <see cref="Nodes"/>.
    /// </summary>
    static async Task<Nodes.Opened> OpenSurface(string home, string? surfaceName, string? profile,
        string? bin, string? nodeExe, string? exePath, string? userData, int? port, string? title)
    {
        var surface = Surface.For(surfaceName ?? "web")
            ?? throw new PadError("PA084", "unknown surface", surfaceName!, null, ["pad cli surfaces"]);

        var effectiveProfile = profile ?? surface.Name switch { "tui" => "tui", "web" => "web", _ => "app" };

        // Opening a second instance on the same home+profile would replace the ledger row
        // and orphan the first process: still running, no longer tracked, un-closeable
        // through PAD. Refuse instead.
        var live = Nodes.Ledger.Load(home)
            .FirstOrDefault(e => string.Equals(e.Profile, effectiveProfile, StringComparison.OrdinalIgnoreCase)
                              && Proc.Alive(e.Pid));
        if (live is not null)
            throw new PadError("PA099", "that profile is already running in this home", home,
                $"profile {live.Profile} · port {live.Port} · pid {live.Pid}",
                ["pad cli node close " + home + " --profile " + effectiveProfile,
                 "pad cli node list " + home]);

        switch (surface.Name)
        {
            case "tui":
                if (bin is null)
                    throw new PadError("PA093", "PAD does not know where the dsh CLI is", home,
                        "set launch.dshCli in pad.json, or pass --bin",
                        ["pad cli node open <home> --surface tui --bin <bin.js>",
                         "pad cli inspect <home>"]);
                return await Nodes.OpenTerminal(home, effectiveProfile, bin, nodeExe ?? "node", title,
                    TimeSpan.FromSeconds(45), CancellationToken.None);

            case "web":
                if (bin is null)
                    throw new PadError("PA094", "PAD does not know where the dsh CLI is", home,
                        "set launch.dshCli in pad.json, or pass --bin",
                        ["pad cli node open <home> --surface web --bin <bin.js>",
                         "pad cli inspect <home>"]);
                return await Nodes.OpenWeb(home, effectiveProfile, bin, nodeExe ?? "node", port,
                    TimeSpan.FromSeconds(90), CancellationToken.None);

            case "app":
                if (exePath is null)
                    throw new PadError("PA095", "the app surface needs --exe", home, null,
                        ["pad cli node open <home> --surface app --exe <DSH Desktop.exe>"]);
                return await Nodes.OpenApp(home, exePath, userData,
                    TimeSpan.FromSeconds(75), CancellationToken.None);

            default:
                throw new PadError("PA085", "that surface is not implemented yet", surface.Name, null,
                    ["pad cli surfaces"]);
        }
    }

    /// <summary>Record what was opened, give it a window when the surface needs one, report facts.</summary>
    static int ReportOpened(string home, Nodes.Opened opened, bool json)
    {
        Nodes.Ledger.Record(home, new Nodes.Running(
            opened.Profile, opened.Surface, opened.Port, opened.Url, opened.Pid,
            DateTimeOffset.Now.ToString("o"), opened.LogPath, opened.Verified));

        var windowed = false;
        var how = "terminal window";
        if (opened.Surface == "web") windowed = Nodes.TryOpenWindow(opened.Url, out how);

        if (json)
        {
            Out(new
            {
                opened.Home, opened.Profile, opened.Surface, opened.Port,
                opened.Url, opened.Pid, opened.LogPath, opened.Verified,
                window = opened.Surface == "tui" ? "terminal window" : (windowed ? how : "none"),
            });
        }
        else
        {
            Say($"opened  {opened.Profile}  surface {opened.Surface}  port {opened.Port}  pid {opened.Pid}");
            Say($"url     {(opened.Url.Length == 0 ? "(none)" : opened.Url)}");
            Say($"window  {how}");
            Say($"log     {(opened.LogPath.Length == 0 ? "(none)" : opened.LogPath)}");
        }
        return 0;
    }

    /// <summary>
    /// Features that ship but stay off until a person turns them on — the same posture
    /// DSH takes with its experimental bundles (D20).
    /// </summary>
    static readonly (string Id, string Description)[] KnownExperiments =
    [
        ("layers", "系统 → 层 → 模块：同一项目里按职能分工具面（D8/D19/D20）"),
    ];

    static void RequireExperiment(Launcher launcher, string id, string what)
    {
        if (launcher.Settings().Other.Has(id)) return;
        throw new PadError("PA096", "experimental feature is off: " + what, "pad.json#other.experiments", null,
            ["pad cli experimental enable " + id, "pad cli experimental list"]);
    }

    /// <summary>
    /// Where the dsh CLI lives: explicit argument, then the user setting, then a pinned
    /// release. Null means nothing is configured — callers name the knob to turn.
    ///
    /// No sniffing here on purpose: <c>DshSniff.Bin()</c> shells out (which / npm root -g)
    /// and blocked the launch path for tens of seconds in testing. PCL sniffs Java once,
    /// with progress and a cache; if PAD grows that, it belongs in settings, not in every
    /// `open` (D21).
    /// </summary>
    static string? ResolveBin(Launcher launcher, string? explicitBin)
    {
        if (!string.IsNullOrWhiteSpace(explicitBin)) return explicitBin;

        var pref = launcher.Settings().Launch.DshCli;
        if (LaunchPolicy.Exists(pref)) return pref;

        return launcher.Releases()
            .Select(r => r.Bin)
            .FirstOrDefault(b => LaunchPolicy.Exists(b));
    }

    static int Usage(string line)
    {
        Console.Error.WriteLine("usage: " + line);
        return 2;
    }

    /// <summary>Value of <c>--name value</c> in a flat argument list, or null.</summary>
    static string? FlagValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    /// <summary>Positional arguments, skipping the values of the flags named in
    /// <paramref name="valued"/> (so a path after <c>--out</c> is not mistaken for one).</summary>
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

    /// <summary>Compare two paths as paths, not strings (<c>foo\..\bar.zip</c> == <c>bar.zip</c>).</summary>
    static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    static string[] Split(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
