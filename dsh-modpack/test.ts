#!/usr/bin/env bun
/**
 * DSH 整合包编译器：PackDoc → 官方 dsh.bundle（package.json + cordis.patch.yml + skills）。
 */
import { access, readFile, rm } from 'node:fs/promises'
import { join } from 'node:path'
import { parse as yamlParse } from 'yaml'
import { packTestTmp } from '../test/tmp-root.js'
import type { PackDoc } from '../src/types.js'
import { compilePackToDshBundle } from './compile.js'
import { loadRegistry } from './registry.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const registry = loadRegistry()
if (registry.units.skill?.plugin !== '@deepseek-ai/dsh-skill-filesystem') {
  fail(`registry skill plugin: ${registry.units.skill?.plugin}`)
}
if (registry.units.mcp?.plugin !== '@deepseek-ai/dsh-mcp-client') {
  fail(`registry mcp plugin: ${registry.units.mcp?.plugin}`)
}
if (registry.units.hooks?.plugin !== '@deepseek-ai/dsh-hooks-claude-code') {
  fail(`registry hooks plugin: ${registry.units.hooks?.plugin}`)
}
for (const k of ['skill', 'mcp', 'hooks', 'rules', 'commands', 'persona', 'preset', 'plugins', 'subagents', 'memory', 'settings', 'experiences']) {
  if (!registry.units[k]) fail(`registry missing unit ${k}`)
}
if (registry.units.persona?.plugin !== '@deepseek-ai/dsh-persona') {
  fail(`registry persona plugin: ${registry.units.persona?.plugin}`)
}
console.log('✓ registry maps every packable unit')

const fixturePath = join(import.meta.dir, '..', 'test', 'fixtures', 'demo.pack.json')
const demo = JSON.parse(await readFile(fixturePath, 'utf8')) as PackDoc
demo.tools = {
  mcp: [{ name: 'demo-fs', command: 'npx', args: ['-y', '@modelcontextprotocol/server-filesystem', '.'] }],
}

const out = packTestTmp(`dsh-modpack-${Date.now()}`)
const result = await compilePackToDshBundle(demo, out)

const pkgPath = join(result.dir, 'package.json')
const patchPath = join(result.dir, 'cordis.patch.yml')
const skillPath = join(result.dir, 'skills', 'demo', 'SKILL.md')
const installPath = join(result.dir, 'INSTALL.md')

for (const p of [pkgPath, patchPath, skillPath, installPath]) {
  try {
    await access(p)
  } catch {
    fail(`missing ${p}`)
  }
}

const pkg = JSON.parse(await readFile(pkgPath, 'utf8')) as {
  name?: string
  dsh?: { bundle?: { patch?: string } }
}
if (!pkg.dsh?.bundle?.patch) fail('package.json missing dsh.bundle.patch')
if (pkg.dsh.bundle.patch !== './cordis.patch.yml') fail(`patch field ${pkg.dsh.bundle.patch}`)
if (!String(pkg.name || '').includes('demo')) fail(`npm name should include pack name, got ${pkg.name}`)
console.log('✓ package.json is a dsh.bundle')

const skill = await readFile(skillPath, 'utf8')
if (!skill.includes('install smoke test skill')) fail('skill body not copied')
console.log('✓ skills/ copied from pack bundle')

const demoModPath = join(result.dir, 'mods', 'demo', 'mod.json')
const demoModSkill = join(result.dir, 'mods', 'demo', 'SKILL.md')
try {
  await access(demoModPath)
  await access(demoModSkill)
} catch {
  fail(`modpack must ship mods/, missing ${demoModPath} or ${demoModSkill}`)
}
const demoMod = JSON.parse(await readFile(demoModPath, 'utf8')) as {
  schema?: string
  id?: string
  kind?: string
  publisher?: string
  version?: string
  spec?: string
}
if (demoMod.schema !== 'pack-agent.mod/v0') fail(`mod schema ${demoMod.schema}`)
if (demoMod.id !== 'demo') fail(`mod id ${demoMod.id}`)
if (demoMod.kind !== 'skill') fail(`mod kind ${demoMod.kind}`)
if (!('publisher' in demoMod) || !('version' in demoMod) || !('spec' in demoMod)) {
  fail('mod.json must reserve publisher/version/spec')
}
const catalog = JSON.parse(await readFile(join(result.dir, 'catalog.json'), 'utf8')) as { mods?: unknown[] }
if (!Array.isArray(catalog.mods) || catalog.mods.length < 1) fail(`catalog.mods missing: ${JSON.stringify(catalog)}`)
if (!catalog.mods.some((m: { id?: string }) => m.id === 'demo')) fail('catalog.mods must list demo')
if (!catalog.mods.some((m: { id?: string; kind?: string }) => m.kind === 'mcp' && m.id === 'demo-fs')) {
  fail('MCP must be a shipped mod')
}
console.log('✓ modpack ships mods/ with reserved identity fields')

const patchRaw = yamlParse(await readFile(patchPath, 'utf8'))
if (!Array.isArray(patchRaw)) fail('cordis.patch.yml must be a YAML list')
const inserts = patchRaw.flatMap((op: { insert?: unknown[] }) => (Array.isArray(op.insert) ? op.insert : []))
const skillRow = inserts.find((it: { name?: string }) => it.name === '@deepseek-ai/dsh-skill-filesystem')
if (!skillRow) fail('patch must insert dsh-skill-filesystem for bundled skills')
const mcpRow = inserts.find((it: { name?: string; config?: { serverName?: string } }) => {
  return it.name === '@deepseek-ai/dsh-mcp-client' && it.config?.serverName === 'demo-fs'
})
if (!mcpRow) fail('patch must insert dsh-mcp-client for pack MCP')
console.log('✓ cordis.patch.yml insert skill-filesystem + mcp-client')

