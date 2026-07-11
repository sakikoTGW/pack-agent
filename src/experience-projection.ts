/**
 * 经验罐头 → 各 harness 会话注入口（与 L1 skills 投射并列）。
 *
 * Skill = 外挂 mod；Experience = SessionStart / pre_llm / persona 内化注入，不写 skills 树。
 * 槽位与 RUNTIME_ADAPTERS 1:1 覆盖（见 validateExperienceAdapterCoverage）。
 */
import { promises as fs } from 'node:fs'
import { homedir } from 'node:os'
import { dirname, join } from 'node:path'
import JSON5 from 'json5'
import { parse as yamlParse, stringify as yamlStringify } from 'yaml'
import { RUNTIME_ADAPTERS } from './adapters.js'
import { loadExperienceInjection } from './experience-loader.js'

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
  },
  {
    runtime: 'cursor',
    label: 'Cursor user sessionStart',
    resolvePath: () => join(homedir(), '.cursor', 'hooks.json'),
    hookEvent: 'sessionStart',
    kind: 'cursor-hooks',
    createIfMissing: false,
  },
  {
    runtime: 'claude-code',
    label: 'Claude Code project SessionStart',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'claude-code',
    label: 'Claude Code user SessionStart',
    resolvePath: () => join(homedir(), '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
  },
  {
    runtime: 'codex',
    label: 'Codex Claude-style project settings',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'codex',
    label: 'Codex project settings',
    resolvePath: cwd => join(cwd, '.codex', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'codex',
    label: 'Codex user settings',
    resolvePath: () => join(homedir(), '.codex', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
  },
  {
    runtime: 'opencode',
    label: 'OpenCode project opencode.json',
    resolvePath: cwd => join(cwd, 'opencode.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'opencode',
    label: 'OpenCode project .opencode/opencode.json',
    resolvePath: cwd => join(cwd, '.opencode', 'opencode.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'opencode',
    label: 'OpenCode user config',
    resolvePath: () => join(homedir(), '.config', 'opencode', 'opencode.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
  },
  {
    runtime: 'openclaw',
    label: 'OpenClaw project gateway',
    resolvePath: cwd => join(cwd, 'openclaw.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json5',
    createIfMissing: true,
  },
  {
    runtime: 'openclaw',
    label: 'OpenClaw user gateway',
    resolvePath: () => join(homedir(), '.openclaw', 'openclaw.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json5',
    createIfMissing: false,
  },
  {
    runtime: 'hermes',
    label: 'Hermes project pre_llm_call',
    resolvePath: cwd => join(cwd, '.hermes', 'config.yaml'),
    hookEvent: 'pre_llm_call',
    kind: 'hook-yaml',
    createIfMissing: true,
  },
  {
    runtime: 'hermes',
    label: 'Hermes user pre_llm_call',
    resolvePath: () => join(homedir(), '.hermes', 'config.yaml'),
    hookEvent: 'pre_llm_call',
    kind: 'hook-yaml',
    createIfMissing: false,
  },
  {
    runtime: 'astrbot',
    label: 'AstrBot experience sidecar (L1 plugin skills only; persona DB not supported)',
    resolvePath: cwd => join(cwd, '.agent-pack', 'harness', 'astrbot', 'experience-inject.md'),
    hookEvent: 'sidecar',
    kind: 'sidecar-markdown',
    createIfMissing: true,
  },
  {
    runtime: 'gemini-cli',
    label: 'Gemini CLI project',
    resolvePath: cwd => join(cwd, '.gemini', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'gemini-cli',
    label: 'Gemini CLI user',
    resolvePath: () => join(homedir(), '.gemini', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: false,
  },
  {
    runtime: 'windsurf',
    label: 'Windsurf project',
    resolvePath: cwd => join(cwd, '.windsurf', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'github-copilot',
    label: 'Copilot vscode settings hooks',
    resolvePath: cwd => join(cwd, '.vscode', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
  },
  {
    runtime: 'generic-agents',
    label: 'Generic Claude-style hooks',
    resolvePath: cwd => join(cwd, '.claude', 'settings.json'),
    hookEvent: 'SessionStart',
    kind: 'hook-json',
    createIfMissing: true,
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

function hookCommand(stateDir: string, slot: ExperienceInjectSlot): string {
  const rel = `${stateDir}/bin/experience-session-hook.cjs`.replace(/\\/g, '/')
  const parts: string[] = []
  if (slot.kind === 'cursor-hooks') parts.push('AGENT_PACK_HOOK_STYLE=cursor')
  const ev = slot.hookEvent
  if (ev !== 'SessionStart' && ev !== 'sessionStart') {
    parts.push(`AGENT_PACK_HOOK_EVENT=${ev}`)
  }
  parts.push(`node ${rel}`)
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
    } catch {
      doc = {}
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
    } catch {
      doc = {}
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
    } catch {
      if (!createIfMissing) return false
      doc = {}
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
    } catch {
      doc = { version: 1, hooks: {} }
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
}

async function wireSlot(
  cwd: string,
  stateDir: string,
  slot: ExperienceInjectSlot,
  injectionText: string,
): Promise<{ ok: boolean; config?: string }> {
  if (slot.kind === 'sidecar-markdown') {
    if (!injectionText.trim()) return { ok: false }
    const abs = slot.resolvePath(cwd)
    await fs.mkdir(dirname(abs), { recursive: true })
    await fs.writeFile(abs, injectionText, 'utf8')
    return { ok: true, config: abs }
  }

  const abs = slot.resolvePath(cwd)
  const cmd = hookCommand(stateDir, slot)
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
}

/** 为已投射 / 在场的 harness 接 SessionStart / pre_llm / persona 经验注入 */
export async function projectExperienceToHarnesses(
  cwd: string,
  projectedRuntimes: string[],
  stateDir = '.agent-pack',
): Promise<ExperienceProjectionReport> {
  const report: ExperienceProjectionReport = { wired: [], skipped: [] }
  const missingCoverage = validateExperienceAdapterCoverage()
  if (missingCoverage.length) {
    report.skipped.push(`coverage-gap (${missingCoverage.join(', ')})`)
  }

  const injection = await loadExperienceInjection(cwd, stateDir)
  const injectionText = injection.systemPromptDelta

  await ensureExperienceHookScript(cwd, stateDir)

  const seen = new Set<string>()
  for (const runtime of projectedRuntimes) {
    const slots = EXPERIENCE_INJECT_SLOTS.filter(s => s.runtime === runtime)
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
      }
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

/** 卸掉 experience-session-hook */
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
    let ok = false
    if (w.event === 'sessionStart') {
      ok = (await stripCursorHooks(w.config)) || (await stripJsonHooks(w.config, 'SessionStart'))
    } else if (w.event === 'pre_llm_call') {
      ok = await stripYamlHermesHooks(w.config)
    } else {
      ok = await stripJsonHooks(w.config, w.event || 'SessionStart')
    }
    if (ok) report.removed.push(w)
    else report.skipped.push(w.config)
  }

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
