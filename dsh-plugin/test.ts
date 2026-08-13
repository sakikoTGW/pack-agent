#!/usr/bin/env bun
/**
 * pack-agent 长出的 DSH 插件：cordis apply 注册工具与斜杠命令。
 */
import { access, readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { packTestTmp } from '../test/tmp-root.js'
import { apply, inject, name as pluginName } from './src/index.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

if (pluginName !== 'pack-agent') fail(`plugin name ${pluginName}`)
if (!inject.includes('tools')) fail(`inject must include tools: ${inject.join(',')}`)
if (!inject.includes('skills')) fail(`inject must include skills: ${inject.join(',')}`)
console.log('✓ cordis plugin name/inject')

type ToolDef = { name: string; description?: string; execute: (args: Record<string, unknown>) => Promise<unknown> }
type CmdDef = { name: string; description?: string; handler: (inv: { rawInput: string }) => Promise<unknown> | unknown }

const tools: ToolDef[] = []
const commands: CmdDef[] = []
const providers: Array<{ name: string; list: () => Promise<unknown>; get: (c: unknown) => Promise<unknown> }> = []
const ctx = {
  tools: { register(def: ToolDef) { tools.push(def) } },
  commands: { register(def: CmdDef) { commands.push(def) } },
  skills: {
    registerProvider(create: (control: { signal: AbortSignal; invalidate: () => void }) => (typeof providers)[0]) {
      providers.push(create({ signal: new AbortController().signal, invalidate() {} }))
      return () => {}
    },
  },
}

const cwd = packTestTmp(`dsh-plugin-${Date.now()}`)
apply(ctx, { cwd })

const toolNames = tools.map(t => t.name).sort()
for (const n of ['packagent_detect', 'packagent_compile', 'packagent_project', 'packagent_map', 'packagent_search', 'packagent_allow', 'packagent_deny', 'packagent_list']) {
  if (!toolNames.includes(n)) fail(`missing tool ${n}; have ${toolNames.join(',')}`)
}
const cmdNames = commands.map(c => c.name).sort()
for (const n of ['packagent-detect', 'packagent-compile', 'packagent-project', 'packagent-map', 'packagent-search', 'packagent-allow']) {
  if (!cmdNames.includes(n)) fail(`missing command /${n}; have ${cmdNames.join(',')}`)
}
console.log('✓ tools + slash commands registered')

const detect = tools.find(t => t.name === 'packagent_detect')!
const detected = await detect.execute({}) as { ok?: boolean; detected?: string[] }
if (!detected?.ok || !Array.isArray(detected.detected)) fail(`detect result ${JSON.stringify(detected)}`)
console.log('✓ packagent_detect executes')

const fixture = join(import.meta.dir, '..', 'test', 'fixtures', 'demo.pack.json')
const outDir = join(cwd, 'compiled-demo')
const compile = tools.find(t => t.name === 'packagent_compile')!
const compiled = await compile.execute({ pack: fixture, out: outDir }) as { ok?: boolean; dir?: string; npmName?: string }
if (!compiled?.ok || !compiled.dir) fail(`compile result ${JSON.stringify(compiled)}`)
try {
  await access(join(compiled.dir, 'package.json'))
} catch {
  fail(`compiled bundle missing package.json at ${compiled.dir}`)
}
const pkg = JSON.parse(await readFile(join(compiled.dir, 'package.json'), 'utf8')) as { dsh?: { bundle?: { patch?: string } } }
if (!pkg.dsh?.bundle?.patch) fail('compiled output is not a dsh.bundle')
console.log('✓ packagent_compile produces dsh.bundle')

const project = tools.find(t => t.name === 'packagent_project')!
const projected = await project.execute({ pack: fixture, cwd }) as { ok?: boolean; dir?: string; id?: string }
if (!projected?.ok || !projected.dir) fail(`project result ${JSON.stringify(projected)}`)
if (!projected.dir.replace(/\\/g, '/').includes('.agent-pack/modpacks/')) {
  fail(`project must write .agent-pack/modpacks, got ${projected.dir}`)
}
try {
  await access(join(projected.dir, 'package.json'))
} catch {
  fail(`projected pack missing package.json at ${projected.dir}`)
}
console.log('✓ packagent_project writes modpacks dir')

if (!providers.length) fail('must register catalog SkillProvider')
const allow = tools.find(t => t.name === 'packagent_allow')!
const allowed = await allow.execute({ id: projected.id, cwd }) as { ok?: boolean }
if (!allowed?.ok) fail(`allow ${JSON.stringify(allowed)}`)
const listedSkills = await providers[0].list() as Array<{ name?: string }>
if (!listedSkills.some(s => s.name === 'demo')) {
  fail(`provider list after allow missing demo: ${JSON.stringify(listedSkills)}`)
}
console.log('✓ SkillProvider list is the allow-list')

const cmd = commands.find(c => c.name === 'packagent-detect')!
const cmdOut = await cmd.handler({ rawInput: '' }) as { kind?: string; text?: string }
if (cmdOut?.kind !== 'success') fail(`slash detect ${JSON.stringify(cmdOut)}`)
console.log('✓ /packagent-detect')

const pluginPkg = JSON.parse(await readFile(join(import.meta.dir, 'package.json'), 'utf8')) as {
  name?: string
  dsh?: { bundle?: { patch?: string } }
}
if (pluginPkg.name !== '@sakikotgw/pack-agent-dsh') fail(`plugin package name ${pluginPkg.name}`)
if (!pluginPkg.dsh?.bundle?.patch) fail('plugin package.json missing dsh.bundle.patch')
console.log('✓ plugin is a dsh.bundle')

const skillMd = await readFile(join(import.meta.dir, 'skills', 'pack-agent-dsh', 'SKILL.md'), 'utf8')
if (skillMd.includes('dsh plugin --profile web add "<compiled-dir>"')) {
  fail('SKILL.md must not tell the model to plugin-add a compiled pack')
}
if (!skillMd.includes('packagent_map') || !skillMd.includes('packagent_allow')) {
  fail('SKILL.md must document map/allow tools')
}
console.log('✓ plugin skill documents projection, not plugin-add-per-pack')

console.log('[OK] dsh-plugin')
