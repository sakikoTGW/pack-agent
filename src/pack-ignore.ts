/**
 * pack.ignore — gitignore 式路径过滤（打包时 exclude / 可选 ! 反选）。
 *
 * 默认：`.agent-pack/pack.ignore`；可在 project.yaml 用 `pack.ignore` 覆盖路径。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'

export type PackIgnoreRule = { pattern: string; negated: boolean; anchored: boolean }

export type PackIgnoreMatcher = {
  rules: PackIgnoreRule[]
  source?: string
}

const DEFAULT_IGNORE_REL = '.agent-pack/pack.ignore'

export function parsePackIgnore(content: string): PackIgnoreRule[] {
  const rules: PackIgnoreRule[] = []
  for (const raw of content.split(/\r?\n/)) {
    const line = raw.trim()
    if (!line || line.startsWith('#')) continue
    let negated = false
    let pat = line
    if (pat.startsWith('!')) {
      negated = true
      pat = pat.slice(1).trim()
    }
    if (!pat) continue
    const anchored = !pat.includes('/')
    rules.push({ pattern: pat.replace(/\\/g, '/'), negated, anchored })
  }
  return rules
}

function globToRegex(glob: string): RegExp {
  let re = '^'
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i]
    if (c === '*') {
      if (glob[i + 1] === '*') {
        re += '.*'
        i++
        if (glob[i + 1] === '/') i++
      } else {
        re += '[^/]*'
      }
      continue
    }
    if (c === '?') {
      re += '[^/]'
      continue
    }
    if ('.+^${}()|[]\\'.includes(c)) re += '\\' + c
    else re += c
  }
  re += '$'
  return new RegExp(re)
}

function matchOne(path: string, rule: PackIgnoreRule): boolean {
  const p = path.replace(/\\/g, '/').replace(/^\.\/+/, '')
  const pat = rule.pattern.replace(/^\.\/+/, '')
  const fullPat = pat.startsWith('**/') ? pat : pat.includes('/') ? pat : `**/${pat}`
  const re = globToRegex(fullPat)
  if (re.test(p)) return true
  const base = p.split('/').pop() ?? p
  if (pat.includes('/')) return false
  return base === pat || globToRegex(pat).test(base)
}

/** true = 应排除（不打包） */
export function isPackIgnored(relativePath: string, matcher: PackIgnoreMatcher): boolean {
  const p = relativePath.replace(/\\/g, '/').replace(/^\.\/+/, '')
  let ignored = false
  for (const rule of matcher.rules) {
    if (matchOne(p, rule)) ignored = !rule.negated
  }
  return ignored
}

export async function loadPackIgnore(cwd: string, ignorePath?: string): Promise<PackIgnoreMatcher> {
  const candidates = [
    ignorePath ? join(cwd, ignorePath) : join(cwd, DEFAULT_IGNORE_REL),
    join(cwd, '.agentpackignore'),
    join(cwd, '.packignore'),
  ]
  for (const abs of candidates) {
    try {
      const text = await fs.readFile(abs, 'utf8')
      return { rules: parsePackIgnore(text), source: abs }
    } catch {
      /* try next */
    }
  }
  return { rules: parsePackIgnore(DEFAULT_PACK_IGNORE_TEMPLATE), source: '(built-in defaults)' }
}

function relRef(cwd: string, ref: string): string {
  const normCwd = cwd.replace(/\\/g, '/').replace(/\/$/, '')
  let p = ref.replace(/\\/g, '/')
  if (p.toLowerCase().startsWith(normCwd.toLowerCase() + '/')) {
    p = p.slice(normCwd.length + 1)
  }
  return p.replace(/^\.\/+/, '')
}

export function filterByIgnore<T>(
  items: T[],
  refOf: (item: T) => string,
  matcher: PackIgnoreMatcher,
  cwd?: string,
): T[] {
  return items.filter(item => {
    const ref = refOf(item)
    const rel = cwd ? relRef(cwd, ref) : ref.replace(/\\/g, '/').replace(/^\.\/+/, '')
    return !isPackIgnored(rel, matcher)
  })
}

/** 内置默认：隐私 / 密钥 / 大体积 ephemeral */
export const DEFAULT_PACK_IGNORE_TEMPLATE = `# agent-pack default ignore (gitignore-style)
# Lines starting with # are comments. Use !pattern to re-include.

# Secrets & env
.env
.env.*
**/.env
**/*.pem
**/*.key
**/credentials.json

# Local overrides
**/*.local.md
**/*.local.json
.claude/settings.local.json

# VCS & deps
**/.git/**
node_modules/**

# Ephemeral capture (merged via --capture-as, not as L1 files)
.ccui/packs/**
.agent-pack/capture/**
.agent-pack/staging/**
.agent-pack/exports/**

# Session archives (opt-in: modules.transcripts)
**/transcripts/**
**/.cursor/projects/**/agent-transcripts/**

# Personal memory (opt-in: modules.memory — remove lines below to pack)
MEMORY.md
USER.md
.claude/projects/**/memory/**

# Pack state (never re-pack lock/staging)
.agent-pack/lock.json
.agent-pack/experiences/**
`

export async function ensureDefaultPackIgnore(cwd: string, stateDir = '.agent-pack'): Promise<string> {
  const path = join(cwd, stateDir, 'pack.ignore')
  try {
    await fs.access(path)
    return path
  } catch {
    /* create */
  }
  await fs.mkdir(join(cwd, stateDir), { recursive: true })
  await fs.writeFile(path, DEFAULT_PACK_IGNORE_TEMPLATE, 'utf8')
  return path
}
