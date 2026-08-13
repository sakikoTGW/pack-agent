#!/usr/bin/env bun
import { existsSync, readFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { join } from 'node:path'
import { pathToFileURL } from 'node:url'
import { packAgentRoot } from '../src/package-root.js'
import { loadRegistry } from '../dsh-modpack/registry.js'
import { stageDshNpm } from '../scripts/stage-dsh-npm.js'
import { packTestTmp } from './tmp-root.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const dest = stageDshNpm()
for (const rel of [
  'package.json',
  'lib/index.js',
  'cordis.patch.yml',
  'README.md',
  'README.zh.md',
  'skills/pack-agent-dsh/SKILL.md',
  'dsh-modpack/registry.yaml',
  'crates/pack-index/Cargo.toml',
  'crates/pack-index/src/main.rs',
  'LICENSE',
]) {
  const p = join(dest, rel)
  if (!existsSync(p)) fail(`staged missing ${p}`)
}

const pkg = JSON.parse(readFileSync(join(dest, 'package.json'), 'utf8')) as {
  name?: string
  files?: string[]
  dsh?: { bundle?: { patch?: string } }
}
if (pkg.name !== '@sakikotgw/pack-agent-dsh') fail(`staged name ${pkg.name}`)
if (!pkg.dsh?.bundle?.patch) fail('staged package is not a dsh.bundle')
if (pkg.files?.includes('src')) fail('DSH tarball must not ship TypeScript src')

const found = packAgentRoot(join(dest, 'lib'))
if (found !== dest) fail(`packAgentRoot(staged/lib) ${found} != ${dest}`)

const lib = readFileSync(join(dest, 'lib', 'index.js'), 'utf8')
if (!lib.includes('@sakikotgw/pack-agent-dsh')) {
  fail('bundled lib must accept @sakikotgw/pack-agent-dsh as package root')
}

const reg = loadRegistry(join(dest, 'dsh-modpack', 'registry.yaml'))
if (!reg.units || Object.keys(reg.units).length < 1) fail('empty registry in staged package')

const skill = readFileSync(join(dest, 'skills', 'pack-agent-dsh', 'SKILL.md'), 'utf8')
if (!skill.includes('@sakikotgw/pack-agent-dsh')) {
  fail('staged SKILL.md must tell the host to add @sakikotgw/pack-agent-dsh')
}
const stagedReadme = readFileSync(join(dest, 'README.md'), 'utf8')
if (!stagedReadme.includes('dsh plugin --profile web add @sakikotgw/pack-agent-dsh')) {
  fail('staged README.md must install @sakikotgw/pack-agent-dsh')
}
if (stagedReadme.includes('北极星') || stagedReadme.toLowerCase().includes('north star')) {
  fail('DSH README must not use north-star copy')
}
if (skill.includes('dsh plugin --profile web add "<compiled-dir>"')) {
  fail('SKILL.md must not tell the model to plugin-add a compiled pack')
}

const href = pathToFileURL(join(dest, 'lib', 'index.js')).href
const fixture = join(import.meta.dirname, 'fixtures', 'demo.pack.json')
const out = packTestTmp(`stage-dsh-smoke-${Date.now()}`)
const smoke = join(import.meta.dirname, 'dsh-node-smoke.mjs')
const r = spawnSync('node', [smoke, href, fixture, out], {
  encoding: 'utf8',
  cwd: dest,
  env: {
    ...process.env,
    AGENT_PACK_TMP: process.env.AGENT_PACK_TMP || 'E:\\tmp\\pack-agent',
  },
})
if (r.status !== 0) {
  fail(`node import of staged lib failed (exit ${r.status}):\n${r.stderr || ''}\n${r.stdout || ''}`)
}
if (!r.stdout?.includes('ok')) fail(`unexpected staged node stdout: ${r.stdout}`)

console.log(`✓ staged ${dest}`)
console.log('[OK] stage-dsh-npm')
