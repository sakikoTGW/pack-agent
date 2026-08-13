import type { PackAst } from './ast.js'
import { diag, type Diag } from './diagnostics.js'
import type { KindDoc } from './kind-load.js'

type KindLookup = Map<string, KindDoc> | Record<string, KindDoc>

function getKindDoc(kindsByUnitKind: KindLookup, kindId: string): KindDoc | undefined {
  return kindsByUnitKind instanceof Map ? kindsByUnitKind.get(kindId) : kindsByUnitKind[kindId]
}

function asExportSymbols(kind: KindDoc | undefined, unitName: string): string[] {
  const exports = kind?.abi?.exports
  if (!Array.isArray(exports) || !unitName) return []

  const symbols = new Set<string>()
  for (const exportTag of exports) {
    if (typeof exportTag !== 'string' || !exportTag) continue
    symbols.add(`${exportTag}:${unitName}`)
  }
  return [...symbols]
}

export function linkWorld(ast: PackAst, kindsByUnitKind: KindLookup): Diag[] {
  if (!ast.world) return []

  const diagnostics: Diag[] = []
  const providersBySymbol = new Map<string, string[]>()
  const addProvider = (symbol: string, unitName: string) => {
    const providers = providersBySymbol.get(symbol)
    if (providers) providers.push(unitName)
    else providersBySymbol.set(symbol, [unitName])
  }

  for (const unit of ast.units) {
    const kind = getKindDoc(kindsByUnitKind, unit.kind)
    for (const symbol of asExportSymbols(kind, unit.name)) addProvider(symbol, unit.name)
  }

  for (const [symbol, providers] of providersBySymbol) {
    if (providers.length > 1) diagnostics.push(diag('E-ABI-CONFLICT', { symbol, units: providers }))
  }

  const worldSymbols = new Set<string>([
    ...(ast.world.exports ?? []),
    ...(ast.world.imports ?? []),
  ])

  for (const symbol of worldSymbols) {
    const providers = providersBySymbol.get(symbol)
    if (!providers || providers.length === 0) diagnostics.push(diag('E-ABI-UNSATISFIED', { symbol }))
  }

  return diagnostics
}
