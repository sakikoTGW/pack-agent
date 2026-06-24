/** ccui-pack / agent-pack manifest (L1 + optional L2 + 经验罐头 + 扩展沉积) */

import type { PackModuleId, PackModules } from './modules.js'

/** 抓包/蒸馏内容的交付方式 */
export type CaptureDeliver = 'skill' | 'experience'

/** 扩展模块交付策略（安装时可覆盖） */
export type ModuleDeliver = 'install' | 'experience' | 'skip'

export type PackPolicy = {
  captureAs?: CaptureDeliver
  knowledgeAs?: 'skill'
}

export type PackSkillEntry = {
  name?: string
  version?: string
  ref?: string
  source?: string
  scope?: string
  contentHash?: string
  fileCount?: number
  license?: string
  description?: string
  requires?: string[]
  deliverAs?: CaptureDeliver
}

export type PackRuleEntry = {
  name?: string
  version?: string
  ref?: string
  format?: string
  scope?: string
  contentHash?: string
}

export type PackMcpEntry = {
  name?: string
  version?: string
  type?: string
  command?: string
  args?: string[]
  url?: string
  env?: Record<string, string>
  package?: string
  packageVersion?: string
  configHash?: string
}

export type PackExperience = {
  id: string
  name?: string
  version?: string
  kind?: 'distill' | 'capture' | 'manual'
  scope?: 'session' | 'turn' | 'until-eject'
  ttl?: string
  source?: string
  contentHash?: string
  harness?: { base_system_prompt?: string; tool_schemas?: unknown[]; system_reminders?: string[] }
  assembly?: Record<string, unknown>
  model?: Record<string, unknown>
  offset?: { weight?: number; promptDelta?: string; reminders?: string[]; updatedAt?: string }
  meta?: Record<string, unknown>
}

export type PackHookEntry = {
  name?: string
  ref?: string
  format?: string
  scope?: string
  contentHash?: string
  hookEvents?: string[]
}

export type PackSubagentEntry = {
  name?: string
  ref?: string
  scope?: string
  contentHash?: string
  description?: string
}

export type PackMemoryEntry = {
  name?: string
  ref?: string
  kind?: 'project-memory' | 'user-profile' | 'local-notes' | 'pack-memory'
  scope?: string
  contentHash?: string
}

export type PackSettingsEntry = {
  key?: string
  ref?: string
  format?: string
  contentHash?: string
}

export type PackResolution = {
  lockedAt?: string
  packContentHash?: string
  agentPackCli?: string
  /** 安装此 pack 所需的最低 agent-pack CLI 版本 */
  minPackCli?: string
  skillCount?: number
  ruleCount?: number
  mcpCount?: number
  experienceCount?: number
  hookCount?: number
  subagentCount?: number
  memoryCount?: number
  captureDeliver?: CaptureDeliver
  modules?: Partial<Record<PackModuleId, boolean>>
}

export type PackAgentRef = {
  id: string
  harness?: string
}

export type PackDoc = {
  schema?: string
  name?: string
  version?: string
  /** pack / agent 作者 */
  author?: string
  /** pack / agent 介绍（展示用） */
  description?: string
  /** 本 pack 对应哪一个 agent 定义 */
  agent?: PackAgentRef
  channel?: 'dev' | 'stable' | 'snapshot'
  policy?: PackPolicy
  runtime?: { id?: string; label?: string; verified?: boolean; minVersion?: string }
  knowledge?: { skills?: PackSkillEntry[]; rules?: PackRuleEntry[] }
  experiences?: PackExperience[]
  /** hooks / automation（可选模块） */
  automation?: { hooks?: PackHookEntry[] }
  /** subagents（可选模块） */
  agents?: { subagents?: PackSubagentEntry[] }
  /** MEMORY / USER 等（可选模块） */
  memory?: { files?: PackMemoryEntry[] }
  /** settings 片段（permissions/env，默认关） */
  settings?: { fragments?: PackSettingsEntry[] }
  /** 本包实际纳入的模块开关（快照） */
  modules?: PackModules
  tools?: { mcp?: PackMcpEntry[]; builtin_map?: Array<{ name?: string; mapTo?: string }> }
  harness?: { base_system_prompt?: string; tool_schemas?: unknown[]; system_reminders?: string[] }
  assembly?: Record<string, unknown>
  model?: Record<string, unknown>
  resolution?: PackResolution
  meta?: Record<string, unknown>
  bundle?: { portable?: boolean; files?: Array<{ path: string; content: string }> }
}

/** 冲突时：stop=停下并报错；skip=跳过该项；replace=覆盖 */
export type ConflictPolicy = 'stop' | 'skip' | 'replace'

export type InstallOpts = {
  runtime?: string
  runtimes?: string[]
  stateDir?: string
  noBootstrap?: boolean
  captureAs?: CaptureDeliver
  /** 安装时模块开关（覆盖 pack.modules） */
  modules?: PackModules
  /** 冲突策略（默认 stop） */
  onConflict?: ConflictPolicy
  /** @deprecated requires 不满足时仅 stop；请修 bundle */
  forceRequires?: boolean
  /** 装完后写入 agent-pack MCP 到 .mcp.json / .cursor/mcp.json */
  bootstrapMcp?: boolean
}

export type RuntimeInstallReport = {
  runtime: string
  label: string
  skills: string[]
  rules: string[]
  mcp: string[]
  skipped: string[]
  harnessL2?: { path?: string; skipped?: string }
}

export type InstallReport = {
  ok: boolean
  name: string
  detected: string[]
  projected: string[]
  runtimes: RuntimeInstallReport[]
  skills: string[]
  rules: string[]
  mcp: string[]
  skipped: string[]
  harnessPresetHint?: string
  lockPath?: string
  experiences?: Array<{ id: string; path: string }>
  /** 经验罐头接上的 harness 注入点（SessionStart / pre_llm 等） */
  experienceHooks?: string[]
  captureDeliver?: CaptureDeliver
  hooks?: string[]
  subagents?: string[]
  memory?: string[]
  settings?: string[]
  requiresCheck?: { satisfied: unknown[]; missing: unknown[] }
  ledgerPath?: string
  mcpBootstrap?: string[]
  ejectHint?: string
  /** on_conflict=skip|replace 时记录的处理项 */
  conflictsResolved?: Array<{ action: 'skip' | 'replace'; detail: import('./errors.js').PackConflictDetail }>
}
