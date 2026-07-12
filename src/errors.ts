/**
 * 统一错误分类 — 不是所有"报错"都该给 stop/skip/replace 菜单：
 *
 *   conflict — 安装时磁盘/配置里已有东西挡路，skip/replace 是真实可行的解法。
 *   input    — 用户给的文件本身有问题（不存在 / 语法错 / schema 不对）。修法是"改文件"，
 *              不是 on_conflict；--on_conflict skip|replace 对这类错误完全无意义。
 *   usage    — 命令用法/前置条件没满足（没指定 agent、CLI 版本太老、依赖没打进包）。
 *              修法是换参数/换操作，同样不是 on_conflict 能解的。
 *
 * 像 Rust 的 Result<T,E> 一样：每种失败模式都该有自己的分支和对应的修复指引，
 * 而不是所有 kind 共用同一套「stop/skip/replace」footer——那套 footer 只对 conflict 成立。
 */
import type { ConflictPolicy } from './types.js'

export type PackConflictCategory = 'conflict' | 'input' | 'usage'

export type PackConflictKind =
  | 'skill-handcrafted'
  | 'skill-ownership'
  | 'skill-version'
  | 'mcp-server'
  | 'requires-unmet'
  | 'skill-unresolved'
  | 'min-pack-cli'
  | 'agent-required'
  | 'agent-unknown'
  | 'agent-empty'
  /** 引用的文件不存在（pack.json / manifest / --from 等） */
  | 'file-not-found'
  /** 文件存在但解析失败（JSON/YAML 语法错、或结构不对） */
  | 'file-invalid'

export const CONFLICT_POLICIES: ConflictPolicy[] = ['stop', 'skip', 'replace']

const CATEGORY_BY_KIND: Record<PackConflictKind, PackConflictCategory> = {
  'skill-handcrafted': 'conflict',
  'skill-ownership': 'conflict',
  'skill-version': 'conflict',
  'mcp-server': 'conflict',
  'requires-unmet': 'usage',
  'skill-unresolved': 'usage',
  'min-pack-cli': 'usage',
  'agent-required': 'usage',
  'agent-unknown': 'usage',
  'agent-empty': 'usage',
  'file-not-found': 'input',
  'file-invalid': 'input',
}

export function categoryOf(kind: PackConflictKind): PackConflictCategory {
  return CATEGORY_BY_KIND[kind] ?? 'usage'
}

export type PackConflictDetail = {
  kind: PackConflictKind
  summary: string
  path?: string
  runtime?: string
  packName?: string
  skillName?: string
  serverName?: string
  context?: string[]
  help: string[]
}

/** 「文件缺失 / 文件损坏」的通用构造器——统一格式，避免每处调用点各写一套 message */
export function buildFileErrorDetail(opts: {
  /** 缺失用 file-not-found；存在但解析/校验失败用 file-invalid */
  kind: 'file-not-found' | 'file-invalid'
  /** 这个文件是干什么的，给用户一句话上下文，如 "agents.yaml" / "select manifest" / "pack lock" */
  what: string
  path: string
  /** 原始错误（ENOENT / SyntaxError / YAMLParseError…），用于展示真实原因，不能吞掉 */
  cause?: Error
  help?: string[]
}): PackConflictDetail {
  const rel = opts.path.replace(/\\/g, '/')
  const ctx: string[] = []
  let summary: string
  if (opts.kind === 'file-not-found') {
    summary = `${opts.what} not found`
    ctx.push(`expected at \`${rel}\``)
  } else {
    summary = `${opts.what} exists but could not be parsed`
    ctx.push(`path: \`${rel}\``)
    if (opts.cause?.message) ctx.push(`parser said: ${opts.cause.message.split('\n')[0]}`)
  }
  return {
    kind: opts.kind,
    summary,
    path: rel,
    context: ctx,
    help: opts.help ?? [],
  }
}

export type ConflictResolution = 'skip' | 'replace'

export class PackConflictError extends Error {
  readonly detail: PackConflictDetail
  readonly category: PackConflictCategory
  /** 只有 category === 'conflict' 时这三个才是真实可选项；其它 category 下永远是空数组，调用方不该再提示 on_conflict */
  readonly choices: ConflictPolicy[]
  readonly retryHint: string

  constructor(detail: PackConflictDetail) {
    super(formatPackConflict(detail))
    this.name = 'PackConflictError'
    this.detail = detail
    this.category = categoryOf(detail.kind)
    this.choices = this.category === 'conflict' ? CONFLICT_POLICIES : []
    this.retryHint = conflictRetryHint(detail)
  }
}

export function conflictRetryHint(detail: PackConflictDetail): string {
  if (categoryOf(detail.kind) !== 'conflict') {
    return detail.help[0] ?? 'see help above'
  }
  return detail.skillName
    ? `retry with on_conflict=skip|replace (e.g. agent-pack install --on-conflict skip)`
    : `retry with on_conflict=skip|replace`
}

