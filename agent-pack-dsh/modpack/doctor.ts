/**
 * launcher doctor：全表 schema / 闭包 / 环 / 原语能否解析。JSON 不准写检查脚本。
 */
import { spawnSync } from 'node:child_process'
import { statSync } from 'node:fs'
import { join } from 'node:path'
import { ENGINE_HANDLERS, ENGINE_PRIMITIVES } from './engine-primitives.js'
import { builtinRegistryDir, loadRegistryStore, RegistryError, type RegistryWarning } from './registry-store.js'
import { listInstances, renderDiag, type LauncherRoot } from './launcher.js'
import { checkAdoptedHome } from './adopt.js'

export type DoctorError = { code: string; message: string; location: string }

export type DoctorReport = {
  ok: boolean
  root: string
  node: string
  pnpm: string | null
  writable: boolean
  primitives: string[]
  registry: { names: string[] }
  warnings: RegistryWarning[]
  errors: DoctorError[]
}

export function doctorLauncher(root: LauncherRoot, opts?: { builtinDir?: string }): DoctorReport {
  const errors: DoctorError[] = []
  const node = spawnSync('node', ['--version'], { encoding: 'utf8' })
  const pnpm = spawnSync('pnpm', ['--version'], { encoding: 'utf8', shell: process.platform === 'win32' })
  const pnpmVer = pnpm.status === 0 ? (pnpm.stdout || '').trim() : null
  if (!pnpmVer) {
    errors.push({ code: 'PA011', message: 'pnpm not found on PATH', location: 'doctor' })
  }
  let warnings: RegistryWarning[] = []
  const names: string[] = []
  try {
    const store = loadRegistryStore({
      builtinDir: opts?.builtinDir || builtinRegistryDir(),
      userDir: join(root.path, 'library', 'registries'),
    })
    warnings = store.warnings
    for (const name of ['format-sniff', 'task-kinds', 'pa-codes', 'profiles', 'crash-rules', 'faq', 'migrate', 'compat-snapshot-spec']) {
      if (store.entries(name).length) names.push(name)
    }
    for (const ent of store.entries('format-sniff')) {
      const handler = String(ent.body.handler || '')
      if (handler && !ENGINE_HANDLERS.includes(handler as (typeof ENGINE_HANDLERS)[number])) {
        errors.push({
          code: 'PA018',
          message: `runtime referenced an id that is not in the merged registry`,
          location: `format-sniff/${ent.id} handler ${handler}`,
        })
      }
    }
    for (const ent of store.entries('task-kinds')) {
      const raw = ent.body.steps
      if (!Array.isArray(raw)) continue
      for (const row of raw) {
        const prim = row && typeof row === 'object' ? String((row as { primitive?: string }).primitive || '') : ''
        if (!prim) continue
        if (!ENGINE_PRIMITIVES.includes(prim as (typeof ENGINE_PRIMITIVES)[number])) {
          errors.push({
            code: 'PA018',
            message: `runtime referenced an id that is not in the merged registry`,
            location: `task-kinds/${ent.id} primitive ${prim}`,
          })
        }
      }
    }
  } catch (e) {
    if (e instanceof RegistryError) {
      errors.push({ code: e.code, message: e.message, location: 'registry' })
    } else {
      errors.push({ code: 'PA017', message: String(e), location: 'registry' })
    }
  }
  for (const inst of listInstances(root)) {
    if (!inst.adopted) continue
    const w = checkAdoptedHome(root, inst.id)
    if (w) {
      warnings.push({ code: w.code, message: w.message, id: inst.id })
      process.stderr.write(renderDiag(w))
    }
  }
  return {
    ok: errors.length === 0,
    root: root.path,
    node: (node.stdout || '').trim(),
    pnpm: pnpmVer,
    writable: !statSync(root.path).isFile(),
    primitives: [...ENGINE_PRIMITIVES],
    registry: { names },
    warnings,
    errors,
  }
}
