/**
 * MCP / Hermes 投射（install 子集）—— 各家 MCP 容器格式不同，统一 merge。
 * 参考 agents-anywhere mcp.json → 各引擎 native 格式的思路。
 */
import { buildMcpConflictDetail, resolveInstallConflict, type PackConflictDetail } from './errors.js'
import type { ConflictPolicy } from './types.js'
import { promises as fs } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join } from 'node:path'
import { parse as yamlParse, stringify as yamlStringify } from 'yaml'
import JSON5 from 'json5'

export type McpFormat =
  | 'json-mcpServers'
  | 'json-mcp'
  | 'json5-openclaw'
  | 'yaml-mcp_servers'
  | 'toml-mcp_servers'

export type McpTarget = {
  absFile: string
  projectLocal: boolean
  format: McpFormat
}

export type McpServers = Record<string, Record<string, unknown>>
export type MergeResult = { added: string[]; unchanged: string[]; file: string; format: McpFormat }

export type MergeMcpOpts = {
  packName?: string
  runtime?: string
  onConflict?: ConflictPolicy
  onResolved?: (action: 'skip' | 'replace', detail: PackConflictDetail) => void
}

const MARKER = 'agent-pack-mcp'

function home(): string {
  return homedir()
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

/** 按 runtime 选 MCP 写入目标（项目内优先可创建；全局仅已存在才写） */
export function mcpTargetFor(runtime: string, cwd: string): McpTarget {
  switch (runtime) {
    case 'openclaw':
      return { absFile: join(home(), '.openclaw', 'openclaw.json'), projectLocal: false, format: 'json5-openclaw' }
    case 'hermes':
      return { absFile: join(home(), '.hermes', 'config.yaml'), projectLocal: false, format: 'yaml-mcp_servers' }
    case 'astrbot':
      return { absFile: join(cwd, 'data', 'mcp_server.json'), projectLocal: true, format: 'json-mcpServers' }
    case 'codex':
      return { absFile: join(cwd, '.codex', 'config.toml'), projectLocal: true, format: 'toml-mcp_servers' }
    case 'opencode':
      return { absFile: join(cwd, 'opencode.json'), projectLocal: true, format: 'json-mcp' }
    case 'gemini-cli':
      return { absFile: join(cwd, '.gemini', 'settings.json'), projectLocal: true, format: 'json-mcpServers' }
    case 'windsurf':
      return { absFile: join(cwd, '.windsurf', 'mcp_config.json'), projectLocal: true, format: 'json-mcpServers' }
    case 'github-copilot':
      return { absFile: join(cwd, '.vscode', 'mcp.json'), projectLocal: true, format: 'json-mcpServers' }
    case 'claude-code':
    default:
      return { absFile: join(cwd, '.mcp.json'), projectLocal: true, format: 'json-mcpServers' }
  }
}

function tomlMcpBlock(name: string, cfg: Record<string, unknown>): string {
  const q = (v: unknown) => JSON.stringify(v)
  const lines = [`# >>> ${MARKER}:${name} >>>`, `[mcp_servers.${name}]`]
  if (cfg.url) lines.push(`url = ${q(cfg.url)}`)
  else {
    if (cfg.command) lines.push(`command = ${q(cfg.command)}`)
    if (Array.isArray(cfg.args)) lines.push(`args = ${q(cfg.args)}`)
  }
  if (cfg.env && typeof cfg.env === 'object') {
    lines.push(`[mcp_servers.${name}.env]`)
    for (const [k, v] of Object.entries(cfg.env as Record<string, unknown>)) lines.push(`${k} = ${q(v)}`)
  }
  lines.push(`# <<< ${MARKER}:${name} <<<`)
  return lines.join('\n')
}

function stripTomlMarkerBlocks(text: string, names: string[]): string {
  let out = text
  for (const n of names) {
    const re = new RegExp(`\\n?# >>> ${MARKER}:${n} >>>[\\s\\S]*?# <<< ${MARKER}:${n} <<<\\n?`, 'g')
    out = out.replace(re, '\n')
  }
  return out
}

function mcpEntryEqual(a: Record<string, unknown>, b: Record<string, unknown>): boolean {
  return JSON.stringify(a) === JSON.stringify(b)
}

export async function mergeMcp(
  target: McpTarget,
  servers: McpServers,
  opts: MergeMcpOpts = {},
): Promise<MergeResult> {
  const names = Object.keys(servers)
  const out: MergeResult = { added: [], unchanged: [], file: target.absFile, format: target.format }
  if (!names.length) return out

  const policy = opts.onConflict ?? 'stop'

  const handleMcpConflict = (serverName: string): 'skip' | 'replace' => {
    const detail = buildMcpConflictDetail({
      serverName,
      configFile: target.absFile,
      runtime: opts.runtime,
      packName: opts.packName,
    })
    const action = resolveInstallConflict(policy, detail)
    opts.onResolved?.(action, detail)
    return action
  }

  if (target.format === 'toml-mcp_servers') {
    const fileExists = await exists(target.absFile)
    if (!fileExists) {
      await fs.mkdir(dirname(target.absFile), { recursive: true })
      await fs.writeFile(target.absFile, '', 'utf8')
    }
    let text = await fs.readFile(target.absFile, 'utf8')
    for (const n of names) {
      if (new RegExp(`(^|\\n)\\s*\\[mcp_servers\\.${n}\\]`).test(text)) {
        const action = handleMcpConflict(n)
        if (action === 'skip') {
          out.unchanged.push(n)
          continue
        }
        text = stripTomlMarkerBlocks(text, [n])
        text = text.replace(new RegExp(`(^|\\n)\\s*\\[mcp_servers\\.${n}\\][\\s\\S]*?(?=\\n\\[|$)`, 'g'), '\n')
      }
      text += `\n${tomlMcpBlock(n, servers[n])}\n`
      out.added.push(n)
    }
    if (out.added.length) await fs.writeFile(target.absFile, text, 'utf8')
    return out
  }

  const fileExists = await exists(target.absFile)
  if (!target.projectLocal && !fileExists) {
    return out
  }

  const withActive = (cfg: Record<string, unknown>) => ({ ...cfg, active: true })

  if (target.format === 'json-mcpServers') {
    const json = fileExists
      ? (JSON.parse(await fs.readFile(target.absFile, 'utf8')) as { mcpServers?: McpServers })
      : { mcpServers: {} }
    if (!json.mcpServers || typeof json.mcpServers !== 'object') json.mcpServers = {}
    for (const n of names) {
      const next = withActive(servers[n])
      const prev = json.mcpServers[n]
      if (prev) {
        if (mcpEntryEqual(prev, next)) {
          out.unchanged.push(n)
          continue
        }
        const action = handleMcpConflict(n)
        if (action === 'skip') {
          out.unchanged.push(n)
          continue
        }
      }
      json.mcpServers[n] = next
      out.added.push(n)
    }
    if (out.added.length || out.unchanged.length) {
      await fs.mkdir(dirname(target.absFile), { recursive: true })
      await fs.writeFile(target.absFile, `${JSON.stringify(json, null, 2)}\n`, 'utf8')
    }
    return out
  }

  if (target.format === 'json-mcp') {
    const json = fileExists
      ? (JSON.parse(await fs.readFile(target.absFile, 'utf8')) as { mcp?: McpServers })
      : { mcp: {} }
    if (!json.mcp || typeof json.mcp !== 'object') json.mcp = {}
    for (const n of names) {
      const next = withActive(servers[n])
      const prev = json.mcp[n]
      if (prev) {
        if (mcpEntryEqual(prev, next)) {
          out.unchanged.push(n)
          continue
        }
        const action = handleMcpConflict(n)
        if (action === 'skip') {
          out.unchanged.push(n)
          continue
        }
      }
      json.mcp[n] = next
      out.added.push(n)
    }
    if (out.added.length || out.unchanged.length) {
      await fs.mkdir(dirname(target.absFile), { recursive: true })
      await fs.writeFile(target.absFile, `${JSON.stringify(json, null, 2)}\n`, 'utf8')
    }
    return out
  }

  if (target.format === 'json5-openclaw') {
    const raw = await fs.readFile(target.absFile, 'utf8')
    const json = (JSON5.parse(raw) || {}) as { mcp?: { servers?: McpServers } }
    if (!json.mcp || typeof json.mcp !== 'object') json.mcp = {}
    if (!json.mcp.servers || typeof json.mcp.servers !== 'object') json.mcp.servers = {}
    for (const n of names) {
      const cfg = { ...servers[n] }
      if (!cfg.url && !cfg.transport) cfg.transport = 'stdio'
      const prev = json.mcp.servers[n]
      if (prev) {
        if (mcpEntryEqual(prev as Record<string, unknown>, cfg as Record<string, unknown>)) {
          out.unchanged.push(n)
          continue
        }
        const action = handleMcpConflict(n)
        if (action === 'skip') {
          out.unchanged.push(n)
          continue
        }
      }
      json.mcp.servers[n] = cfg
      out.added.push(n)
    }
    if (out.added.length) await fs.writeFile(target.absFile, `${JSON.stringify(json, null, 2)}\n`, 'utf8')
    return out
  }

  if (target.format === 'yaml-mcp_servers') {
    const raw = await fs.readFile(target.absFile, 'utf8')
    const doc = (yamlParse(raw) || {}) as { mcp_servers?: McpServers }
    if (!doc.mcp_servers || typeof doc.mcp_servers !== 'object') doc.mcp_servers = {}
    for (const n of names) {
      const src = servers[n]
      const cfg: Record<string, unknown> = {}
      if (src.url) cfg.url = src.url
      else {
        if (src.command) cfg.command = src.command
        if (src.args) cfg.args = src.args
        if (src.env) cfg.env = src.env
      }
      cfg.enabled = true
      const prev = doc.mcp_servers[n]
      if (prev) {
        if (mcpEntryEqual(prev as Record<string, unknown>, cfg)) {
          out.unchanged.push(n)
          continue
        }
        const action = handleMcpConflict(n)
        if (action === 'skip') {
          out.unchanged.push(n)
          continue
        }
      }
      doc.mcp_servers[n] = cfg
      out.added.push(n)
    }
    if (out.added.length) await fs.writeFile(target.absFile, yamlStringify(doc), 'utf8')
    return out
  }

  return out
}

export async function unmergeMcp(absFile: string, format: McpFormat, names: string[]): Promise<void> {
  if (!names.length || !(await exists(absFile))) return
  if (format === 'toml-mcp_servers') {
    const text = await fs.readFile(absFile, 'utf8')
    await fs.writeFile(absFile, stripTomlMarkerBlocks(text, names), 'utf8')
    return
  }
  if (format === 'json-mcpServers') {
    const json = JSON.parse(await fs.readFile(absFile, 'utf8')) as { mcpServers?: McpServers }
    if (json.mcpServers) for (const n of names) delete json.mcpServers[n]
    await fs.writeFile(absFile, `${JSON.stringify(json, null, 2)}\n`, 'utf8')
  } else if (format === 'json-mcp') {
    const json = JSON.parse(await fs.readFile(absFile, 'utf8')) as { mcp?: McpServers }
    if (json.mcp) for (const n of names) delete json.mcp[n]
    await fs.writeFile(absFile, `${JSON.stringify(json, null, 2)}\n`, 'utf8')
  } else if (format === 'json5-openclaw') {
    const json = JSON5.parse(await fs.readFile(absFile, 'utf8')) as { mcp?: { servers?: McpServers } }
    if (json.mcp?.servers) for (const n of names) delete json.mcp.servers[n]
    await fs.writeFile(absFile, `${JSON.stringify(json, null, 2)}\n`, 'utf8')
  } else if (format === 'yaml-mcp_servers') {
    const doc = (yamlParse(await fs.readFile(absFile, 'utf8')) || {}) as { mcp_servers?: McpServers }
    if (doc.mcp_servers) for (const n of names) delete doc.mcp_servers[n]
    await fs.writeFile(absFile, yamlStringify(doc), 'utf8')
  }
}

export async function addHermesExternalDir(configAbs: string, skillsAbs: string): Promise<boolean> {
  if (!(await exists(configAbs))) return false
  const doc = (yamlParse(await fs.readFile(configAbs, 'utf8')) || {}) as {
    skills?: { external_dirs?: string[] }
  }
  if (!doc.skills || typeof doc.skills !== 'object') doc.skills = {}
  const list = Array.isArray(doc.skills.external_dirs) ? doc.skills.external_dirs : []
  if (!list.includes(skillsAbs)) list.push(skillsAbs)
  doc.skills.external_dirs = list
  await fs.writeFile(configAbs, yamlStringify(doc), 'utf8')
  return true
}

export async function removeHermesExternalDir(configAbs: string, skillsAbs: string): Promise<void> {
  if (!(await exists(configAbs))) return
  const doc = (yamlParse(await fs.readFile(configAbs, 'utf8')) || {}) as {
    skills?: { external_dirs?: string[] }
  }
  if (doc.skills?.external_dirs) {
    doc.skills.external_dirs = doc.skills.external_dirs.filter(d => d !== skillsAbs)
    await fs.writeFile(configAbs, yamlStringify(doc), 'utf8')
  }
}
