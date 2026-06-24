/**
 * 选件封包：扫描 → 筛选 → 可选合并 L2 → 写盘 → 可选安装。
 */
import type { InstallReport, PackDoc } from './types.js'
import { exportPackFromProject, type ExportOpts } from './export.js'
import { installPack } from './install.js'
import type { PackSelectManifest } from './select.js'
import { DEFAULT_STATE_DIR } from './project.js'

export type PackOpts = ExportOpts & {
  install?: boolean
}

export type PackReport = InstallReport & {
  exportPath: string
  exported: true
  stats: Record<string, unknown>
  selection?: PackSelectManifest
}

export async function packFromProject(cwd: string, opts: PackOpts = {}): Promise<PackReport | (InstallReport & { exportPath: string; stats: Record<string, unknown> })> {
  const { pack, outPath, stats } = await exportPackFromProject(cwd, opts)

  if (!opts.install) {
    return { exportPath: outPath, exported: true, stats, ok: true, name: pack.name || 'pack', detected: [], projected: [], runtimes: [], skills: [], rules: [], mcp: [], skipped: [] } as PackReport
  }

  const install = await installPack(cwd, pack, { ...opts, stateDir: opts.stateDir ?? DEFAULT_STATE_DIR })
  return { ...install, exportPath: outPath, exported: true, stats }
}
