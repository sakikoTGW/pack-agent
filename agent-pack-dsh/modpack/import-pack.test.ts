#!/usr/bin/env bun
/**
 * import：无 dsh.version 不建实例；成功可 run；必选 add 失败留下 import-failed 并英文提醒。
 */
import { mkdirSync, writeFileSync, existsSync, readFileSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { ensureRoot, seedFakeVersion, getInstance, listInstances, renderDiag, LauncherError } from './launcher.js'
import { importPack } from './import-pack.js'

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
