#!/usr/bin/env bun
/**
 * import：无 dsh.version 不建实例；成功可 run；必选 add 失败留下 import-failed 并英文提醒。
 * 老 ccui-pack（只有 dsh.plugins[].spec）也要能建实例：组合包逐个 `dsh plugin add`，
 * 投影里记 plugin mod（供检索），instance.json 记 source.packId（目录行「装到哪」）。
 */
import { mkdirSync, writeFileSync, existsSync, readFileSync, readdirSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { writePackZip } from '../../src/pack-archive.js'
import { packTestTmp } from '../../test/tmp-root.js'
import { ensureRoot, seedFakeVersion, getInstance, listInstances, renderDiag, LauncherError } from './launcher.js'
import { importPack } from './import-pack.js'
import { packIdFromPath } from './pack-catalog.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const fakeJs = `const fs = require('fs')
const path = require('path')
const home = process.env.DSH_HOME
const args = process.argv.slice(2)
if (args.includes('--version') || args.includes('-V')) {
  process.stdout.write('@@VERSION@@\\n')
  process.exit(0)
}
if (args.includes('--dump-config')) {
  const added = path.join(home, 'pa-added.json')
  const list = fs.existsSync(added) ? JSON.parse(fs.readFileSync(added, 'utf8')) : []
  if (list.some((s) => String(s).includes('pack-agent'))) {
    process.stdout.write('- id: pack-agent\\n')
  }
  process.stdout.write('dsh.profile.bundles:\\n')
  for (const s of list) process.stdout.write('  - ' + s + '\\n')
  process.exit(0)
}
if (args[0] === 'plugin' && args.includes('add')) {
  const spec = args[args.length - 1]
  if (String(spec).includes('fail-required')) process.exit(1)
  const added = path.join(home, 'pa-added.json')
  const list = fs.existsSync(added) ? JSON.parse(fs.readFileSync(added, 'utf8')) : []
  list.push(spec)
  fs.writeFileSync(added, JSON.stringify(list))
  process.exit(0)
}
let port = '3080'
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--port' && args[i + 1]) port = String(args[i + 1])
}
process.stdout.write('dsh web: http://127.0.0.1:' + port + '\\n')
setInterval(() => {}, 60000)
`

function writePack(dir: string, body: unknown): string {
  mkdirSync(dir, { recursive: true })
  const p = join(dir, 'pack.json')
  writeFileSync(p, JSON.stringify(body, null, 2))
  return p
}

const tmp = packTestTmp(`import-pack-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)

const noVer = writePack(join(tmp, 'no-ver'), {
  schema: 'ccui-pack/v0.2',
  name: 'no-ver',
  version: '0.1.0',
  dsh: { profile: 'web' },
})
try {
  await importPack(root, noVer, { publishedVersions: ['0.1.0-rc.6'] })
  fail('missing dsh.version must PA012')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA012') fail(`expected PA012, got ${e}`)
}
if (listInstances(root).length) fail('PA012 must not create an instance')
console.log('✓ PA012 no instance')

const okDir = join(tmp, 'ok-pack')
mkdirSync(join(okDir, 'overrides'), { recursive: true })
writeFileSync(join(okDir, 'overrides', 'AGENTS.md'), '# from pack\n')
const okPack = writePack(okDir, {
  schema: 'ccui-pack/v0.2',
  name: 'ok-pack',
  version: '0.1.0',
  knowledge: { skills: [{ name: 'ok-skill', source: 'bundled' }] },
  bundle: {
    portable: true,
    files: [{
      path: 'skills/ok-skill/SKILL.md',
      content: '---\nname: ok-skill\ndescription: import smoke\n---\n# ok\n',
    }],
  },
  dsh: {
    version: '0.1.0-rc.6',
    profile: 'web',
    plugins: [{ spec: '@fake/ok@1.0.0', required: true }],
    overrides: [{ from: 'overrides/AGENTS.md', to: 'AGENTS.md' }],
  },
})
const imported = await importPack(root, okPack, { publishedVersions: ['0.1.0-rc.6'] })
if (imported.status !== 'ready') fail(`status ${imported.status}`)
if (imported.profile.name !== 'web') fail(`profile ${imported.profile.name}`)
const agents = join(root.path, 'instances', imported.id, 'workspace', 'AGENTS.md')
if (!existsSync(agents) || !readFileSync(agents, 'utf8').includes('from pack')) fail('override not copied')
const added = JSON.parse(readFileSync(join(root.path, 'instances', imported.id, 'home', 'pa-added.json'), 'utf8')) as string[]
if (!added.some((s) => s.includes('pack-agent'))) fail(`manager not added: ${added}`)
if (!added.includes('@fake/ok@1.0.0')) fail(`plugin not added: ${added}`)
const mods = join(root.path, 'instances', imported.id, 'workspace', '.agent-pack', 'modpacks')
if (!existsSync(mods)) fail('projection missing')
console.log('✓ import success')

