import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'

export const GATEWAY_FILE = 'pad-gateway.json'
export const GATEWAY_SCHEMA = 'pack-agent.pad-gateway/v1'

export type GatewayAd = {
  schema: string
  url: string
  token: string
  pid: number
  profile?: string
  release?: string
}

export type PublicAd = {
  url: string
  pid: number
  profile?: string
  release?: string
  hasToken: true
}

export function loadAd(homeOrFile: string): GatewayAd {
  const file = homeOrFile.endsWith('.json') ? homeOrFile : join(homeOrFile, GATEWAY_FILE)
  if (!existsSync(file)) {
    throw new Error(`pad-gateway.json missing at ${file}. Start the instance with @sakikotgw/pad-gateway in this profile's dsh.profile.bundles.`)
  }
  let raw: unknown
  try {
    raw = JSON.parse(readFileSync(file, 'utf8'))
  } catch (e) {
    throw new Error(`pad-gateway.json is not JSON: ${e instanceof Error ? e.message : e}`)
  }
  return parseAd(raw)
}

export function parseAd(raw: unknown): GatewayAd {
  if (!raw || typeof raw !== 'object') throw new Error('pad-gateway.json is empty')
  const o = raw as Record<string, unknown>
  const schema = String(o.schema ?? '')
  const url = String(o.url ?? '')
  const token = String(o.token ?? '')
  const pid = Number(o.pid)
  if (schema !== GATEWAY_SCHEMA) {
    throw new Error(`unexpected pad-gateway schema ${schema || '(missing)'}; want ${GATEWAY_SCHEMA}`)
  }
  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    throw new Error(`pad-gateway url is not a URL: ${url}`)
  }
  if (parsed.protocol !== 'http:') throw new Error('pad-gateway url must be http loopback')
  if (parsed.hostname !== '127.0.0.1' && parsed.hostname !== 'localhost') {
    throw new Error('pad-gateway url is not loopback')
  }
  if (token.length < 16) throw new Error('pad-gateway token is too short')
  if (!Number.isInteger(pid) || pid <= 0) throw new Error('pad-gateway pid missing')
  return {
    schema,
    url,
    token,
    pid,
    profile: typeof o.profile === 'string' ? o.profile : undefined,
    release: typeof o.release === 'string' ? o.release : undefined,
  }
}

export function pingUrl(ad: GatewayAd): string {
  const root = ad.url.replace(/\/+$/, '')
  const base = root.endsWith('/api') ? root.slice(0, -4) : root
  return `${base}/pad/ping`
}

export function publicAd(ad: GatewayAd): PublicAd {
  return {
    url: ad.url,
    pid: ad.pid,
    profile: ad.profile,
    release: ad.release,
    hasToken: true,
  }
}

export function resolveHome(env: NodeJS.ProcessEnv = process.env): string {
  const adFile = env.PAD_GATEWAY_AD?.trim()
  if (adFile) return dirname(adFile)
  const home = env.DSH_HOME?.trim()
  if (home) return home
  throw new Error('Set DSH_HOME to the instance home, or PAD_GATEWAY_AD to that home\'s pad-gateway.json')
}
