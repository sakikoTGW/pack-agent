#!/usr/bin/env bun
/**
 * P2/P3：元数据 TTL 缓存、命名钥匙、自更新、货架、加权进度、快捷方式、迁移。
 */
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { createInstance, ensureRoot, getInstance, seedFakeVersion } from './launcher.js'
import { getMeta, putMeta, publishedDshVersions, refreshMeta } from './meta-cache.js'
import {
  copyNamedCredentialsToHome,
  listCredentialSets,
  namedCredentialsPath,
  setNamedCredentials,
} from './credentials.js'
import { applyUpdate, checkUpdate } from './update.js'
import { marketInstall, marketList, marketSearch } from './market.js'
import { writeShortcut } from './shortcut.js'
import { INSTALL_VERSION_LEAVES, createProgressJob, jobPercent, reportLeafProgress } from './jobs.js'
import { pnpmStoreDir } from './pnpm-store.js'
import { builtinRegistryDir, loadRegistryStore } from './registry-store.js'
import { invokeLauncherApi, LAUNCHER_API_METHODS } from './launcher-api.js'

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
  process.stdout.write('dsh.profile.bundles:\\n')
  for (const s of list) process.stdout.write('  - ' + s + '\\n')
  process.exit(0)
}
if (args[0] === 'plugin' && args.includes('add')) {
  const spec = args[args.length - 1]
  const added = path.join(home, 'pa-added.json')
  const list = fs.existsSync(added) ? JSON.parse(fs.readFileSync(added, 'utf8')) : []
  list.push(spec)
  fs.writeFileSync(added, JSON.stringify(list))
  process.exit(0)
}
process.exit(0)
`

const tmp = packTestTmp(`p2-p3-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)

let fetchCalls = 0
const fetchDsh = () => {
  fetchCalls += 1
  return { versions: ['0.1.0-rc.3', '0.1.0-rc.6'] }
}

const first = await publishedDshVersions(root, { fetch: fetchDsh, now: 1_000 })
if (first.join(',') !== '0.1.0-rc.3,0.1.0-rc.6') fail(`published ${first}`)
if (fetchCalls !== 1) fail(`first fetch ${fetchCalls}`)
const cached = await publishedDshVersions(root, {
  fetch: () => {
    throw new Error('network should not run on fresh cache')
  },
  now: 1_000 + 10,
})
if (cached.join(',') !== first.join(',')) fail('fresh cache miss')
const stale = await publishedDshVersions(root, {
  fetch: () => ({ versions: ['9.9.9'] }),
  now: 1_000 + 3_600_000 + 1,
  wait: false,
})
if (stale.join(',') !== first.join(',')) fail(`stale should return old ${stale}`)
await refreshMeta(root, 'dsh-versions', { fetch: () => ({ versions: ['9.9.9'] }), now: 1_000 + 3_600_000 + 2 })
const after = getMeta<{ versions: string[] }>(root, 'dsh-versions')
if (!after?.body.versions.includes('9.9.9')) fail(`refresh ${JSON.stringify(after)}`)
putMeta(root, 'plugins', { plugins: [] }, 1_000)
console.log('✓ meta cache TTL')

setNamedCredentials(root, 'work', 'DEEPSEEK_API_KEY: "REF: work-secret"\n')
if (!existsSync(namedCredentialsPath(root, 'work'))) fail('named file missing')
const names = listCredentialSets(root)
if (!names.includes('work')) fail(`sets ${names}`)
const named = createInstance(root, {
  name: 'named-box',
  version: '0.1.0-rc.6',
  credentialsKind: 'named',
  credentialsSet: 'work',
})
const homeCred = join(named.home, '.credentials.yaml')
if (!existsSync(homeCred)) fail('named key not copied')
if (!readFileSync(homeCred, 'utf8').includes('work-secret')) fail('named body')
if (JSON.stringify(getInstance(root, named.id)).includes('work-secret')) fail('secret in instance.json')
if (named.credentials?.set !== 'work') fail(`set field ${JSON.stringify(named.credentials)}`)
copyNamedCredentialsToHome(root, join(tmp, 'other-home'), 'work')
if (!readFileSync(join(tmp, 'other-home', '.credentials.yaml'), 'utf8').includes('work-secret')) fail('copyNamed')
console.log('✓ named credentials')

