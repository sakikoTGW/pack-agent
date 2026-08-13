/**
 * pack-agent 的 DeepSeek Harness 插件。
 * 宿主只装这一份管理器。整合包走投影 + 允许集，不进 plugin 栈。
 */
import { resolve } from 'node:path'
import {
  actionAllow,
  actionCompile,
  actionDeny,
  actionDetect,
  actionList,
  actionMap,
  actionProject,
  actionSearch,
  defaultCompileOut,
} from './actions.js'
import { loadPackDoc } from '../../dsh-modpack/compile.js'
import { createCatalogSkillProvider } from './provider.js'

export const name = 'pack-agent'
export const inject = ['tools', 'skills']

export type PackAgentDshConfig = {
  cwd?: string
}

type ToolRegister = {
  register: (def: {
    name: string
    description: string
    parameters: Record<string, unknown>
    execute: (args: Record<string, unknown>) => Promise<unknown>
    output: {
      schema: Record<string, unknown>
      render: (args: unknown, value: unknown) => Array<{ type: 'text'; text: string }>
    }
  }) => unknown
}

type CommandRegister = {
  register: (def: {
    name: string
    description: string
    input?: { hint: string }
    handler: (invocation: { rawInput: string }) => Promise<{ kind: 'success' | 'error'; text: string }>
  }) => unknown
}

type SkillProviderControl = {
  signal: AbortSignal
  invalidate: () => void
}

type SkillsHost = {
  registerProvider: (create: (control: SkillProviderControl) => unknown) => () => void
}

type PluginContext = {
  tools: ToolRegister
  commands?: CommandRegister
  skills?: SkillsHost
}

function cwdOf(config: PackAgentDshConfig | undefined, argsCwd?: unknown): string {
  if (typeof argsCwd === 'string' && argsCwd.trim()) return resolve(argsCwd)
  if (config?.cwd?.trim()) return resolve(config.cwd)
  return process.cwd()
}

