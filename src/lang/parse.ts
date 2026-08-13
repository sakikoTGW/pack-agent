import { parse as parseToml } from 'smol-toml'
import type { PackAst, UnitAst, WorldAst } from './ast.js'

function asObject(value: unknown): Record<string, unknown> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  return value as Record<string, unknown>
}

function asString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

function asStringArray(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) return undefined
  return value.every((item) => typeof item === 'string') ? value : undefined
}

function normalizeUnit(value: unknown): UnitAst {
  const unit = asObject(value) ?? {}
  const ref = asString(unit.ref) ?? asString(unit.path) ?? ''
  const ast: UnitAst = {
    name: asString(unit.name) ?? ref,
    kind: asString(unit.kind) ?? '',
    path: ref,
  }

  const title = asString(unit.title)
  if (title !== undefined) ast.title = title

  return ast
}

function normalizeWorld(value: unknown): WorldAst | undefined {
  const world = asObject(value)
  if (!world) return undefined

  const ast: WorldAst = {}
  const exports = asStringArray(world.exports)
  if (exports !== undefined) ast.exports = exports
  const imports = asStringArray(world.imports)
  if (imports !== undefined) ast.imports = imports
  return ast
}

function normalizeUnits(value: unknown): UnitAst[] {
  if (Array.isArray(value)) return value.map(normalizeUnit)
  if (value === undefined) return []
  return [normalizeUnit(value)]
}

export function parsePackToml(text: string): PackAst {
  let doc: Record<string, unknown>
  try {
    doc = parseToml(text) as Record<string, unknown>
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    throw new Error(`invalid Pack.toml syntax: ${message}`)
  }

  const ast: PackAst = {
    edition: asString(doc.edition) ?? '',
    name: asString(doc.name) ?? '',
    units: normalizeUnits(doc.unit ?? doc.units),
  }

  const version = asString(doc.version)
  if (version !== undefined) ast.version = version

  const world = normalizeWorld(doc.world)
  if (world) ast.world = world

  return ast
}
