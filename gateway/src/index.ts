/**
 * PAD 的管理侧门。
 *
 * DSH 官方的 `@deepseek-ai/dsh-host-apiproxy` 只提供 `ctx.apiProxy`，自己不注册路由
 * （README: "This package registers no routes; carriers such as HTTP wrap ctx.apiProxy
 * themselves"）。Web 版由 web-app 组合包接上 HTTP；终端 profile 没人接，所以 PAD 在这里
 * 接一份 loopback carrier，让桌面端读到的东西和 DSH Web UI 完全同源。
 *
 * 这必须是独立插件：cordis 的 inject 会等服务就绪，如果把 apiProxy 混进主插件的 inject，
 * 没装 apiproxy 的 profile 会连 tools / skills 一起等不到。
 */
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http'
import { randomBytes, timingSafeEqual } from 'node:crypto'
import { appendFileSync, chmodSync, existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

export const name = 'pack-agent-pad-gateway'
export const inject = ['apiProxy']

export const GATEWAY_FILE = 'pad-gateway.json'
export const GATEWAY_LOG = 'pad-gateway.log'
export const GATEWAY_SCHEMA = 'pack-agent.pad-gateway/v1'

export type PadGatewayAd = {
  schema: typeof GATEWAY_SCHEMA
  url: string
  token: string
  pid: number
  release?: string
  profile?: string
}

type FetchHandler = { fetch: (input: Request) => Promise<Response> }

type GatewayContext = {
  apiProxy: unknown
  on?: (event: 'dispose', fn: () => void) => void
  logger?: { info?: (msg: string) => void; warn?: (msg: string) => void }
}

export type PadGatewayConfig = {
  /** Override the advertised DSH_HOME; defaults to `process.env.DSH_HOME`. */
  home?: string
}

function tokenOk(got: string, expected: string): boolean {
  const a = Buffer.from(got)
  const b = Buffer.from(expected)
  return a.length === b.length && timingSafeEqual(a, b)
}

function bearer(req: IncomingMessage): string {
  const raw = req.headers.authorization
  if (typeof raw !== 'string') return ''
  return raw.startsWith('Bearer ') ? raw.slice(7).trim() : ''
}

function readBody(req: IncomingMessage): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = []
    let size = 0
    req.on('data', (c: Buffer) => {
      size += c.length
      // Attachments ride session.attachment, so a management call is never large.
      if (size > 8 * 1024 * 1024) {
        reject(new Error('request body too large'))
        req.destroy()
        return
      }
      chunks.push(c)
    })
    req.on('end', () => resolve(Buffer.concat(chunks)))
    req.on('error', reject)
  })
}

/** Translate a node request into the WHATWG Request that toFetchHandler expects. */
async function toRequest(req: IncomingMessage): Promise<Request> {
  const url = `http://127.0.0.1${req.url ?? '/'}`
  const headers = new Headers()
  for (const [k, v] of Object.entries(req.headers)) {
    if (typeof v === 'string') headers.set(k, v)
    else if (Array.isArray(v)) headers.set(k, v.join(', '))
  }
  const method = req.method ?? 'GET'
  const init: RequestInit = { method, headers }
  if (method !== 'GET' && method !== 'HEAD') {
    const body = await readBody(req)
    if (body.length > 0) init.body = body
  }
  // The client aborting an SSE stream has to reach api.events.mux's signal.
  const ac = new AbortController()
  req.on('close', () => ac.abort())
  init.signal = ac.signal
  return new Request(url, init)
}

/** Stream a fetch Response back out through the node response, SSE included. */
async function writeResponse(res: ServerResponse, response: Response): Promise<void> {
  const headers: Record<string, string> = {}
  response.headers.forEach((value, key) => { headers[key] = value })
  res.writeHead(response.status, headers)
  if (!response.body) {
    res.end()
    return
  }
  const reader = response.body.getReader()
  try {
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break
      if (value) res.write(Buffer.from(value))
    }
  } catch {
    /* client went away mid-stream */
  } finally {
    res.end()
  }
}

