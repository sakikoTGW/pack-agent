#!/usr/bin/env bun
/**
 * agent-pack — 打包到安装全自动
 *
 *   agent-pack sync              扫描当前项目 → 打便携包 → 装到所有在场 harness
 *   agent-pack sync --from x.pack.json
 *   agent-pack install x.pack.json
 *   agent-pack detect
 */
import { resolve } from 'node:path'
import { collectListArg, hasFlag, parseAgentArg, parseAllowFullScan, parseCaptureAs, parseModulesArg, parseOnConflict } from './args.js'
import { detectRuntimes, RUNTIME_ADAPTERS } from './adapters.js'
import { exportPackFromProject } from './export.js'
import { installPackFile } from './install.js'
import { PackConflictError } from './errors.js'
import { syncPack } from './sync.js'
import { PACK_APPLY_SKIP } from './project.js'
import type { PackSelectManifest } from './select.js'

function exportCommonArgs(args: string[]) {
  return {
    agent: parseAgentArg(args),
    allowFullScan: parseAllowFullScan(args),
    noBootstrap: hasFlag(args, '--no-bootstrap'),
    captureAs: parseCaptureAs(args),
    modules: parseModulesArg(args),
  }
}

function printGlobalHelp(): void {
  console.log(`agent-pack — pack one agent → detect harness → install

Commands:
  sync     Export one agent (or --all) → portable pack → install
  pack     Selective pack (--agent / --manifest / --skills) + optional --install
  export   Write pack only (requires --agent, --manifest, --skills, or --all)
  install  Install existing pack
  agents   List or init .agent-pack/agents.yaml
  diff     Compare two lock.json or pack.json files
  eject    Uninstall pack (ledger-based)
  status   Show lock / ledger / experiences
  detect   Show detected harnesses

Global flags:
  --agent, -a <id>   Pack one agent from .agent-pack/agents.yaml
  --all              Full-project scan (legacy; omit skills filter)
  --help, -h         Show help`)
}

const [, , cmd, ...rest] = process.argv

function parseArgs(args: string[]): {
  path?: string
  from?: string
  runtime?: string
  name?: string
  help?: boolean
} {
  let path: string | undefined
  let from: string | undefined
  let runtime: string | undefined
  let name: string | undefined
  for (let i = 0; i < args.length; i++) {
    const a = args[i]
    if (a === '--help' || a === '-h') return { help: true }
    if (a === '--runtime' || a === '-t') {
      runtime = args[++i]
      continue
    }
    if (a === '--from' || a === '-f') {
      from = args[++i]
      continue
    }
    if (a === '--name' || a === '-n') {
      name = args[++i]
      continue
    }
    if (!a.startsWith('-') && !path) path = a
  }
  return { path, from, runtime, name }
}

async function cmdDetect(): Promise<void> {
  const cwd = process.cwd()
  const all = await detectRuntimes(cwd)
  const installable = all.filter(id => !PACK_APPLY_SKIP.has(id))
  console.log('Detected harnesses:', all.length ? all.join(', ') : '(none)')
  console.log('Will install to:  ', installable.length ? installable.join(', ') : '(none — use --runtime)')
  console.log('\nSupported adapters:')
  for (const a of RUNTIME_ADAPTERS) {
    if (PACK_APPLY_SKIP.has(a.id)) continue
    console.log(`  ${a.id.padEnd(16)} ${a.label}${a.verified ? '' : ' (unverified)'}`)
  }
}

async function cmdSync(args: string[]): Promise<void> {
  const { from, runtime, name, help } = parseArgs(args)
  const common = exportCommonArgs(args)
  const onConflict = parseOnConflict(args)
  if (help) {
    console.log(`Usage: agent-pack sync [--agent id] [--all] [--from pack.json] [--name name] [--runtime id] ...

  --agent <id>   Pack one agent from .agent-pack/agents.yaml (required unless --from or --all)
  --all          Full-project scan (legacy)

  agent-pack sync --agent packer
  agent-pack sync --from .agent-pack/exports/packer.pack.json`)
    process.exit(0)
  }
  const cwd = process.cwd()
  const report = await syncPack(cwd, { from, runtime, name, onConflict, ...common })
  if (report.exported && report.exportPath) {
    console.error(`[export] ${report.exportPath}`)
    if (report.stats) {
      console.error(
        `  skills=${report.stats.skills} rules=${report.stats.rules} mcp=${report.stats.mcp} bundle=${report.stats.bundleFiles} lock=${report.lockPath ?? report.stats.lockPath ?? 'n/a'}`,
      )
    }
  }
  console.log(JSON.stringify(report, null, 2))
  if (!report.ok) {
    console.error('\nSync incomplete.')
    process.exit(1)
  }
  console.log(`\nOK: sync → [${report.projected.join(', ')}]`)
}

