/**
 * DeepSeek Harness Cordis overlay（对照 deepseek-harness-master 源码 + examples/mcp-memory）。
 *
 * DSH 不读 mcp.json。MCP 是 `@deepseek-ai/dsh-mcp-client` 的 cordis 插件实例，
 * 通过 `dsh web --patch <file>` 或 `$DSH_HOME/cordis.patch.yml` 的 `insert` 补丁进入进程。
 * skills 不走这条路：skill-filesystem 直接扫 `<project>/.dsh/skills`（rank 100）。
 */
import { promises as fs } from 'node:fs'
import { dirname } from 'node:path'
import { parse as yamlParse, stringify as yamlStringify } from 'yaml'

type McpServers = Record<string, Record<string, unknown>>
type ScannedMcp = { name: string; type?: string; command?: string; args?: string[]; url?: string }

export const DSH_MCP_CLIENT = '@deepseek-ai/dsh-mcp-client'
export const DSH_HOOKS_CLAUDE_CODE = '@deepseek-ai/dsh-hooks-claude-code'
export const DSH_HOOKS_PLUGIN_ID = 'agent-pack-experience-hooks'
export const DSH_OVERLAY_REL = '.dsh/agent-pack.cordis.yml'

export const DSH_APPLY_NOTE =
  'dsh: skills 在 .dsh/skills，会被 skill-filesystem 自动发现。MCP 与经验钩子写在 .dsh/agent-pack.cordis.yml，需 `dsh web --patch .dsh/agent-pack.cordis.yml` 或并入 $DSH_HOME/cordis.patch.yml 才会进入进程。'

const OVERLAY_HEADER = `# agent-pack DeepSeek Harness overlay
# apply: dsh web --patch .dsh/agent-pack.cordis.yml
# persist: merge into $DSH_HOME/cordis.patch.yml
`

export type DshInsertItem = {
  id: string
  name: string
  config: Record<string, unknown>
}

type PatchOp = { insert?: DshInsertItem[]; [k: string]: unknown }

export function dshMcpPluginId(serverName: string): string {
  return `agent-pack-mcp-${serverName}`
}

export function mcpServersToDshInsertItems(servers: McpServers): DshInsertItem[] {
  return Object.entries(servers).map(([name, cfg]) => ({
    id: dshMcpPluginId(name),
    name: DSH_MCP_CLIENT,
    config: dshMcpClientConfig(name, cfg),
  }))
}

function dshMcpClientConfig(name: string, cfg: Record<string, unknown>): Record<string, unknown> {
  const url = typeof cfg.url === 'string' ? cfg.url : undefined
  if (url) {
    const out: Record<string, unknown> = {
      serverName: name,
      transport: 'streamable-http',
      url,
    }
    if (cfg.headers && typeof cfg.headers === 'object') out.headers = cfg.headers
    return out
  }
  const out: Record<string, unknown> = {
    serverName: name,
    transport: 'stdio',
    command: cfg.command,
  }
  if (Array.isArray(cfg.args)) out.args = cfg.args
  if (cfg.env && typeof cfg.env === 'object') out.env = cfg.env
  return out
}

export function dshExperienceHooksInsert(hooksJsonAbs: string): DshInsertItem {
  return {
    id: DSH_HOOKS_PLUGIN_ID,
    name: DSH_HOOKS_CLAUDE_CODE,
    config: {
      configPath: hooksJsonAbs.replace(/\\/g, '/'),
    },
  }
}

function asOps(raw: unknown): PatchOp[] {
  if (!Array.isArray(raw)) return []
  return raw.filter(x => x && typeof x === 'object' && !Array.isArray(x)) as PatchOp[]
}

export function parseDshCordisMcp(text: string): ScannedMcp[] {
  let raw: unknown
  try {
    raw = yamlParse(text)
  } catch {
    return []
  }
  const out: ScannedMcp[] = []
  const seen = new Set<string>()
  for (const op of asOps(raw)) {
    if (!Array.isArray(op.insert)) continue
    for (const item of op.insert) {
      if (!item || item.name !== DSH_MCP_CLIENT) continue
      const cfg = item.config && typeof item.config === 'object' ? item.config : {}
      const name = typeof cfg.serverName === 'string' ? cfg.serverName : item.id
      if (!name || seen.has(name)) continue
      seen.add(name)
      const url = typeof cfg.url === 'string' ? cfg.url : undefined
      out.push({
        name,
        type: url ? 'http' : 'stdio',
        command: typeof cfg.command === 'string' ? cfg.command : undefined,
        args: Array.isArray(cfg.args) ? cfg.args.map(String) : undefined,
        url,
      })
    }
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

export async function upsertDshCordisInsert(absFile: string, items: DshInsertItem[]): Promise<void> {
  if (!items.length) return
  let ops: PatchOp[] = []
  if (await exists(absFile)) {
    try {
      ops = asOps(yamlParse(await fs.readFile(absFile, 'utf8')))
    } catch {
      ops = []
    }
  }
  const pending = new Map(items.map(it => [it.id, it]))
  for (const op of ops) {
    if (!Array.isArray(op.insert)) continue
    op.insert = op.insert.map(it => {
      if (it?.id && pending.has(it.id)) {
        const next = pending.get(it.id)!
        pending.delete(it.id)
        return next
      }
      return it
    })
  }
  if (pending.size) {
    const rest = [...pending.values()]
    const lastInsert = [...ops].reverse().find(o => Array.isArray(o.insert))
    if (lastInsert?.insert) lastInsert.insert.push(...rest)
    else ops.push({ insert: rest })
  }
  await fs.mkdir(dirname(absFile), { recursive: true })
  await fs.writeFile(absFile, OVERLAY_HEADER + yamlStringify(ops), 'utf8')
}

export async function removeDshCordisInsertIds(absFile: string, ids: string[]): Promise<void> {
  if (!ids.length || !(await exists(absFile))) return
  const drop = new Set(ids)
  let ops: PatchOp[]
  try {
    ops = asOps(yamlParse(await fs.readFile(absFile, 'utf8')))
  } catch {
    return
  }
  const next: PatchOp[] = []
  for (const op of ops) {
    if (!Array.isArray(op.insert)) {
      next.push(op)
      continue
    }
    const kept = op.insert.filter(it => !it?.id || !drop.has(it.id))
    if (kept.length) next.push({ ...op, insert: kept })
  }
  if (!next.length) {
    await fs.rm(absFile, { force: true })
    return
  }
  await fs.writeFile(absFile, OVERLAY_HEADER + yamlStringify(next), 'utf8')
}
