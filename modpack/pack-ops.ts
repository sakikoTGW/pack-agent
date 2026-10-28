/**
 * 对该实例工作区走投影白名单。目录禁止 dsh plugin add。
 */
import { existsSync, readFileSync, rmSync } from 'node:fs'
import { resolve } from 'node:path'
import { readPackZip } from '../../src/pack-archive.js'
import type { PackDoc } from '../../src/types.js'
import {
  catalogAllow,
  catalogDeny,
  catalogList,
  catalogPaths,
  catalogSetList,
  catalogSetLoad,
  catalogSetSave,
  projectPack,
  type CatalogAllowSets,
  type CatalogPackRow,
  type ProjectResult,
} from './catalog.js'
import { getInstance, type LauncherRoot } from './launcher.js'

function workspaceOf(root: LauncherRoot, id: string): string {
  return getInstance(root, id).workspace.path
}

export async function packList(root: LauncherRoot, id: string): Promise<CatalogPackRow[]> {
  const ws = workspaceOf(root, id)
  const { db } = catalogPaths(ws)
  if (!existsSync(db)) return []
  return catalogList(ws)
}

export async function packProject(
  root: LauncherRoot,
  id: string,
  packPath: string,
  opts?: { allow?: boolean },
): Promise<ProjectResult> {
  const abs = resolve(packPath)
  // .pack.zip：整包解压拿到 pack.json + 实体 skills/rules + mcp.json，再把解压根当
  // projectPack 的 from（和 import 同一套 hydrate 语义：缺 bundle 时从磁盘重建）。
  // 解析/解压失败不吞，原错误抛给调用方（catalog 里 zip 行点「用这个包建实例」走这里）。
  if (abs.toLowerCase().endsWith('.pack.zip')) {
    const { pack, extractedRoot } = await readPackZip(abs)
    try {
      return await projectPack(pack, workspaceOf(root, id), { allow: opts?.allow, from: extractedRoot })
    } finally {
      rmSync(extractedRoot, { recursive: true, force: true })
    }
  }
  // .pack.json 维持原逻辑（也接住 .pinst.zip 等其它文件的原生解析错误，不静默改语义）
  const pack = JSON.parse(readFileSync(abs, 'utf8')) as PackDoc
  return projectPack(pack, workspaceOf(root, id), { allow: opts?.allow })
}

export async function packAllow(root: LauncherRoot, id: string, packId: string): Promise<void> {
  await catalogAllow(workspaceOf(root, id), packId)
}

export async function packDeny(root: LauncherRoot, id: string, packId: string): Promise<void> {
  await catalogDeny(workspaceOf(root, id), packId)
}

export async function packSetSave(root: LauncherRoot, id: string, name: string): Promise<void> {
  await catalogSetSave(workspaceOf(root, id), name)
}

export async function packSetLoad(root: LauncherRoot, id: string, name: string): Promise<void> {
  await catalogSetLoad(workspaceOf(root, id), name)
}

export async function packSetList(root: LauncherRoot, id: string): Promise<CatalogAllowSets> {
  return catalogSetList(workspaceOf(root, id))
}
