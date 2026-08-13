import { getStdlibInstallTemplate } from '../lang/stdlib.js'
import type { PackIr } from '../lang/ir.js'

export function describeCodegenTargets(ir: PackIr): string[] {
  const seen = new Set<string>()
  const out: string[] = []

  for (const unit of ir.units) {
    if (seen.has(unit.kind)) continue
    seen.add(unit.kind)
    out.push(getStdlibInstallTemplate(unit.kind) ?? unit.kind)
  }

  return out
}
