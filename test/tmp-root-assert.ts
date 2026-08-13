#!/usr/bin/env bun
/** Windows：临时目录必须在 E:，禁止再写 C:\\Users\\...\\Temp */
import { existsSync, unlinkSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { DEFAULT_WIN_PACK_TMP, packTmpRoot } from '../src/tmp-root.js'

const root = packTmpRoot()

if (process.platform === 'win32' && existsSync('E:\\')) {
  if (!root.toUpperCase().startsWith('E:\\')) {
    console.error(`✗ packTmpRoot not on E: got ${root}`)
    process.exit(1)
  }
  if (!process.env.AGENT_PACK_TMP?.trim() && root !== DEFAULT_WIN_PACK_TMP) {
    console.error(`✗ expected ${DEFAULT_WIN_PACK_TMP} got ${root}`)
    process.exit(1)
  }
  for (const k of ['TMP', 'TEMP', 'TMPDIR', 'BUN_TMPDIR'] as const) {
    const v = process.env[k] || ''
    if (!v.toUpperCase().startsWith('E:\\')) {
      console.error(`✗ ${k}=${v} still not on E:`)
      process.exit(1)
    }
  }
  const probe = join(root, `probe-${Date.now()}.txt`)
  writeFileSync(probe, 'ok')
  if (!existsSync(probe) || !probe.toUpperCase().startsWith('E:\\')) {
    console.error(`✗ probe not on E: ${probe}`)
    process.exit(1)
  }
  unlinkSync(probe)
  console.log(`✓ tmp on E: ${root}`)
} else {
  console.log(`✓ packTmpRoot ${root}`)
}
