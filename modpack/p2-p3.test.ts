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
import { initPortableKit } from './portable.js'

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
if (!listed.some((p) => p.name === 'modlens')) fail(`market missing modlens ${JSON.stringify(listed)}`)
if (!listed.some((p) => p.name === 'turtle-ui')) fail('market missing turtle-ui')
if (!listed.some((p) => p.npm === '@deepseek-harness-tui/dsh-tui')) fail('market missing pinned dsh-tui bundle')
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
// PAD 窗是 WPF 原生 exe。旧的 tauri/ + Host.cs WebView2 壳已删，别再回来。
const padDir = join(import.meta.dirname, '../pad')
for (const dead of ['../tauri', '../portable/Host.cs', '../portable/PadHost', './pad-server.ts', './tauri-bridge.ts']) {
  if (existsSync(join(import.meta.dirname, dead))) fail(`旧壳没删干净：${dead}`)
}
const padProj = readFileSync(join(padDir, 'Pad.csproj'), 'utf8')
if (!padProj.includes('net9.0-windows')) fail('Pad.csproj 不是 net9.0-windows')
if (!padProj.includes('<UseWPF>true</UseWPF>')) fail('Pad.csproj 缺 UseWPF')
if (!padProj.includes('pack-agent-for DSH')) fail('Pad.csproj 缺 AssemblyName pack-agent-for DSH')
if (padProj.includes('WebView2')) fail('Pad.csproj 不该再引 WebView2')

const mainXaml = readFileSync(join(padDir, 'MainWindow.xaml'), 'utf8')
if (!mainXaml.includes('ResizeMode="CanMinimize"')) fail('PAD 窗必须固定大小 CanMinimize')
if (!mainXaml.includes('WindowStyle="None"')) fail('PAD 窗缺无边框')
if (!mainXaml.includes('Title="PAD"')) fail('PAD 窗缺标题 PAD')
for (const tab of ['启动', '管理', '下载', '设置']) {
  if (!mainXaml.includes(`Content="${tab}"`)) fail(`顶栏缺 ${tab} 页`)
}
if (mainXaml.includes('联机') || mainXaml.includes('更多')) fail('顶栏不该有摆设页')
if (!mainXaml.includes('Back_Click')) fail('内页缺回退键')
if (!mainXaml.includes('v:Ico')) fail('顶栏页签没有小图标')

const settingsXaml = readFileSync(join(padDir, 'Views', 'SettingsView.xaml'), 'utf8')
for (const layer of ['启动', '个性化', '下载', '其他']) {
  if (!settingsXaml.includes(`Content="${layer}"`)) fail(`设置缺分层 ${layer}`)
}
if (!settingsXaml.includes('Tag="launch"')) fail('设置缺启动层')
if (!settingsXaml.includes('Tag="ui"')) fail('设置缺个性化层')
const padModels = readFileSync(join(padDir, 'Core', 'Models.cs'), 'utf8')
if (!padModels.includes('LaunchPrefs') || !padModels.includes('UiPrefs')) fail('pad.json 没分层')
if (!existsSync(join(padDir, 'Core', 'Theme.cs'))) fail('缺 Theme.cs')

const downloadXaml = readFileSync(join(padDir, 'Views', 'DownloadView.xaml'), 'utf8')
if (!downloadXaml.includes('社区资源')) fail('下载页缺社区资源分组')
if (!downloadXaml.includes('x:Name="ShelfNav"')) fail('下载页货架必须动态生成（ShelfNav）')
if (!downloadXaml.includes('Height="64"')) fail('市场行不是 64px')
if (!existsSync(join(padDir, 'Views', 'Ico.cs'))) fail('缺 Ico 控件')
if (!existsSync(join(padDir, 'Theme', 'Icons.xaml'))) fail('缺 Icons.xaml')
if (!existsSync(join(padDir, 'Core', 'Market.cs'))) fail('缺 Market.cs')
const padMarket = readFileSync(join(padDir, 'Core', 'Market.cs'), 'utf8')
if (!padMarket.includes('Download.Shelves')) fail('Market 货架没读 pad.json')
if (!padModels.includes('keywords:dsh-plugin')) fail('DefaultShelves 不是 npm keywords:dsh-plugin')
if (!padModels.includes('windowWidth') || !padModels.includes('defaultRelease'))
  fail('pad.json v3 缺窗几何/默认发行号')
if (!padModels.includes('installTimeoutSec'))
  fail('pad.json 缺安装超时')
if (!settingsXaml.includes('x:Name="ShelvesBox"')) fail('设置缺货架编辑')
if (!settingsXaml.includes('x:Name="WinWBox"')) fail('设置缺窗宽')
if (!settingsXaml.includes('Tag="custom"')) fail('设置缺自定义主题')

const palette = readFileSync(join(padDir, 'Theme', 'Palette.xaml'), 'utf8').replace(/<!--[\s\S]*?-->/g, '')
if (/1370f3/i.test(palette)) fail('PAD 配色不该抄 PCL #1370f3')
if (!/<Color x:Key="C\.Accent"/.test(palette)) fail('Palette 缺 C.Accent')

