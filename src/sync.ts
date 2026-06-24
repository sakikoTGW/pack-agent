/**
 * 全自动：扫描/读 pack → 便携化 → 检测 harness → 投射安装。
 */
import { resolve } from 'node:path'
import { promises as fs } from 'node:fs'
import type { InstallOpts, InstallReport, PackDoc } from './types.js'
import { exportPackFromProject, type ExportOpts } from './export.js'
import { installPack } from './install.js'
import { readPackFile } from './portable.js'
import { DEFAULT_STATE_DIR } from './project.js'
import { injectBootstrapIntoPack } from './bootstrap.js'
import { writePackLock } from './lock.js'
import { enrichPackVersions, PACK_SCHEMA_V02 } from './versioning.js'
import { loadPackProjectConfig } from './project-config.js'

export type SyncOpts = InstallOpts &
  ExportOpts & {
    from?: string
    /** pack 命令：封包后是否立即 install */
    install?: boolean
  }

export type SyncReport = InstallReport & {
  exportPath?: string
  exported?: boolean
  stats?: Record<string, unknown>
  lockPath?: string
}

/**
 * 一条命令走完：打包 + 安装。
 * - 无 `from`：扫描当前目录 → 写 .agent-pack/exports/*.pack.json → 装到在场 harness
 * - 有 `from`：读 pack → 装到在场 harness
 */
export async function syncPack(cwd: string, opts: SyncOpts = {}): Promise<SyncReport> {
  const stateDir = opts.stateDir ?? DEFAULT_STATE_DIR
  let pack: PackDoc
  let exportPath: string | undefined
  let stats: Record<string, unknown> | undefined
  let exported = false

  let lockPath: string | undefined

  if (opts.from) {
    exportPath = resolve(cwd, opts.from)
    pack = await readPackFile(exportPath)
    if (!opts.noBootstrap) {
      pack = await injectBootstrapIntoPack(pack, cwd)
    }
    const projectCfg = await loadPackProjectConfig(cwd, stateDir)
    pack = await enrichPackVersions(cwd, pack)
    pack.schema = projectCfg.packSchema ?? PACK_SCHEMA_V02
    lockPath = await writePackLock(cwd, pack, stateDir)
  } else {
    const ex = await exportPackFromProject(cwd, { ...opts, stateDir })
    pack = ex.pack
    exportPath = ex.outPath
    stats = ex.stats
    lockPath = ex.lockPath
    exported = true
  }

  const install = await installPack(cwd, pack, { ...opts, stateDir })
  return { ...install, exportPath, exported, stats, lockPath }
}
