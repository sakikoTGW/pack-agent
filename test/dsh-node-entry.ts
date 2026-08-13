#!/usr/bin/env bun
/**
 * DSH 用 Node 加载 bundle。官方 publish 教程交 index.js，不交 .ts。
 * 本测试只 spawn node，不用 bun 执行入口。
 */
import { existsSync, readFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { join, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import { packAgentRoot } from '../src/package-root.js'
import { packTestTmp } from './tmp-root.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const root = resolve(import.meta.dirname, '..')
if (packAgentRoot() !== root) fail(`packAgentRoot ${packAgentRoot()} != ${root}`)

const pkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8')) as {
  exports?: Record<string, string | { import?: string; default?: string; bun?: string }>
}
const exp = pkg.exports?.['./dsh']
const rel = typeof exp === 'string' ? exp : exp?.import || exp?.default
if (!rel) fail('package.json missing exports["./dsh"]')
if (rel.endsWith('.ts')) {
  fail(`DSH export is ${rel}; Node cannot load TypeScript. Official bundle ships index.js`)
}
const abs = resolve(root, rel)
if (!existsSync(abs)) fail(`DSH entry missing on disk: ${abs}`)

const href = pathToFileURL(abs).href
const fixture = join(root, 'test', 'fixtures', 'demo.pack.json')
const out = packTestTmp(`dsh-node-entry-${Date.now()}`)
const smoke = join(root, 'test', 'dsh-node-smoke.mjs')
const r = spawnSync('node', [smoke, href, fixture, out], {
  encoding: 'utf8',
  cwd: root,
  env: {
    ...process.env,
    AGENT_PACK_TMP: process.env.AGENT_PACK_TMP || 'E:\\tmp\\pack-agent',
  },
})
if (r.status !== 0) {
  fail(`node import/compile failed (exit ${r.status}):\n${r.stderr || ''}\n${r.stdout || ''}`)
}
if (!r.stdout?.includes('ok')) fail(`unexpected node stdout: ${r.stdout}`)
console.log('✓ node imports package exports["./dsh"] and compile finds registry.yaml')
console.log('[OK] dsh-node-entry')
