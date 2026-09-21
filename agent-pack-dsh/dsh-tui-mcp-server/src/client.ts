import { loadAd, publicAd, resolveHome, type GatewayAd } from './ad.ts'
import { ping, rpc } from './rpc.ts'
import { compactHistory } from './history.ts'

export const ALLOWED_RPC = new Set([
  'session.list',
  'session.search',
  'session.create',
  'session.history',
  'session.models',
  'session.selectModel',
  'session.rename',
  'session.fork',
  'session.prompt',
  'session.updateQueue',
  'session.cancel',
  'workspace.list',
  'host.describe',
  'skill.list',
  'agentPreset.list',
  'llm.providers',
  'llm.models',
  'credentials.describe',
])

export function adFromEnv(home?: string): GatewayAd {
  return loadAd(home?.trim() || resolveHome())
}

export function sessionsOf(value: unknown): Array<Record<string, unknown>> {
  const items = value && typeof value === 'object' && Array.isArray((value as { items?: unknown }).items)
    ? (value as { items: unknown[] }).items
    : []
  return items.map((row) => {
    if (!row || typeof row !== 'object') return { raw: row }
    const s = row as Record<string, unknown>
    return {
      sessionId: s.sessionId,
      running: s.running,
      blank: s.blank,
      agentPreset: s.agentPreset,
      cwd: s.cwd,
      updatedAt: s.updatedAt,
    }
  })
}

export async function pingStatus(home?: string) {
  const ad = adFromEnv(home)
  const body = await ping(ad)
  return { ad: publicAd(ad), ping: body }
}

export async function listSessions(home?: string) {
  const ad = adFromEnv(home)
  return { ad: publicAd(ad), sessions: sessionsOf(await rpc(ad, 'session.list', {})) }
}

export async function promptSession(opts: {
  home?: string
  sessionId: string
  text: string
  mode?: 'queue' | 'steer'
}) {
  const ad = adFromEnv(opts.home)
  const accepted = await rpc(ad, 'session.prompt', {
    sessionId: opts.sessionId,
    mode: opts.mode ?? 'queue',
    content: [{ type: 'text', text: opts.text }],
  })
  return { accepted }
}

export async function historySession(opts: {
  home?: string
  sessionId: string
  maxMessages?: number
  beforeSeq?: number
}) {
  const ad = adFromEnv(opts.home)
  const page = await rpc(ad, 'session.history', {
    sessionId: opts.sessionId,
    maxMessages: opts.maxMessages ?? 24,
    ...(opts.beforeSeq != null ? { beforeSeq: opts.beforeSeq } : {}),
  }, 30000)
  return compactHistory(page)
}

export async function waitTurn(opts: {
  home?: string
  sessionId: string
  timeoutMs?: number
  intervalMs?: number
}) {
  const ad = adFromEnv(opts.home)
  const timeoutMs = Math.min(Math.max(opts.timeoutMs ?? 20000, 500), 120000)
  const intervalMs = Math.min(Math.max(opts.intervalMs ?? 400, 100), 5000)
  const start = Date.now()
  let sawRunning = false
  let lastRunning: unknown
  while (Date.now() - start < timeoutMs) {
    const rows = sessionsOf(await rpc(ad, 'session.list', {}))
    const row = rows.find((s) => s.sessionId === opts.sessionId)
    lastRunning = row?.running
    if (row?.running === true) sawRunning = true
    if (sawRunning && row?.running === false) break
    if (!sawRunning && Date.now() - start >= 1200 && row?.running === false) break
    await new Promise((r) => setTimeout(r, intervalMs))
  }
  const history = compactHistory(await rpc(ad, 'session.history', {
    sessionId: opts.sessionId,
    maxMessages: 24,
  }, 30000))
  return {
    elapsedMs: Date.now() - start,
    sawRunning,
    running: lastRunning,
    history,
  }
}

export async function describeCredentials(home?: string, refs?: string[]) {
  const ad = adFromEnv(home)
  const names = refs && refs.length > 0 ? refs : ['DEEPSEEK_API_KEY']
  return rpc(ad, 'credentials.describe', { refs: names })
}

export async function callAllowed(opts: {
  home?: string
  method: string
  payload?: unknown
}) {
  if (!ALLOWED_RPC.has(opts.method)) {
    throw new Error(`rpc ${opts.method} is not allowed on this MCP. Use session.prompt / history / list, or credentials.describe. credentials.set is blocked so keys never enter the chat.`)
  }
  const ad = adFromEnv(opts.home)
  const timeout = opts.method === 'session.history' ? 30000 : 15000
  const value = await rpc(ad, opts.method, opts.payload ?? {}, timeout)
  if (opts.method === 'session.history') return compactHistory(value)
  if (opts.method === 'session.list') return { sessions: sessionsOf(value) }
  return value
}
