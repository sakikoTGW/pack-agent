#!/usr/bin/env bun
/**
 * pack-agent 长出的 DSH 插件：cordis apply 注册工具与斜杠命令。
 */
import { access, readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { apply, inject, name as pluginName } from './src/index.js'
import {
  GATEWAY_FILE,
  GATEWAY_SCHEMA,
  inject as gatewayInject,
  name as gatewayName,
} from '../gateway/src/index.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

if (pluginName !== 'pack-agent') fail(`plugin name ${pluginName}`)
if (!inject.includes('tools')) fail(`inject must include tools: ${inject.join(',')}`)
if (!inject.includes('skills')) fail(`inject must include skills: ${inject.join(',')}`)
if (!inject.includes('commands')) {
  fail(`inject must include commands; Cordis throws "cannot get property commands without inject": ${inject.join(',')}`)
}
if (inject.includes('apiProxy')) {
  fail('apiProxy must NOT be on the main plugin: cordis inject waits, which would stall tools/skills on profiles without the gateway')
}
console.log('✓ cordis plugin name/inject')

if (gatewayName !== 'pack-agent-pad-gateway') fail(`gateway name ${gatewayName}`)
if (!gatewayInject.includes('apiProxy')) fail(`gateway must inject apiProxy: ${gatewayInject.join(',')}`)
if (GATEWAY_FILE !== 'pad-gateway.json') fail(`gateway advert file ${GATEWAY_FILE}`)
if (GATEWAY_SCHEMA !== 'pack-agent.pad-gateway/v1') fail(`gateway schema ${GATEWAY_SCHEMA}`)
console.log('✓ pad gateway is a separate plugin injecting apiProxy')

type ToolDef = {
  name: string
  description?: string
  execute: (args: Record<string, unknown>) => Promise<unknown>
  output?: { schema?: unknown; render?: unknown }
}
type CmdDef = { name: string; description?: string; handler: (inv: { rawInput: string }) => Promise<unknown> | unknown }

const tools: ToolDef[] = []
const commands: CmdDef[] = []
const providers: Array<{ name: string; list: () => Promise<unknown>; get: (c: unknown) => Promise<unknown> }> = []
const ctx = {
  tools: {
    register(def: ToolDef) {
      // DeepSeek Harness `@deepseek-ai/dsh-tools` ToolRuntime.register (0.1.0-rc.7)
      const output = def.output as { render?: unknown } | undefined
      if (output === undefined || typeof output !== 'object' || typeof output.render !== 'function') {
        fail(`tool "${def.name}" must declare output { schema, render, presentationMeta? }`)
      }
      tools.push(def)
    },
  },
  commands: { register(def: CmdDef) { commands.push(def) } },
  skills: {
    registerProvider(create: (control: { signal: AbortSignal; invalidate: () => void }) => (typeof providers)[0]) {
      providers.push(create({ signal: new AbortController().signal, invalidate() {} }))
      return () => {}
    },
  },
}

const cwd = packTestTmp(`dsh-plugin-${Date.now()}`)
const injectSet = new Set(inject)
const cordisCtx = new Proxy(ctx, {
  get(target, prop, receiver) {
    if (typeof prop === 'string' && prop in target && !injectSet.has(prop)) {
      throw new Error(`cannot get property "${prop}" without inject`)
    }
    return Reflect.get(target, prop, receiver)
  },
})
apply(cordisCtx, { cwd })

const toolNames = tools.map(t => t.name).sort()
for (const n of ['packagent_detect', 'packagent_compile', 'packagent_project', 'packagent_map', 'packagent_search', 'packagent_allow', 'packagent_deny', 'packagent_list', 'packagent_set_save', 'packagent_set_load', 'packagent_set_list']) {
  if (!toolNames.includes(n)) fail(`missing tool ${n}; have ${toolNames.join(',')}`)
}
const cmdNames = commands.map(c => c.name).sort()
for (const n of ['packagent-detect', 'packagent-compile', 'packagent-project', 'packagent-map', 'packagent-search', 'packagent-allow', 'packagent-set-save', 'packagent-set-load', 'packagent-set-list']) {
  if (!cmdNames.includes(n)) fail(`missing command /${n}; have ${cmdNames.join(',')}`)
}
console.log('✓ tools + slash commands registered')

const detect = tools.find(t => t.name === 'packagent_detect')!
const detected = await detect.execute({}) as { ok?: boolean; detected?: string[] }
if (!detected?.ok || !Array.isArray(detected.detected)) fail(`detect result ${JSON.stringify(detected)}`)
console.log('✓ packagent_detect executes')

const fixture = join(import.meta.dir, '../..', 'test', 'fixtures', 'demo.pack.json')
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

const setSave = tools.find(t => t.name === 'packagent_set_save')!
const saved = await setSave.execute({ name: 'demo-only', cwd }) as { ok?: boolean; name?: string }
if (!saved?.ok || saved.name !== 'demo-only') fail(`set-save ${JSON.stringify(saved)}`)
const setList = tools.find(t => t.name === 'packagent_set_list')!
const listedSets = await setList.execute({ cwd }) as { sets?: Array<{ name?: string; pack_ids?: string[] }> }
if (!listedSets.sets?.some(s => s.name === 'demo-only' && s.pack_ids?.includes(String(projected.id)))) {
  fail(`set-list after save ${JSON.stringify(listedSets)}`)
}
console.log('✓ packagent_set_save/list writes workspace presets')

const cmd = commands.find(c => c.name === 'packagent-detect')!
const cmdOut = await cmd.handler({ rawInput: '' }) as { kind?: string; text?: string }
if (cmdOut?.kind !== 'success') fail(`slash detect ${JSON.stringify(cmdOut)}`)
console.log('✓ /packagent-detect')

const pluginPkg = JSON.parse(await readFile(join(import.meta.dir, 'package.json'), 'utf8')) as {
  name?: string
  dsh?: { bundle?: { patch?: string } }
  exports?: Record<string, unknown>
}
if (pluginPkg.name !== '@sakikotgw/pack-agent-dsh') fail(`plugin package name ${pluginPkg.name}`)
if (!pluginPkg.dsh?.bundle?.patch) fail('plugin package.json missing dsh.bundle.patch')
const engines = (pluginPkg as { engines?: { dsh?: string } }).engines
if (!engines?.dsh || !engines.dsh.includes('0.1.0-rc.7')) fail("plugin engines.dsh " + String(engines && engines.dsh))
console.log('✓ plugin is a dsh.bundle')

const skillMd = await readFile(join(import.meta.dir, 'skills', 'pack-agent-dsh', 'SKILL.md'), 'utf8')
if (!skillMd.includes('@sakikotgw/pack-agent-dsh')) {
  fail('SKILL.md must tell the host to add @sakikotgw/pack-agent-dsh')
}
if (skillMd.includes('dsh plugin --profile web add "<compiled-dir>"')) {
  fail('SKILL.md must not tell the model to plugin-add a compiled pack')
}
if (!skillMd.includes('packagent_map') || !skillMd.includes('packagent_allow') || !skillMd.includes('packagent_set_save')) {
  fail('SKILL.md must document map/allow/set-save tools')
}
console.log('✓ plugin skill documents projection, not plugin-add-per-pack')

// The manager's own patch must NOT insert apiproxy: a profile that installed only
// the manager would fail to compose a package it never installed.
const patch = await readFile(join(import.meta.dir, 'cordis.patch.yml'), 'utf8')
if (patch.includes('apiProxy') || patch.includes('host-apiproxy')) {
  fail('the manager patch must not touch apiproxy; that belongs to @sakikotgw/pad-gateway')
}

// The gateway package carries apiproxy as a dependency and inserts it, so adding
// one package is all a terminal profile needs.
const gwDir = join(import.meta.dir, '..', 'gateway')
const gwPatch = await readFile(join(gwDir, 'cordis.patch.yml'), 'utf8')
for (const row of ['@deepseek-ai/dsh-host-apiproxy', '@deepseek-ai/dsh-host-directory-picker-native', '@sakikotgw/pad-gateway']) {
  if (!gwPatch.includes(row)) fail(`gateway patch must insert ${row}`)
}
// -auto injects ['webServer','loader']. A terminal profile has no webServer, so the
// picker never starts, apiproxy never starts, and the profile hangs with no error.
if (gwPatch.includes('directory-picker-auto')) {
  fail('gateway must use the native directory picker; -auto needs webServer and hangs terminal profiles')
}
if (!/pad-gateway'[\s\S]{0,80}inject: \[apiProxy\]/.test(gwPatch)) {
  fail('the pad-gateway row must inject apiProxy')
}
const gwPkg = JSON.parse(await readFile(join(gwDir, 'package.json'), 'utf8')) as {
  name?: string
  dsh?: { bundle?: { patch?: string } }
  dependencies?: Record<string, string>
}
if (gwPkg.name !== '@sakikotgw/pad-gateway') fail(`gateway package name ${gwPkg.name}`)
if (!gwPkg.dsh?.bundle?.patch) fail('gateway package must be a dsh.bundle')
for (const dep of ['@deepseek-ai/dsh-host-apiproxy', '@deepseek-ai/dsh-host-directory-picker-native']) {
  if (!gwPkg.dependencies?.[dep]) fail(`gateway package must depend on ${dep}`)
}

// No DSH_HOME means no instance to advertise into; that must be a quiet no-op
// rather than a crash that takes the profile down.
const gatewayMod = await import('../gateway/src/index.js')
const prevHome = process.env.DSH_HOME
delete process.env.DSH_HOME
const warnings: string[] = []
gatewayMod.apply(
  { apiProxy: {}, logger: { warn: (m: string) => warnings.push(m) } },
  {},
)
if (!warnings.some((w) => w.includes('DSH_HOME'))) {
  fail(`gateway without DSH_HOME must warn and return, got ${JSON.stringify(warnings)}`)
}
if (prevHome === undefined) delete process.env.DSH_HOME
else process.env.DSH_HOME = prevHome
console.log('✓ pad gateway row wired, and no-op without DSH_HOME')

console.log('[OK] dsh-plugin')