const install = await readFile(installPath, 'utf8')
if (!install.includes('allow')) fail('INSTALL.md must tell packagent dsh allow')
if (install.includes(`dsh plugin --profile web add "${result.dir}`)) {
  fail('INSTALL.md must not dsh plugin add the projected pack')
}
if (!result.installCommand.includes('allow')) fail(`installCommand ${result.installCommand}`)
console.log('✓ install path is project + allow, not dsh plugin add')

await rm(out, { recursive: true, force: true }).catch(() => {})

const skillMd = await readFile(join(import.meta.dir, '..', 'skills', 'agent-pack', 'SKILL.md'), 'utf8')
const ours: PackDoc = {
  name: 'packer',
  version: '0.4.0',
  description: 'Agent Modpack packer',
  knowledge: { skills: [{ name: 'agent-pack', source: 'bundled' }] },
  bundle: { portable: true, files: [{ path: 'skills/agent-pack/SKILL.md', content: skillMd }] },
}
const oursOut = packTestTmp(`dsh-modpack-ours-${Date.now()}`)
const oursResult = await compilePackToDshBundle(ours, oursOut)
const oursSkill = join(oursResult.dir, 'skills', 'agent-pack', 'SKILL.md')
try {
  await access(oursSkill)
} catch {
  fail(`ours missing ${oursSkill}`)
}
const oursBody = await readFile(oursSkill, 'utf8')
if (!oursBody.includes('Agent Modpack')) fail('ours packer skill not mounted')
console.log('✓ ours: packer skill compiled onto DSH bundle')
await rm(oursOut, { recursive: true, force: true }).catch(() => {})

const everything: PackDoc = {
  name: 'everything',
  version: '0.1.0',
  description: 'all packable layers',
  harness: { base_system_prompt: 'You are the everything pack agent.' },
  commands: { files: [{ name: 'pack-hello', ref: 'commands/pack-hello.md' }] },
  automation: { hooks: [{ name: 'session', ref: 'automation/hooks.json' }] },
  dsh: {
    persona: 'You are the everything pack agent on DSH.',
    preset: { name: 'everything', description: 'full modpack' },
    plugins: [
      { id: 'pack-agent-plan', name: '@deepseek-ai/dsh-plan-mode' },
      { id: 'pack-agent-goal-tool', name: '@deepseek-ai/dsh-tool-goal' },
    ],
  },
  bundle: {
    portable: true,
    files: [
      { path: 'skills/demo/SKILL.md', content: '---\nname: demo\n---\n# demo\n' },
      { path: 'commands/pack-hello.md', content: '# hello\n' },
      { path: 'rules/AGENTS.md', content: '# agents\n' },
      { path: 'automation/hooks.json', content: '{"hooks":{}}\n' },
      { path: 'agents/kid.md', content: '# subagent\n' },
      { path: 'memory/notes.md', content: '# mem\n' },
      { path: 'settings/frag.json', content: '{}\n' },
    ],
  },
}
const allOut = packTestTmp(`dsh-modpack-all-${Date.now()}`)
const allResult = await compilePackToDshBundle(everything, allOut)
for (const rel of ['commands/pack-hello.md', 'instructions/AGENTS.md', 'automation/hooks.json', 'agents/kid.md', 'memory/notes.md', 'settings/frag.json', 'preset.yml']) {
  try {
    await access(join(allResult.dir, ...rel.split('/')))
  } catch {
    fail(`everything missing ${rel}`)
  }
}
const allPatch = yamlParse(await readFile(join(allResult.dir, 'cordis.patch.yml'), 'utf8'))
const allInserts = (Array.isArray(allPatch) ? allPatch : []).flatMap((op: { insert?: Array<{ id?: string; name?: string; config?: { text?: string } }> }) => op.insert ?? [])
if (!allInserts.some(it => it.name === '@deepseek-ai/dsh-persona' && String(it.config?.text || '').includes('everything pack agent'))) {
  fail('persona insert missing')
}
if (!allInserts.some(it => it.id === 'pack-agent-plan' && it.name === '@deepseek-ai/dsh-plan-mode')) {
  fail('passthrough plugin pack-agent-plan missing')
}
if (!allInserts.some(it => it.id === 'pack-agent-goal-tool')) fail('passthrough plugin goal missing')
if (!allInserts.some(it => it.name === '@deepseek-ai/dsh-hooks-claude-code')) fail('hooks insert missing')
try {
  await access(join(allResult.dir, 'mods', 'demo', 'mod.json'))
  await access(join(allResult.dir, 'mods', 'pack-agent-plan', 'mod.json'))
} catch {
  fail('everything pack must ship skill + plugin as mods/')
}
console.log('✓ everything packable: files + persona + plugins + hooks + preset + mods/')
await rm(allOut, { recursive: true, force: true }).catch(() => {})

try {
  await compilePackToDshBundle(
    { name: 'empty-table', version: '0.0.1', description: 'json only', bundle: { portable: true, files: [] } },
    packTestTmp(`dsh-modpack-empty-${Date.now()}`),
  )
  fail('pack with no mods must be rejected')
} catch (err) {
  const msg = err instanceof Error ? err.message : String(err)
  if (!msg.toLowerCase().includes('mod')) fail(`empty pack error should mention mod, got ${msg}`)
}
console.log('✓ json-only pack without mods is rejected')

console.log('[OK] dsh-modpack compile')
