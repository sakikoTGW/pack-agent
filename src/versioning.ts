/**
 * 组件版本解析：skill frontmatter、内容 hash、MCP 包识别。
 */
import { createHash } from 'node:crypto'
import { promises as fs } from 'node:fs'
import { basename, dirname, join, relative } from 'node:path'
import type { PackDoc, PackMcpEntry, PackRuleEntry, PackSkillEntry } from './types.js'

export const PACK_SCHEMA_V02 = 'ccui-pack/v0.2'
export const LOCK_SCHEMA = 'agent-pack/lock/v1'

export function sha256Short(input: string, len = 16): string {
  return createHash('sha256').update(input).digest('hex').slice(0, len)
}

export function sha256Full(input: string): string {
  return `sha256:${createHash('sha256').update(input).digest('hex')}`
}

/** 解析 SKILL.md YAML frontmatter（轻量，无依赖） */
export function parseSkillFrontmatter(content: string): Record<string, string> {
  const m = content.match(/^---\r?\n([\s\S]*?)\r?\n---/)
  if (!m) return {}
  const out: Record<string, string> = {}
  for (const line of m[1].split('\n')) {
    const trimmed = line.trim()
    if (!trimmed || trimmed.startsWith('#')) continue
    const kv = trimmed.match(/^([a-zA-Z0-9_-]+):\s*(.+)$/)
    if (!kv) continue
    let v = kv[2].trim()
    if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) {
      v = v.slice(1, -1)
    }
    out[kv[1]] = v
  }
  return out
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

