import { join, resolve } from 'node:path'
import { detectRuntimes } from '../../src/adapters.js'
import {
  catalogAllow,
  catalogDeny,
  catalogList,
  catalogSearch,
  catalogSnapshot,
  mapPackToDsh,
  projectPack,
} from '../../dsh-modpack/catalog.js'
import { compilePackToDshBundle, loadPackDoc, npmNameForPack } from '../../dsh-modpack/compile.js'

export async function actionDetect(cwd: string) {
  const detected = await detectRuntimes(cwd)
  return { ok: true as const, cwd, detected }
}

export async function actionCompile(packPath: string, outDir: string) {
  const pack = await loadPackDoc(resolve(packPath))
  const result = await compilePackToDshBundle(pack, outDir)
  return { ok: true as const, ...result }
}

export async function actionProject(packPath: string, cwd: string, allow = false, from?: string) {
  const pack = await loadPackDoc(resolve(packPath))
  const result = await projectPack(pack, cwd, { allow, from })
  return { ok: true as const, ...result }
}

export async function actionMap(packPath: string, cwd: string, opts?: { allow?: boolean; from?: string }) {
  const pack = await loadPackDoc(resolve(packPath))
  const result = await mapPackToDsh(pack, cwd, opts)
  return { ok: true as const, mappedFrom: pack.runtime?.id || 'legacy-pack', ...result }
}

export async function actionSearch(cwd: string, query: string) {
  const hits = await catalogSearch(cwd, query)
  return { ok: true as const, hits }
}

export async function actionAllow(cwd: string, id: string) {
  await catalogAllow(cwd, id)
  return { ok: true as const, id, enabled: true }
}

export async function actionDeny(cwd: string, id: string) {
  await catalogDeny(cwd, id)
  return { ok: true as const, id, enabled: false }
}

export async function actionList(cwd: string, enabledOnly = false) {
  const packs = await catalogList(cwd, enabledOnly)
  return { ok: true as const, packs }
}

export async function actionSnapshot(cwd: string) {
  const snap = await catalogSnapshot(cwd)
  return { ok: true as const, ...snap }
}

export function defaultCompileOut(cwd: string, packName: string): string {
  return join(cwd, '.agent-pack', 'modpacks', npmNameForPack(packName))
}
