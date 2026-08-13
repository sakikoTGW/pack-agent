/**
 * 整合包库 —— 像启动器的"我的整合包"列表：这个项目导出过哪些包、装了哪些包。
 * 纯读盘汇总，不改任何状态。
 */
import { promises as fs } from 'node:fs'
import { basename, join } from 'node:path'
import { listInstallLedgers, type InstallLedger } from './install-ledger.js'
import { readPackLock } from './lock.js'
import { readPackFile } from './portable.js'

export type ExportedPackSummary = {
  name: string
  version?: string
  agentId?: string
  skills: number
  rules: number
  mcp: number
  path: string
  exportedAt?: string
  fidelity?: string
}

export type InstalledPackSummary = {
  name: string
  version?: string
  installedAt: string
  itemCount: number
  runtimes: string[]
  captureDeliver?: string
  ledgerPath: string
  isCurrentLock: boolean
}

export type PackCatalog = {
  cwd: string
  exported: ExportedPackSummary[]
  installed: InstalledPackSummary[]
  currentLockPack?: string
  /** 打不开/解析失败的 ledger 或 pack 文件——不会静默从列表消失，至少告诉你哪份坏了 */
  warnings: string[]
}

async function listPackFiles(dir: string): Promise<string[]> {
  try {
    const names = await fs.readdir(dir)
    const zips = names.filter(n => n.endsWith('.pack.zip')).map(n => join(dir, n))
    const jsons = names.filter(n => n.endsWith('.pack.json')).map(n => join(dir, n))
    // 同名优先 zip
    const byStem = new Map<string, string>()
    for (const f of jsons) {
      const stem = basename(f).replace(/\.pack\.json$/i, '')
      byStem.set(stem, f)
    }
    for (const f of zips) {
      const stem = basename(f).replace(/\.pack\.zip$/i, '')
      byStem.set(stem, f)
    }
    return [...byStem.values()]
  } catch {
    return []
  }
}

export async function listExportedPacks(
  cwd: string,
  stateDir = '.agent-pack',
): Promise<{ packs: ExportedPackSummary[]; warnings: string[] }> {
  const files = await listPackFiles(join(cwd, stateDir, 'exports'))
  const packs: ExportedPackSummary[] = []
  const warnings: string[] = []
  for (const f of files) {
    try {
      const pack = await readPackFile(f)
      packs.push({
        name: pack.name || 'unnamed-pack',
        version: pack.version,
        agentId: pack.agent?.id,
        skills: pack.knowledge?.skills?.length ?? 0,
        rules: pack.knowledge?.rules?.length ?? 0,
        mcp: pack.tools?.mcp?.length ?? 0,
        path: f,
        exportedAt: pack.meta?.exportedAt as string | undefined,
        fidelity: pack.meta?.fidelity as string | undefined,
      })
    } catch (e) {
      warnings.push(`${f}: ${(e as Error).message}`)
    }
  }
  return { packs: packs.sort((a, b) => a.name.localeCompare(b.name)), warnings }
}

function runtimesOf(ledger: InstallLedger): string[] {
  return [...new Set(ledger.items.map(i => i.runtime).filter(Boolean) as string[])]
}

export async function listInstalledPacks(
  cwd: string,
  stateDir = '.agent-pack',
): Promise<{ packs: InstalledPackSummary[]; warnings: string[] }> {
  const [{ ledgers, warnings }, lock] = await Promise.all([
    listInstallLedgers(cwd, stateDir),
    readPackLock(cwd, stateDir),
  ])
  const packs = ledgers.map(l => ({
    name: l.packName,
    version: l.packVersion,
    installedAt: l.installedAt,
    itemCount: l.items.length,
    runtimes: runtimesOf(l),
    captureDeliver: l.captureDeliver,
    ledgerPath: join(cwd, stateDir, 'applied', `${l.packName.replace(/[^\w.-]+/g, '_')}-ledger.json`),
    isCurrentLock: lock?.packName === l.packName,
  }))
  return { packs, warnings: warnings.map(w => `${w.path}: ${w.error}`) }
}

export async function buildPackCatalog(cwd: string, stateDir = '.agent-pack'): Promise<PackCatalog> {
  const [exportedRes, installedRes, lock] = await Promise.all([
    listExportedPacks(cwd, stateDir),
    listInstalledPacks(cwd, stateDir),
    readPackLock(cwd, stateDir),
  ])
  return {
    cwd,
    exported: exportedRes.packs,
    installed: installedRes.packs,
    currentLockPack: lock?.packName,
    warnings: [...exportedRes.warnings, ...installedRes.warnings],
  }
}

export function formatPackCatalog(catalog: PackCatalog): string {
  const lines: string[] = []
  lines.push(`Installed packs in this project (${catalog.installed.length}):`)
  if (!catalog.installed.length) {
    lines.push('  (none — run `agent-pack install <pack.json>` or `agent-pack sync`)')
  } else {
    for (const p of catalog.installed) {
      const mark = p.isCurrentLock ? '*' : ' '
      lines.push(
        `  ${mark} ${p.name}${p.version ? ` v${p.version}` : ''} — ${p.itemCount} items on [${p.runtimes.join(', ') || '?'}], installed ${p.installedAt}`,
      )
    }
    lines.push('  (* = matches .agent-pack/lock.json, i.e. last install/export)')
  }
  lines.push('')
  lines.push(`Exported packs available (${catalog.exported.length}):`)
  if (!catalog.exported.length) {
    lines.push('  (none — run `agent-pack export --agent <id>`)')
  } else {
    for (const p of catalog.exported) {
      lines.push(
        `    ${p.name}${p.version ? ` v${p.version}` : ''} [${p.fidelity ?? 'L1'}] — skills=${p.skills} rules=${p.rules} mcp=${p.mcp} — ${p.path}`,
      )
    }
  }
  if (catalog.warnings.length) {
    lines.push('')
    lines.push(`⚠ ${catalog.warnings.length} file(s) could not be read (shown below are NOT missing — they exist but are corrupt/unreadable, fix or delete them):`)
    for (const w of catalog.warnings) lines.push(`    ${w}`)
  }
  return lines.join('\n')
}