export function formatPackConflict(d: PackConflictDetail): string {
  const category = categoryOf(d.kind)
  const lines: string[] = []
  lines.push(`error: ${d.summary}`)
  if (d.path) {
    lines.push(`  --> ${d.path.replace(/\\/g, '/')}`)
  }
  lines.push('')
  if (d.context?.length) {
    for (const c of d.context) {
      lines.push(`  | ${c}`)
    }
    lines.push('  |')
  }
  if (d.help.length) {
    lines.push('  = help:')
    for (const h of d.help) {
      lines.push(`      ${h}`)
    }
  }
  // stop/skip/replace 只对「安装时磁盘上已有东西挡路」成立；文件缺失/损坏、
  // 用法/前置条件没满足这两类错误，on_conflict 完全无法修复，不该暗示用户去试。
  if (category === 'conflict') {
    lines.push('  = choices:')
    lines.push('      stop   — abort install (default)')
    lines.push('      skip   — leave existing file, continue with the rest')
    lines.push('      replace — overwrite conflicting target, then continue')
    lines.push(`  = retry: ${conflictRetryHint(d)}`)
  }
  return lines.join('\n')
}

/** stop → throw；skip / replace → 返回动作 */
export function resolveInstallConflict(
  policy: ConflictPolicy,
  detail: PackConflictDetail,
): ConflictResolution {
  if (policy === 'stop') {
    throw new PackConflictError(detail)
  }
  if (policy === 'skip') return 'skip'
  return 'replace'
}

export function buildSkillConflictDetail(opts: {
  kind: 'skill-handcrafted' | 'skill-ownership' | 'skill-version'
  dest: string
  skillName: string
  packName: string
  packVersion?: string
  runtime?: string
  ownerPack?: string
  ownerVersion?: string
  expectedHash?: string
  actualHash?: string
}): PackConflictDetail {
  const rel = opts.dest.replace(/\\/g, '/')
  const wantedVer = opts.packVersion ? `@${opts.packVersion}` : ''
  const ctx: string[] = [`pack \`${opts.packName}${wantedVer}\` wants to install skill \`${opts.skillName}\``]
  let summary: string
  const help: string[] = []

  if (opts.kind === 'skill-handcrafted') {
    summary = `skill directory already exists without agent-pack origin marker`
    ctx.push(`path \`${rel}\` looks hand-crafted or installed outside agent-pack`)
    help.push(`on_conflict=replace to overwrite with pack contents`)
    help.push(`on_conflict=skip to keep the existing directory`)
  } else if (opts.kind === 'skill-ownership') {
    const ownerVer = opts.ownerVersion ? `@${opts.ownerVersion}` : ''
    summary = `skill directory owned by a different pack (content differs — same-content collisions are auto-merged, no error)`
    ctx.push(`existing owner: pack \`${opts.ownerPack ?? 'unknown'}${ownerVer}\``)
    ctx.push(`requested owner: pack \`${opts.packName}${wantedVer}\``)
    help.push(`on_conflict=replace to overwrite (destructive — the other pack's version of this skill is lost)`)
    help.push(`or eject pack \`${opts.ownerPack}\` first`)
    help.push(`if this should be the same skill, re-export both packs so contentHash matches — identical content never conflicts`)
  } else {
    summary = `skill content version conflict for the same pack`
    ctx.push(`pack \`${opts.packName}${wantedVer}\` bundle hash ≠ installed hash`)
    if (opts.expectedHash) ctx.push(`bundle: ${opts.expectedHash}`)
    if (opts.actualHash) ctx.push(`installed: ${opts.actualHash}`)
    help.push(`on_conflict=replace to install bundle version`)
  }

  return {
    kind: opts.kind,
    summary,
    path: rel,
    runtime: opts.runtime,
    packName: opts.packName,
    skillName: opts.skillName,
    context: ctx,
    help,
  }
}

export function buildMcpConflictDetail(opts: {
  serverName: string
  configFile: string
  runtime?: string
  packName?: string
}): PackConflictDetail {
  const file = opts.configFile.replace(/\\/g, '/')
  return {
    kind: 'mcp-server',
    summary: `MCP server \`${opts.serverName}\` already defined in config`,
    path: file,
    runtime: opts.runtime,
    packName: opts.packName,
    serverName: opts.serverName,
    context: [
      `pack wants to register MCP server \`${opts.serverName}\``,
      `but \`${file}\` already contains that server name with different config`,
    ],
    help: [
      `on_conflict=replace to overwrite the server entry`,
      `on_conflict=skip to keep the existing entry`,
    ],
  }
}

export function conflictPayload(detail: PackConflictDetail) {
  const category = categoryOf(detail.kind)
  return {
    ok: false as const,
    conflict: category === 'conflict',
    category,
    detail,
    choices: category === 'conflict' ? CONFLICT_POLICIES : [],
    retryHint: conflictRetryHint(detail),
  }
}
