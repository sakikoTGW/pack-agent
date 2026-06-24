import { promises as fs } from 'node:fs'
import { basename, dirname, join, relative } from 'node:path'
import type { PackDoc } from './types.js'

export type BundleFile = { path: string; content: string }

/** 扫描到的 skills/rules 内嵌进 pack → 装包不依赖本机绝对路径 */
export async function embedPortableFiles(pack: PackDoc, cwd: string): Promise<PackDoc> {
  const files: BundleFile[] = []
  const seen = new Set<string>()

  for (const s of pack.knowledge?.skills ?? []) {
    const ref = String(s.ref || '').trim()
    if (!ref) continue
    const abs = ref.match(/^[a-zA-Z]:/) || ref.startsWith('/') ? ref : join(cwd, ref)
    const dir = abs.endsWith('SKILL.md') ? dirname(abs) : abs
    if (!(await exists(join(dir, 'SKILL.md')))) continue
    const skillName = s.name || basename(dir)
    for (const f of await walkFiles(dir)) {
      const p = `skills/${skillName}/${f.path}`
      if (seen.has(p)) continue
      seen.add(p)
      files.push({ path: p, content: f.content })
    }
  }

  for (const r of pack.knowledge?.rules ?? []) {
    const ref = String(r.ref || '').trim()
    if (!ref) continue
    const abs = ref.match(/^[a-zA-Z]:/) || ref.startsWith('/') ? ref : join(cwd, ref)
    if (!(await exists(abs))) continue
    try {
      const st = await fs.stat(abs)
      if (!st.isFile()) continue
    } catch {
      continue
    }
    const name = r.name || basename(abs)
    const p = `rules/${name}`
    if (seen.has(p)) continue
    seen.add(p)
    files.push({ path: p, content: await fs.readFile(abs, 'utf8') })
  }

  return {
    ...pack,
    bundle: { portable: true, files },
    meta: { ...pack.meta, portable: true },
  }
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

async function walkFiles(dir: string, base = dir): Promise<BundleFile[]> {
  const out: BundleFile[] = []
  let entries: import('node:fs').Dirent[] = []
  try {
    entries = await fs.readdir(dir, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    const abs = join(dir, e.name)
    if (e.isDirectory()) {
      out.push(...(await walkFiles(abs, base)))
      continue
    }
    if (!e.isFile()) continue
    try {
      const rel = relative(base, abs).replace(/\\/g, '/')
      out.push({ path: rel, content: await fs.readFile(abs, 'utf8') })
    } catch {
      /* skip symlinks / odd fs entries */
    }
  }
  return out
}

export async function materializePortableBundle(
  cwd: string,
  pack: PackDoc,
  stateDir = '.agent-pack',
): Promise<string | null> {
  const bundle = pack.bundle
  if (!bundle?.files?.length) return null
  const safe = (pack.name || 'pack').replace(/[^\w.-]+/g, '_')
  const root = join(cwd, stateDir, 'staging', safe)
  await fs.mkdir(root, { recursive: true })
  for (const f of bundle.files) {
    const dest = join(root, f.path.replace(/\//g, '\\'))
    await fs.mkdir(dirname(dest), { recursive: true })
    await fs.writeFile(dest, f.content, 'utf8')
  }
  return root
}

export async function resolveSkillDir(
  cwd: string,
  name: string,
  ref: string,
  stagingRoot: string | null,
): Promise<string | null> {
  const abs = ref.match(/^[a-zA-Z]:/) || ref.startsWith('/') ? ref : join(cwd, ref)
  const dir = abs.endsWith('SKILL.md') ? dirname(abs) : abs
  if (await exists(join(dir, 'SKILL.md'))) return dir

  if (stagingRoot) {
    const staged = join(stagingRoot, 'skills', name)
    if (await exists(join(staged, 'SKILL.md'))) return staged
  }

  for (const base of ['.agents/skills', '.claude/skills', '.opencode/skills', '.gemini/skills']) {
    const p = join(cwd, base, name)
    if (await exists(join(p, 'SKILL.md'))) return p
  }
  return null
}

export async function resolveRuleFile(
  cwd: string,
  name: string,
  ref: string,
  stagingRoot: string | null,
): Promise<string | null> {
  const abs = ref.match(/^[a-zA-Z]:/) || ref.startsWith('/') ? ref : join(cwd, ref)
  if (await exists(abs)) return abs

  if (stagingRoot) {
    const staged = join(stagingRoot, 'rules', name)
    if (await exists(staged)) return staged
  }

  for (const token of ['CLAUDE.md', 'AGENTS.md', 'GEMINI.md', join('.claude', 'rules', name)]) {
    const p = join(cwd, token)
    if (basename(p) === name && (await exists(p))) return p
  }
  return null
}

export function skillHasBundleFiles(pack: PackDoc, skillName: string): boolean {
  const prefix = `skills/${skillName}/`
  return Boolean(pack.bundle?.files?.some(f => f.path.startsWith(prefix) && f.path.endsWith('SKILL.md')))
}

export function skillOriginInBundle(pack: PackDoc, skillName: string): 'bundled' | 'ref-only' | 'missing' {
  if (skillHasBundleFiles(pack, skillName)) return 'bundled'
  const entry = pack.knowledge?.skills?.find(s => String(s.name) === skillName)
  if (entry?.ref) return 'ref-only'
  return 'missing'
}

export async function readPackFile(path: string): Promise<PackDoc> {
  const raw = await fs.readFile(path, 'utf8')
  return JSON.parse(raw) as PackDoc
}