export function apply(ctx: GatewayContext, config: PadGatewayConfig = {}): void {
  const home = (config.home ?? process.env.DSH_HOME ?? '').trim()

  // A TUI owns the terminal and cordis may hand us no logger, so every diagnostic
  // also lands in a file inside the home PAD is about to read. Without this a
  // failure to boot is completely silent.
  const trace = (line: string) => {
    const stamped = `${new Date().toISOString()} ${line}\n`
    ctx.logger?.info?.(line)
    if (!home) {
      process.stderr.write(`[pad-gateway] ${stamped}`)
      return
    }
    try { appendFileSync(join(home, GATEWAY_LOG), stamped) } catch { /* nowhere to report */ }
  }

  if (!home) {
    trace('no DSH_HOME, pad gateway not started')
    ctx.logger?.warn?.('[pack-agent] no DSH_HOME, pad gateway not started')
    return
  }

  const token = randomBytes(32).toString('hex')
  const file = join(home, GATEWAY_FILE)
  let server: Server | undefined
  trace(`apply() in pid ${process.pid}`)

  const boot = async () => {
    trace('importing @deepseek-ai/dsh-host-apiproxy')
    const mod = await import('@deepseek-ai/dsh-host-apiproxy') as {
      toFetchHandler: (api: unknown) => FetchHandler
    }
    if (typeof mod.toFetchHandler !== 'function') {
      throw new Error('apiproxy has no toFetchHandler export')
    }
    if (!ctx.apiProxy) throw new Error('ctx.apiProxy is missing despite inject')
    const handler = mod.toFetchHandler(ctx.apiProxy)
    trace('toFetchHandler ready')

    server = createServer((req, res) => {
      void (async () => {
        try {
          if (!tokenOk(bearer(req), token)) {
            res.writeHead(401, { 'content-type': 'application/json' })
            res.end(JSON.stringify({ error: 'unauthorized' }))
            return
          }
          const path = (req.url ?? '/').split('?')[0]
          if (path === '/pad/ping') {
            res.writeHead(200, { 'content-type': 'application/json' })
            res.end(JSON.stringify({
              ok: true,
              pid: process.pid,
              profile: process.env.DSH_PROFILE ?? null,
              schema: GATEWAY_SCHEMA,
            }))
            return
          }
          await writeResponse(res, await handler.fetch(await toRequest(req)))
        } catch (err) {
          if (!res.headersSent) res.writeHead(500, { 'content-type': 'text/plain; charset=utf-8' })
          res.end(String(err instanceof Error ? err.message : err))
        }
      })()
    })

    await new Promise<void>((resolve, reject) => {
      server!.once('error', reject)
      server!.listen(0, '127.0.0.1', () => resolve())
    })

    const addr = server!.address()
    if (!addr || typeof addr === 'string') throw new Error('pad gateway failed to bind')

    const ad: PadGatewayAd = {
      schema: GATEWAY_SCHEMA,
      url: `http://127.0.0.1:${addr.port}/api`,
      token,
      pid: process.pid,
      ...process.env.DSH_PROFILE ? { profile: process.env.DSH_PROFILE } : {},
    }
    writeFileSync(file, `${JSON.stringify(ad, null, 2)}\n`, { encoding: 'utf8', mode: 0o600 })
    try { chmodSync(file, 0o600) } catch { /* windows */ }
    trace(`listening on ${ad.url}`)
  }

  void boot().catch((err: unknown) => {
    const detail = err instanceof Error ? `${err.message}\n${err.stack ?? ''}` : String(err)
    trace(`boot failed: ${detail}`)
    ctx.logger?.warn?.(`[pack-agent] pad gateway failed: ${detail}`)
  })

  ctx.on?.('dispose', () => {
    server?.close()
    // Only remove our own advertisement, never a newer process's.
    try {
      if (existsSync(file)) {
        const cur = JSON.parse(readFileSync(file, 'utf8')) as PadGatewayAd
        if (cur.pid === process.pid && cur.token === token) rmSync(file, { force: true })
      }
    } catch {
      rmSync(file, { force: true })
    }
  })
}

export default apply
