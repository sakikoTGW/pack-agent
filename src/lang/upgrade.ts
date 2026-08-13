import type { PackDoc } from '../types.js'
import type { PackIr } from './ir.js'

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === 'object' && !Array.isArray(value))
}

function asString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function asArray(value: unknown): unknown[] | undefined {
  return Array.isArray(value) ? value : undefined
}

function normalizeRef(entry: Record<string, unknown>): string | undefined {
  return asString(entry.ref) ?? asString(entry.path) ?? asString(entry.source)
}

function normalizeName(entry: Record<string, unknown>, fallback: string): string {
  return asString(entry.name) ?? normalizeRef(entry) ?? fallback
}

function pushUnit(
  units: PackIr['units'],
  kind: 'agent.skill' | 'agent.rule' | 'agent.mcp' | 'agent.command',
  entry: Record<string, unknown>,
  fallback: string,
): void {
  const name = normalizeName(entry, fallback)
  const path = normalizeRef(entry) ?? name
  units.push({ kind, name, path })
}

function upgradeKnownSection(
  units: PackIr['units'],
  section: unknown,
  kind: 'agent.skill' | 'agent.rule' | 'agent.mcp' | 'agent.command',
  fallbackPrefix: string,
): void {
  const entries = asArray(section)
  if (!entries) return

  for (let i = 0; i < entries.length; i++) {
    const entry = entries[i]
    if (!isRecord(entry)) continue
    pushUnit(units, kind, entry, `${fallbackPrefix}-${i + 1}`)
  }
}

export function upgradePackDocToIr(doc: unknown): PackIr {
  if (!isRecord(doc)) {
    throw new Error('unrecognizable pack doc: expected object')
  }

  const schema = asString(doc.schema)
  if (schema !== undefined && schema !== 'ccui-pack/v0.1' && schema !== 'ccui-pack/v0.2') {
    throw new Error(`unsupported pack doc schema: ${schema}`)
  }
  if (schema === undefined && (!asString(doc.name) || !isRecord(doc.knowledge))) {
    throw new Error('unrecognizable pack doc: missing name/knowledge')
  }

  const units: PackIr['units'] = []
  const knowledge = isRecord(doc.knowledge) ? doc.knowledge : undefined
  const tools = isRecord(doc.tools) ? doc.tools : undefined
  const commands = isRecord(doc.commands) ? doc.commands : undefined
  const collab = isRecord(doc.collab) ? doc.collab : undefined
  const collabFiles = asArray(collab?.files)

  upgradeKnownSection(units, knowledge?.skills, 'agent.skill', 'skill')
  upgradeKnownSection(units, knowledge?.rules, 'agent.rule', 'rule')
  upgradeKnownSection(units, tools?.mcp, 'agent.mcp', 'mcp')
  upgradeKnownSection(units, commands?.files, 'agent.command', 'command')

  const meta: Record<string, unknown> = {}
  if (collabFiles?.length) {
    meta.notes = [`ignored ${collabFiles.length} collab file${collabFiles.length === 1 ? '' : 's'}`]
  }

  const ir: PackIr = {
    schema: 'agent-pack-ir/2026',
    edition: '2026',
    name: asString(doc.name) ?? 'pack',
    units,
  }

  const version = asString(doc.version)
  if (version !== undefined) ir.version = version
  if (Object.keys(meta).length) ir.meta = meta

  return ir
}

export type { PackDoc }