function textOf(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}`
}

/** DSH ToolRuntime.register 要求 output.render；对照 @deepseek-ai/dsh-tools 0.1.0-rc.6。 */
function withDshOutput<T extends {
  name: string
  description: string
  parameters: Record<string, unknown>
  execute: (args: Record<string, unknown>) => Promise<unknown>
}>(def: T) {
  return {
    ...def,
    output: {
      schema: { type: 'object', additionalProperties: true },
      render(_args: unknown, value: unknown) {
        const text = typeof value === 'string' ? value : JSON.stringify(value ?? null, null, 2)
        return [{ type: 'text' as const, text }]
      },
    },
  }
}

export function apply(ctx: PluginContext, config: PackAgentDshConfig = {}): void {
  ctx.skills?.registerProvider(() => createCatalogSkillProvider(cwdOf(config)))

  ctx.tools.register(withDshOutput({
    name: 'packagent_detect',
    description: 'Detect which agent harnesses are present in a project directory.',
    parameters: {
      type: 'object',
      properties: { cwd: { type: 'string', description: 'Project root' } },
      additionalProperties: false,
    },
    execute: args => actionDetect(cwdOf(config, args.cwd)),
  }))

  ctx.tools.register(withDshOutput({
    name: 'packagent_compile',
    description: 'Compile a pack-agent pack into a projected dsh.bundle directory (does not index).',
    parameters: {
      type: 'object',
      properties: {
        pack: { type: 'string', description: 'Path to .pack.json or .pack.zip' },
        out: { type: 'string', description: 'Output directory' },
        cwd: { type: 'string' },
      },
      required: ['pack'],
      additionalProperties: false,
    },
    async execute(args) {
      const pack = String(args.pack || '')
      if (!pack) throw new Error('pack path required')
      const cwd = cwdOf(config, args.cwd)
      const doc = await loadPackDoc(resolve(cwd, pack))
      const out = typeof args.out === 'string' && args.out.trim()
        ? resolve(cwd, args.out)
        : defaultCompileOut(cwd, doc.name || 'pack')
      return actionCompile(resolve(cwd, pack), out)
    },
  }))

  ctx.tools.register(withDshOutput({
    name: 'packagent_project',
    description: 'Project a pack into .agent-pack/modpacks and index the SQLite catalog. Does not dsh plugin add.',
    parameters: {
      type: 'object',
      properties: {
        pack: { type: 'string' },
        cwd: { type: 'string' },
        allow: { type: 'boolean', description: 'Enable this pack in the allow-list' },
      },
      required: ['pack'],
      additionalProperties: false,
    },
    async execute(args) {
      const pack = String(args.pack || '')
      if (!pack) throw new Error('pack path required')
      const cwd = cwdOf(config, args.cwd)
      return actionProject(resolve(cwd, pack), cwd, Boolean(args.allow))
    },
  }))

  ctx.tools.register(withDshOutput({
    name: 'packagent_map',
    description: 'Map a Cursor/ccui-pack export into a DeepSeek Harness modpack (mods/ + catalog). Use --from for ref-only packs.',
    parameters: {
      type: 'object',
      properties: {
        pack: { type: 'string' },
        cwd: { type: 'string' },
        from: { type: 'string', description: 'Cursor project root to hydrate skill refs' },
        allow: { type: 'boolean' },
      },
      required: ['pack'],
      additionalProperties: false,
    },
    async execute(args) {
      const pack = String(args.pack || '')
      if (!pack) throw new Error('pack path required')
      const cwd = cwdOf(config, args.cwd)
      const from = typeof args.from === 'string' && args.from.trim() ? resolve(cwd, args.from) : undefined
      return actionMap(resolve(cwd, pack), cwd, { allow: Boolean(args.allow), from })
    },
  }))

  ctx.tools.register(withDshOutput({
    name: 'packagent_search',
    description: 'Full-text search projected packs via the Rust SQLite catalog.',
    parameters: {
      type: 'object',
      properties: {
        query: { type: 'string' },
        cwd: { type: 'string' },
      },
      required: ['query'],
      additionalProperties: false,
    },
    async execute(args) {
      const query = String(args.query || '').trim()
      if (!query) throw new Error('query required')
      return actionSearch(cwdOf(config, args.cwd), query)
    },
  }))

  ctx.tools.register(withDshOutput({
    name: 'packagent_allow',
    description: 'Allow a projected pack so the current session can see its skills.',
    parameters: {
      type: 'object',
      properties: {
        id: { type: 'string' },
        cwd: { type: 'string' },
      },
      required: ['id'],
      additionalProperties: false,
    },
    async execute(args) {
      const id = String(args.id || '').trim()
      if (!id) throw new Error('id required')
      return actionAllow(cwdOf(config, args.cwd), id)
    },
  }))

  ctx.tools.register(withDshOutput({
    name: 'packagent_deny',
    description: 'Deny a projected pack. Files stay on disk; the session can no longer see it.',
    parameters: {
      type: 'object',
      properties: {
        id: { type: 'string' },
        cwd: { type: 'string' },
      },
      required: ['id'],
      additionalProperties: false,
    },
    async execute(args) {
      const id = String(args.id || '').trim()
      if (!id) throw new Error('id required')
      return actionDeny(cwdOf(config, args.cwd), id)
    },
  }))

  ctx.tools.register(withDshOutput({
    name: 'packagent_list',
    description: 'List projected packs in the SQLite catalog.',
    parameters: {
      type: 'object',
      properties: {
        cwd: { type: 'string' },
        enabled: { type: 'boolean' },
      },
      additionalProperties: false,
    },
    execute: args => actionList(cwdOf(config, args.cwd), Boolean(args.enabled)),
  }))

  ctx.commands?.register({
    name: 'packagent-detect',
    description: 'list detected agent harnesses in the project',
    handler: async () => {
      const r = await actionDetect(cwdOf(config))
      return { kind: 'success' as const, text: r.detected.length ? r.detected.join(', ') : '(none)' }
    },
  })

  ctx.commands?.register({
    name: 'packagent-compile',
    description: 'compile a pack-agent pack into a projected dsh.bundle',
    input: { hint: '<pack.json|pack.zip>' },
    handler: async invocation => {
      const pack = invocation.rawInput.trim()
      if (!pack) return { kind: 'error' as const, text: 'Usage: /packagent-compile <pack.json|pack.zip>' }
      const cwd = cwdOf(config)
      const doc = await loadPackDoc(resolve(cwd, pack))
      const r = await actionCompile(resolve(cwd, pack), defaultCompileOut(cwd, doc.name || 'pack'))
      return { kind: 'success' as const, text: `${r.dir}\n${r.installCommand}` }
    },
  })

  ctx.commands?.register({
    name: 'packagent-project',
    description: 'project a pack into .agent-pack/modpacks and index it',
    input: { hint: '<pack.json|pack.zip>' },
    handler: async invocation => {
      const pack = invocation.rawInput.trim()
      if (!pack) return { kind: 'error' as const, text: 'Usage: /packagent-project <pack.json|pack.zip>' }
      const r = await actionProject(resolve(cwdOf(config), pack), cwdOf(config))
      return { kind: 'success' as const, text: `${r.dir}\n${r.id}\n${r.installCommand}` }
    },
  })

  ctx.commands?.register({
    name: 'packagent-map',
    description: 'map a Cursor/ccui-pack export into a DSH modpack',
    input: { hint: '<pack.json|pack.zip>' },
    handler: async invocation => {
      const pack = invocation.rawInput.trim()
      if (!pack) return { kind: 'error' as const, text: 'Usage: /packagent-map <pack.json|pack.zip>' }
      const r = await actionMap(resolve(cwdOf(config), pack), cwdOf(config))
      return { kind: 'success' as const, text: `${r.dir}\n${r.id}\n${r.installCommand}` }
    },
  })

  ctx.commands?.register({
    name: 'packagent-search',
    description: 'search projected packs',
    input: { hint: '<query>' },
    handler: async invocation => {
      const query = invocation.rawInput.trim()
      if (!query) return { kind: 'error' as const, text: 'Usage: /packagent-search <query>' }
      const r = await actionSearch(cwdOf(config), query)
      return { kind: 'success' as const, text: textOf(r.hits) }
    },
  })

  ctx.commands?.register({
    name: 'packagent-allow',
    description: 'allow a projected pack for this session',
    input: { hint: '<pack-id>' },
    handler: async invocation => {
      const id = invocation.rawInput.trim()
      if (!id) return { kind: 'error' as const, text: 'Usage: /packagent-allow <pack-id>' }
      const r = await actionAllow(cwdOf(config), id)
      return { kind: 'success' as const, text: textOf(r) }
    },
  })
}
