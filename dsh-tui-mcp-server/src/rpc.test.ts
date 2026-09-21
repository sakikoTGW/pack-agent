#!/usr/bin/env bun
import { unwrapResult } from './rpc.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const ok = unwrapResult({
  type: 'server-response',
  result: { ok: true, value: { items: [{ sessionId: 'abc' }] } },
})
if (!ok || typeof ok !== 'object' || !('items' in ok)) fail('ok value')
const items = (ok as { items: { sessionId: string }[] }).items
if (items[0]?.sessionId !== 'abc') fail('items')

let threw = false
try {
  unwrapResult({
    type: 'server-response',
    result: { ok: false, error: { code: 'model-unavailable', message: 'no key' } },
  })
} catch (e) {
  threw = String(e).includes('model-unavailable') || String(e).includes('no key')
}
if (!threw) fail('refused rpc must throw with the error body')

threw = false
try {
  unwrapResult({ type: 'server-response' })
} catch {
  threw = true
}
if (!threw) fail('missing result must throw')

console.log('✓ rpc unwrap')