const chk = checkUpdate(root, { current: '0.4.2', published: ['0.4.2', '0.5.0', '0.6.0-dev.1'], channel: 'stable' })
if (!chk.updateAvailable || chk.latest !== '0.5.0') fail(`stable ${JSON.stringify(chk)}`)
const dev = checkUpdate(root, { current: '0.4.2', published: ['0.4.2', '0.5.0', '0.6.0-dev.1'], channel: 'dev' })
if (dev.latest !== '0.6.0-dev.1') fail(`dev ${JSON.stringify(dev)}`)
const src = join(tmp, 'pkg-0.5.0')
mkdirSync(src, { recursive: true })
writeFileSync(join(src, 'package.json'), JSON.stringify({ name: '@sakikotgw/pack-agent', version: '0.5.0' }))
const applied = applyUpdate(root, { version: '0.5.0', sourceDir: src })
if (!existsSync(join(applied.dir, 'package.json'))) fail('update dir')
if (JSON.parse(readFileSync(join(root.path, 'library', 'updates', 'current.json'), 'utf8')).version !== '0.5.0') {
  fail('current.json')
}
console.log('✓ self-update channel + replace')

putMeta(
  root,
  'plugins',
  {
    plugins: [
      {
        name: 'modlens',
        npm: '@fake/modlens',
        category: 'tools',
        description: { zh: '透镜', en: 'lens' },
        install: 'dsh plugin add @fake/modlens',
      },
      {
        name: 'turtle-ui',
        npm: '@fake/turtle-ui',
        category: 'ui',
        description: { zh: '龟', en: 'turtle' },
      },
    ],
  },
  50_000,
)
const listed = marketList(root)
if (listed.length !== 2) fail(`market list ${listed.length}`)
const hit = marketSearch(root, '透镜')
if (hit.length !== 1 || hit[0].name !== 'modlens') fail(`search ${JSON.stringify(hit)}`)
const inst = createInstance(root, { name: 'shop', version: '0.1.0-rc.6' })
const added = marketInstall(root, inst.id, 'modlens')
if (!added.bundles.some((b) => b.includes('@fake/modlens'))) fail(`install ${JSON.stringify(added)}`)
console.log('✓ market plugins.json + plugin add')

const bat = writeShortcut(root, inst.id)
if (!existsSync(bat)) fail('shortcut missing')
if (!readFileSync(bat, 'utf8').includes(inst.id)) fail('shortcut body')
console.log('✓ shortcut')

const job = createProgressJob(root, { kind: 'install-version', leaves: INSTALL_VERSION_LEAVES })
reportLeafProgress(root, job.id, 'download', { done: 40, total: 80 })
const pct = jobPercent(root, job.id)
if (Math.abs(pct - 40) > 0.01) fail(`percent ${pct} want 40`)
console.log('✓ weighted job progress')

if (!existsSync(pnpmStoreDir(root))) fail('pnpm store dir')
console.log('✓ pnpm store path')

const oldDir = join(root.path, 'instances', 'legacy')
mkdirSync(join(oldDir, 'home', 'sessions'), { recursive: true })
writeFileSync(
  join(oldDir, 'instance.json'),
  JSON.stringify({
    schema: 'pack-agent.launcher.instance/v1',
    id: 'legacy',
    name: 'legacy',
    dsh: { version: '0.1.0-rc.6' },
    profile: { name: 'web', port: 'auto' },
    workspace: { kind: 'owned', path: join(oldDir, 'workspace') },
    status: 'ready',
    home: join(oldDir, 'home'),
    created: '2026-01-01T00:00:00.000Z',
    updated: '2026-01-01T00:00:00.000Z',
  }),
)
mkdirSync(join(oldDir, 'workspace'), { recursive: true })
const migrated = getInstance(root, 'legacy')
if (!migrated.display || migrated.display.star !== false) fail(`migrate display ${JSON.stringify(migrated.display)}`)
console.log('✓ instance migrate display')

const store = loadRegistryStore({ builtinDir: builtinRegistryDir(), userDir: join(root.path, 'library', 'registries') })
if (!store.entry('migrate', 'instance-display')) fail('migrate registry')
if (!store.entry('compat-snapshot-spec', 'sessionFormatVersion')) fail('compat-snapshot-spec')
console.log('✓ migrate + compat registries')

for (const m of ['market.list', 'market.search', 'market.install', 'update.check', 'update.apply', 'shortcut.write', 'meta.get', 'credentials.list']) {
  if (!LAUNCHER_API_METHODS.includes(m as (typeof LAUNCHER_API_METHODS)[number])) fail(`missing api ${m}`)
}
const html = readFileSync(join(import.meta.dirname, '../tauri/index.html'), 'utf8')
if (!html.includes('货架')) fail('tauri missing 货架')
if (!html.includes('快捷方式')) fail('tauri missing 快捷方式')
if (!html.includes('job-progress') && !html.includes('进度')) fail('tauri missing 进度')
if (!html.includes('id="run"')) fail('tauri missing 启动')
const emptyMarket = (await invokeLauncherApi(root, 'market.search', { q: '透镜' })) as { name: string }[]
if (!emptyMarket.some((p) => p.name === 'modlens')) fail(`api search ${JSON.stringify(emptyMarket)}`)
console.log('✓ api + tauri chrome')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ p2-p3')