// ---- 老 ccui-pack：只有 dsh.plugins[].spec（没有 bundle / 技能 / 知识锚点）----
// 用户报的整合包就是这个形状：不能报 "has no mods"，组合包要真 add，投影要记 plugin mod。
const legacyZip = join(tmp, 'legacy-specs.pack.zip')
const legacySpecs = [
  'dsh-find-skill',
  '@legacy-scope/dsh-sidebar@^1.2.0',
  '@other-scope/dsh-sidebar',
  'dsh-cost-meter@latest',
]
await writePackZip(
  {
    schema: 'ccui-pack/v0.2',
    name: 'Legacy Specs',
    version: '0.2.0',
    description: 'legacy pack with dsh.plugins specs only',
    bundle: { portable: true, files: [] },
    dsh: {
      version: '0.1.0-rc.6',
      profile: 'web',
      plugins: legacySpecs.map((spec) => ({ spec, required: true })),
    },
  },
  legacyZip,
)
const legacy = await importPack(root, legacyZip, { publishedVersions: ['0.1.0-rc.6'] })
if (legacy.status !== 'ready') fail(`legacy pack status ${legacy.status}`)
if (legacy.name !== 'Legacy Specs') fail(`legacy name ${legacy.name}`)
const legacyAdded = JSON.parse(
  readFileSync(join(root.path, 'instances', legacy.id, 'home', 'pa-added.json'), 'utf8'),
) as string[]
for (const spec of legacySpecs) {
  if (!legacyAdded.includes(spec)) fail(`legacy 组合包没被 dsh plugin add：${spec}（${legacyAdded}）`)
}
console.log('✓ legacy pack: dsh.plugins[] specs are installed through plugin add')

if (packIdFromPath(legacyZip) !== 'legacy-specs') {
  fail(`packIdFromPath ${packIdFromPath(legacyZip)} != legacy-specs`)
}
const legacyInstJson = JSON.parse(
  readFileSync(join(root.path, 'instances', legacy.id, 'instance.json'), 'utf8'),
) as { source?: { kind?: string; packId?: string; packTitle?: string; packVersion?: string; packPath?: string } }
const legacySource = legacyInstJson.source
if (!legacySource) fail('legacy instance.json missing source')
if (legacySource.kind !== 'pack') fail(`source.kind ${legacySource.kind}`)
if (legacySource.packId !== 'legacy-specs') fail(`source.packId ${legacySource.packId}`)
if (legacySource.packTitle !== 'Legacy Specs') fail(`source.packTitle ${legacySource.packTitle}`)
if (legacySource.packVersion !== '0.2.0') fail(`source.packVersion ${legacySource.packVersion}`)
if (!String(legacySource.packPath || '').toLowerCase().endsWith('legacy-specs.pack.zip')) {
  fail(`source.packPath ${legacySource.packPath}`)
}
// 目录行 id 与实例来源同一个 helper：pack-catalog 的 installed 才对得上。
if (packIdFromPath(String(legacySource.packPath)) !== legacySource.packId) {
  fail(`source.packId ${legacySource.packId} != catalog id ${packIdFromPath(String(legacySource.packPath))}`)
}
console.log('✓ legacy pack: instance.json source.packId matches pack-catalog row id')