async function cmdExport(args: string[]): Promise<void> {
  const { runtime, name, help } = parseArgs(args)
  const common = exportCommonArgs(args)
  if (help) {
    console.log(`Usage: agent-pack export --agent <id> [--name name] [--runtime id]
       agent-pack export --all [--name name]   # legacy full scan`)
    process.exit(0)
  }
  const cwd = process.cwd()
  const { pack, outPath, stats } = await exportPackFromProject(cwd, { runtime, name, ...common })
  console.error(`[export] ${outPath}`)
  console.error(`  agent=${pack.agent?.id ?? '—'} author=${pack.author ?? '—'} skills=${stats.skills} rules=${stats.rules} mcp=${stats.mcp} bundle=${pack.bundle?.files?.length ?? 0}`)
  console.log(outPath)
}

async function cmdInstall(args: string[]): Promise<void> {
  const { path, runtime, help } = parseArgs(args)
  const noBootstrap = hasFlag(args, '--no-bootstrap')
  const captureAs = parseCaptureAs(args)
  const modules = parseModulesArg(args)
  const onConflict = parseOnConflict(args)
  if (help || !path) {
    console.log('Usage: agent-pack install <pack.json> [--runtime id] [--capture-as skill|experience] [--on-conflict stop|skip|replace] [--modules ...] [--no-bootstrap]')
    process.exit(help ? 0 : 1)
  }
  const cwd = process.cwd()
  const abs = resolve(cwd, path)
  const report = await installPackFile(cwd, abs, {
    ...(runtime ? { runtime } : {}),
    noBootstrap,
    captureAs,
    modules,
    onConflict,
  })
  console.log(JSON.stringify(report, null, 2))
  if (!report.ok) process.exit(1)
  console.log(`\nOK: install → [${report.projected.join(', ')}]`)
}

async function cmdPack(args: string[]): Promise<void> {
  const { runtime, name, help } = parseArgs(args)
  const install = hasFlag(args, '--install', '-i')
  const common = exportCommonArgs(args)
  let manifest: string | undefined
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--manifest' || args[i] === '-m') manifest = args[++i]
  }
  const skills = collectListArg(args, '--skills')
  const rules = collectListArg(args, '--rules')
  const mcp = collectListArg(args, '--mcp')
  const withHarness = hasFlag(args, '--harness', '--with-harness')

  if (help) {
    console.log(`Usage: agent-pack pack --agent <id> [--install]
       agent-pack pack --manifest select.json [--install]
       agent-pack pack --skills a,b [--install]`)
    process.exit(0)
  }

  const cwd = process.cwd()
  const { packFromProject } = await import('./pack.js')

  let select: string | PackSelectManifest | undefined
  if (manifest) {
    select = manifest
  } else if (skills?.length || rules?.length || mcp?.length || withHarness) {
    select = {
      name,
      ...(skills?.length ? { skills } : {}),
      ...(rules?.length ? { rules } : {}),
      ...(mcp?.length ? { mcp } : {}),
      ...(withHarness ? { harness: true, captureAs: 'skill' as const } : {}),
      ...(common.captureAs && !withHarness ? { captureAs: common.captureAs } : {}),
    }
  }

  const report = await packFromProject(cwd, {
    runtime,
    name,
    select,
    withHarness,
    install,
    ...common,
  })
  console.error(`[pack] ${report.exportPath}`)
  if ('stats' in report && report.stats) {
    console.error(`  fidelity=${report.stats.fidelity} skills=${report.stats.skills} rules=${report.stats.rules}`)
  }
  console.log(JSON.stringify(report, null, 2))
  if ('ok' in report && !report.ok) process.exit(1)
  if (install && 'projected' in report) console.log(`\nOK: pack+install → [${report.projected.join(', ')}]`)
  else console.log(`\nOK: packed → ${report.exportPath}`)
}

