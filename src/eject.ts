/**
 * 整合包卸载（eject）—— 按 install-ledger 逐项回滚；少件报 missing，不 abort。
 */
import { promises as fs } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import type { McpFormat } from './projection.js'
import { removeHermesExternalDir, unmergeMcp } from './projection.js'
import { unwireExperienceHooks } from './experience-projection.js'
import { removeAgentPackMcpBootstrap } from './mcp-bootstrap.js'
import {
  readInstallLedger,
  packSafeName,
  ledgerPath as installLedgerPath,
  type InstallLedger,
  type InstallLedgerItem,
} from './install-ledger.js'
import { packMarker, readSkillOriginMarker, removeMarkedBlock, SKILL_ORIGIN_FILE } from './markers.js'
import { removeAgentPackMcpBootstrap, mcpBootstrapTargets } from './mcp-bootstrap.js'

export type EjectItemStatus = 'removed' | 'missing' | 'skipped' | 'conflict' | 'partial'

export type EjectItemReport = {
  kind: InstallLedgerItem['kind']
  path: string
  status: EjectItemStatus
  note?: string
}

export type EjectReport = {
  ok: boolean
  packName: string
  items: EjectItemReport[]
  summary: { removed: number; missing: number; skipped: number; conflict: number; partial: number }
  remediation: string[]
  ledgerPath?: string
}

