import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { diag, type Diag } from './diagnostics.js'
import type { PackAst } from './ast.js'
import { defaultKindRoots, loadKind } from './kind-load.js'
import { parsePackToml } from './parse.js'
import { linkWorld } from './typeck.js'

export type CheckResult = { ok: boolean; diagnostics: Diag[]; ast?: PackAst }

function buildKindRoots(opts?: { kindRoots?: string[] }): string[] {
  const roots = [...defaultKindRoots(), resolve(process.cwd(), 'kinds')]
  if (opts?.kindRoots?.length) roots.push(...opts.kindRoots)
  return roots
}

export function checkPackToml(text: string, opts?: { kindRoots?: string[] }): CheckResult {
  const diagnostics: Diag[] = []
  const ast = parsePackToml(text)
  const kindRoots = buildKindRoots(opts)
  const kindsByUnitKind = new Map<string, NonNullable<ReturnType<typeof loadKind>>>()
  let allKindsLoaded = true

  if (ast.edition !== '2026') {
    diagnostics.push(diag('E-EDITION-UNKNOWN', { edition: ast.edition }))
  }

  for (const unit of ast.units) {
    const kind = loadKind(unit.kind, kindRoots)
    if (!kind) {
      diagnostics.push(diag('E-KIND-NOT-FOUND', { kind: unit.kind }))
      allKindsLoaded = false
      continue
    }

    kindsByUnitKind.set(unit.kind, kind)
    if (!kind.describe?.title) diagnostics.push(diag('E-SCHEMA-INVALID', { name: unit.kind, field: 'describe.title' }))
  }

  if (ast.world && ast.edition === '2026' && allKindsLoaded) {
    diagnostics.push(...linkWorld(ast, kindsByUnitKind))
  }

  return {
    ok: diagnostics.length === 0,
    diagnostics,
    ast,
  }
}

export async function checkPackFile(path: string, opts?: { kindRoots?: string[] }): Promise<CheckResult> {
  const text = readFileSync(resolve(path), 'utf8')
  return checkPackToml(text, opts)
}
