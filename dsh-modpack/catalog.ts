/**
 * 投影根 `.agent-pack/modpacks/` + SQLite 注册表。检索/允许集走 Rust pack-index。
 */
import { spawnSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { ensurePackIndexBin } from '../scripts/build-pack-index.js'
import { packTmpRoot } from '../src/tmp-root.js'
import type { PackDoc } from '../src/types.js'
import { compilePackToDshBundle, npmNameForPack } from './compile.js'
import { hydrateLegacyPack, remapLegacyPack } from './map.js'

export type CatalogPackRow = {
  id: string
  name: string
  version: string
  description: string
  dir: string
  enabled: boolean
}

export type CatalogHit = {
  pack_id: string
  kind: string
  name: string
  path: string
  snippet: string
}

export type CatalogSkill = {
  pack_id: string
  name: string
  description: string
  path: string
}

export type CatalogSnapshot = {
  skills: CatalogSkill[]
}

export type ProjectResult = {
  dir: string
  id: string
  npmName: string
  installCommand: string
}

export function catalogPaths(workspace: string): { root: string; modpacksDir: string; db: string } {
  const root = join(workspace, '.agent-pack')
  return {
    root,
    modpacksDir: join(root, 'modpacks'),
    db: join(root, 'catalog.sqlite'),
  }
}

function runPackIndex(db: string, args: string[]): Record<string, unknown> {
  const bin = ensurePackIndexBin()
  const tmp = packTmpRoot()
  const proc = spawnSync(bin, [...args, '--db', db, '--json'], {
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024,
    env: { ...process.env, TMP: tmp, TEMP: tmp, TMPDIR: tmp },
  })
  const stdout = (proc.stdout || '').trim()
  let parsed: Record<string, unknown> = {}
  if (stdout) {
    try {
      parsed = JSON.parse(stdout) as Record<string, unknown>
    } catch {
      throw new Error(`pack-index invalid JSON: ${stdout.slice(0, 400)}\n${proc.stderr || ''}`)
    }
  }
  if (proc.status !== 0) {
    const err = (parsed.error as string | undefined) || proc.stderr || stdout || `exit ${proc.status}`
    throw new Error(String(err))
  }
  return parsed
}

export async function catalogIndex(workspace: string): Promise<{ packs: number; units: number }> {
  const { db, modpacksDir } = catalogPaths(workspace)
  mkdirSync(modpacksDir, { recursive: true })
  mkdirSync(dirname(db), { recursive: true })
  const out = runPackIndex(db, ['index', '--root', modpacksDir])
  return {
    packs: Number(out.packs || 0),
    units: Number(out.units || 0),
  }
}

export async function projectPack(
  pack: PackDoc,
  workspace: string,
  opts?: { allow?: boolean; from?: string },
): Promise<ProjectResult> {
  const ready = opts?.from
    ? await hydrateLegacyPack(pack, opts.from)
    : remapLegacyPack(pack)
  const id = npmNameForPack(ready.name || 'pack')
  const { modpacksDir } = catalogPaths(workspace)
  const dir = join(modpacksDir, id)
  const compiled = await compilePackToDshBundle(ready, dir)
  await catalogIndex(workspace)
  if (opts?.allow) await catalogAllow(workspace, id)
  return {
    dir: compiled.dir,
    id,
    npmName: compiled.npmName,
    installCommand: compiled.installCommand,
  }
}

export async function mapPackToDsh(
  pack: PackDoc,
  workspace: string,
  opts?: { allow?: boolean; from?: string },
): Promise<ProjectResult> {
  return projectPack(pack, workspace, opts)
}

export async function catalogSearch(workspace: string, query: string): Promise<CatalogHit[]> {
  const { db } = catalogPaths(workspace)
  const out = runPackIndex(db, ['search', query])
  const hits = Array.isArray(out.hits) ? out.hits : []
  return hits as CatalogHit[]
}

export async function catalogAllow(workspace: string, id: string): Promise<void> {
  const { db } = catalogPaths(workspace)
  runPackIndex(db, ['allow', id])
}

export async function catalogDeny(workspace: string, id: string): Promise<void> {
  const { db } = catalogPaths(workspace)
  runPackIndex(db, ['deny', id])
}

export async function catalogList(workspace: string, enabledOnly = false): Promise<CatalogPackRow[]> {
  const { db } = catalogPaths(workspace)
  const args = enabledOnly ? ['list', '--enabled'] : ['list']
  const out = runPackIndex(db, args)
  const packs = Array.isArray(out.packs) ? out.packs : []
  return packs as CatalogPackRow[]
}

export async function catalogSnapshot(workspace: string): Promise<CatalogSnapshot> {
  const { db } = catalogPaths(workspace)
  const out = runPackIndex(db, ['snapshot'])
  const skills = Array.isArray(out.skills) ? out.skills : []
  return { skills: skills as CatalogSkill[] }
}
