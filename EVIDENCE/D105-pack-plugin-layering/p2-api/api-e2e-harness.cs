// P2 ledger API scratch smoke test (not in repo). Runs against the scratch instance in
// .tmp/p2-ledger-e2e: SetPackPluginsActive(false/true) -> RepairPack -> UninstallPack ->
// ApplyPackToInstance, asserting against the profile truth after every step. Never touches
// the live launcher root.
using System.IO;
using Pad.Core;

if (args.Length < 2)
{
    Console.WriteLine("usage: p2-ledger-harness <launcher-root> <instance-id>");
    return 2;
}

var root = args[0];
var instanceId = args[1];
Environment.SetEnvironmentVariable("PACK_LAUNCHER_ROOT", root);
var launcher = new Launcher(root);
var inst = launcher.Instance(instanceId) ?? throw new Exception($"instance {instanceId} missing");
var packs = new Packs(launcher);
var log = new Progress<string>(s => Console.WriteLine("  [log] " + s));
var ct = CancellationToken.None;
const string profile = "web";
const string packId = "Probe-Specs-0.1.0";
var packPath = Path.Combine(root, "library", "packs", "Probe-Specs-0.1.0.pack.zip");
var projectionRel = ".agent-pack/modpacks/pack-agent-modpack-probe-specs";
var fail = 0;

void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
    if (!ok) fail++;
}

bool HasBundle(string key) => PackLedger.ReadProfile(inst, profile).BundleName(key) is not null;
bool HasDep(string key) => PackLedger.ReadProfile(inst, profile).HasDependency(key);
string State() => PackLedger.Load(launcher, inst).EffectiveState(profile, packId);
bool Projection() => Directory.Exists(Path.Combine(inst.Workspace.Path,
    projectionRel.Replace('/', Path.DirectorySeparatorChar)));

// --- baseline ---
Check("baseline: dep+bundle present", HasDep("dsh-context") && HasBundle("dsh-context"));
Check("baseline: ledger installed", State() == PackLedger.StateInstalled);
Check("baseline: projection present", Projection());

// --- disable ---
Console.WriteLine("== SetPackPluginsActive(false)");
var off = await packs.SetPackPluginsActive(inst, packId, profile, false, log, ct);
Check("disable: bundle row removed", !HasBundle("dsh-context"));
Check("disable: dependency kept", HasDep("dsh-context"));
Check("disable: projection kept", Projection());
Check("disable: ledger effective disabled", State() == PackLedger.StateDisabled);
var ledgerOff = PackLedger.Load(launcher, inst).Get(packId)!;
Check("disable: content allow=false", !ledgerOff.Content.Allow);
Check("disable: recorded state disabled", ledgerOff.Profiles[profile].State == PackLedger.StateDisabled);
Check("disable: result says 1 removed", off.BundlesRemoved.Count == 1);

// --- enable again (no re-download) ---
Console.WriteLine("== SetPackPluginsActive(true)");
var on = await packs.SetPackPluginsActive(inst, packId, profile, true, log, ct);
Check("enable: bundle row back", HasBundle("dsh-context"));
Check("enable: ledger effective installed", State() == PackLedger.StateInstalled);
Check("enable: content allow=true", PackLedger.Load(launcher, inst).Get(packId)!.Content.Allow);
Check("enable: nothing missing", on.Missing.Count == 0);

// --- repair (nothing missing -> no-op) ---
Console.WriteLine("== RepairPack");
var rep = await packs.RepairPack(inst, packId, profile, log, ct);
Check("repair: still installed", rep.State == PackLedger.StateInstalled);
Check("repair: nothing added", rep.Added.Count == 0 && rep.StillMissing.Count == 0);

// --- uninstall (exclusive plugin rollback + projection + ledger entry) ---
Console.WriteLine("== UninstallPack");
var un = await packs.UninstallPack(inst, packId, profile, log, ct);
Check("uninstall: removed exclusive plugin", un.RemovedPlugins.Contains("dsh-context"));
Check("uninstall: no shared skipped", un.SkippedShared.Count == 0);
Check("uninstall: projection deleted", !Projection());
Check("uninstall: dependency rolled back", !HasDep("dsh-context"));
Check("uninstall: ledger entry gone", PackLedger.Load(launcher, inst).Get(packId) is null);

// --- install into existing instance (R8: project + allow + manager + diff add) ---
Console.WriteLine("== ApplyPackToInstance");
var ap = await packs.ApplyPackToInstance(inst, packPath, profile, log, ct);
foreach (var warning in ap.Warnings) Console.WriteLine("  [warn] " + warning);
foreach (var failure in ap.Failures) Console.WriteLine("  [fail] " + failure.Text);
Check("apply: ok (no conflicts, optional failures ok)", ap.Ok);
Check("apply: projection id", ap.ProjectionId == "pack-agent-modpack-probe-specs");
Check("apply: projection back", Projection());
Check("apply: dependency+bundle restored", HasDep("dsh-context") && HasBundle("dsh-context"));
Check("apply: content allow=true", PackLedger.Load(launcher, inst).Get(packId)!.Content.Allow);
Check("apply: ledger installed", State() == PackLedger.StateInstalled);
var ledgerApply = PackLedger.Load(launcher, inst).Get(packId)!;
Check("apply: ledger version/title", ledgerApply.Title == "Probe Specs" && ledgerApply.InstalledVersion == "0.1.0");
Check("apply: owners refcount", PackLedger.Load(launcher, inst).OwnersOf(profile, "dsh-context").Count == 1);

// --- apply again: diff must skip everything (no duplicate add/download) ---
Console.WriteLine("== ApplyPackToInstance again (idempotent diff)");
var ap2 = await packs.ApplyPackToInstance(inst, packPath, profile, log, ct);
Check("reapply: no adds", ap2.Added.Count == 0);
Check("reapply: kept plugin", ap2.Kept.Contains("dsh-context"));

Console.WriteLine(fail == 0 ? "HARNESS PASS" : $"HARNESS FAIL {fail}");
return fail == 0 ? 0 : 1;