const legacyProjection = join(
  root.path, 'instances', legacy.id, 'workspace', '.agent-pack', 'modpacks', 'pack-agent-modpack-legacy-specs',
)
const legacyModDirs = readdirSync(join(legacyProjection, 'mods')).sort()
const wantModDirs = [
  'plugin-dsh-cost-meter',
  'plugin-dsh-find-skill',
  'plugin-dsh-sidebar',
  'plugin-dsh-sidebar-2',
]
if (JSON.stringify(legacyModDirs) !== JSON.stringify(wantModDirs)) {
  fail(`legacy 投影 mods 目录 ${JSON.stringify(legacyModDirs)} want ${JSON.stringify(wantModDirs)}`)
}
const legacyModsById = new Map<string, { id?: string; kind?: string; name?: string; spec?: string }>()
for (const dir of legacyModDirs) {
  const modJson = join(legacyProjection, 'mods', dir, 'mod.json')
  const pluginJson = join(legacyProjection, 'mods', dir, 'plugin.json')
  if (!existsSync(modJson) || !existsSync(pluginJson)) fail(`legacy mod 缺 mod.json/plugin.json: ${dir}`)
  const mod = JSON.parse(readFileSync(modJson, 'utf8')) as { id?: string; kind?: string; name?: string; spec?: string }
  if (mod.id !== dir) fail(`mod.id ${mod.id} != dir ${dir}`)
  if (mod.kind !== 'plugin') fail(`mod.kind ${mod.kind}`)
  legacyModsById.set(dir, mod)
}
const scoped = legacyModsById.get('plugin-dsh-sidebar')
if (!scoped) fail('scoped spec mod missing')
if (scoped.name !== '@legacy-scope/dsh-sidebar') fail(`scope 展示名 ${scoped.name}`)
if (scoped.spec !== '@legacy-scope/dsh-sidebar@^1.2.0') fail(`mod.spec ${scoped.spec}`)
const scopedPayload = JSON.parse(
  readFileSync(join(legacyProjection, 'mods', 'plugin-dsh-sidebar', 'plugin.json'), 'utf8'),
) as { selector?: string; required?: boolean }
if (scopedPayload.selector !== '^1.2.0') fail(`plugin.json selector ${scopedPayload.selector}`)
if (scopedPayload.required !== true) fail('plugin.json required must stay true')
const dup = legacyModsById.get('plugin-dsh-sidebar-2')
if (dup?.name !== '@other-scope/dsh-sidebar') fail(`重名 spec 的展示名 ${dup?.name}`)
console.log('✓ legacy pack: projection records spec-only 组合包 as plugin mods (unique ids)')

const legacyCatalog = JSON.parse(readFileSync(join(legacyProjection, 'catalog.json'), 'utf8')) as {
  name?: string
  mods?: Array<{ kind?: string }>
}
if (legacyCatalog.name !== 'Legacy Specs') fail(`legacy catalog.name ${legacyCatalog.name}`)
if ((legacyCatalog.mods ?? []).filter((m) => m.kind === 'plugin').length !== legacySpecs.length) {
  fail(`legacy catalog.mods plugin count: ${JSON.stringify(legacyCatalog.mods)}`)
}
const legacyPatch = readFileSync(join(legacyProjection, 'cordis.patch.yml'), 'utf8')
if (legacyPatch.includes('insert:')) {
  fail(`spec-only 组合包不许生成 cordis.patch insert:\n${legacyPatch}`)
}
console.log('✓ legacy pack: no duplicate cordis.patch insert for spec-only plugins')

