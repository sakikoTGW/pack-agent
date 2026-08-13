import type { UnitAst, WorldAst } from './ast.js'
import type { KindDoc } from './kind-load.js'

export type PackIr = {
  schema: 'agent-pack-ir/2026'
  edition: string
  name: string
  version?: string
  units: UnitAst[]
  world?: WorldAst
  kinds?: Record<string, KindDoc>
  meta?: Record<string, unknown>
}
