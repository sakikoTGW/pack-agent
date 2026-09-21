#!/usr/bin/env bun
import { callAllowed } from './client.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

let threw = false
try {
  await callAllowed({ method: 'credentials.set', payload: { ref: 'DEEPSEEK_API_KEY', value: 'nope' } })
} catch (e) {
  threw = String(e).includes('not allowed')
}
if (!threw) fail('credentials.set must be blocked')

console.log('✓ rpc allowlist')
