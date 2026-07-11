import { resolve } from 'node:path'
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js'
import type { CaptureDeliver, ConflictPolicy, InstallOpts } from '../src/types.js'
import { parseModulesList, type PackModules } from '../src/modules.js'
import type { ExportOpts } from '../src/export.js'
import { conflictPayload, PackConflictError } from '../src/errors.js'

export function resolveProjectCwd(cwd?: string): string {
  const raw = cwd?.trim() || process.env.AGENT_PACK_CWD?.trim() || process.cwd()
  return resolve(raw)
}

export function jsonToolResult(data: unknown): CallToolResult {
  const text = JSON.stringify(data, null, 2)
  return {
    content: [{ type: 'text', text }],
    structuredContent: data as Record<string, unknown>,
  }
}

export function toolError(message: string, detail?: unknown): CallToolResult {
  const payload = { ok: false, error: message, detail }
  return {
    content: [{ type: 'text', text: JSON.stringify(payload, null, 2) }],
    isError: true,
  }
}

export function toolConflictResult(error: PackConflictError): CallToolResult {
  const payload = conflictPayload(error.detail)
  return {
    content: [{ type: 'text', text: error.message }],
    structuredContent: payload as Record<string, unknown>,
    isError: true,
  }
}

export type McpPackOpts = {
  cwd?: string
  runtime?: string
  agent?: string
  all?: boolean
  capture_as?: CaptureDeliver
  no_bootstrap?: boolean
  modules?: string[]
  state_dir?: string
  on_conflict?: ConflictPolicy
}

function parseModules(modules?: string[]): PackModules | undefined {
  if (!modules?.length) return undefined
  return parseModulesList(modules)
}

export function toInstallOpts(p: McpPackOpts & { force_requires?: boolean; bootstrap_mcp?: boolean }): InstallOpts {
  return {
    runtime: p.runtime,
    stateDir: p.state_dir,
    noBootstrap: p.no_bootstrap,
    captureAs: p.capture_as,
    modules: parseModules(p.modules),
    onConflict: p.on_conflict,
    bootstrapMcp: p.bootstrap_mcp,
  }
}

export function toExportOpts(p: McpPackOpts & { name?: string; select?: ExportOpts['select'] }): ExportOpts {
  return {
    runtime: p.runtime,
    name: p.name,
    agent: p.agent,
    allowFullScan: p.all,
    stateDir: p.state_dir,
    noBootstrap: p.no_bootstrap,
    captureAs: p.capture_as,
    modules: parseModules(p.modules),
    select: p.select,
  }
}

export function toSyncOpts(
  p: McpPackOpts & { from?: string; name?: string; select?: SyncOpts['select'] },
): SyncOpts {
  return {
    ...toExportOpts(p),
    from: p.from,
    onConflict: p.on_conflict,
  }
}
