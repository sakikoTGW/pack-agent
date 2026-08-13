import { promises as fs } from 'node:fs'
import { basename, dirname, join, resolve } from 'node:path'
import { packTmpRoot } from '../tmp-root.js'
import type { Diag } from './diagnostics.js'
import type { PackAst } from './ast.js'
import { checkPackToml } from './check.js'
import { defaultKindRoots, loadKind, type KindDoc } from './kind-load.js'
import { lowerPack } from './lower.js'
import type { PackIr } from './ir.js'
import { writeZipFromDir } from '../pack-archive.js'

export type BuildPackOpts = {
  out?: string
  kindRoots?: string[]
  kinds?: Map<string, KindDoc> | Record<string, KindDoc>
}

export type BuildPackResult = {
  ok: boolean
  diagnostics: Diag[]
  ir?: PackIr
  zipPath?: string
}

function buildKindRoots(opts?: BuildPackOpts): string[] {
  const roots = [...defaultKindRoots(), resolve(process.cwd(), 'kinds')]
  if (opts?.kindRoots?.length) roots.push(...opts.kindRoots)
  return roots
}

function loadKindsForUnits(ast: PackAst, opts?: BuildPackOpts): Map<string, KindDoc> | Record<string, KindDoc> {
  if (opts?.kinds) return opts.kinds

  const kinds = new Map<string, KindDoc>()
  const roots = buildKindRoots(opts)
  for (const kindId of new Set(ast.units.map((unit) => unit.kind).filter(Boolean))) {
    const doc = loadKind(kindId, roots)
    if (doc) kinds.set(kindId, doc)
  }
  return kinds
}

function sanitizeFileStem(name: string): string {
  const stem = name
    .trim()
    .replace(/[<>:"/\\|?*\u0000-\u001F]+/g, '_')
    .replace(/\s+/g, '_')
  return stem || 'pack'
}

function defaultZipPath(ir: Pick<PackIr, 'name'>): string {
  return join(packTmpRoot(), `${sanitizeFileStem(ir.name)}.pack.zip`)
}

async function writePackIrZip(ir: PackIr, zipPath: string): Promise<void> {
  const stage = join(packTmpRoot(), `pack-agent-build-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`)
  try {
    await fs.mkdir(stage, { recursive: true })
    await fs.writeFile(join(stage, 'pack.json'), JSON.stringify(ir), 'utf8')
    await writeZipFromDir(stage, zipPath)
  } finally {
    await fs.rm(stage, { recursive: true, force: true }).catch(() => {})
  }
}

export async function buildPackToml(text: string, opts: BuildPackOpts = {}): Promise<BuildPackResult> {
  const check = checkPackToml(text, { kindRoots: opts.kindRoots })
  if (!check.ok) {
    return {
      ok: false,
      diagnostics: check.diagnostics,
    }
  }

  const ast = check.ast
  if (!ast) {
    return {
      ok: false,
      diagnostics: check.diagnostics,
    }
  }

  const ir = lowerPack(ast, loadKindsForUnits(ast, opts))
  const zipPath = opts.out ? resolve(process.cwd(), opts.out) : defaultZipPath(ir)
  await fs.mkdir(dirname(zipPath), { recursive: true })
  await writePackIrZip(ir, zipPath)

  return {
    ok: true,
    diagnostics: [],
    ir,
    zipPath,
  }
}
