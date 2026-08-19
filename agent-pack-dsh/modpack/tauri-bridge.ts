#!/usr/bin/env bun
/**
 * Tauri sidecar：bun 调 launcher-api，stdout 一份 JSON。
 */
import { ensureRoot } from './launcher.js'
import { invokeLauncherApi } from './launcher-api.js'

const method = process.argv[2]
const params = JSON.parse(process.argv[3] || '{}') as Record<string, unknown>
const rootPath = process.env.PACK_LAUNCHER_ROOT?.trim()
if (!method) {
  console.error('Usage: tauri-bridge <method> <json-params>')
  process.exit(2)
}
if (!rootPath) {
  console.error('PACK_LAUNCHER_ROOT is required')
  process.exit(2)
}
try {
  const result = await invokeLauncherApi(ensureRoot(rootPath), method, params)
  process.stdout.write(JSON.stringify({ ok: true, result }) + '\n')
} catch (e) {
  const err = e as { diagnostic?: unknown; message?: string }
  process.stdout.write(JSON.stringify({ ok: false, error: err.diagnostic || String(err.message || e) }) + '\n')
  process.exit(1)
}
