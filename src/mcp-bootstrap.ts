/**
 * 安装后可选：把 agent-pack MCP server 写入项目 MCP 配置。
 */
import { promises as fs } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

export type McpBootstrapTarget = {
  absFile: string
  format: 'json-mcpServers'
}

import { PackConflictError } from './errors.js'

const SERVER_NAME = 'agent-pack'

function resolveMcpServerEntry(cwd: string): { command: string; args: string[]; env: Record<string, string> } {
  const packCliRoot = join(fileURLToPath(new URL('.', import.meta.url)), '..')
  const serverTs = join(packCliRoot, 'mcp', 'server.ts').replace(/\\/g, '/')
  return {
    command: 'bun',
    args: [serverTs],
    env: { AGENT_PACK_CWD: cwd.replace(/\\/g, '/') },
  }
}

export function mcpBootstrapTargets(cwd: string): McpBootstrapTarget[] {
  return [
    { absFile: join(cwd, '.mcp.json'), format: 'json-mcpServers' },
    { absFile: join(cwd, '.cursor', 'mcp.json'), format: 'json-mcpServers' },
  ]
}

export async function bootstrapAgentPackMcp(
  cwd: string,
  opts: { skipIfPresent?: boolean } = {},
): Promise<{ wired: string[]; skipped: string[] }> {
  const wired: string[] = []
  const skipped: string[] = []
  const entry = resolveMcpServerEntry(cwd)

  for (const t of mcpBootstrapTargets(cwd)) {
    let doc: Record<string, unknown> = {}
    if (await exists(t.absFile)) {
      try {
        doc = JSON.parse(await fs.readFile(t.absFile, 'utf8')) as Record<string, unknown>
      } catch {
        doc = {}
      }
      const servers = (doc.mcpServers ?? {}) as Record<string, Record<string, unknown>>
      const prev = servers[SERVER_NAME]
      if (prev) {
        if (JSON.stringify(prev) === JSON.stringify(entry)) {
          skipped.push(`${t.absFile} (unchanged ${SERVER_NAME})`)
          continue
        }
        throw new PackConflictError({
          kind: 'mcp-server',
          summary: `MCP server \`${SERVER_NAME}\` already configured differently`,
          path: t.absFile,
          context: [
            `existing ${SERVER_NAME} entry in ${t.absFile} does not match agent-pack bootstrap`,
          ],
          help: [
            `edit ${t.absFile} manually, or remove the ${SERVER_NAME} entry and retry install`,
          ],
        })
      }
      servers[SERVER_NAME] = entry
      doc.mcpServers = servers
    } else {
      doc = { mcpServers: { [SERVER_NAME]: entry } }
    }
    await fs.mkdir(dirname(t.absFile), { recursive: true })
    await fs.writeFile(t.absFile, JSON.stringify(doc, null, 2), 'utf8')
    wired.push(t.absFile)
  }

  return { wired, skipped }
}

export async function removeAgentPackMcpBootstrap(cwd: string): Promise<Array<{ file: string; status: 'removed' | 'missing' | 'unchanged' }>> {
  const out: Array<{ file: string; status: 'removed' | 'missing' | 'unchanged' }> = []
  for (const t of mcpBootstrapTargets(cwd)) {
    let doc: Record<string, unknown>
    try {
      doc = JSON.parse(await fs.readFile(t.absFile, 'utf8')) as Record<string, unknown>
    } catch {
      out.push({ file: t.absFile, status: 'missing' })
      continue
    }
    const servers = (doc.mcpServers ?? {}) as Record<string, unknown>
    if (!(SERVER_NAME in servers)) {
      out.push({ file: t.absFile, status: 'unchanged' })
      continue
    }
    delete servers[SERVER_NAME]
    doc.mcpServers = servers
    if (Object.keys(servers).length === 0 && Object.keys(doc).length <= 1) {
      await fs.rm(t.absFile, { force: true })
    } else {
      await fs.writeFile(t.absFile, JSON.stringify(doc, null, 2), 'utf8')
    }
    out.push({ file: t.absFile, status: 'removed' })
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

export { SERVER_NAME as AGENT_PACK_MCP_SERVER_NAME }
