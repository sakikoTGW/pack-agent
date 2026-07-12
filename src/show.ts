/**
 * 整合包内容浏览 —— 装之前先看清楚"包里有什么"，就像 CurseForge 装整合包前看 mod 列表。
 * 只读，不碰磁盘（除了读 pack 文件本身）。
 */
import type { PackDoc } from './types.js'
import { readPackFile } from './portable.js'

export type PackContentItem = {
  name: string
  version?: string
  description?: string
  detail?: string
}

export type PackShow = {
  name: string
  version: string
  author?: string
  description?: string
  agent?: { id: string; harness?: string }
  runtime?: string
  channel?: string
  fidelity?: string
  portable: boolean
  bundleFileCount: number
  captureDeliver?: string
  skills: PackContentItem[]
  rules: PackContentItem[]
  mcp: PackContentItem[]
  experiences: PackContentItem[]
  requires: string[]
  minAgentPackCli?: string
  totalItems: number
}

export function describePack(pack: PackDoc): PackShow {
  const skills: PackContentItem[] = (pack.knowledge?.skills ?? []).map(s => ({
    name: String(s.name || '(unnamed)'),
    version: s.version,
    description: s.description,
    detail: s.requires?.length ? `requires: ${s.requires.join(', ')}` : undefined,
  }))
  const rules: PackContentItem[] = (pack.knowledge?.rules ?? []).map(r => ({
    name: String(r.name || '(unnamed)'),
    version: r.version,
    detail: r.format,
  }))
  const mcp: PackContentItem[] = (pack.tools?.mcp ?? []).map(m => ({
    name: String(m.name || '(unnamed)'),
    version: m.version,
    detail: m.url ? `url: ${m.url}` : [m.command, ...(m.args ?? [])].filter(Boolean).join(' '),
  }))
  const experiences: PackContentItem[] = (pack.experiences ?? []).map(e => ({
    name: e.name || e.id,
    version: e.version,
    detail: [e.kind, e.scope].filter(Boolean).join('/') || undefined,
  }))
  const requires = [...new Set((pack.knowledge?.skills ?? []).flatMap(s => s.requires ?? []))]

  return {
    name: pack.name || 'unnamed-pack',
    version: pack.version || '0.0.0',
    author: pack.author,
    description: pack.description,
    agent: pack.agent,
    runtime: pack.runtime?.id,
    channel: pack.channel,
    fidelity: String(pack.meta?.fidelity ?? 'L1'),
    portable: Boolean(pack.bundle?.portable),
    bundleFileCount: pack.bundle?.files?.length ?? 0,
    captureDeliver: pack.policy?.captureAs,
    skills,
    rules,
    mcp,
    experiences,
    requires,
    minAgentPackCli: pack.resolution?.minPackCli,
    totalItems: skills.length + rules.length + mcp.length + experiences.length,
  }
}

export async function describePackFile(path: string): Promise<PackShow> {
  const pack = await readPackFile(path)
  return describePack(pack)
}

function section(title: string, items: PackContentItem[]): string {
  if (!items.length) return `${title} (0)`
  const lines = items.map(it => {
    const v = it.version ? ` v${it.version}` : ''
    const d = it.description ? ` — ${it.description}` : it.detail ? ` — ${it.detail}` : ''
    return `  - ${it.name}${v}${d}`
  })
  return `${title} (${items.length}):\n${lines.join('\n')}`
}

/** 人类可读的整合包内容清单（类似整合包 mod 列表） */
export function formatPackShow(show: PackShow): string {
  const header = [
    `📦 ${show.name}  v${show.version}   [fidelity: ${show.fidelity}]`,
    show.author || show.description
      ? `   ${[show.author && `by ${show.author}`, show.description].filter(Boolean).join(' — ')}`
      : undefined,
    show.agent ? `   agent: ${show.agent.id}${show.agent.harness ? ` (harness: ${show.agent.harness})` : ''}` : undefined,
  ]
    .filter(Boolean)
    .join('\n')

  const body = [
    section('Skills', show.skills),
    section('Rules', show.rules),
    section('MCP tools', show.mcp),
    section('Experiences', show.experiences),
  ].join('\n\n')

  const footer = [
    `Bundle: ${show.portable ? `portable (${show.bundleFileCount} files embedded)` : 'NOT portable (ref-only; will fail on a different machine)'}`,
    show.requires.length ? `Requires skills: ${show.requires.join(', ')}` : undefined,
    show.minAgentPackCli ? `Requires agent-pack CLI >= ${show.minAgentPackCli}` : undefined,
    show.captureDeliver ? `Capture deliver: ${show.captureDeliver}` : undefined,
  ]
    .filter(Boolean)
    .join('\n')

  return [header, '', body, '', footer].join('\n')
}
