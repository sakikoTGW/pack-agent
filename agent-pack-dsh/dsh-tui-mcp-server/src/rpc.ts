import { randomUUID } from 'node:crypto'
import type { GatewayAd } from './ad.ts'
import { pingUrl } from './ad.ts'

export type RpcError = {
  method: string
  detail: string
}

export function unwrapResult(body: unknown): unknown {
  if (!body || typeof body !== 'object') throw new Error('apiproxy answer is not an object')
  const result = (body as { result?: unknown }).result
  if (result === undefined) throw new Error('apiproxy answer has no result')
  if (!result || typeof result !== 'object') throw new Error('apiproxy result is not an object')
  const r = result as { ok?: unknown; value?: unknown; error?: unknown }
  if (r.ok === false) {
    throw new Error(formatRefused(r.error))
  }
  return r.value
}

function formatRefused(error: unknown): string {
  if (error == null) return 'apiproxy refused the call'
  if (typeof error === 'string') return error
  try {
    return JSON.stringify(error)
  } catch {
    return String(error)
  }
}

export async function ping(ad: GatewayAd, timeoutMs = 6000): Promise<unknown> {
  const res = await fetch(pingUrl(ad), {
    headers: { authorization: `Bearer ${ad.token}` },
    signal: AbortSignal.timeout(timeoutMs),
  })
  const text = await res.text()
  if (!res.ok) throw new Error(`pad/ping HTTP ${res.status}: ${clip(text, 400)}`)
  try {
    return JSON.parse(text)
  } catch {
    return { raw: text }
  }
}

export async function rpc(
  ad: GatewayAd,
  method: string,
  payload: unknown = {},
  timeoutMs = 15000,
): Promise<unknown> {
  const envelope = {
    type: 'client-request',
    rpcId: randomUUID(),
    method,
    payload: payload ?? {},
  }
  const res = await fetch(`${ad.url.replace(/\/+$/, '')}/${method}`, {
    method: 'POST',
    headers: {
      authorization: `Bearer ${ad.token}`,
      'content-type': 'application/json',
    },
    body: JSON.stringify(envelope),
    signal: AbortSignal.timeout(timeoutMs),
  })
  const text = await res.text()
  if (!res.ok) {
    throw new Error(`apiproxy ${method} HTTP ${res.status}: ${clip(text, 800)}`)
  }
  let body: unknown
  try {
    body = JSON.parse(text)
  } catch {
    throw new Error(`apiproxy ${method} returned non-JSON: ${clip(text, 400)}`)
  }
  return unwrapResult(body)
}

function clip(s: string, n: number): string {
  return s.length <= n ? s : `${s.slice(0, n)}…`
}
