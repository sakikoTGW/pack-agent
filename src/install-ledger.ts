/**
 * 安装清单（ledger）—— 卸载/eject 的唯一依据；少件时逐项报 missing，不整包 abort。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import type { ExperienceProjectionReport } from './experience-projection.js'
import type { InstallReport, PackDoc } from './types.js'
import type { PackProjectManifest } from './project.js'
import type { ModuleInstallReport } from './install-modules.js'

export type LedgerItemKind =
  | 'skill'
  | 'rule'
  | 'mcp'
  | 'experience'
  | 'experience-hook'
  | 'harness-l2'
  | 'sidecar'
  | 'astrbot-plugin'
  | 'hermes-external'
  | 'extended-hook'
  | 'subagent'
  | 'memory'
  | 'settings'
  | 'mcp-bootstrap'
  | 'staging'

export type InstallLedgerItem = {
  kind: LedgerItemKind
  path: string
  runtime?: string
  name?: string
  meta?: Record<string, unknown>
}

export type InstallLedger = {
  schema: 'agent-pack/install-ledger/v1'
  packName: string
  packVersion?: string
  installedAt: string
  captureDeliver?: string
  items: InstallLedgerItem[]
}

export function packSafeName(name: string): string {
  return name.replace(/[^\w.-]+/g, '_')
}

export function ledgerPath(cwd: string, packName: string, stateDir = '.agent-pack'): string {
  return join(cwd, stateDir, 'applied', `${packSafeName(packName)}-ledger.json`)
}

export async function readInstallLedger(
  cwd: string,
  packName: string,
  stateDir = '.agent-pack',
): Promise<InstallLedger | null> {
  try {
    return JSON.parse(await fs.readFile(ledgerPath(cwd, packName, stateDir), 'utf8')) as InstallLedger
  } catch {
    return null
  }
}

export async function writeInstallLedger(
  cwd: string,
  ledger: InstallLedger,
  stateDir = '.agent-pack',
): Promise<string> {
  const path = ledgerPath(cwd, ledger.packName, stateDir)
  await fs.mkdir(join(cwd, stateDir, 'applied'), { recursive: true })
  await fs.writeFile(path, JSON.stringify(ledger, null, 2), 'utf8')
  return path
}

/** 从 install 报告拼装 ledger（装完立刻写，供 eject 对照） */
export function buildInstallLedger(input: {
  pack: PackDoc
  manifest: PackProjectManifest
  install: InstallReport
  stateDir: string
  expProjection?: ExperienceProjectionReport
  extInstalled?: ModuleInstallReport
  mcpBootstrapFiles?: string[]
  stagingRoot?: string | null
}): InstallLedger {
  const packName = input.pack.name || 'unnamed-pack'
  const items: InstallLedgerItem[] = []

  for (const rt of input.manifest.runtimes) {
    for (const skill of rt.skills) {
      let skillPath: string
      if (rt.runtime === 'astrbot' && rt.astrbotPluginDirs?.[0]) {
        skillPath = `${rt.astrbotPluginDirs[0]}/skills/${skill}`
      } else {
        skillPath = `${rt.skillsDir || '.claude/skills'}/${skill}`.replace(/\\/g, '/')
      }
      items.push({
        kind: 'skill',
        runtime: rt.runtime,
        name: skill,
        path: skillPath,
      })
    }
    for (const rule of rt.rules) {
      items.push({ kind: 'rule', runtime: rt.runtime, name: rule, path: rule })
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
    if (rt.harnessL2?.path) {
      items.push({
        kind: 'harness-l2',
        runtime: rt.runtime,
        path: rt.harnessL2.path,
        meta: { kind: rt.harnessL2.kind },
      })
    }
    for (const p of rt.astrbotPluginDirs ?? []) {
      items.push({ kind: 'astrbot-plugin', runtime: rt.runtime, path: p })
    }
    if (rt.hermesExternalDir) {
      items.push({
        kind: 'hermes-external',
        runtime: rt.runtime,
        path: rt.hermesExternalDir.skillsAbs,
        meta: { configAbs: rt.hermesExternalDir.configAbs },
      })
    }
  }

  for (const exp of input.install.experiences ?? []) {
    items.push({ kind: 'experience', name: exp.id, path: exp.path })
  }

  for (const w of input.expProjection?.wired ?? []) {
    items.push({
      kind: 'experience-hook',
      runtime: w.runtime,
      path: w.config,
      meta: { event: w.event, label: w.label },
    })
  }

  for (const h of input.extInstalled?.hooks ?? []) {
    items.push({ kind: 'extended-hook', path: h })
  }
  for (const s of input.extInstalled?.subagents ?? []) {
    items.push({ kind: 'subagent', path: s })
  }
  for (const m of input.extInstalled?.memory ?? []) {
    items.push({ kind: 'memory', path: m })
  }
  for (const s of input.extInstalled?.settings ?? []) {
    items.push({ kind: 'settings', path: s })
  }

  for (const f of input.mcpBootstrapFiles ?? []) {
    items.push({ kind: 'mcp-bootstrap', path: f, name: 'agent-pack' })
  }

  if (input.stagingRoot) {
    items.push({ kind: 'staging', path: input.stagingRoot.replace(/\\/g, '/') })
  }

  return {
    schema: 'agent-pack/install-ledger/v1',
    packName,
    packVersion: input.pack.version,
    installedAt: new Date().toISOString(),
    captureDeliver: input.install.captureDeliver,
    items,
  }
}

function joinRel(...parts: (string | undefined)[]): string {
  return parts.filter(Boolean).join('/').replace(/\\/g, '/')
}
