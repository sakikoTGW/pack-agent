/**
 * 经验罐头 → 各 harness 会话注入口（与 L1 skills 投射并列）。
 *
 * Skill = 外挂 mod（固化）；Experience = 会话注入且随对话变（live），不写 skills 树。
 * SessionStart 灌基座罐头；UserPromptSubmit / pre_llm_call 每轮更新 live。
 * 槽位与 RUNTIME_ADAPTERS 1:1 覆盖（见 validateExperienceAdapterCoverage）。
 */
import { promises as fs } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join } from 'node:path'
import JSON5 from 'json5'
import { parse as yamlParse, stringify as yamlStringify } from 'yaml'
import { RUNTIME_ADAPTERS } from './adapters.js'
import { loadExperienceInjection } from './experience-loader.js'
import { PackConflictError, buildFileErrorDetail } from './errors.js'
import { DSH_APPLY_NOTE, dshExperienceHooksInsert, upsertDshCordisInsert } from '../agent-pack-dsh/cordis.js'

/**
 * 解析目标 harness 配置失败时**绝不能**当成空文件重写——那样会把用户文件里
 * 本来存在的 permissions/env/其它 hooks 全部静默抹掉，只剩下我们这一个 hook。
 * 统一在这里 abort，调用方（wireSlot）据此把该 slot 记进 skipped，装其它 slot 不受影响。
 */
function throwConfigParseError(absPath: string, cause: Error): never {
  throw new PackConflictError(
    buildFileErrorDetail({
      kind: 'file-invalid',
      what: 'harness config (experience hook target)',
      path: absPath,
      cause,
      help: [
        `fix the syntax at \`${absPath}\` — agent-pack refuses to overwrite a file it can't parse`,
        `or remove/back up the file and retry`,
      ],
    }),
  )
}

export const EXPERIENCE_HOOK_MARKER = 'agent-pack/experience-session-hook'

export type ExperienceInjectKind =
  | 'hook-json'
  | 'hook-json5'
  | 'hook-yaml'
  | 'cursor-hooks'
  | 'sidecar-markdown'

export type ExperienceInjectSlot = {
  runtime: string
  label: string
  resolvePath: (cwd: string) => string
  hookEvent: string
  kind: ExperienceInjectKind
  /** 配置文件不存在时是否创建（项目内优先 true；全局 ~/. 默认 false） */
  createIfMissing: boolean
  /**
   * project = 只影响当前项目（安全默认）；user = 写用户全局配置（如 ~/.claude/settings.json），
   * 会在**所有**该 harness 项目里生效。默认不写 user 槽位，除非显式 includeGlobal。
   */
  scope: 'project' | 'user'
}

