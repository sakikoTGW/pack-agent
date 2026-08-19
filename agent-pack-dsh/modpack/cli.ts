#!/usr/bin/env bun
/**
 * packagent dsh — 投影 + 注册表，不把整合包装进 DSH plugin 栈。
 *
 *   packagent dsh compile <pack.json|pack.zip> [--out dir]
 *   packagent dsh ours [--out dir]
 *   packagent dsh map <pack.json|pack.zip> [--cwd dir] [--from dir] [--allow]
 *   packagent dsh index [--cwd dir]
 *   packagent dsh search <query> [--cwd dir]
 *   packagent dsh allow <id> [--cwd dir]
 *   packagent dsh deny <id> [--cwd dir]
 *   packagent dsh list [--cwd dir] [--enabled]
 *   packagent dsh snapshot [--cwd dir]
 *   packagent dsh set-save <name> [--cwd dir]
 *   packagent dsh set-load <name> [--cwd dir]
 *   packagent dsh set-list [--cwd dir]
 *   packagent dsh launcher …
 */
import { mkdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { compileOursFromRepo, compilePackToDshBundle, npmNameForPack } from './compile.js'
import {
  catalogAllow,
  catalogDeny,
  catalogIndex,
  catalogList,
  catalogSearch,
  catalogSetList,
  catalogSetLoad,
  catalogSetSave,
  catalogSnapshot,
  mapPackToDsh,
  projectPack,
} from './catalog.js'
import { isPackZipPath, readPackZip } from '../../src/pack-archive.js'
import type { PackDoc } from '../../src/types.js'

function fail(msg: string): never {
  console.error(msg)
  process.exit(1)
}

function printHelp(): void {
  console.log(`Usage:
  packagent dsh compile <pack.json|pack.zip> [--out dir]
  packagent dsh ours [--out dir]
  packagent dsh project <pack.json|pack.zip> [--cwd dir] [--allow]
  packagent dsh map <pack.json|pack.zip> [--cwd dir] [--from dir] [--allow]
  packagent dsh index [--cwd dir]
  packagent dsh search <query> [--cwd dir]
  packagent dsh allow <id> [--cwd dir]
  packagent dsh deny <id> [--cwd dir]
  packagent dsh list [--cwd dir] [--enabled]
  packagent dsh snapshot [--cwd dir]
  packagent dsh set-save <name> [--cwd dir]
  packagent dsh set-load <name> [--cwd dir]
  packagent dsh set-list [--cwd dir]
  packagent dsh launcher …`)
}

function parseFlags(args: string[]): {
  positional: string[]
  out?: string
  cwd?: string
  from?: string
  help: boolean
  allow: boolean
  enabled: boolean
} {
  const positional: string[] = []
  let out: string | undefined
  let cwd: string | undefined
  let from: string | undefined
  let help = false
  let allow = false
  let enabled = false
  for (let i = 0; i < args.length; i++) {
    const a = args[i]
    if (a === '--help' || a === '-h') help = true
    else if (a === '--out') out = args[++i]
    else if (a === '--cwd') cwd = args[++i]
    else if (a === '--from') from = args[++i]
    else if (a === '--allow') allow = true
    else if (a === '--enabled') enabled = true
    else positional.push(a)
  }
  return { positional, out, cwd, from, help, allow, enabled }
}

async function loadPack(path: string): Promise<PackDoc> {
  const abs = resolve(process.cwd(), path)
  if (isPackZipPath(abs)) {
    const { pack, extractedRoot } = await readPackZip(abs)
    const { rm } = await import('node:fs/promises')
    await rm(extractedRoot, { recursive: true, force: true }).catch(() => {})
    return pack
  }
  return JSON.parse(readFileSync(abs, 'utf8')) as PackDoc
}

function workspaceOf(cwd?: string): string {
  return resolve(process.cwd(), cwd || '.')
}

export async function runDshCli(args: string[]): Promise<void> {
  const cmd = args[0]
  if (cmd === 'launcher') {
    const { runLauncherCli } = await import('./launcher.js')
    await runLauncherCli(args.slice(1))
    return
  }
  const { positional, out, cwd, from, help, allow, enabled } = parseFlags(args.slice(1))
  if (!cmd || help || cmd === '--help' || cmd === '-h') {
    printHelp()
    process.exit(cmd && !help ? 1 : 0)
  }

  if (cmd === 'compile') {
    const packPath = positional[0]
    if (!packPath) fail('Usage: packagent dsh compile <pack.json|pack.zip> [--out dir]')
    const pack = await loadPack(packPath)
    const fallback = join(process.cwd(), '.agent-pack', 'modpacks', npmNameForPack(pack.name || 'pack'))
    const dest = resolve(process.cwd(), out || fallback)
    mkdirSync(dirname(dest), { recursive: true })
    const result = await compilePackToDshBundle(pack, dest)
    console.log(result.dir)
    console.log(result.installCommand)
    return
  }

  if (cmd === 'ours') {
    const repoRoot = resolve(import.meta.dirname, '../..')
    const fallback = join(repoRoot, '.agent-pack', 'modpacks', npmNameForPack('packer'))
    const dest = resolve(process.cwd(), out || fallback)
    mkdirSync(dirname(dest), { recursive: true })
    const result = await compileOursFromRepo(repoRoot, dest)
    console.log(result.dir)
    console.log(result.installCommand)
    return
  }

  if (cmd === 'project') {
    const packPath = positional[0]
    if (!packPath) fail('Usage: packagent dsh project <pack.json|pack.zip> [--cwd dir] [--from dir] [--allow]')
    const pack = await loadPack(packPath)
    const result = await projectPack(pack, workspaceOf(cwd), { allow, from: from ? resolve(process.cwd(), from) : undefined })
    console.log(result.dir)
    console.log(result.id)
    console.log(result.installCommand)
    return
  }

  if (cmd === 'map') {
    const packPath = positional[0]
    if (!packPath) fail('Usage: packagent dsh map <pack.json|pack.zip> [--cwd dir] [--from dir] [--allow]')
    const pack = await loadPack(packPath)
    const result = await mapPackToDsh(pack, workspaceOf(cwd), {
      allow,
      from: from ? resolve(process.cwd(), from) : undefined,
    })
    console.log(result.dir)
    console.log(result.id)
    console.log(result.installCommand)
    return
  }

  if (cmd === 'index') {
    const r = await catalogIndex(workspaceOf(cwd))
    console.log(JSON.stringify(r))
    return
  }

  if (cmd === 'search') {
    const query = positional.join(' ').trim()
    if (!query) fail('Usage: packagent dsh search <query> [--cwd dir]')
    const hits = await catalogSearch(workspaceOf(cwd), query)
    console.log(JSON.stringify(hits, null, 2))
    return
  }

  if (cmd === 'allow') {
    const id = positional[0]
    if (!id) fail('Usage: packagent dsh allow <id> [--cwd dir]')
    await catalogAllow(workspaceOf(cwd), id)
    console.log(JSON.stringify({ ok: true, id, enabled: true }))
    return
  }

  if (cmd === 'deny') {
    const id = positional[0]
    if (!id) fail('Usage: packagent dsh deny <id> [--cwd dir]')
    await catalogDeny(workspaceOf(cwd), id)
    console.log(JSON.stringify({ ok: true, id, enabled: false }))
    return
  }

  if (cmd === 'list') {
    const packs = await catalogList(workspaceOf(cwd), enabled)
    console.log(JSON.stringify(packs, null, 2))
    return
  }

  if (cmd === 'snapshot') {
    const snap = await catalogSnapshot(workspaceOf(cwd))
    console.log(JSON.stringify(snap, null, 2))
    return
  }

  if (cmd === 'set-save') {
    const name = positional[0]
    if (!name) fail('Usage: packagent dsh set-save <name> [--cwd dir]')
    await catalogSetSave(workspaceOf(cwd), name)
    console.log(JSON.stringify({ ok: true, name, saved: true }))
    return
  }

  if (cmd === 'set-load') {
    const name = positional[0]
    if (!name) fail('Usage: packagent dsh set-load <name> [--cwd dir]')
    await catalogSetLoad(workspaceOf(cwd), name)
    console.log(JSON.stringify({ ok: true, name, loaded: true }))
    return
  }

  if (cmd === 'set-list') {
    const sets = await catalogSetList(workspaceOf(cwd))
    console.log(JSON.stringify(sets, null, 2))
    return
  }

  fail(`Unknown dsh subcommand: ${cmd}. Use: compile | ours | project | map | index | search | allow | deny | list | snapshot | set-save | set-load | set-list | launcher`)
}

if (import.meta.main) {
  await runDshCli(process.argv.slice(2))
}
