#!/usr/bin/env bun
/**
 * pinst：导出剥凭据 PA104；导入新 id、端口清空、凭据不还原。
 * Q4：账本（instances/<id>/pack-ledger.json）随实例走 —— 导出带上、导入原样恢复、不重算；
 * 旧 pinst 没带账本 → 导入后保持「未登记」（不生成空账本）。
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
// Q4：账本在实例目录（不是 home）。内容带一条关系，用来断言 pinst 是原样搬运、不是重算。
const ledgerPath = join(root.path, 'instances', inst.id, 'pack-ledger.json')
const ledgerBody = `${JSON.stringify(
  {
    schema: 'pack-agent.pack-ledger/v1',
    updated: '2026-11-04T00:00:00.000Z',
    packs: {
      'Alpha-0.1.0': {
        title: 'Alpha',
        installedVersion: '0.1.0',
        path: 'library/packs/Alpha-0.1.0.pack.zip',
        installedAt: '2026-11-04T00:00:00.000Z',
        content: { dir: '.agent-pack/modpacks/pack-agent-modpack-alpha', allow: true },
        profiles: {
          web: {
            state: 'installed',
            plugins: [{ spec: 'dsh-context', addedVersion: '0.80.0', origin: 'pack' }],
          },
        },
      },
    },
    owners: { web: { 'dsh-context': ['Alpha-0.1.0'] } },
  },
  null,
  2,
)}\n`
writeFileSync(ledgerPath, ledgerBody)

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
// Q4：账本随实例导出（zip 根 pack-ledger.json），内容原样。
if (!existsSync(join(stage, 'pack-ledger.json'))) fail('pinst zip missing pack-ledger.json')
if (readFileSync(join(stage, 'pack-ledger.json'), 'utf8') !== ledgerBody) {
  fail('pinst zip pack-ledger.json 被改写了')
}
console.log('✓ export strips credentials, no logs, ships pack-ledger.json as-is')

const imported = await importPack(root, zip, { name: 'beta', publishedVersions: ['0.1.0-rc.6'] })
if (imported.id === inst.id) fail('import reused id')
if (imported.profile.port !== 'auto') fail(`port ${imported.profile.port}`)
if (imported.dsh.version !== '0.1.0-rc.6') fail(`version ${imported.dsh.version}`)
const newHome = join(root.path, 'instances', imported.id, 'home')
if (existsSync(join(newHome, '.credentials.yaml'))) fail('credentials restored')
if (!existsSync(join(newHome, 'sessions', 'keep-me.txt'))) fail('session file missing')
if (getInstance(root, imported.id).status !== 'ready') fail('status')
// Q4：账本原样恢复到 instances/<newId>/pack-ledger.json（不是 home 里），不重算、不认领。
const restoredLedger = join(root.path, 'instances', imported.id, 'pack-ledger.json')
if (!existsSync(restoredLedger)) fail('imported instance missing restored pack-ledger.json')
if (readFileSync(restoredLedger, 'utf8') !== ledgerBody) fail('imported pack-ledger.json 被重算了')
if (existsSync(join(newHome, 'pack-ledger.json'))) fail('pack-ledger.json must live in the instance dir, not home')
console.log('✓ import new id, port auto, no credentials, restores pack-ledger.json verbatim')

// 旧 pinst（P2 之前导出，没带账本）：导入照常成功，但保持「未登记」——
// 不生成空账本文件（PackLedger.Load 对缺失文件返回空账本 + Exists=false）。
const legacyInst = createInstance(root, { name: 'gamma', version: '0.1.0-rc.6', profile: 'web', port: 31922 })
writeFileSync(join(root.path, 'instances', legacyInst.id, 'home', 'sessions', 'old.txt'), 'old')
const legacyZip = join(tmp, 'gamma-legacy.pinst.zip')
await exportInstance(root, legacyInst.id, { out: legacyZip })
if (existsSync(join(root.path, 'instances', legacyInst.id, 'pack-ledger.json'))) fail('gamma should not have a ledger')
const importedLegacy = await importPack(root, legacyZip, { name: 'delta', publishedVersions: ['0.1.0-rc.6'] })
if (importedLegacy.id === legacyInst.id) fail('legacy pinst reuse id')
if (existsSync(join(root.path, 'instances', importedLegacy.id, 'pack-ledger.json'))) {
  fail('旧 pinst 导入不该生成空账本（保持「未登记」语义）')
}
if (!existsSync(join(root.path, 'instances', importedLegacy.id, 'home', 'sessions', 'old.txt'))) {
  fail('legacy pinst home not restored')
}
console.log('✓ legacy pinst without ledger imports fine, keeps 未登记 (no empty ledger file)')

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
