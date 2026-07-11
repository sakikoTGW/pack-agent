/**
 * 扩展扫描 — hooks / subagents / memory / settings（L1 之外的沉积层）。
 */
import { promises as fs } from 'node:fs'
import { basename, join } from 'node:path'
import type {
  PackHookEntry,
  PackMemoryEntry,
  PackSettingsEntry,
  PackSubagentEntry,
} from './types.js'
import { isPackIgnored, type PackIgnoreMatcher } from './pack-ignore.js'
import { sha256Full } from './versioning.js'

export type ExtendedScan = {
  hooks: PackHookEntry[]
  subagents: PackSubagentEntry[]
  memory: PackMemoryEntry[]
  settings: PackSettingsEntry[]
  transcripts: Array<{ name: string; ref: string }>
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

function relRef(cwd: string, abs: string): string {
  return abs.replace(/\\/g, '/').replace(cwd.replace(/\\/g, '/'), '').replace(/^\//, '')
}

async function readText(abs: string): Promise<string | null> {
  try {
    return await fs.readFile(abs, 'utf8')
  } catch {
    return null
  }
}

async function scanSubagents(cwd: string, ignore: PackIgnoreMatcher): Promise<PackSubagentEntry[]> {
  const dirs = ['.claude/agents', '.agents/agents', '.codex/agents']
  const out: PackSubagentEntry[] = []
  const seen = new Set<string>()

  for (const dir of dirs) {
    const absDir = join(cwd, dir)
    let names: string[] = []
    try {
      names = (await fs.readdir(absDir)).filter(n => n.endsWith('.md'))
    } catch {
      continue
    }
    for (const n of names) {
      const abs = join(absDir, n)
      const ref = relRef(cwd, abs)
      if (isPackIgnored(ref, ignore)) continue
      const name = n.replace(/\.md$/, '')
      if (seen.has(name)) continue
      seen.add(name)
      const content = (await readText(abs)) ?? ''
      out.push({
        name,
        ref,
        scope: dir.startsWith('.claude') ? 'claude-code' : 'generic',
        contentHash: sha256Full(content),
      })
    }
  }
  return out
}

async function scanMemory(cwd: string, ignore: PackIgnoreMatcher): Promise<PackMemoryEntry[]> {
  const candidates: Array<{ abs: string; kind: PackMemoryEntry['kind']; name: string }> = [
    { abs: join(cwd, 'MEMORY.md'), kind: 'project-memory', name: 'MEMORY.md' },
    { abs: join(cwd, 'USER.md'), kind: 'user-profile', name: 'USER.md' },
    { abs: join(cwd, 'CLAUDE.local.md'), kind: 'local-notes', name: 'CLAUDE.local.md' },
  ]

  const memDir = join(cwd, '.agent-pack', 'memory')
  try {
    for (const f of await fs.readdir(memDir)) {
      if (f.endsWith('.md') || f.endsWith('.json')) {
        candidates.push({ abs: join(memDir, f), kind: 'pack-memory', name: f })
      }
    }
  } catch {
    /* no pack memory dir */
  }

  const out: PackMemoryEntry[] = []
  for (const c of candidates) {
    if (!(await exists(c.abs))) continue
    const ref = relRef(cwd, c.abs)
    if (isPackIgnored(ref, ignore)) continue
    const content = (await readText(c.abs)) ?? ''
    if (!content.trim()) continue
    out.push({
      name: c.name,
      ref,
      kind: c.kind,
      scope: 'project',
      contentHash: sha256Full(content),
    })
  }
  return out
}

async function scanHooks(cwd: string, ignore: PackIgnoreMatcher): Promise<PackHookEntry[]> {
  const paths = ['.claude/settings.json', '.claude/settings.local.json']
  const out: PackHookEntry[] = []

  for (const rel of paths) {
    const abs = join(cwd, rel)
    if (!(await exists(abs))) continue
    if (isPackIgnored(rel, ignore)) continue
    try {
      const doc = JSON.parse(await fs.readFile(abs, 'utf8')) as { hooks?: Record<string, unknown> }
      const hooks = doc.hooks
      if (!hooks || typeof hooks !== 'object' || !Object.keys(hooks).length) continue
      const payload = JSON.stringify(hooks)
      out.push({
        name: basename(rel, '.json'),
        ref: rel,
        format: 'claude-settings-hooks',
        scope: 'project',
        contentHash: sha256Full(payload),
        hookEvents: Object.keys(hooks),
      })
    } catch {
      /* skip invalid json */
    }
  }
  return out
}

async function scanSettingsFragments(cwd: string, ignore: PackIgnoreMatcher): Promise<PackSettingsEntry[]> {
  const rel = '.claude/settings.json'
  const abs = join(cwd, rel)
  if (!(await exists(abs)) || isPackIgnored(rel, ignore)) return []
  try {
    const doc = JSON.parse(await fs.readFile(abs, 'utf8')) as Record<string, unknown>
    const keys = ['permissions', 'env', 'model', 'enabledPlugins'] as const
    const out: PackSettingsEntry[] = []
    for (const key of keys) {
      if (doc[key] === undefined) continue
      const fragment = { [key]: doc[key] }
      out.push({
        key,
        ref: rel,
        format: 'claude-settings-fragment',
        contentHash: sha256Full(JSON.stringify(fragment)),
      })
    }
    return out
  } catch {
    return []
  }
}

async function scanTranscriptIndex(cwd: string, ignore: PackIgnoreMatcher): Promise<Array<{ name: string; ref: string }>> {
  const dirs = [
    join(cwd, '.cursor', 'projects'),
    join(cwd, '.claude', 'projects'),
  ]
  const out: Array<{ name: string; ref: string }> = []
  for (const base of dirs) {
    try {
      const walk = async (dir: string): Promise<void> => {
        const entries = await fs.readdir(dir, { withFileTypes: true })
        for (const e of entries) {
          const abs = join(dir, e.name)
          const ref = relRef(cwd, abs)
          if (isPackIgnored(ref, ignore)) continue
          if (e.isDirectory()) {
            await walk(abs)
            continue
          }
          if (e.name.endsWith('.jsonl') && (ref.includes('transcript') || ref.includes('agent-transcripts'))) {
            out.push({ name: e.name, ref })
          }
        }
      }
      await walk(base)
    } catch {
      /* missing dir */
    }
  }
  return out.slice(0, 50)
}

export async function scanExtendedModules(
  cwd: string,
  ignore: PackIgnoreMatcher,
  enabled: Partial<Record<'hooks' | 'subagents' | 'memory' | 'settings' | 'transcripts', boolean>>,
): Promise<ExtendedScan> {
  const [hooks, subagents, memory, settings, transcripts] = await Promise.all([
    enabled.hooks ? scanHooks(cwd, ignore) : Promise.resolve([]),
    enabled.subagents ? scanSubagents(cwd, ignore) : Promise.resolve([]),
    enabled.memory ? scanMemory(cwd, ignore) : Promise.resolve([]),
    enabled.settings ? scanSettingsFragments(cwd, ignore) : Promise.resolve([]),
    enabled.transcripts ? scanTranscriptIndex(cwd, ignore) : Promise.resolve([]),
  ])
  return { hooks, subagents, memory, settings, transcripts }
}

/** 将扩展扫描写入 pack + 准备 bundle 路径 */
export async function mergeExtendedIntoPack(
  cwd: string,
  pack: import('./types.js').PackDoc,
  ext: ExtendedScan,
): Promise<import('./types.js').PackDoc> {
  const out = { ...pack }
  if (ext.hooks.length) out.automation = { hooks: ext.hooks }
  if (ext.subagents.length) out.agents = { subagents: ext.subagents }
  if (ext.memory.length) out.memory = { files: ext.memory }
  if (ext.settings.length) out.settings = { fragments: ext.settings }
  if (ext.transcripts.length) {
    out.meta = {
      ...out.meta,
      transcriptIndex: ext.transcripts,
      fidelity: out.meta?.fidelity ?? 'L1+archive-index',
    }
  }
  return out
}

export async function embedExtendedBundleFiles(
  cwd: string,
  pack: import('./types.js').PackDoc,
): Promise<import('./types.js').PackDoc> {
  const files = [...(pack.bundle?.files ?? [])]
  const seen = new Set(files.map(f => f.path))

  for (const h of pack.automation?.hooks ?? []) {
    const ref = String(h.ref || '')
    if (!ref) continue
    const abs = join(cwd, ref)
    const content = await readText(abs)
    if (!content) continue
    try {
      const doc = JSON.parse(content) as { hooks?: unknown }
      const p = `automation/${h.name ?? basename(ref)}.hooks.json`
      if (seen.has(p)) continue
      seen.add(p)
      files.push({ path: p, content: JSON.stringify(doc.hooks ?? {}, null, 2) })
    } catch {
      /* skip */
    }
  }

  for (const a of pack.agents?.subagents ?? []) {
    const ref = String(a.ref || '')
    if (!ref) continue
    const abs = join(cwd, ref)
    const content = await readText(abs)
    if (!content) continue
    const p = `agents/${a.name ?? basename(ref, '.md')}.md`
    if (seen.has(p)) continue
    seen.add(p)
    files.push({ path: p, content })
  }

  for (const m of pack.memory?.files ?? []) {
    const ref = String(m.ref || '')
    if (!ref) continue
    const abs = join(cwd, ref)
    const content = await readText(abs)
    if (!content) continue
    const p = `memory/${m.name ?? basename(ref)}`
    if (seen.has(p)) continue
    seen.add(p)
    files.push({ path: p, content })
  }

  for (const s of pack.settings?.fragments ?? []) {
    const ref = String(s.ref || '')
    if (!ref) continue
    const abs = join(cwd, ref)
    const raw = await readText(abs)
    if (!raw) continue
    try {
      const doc = JSON.parse(raw) as Record<string, unknown>
      const fragment = doc[s.key]
      if (fragment === undefined) continue
      const p = `settings/${s.key}.json`
      if (seen.has(p)) continue
      seen.add(p)
      files.push({ path: p, content: JSON.stringify(fragment, null, 2) })
    } catch {
      /* skip */
    }
  }

  const transcriptIndex = pack.meta?.transcriptIndex as Array<{ name: string; ref: string }> | undefined
  for (const t of transcriptIndex ?? []) {
    const ref = String(t.ref || '')
    if (!ref) continue
    const abs = join(cwd, ref)
    const content = await readText(abs)
    if (!content) continue
    const p = `transcripts/${t.name}`
    if (seen.has(p)) continue
    seen.add(p)
    files.push({ path: p, content })
  }

  if (!files.length) return pack
  return { ...pack, bundle: { portable: true, files } }
}