// ---- Q6：包内本地插件先复制到实例目录，再以 file: 装 -------------------------
// 原来直接 file:<import 临时解压目录>，import 一结束临时目录就删；现在要落在
// <instance>/home/local-plugins/<packId>/<name>/，路径稳定、可归属、卸包可回滚。
const localZip = join(tmp, 'local-plugin.pack.zip')
await writePackZip(
  {
    schema: 'ccui-pack/v0.2',
    name: 'Local Plugin Pack',
    version: '0.3.0',
    description: 'pack with an in-pack local plugin',
    knowledge: { skills: [{ name: 'local-skill', source: 'bundled' }] },
    bundle: {
      portable: true,
      files: [
        {
          path: 'skills/local-skill/SKILL.md',
          content: '---\nname: local-skill\ndescription: local plugin smoke\n---\n# local\n',
        },
        {
          path: 'plugins/my-local-plugin/package.json',
          content: `${JSON.stringify({ name: 'my-local-plugin', version: '0.3.0', main: 'index.js' }, null, 2)}\n`,
        },
        { path: 'plugins/my-local-plugin/index.js', content: 'export default 1\n' },
      ],
    },
    dsh: {
      version: '0.1.0-rc.6',
      profile: 'web',
      plugins: [{ name: 'my-local-plugin', local: 'plugins/my-local-plugin', required: true }],
    },
  },
  localZip,
)
const localPack = await importPack(root, localZip, { publishedVersions: ['0.1.0-rc.6'] })
if (localPack.status !== 'ready') fail(`local pack status ${localPack.status}`)
const localDest = join(
  root.path, 'instances', localPack.id, 'home', 'local-plugins', packIdFromPath(localZip), 'my-local-plugin',
)
if (!existsSync(join(localDest, 'package.json'))) fail(`local plugin not copied to instance: ${localDest}`)
if (!existsSync(join(localDest, 'index.js'))) fail(`local plugin files not copied: ${localDest}`)
const localPkg = JSON.parse(readFileSync(join(localDest, 'package.json'), 'utf8')) as { name?: string; version?: string }
if (localPkg.name !== 'my-local-plugin' || localPkg.version !== '0.3.0') {
  fail(`copied local plugin manifest ${JSON.stringify(localPkg)}`)
}
const localAdded = JSON.parse(
  readFileSync(join(root.path, 'instances', localPack.id, 'home', 'pa-added.json'), 'utf8'),
) as string[]
if (!localAdded.includes(`file:${localDest}`)) {
  fail(`local plugin must be installed via file:<instance copy>, got ${JSON.stringify(localAdded)}`)
}
// 不允许再把 file: 指回 import 的临时解压目录（那个目录在 import 结束时会被删掉）。
if (localAdded.some((s) => s.startsWith('file:') && s !== `file:${localDest}`)) {
  fail(`unexpected file: spec ${JSON.stringify(localAdded)}`)
}
const localDefaults = join(
  root.path, 'instances', localPack.id, 'workspace', '.agent-pack', 'modpacks',
  `pack-agent-modpack-${'Local Plugin Pack'.toLowerCase().replace(/[^a-z0-9]+/g, '-')}`,
)
if (!existsSync(localDefaults)) fail(`local pack projection missing: ${localDefaults}`)
console.log('✓ Q6: in-pack local plugin copied under <instance>/home/local-plugins/<packId>/<name> and added via file:')

const badDir = join(tmp, 'bad-pack')
const badPack = writePack(badDir, {
  schema: 'ccui-pack/v0.2',
  name: 'bad-pack',
  version: '0.1.0',
  knowledge: { skills: [{ name: 'bad-skill', source: 'bundled' }] },
  bundle: {
    portable: true,
    files: [{
      path: 'skills/bad-skill/SKILL.md',
      content: '---\nname: bad-skill\ndescription: fail plugin\n---\n# bad\n',
    }],
  },
  dsh: {
    version: '0.1.0-rc.6',
    profile: 'web',
    plugins: [{ spec: '@fake/fail-required@1.0.0', required: true }],
  },
})
try {
  await importPack(root, badPack, { publishedVersions: ['0.1.0-rc.6'] })
  fail('required plugin fail must PA014')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA014') fail(`expected PA014, got ${e}`)
  const text = renderDiag(e.diagnostic)
  if (!/import failed/i.test(text)) fail(`must say import failed:\n${text}`)
  if (!/still on disk/i.test(text)) fail(`must say still on disk:\n${text}`)
  if (!text.includes('instance remove')) fail(`must say how to delete:\n${text}`)
}
const leftover = getInstance(root, 'bad-pack')
if (leftover.status !== 'import-failed') fail(`status ${leftover.status}`)
if (!existsSync(join(root.path, 'instances', 'bad-pack', 'instance.json'))) fail('instance dir deleted')
console.log('✓ PA014 leftover + remind')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ import-pack')
