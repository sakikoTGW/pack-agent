/**
 * 该实例 DSH_HOME 里的 agent-preset 名册。
 * 随附根随发行号安装；用户层是 `$DSH_HOME/.agent-presets`。
 * 只做磁盘发现与用户层 copy/remove。不代聊，不改随附目录。
 */
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { dshBin, getInstance, pa, type LauncherRoot } from './launcher.js'

export const USER_PRESET_DIR = '.agent-presets'
export const COMPOSITION_FILE = 'agent.cordis.yml'
export const PRESET_ID = /^[a-z0-9][a-z0-9-]*$/

export type PresetTrust = 'system' | 'user'

export type AgentPresetRow = {
  id: string
  trust: PresetTrust
  path: string
  broken?: string
}

export function shippedPresetRoot(root: LauncherRoot, version: string): string {
  return join(dirname(dshBin(root, version)), '..', 'config', 'agent-presets')
}

export function userPresetRoot(home: string): string {
  return join(home, USER_PRESET_DIR)
}

function scanRoot(dir: string, trust: PresetTrust): AgentPresetRow[] {
  if (!existsSync(dir)) return []
  const out: AgentPresetRow[] = []
  for (const ent of readdirSync(dir, { withFileTypes: true })) {
    if (!ent.isDirectory()) continue
    if (!PRESET_ID.test(ent.name)) continue
    const composition = join(dir, ent.name, COMPOSITION_FILE)
    const row: AgentPresetRow = { id: ent.name, trust, path: composition }
    if (!existsSync(composition)) row.broken = `missing ${COMPOSITION_FILE}`
    out.push(row)
  }
  return out
}

export function listAgentPresets(root: LauncherRoot, id: string): AgentPresetRow[] {
  const inst = getInstance(root, id)
  const byId = new Map<string, AgentPresetRow>()
  for (const row of scanRoot(userPresetRoot(inst.home), 'user')) byId.set(row.id, row)
  for (const row of scanRoot(shippedPresetRoot(root, inst.dsh.version), 'system')) byId.set(row.id, row)
  return [...byId.values()].sort((a, b) => a.id.localeCompare(b.id))
}

function findPreset(root: LauncherRoot, id: string, presetId: string): AgentPresetRow {
  const row = listAgentPresets(root, id).find((p) => p.id === presetId)
  if (!row) {
    throw pa('PA113', `agent-preset \`${presetId}\` is not on this instance roster`, `instance \`${id}\` / agent-preset`, {
      help: ['packagent dsh launcher agent-preset list <id>'],
    })
  }
  return row
}

export function copyAgentPreset(root: LauncherRoot, id: string, fromId: string, toId: string): AgentPresetRow {
  if (!PRESET_ID.test(toId)) {
    throw pa('PA113', `agent-preset id \`${toId}\` is not a legal directory name`, `instance \`${id}\` / agent-preset`, {
      help: ['use a lowercase id matching [a-z0-9][a-z0-9-]*'],
    })
  }
  if (listAgentPresets(root, id).some((p) => p.id === toId)) {
    throw pa('PA113', `agent-preset \`${toId}\` already exists`, `instance \`${id}\` / agent-preset`, {
      help: ['remove the user-layer preset first', 'choose another id'],
    })
  }
  const from = findPreset(root, id, fromId)
  const fromDir = dirname(from.path)
  if (!existsSync(fromDir)) {
    throw pa('PA113', `agent-preset \`${fromId}\` directory is missing`, `instance \`${id}\` / agent-preset`)
  }
  const inst = getInstance(root, id)
  const destDir = join(userPresetRoot(inst.home), toId)
  mkdirSync(userPresetRoot(inst.home), { recursive: true })
  cpSync(fromDir, destDir, { recursive: true })
  return { id: toId, trust: 'user', path: join(destDir, COMPOSITION_FILE) }
}

export function removeAgentPreset(root: LauncherRoot, id: string, presetId: string): { removed: string } {
  const row = findPreset(root, id, presetId)
  if (row.trust === 'system') {
    throw pa('PA112', `cannot remove shipped agent-preset \`${presetId}\``, `instance \`${id}\` / agent-preset`, {
      help: ['copy it to a user-layer id, then edit the copy'],
    })
  }
  const inst = getInstance(root, id)
  const destDir = join(userPresetRoot(inst.home), presetId)
  if (!existsSync(destDir)) {
    throw pa('PA112', `cannot remove shipped agent-preset \`${presetId}\``, `instance \`${id}\` / agent-preset`)
  }
  rmSync(destDir, { recursive: true, force: true })
  return { removed: presetId }
}
