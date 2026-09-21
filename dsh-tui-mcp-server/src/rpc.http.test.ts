#!/usr/bin/env bun
import { createServer } from 'node:http'
import { rpc, ping } from './rpc.ts'
import type { GatewayAd } from './ad.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const ad = {
  schema: 'pack-agent.pad-gateway/v1',
  url: '',
  token: 'b'.repeat(32),
  pid: 1,
} as GatewayAd

const server = createServer((req, res) => {
  const auth = req.headers.authorization
  if (auth !== `Bearer ${ad.token}`) {
    res.writeHead(401)
    res.end('no')
    return
  }
  if (req.url === '/pad/ping') {
    res.writeHead(200, { 'content-type': 'application/json' })
    res.end(JSON.stringify({ ok: true, pid: 9 }))
    return
  }
  if (req.url === '/api/session.list') {
    const chunks: Buffer[] = []
    req.on('data', (c) => chunks.push(c))
    req.on('end', () => {
      const body = JSON.parse(Buffer.concat(chunks).toString('utf8'))
      if (body.type !== 'client-request' || body.method !== 'session.list') {
        res.writeHead(400)
        res.end('bad envelope')
        return
      }
      res.writeHead(200, { 'content-type': 'application/json' })
      res.end(JSON.stringify({
        type: 'server-response',
        result: { ok: true, value: { items: [{ sessionId: 's1', running: false, blank: true }] } },
      }))
    })
    return
  }
  res.writeHead(404)
  res.end()
})

await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', () => resolve()))
const addr = server.address()
if (!addr || typeof addr === 'string') fail('bind')
ad.url = `http://127.0.0.1:${addr.port}/api`

try {
  const p = await ping(ad) as { ok?: boolean }
  if (p.ok !== true) fail(`ping ${JSON.stringify(p)}`)
  const listed = await rpc(ad, 'session.list', {}) as { items: { sessionId: string }[] }
  if (listed.items[0]?.sessionId !== 's1') fail(JSON.stringify(listed))
} finally {
  server.close()
}

console.log('✓ rpc http')
