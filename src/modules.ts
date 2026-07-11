/**
 * 可选模块 — 打包者/安装者各自决定纳入哪些「沉积层」。
 */
import type { PackDoc } from './types.js'

export type PackModuleId =
  | 'skills'
  | 'rules'
  | 'mcp'
  | 'experiences'
  | 'hooks'
  | 'subagents'
  | 'memory'
  | 'settings'
  | 'transcripts'

export type PackModules = Partial<Record<PackModuleId, boolean>>

/** 默认 L1 + 经验罐头；hooks/subagents/memory 等需显式开启 */
export const DEFAULT_PACK_MODULES: Required<PackModules> = {
  skills: true,
  rules: true,
  mcp: true,
  experiences: true,
  hooks: false,
  subagents: false,
  memory: false,
  settings: false,
  transcripts: false,
}

export type PackModulesInput = PackModules | string[] | undefined

export function parseModulesList(raw: string[] | undefined): PackModules | undefined {
  if (!raw?.length) return undefined
  const out: PackModules = { ...DEFAULT_PACK_MODULES }
  for (const k of Object.keys(out) as PackModuleId[]) out[k] = false
  for (const token of raw) {
    const t = token.trim().toLowerCase()
    if (t.startsWith('no-') || t.startsWith('-')) {
      const id = t.replace(/^no-|^-/, '') as PackModuleId
      if (id in DEFAULT_PACK_MODULES) out[id] = false
      continue
    }
    if (t === 'all') {
      for (const k of Object.keys(DEFAULT_PACK_MODULES) as PackModuleId[]) out[k] = true
      continue
    }
    if (t in DEFAULT_PACK_MODULES) out[t as PackModuleId] = true
  }
  return out
}

export function resolvePackModules(
  project?: PackModules,
  manifest?: PackModules,
  cli?: PackModules,
): Required<PackModules> {
  return {
    ...DEFAULT_PACK_MODULES,
    ...project,
    ...manifest,
    ...cli,
  }
}

export function modulesEnabled(m: Required<PackModules>, id: PackModuleId): boolean {
  return Boolean(m[id])
}

/** 按模块开关裁剪 pack（安装前也可调用） */
export function filterPackByModules(pack: PackDoc, modules: Required<PackModules>): PackDoc {
  const out: PackDoc = { ...pack, modules: { ...modules } }

  if (!modules.skills) {
    out.knowledge = { ...out.knowledge, skills: [] }
  }
  if (!modules.rules) {
    out.knowledge = { ...out.knowledge, rules: [] }
  }
  if (!modules.mcp) {
    out.tools = { ...out.tools, mcp: [] }
  }
  if (!modules.experiences) {
    out.experiences = []
    if (out.policy?.captureAs === 'experience') {
      out.harness = { base_system_prompt: '', tool_schemas: [], system_reminders: [] }
    }
  }
  if (!modules.hooks) {
    out.automation = { ...out.automation, hooks: [] }
  }
  if (!modules.subagents) {
    out.agents = { ...out.agents, subagents: [] }
  }
  if (!modules.memory) {
    out.memory = { files: [] }
  }
  if (!modules.settings) {
    out.settings = { fragments: [] }
  }

  if (out.bundle?.files?.length) {
    const files = out.bundle.files.filter(f => {
      if (f.path.startsWith('skills/') && !modules.skills) return false
      if (f.path.startsWith('rules/') && !modules.rules) return false
      if (f.path.startsWith('automation/') && !modules.hooks) return false
      if (f.path.startsWith('agents/') && !modules.subagents) return false
      if (f.path.startsWith('memory/') && !modules.memory) return false
      if (f.path.startsWith('settings/') && !modules.settings) return false
      return true
    })
    out.bundle = { ...out.bundle, files }
  }

  return out
}