const padCli = readFileSync(join(padDir, 'Core', 'Cli.cs'), 'utf8')
for (const verb of ['release', 'instance', 'profile', 'run', 'stop', 'ps', 'gateway', 'where', 'market']) {
  if (!padCli.includes(`"${verb}"`)) fail(`pad cli 缺 ${verb}`)
}
const padRunner = readFileSync(join(padDir, 'Core', 'Runner.cs'), 'utf8')
if (!padRunner.includes('wt')) fail('Runner 不弹 Windows Terminal')
const padGateway = readFileSync(join(padDir, 'Core', 'Gateway.cs'), 'utf8')
if (!padGateway.includes('client-request')) fail('Gateway 不走官方 apiproxy 信封')
if (padGateway.includes('pad-control')) fail('pad-control 已废，别再用')
console.log('✓ PAD WPF 壳：固定窗 + 四页 + 回退键 + 官方 apiproxy')

// 投影轴留在 TS/Rust：PAD 当子进程调，不在 C# 里再写一份 catalog。
const padPacks = readFileSync(join(padDir, 'Core', 'Packs.cs'), 'utf8')
for (const verb of ['pack list', 'pack project', 'pack allow', 'pack deny', 'scan-drop']) {
  if (!padPacks.includes(verb)) fail(`Packs.cs 缺 ${verb}`)
}
if (!padPacks.includes('packagent.js')) fail('Packs.cs 不调 packagent bin')

// 热重载 = 试验（--patch 只挂本次）→ 固化（写进 bundles）。官方 apiproxy 没有
// plugin/loader rpc，所以不许假装能对跑着的进程热挂。
const padTrial = readFileSync(join(padDir, 'Core', 'Trial.cs'), 'utf8')
if (!padTrial.includes('--patch')) fail('Trial 不用官方 --patch 叠加')
if (!padTrial.includes('dsh.profile.bundles')) fail('Trial 不写 dsh.profile.bundles')
if (!padTrial.includes('PA032')) fail('Trial 缺「已固化不许重复试验」这条防线')
if (!padTrial.includes('wasDependency')) fail('Trial 会卸掉 profile 原本就有的依赖')
if (!/Commit\(/.test(padTrial) || !/Discard\(/.test(padTrial)) fail('Trial 缺固化/丢掉')
const padRunnerPatch = readFileSync(join(padDir, 'Core', 'Runner.cs'), 'utf8')
if (!padRunnerPatch.includes('PatchArgs')) fail('启动脚本不带试验的 --patch')
const codes = readFileSync(join(import.meta.dirname, 'registries', 'pa-codes.json'), 'utf8')
if (!codes.includes('PA032')) fail('pa-codes 缺 PA032')
if (codes.includes('pad-control')) fail('pa-codes 还在说 pad-control')
console.log('✓ PAD 装包链 + 热重载：试验 --patch → 固化 bundles')

const kit = join(tmp, 'unzip-play')
process.env.PAD_PUBLISH_SINGLE = '0'
const made = initPortableKit(kit, { repo: join(import.meta.dirname, '../..') })
if (!existsSync(join(kit, '打开.bat'))) fail('portable missing 打开.bat')
if (!existsSync(join(kit, '.pack-launcher', 'launcher.json'))) fail('portable missing .pack-launcher')
const openBat = readFileSync(join(kit, '打开.bat'), 'utf8')
if (!openBat.includes('PACK_LAUNCHER_ROOT')) fail('bat missing root')
if (openBat.includes('npm run dev')) fail('bat 还在起 tauri dev')
if (!openBat.includes('pack-agent-for DSH.exe')) fail('bat 不启动 PAD exe')
if (!existsSync(join(kit, 'example-pack', 'pack.json'))) fail('portable missing example-pack')
if (!existsSync(join(kit, 'example-pack', 'skills', 'hello', 'SKILL.md'))) fail('example-pack missing skills/=mods')
if (!existsSync(join(kit, 'example.pack.zip'))) fail('整合包目录缺 exe 旁 example.pack.zip')
if (!made.launcherRoot.endsWith('.pack-launcher')) fail(`launcherRoot ${made.launcherRoot}`)
if (!existsSync(join(kit, 'pack-agent-for DSH.exe'))) fail('portable missing pack-agent-for DSH.exe')
const mz = readFileSync(join(kit, 'pack-agent-for DSH.exe'))
if (mz[0] !== 0x4d || mz[1] !== 0x5a) fail('pack-agent-for DSH.exe is not a Windows exe')
if (existsSync(join(kit, 'hostfxr.dll')) || existsSync(join(kit, 'coreclr.dll')))
  fail('portable 把运行时 dll 跟 exe 平铺在包根')
console.log('✓ portable unzip kit')
const emptyMarket = (await invokeLauncherApi(root, 'market.search', { q: '透镜' })) as { name: string }[]
if (!emptyMarket.some((p) => p.name === 'modlens')) fail(`api search ${JSON.stringify(emptyMarket)}`)
console.log('✓ api + PAD chrome')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ p2-p3')