async function walkDirFiles(dir: string, base = dir): Promise<Array<{ path: string; content: string }>> {
  const out: Array<{ path: string; content: string }> = []
  let entries: import('node:fs').Dirent[] = []
  try {
    entries = await fs.readdir(dir, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    const abs = join(dir, e.name)
    if (e.isDirectory()) {
      out.push(...(await walkDirFiles(abs, base)))
      continue
    }
    if (!e.isFile()) continue
    try {
      out.push({
        path: relative(base, abs).replace(/\\/g, '/'),
        content: await fs.readFile(abs, 'utf8'),
      })
    } catch {
      /* skip */
    }
  }
  return out
}

export async function resolveSkillDirFromRef(cwd: string, ref: string): Promise<string | null> {
  const abs = ref.match(/^[a-zA-Z]:/) || ref.startsWith('/') ? ref : join(cwd, ref)
  const dir = abs.endsWith('SKILL.md') ? dirname(abs) : abs
  return (await exists(join(dir, 'SKILL.md'))) ? dir : null
}

/** 扫描 skill 目录 → 版本元数据 */
export async function resolveSkillVersion(
  cwd: string,
  skill: Pick<PackSkillEntry, 'name' | 'ref'>,
): Promise<Pick<PackSkillEntry, 'version' | 'contentHash' | 'fileCount' | 'license' | 'description'>> {
  const dir = skill.ref ? await resolveSkillDirFromRef(cwd, skill.ref) : null
  if (!dir) {
    return { version: '0.0.0', contentHash: undefined, fileCount: 0 }
  }

  const files = await walkDirFiles(dir)
  files.sort((a, b) => a.path.localeCompare(b.path))
  const payload = files.map(f => `${f.path}\n${f.content}`).join('\n---\n')
  const contentHash = sha256Full(payload)

  const skillMd = files.find(f => f.path === 'SKILL.md')?.content ?? ''
  const fm = parseSkillFrontmatter(skillMd)
  const version = fm.version?.trim() || `0.0.0+${sha256Short(contentHash, 12)}`

  return {
    version,
    contentHash,
    fileCount: files.length,
    license: fm.license,
    description: fm.description,
  }
}

/** 从 bundle 内 skill 文件算 hash（便携 pack 无 ref 时） */
export function hashSkillFromBundle(
  pack: PackDoc,
  skillName: string,
): { contentHash: string; fileCount: number } | null {
  const prefix = `skills/${skillName}/`
  const files = (pack.bundle?.files ?? []).filter(f => f.path.startsWith(prefix))
  if (!files.length) return null
  const sorted = [...files].sort((a, b) => a.path.localeCompare(b.path))
  const payload = sorted.map(f => `${f.path}\n${f.content}`).join('\n---\n')
  return { contentHash: sha256Full(payload), fileCount: files.length }
}

export async function resolveRuleVersion(
  cwd: string,
  rule: Pick<PackRuleEntry, 'name' | 'ref'>,
): Promise<Pick<PackRuleEntry, 'version' | 'contentHash'>> {
  if (!rule.ref) return { version: '0.0.0' }
  const abs = rule.ref.match(/^[a-zA-Z]:/) || rule.ref.startsWith('/') ? rule.ref : join(cwd, rule.ref)
  try {
    const content = await fs.readFile(abs, 'utf8')
    const contentHash = sha256Full(content)
    const m = content.match(/^---\r?\n[\s\S]*?version:\s*["']?([^\s"']+)["']?/m)
    return { version: m?.[1] ?? `0.0.0+${sha256Short(contentHash, 12)}`, contentHash }
  } catch {
    return { version: '0.0.0' }
  }
}

/** 识别 npx/bunx 包名与版本约束 */
export function resolveMcpPackageMeta(mcp: PackMcpEntry): Pick<PackMcpEntry, 'package' | 'packageVersion' | 'configHash'> {
  const cfg = JSON.stringify({
    type: mcp.type ?? 'stdio',
    command: mcp.command,
    args: mcp.args ?? [],
    url: mcp.url,
    env: mcp.env ?? {},
  })
  const configHash = sha256Full(cfg)

  let pkg: string | undefined
  let pkgVer: string | undefined
  const args = mcp.args ?? []
  const cmd = (mcp.command ?? '').toLowerCase()

  if (cmd && !['npx', 'bunx', 'pnpm', 'yarn', 'node'].includes(cmd)) {
    pkg = mcp.command
  } else {
    for (const a of args) {
    if (['-y', '--yes', '-p', '--package'].includes(a)) continue
    if (!/^[@a-z0-9][\w.@/-]*$/i.test(a)) continue
    if (a.startsWith('@') && a.includes('/')) {
      const rest = a.slice(1)
      const slash = rest.indexOf('/')
      const scope = rest.slice(0, slash)
      const tail = rest.slice(slash + 1)
      const at = tail.lastIndexOf('@')
      if (at > 0) {
        pkg = `@${scope}/${tail.slice(0, at)}`
        pkgVer = tail.slice(at + 1)
      } else {
        pkg = `@${scope}/${tail}`
      }
    } else if (a.includes('@')) {
      const at = a.lastIndexOf('@')
      pkg = a.slice(0, at)
      pkgVer = a.slice(at + 1)
    } else {
      pkg = a
    }
    break
    }
  }

  if (!pkg && mcp.command && !mcp.url) pkg = mcp.command

  return { package: pkg, packageVersion: pkgVer, configHash }
}

export async function tryResolveMcpPackageVersion(cwd: string, pkg: string): Promise<string | undefined> {
  const candidates = [
    join(cwd, 'node_modules', ...pkg.split('/'), 'package.json'),
    join(cwd, 'gui', 'node_modules', ...pkg.split('/'), 'package.json'),
  ]
  for (const p of candidates) {
    try {
      const j = JSON.parse(await fs.readFile(p, 'utf8')) as { version?: string }
      if (j.version) return j.version
    } catch {
      /* next */
    }
  }
  return undefined
}

export function hashPackBundle(pack: PackDoc): string | undefined {
  const files = pack.bundle?.files ?? []
  if (!files.length) return undefined
  const sorted = [...files].sort((a, b) => a.path.localeCompare(b.path))
  const payload = sorted.map(f => `${f.path}\n${f.content}`).join('\n---\n')
  return sha256Full(payload)
}

/** 为 pack 填充 v0.2 组件版本与 resolution */
export async function enrichPackVersions(
  cwd: string,
  pack: PackDoc,
  opts: { cliVersion?: string; minPackCli?: string } = {},
): Promise<PackDoc> {
  const skills: PackSkillEntry[] = []
  for (const s of pack.knowledge?.skills ?? []) {
    const name = String(s.name || '')
    let meta: Partial<PackSkillEntry> = {}
    if (s.ref) {
      meta = await resolveSkillVersion(cwd, { name, ref: s.ref })
    } else {
      const fromBundle = hashSkillFromBundle(pack, name)
      if (fromBundle) {
        meta = {
          contentHash: fromBundle.contentHash,
          fileCount: fromBundle.fileCount,
          version: s.version ?? `0.0.0+${sha256Short(fromBundle.contentHash, 12)}`,
        }
      }
    }
    skills.push({ ...s, ...meta, name: name || s.name })
  }

  const rules: PackRuleEntry[] = []
  for (const r of pack.knowledge?.rules ?? []) {
    const meta = r.ref ? await resolveRuleVersion(cwd, r) : {}
    rules.push({ ...r, ...meta })
  }

  const mcp: PackMcpEntry[] = []
  for (const m of pack.tools?.mcp ?? []) {
    const meta = resolveMcpPackageMeta(m)
    let installed = meta.packageVersion
    if (!installed && meta.package) {
      installed = await tryResolveMcpPackageVersion(cwd, meta.package)
    }
    mcp.push({
      ...m,
      ...meta,
      version: m.version ?? installed ?? meta.packageVersion ?? '0.0.0',
      packageVersion: installed ?? meta.packageVersion,
    })
  }

  const packContentHash = hashPackBundle(pack)

  return {
    ...pack,
    schema: PACK_SCHEMA_V02,
    knowledge: { skills, rules },
    tools: { ...pack.tools, mcp },
    resolution: {
      lockedAt: new Date().toISOString(),
      packContentHash,
      agentPackCli: opts.cliVersion ?? '0.1.0',
      minPackCli: opts.minPackCli ?? pack.resolution?.minPackCli,
      skillCount: skills.length,
      ruleCount: rules.length,
      mcpCount: mcp.length,
      experienceCount: pack.experiences?.length ?? 0,
      hookCount: pack.automation?.hooks?.length ?? 0,
      subagentCount: pack.agents?.subagents?.length ?? 0,
      memoryCount: pack.memory?.files?.length ?? 0,
      modules: pack.modules,
      captureDeliver: pack.policy?.captureAs,
    },
    meta: {
      ...pack.meta,
      schemaVersion: '0.2',
    },
  }
}