async function cmdDiff(args: string[]): Promise<void> {
  const { path: left, help } = parseArgs(args)
  let right: string | undefined
  for (let i = 0; i < args.length; i++) {
    if (!args[i].startsWith('-') && args[i] !== left) {
      right = args[i]
      break
    }
  }
  if (help || !left || !right) {
    console.log('Usage: agent-pack diff <lock|pack-a> <lock|pack-b>')
    process.exit(help ? 0 : 1)
  }
  const { diffLockFiles, diffPackFiles, formatDiffReport } = await import('./diff.js')
  const isLock = (p: string) => p.endsWith('lock.json') || p.includes('lock.json')
  const report =
    isLock(left) && isLock(right)
      ? await diffLockFiles(resolve(process.cwd(), left), resolve(process.cwd(), right))
      : await diffPackFiles(resolve(process.cwd(), left), resolve(process.cwd(), right))
  console.log(formatDiffReport(report))
}

async function cmdEject(args: string[]): Promise<void> {
  const { name, help } = parseArgs(args)
  let force = hasFlag(args, '--force')
  if (help) {
    console.log('Usage: agent-pack eject [--name pack] [--force]')
    console.log('  Uninstall using install-ledger; missing files reported, not fatal.')
    process.exit(0)
  }
  const { ejectPack } = await import('./eject.js')
  const report = await ejectPack(process.cwd(), { packName: name, force })
  console.log(JSON.stringify(report, null, 2))
  if (report.summary.conflict > 0 && !force) {
    console.error('\nConflicts — use --force or remove manually.')
    process.exit(1)
  }
}

async function cmdStatus(args: string[]): Promise<void> {
  if (hasFlag(args, '--help', '-h')) {
    console.log('Usage: agent-pack status')
    process.exit(0)
  }
  const { packStatus } = await import('./eject.js')
  console.log(JSON.stringify(await packStatus(process.cwd()), null, 2))
}

async function cmdAgents(args: string[]): Promise<void> {
  const sub = args[0]
  const cwd = process.cwd()
  const {
    loadAgentsRegistry,
    listAgentProfiles,
    ensureAgentsYamlTemplate,
    agentsYamlPath,
  } = await import('./agents.js')

  if (sub === 'init') {
    const path = await ensureAgentsYamlTemplate(cwd)
    console.log(`Created template: ${path}`)
    return
  }

  const registry = await loadAgentsRegistry(cwd)
  if (!registry) {
    console.log('No .agent-pack/agents.yaml — run: agent-pack agents init')
    process.exit(sub === 'list' ? 0 : 1)
  }

  const profiles = listAgentProfiles(registry)
  if (sub === 'list' || !sub || sub.startsWith('-')) {
    for (const p of profiles) {
      const parts = [
        p.id,
        p.author ? `@${p.author}` : '',
        p.description ? `— ${p.description}` : '',
      ].filter(Boolean)
      const inc: string[] = []
      if (p.skills?.length) inc.push(`skills:${Array.isArray(p.skills) ? p.skills.join(',') : '*'}`)
      if (p.rules?.length) inc.push(`rules:${Array.isArray(p.rules) ? p.rules.join(',') : '*'}`)
      if (p.mcp?.length) inc.push(`mcp:${Array.isArray(p.mcp) ? p.mcp.join(',') : '*'}`)
      console.log(`${parts.join(' ')}  [${inc.join(' ')}]`)
    }
    console.log(`\nfile: ${agentsYamlPath(cwd)}`)
    return
  }

  console.log(`Unknown agents subcommand: ${sub}. Use: agents list | agents init`)
  process.exit(1)
}

async function main(): Promise<void> {
  if (!cmd || cmd === '--help' || cmd === '-h') {
    printGlobalHelp()
    process.exit(0)
  }

  switch (cmd) {
    case 'agents':
      await cmdAgents(rest)
      break
    case 'eject':
      await cmdEject(rest)
      break
    case 'status':
      await cmdStatus(rest)
      break
    case 'diff':
      await cmdDiff(rest)
      break
    case 'pack':
      await cmdPack(rest)
      break
    case 'sync':
      await cmdSync(rest)
      break
    case 'export':
      await cmdExport(rest)
      break
    case 'install':
      await cmdInstall(rest)
      break
    case 'detect':
      await cmdDetect()
      break
    default:
      printGlobalHelp()
      console.error(`\nUnknown command: ${cmd}`)
      process.exit(1)
  }
}

main().catch(e => {
  if (e instanceof PackConflictError) {
    console.error(e.message)
    process.exit(1)
  }
  console.error((e as Error).message)
  process.exit(1)
})
