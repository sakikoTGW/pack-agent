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
  /** 识别码；缺省用 name。预留，给发布/检索 */
  id?: string
  /** 发布者。预留 */
  publisher?: string
  /** 规范性标注。预留 */
  spec?: string
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
  id?: string
  publisher?: string
  spec?: string
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

/** 斜杠指令 / slash command（Cursor `.cursor/commands`、Claude `.claude/commands`） */
export type PackCommandEntry = {
  name?: string
  ref?: string
  /** 来源 harness 目录提示 */
  scope?: string
  contentHash?: string
}

/** @deprecated Legacy pre-Kind `.collab/` payload kept for upgrade compatibility only. */
export type PackCollabEntry = {
  name?: string
  ref?: string
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
  /** slash commands（默认开，跨 harness 迁移关键） */
  commands?: { files?: PackCommandEntry[] }
  /** @deprecated Legacy pre-Kind `.collab/` payload; new packs should use `cos.collab` Kind units. */
  collab?: { files?: PackCollabEntry[] }
  /** 本包实际纳入的模块开关（快照） */
  modules?: PackModules
  tools?: { mcp?: PackMcpEntry[]; builtin_map?: Array<{ name?: string; mapTo?: string }> }
  harness?: { base_system_prompt?: string; tool_schemas?: unknown[]; system_reminders?: string[] }
  assembly?: Record<string, unknown>
  model?: Record<string, unknown>
  resolution?: PackResolution
  meta?: Record<string, unknown>
  bundle?: { portable?: boolean; files?: Array<{ path: string; content: string }> }
  /** DeepSeek Harness：任意 Cordis 插件都可进整合包 */
  dsh?: PackDshLayer
}

export const PACK_MOD_SCHEMA = 'pack-agent.mod/v0'

export type PackModKind = 'skill' | 'mcp' | 'plugin' | 'rule' | 'command' | 'hook'

/** 整合包里的一份 mod。publisher / spec 现可空，给以后的发布与规范性标注。 */
export type PackModManifest = {
  schema: typeof PACK_MOD_SCHEMA
  id: string
  kind: PackModKind
  name: string
  publisher: string
  version: string
  spec: string
  path: string
}

export type PackDshPlugin = {
  /** 交给 `dsh plugin add`。缺省用 `name` */
  spec?: string
  required?: boolean
  id?: string
  name?: string
  config?: Record<string, unknown>
  disabled?: boolean
  inject?: string[]
}

export type PackDshOverride = {
  from: string
  to: string
}

export type PackDshLayer = {
  /** import 必填。精确号或 npm range */
  version?: string
  /** `--profile` 名，默认 web */
  profile?: string
  persona?: string
  preset?: { id?: string; name?: string; description?: string }
  plugins?: PackDshPlugin[]
  /** 拷进实例工作区，不进 DSH_HOME，不进投影 mods/ */
  overrides?: PackDshOverride[]
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
  /**
   * 是否允许写用户全局配置：经验罐头 SessionStart hook（~/.claude/settings.json 等）、
   * Hermes skills.external_dirs、OpenClaw/Hermes 全局 MCP servers 合并。
   * 默认 false：install 只影响当前项目，不会悄悄改用户在所有项目里的 harness 行为
   * （历史上这是真实发生过的 bug：临时测试目录被写进 ~/.hermes/config.yaml 且从不清理）。
   */
  allowGlobalConfig?: boolean
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
  /** 配置写对了，但还需要用户手动做一步才会真的生效（例如 Hermes shell-hook 的一次性 consent） */
  notes?: string[]
  captureDeliver?: CaptureDeliver
  hooks?: string[]
  subagents?: string[]
  memory?: string[]
  settings?: string[]
  commands?: string[]
  requiresCheck?: { satisfied: unknown[]; missing: unknown[] }
  ledgerPath?: string
  mcpBootstrap?: string[]
  ejectHint?: string
  /** on_conflict=skip|replace 时记录的处理项 */
  conflictsResolved?: Array<{ action: 'skip' | 'replace'; detail: import('./errors.js').PackConflictDetail }>
}
