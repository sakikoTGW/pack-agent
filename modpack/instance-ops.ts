/**
 * clone / rename / pin / display。克隆拷整份 home，工作区指回原路径。
 */
import { cpSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { withInstanceLock } from './jobs.js'
import {
  assertUniqueHome,
  getInstance,
  LauncherError,
  slugify,
  verifyVersion,
  writeInstance,
  type Diagnostic,
  type InstanceRecord,
  type LauncherRoot,
} from './launcher.js'

export type OpResult<T> = { value: T; warnings: Diagnostic[] }

function nowIso(): string {
  return new Date().toISOString()
}

function writeSessionPatch(home: string): void {
  const sessionsRoot = join(home, 'sessions')
  writeFileSync(
    join(home, 'launcher.patch.yml'),
    `- id: session-persistence-jsonl\n  config:\n    root: '${sessionsRoot.replace(/\\/g, '\\\\')}'\n`,
  )
}

function patchIdsFromYml(yml: string): string[] {
  const ids: string[] = []
  for (const line of yml.split(/\r?\n/)) {
    const m = line.match(/^- id:\s*(\S+)/)
    if (m) ids.push(m[1].replace(/['"]/g, ''))
  }
  return ids
}

export function overlayWarnings(root: LauncherRoot, inst: InstanceRecord): Diagnostic[] {
  const patch = join(inst.home, 'launcher.patch.yml')
  if (!existsSync(patch)) return []
  const ids = patchIdsFromYml(readFileSync(patch, 'utf8'))
  const verPath = join(root.path, 'versions', inst.dsh.version, 'version.json')
  const snap = existsSync(verPath)
    ? (JSON.parse(readFileSync(verPath, 'utf8')) as { patchRowIds?: string[] })
    : {}
  const rows = Array.isArray(snap.patchRowIds) ? snap.patchRowIds : ['session-persistence-jsonl']
  const warnings: Diagnostic[] = []
  for (const id of ids) {
    if (!rows.includes(id)) {
      warnings.push({
        code: 'PA107',
        level: 'warning',
        message: `overlay patch target \`${id}\` is missing from pinned release patchRowIds`,
        location: `instance \`${inst.id}\` / launcher.patch.yml`,
        help: [`packagent dsh launcher version verify ${inst.dsh.version}`, 'pin a release that still has this row id'],
      })
    }
  }
  return warnings
}

export function cloneInstance(root: LauncherRoot, id: string, newName: string): InstanceRecord {
  return withInstanceLock(root, id, 'clone', () => {
    const src = getInstance(root, id)
    const newId = slugify(newName)
    const destDir = join(root.path, 'instances', newId)
    if (existsSync(join(destDir, 'instance.json'))) {
      throw new LauncherError(`instance \`${newId}\` already exists`)
    }
    const home = join(destDir, 'home')
    assertUniqueHome(root, home)
    mkdirSync(join(destDir, 'logs'), { recursive: true })
    mkdirSync(home, { recursive: true })
    if (existsSync(src.home)) {
      cpSync(src.home, home, { recursive: true })
    }
    writeSessionPatch(home)
    const rec: InstanceRecord = {
      ...src,
      id: newId,
      name: newName,
      home: resolve(home),
      workspace: { kind: 'existing', path: src.workspace.path },
      profile: { ...src.profile, port: 'auto' },
      status: 'ready',
      adopted: undefined,
      created: nowIso(),
      updated: nowIso(),
    }
    writeInstance(root, rec)
    return getInstance(root, newId)
  })
}

export function renameInstance(root: LauncherRoot, id: string, newName: string): InstanceRecord {
  const rec = getInstance(root, id)
  rec.name = newName
  writeInstance(root, rec)
  return rec
}

export function setInstanceDisplay(
  root: LauncherRoot,
  id: string,
  patch: Partial<{ info: string; logo: string | null; star: boolean; category: string }>,
): InstanceRecord {
  const rec = getInstance(root, id)
  rec.display = {
    info: rec.display?.info ?? '',
    logo: rec.display?.logo ?? null,
    star: rec.display?.star ?? false,
    category: rec.display?.category ?? '',
    ...patch,
  }
  writeInstance(root, rec)
  return rec
}

export function pinInstance(root: LauncherRoot, id: string, version: string): OpResult<InstanceRecord> {
  verifyVersion(root, version)
  const rec = getInstance(root, id)
  rec.dsh.version = version
  rec.pinnedAt = nowIso()
  writeInstance(root, rec)
  const warnings: Diagnostic[] = [
    {
      code: 'PA103',
      level: 'warning',
      message: 'pinned release changed; plugins have not been re-verified',
      location: `instance \`${id}\` / dsh.version`,
      help: ['packagent dsh launcher plugin list ' + id],
    },
    ...overlayWarnings(root, rec),
  ]
  return { value: rec, warnings }
}
