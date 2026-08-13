import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import type { PackAst } from './ast.js'
import { checkPackToml, type CheckResult } from './check.js'
import { defaultKindRoots, loadKind, type KindDoc } from './kind-load.js'
import { parsePackToml } from './parse.js'

export type ExplainResult = CheckResult & { text: string }

function buildKindRoots(opts?: { kindRoots?: string[] }): string[] {
  const roots = [...defaultKindRoots(), resolve(process.cwd(), 'kinds')]
  if (opts?.kindRoots?.length) roots.push(...opts.kindRoots)
  return roots
}

function formatList(values?: string[]): string {
  return values && values.length ? values.join(', ') : '(none)'
}

function formatAbiSummary(kind?: KindDoc | null): string {
  if (!kind) return 'abi: (kind not found)'
  const parts: string[] = []
  if (kind.abi?.exports?.length) parts.push(`abi.exports: ${formatList(kind.abi.exports)}`)
  if (kind.abi?.imports?.length) parts.push(`abi.imports: ${formatList(kind.abi.imports)}`)
  return parts.length ? parts.join('  ') : 'abi: (none)'
}

function formatWorld(ast: PackAst): string {
  const exports = ast.world?.exports ?? []
  const imports = ast.world?.imports ?? []
  if (!exports.length && !imports.length) return 'world: (none)'

  const lines = ['world:']
  if (exports.length) lines.push(`  exports: ${formatList(exports)}`)
  if (imports.length) lines.push(`  imports: ${formatList(imports)}`)
  return lines.join('\n')
}

function formatExplain(ast: PackAst, kindRoots: string[]): string {
  const lines = [`pack: ${ast.name || '(none)'}`, `edition: ${ast.edition || '(none)'}`, 'units:']
  if (!ast.units.length) {
    lines.push('  (none)')
  } else {
    for (const unit of ast.units) {
      const kind = unit.kind ? loadKind(unit.kind, kindRoots) : null
      const title = kind?.describe?.title || '(missing)'
      lines.push(
        `  - name: ${unit.name || '(none)'}  kind: ${unit.kind || '(none)'}  title: ${title}  ${formatAbiSummary(kind)}`,
      )
    }
  }
  lines.push(formatWorld(ast))
  return lines.join('\n')
}

export function explainPackToml(text: string, opts?: { kindRoots?: string[] }): ExplainResult {
  const check = checkPackToml(text, opts)
  const ast = check.ast ?? parsePackToml(text)
  const kindRoots = buildKindRoots(opts)
  return {
    ...check,
    text: formatExplain(ast, kindRoots),
  }
}

export function explainPackFile(path: string, opts?: { kindRoots?: string[] }): ExplainResult {
  const text = readFileSync(resolve(path), 'utf8')
  return explainPackToml(text, opts)
}