/** 各 harness 经验注入槽 — 每个 RUNTIME_ADAPTERS.id 至少一条 */
export const EXPERIENCE_INJECT_SLOTS: ExperienceInjectSlot[] = [
  {
    runtime: 'cursor',
    label: 'Cursor project sessionStart',
    resolvePath: cwd => join(cwd, '.cursor', 'hooks.json'),
    hookEvent: 'sessionStart',
    kind: 'cursor-hooks',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'cursor',
    label: 'Cursor user sessionStart',
    resolvePath: () => join(homedir(), '.cursor', 'hooks.json'),
    hookEvent: 'sessionStart',
    kind: 'cursor-hooks',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'claude-code',
    label: 'Claude Code project SessionStart',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'claude-code',
    label: 'Claude Code project UserPromptSubmit (live experience)',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'UserPromptSubmit',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'claude-code',
    label: 'Claude Code user SessionStart',
    resolvePath: () => join(homedir(), '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'claude-code',
    label: 'Claude Code user UserPromptSubmit (live experience)',
    resolvePath: () => join(homedir(), '.claude', 'settings.json'),
    hookEvent: 'UserPromptSubmit',
    kind: 'hook-json',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'codex',
    label: 'Codex Claude-style project settings',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'codex',
    label: 'Codex Claude-style UserPromptSubmit (live experience)',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'UserPromptSubmit',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'codex',
    label: 'Codex project settings',
    resolvePath: cwd => join(cwd, '.codex', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'codex',
    label: 'Codex project UserPromptSubmit (live experience)',
    resolvePath: cwd => join(cwd, '.codex', 'settings.json'),
    hookEvent: 'UserPromptSubmit',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'codex',
    label: 'Codex user settings',
    resolvePath: () => join(homedir(), '.codex', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'codex',
    label: 'Codex user UserPromptSubmit (live experience)',
    resolvePath: () => join(homedir(), '.codex', 'settings.json'),
    hookEvent: 'UserPromptSubmit',
    kind: 'hook-json',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'opencode',
    label: 'OpenCode project opencode.json',
    resolvePath: cwd => join(cwd, 'opencode.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'opencode',
    label: 'OpenCode project .opencode/opencode.json',
    resolvePath: cwd => join(cwd, '.opencode', 'opencode.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'opencode',
    label: 'OpenCode user config',
    resolvePath: () => join(homedir(), '.config', 'opencode', 'opencode.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'openclaw',
    label: 'OpenClaw project gateway',
    resolvePath: cwd => join(cwd, 'openclaw.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json5',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'openclaw',
    label: 'OpenClaw user gateway',
    resolvePath: () => join(homedir(), '.openclaw', 'openclaw.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json5',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'hermes',
    label: 'Hermes project pre_llm_call',
    resolvePath: cwd => join(cwd, '.hermes', 'config.yaml'),
    hookEvent: 'pre_llm_call',
    kind: 'hook-yaml',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'hermes',
    label: 'Hermes user pre_llm_call',
    resolvePath: () => join(homedir(), '.hermes', 'config.yaml'),
    hookEvent: 'pre_llm_call',
    kind: 'hook-yaml',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'astrbot',
    label: 'AstrBot experience sidecar (L1 plugin skills only; persona DB not supported)',
    resolvePath: cwd => join(cwd, '.agent-pack', 'harness', 'astrbot', 'experience-inject.md'),
    hookEvent: 'sidecar',
    kind: 'sidecar-markdown',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'gemini-cli',
    label: 'Gemini CLI project',
    resolvePath: cwd => join(cwd, '.gemini', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'gemini-cli',
    label: 'Gemini CLI user',
    resolvePath: () => join(homedir(), '.gemini', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
    scope: 'user',
  },
  {
    runtime: 'windsurf',
    label: 'Windsurf project',
    resolvePath: cwd => join(cwd, '.windsurf', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'github-copilot',
    label: 'Copilot vscode settings hooks',
    resolvePath: cwd => join(cwd, '.vscode', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'dsh',
    label: 'DeepSeek Harness project SessionStart (Claude Code dialect)',
    resolvePath: cwd => join(cwd, '.dsh', 'hooks.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'dsh',
    label: 'DeepSeek Harness project UserPromptSubmit (live experience)',
    resolvePath: cwd => join(cwd, '.dsh', 'hooks.json'),
    hookEvent: 'UserPromptSubmit',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
  {
    runtime: 'generic-agents',
    label: 'Generic Claude-style hooks',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
    scope: 'project',
  },
]

/** 断言适配表每个 runtime 至少有一条 experience 槽（测试 / 启动自检） */
export function validateExperienceAdapterCoverage(): string[] {
  const covered = new Set(EXPERIENCE_INJECT_SLOTS.map(s => s.runtime))
  return RUNTIME_ADAPTERS.map(a => a.id).filter(id => !covered.has(id))
}

/**
 * Experience 投射目标：L1 可 skip cursor，但 experience 仍应对所有在场 harness 接线。
 * generic-agents 与具体 harness 重复，experience 阶段跳过。
 */
export function resolveExperienceRuntimes(
  detected: string[],
  projected: string[],
  explicit?: string[],
): string[] {
  const adapterIds = new Set(RUNTIME_ADAPTERS.map(a => a.id))
  const base = explicit?.length ? explicit : [...new Set([...detected, ...projected])]
  return base.filter(id => adapterIds.has(id) && id !== 'generic-agents')
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

/**
 * Hook 命令必须用绝对路径：harness 读取 **project** 配置时 cwd 通常等于项目根，
 * 相对路径凑巧能跑；但读取 **user** 全局配置（~/.claude/settings.json 等）时，
 * harness 可能在任意目录启动 —— 相对路径会指向别的项目甚至不存在的文件，
 * 导致每次 SessionStart 都报错。绝对路径两种 scope 下都正确。
 */
function hookCommand(cwd: string, stateDir: string, slot: ExperienceInjectSlot): string {
  const abs = join(cwd, stateDir, 'bin', 'experience-session-hook.cjs').replace(/\\/g, '/')
  const parts: string[] = []
  if (slot.kind === 'cursor-hooks') parts.push('AGENT_PACK_HOOK_STYLE=cursor')
  // 实测纠正（2026-07-12，对着 E:\hermes-agent-main 源码 agent/shell_hooks.py 现场核实）：
  // Hermes 的 pre_llm_call shell hook 期望 stdout 是 {"context": "..."}；我们默认输出的
  // Claude 风格 {hookSpecificOutput:{...}} 对 Hermes 来说是"不认识的 JSON"，会被当空注入
  // 静默吞掉——写对了 config.yaml 但经验罐头从来没真的进过 Hermes 的上下文。
  if (slot.kind === 'hook-yaml') parts.push('AGENT_PACK_HOOK_STYLE=hermes')
  const ev = slot.hookEvent
  if (ev !== 'SessionStart' && ev !== 'sessionStart') {
    parts.push(`AGENT_PACK_HOOK_EVENT=${ev}`)
  }
  parts.push(`node "${abs}"`)
  return parts.join(' ')
}

function hookAlreadyPresentClaude(matchers: unknown[]): boolean {
  for (const m of matchers) {
    if (!m || typeof m !== 'object') continue
    const hooks = (m as { hooks?: unknown[] }).hooks ?? []
    for (const h of hooks) {
      if (!h || typeof h !== 'object') continue
      const cmd = String((h as { command?: string }).command ?? '')
      if (cmd.includes(EXPERIENCE_HOOK_MARKER) || cmd.includes('experience-session-hook')) return true
    }
  }
  return false
}

function hookAlreadyPresentCursor(list: unknown[]): boolean {
  for (const h of list) {
    if (!h || typeof h !== 'object') continue
    const cmd = String((h as { command?: string }).command ?? '')
    if (cmd.includes('experience-session-hook')) return true
  }
  return false
}

async function mergeJsonHooks(
  absPath: string,
  hookEvent: string,
  command: string,
  createIfMissing: boolean,
): Promise<boolean> {
  if (!createIfMissing && !(await exists(absPath))) return false
  let doc: Record<string, unknown> = {}
  if (await exists(absPath)) {
    try {
      doc = JSON.parse(await fs.readFile(absPath, 'utf8')) as Record<string, unknown>
    } catch (e) {
      throwConfigParseError(absPath, e as Error)
    }
  }
  const hooksRoot = (doc.hooks ?? {}) as Record<string, unknown>
  const matchers = (hooksRoot[hookEvent] ?? []) as unknown[]
  if (hookAlreadyPresentClaude(matchers)) return true
  hooksRoot[hookEvent] = [
    ...matchers,
    {
      hooks: [
        {
          type: 'command',
          command,
          timeout: 30,
          statusMessage: 'Loading agent-pack experiences',
        },
      ],
    },
  ]
  doc.hooks = hooksRoot
  doc._agentPackExperience = { wiredAt: new Date().toISOString(), hookEvent, command }
  await fs.mkdir(dirname(absPath), { recursive: true })
  await fs.writeFile(absPath, JSON.stringify(doc, null, 2), 'utf8')
  return true
}

async function mergeJson5Hooks(
  absPath: string,
  hookEvent: string,
  command: string,
  createIfMissing: boolean,
): Promise<boolean> {
  if (!createIfMissing && !(await exists(absPath))) return false
  let doc: Record<string, unknown> = {}
  if (await exists(absPath)) {
    try {
      doc = JSON5.parse(await fs.readFile(absPath, 'utf8')) as Record<string, unknown>
    } catch (e) {
      throwConfigParseError(absPath, e as Error)
    }
  }
  const hooksRoot = (doc.hooks ?? {}) as Record<string, unknown>
  const matchers = (hooksRoot[hookEvent] ?? []) as unknown[]
  if (hookAlreadyPresentClaude(matchers)) return true
  hooksRoot[hookEvent] = [
    ...matchers,
    { hooks: [{ type: 'command', command, timeout: 30 }] },
  ]
  doc.hooks = hooksRoot
  await fs.mkdir(dirname(absPath), { recursive: true })
  await fs.writeFile(absPath, JSON5.stringify(doc, null, 2), 'utf8')
  return true
}

async function mergeYamlHermesHooks(
  absPath: string,
  command: string,
  createIfMissing: boolean,
): Promise<boolean> {
  if (!createIfMissing && !(await exists(absPath))) return false
  let doc: Record<string, unknown> = {}
  if (await exists(absPath)) {
    try {
      doc = yamlParse(await fs.readFile(absPath, 'utf8')) as Record<string, unknown>
    } catch (e) {
      throwConfigParseError(absPath, e as Error)
    }
  }
  const hooksRoot = (doc.hooks ?? {}) as Record<string, unknown>
  const list = (hooksRoot.pre_llm_call ?? []) as unknown[]
  const dup = list.some(
    e => typeof e === 'object' && e && String((e as { command?: string }).command ?? '').includes('experience-session-hook'),
  )
  if (dup) return true
  hooksRoot.pre_llm_call = [...list, { command, timeout: 30 }]
  doc.hooks = hooksRoot
  await fs.mkdir(dirname(absPath), { recursive: true })
  await fs.writeFile(absPath, yamlStringify(doc), 'utf8')
  return true
}

async function mergeCursorHooks(
  absPath: string,
  command: string,
  createIfMissing: boolean,
): Promise<boolean> {
  if (!createIfMissing && !(await exists(absPath))) return false
  let doc: { version?: number; hooks?: Record<string, unknown[]> } = { version: 1, hooks: {} }
  if (await exists(absPath)) {
    try {
      doc = JSON.parse(await fs.readFile(absPath, 'utf8')) as typeof doc
    } catch (e) {
      throwConfigParseError(absPath, e as Error)
    }
  }
  if (!doc.hooks) doc.hooks = {}
  const list = (doc.hooks.sessionStart ?? []) as unknown[]
  if (hookAlreadyPresentCursor(list)) return true
  doc.hooks.sessionStart = [...list, { command, timeout: 30 }]
  doc.version = doc.version ?? 1
  await fs.mkdir(dirname(absPath), { recursive: true })
  await fs.writeFile(absPath, JSON.stringify(doc, null, 2), 'utf8')
  return true
}


async function writeExperienceSidecar(
  cwd: string,
  runtime: string,
  stateDir: string,
  injectionText: string,
): Promise<string> {
  const dir = join(cwd, stateDir, 'harness', runtime)
  await fs.mkdir(dir, { recursive: true })
  const sidecar = join(dir, 'experience-inject.md')
  await fs.writeFile(sidecar, injectionText, 'utf8')
  return sidecar
}

export async function ensureExperienceHookScript(cwd: string, stateDir = '.agent-pack'): Promise<string> {
  const src = join(import.meta.dir, '..', 'scripts', 'experience-session-hook.cjs')
  const destDir = join(cwd, stateDir, 'bin')
  const dest = join(destDir, 'experience-session-hook.cjs')
  await fs.mkdir(destDir, { recursive: true })
  await fs.copyFile(src, dest)
  return dest
}

export type ExperienceProjectionReport = {
  wired: Array<{ runtime: string; label: string; config: string; event: string }>
  skipped: string[]
  /** 写对了配置但还需要用户手动做一步才会真的生效——不是 bug，只是别让用户以为装完就完事了 */
  notes: string[]
}

async function wireSlot(
  cwd: string,
  stateDir: string,
  slot: ExperienceInjectSlot,
  injectionText: string,
): Promise<{ ok: boolean; config?: string; parseError?: string }> {
  if (slot.kind === 'sidecar-markdown') {
    if (!injectionText.trim()) return { ok: false }
    const abs = slot.resolvePath(cwd)
    await fs.mkdir(dirname(abs), { recursive: true })
    await fs.writeFile(abs, injectionText, 'utf8')
    return { ok: true, config: abs }
  }

  const abs = slot.resolvePath(cwd)
  const cmd = hookCommand(cwd, stateDir, slot)
  try {
    let ok = false
    if (slot.kind === 'hook-json') {
      ok = await mergeJsonHooks(abs, slot.hookEvent, cmd, slot.createIfMissing)
    } else if (slot.kind === 'hook-json5') {
      ok = await mergeJson5Hooks(abs, slot.hookEvent, cmd, slot.createIfMissing)
    } else if (slot.kind === 'hook-yaml') {
      ok = await mergeYamlHermesHooks(abs, cmd, slot.createIfMissing)
    } else if (slot.kind === 'cursor-hooks') {
      ok = await mergeCursorHooks(abs, cmd, slot.createIfMissing)
    }
    return ok ? { ok: true, config: abs } : { ok: false }
  } catch (e) {
    // 这个 harness 的配置解析失败——绝不能因为一个 slot 坏了就让其它 harness 也装不上。
    // 记进 skipped 带上真实原因，剩下的 slot/runtime 继续走。
    if (e instanceof PackConflictError) return { ok: false, parseError: e.message }
    throw e
  }
}

/** 为已投射 / 在场的 harness 接 SessionStart / pre_llm / persona 经验注入 */
export async function projectExperienceToHarnesses(
  cwd: string,
  projectedRuntimes: string[],
  stateDir = '.agent-pack',
  opts: { includeGlobal?: boolean } = {},
): Promise<ExperienceProjectionReport> {
  const report: ExperienceProjectionReport = { wired: [], skipped: [], notes: [] }
  const missingCoverage = validateExperienceAdapterCoverage()
  if (missingCoverage.length) {
    report.skipped.push(`coverage-gap (${missingCoverage.join(', ')})`)
  }

  const injection = await loadExperienceInjection(cwd, stateDir)
  const injectionText = injection.systemPromptDelta

  await ensureExperienceHookScript(cwd, stateDir)

  const includeGlobal = opts.includeGlobal ?? false
  if (!includeGlobal) {
    report.skipped.push('user-scope slots skipped (pass includeGlobal / --global-config to opt in)')
  }

  const seen = new Set<string>()
  for (const runtime of projectedRuntimes) {
    const slots = EXPERIENCE_INJECT_SLOTS.filter(
      s => s.runtime === runtime && (includeGlobal || s.scope === 'project'),
    )
    if (!slots.length) {
      report.skipped.push(`${runtime} (无经验注入槽)`)
      continue
    }

    let any = false
    for (const slot of slots) {
      const abs = slot.resolvePath(cwd)
      const key = `${slot.kind}:${abs}#${slot.hookEvent}`
      if (seen.has(key)) continue

      const needsText = slot.kind === 'sidecar-markdown'
      if (needsText && !injectionText.trim()) continue

      const result = await wireSlot(cwd, stateDir, slot, injectionText)
      if (result.ok && result.config) {
        seen.add(key)
        any = true
        report.wired.push({
          runtime,
          label: slot.label,
          config: result.config,
          event: slot.hookEvent,
        })
        if (slot.kind === 'hook-yaml') {
          // 实测纠正（对着 E:\hermes-agent-main 的 agent/shell_hooks.py 核实）：Hermes 对
          // shell hook 有一次性 consent 门槛——写进 config.yaml 不代表会真的跑，第一次触发时
          // 没有 TTY 会直接跳过并打 warning。要免交互，配置里加 hooks_auto_accept: true，
          // 或跑的时候带 --accept-hooks / HERMES_ACCEPT_HOOKS=1。
          report.notes.push(
            `${runtime}: hook 已写入 config.yaml，但 Hermes 首次触发前需要交互确认（或设 hooks_auto_accept: true / HERMES_ACCEPT_HOOKS=1），否则会被静默跳过 —— 这不是 agent-pack 能单方面绕过的（涉及用户对 shell 命令的信任许可）`,
          )
        }
      } else if (result.parseError) {
        report.skipped.push(`${runtime}:${slot.label} — ${result.parseError.split('\n')[0]}`)
      }
    }

    if (runtime === 'dsh' && any) {
      const hooksJson = join(cwd, '.dsh', 'hooks.json')
      await upsertDshCordisInsert(join(cwd, '.dsh', 'agent-pack.cordis.yml'), [
        dshExperienceHooksInsert(hooksJson),
      ])
      if (includeGlobal) {
        const dshHome = join(homedir(), '.dsh')
        try {
          await fs.access(dshHome)
          await upsertDshCordisInsert(join(dshHome, 'cordis.patch.yml'), [
            dshExperienceHooksInsert(hooksJson),
          ])
        } catch {
          /* no ~/.dsh */
        }
      }
      if (!report.notes.includes(DSH_APPLY_NOTE)) report.notes.push(DSH_APPLY_NOTE)
    }

    if (!any && injectionText.trim()) {
      const sidecar = await writeExperienceSidecar(cwd, runtime, stateDir, injectionText)
      report.wired.push({
        runtime,
        label: 'sidecar (harness manifest)',
        config: sidecar,
        event: 'ambient',
      })
      any = true
    }

    if (!any) report.skipped.push(`${runtime} (配置文件不可写或不存在)`)
  }

  const manifestPath = join(cwd, stateDir, 'applied', 'experience-projection.json')
  await fs.mkdir(dirname(manifestPath), { recursive: true })
  await fs.writeFile(
    manifestPath,
    JSON.stringify(
      {
        wiredAt: new Date().toISOString(),
        runtimes: projectedRuntimes,
        wired: report.wired,
        skipped: report.skipped,
        experienceIds: injection.experienceIds,
      },
      null,
      2,
    ),
    'utf8',
  )

  return report
}

export type ExperienceUnwireReport = {
  removed: Array<{ config: string; event: string }>
  skipped: string[]
}

function hookEntryHasMarker(cmd: unknown): boolean {
  if (!cmd || typeof cmd !== 'object') return false
  const c = String((cmd as { command?: string }).command ?? '')
  return c.includes('experience-session-hook')
}

async function stripJsonHooks(absPath: string, hookEvent: string): Promise<boolean> {
  if (!(await exists(absPath))) return false
  let doc: Record<string, unknown>
  try {
    doc = JSON.parse(await fs.readFile(absPath, 'utf8')) as Record<string, unknown>
  } catch {
    return false
  }
  const hooksRoot = (doc.hooks ?? {}) as Record<string, unknown>
  const matchers = (hooksRoot[hookEvent] ?? []) as unknown[]
  const filtered = matchers.filter(m => {
    if (!m || typeof m !== 'object') return true
    const inner = (m as { hooks?: unknown[] }).hooks ?? []
    return !inner.some(h => hookEntryHasMarker(h))
  })
  if (filtered.length === matchers.length) return false
  hooksRoot[hookEvent] = filtered
  doc.hooks = hooksRoot
  delete doc._agentPackExperience
  await fs.writeFile(absPath, JSON.stringify(doc, null, 2), 'utf8')
  return true
}

async function stripCursorHooks(absPath: string): Promise<boolean> {
  if (!(await exists(absPath))) return false
  let doc: { hooks?: { sessionStart?: unknown[] } }
  try {
    doc = JSON.parse(await fs.readFile(absPath, 'utf8')) as typeof doc
  } catch {
    return false
  }
  const list = doc.hooks?.sessionStart ?? []
  const filtered = list.filter(h => !hookEntryHasMarker(h))
  if (filtered.length === list.length) return false
  if (!doc.hooks) doc.hooks = {}
  doc.hooks.sessionStart = filtered
  await fs.writeFile(absPath, JSON.stringify(doc, null, 2), 'utf8')
  return true
}

async function stripYamlHermesHooks(absPath: string): Promise<boolean> {
  if (!(await exists(absPath))) return false
  let doc: Record<string, unknown>
  try {
    doc = yamlParse(await fs.readFile(absPath, 'utf8')) as Record<string, unknown>
  } catch {
    return false
  }
  const hooksRoot = (doc.hooks ?? {}) as Record<string, unknown>
  const list = (hooksRoot.pre_llm_call ?? []) as unknown[]
  const filtered = list.filter(e => !hookEntryHasMarker(e))
  if (filtered.length === list.length) return false
  hooksRoot.pre_llm_call = filtered
  doc.hooks = hooksRoot
  await fs.writeFile(absPath, yamlStringify(doc), 'utf8')
  return true
}

/** 按 EXPERIENCE_INJECT_SLOTS 卸掉 experience-session-hook（读 experience-projection.json 或全槽扫描） */
export async function unwireExperienceHooks(
  cwd: string,
  stateDir = '.agent-pack',
): Promise<ExperienceUnwireReport> {
  const report: ExperienceUnwireReport = { removed: [], skipped: [] }
  let wired: Array<{ config: string; event: string }> = []
  try {
    const man = JSON.parse(
      await fs.readFile(join(cwd, stateDir, 'applied', 'experience-projection.json'), 'utf8'),
    ) as { wired?: Array<{ config: string; event: string }> }
    wired = (man.wired ?? []).map(w => ({ config: w.config, event: w.event }))
  } catch {
    for (const slot of EXPERIENCE_INJECT_SLOTS) {
      if (slot.kind === 'sidecar-markdown') continue
      wired.push({ config: slot.resolvePath(cwd), event: slot.hookEvent })
    }
  }

  const seen = new Set<string>()
  for (const w of wired) {
    const key = `${w.config}#${w.event}`
    if (seen.has(key)) continue
    seen.add(key)
    const slot = EXPERIENCE_INJECT_SLOTS.find(
      s => s.resolvePath(cwd) === w.config || w.config.endsWith(s.resolvePath(cwd).replace(/\//g, '\\')),
    )
    let ok = false
    if (w.event === 'sessionStart' || slot?.kind === 'cursor-hooks') {
      ok = await stripCursorHooks(w.config)
    } else if (slot?.kind === 'hook-yaml' || w.event === 'pre_llm_call') {
      ok = await stripYamlHermesHooks(w.config)
    } else {
      ok = await stripJsonHooks(w.config, w.event || 'SessionStart')
    }
    if (ok) report.removed.push(w)
    else report.skipped.push(w.config)
  }

  return report
}
