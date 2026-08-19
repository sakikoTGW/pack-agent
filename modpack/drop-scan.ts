/**
 * 启动器根目录旁路扫描 .pack.zip / .pinst.zip，成功则删包。
 */
import { existsSync, readdirSync, rmSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { importPack } from './import-pack.js'
import { LauncherError, type LauncherRoot } from './launcher.js'

export type DropScanResult = {
  imported: string[]
  failed: { path: string; code?: string; message: string }[]
}

function isDropZip(name: string): boolean {
  const n = name.toLowerCase()
  return n.endsWith('.pack.zip') || n.endsWith('.pinst.zip')
}

export async function scanDropZips(
  root: LauncherRoot,
  opts: { publishedVersions?: string[] } = {},
): Promise<DropScanResult> {
  const imported: string[] = []
  const failed: DropScanResult['failed'] = []
  if (!existsSync(root.path)) return { imported, failed }
  const names = readdirSync(root.path).filter(isDropZip).sort()
  for (const name of names) {
    const path = join(root.path, name)
    if (!statSync(path).isFile()) continue
    try {
      const inst = await importPack(root, path, { publishedVersions: opts.publishedVersions })
      rmSync(path, { force: true })
      imported.push(inst.id)
    } catch (e) {
      const err = e as LauncherError
      failed.push({
        path,
        code: err.diagnostic?.code,
        message: err.message || String(e),
      })
    }
  }
  return { imported, failed }
}
