import { resolve } from 'node:path'
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js'
import type { CaptureDeliver, ConflictPolicy, InstallOpts } from '../src/types.js'
import { parseModulesList, type PackModules } from '../src/modules.js'
import type { ExportOpts } from '../src/export.js'
import type { SyncOpts } from '../src/sync.js'
import { conflictPayload, PackConflictError } from '../src/errors.js'

/**
 * 每个 MCP 工具的统一错误边界——不靠每个 handler 自己记得 try/catch（历史上就漏了 10/12 个），
 * 而是在注册层包一次，结构上不可能漏。已知的 PackConflictError → 结构化 conflict 载荷；
 * 任何其它异常（ENOENT、SyntaxError、权限错误…）→ toolError，附带错误类型名，
 * 而不是让原始异常穿透 MCP SDK 变成不透明的协议层错误。
 */
export function withToolErrorBoundary<A, R>(
  handler: (args: A) => Promise<R>,
): (args: A) => Promise<R | CallToolResult> {
  return async (args: A) => {
    try {
      return await handler(args)
    } catch (e) {
      if (e instanceof PackConflictError) {
        return toolConflictResult(e)
      }
      const err = e as Error & { code?: string }
      return toolError(err.message || String(e), {
        errorType: err.name || err.constructor?.name || 'Error',
        code: err.code,
      })
    }
  }
}

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
  /** 是否允许写用户全局 harness 配置（经验 hook / hermes external_dirs / 全局 MCP，默认 false，只影响当前项目） */
  allow_global_config?: boolean
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
    allowGlobalConfig: p.allow_global_config,
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
    allowGlobalConfig: p.allow_global_config,
  }
}