export type EjectOpts = {
  stateDir?: string
  packName?: string
  /** 与用户改过的 skill 目录冲突时仍删除（慎用） */
  force?: boolean
  /** 保留 ledger 供审计 */
  keepLedger?: boolean
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

function absPath(cwd: string, p: string): string {
  if (/^[a-zA-Z]:/.test(p) || p.startsWith('/')) return p
  return join(cwd, p.replace(/\//g, '\\'))
}

async function ejectSkillDir(
  cwd: string,
  relPath: string,
  packName: string,
  force: boolean,
): Promise<EjectItemReport> {
  const path = absPath(cwd, relPath)
  if (!(await exists(path))) {
    return { kind: 'skill', path, status: 'missing', note: '目录已不存在（可能手动删过）' }
  }
  const origin = await readSkillOriginMarker(path)
  if (origin && origin.packName !== packName && !force) {
    return {
      kind: 'skill',
      path,
      status: 'conflict',
      note: `归属包 ${origin.packName} ≠ ${packName}；用 force 或手动处理`,
    }
  }
  if (!origin && !force) {
    return {
      kind: 'skill',
      path,
      status: 'conflict',
      note: `无 ${SKILL_ORIGIN_FILE} 标记，可能非 agent-pack 安装；用 force:eject`,
    }
  }
  try {
    await fs.rm(path, { recursive: true, force: true })
    return { kind: 'skill', path, status: 'removed' }
  } catch (e) {
    return { kind: 'skill', path, status: 'partial', note: (e as Error).message }
  }
}

async function ejectHarnessL2(cwd: string, path: string, packName: string): Promise<EjectItemReport> {
  const abs = absPath(cwd, path)
  if (path.endsWith('-harness.md') || path.includes('agent-pack-')) {
    if (!(await exists(abs))) return { kind: 'harness-l2', path, status: 'missing' }
    await fs.rm(abs, { force: true })
    return { kind: 'harness-l2', path, status: 'removed' }
  }
  const marker = packMarker('harness', packName)
  const r = await removeMarkedBlock(abs, marker)
  if (r === 'missing') return { kind: 'harness-l2', path, status: 'missing' }
  if (r === 'unchanged') return { kind: 'harness-l2', path, status: 'skipped', note: '标记块未找到' }
  return { kind: 'harness-l2', path, status: 'removed' }
}

async function ejectExperienceFiles(cwd: string, stateDir: string): Promise<EjectItemReport[]> {
  const root = join(cwd, stateDir, 'experiences')
  const items: EjectItemReport[] = []
  try {
    for (const f of await fs.readdir(root)) {
      const p = join(root, f)
      try {
        await fs.rm(p, { force: true })
        items.push({ kind: 'experience', path: p, status: 'removed' })
      } catch (e) {
        items.push({ kind: 'experience', path: p, status: 'partial', note: (e as Error).message })
      }
    }
  } catch {
    items.push({ kind: 'experience', path: root, status: 'missing', note: 'experiences 目录不存在' })
  }
  return items
}

async function ejectSidecars(cwd: string, stateDir: string): Promise<EjectItemReport[]> {
  const harnessDir = join(cwd, stateDir, 'harness')
  const items: EjectItemReport[] = []
  try {
    for (const rt of await fs.readdir(harnessDir)) {
      const p = join(harnessDir, rt)
      await fs.rm(p, { recursive: true, force: true })
      items.push({ kind: 'sidecar', path: p, status: 'removed' })
    }
  } catch {
    items.push({ kind: 'sidecar', path: harnessDir, status: 'missing' })
  }
  const proj = join(cwd, stateDir, 'applied', 'experience-projection.json')
  if (await exists(proj)) {
    await fs.rm(proj, { force: true })
    items.push({ kind: 'sidecar', path: proj, status: 'removed' })
  }
  return items
}

function summarize(items: EjectItemReport[]): EjectReport['summary'] {
  const s = { removed: 0, missing: 0, skipped: 0, conflict: 0, partial: 0 }
  for (const i of items) {
    if (i.status in s) s[i.status as keyof typeof s]++
  }
  return s
}

function buildRemediation(report: EjectReport): string[] {
  const tips: string[] = []
  if (report.summary.missing > 0) {
    tips.push('少件 ≠ 失败：ledger 里记录了应删路径，磁盘上已不存在则记 missing，其余照常卸。')
  }
  if (report.summary.conflict > 0) {
    tips.push('conflict 项（无 origin 标记或归属别的包）请人工确认后删，或 eject --force。')
  }
  if (report.summary.partial > 0) {
    tips.push('partial 项可能权限/占用；关 IDE 后重跑 pack_eject。')
  }
  if (report.summary.skipped > 0) {
    tips.push('skipped 多为 hook/规则块已被人改掉；对照 ledger 手动清理。')
  }
  return tips
}

/** 从 applied manifest 重建最小 ledger（旧装包无 ledger 时） */
async function ledgerFromLegacyApplied(
  cwd: string,
  packName: string,
  stateDir: string,
): Promise<InstallLedger | null> {
  const path = join(cwd, stateDir, 'applied', `${packSafeName(packName)}.json`)
  try {
    const man = JSON.parse(await fs.readFile(path, 'utf8')) as import('./project.js').PackProjectManifest
    const items: InstallLedgerItem[] = []
    for (const rt of man.runtimes) {
      for (const skill of rt.skills) {
        items.push({
          kind: 'skill',
          runtime: rt.runtime,
          name: skill,
          path: join(rt.skillsDir || '.claude/skills', skill).replace(/\\/g, '/'),
        })
      }
      if (rt.mcpFileAbs && rt.mcp.length) {
        for (const m of rt.mcp) {
          items.push({
            kind: 'mcp',
            runtime: rt.runtime,
            name: m,
            path: rt.mcpFileAbs,
            meta: { format: rt.mcpFormat },
          })
        }
      }
      if (rt.harnessL2?.path) items.push({ kind: 'harness-l2', runtime: rt.runtime, path: rt.harnessL2.path })
      for (const p of rt.astrbotPluginDirs ?? []) {
        items.push({ kind: 'astrbot-plugin', runtime: rt.runtime, path: p })
      }
    }
    return {
      schema: 'agent-pack/install-ledger/v1',
      packName: man.name,
      packVersion: man.version,
      installedAt: man.appliedAt,
      items,
    }
  } catch {
    return null
  }
}

export async function ejectPack(cwd: string, opts: EjectOpts = {}): Promise<EjectReport> {
  const stateDir = opts.stateDir ?? '.agent-pack'
  let packName = opts.packName
  if (!packName) {
    const lockPath = join(cwd, stateDir, 'lock.json')
    try {
      const lock = JSON.parse(await fs.readFile(lockPath, 'utf8')) as { packName?: string }
      packName = lock.packName
    } catch {
      packName = 'unnamed-pack'
    }
  }
  packName = packName || 'unnamed-pack'

  let ledger = await readInstallLedger(cwd, packName, stateDir)
  if (!ledger) ledger = await ledgerFromLegacyApplied(cwd, packName, stateDir)
  if (!ledger) {
    return {
      ok: false,
      packName,
      items: [
        {
          kind: 'skill',
          path: installLedgerPath(cwd, packName, stateDir),
          status: 'missing',
          note: '无 ledger',
        },
      ],
      summary: { removed: 0, missing: 1, skipped: 0, conflict: 0, partial: 0 },
      remediation: ['未找到 install-ledger；若从未 install 成功，只需手动删 .agent-pack/experiences'],
    }
  }

  const items: EjectItemReport[] = []
  const seen = new Set<string>()
  let mcpBootstrapDone = false

  for (const item of ledger.items) {
    const key = `${item.kind}:${item.path}:${item.name ?? ''}`
    if (seen.has(key)) continue
    seen.add(key)

    switch (item.kind) {
      case 'skill':
        items.push(await ejectSkillDir(cwd, item.path, packName, Boolean(opts.force)))
        break
      case 'harness-l2':
        items.push(await ejectHarnessL2(cwd, item.path, packName))
        break
      case 'mcp': {
        const format = (item.meta?.format as McpFormat) || 'json-mcpServers'
        const abs = item.path.match(/^[a-zA-Z]:/) ? item.path : absPath(cwd, item.path)
        if (!(await exists(abs))) {
          items.push({ kind: 'mcp', path: abs, status: 'missing', name: item.name })
          break
        }
        try {
          await unmergeMcp(abs, format, item.name ? [item.name] : [])
          items.push({ kind: 'mcp', path: abs, status: 'removed', note: item.name })
        } catch (e) {
          items.push({ kind: 'mcp', path: abs, status: 'partial', note: (e as Error).message })
        }
        break
      }
      case 'astrbot-plugin': {
        const abs = absPath(cwd, item.path)
        if (!(await exists(abs))) {
          items.push({ kind: 'astrbot-plugin', path: abs, status: 'missing' })
          break
        }
        await fs.rm(abs, { recursive: true, force: true })
        items.push({ kind: 'astrbot-plugin', path: abs, status: 'removed' })
        break
      }
      case 'hermes-external': {
        const configAbs = String(item.meta?.configAbs || '')
        if (configAbs && (await exists(configAbs))) {
          await removeHermesExternalDir(configAbs, absPath(cwd, item.path))
        }
        const staging = absPath(cwd, item.path)
        if (await exists(staging)) await fs.rm(staging, { recursive: true, force: true })
        items.push({ kind: 'hermes-external', path: item.path, status: 'removed' })
        break
      }
      case 'staging': {
        const abs = absPath(cwd, item.path)
        if (await exists(abs)) await fs.rm(abs, { recursive: true, force: true })
        items.push({ kind: 'staging', path: abs, status: (await exists(abs)) ? 'partial' : 'removed' })
        break
      }
      case 'mcp-bootstrap': {
        if (mcpBootstrapDone) break
        mcpBootstrapDone = true
        for (const r of await removeAgentPackMcpBootstrap(cwd)) {
          items.push({
            kind: 'mcp-bootstrap',
            path: r.file,
            status: r.status === 'removed' ? 'removed' : r.status === 'missing' ? 'missing' : 'skipped',
          })
        }
        break
      }
      default:
        items.push({ kind: item.kind, path: item.path, status: 'skipped', note: '无自动卸载器' })
    }
  }

  if (!mcpBootstrapDone) {
    for (const r of await removeAgentPackMcpBootstrap(cwd)) {
      if (r.status !== 'unchanged') {
        items.push({
          kind: 'mcp-bootstrap',
          path: r.file,
          status: r.status === 'removed' ? 'removed' : 'missing',
        })
      }
    }
  }

  items.push(...(await ejectExperienceFiles(cwd, stateDir)))

  const hookReport = await unwireExperienceHooks(cwd, stateDir)
  for (const w of hookReport.removed) {
    items.push({ kind: 'experience-hook', path: w.config, status: 'removed', note: w.event })
  }
  for (const s of hookReport.skipped) {
    items.push({ kind: 'experience-hook', path: s, status: 'skipped' })
  }

  items.push(...(await ejectSidecars(cwd, stateDir)))

  const appliedManifest = join(cwd, stateDir, 'applied', `${packSafeName(packName)}.json`)
  if (await exists(appliedManifest)) {
    await fs.rm(appliedManifest, { force: true })
    items.push({ kind: 'sidecar', path: appliedManifest, status: 'removed' })
  }

  const safe = packSafeName(packName)
  for (const suffix of ['-tool-schemas.json', '-assembly.json']) {
    const p = join(cwd, stateDir, 'applied', `${safe}${suffix}`)
    if (await exists(p)) {
      await fs.rm(p, { force: true })
      items.push({ kind: 'sidecar', path: p, status: 'removed' })
    }
  }

  if (!opts.keepLedger) {
    const lp = join(cwd, stateDir, 'applied', `${safe}-ledger.json`)
    if (await exists(lp)) {
      await fs.rm(lp, { force: true })
      items.push({ kind: 'sidecar', path: lp, status: 'removed' })
    }
  }

  const summary = summarize(items)
  const report: EjectReport = {
    ok: summary.conflict === 0 && summary.partial === 0,
    packName,
    items,
    summary,
    remediation: [],
  }
  report.remediation = buildRemediation(report)
  return report
}

function ledgerPath(cwd: string, packName: string, stateDir: string): string {
  return join(cwd, stateDir, 'applied', `${packSafeName(packName)}-ledger.json`)
}

export async function packStatus(cwd: string, stateDir = '.agent-pack'): Promise<Record<string, unknown>> {
  const lockPath = join(cwd, stateDir, 'lock.json')
  let lock: Record<string, unknown> | null = null
  try {
    lock = JSON.parse(await fs.readFile(lockPath, 'utf8')) as Record<string, unknown>
  } catch {
    lock = null
  }
  const packName = String(lock?.packName || 'unnamed-pack')
  const ledger = await readInstallLedger(cwd, packName, stateDir)
  let expIndex: unknown = null
  try {
    expIndex = JSON.parse(await fs.readFile(join(cwd, stateDir, 'experiences', 'index.json'), 'utf8'))
  } catch {
    /* none */
  }
  return {
    cwd,
    stateDir,
    lock,
    ledger: ledger ? { packName: ledger.packName, itemCount: ledger.items.length, installedAt: ledger.installedAt } : null,
    experiences: expIndex,
    mcpBootstrap: mcpBootstrapTargets(cwd).map(t => t.absFile),
  }
}