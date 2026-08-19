/**
 * 对该实例 --profile 跑 dsh plugin。已有 session 再 add → PA021 后继续。
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import semver from 'semver'
import {
  dumpConfig,
  getInstance,
  pa,
  pluginAdd,
  runDsh,
  writeInstance,
  type Diagnostic,
  type LauncherRoot,
} from './launcher.js'
import { hasSessionArtifacts } from './session-ops.js'

export type PluginList = { bundles: string[]; disabled: string[] }

function warn(code: string, message: string, location: string, extra: Partial<Diagnostic> = {}): Diagnostic {
  return { code, level: 'warning', message, location, ...extra }
}

function readPkg(spec: string): Record<string, unknown> | null {
  const dir = resolve(spec)
  const pkg = join(dir, 'package.json')
  if (!existsSync(pkg)) return null
  try {
    return JSON.parse(readFileSync(pkg, 'utf8')) as Record<string, unknown>
  } catch {
    return null
  }
}

function parseBundles(dump: string): string[] {
  const bundles: string[] = []
  for (const line of dump.split(/\r?\n/)) {
    if (/id:\s/.test(line) && line.includes('id:')) continue
    const m = line.match(/^\s+-\s+(\S+)/)
    if (m) bundles.push(m[1])
  }
  return bundles
}

function disabledPath(home: string): string {
  return join(home, 'pa-disabled.json')
}

function readDisabledFile(home: string): string[] {
  const p = disabledPath(home)
  if (!existsSync(p)) return []
  try {
    const raw = JSON.parse(readFileSync(p, 'utf8'))
    return Array.isArray(raw) ? raw.map(String) : []
  } catch {
    return []
  }
}

function writeDisabledFile(home: string, ids: string[]): void {
  writeFileSync(disabledPath(home), JSON.stringify(ids))
}

function markRestart(root: LauncherRoot, id: string): void {
  const rec = getInstance(root, id)
  if (rec.status === 'import-failed') return
  rec.status = 'restart-required'
  writeInstance(root, rec)
}

export function pluginList(root: LauncherRoot, id: string): PluginList {
  const inst = getInstance(root, id)
  const dump = dumpConfig(root, id)
  const disabled = [...new Set([...(inst.plugins?.disabled ?? []), ...readDisabledFile(inst.home)])]
  const bundles = parseBundles(dump).filter((b) => !disabled.some((d) => b.includes(d)))
  return { bundles, disabled }
}

export function pluginAddToInstance(
  root: LauncherRoot,
  id: string,
  spec: string,
): { warnings: Diagnostic[]; bundles: string[] } {
  const inst = getInstance(root, id)
  const warnings: Diagnostic[] = []
  if (hasSessionArtifacts(inst.home)) {
    warnings.push(
      warn('PA021', 'adding a bundle while sessions already exist', `instance \`${id}\` / plugin add`, {
        note: 'printed and continuing',
      }),
    )
  }
  const pkg = readPkg(spec)
  if (pkg) {
    const engines = pkg.engines && typeof pkg.engines === 'object' ? (pkg.engines as Record<string, unknown>) : null
    const range = engines && typeof engines.dsh === 'string' ? engines.dsh : ''
    if (!range) {
      warnings.push(warn('PA101', 'plugin did not declare engines.dsh', spec))
    } else if (!semver.satisfies(inst.dsh.version, range, { includePrerelease: true })) {
      throw pa('PA002', 'plugin engines.dsh does not match the pinned release', spec, {
        context: [`engines.dsh: ${range}`, `pinned: ${inst.dsh.version}`],
        help: ['pin a matching release', 'disable this plugin'],
      })
    }
    const dsh = pkg.dsh && typeof pkg.dsh === 'object' ? (pkg.dsh as Record<string, unknown>) : null
    if (!dsh || !dsh.bundle) {
      warnings.push(warn('PA105', 'dependency has no dsh.bundle; installed as a plain dependency', spec))
    }
  }
  const proc = pluginAdd(root, id, spec)
  if (proc.status !== 0) {
    throw pa('PA014', `plugin add failed: ${spec}`, `instance \`${id}\``, {
      context: [(proc.stderr || proc.stdout || '').slice(0, 400)],
    })
  }
  markRestart(root, id)
  return { warnings, bundles: pluginList(root, id).bundles }
}

export function pluginRemove(root: LauncherRoot, id: string, pkg: string): { warnings: Diagnostic[]; bundles: string[] } {
  const inst = getInstance(root, id)
  const proc = runDsh(root, id, ['plugin', '--profile', inst.profile.name, 'remove', pkg])
  if (proc.status !== 0) {
    throw pa('PA014', `plugin remove failed: ${pkg}`, `instance \`${id}\``, {
      context: [(proc.stderr || proc.stdout || '').slice(0, 400)],
    })
  }
  markRestart(root, id)
  return { warnings: [], bundles: pluginList(root, id).bundles }
}

export function pluginUpdate(root: LauncherRoot, id: string, pkg: string): { warnings: Diagnostic[]; bundles: string[] } {
  const inst = getInstance(root, id)
  const proc = runDsh(root, id, ['plugin', '--profile', inst.profile.name, 'update', pkg])
  if (proc.status !== 0) {
    throw pa('PA014', `plugin update failed: ${pkg}`, `instance \`${id}\``, {
      context: [(proc.stderr || proc.stdout || '').slice(0, 400)],
    })
  }
  markRestart(root, id)
  return { warnings: [], bundles: pluginList(root, id).bundles }
}

export function pluginDisable(root: LauncherRoot, id: string, pkg: string): { warnings: Diagnostic[]; bundles: string[] } {
  const inst = getInstance(root, id)
  const warnings: Diagnostic[] = []
  if (/pack-agent/i.test(pkg)) {
    warnings.push(
      warn('PA110', 'pack-agent manager was disabled', `instance \`${id}\``, {
        help: ['projected packs will not be visible until it is enabled again'],
      }),
    )
  }
  const disabled = [...new Set([...(inst.plugins?.disabled ?? []), pkg, ...readDisabledFile(inst.home)])]
  inst.plugins = { disabled }
  writeInstance(root, inst)
  writeDisabledFile(inst.home, disabled)
  markRestart(root, id)
  return { warnings, bundles: pluginList(root, id).bundles }
}

export function pluginEnable(root: LauncherRoot, id: string, pkg: string): { warnings: Diagnostic[]; bundles: string[] } {
  const inst = getInstance(root, id)
  const disabled = [...(inst.plugins?.disabled ?? []), ...readDisabledFile(inst.home)].filter((d) => d !== pkg && !pkg.includes(d) && !d.includes(pkg))
  inst.plugins = { disabled }
  writeInstance(root, inst)
  writeDisabledFile(inst.home, disabled)
  markRestart(root, id)
  return { warnings: [], bundles: pluginList(root, id).bundles }
}
