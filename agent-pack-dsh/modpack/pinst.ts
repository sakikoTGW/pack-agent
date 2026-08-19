/**
 * 实例导出/导入 *.pinst.zip。剥 .credentials.yaml，不还原。端口清空。
 */
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs'
import { basename, join } from 'node:path'
import { packTmpRoot } from '../../src/tmp-root.js'
import { writeZipFromDir } from '../../src/pack-archive.js'
import { catalogSetList } from './catalog.js'
import {
  getInstance,
  instanceHome,
  renderDiag,
  type InstanceRecord,
  type LauncherRoot,
} from './launcher.js'

export type PinstManifest = {
  schema: 'pack-agent.pinst/v1'
  exportedAt: string
  stripped: string[]
  instance: { id: string; name: string; version: string; profile: string }
  allowSets?: { active: string; names: string[] }
}

function extractZip(zipPath: string, dest: string): void {
  mkdirSync(dest, { recursive: true })
  const r = Bun.spawnSync(['tar', '-xf', zipPath, '-C', dest], { stdout: 'pipe', stderr: 'pipe' })
  if (r.exitCode !== 0) {
    throw new Error(`tar xf failed: ${r.stderr.toString() || r.stdout.toString()}`)
  }
}

export async function exportInstance(
  root: LauncherRoot,
  id: string,
  opts: { out?: string } = {},
): Promise<{ path: string; stripped: string[]; manifest: PinstManifest }> {
  const inst = getInstance(root, id)
  const stage = join(packTmpRoot(), `pinst-out-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`)
  mkdirSync(stage, { recursive: true })
  const stripped: string[] = []
  const homeSrc = instanceHome(root, id)
  const homeDest = join(stage, 'home')
  if (existsSync(homeSrc)) {
    cpSync(homeSrc, homeDest, {
      recursive: true,
      filter: (src) => {
        if (basename(src) === '.credentials.yaml') {
          stripped.push('credentials')
          return false
        }
        return true
      },
    })
  }
  writeFileSync(join(stage, 'instance.json'), JSON.stringify(inst, null, 2))
  const packDir = join(inst.workspace.path, '.agent-pack')
  if (existsSync(packDir)) {
    cpSync(packDir, join(stage, 'workspace', '.agent-pack'), { recursive: true })
  }
  let allowSets: PinstManifest['allowSets']
  try {
    const listed = await catalogSetList(inst.workspace.path)
    allowSets = { active: listed.active, names: listed.sets.map((s) => s.name) }
  } catch {
    /* no catalog yet */
  }
  const manifest: PinstManifest = {
    schema: 'pack-agent.pinst/v1',
    exportedAt: new Date().toISOString(),
    stripped: [...new Set(stripped)],
    instance: {
      id: inst.id,
      name: inst.name,
      version: inst.dsh.version,
      profile: inst.profile.name,
    },
    allowSets,
  }
  writeFileSync(join(stage, 'manifest.json'), JSON.stringify(manifest, null, 2))
  const out = opts.out || join(root.path, `${inst.id}.pinst.zip`)
  await writeZipFromDir(stage, out)
  rmSync(stage, { recursive: true, force: true })
  if (manifest.stripped.includes('credentials')) {
    process.stderr.write(
      renderDiag({
        code: 'PA104',
        level: 'warning',
        message: 'export stripped credentials',
        location: `instance \`${id}\``,
      }),
    )
  }
  return { path: out, stripped: manifest.stripped, manifest }
}

export function readPinstZip(zipPath: string): {
  root: string
  manifest: PinstManifest
  instance: InstanceRecord
} {
  const dest = join(packTmpRoot(), `pinst-in-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`)
  extractZip(zipPath, dest)
  const manPath = join(dest, 'manifest.json')
  if (!existsSync(manPath)) {
    throw new Error('missing manifest.json')
  }
  const manifest = JSON.parse(readFileSync(manPath, 'utf8')) as PinstManifest
  const instPath = join(dest, 'instance.json')
  const instance = existsSync(instPath)
    ? (JSON.parse(readFileSync(instPath, 'utf8')) as InstanceRecord)
    : ({} as InstanceRecord)
  return { root: dest, manifest, instance }
}

export function restorePinstHome(pinstRoot: string, destHome: string, destWorkspace: string): void {
  const srcHome = join(pinstRoot, 'home')
  if (existsSync(srcHome)) {
    mkdirSync(destHome, { recursive: true })
    for (const name of readdirSync(srcHome)) {
      if (name === '.credentials.yaml') continue
      cpSync(join(srcHome, name), join(destHome, name), {
        recursive: true,
        filter: (src) => basename(src) !== '.credentials.yaml',
      })
    }
  }
  const srcPack = join(pinstRoot, 'workspace', '.agent-pack')
  if (existsSync(srcPack)) {
    const destPack = join(destWorkspace, '.agent-pack')
    mkdirSync(destPack, { recursive: true })
    for (const name of readdirSync(srcPack)) {
      cpSync(join(srcPack, name), join(destPack, name), { recursive: true })
    }
  }
}
