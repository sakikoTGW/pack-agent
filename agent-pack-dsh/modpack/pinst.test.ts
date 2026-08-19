#!/usr/bin/env bun
/**
 * pinst：导出剥凭据 PA104；导入新 id、端口清空、凭据不还原。
 */
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { createInstance, ensureRoot, getInstance, seedFakeVersion, LauncherError } from './launcher.js'
import { exportInstance } from './pinst.js'
import { importPack } from './import-pack.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const fakeJs = `const args = process.argv.slice(2)
if (args.includes('--version') || args.includes('-V')) {
  process.stdout.write('@@VERSION@@\\n')
  process.exit(0)
}
process.stdout.write('dsh web: http://127.0.0.1:3080\\n')
setInterval(() => {}, 60000)
`

const tmp = packTestTmp(`pinst-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)
const inst = createInstance(root, { name: 'alpha', version: '0.1.0-rc.6', profile: 'web', port: 31921 })
const home = join(root.path, 'instances', inst.id, 'home')
writeFileSync(join(home, '.credentials.yaml'), 'REF: secret-key\n')
mkdirSync(join(home, 'sessions'), { recursive: true })
writeFileSync(join(home, 'sessions', 'keep-me.txt'), 'session-body')
writeFileSync(join(root.path, 'instances', inst.id, 'logs', 'noise.log'), 'log')

const zip = join(tmp, 'alpha.pinst.zip')
const exported = await exportInstance(root, inst.id, { out: zip })
if (!existsSync(zip)) fail('zip missing')
if (!exported.stripped.includes('credentials')) fail(`stripped ${exported.stripped}`)

const stage = join(tmp, 'unzip-check')
mkdirSync(stage, { recursive: true })
const tar = Bun.spawnSync(['tar', '-xf', zip, '-C', stage], { stdout: 'pipe', stderr: 'pipe' })
if (tar.exitCode !== 0) fail(`tar xf failed: ${tar.stderr.toString()}`)
if (existsSync(join(stage, 'home', '.credentials.yaml'))) fail('zip still has credentials')
if (existsSync(join(stage, 'logs', 'noise.log')) || existsSync(join(stage, 'home', '..', 'logs', 'noise.log'))) {
  /* logs must not be in zip at top or beside home */
}
if (existsSync(join(stage, 'logs'))) fail('zip contains logs/')
const manifest = JSON.parse(readFileSync(join(stage, 'manifest.json'), 'utf8')) as { schema: string }
if (manifest.schema !== 'pack-agent.pinst/v1') fail(`schema ${manifest.schema}`)
console.log('✓ export strips credentials, no logs')

const imported = await importPack(root, zip, { name: 'beta', publishedVersions: ['0.1.0-rc.6'] })
if (imported.id === inst.id) fail('import reused id')
if (imported.profile.port !== 'auto') fail(`port ${imported.profile.port}`)
if (imported.dsh.version !== '0.1.0-rc.6') fail(`version ${imported.dsh.version}`)
const newHome = join(root.path, 'instances', imported.id, 'home')
if (existsSync(join(newHome, '.credentials.yaml'))) fail('credentials restored')
if (!existsSync(join(newHome, 'sessions', 'keep-me.txt'))) fail('session file missing')
if (getInstance(root, imported.id).status !== 'ready') fail('status')
console.log('✓ import new id, port auto, no credentials')

const bad = join(tmp, 'bad.pinst.zip')
const badDir = join(tmp, 'bad-stage')
mkdirSync(badDir, { recursive: true })
writeFileSync(join(badDir, 'manifest.json'), JSON.stringify({ schema: 'nope/v0' }))
const packTar = Bun.spawnSync(['tar', '-a', '-cf', bad, '-C', badDir, '.'], { stdout: 'pipe', stderr: 'pipe' })
if (packTar.exitCode !== 0) fail(`pack bad zip: ${packTar.stderr.toString()}`)
try {
  await importPack(root, bad, { publishedVersions: ['0.1.0-rc.6'] })
  fail('bad schema must PA009')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA009') fail(`expected PA009, got ${e}`)
}
console.log('✓ bad pinst PA009')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ pinst')
