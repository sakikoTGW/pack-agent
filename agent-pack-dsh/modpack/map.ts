/**
 * 把 Cursor / Claude / 老版 ccui-pack 映射成 DSH 整合包用的便携路径。
 * skill / MCP / rule / command / hook 都收成 bundle，再编进 mods/。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import JSON5 from 'json5'
import { embedPortableFiles } from '../../src/portable.js'
import { embedExtendedBundleFiles } from '../../src/scan-modules.js'
import type { PackDoc, PackMcpEntry } from '../../src/types.js'

const SKILL_PREFIXES = [
  '.cursor/skills/',
  '.claude/skills/',
  '.agents/skills/',
  '.codex/skills/',
  '.gemini/skills/',
  '.dsh/skills/',
]

const RULE_PREFIXES = ['.cursor/rules/', '.claude/rules/']
const COMMAND_PREFIXES = ['.cursor/commands/', '.claude/commands/']
const MCP_BUNDLE_NAMES = new Set([
  '.cursor/mcp.json',
  '.mcp.json',
  'mcp.json',
  '.vscode/mcp.json',
  '.claude.json',
])

export function remapBundleFilePath(path: string): string {
  const p = path.replace(/\\/g, '/').replace(/^\.\//, '')
  for (const prefix of SKILL_PREFIXES) {
    if (p.startsWith(prefix)) return `skills/${p.slice(prefix.length)}`
  }
  for (const prefix of RULE_PREFIXES) {
    if (p.startsWith(prefix)) return `rules/${p.slice(prefix.length)}`
  }
  for (const prefix of COMMAND_PREFIXES) {
    if (p.startsWith(prefix)) return `commands/${p.slice(prefix.length)}`
  }
  if (p === '.cursor/hooks.json' || p === '.claude/hooks.json') return 'automation/hooks.json'
  if (MCP_BUNDLE_NAMES.has(p) || p.endsWith('/mcp.json')) return 'mcp.json'
  if (p === 'AGENTS.md' || p === 'CLAUDE.md' || p === 'AGENTS.override.md') return `rules/${p}`
  return p
}

function parseMcpJsonText(text: string): PackMcpEntry[] {
  let json: Record<string, unknown>
  try {
    json = JSON.parse(text) as Record<string, unknown>
  } catch {
    try {
      json = JSON5.parse(text) as Record<string, unknown>
    } catch {
      return []
    }
  }
  const servers = (json.mcpServers || json.mcp || {}) as Record<string, Record<string, unknown>>
  if (!servers || typeof servers !== 'object' || Array.isArray(servers)) return []
  const out: PackMcpEntry[] = []
  for (const [name, cfg] of Object.entries(servers)) {
    if (!cfg || typeof cfg !== 'object') continue
    const command = cfg.command
    const cmdStr = Array.isArray(command) ? String(command[0]) : typeof command === 'string' ? command : undefined
    const cmdArgs = Array.isArray(command)
      ? command.slice(1).map(String)
      : Array.isArray(cfg.args)
        ? (cfg.args as unknown[]).map(String)
        : undefined
    const entry: PackMcpEntry = { name }
    if (typeof cfg.url === 'string') {
      entry.url = cfg.url
      entry.type = String(cfg.type || 'http')
    } else if (cmdStr) {
      entry.command = cmdStr
      entry.type = String(cfg.type || 'stdio')
      if (cmdArgs?.length) entry.args = cmdArgs
    } else continue
    if (cfg.env && typeof cfg.env === 'object' && !Array.isArray(cfg.env)) {
      entry.env = cfg.env as Record<string, string>
    }
    out.push(entry)
  }
  return out
}

function mergeMcp(pack: PackDoc, extra: PackMcpEntry[]): PackDoc {
  const byName = new Map<string, PackMcpEntry>()
  for (const m of pack.tools?.mcp ?? []) {
    const n = String(m.name || '').trim()
    if (n) byName.set(n, m)
  }
  for (const m of extra) {
    const n = String(m.name || '').trim()
    if (n && !byName.has(n)) byName.set(n, m)
  }
  return { ...pack, tools: { ...pack.tools, mcp: [...byName.values()] } }
}

export function remapLegacyPack(pack: PackDoc): PackDoc {
  const files = pack.bundle?.files ?? []
  const mapped: Array<{ path: string; content: string }> = []
  const seen = new Set<string>()
  const mcpFromFiles: PackMcpEntry[] = []
  for (const f of files) {
    const original = f.path.replace(/\\/g, '/')
    if (MCP_BUNDLE_NAMES.has(original) || original.endsWith('/mcp.json') || original === 'mcp.json') {
      mcpFromFiles.push(...parseMcpJsonText(f.content))
    }
    const path = remapBundleFilePath(f.path)
    if (seen.has(path)) continue
    seen.add(path)
    mapped.push({ path, content: f.content })
  }
  const next: PackDoc = {
    ...pack,
    bundle: { portable: true, files: mapped },
    meta: {
      ...pack.meta,
      mappedTo: 'dsh',
      mappedFrom: pack.runtime?.id || pack.meta?.source || 'legacy-pack',
    },
  }
  return mergeMcp(next, mcpFromFiles)
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

async function readIfFile(p: string): Promise<string | null> {
  try {
    const st = await fs.stat(p)
    if (!st.isFile()) return null
    return await fs.readFile(p, 'utf8')
  } catch {
    return null
  }
}

async function listDir(p: string): Promise<string[]> {
  try {
    return await fs.readdir(p)
  } catch {
    return []
  }
}

async function ingestSourceTree(pack: PackDoc, sourceRoot: string): Promise<PackDoc> {
  const files = [...(pack.bundle?.files ?? [])]
  const seen = new Set(files.map(f => remapBundleFilePath(f.path)))

  const push = (path: string, content: string) => {
    const key = remapBundleFilePath(path)
    if (seen.has(key)) return
    seen.add(key)
    files.push({ path, content })
  }

  for (const skillRoot of ['.cursor/skills', '.claude/skills', '.agents/skills']) {
    const absRoot = join(sourceRoot, skillRoot)
    for (const name of await listDir(absRoot)) {
      const dir = join(absRoot, name)
      const skillMd = join(dir, 'SKILL.md')
      if (!(await exists(skillMd))) continue
      const entries = await fs.readdir(dir, { withFileTypes: true }).catch(() => [])
      for (const e of entries) {
        if (!e.isFile()) continue
        const content = await readIfFile(join(dir, e.name))
        if (content == null) continue
        push(`${skillRoot}/${name}/${e.name}`, content)
      }
    }
  }

  for (const mcpRel of ['.cursor/mcp.json', '.mcp.json', 'mcp.json', '.vscode/mcp.json']) {
    const content = await readIfFile(join(sourceRoot, mcpRel))
    if (content != null) push(mcpRel, content)
  }

  for (const ruleDir of ['.cursor/rules', '.claude/rules']) {
    const abs = join(sourceRoot, ruleDir)
    for (const name of await listDir(abs)) {
      if (!/\.(mdc|md)$/i.test(name)) continue
      const content = await readIfFile(join(abs, name))
      if (content != null) push(`${ruleDir}/${name}`, content)
    }
  }
  for (const rf of ['AGENTS.md', 'CLAUDE.md', 'AGENTS.override.md']) {
    const content = await readIfFile(join(sourceRoot, rf))
    if (content != null) push(rf, content)
  }

  for (const cmdDir of ['.cursor/commands', '.claude/commands']) {
    const abs = join(sourceRoot, cmdDir)
    for (const name of await listDir(abs)) {
      if (!/\.(md|txt)$/i.test(name)) continue
      const content = await readIfFile(join(abs, name))
      if (content != null) push(`${cmdDir}/${name}`, content)
    }
  }

  for (const hf of ['.cursor/hooks.json', '.claude/hooks.json']) {
    const content = await readIfFile(join(sourceRoot, hf))
    if (content != null) push(hf, content)
  }

  let next: PackDoc = { ...pack, bundle: { portable: true, files } }
  if ((pack.knowledge?.skills?.length ?? 0) > 0) {
    const embedded = await embedPortableFiles(next, sourceRoot)
    const merged = [...files]
    const mergedSeen = new Set(merged.map(f => remapBundleFilePath(f.path)))
    for (const f of embedded.bundle?.files ?? []) {
      const key = remapBundleFilePath(f.path)
      if (mergedSeen.has(key)) continue
      mergedSeen.add(key)
      merged.push(f)
    }
    next = { ...embedded, bundle: { portable: true, files: merged } }
    next = await embedExtendedBundleFiles(sourceRoot, next)
  }
  return next
}

export async function hydrateLegacyPack(pack: PackDoc, sourceRoot: string): Promise<PackDoc> {
  const ingested = await ingestSourceTree(pack, sourceRoot)
  return remapLegacyPack(ingested)
}

export { remapLegacyPack as mapLegacyPack }
