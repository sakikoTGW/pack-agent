/**
 * 安装冲突 — stop 默认硬停；skip / replace 由调用方显式选择。
 */
import type { ConflictPolicy } from './types.js'

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

export const CONFLICT_POLICIES: ConflictPolicy[] = ['stop', 'skip', 'replace']

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

export type ConflictResolution = 'skip' | 'replace'

export class PackConflictError extends Error {
  readonly detail: PackConflictDetail
  readonly choices = CONFLICT_POLICIES
  readonly retryHint: string

  constructor(detail: PackConflictDetail) {
    super(formatPackConflict(detail))
    this.name = 'PackConflictError'
    this.detail = detail
    this.retryHint = conflictRetryHint(detail)
  }
}

export function conflictRetryHint(detail: PackConflictDetail): string {
  const base = detail.skillName
    ? `retry with on_conflict=skip|replace (e.g. agent-pack install --on-conflict skip)`
    : `retry with on_conflict=skip|replace`
  return base
}

export function formatPackConflict(d: PackConflictDetail): string {
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
  lines.push('  = choices:')
  lines.push('      stop   — abort install (default)')
  lines.push('      skip   — leave existing file, continue with the rest')
  lines.push('      replace — overwrite conflicting target, then continue')
  lines.push(`  = retry: ${conflictRetryHint(d)}`)
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
  runtime?: string
  ownerPack?: string
  expectedHash?: string
  actualHash?: string
}): PackConflictDetail {
  const rel = opts.dest.replace(/\\/g, '/')
  const ctx: string[] = [`pack \`${opts.packName}\` wants to install skill \`${opts.skillName}\``]
  let summary: string
  const help: string[] = []

  if (opts.kind === 'skill-handcrafted') {
    summary = `skill directory already exists without agent-pack origin marker`
    ctx.push(`path \`${rel}\` looks hand-crafted or installed outside agent-pack`)
    help.push(`on_conflict=replace to overwrite with pack contents`)
    help.push(`on_conflict=skip to keep the existing directory`)
  } else if (opts.kind === 'skill-ownership') {
    summary = `skill directory owned by a different pack`
    ctx.push(`existing owner: pack \`${opts.ownerPack ?? 'unknown'}\``)
    ctx.push(`requested owner: pack \`${opts.packName}\``)
    help.push(`on_conflict=replace to overwrite (destructive)`)
    help.push(`or eject pack \`${opts.ownerPack}\` first`)
  } else {
    summary = `skill content version conflict for the same pack`
    ctx.push(`pack \`${opts.packName}\` bundle hash ≠ installed hash`)
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
  return {
    ok: false as const,
    conflict: true as const,
    detail,
    choices: CONFLICT_POLICIES,
    retryHint: conflictRetryHint(detail),
  }
}
