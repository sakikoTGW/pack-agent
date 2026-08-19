/**
 * 对该实例工作区走投影白名单。目录禁止 dsh plugin add。
 */
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
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

export async function packProject(root: LauncherRoot, id: string, packPath: string, opts?: { allow?: boolean }): Promise<ProjectResult> {
  const pack = JSON.parse(readFileSync(resolve(packPath), 'utf8')) as PackDoc
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
