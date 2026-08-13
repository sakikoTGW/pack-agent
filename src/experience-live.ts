/**
 * 会话内 live experience — 主矛盾：经验活在上下文、跟着对话变（相对 skill 固化）。
 *
 * 每轮 hook（UserPromptSubmit / pre_llm_call）把本轮信号写入 live 层，
 * 注入文案随 turns 变；换 session_id 重开，不把旧会话焊死。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'

export type HookStdinPayload = {
  session_id?: string
  hook_event_name?: string
  prompt?: string
  cwd?: string
  transcript_path?: string
  /** Hermes / 其它壳可能塞在 extra */
  extra?: Record<string, unknown>
  [key: string]: unknown
}

export type LiveTurn = {
  at: string
  prompt: string
}

export type LiveState = {
  schema: 'agent-pack/experience-live/v1'
  sessionId: string
  updatedAt: string
  turns: LiveTurn[]
}

const LIVE_FILE = 'live.json'
const MAX_TURNS = 40
const MAX_PROMPT_CHARS = 500

function livePath(cwd: string, stateDir: string): string {
  return join(cwd, stateDir, 'experiences', LIVE_FILE)
}

export function parseHookStdin(raw: string): HookStdinPayload {
  const t = raw.trim()
  if (!t) return {}
  try {
    return JSON.parse(t) as HookStdinPayload
  } catch {
    return { prompt: t.slice(0, MAX_PROMPT_CHARS) }
  }
}

function extractPrompt(payload: HookStdinPayload): string {
  const direct = String(payload.prompt ?? '').trim()
  if (direct) return direct.slice(0, MAX_PROMPT_CHARS)
  const extra = payload.extra
  if (extra && typeof extra === 'object') {
    for (const key of ['prompt', 'user_message', 'message', 'text']) {
      const v = extra[key]
      if (typeof v === 'string' && v.trim()) return v.trim().slice(0, MAX_PROMPT_CHARS)
    }
  }
  return ''
}

export function formatLiveInjection(live: LiveState): string {
  if (!live.turns.length) return ''
  const lines = live.turns.map((t, i) => `${i + 1}. ${t.prompt}`)
  return [
    '[Live experience — evolves with this session; not a skill]',
    `session: ${live.sessionId}`,
    'recent turns:',
    ...lines,
  ].join('\n')
}

export async function loadLiveState(
  cwd: string,
  stateDir = '.agent-pack',
): Promise<LiveState | null> {
  try {
    const doc = JSON.parse(await fs.readFile(livePath(cwd, stateDir), 'utf8')) as LiveState
    if (doc?.schema !== 'agent-pack/experience-live/v1') return null
    return doc
  } catch {
    return null
  }
}

export async function applyLiveTurn(
  cwd: string,
  payload: HookStdinPayload,
  stateDir = '.agent-pack',
): Promise<LiveState> {
  const prompt = extractPrompt(payload)
  const sessionId = String(payload.session_id ?? 'default').trim() || 'default'
  const now = new Date().toISOString()

  let prev = await loadLiveState(cwd, stateDir)
  if (!prev || prev.sessionId !== sessionId) {
    prev = {
      schema: 'agent-pack/experience-live/v1',
      sessionId,
      updatedAt: now,
      turns: [],
    }
  }

  if (prompt) {
    const last = prev.turns[prev.turns.length - 1]
    if (!last || last.prompt !== prompt) {
      prev.turns.push({ at: now, prompt })
      if (prev.turns.length > MAX_TURNS) {
        prev.turns = prev.turns.slice(-MAX_TURNS)
      }
    }
  }

  prev.updatedAt = now
  const dir = join(cwd, stateDir, 'experiences')
  await fs.mkdir(dir, { recursive: true })
  await fs.writeFile(livePath(cwd, stateDir), JSON.stringify(prev, null, 2), 'utf8')
  return prev
}

/** 组装注入：base 罐头文案 + live（会变） */
export function mergeBaseAndLive(baseBlocks: string[], live: LiveState | null): string {
  const parts = [
    '--- agent-pack experiences (session injection, not skills) ---',
    ...baseBlocks,
  ]
  const liveText = live ? formatLiveInjection(live) : ''
  if (liveText) parts.push(liveText)
  parts.push('--- end agent-pack experiences ---')
  return parts.join('\n\n')
}
