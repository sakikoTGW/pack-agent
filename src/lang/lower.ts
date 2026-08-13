import type { PackAst } from './ast.js'
import type { KindDoc } from './kind-load.js'
import type { PackIr } from './ir.js'

function kindRecordFromLookup(kinds: Map<string, KindDoc> | Record<string, KindDoc>): Record<string, KindDoc> {
  const out: Record<string, KindDoc> = {}
  if (kinds instanceof Map) {
    for (const [id, doc] of kinds.entries()) {
      if (doc) out[id] = doc
    }
    return out
  }

  for (const [id, doc] of Object.entries(kinds)) {
    if (doc) out[id] = doc
  }
  return out
}

export function lowerPack(ast: PackAst, kinds: Map<string, KindDoc> | Record<string, KindDoc>): PackIr {
  const kindDocs = kindRecordFromLookup(kinds)
  const usedKinds = new Set(ast.units.map((unit) => unit.kind).filter((kind): kind is string => Boolean(kind)))
  const lower: PackIr = {
    schema: 'agent-pack-ir/2026',
    edition: ast.edition,
    name: ast.name,
    units: ast.units.map((unit) => ({ ...unit })),
  }

  if (ast.version !== undefined) lower.version = ast.version
  if (ast.world) {
    lower.world = {
      ...(ast.world.exports ? { exports: [...ast.world.exports] } : {}),
      ...(ast.world.imports ? { imports: [...ast.world.imports] } : {}),
    }
  }

  const kindsInUse: Record<string, KindDoc> = {}
  for (const kindId of usedKinds) {
    const doc = kindDocs[kindId]
    if (doc) kindsInUse[kindId] = doc
  }
  if (Object.keys(kindsInUse).length) lower.kinds = kindsInUse

  return lower
}
