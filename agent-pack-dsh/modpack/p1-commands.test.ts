#!/usr/bin/env bun
/**
 * P1 命令面：clone/rename/pin、plugin、session 锁、job 锁、崩溃分析、空态。
 */
import { mkdirSync, writeFileSync, existsSync, readFileSync, rmSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import {
  createInstance,
  ensureRoot,
  getInstance,
  listInstances,
  listVersions,
  seedFakeVersion,
  LauncherError,
  writeInstance,
} from './launcher.js'
import { encodeSegment, projectKey } from './project-key.js'
import { cloneInstance, pinInstance, renameInstance, setInstanceDisplay } from './instance-ops.js'
import { pluginAddToInstance, pluginDisable, pluginList, pluginRemove } from './plugin-ops.js'
import {
  acquireSidLock,
  backupSessions,
  deleteSession,
  inspectSession,
  listSessions,
} from './session-ops.js'
import { acquireInstanceLock, cancelJob, listJobs } from './jobs.js'
import { analyzeCrash } from './crash.js'
import { wrapList } from './empty-state.js'
import { invokeLauncherApi, LAUNCHER_API_METHODS } from './launcher-api.js'
import { packList } from './pack-ops.js'

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
  const disabled = path.join(home, 'pa-disabled.json')
  const off = fs.existsSync(disabled) ? JSON.parse(fs.readFileSync(disabled, 'utf8')) : []
  if (list.some((s) => String(s).includes('pack-agent'))) {
    process.stdout.write('- id: pack-agent\\n')
  }
  process.stdout.write('dsh.profile.bundles:\\n')
  for (const s of list) {
    if (off.includes(s) || off.some((x) => String(s).includes(String(x)))) continue
    process.stdout.write('  - ' + s + '\\n')
  }
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
if (args[0] === 'plugin' && args.includes('remove')) {
  const pkg = args[args.length - 1]
  const added = path.join(home, 'pa-added.json')
  const list = (fs.existsSync(added) ? JSON.parse(fs.readFileSync(added, 'utf8')) : []).filter((s) => !String(s).includes(pkg))
  fs.writeFileSync(added, JSON.stringify(list))
  process.exit(0)
}
if (args[0] === 'plugin' && args.includes('update')) {
  const pkg = args[args.length - 1]
  const added = path.join(home, 'pa-added.json')
  const list = fs.existsSync(added) ? JSON.parse(fs.readFileSync(added, 'utf8')) : []
  list.push('updated:' + pkg)
  fs.writeFileSync(added, JSON.stringify(list))
  process.exit(0)
}
process.exit(0)
`

const tmp = packTestTmp(`p1-cmd-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.3', fakeJs)
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)

const emptyVer = wrapList('version.list', [])
if (!emptyVer.emptyState || emptyVer.emptyState.title !== '无可用版本') {
  fail(`empty version ${JSON.stringify(emptyVer)}`)
}
const emptyInst = wrapList('instance.list', [])
if (!emptyInst.emptyState?.action?.includes('instance create')) fail(`empty instance ${JSON.stringify(emptyInst)}`)
console.log('✓ emptyState')

const src = createInstance(root, { name: 'src-inst', version: '0.1.0-rc.6', profile: 'web' })
if (!src.display || src.display.star !== false) fail(`display default ${JSON.stringify(src.display)}`)
writeFileSync(join(src.home, '.credentials.yaml'), 'REF: k\n')
mkdirSync(join(src.home, 'attachments'), { recursive: true })
writeFileSync(join(src.home, 'attachments', 'pic.txt'), 'img')
const slug = projectKey(src.workspace.path)
const sid = 'talk-1'
const sidDir = join(src.home, 'sessions', slug, encodeSegment(sid))
mkdirSync(sidDir, { recursive: true })
writeFileSync(
  join(sidDir, 'session.jsonl'),
  JSON.stringify({ version: 0, id: sid, createdAt: 1, cwd: src.workspace.path, dshVersion: '0.1.0-rc.6' }) + '\n',
)

const cloned = cloneInstance(root, src.id, 'clone-inst')
if (cloned.id === src.id) fail('clone id')
if (cloned.home === src.home) fail('clone shared home')
if (!existsSync(join(cloned.home, 'sessions', slug, encodeSegment(sid), 'session.jsonl'))) {
  fail('clone missing session')
}
if (!existsSync(join(cloned.home, '.credentials.yaml'))) fail('clone missing credentials')
if (!existsSync(join(cloned.home, 'attachments', 'pic.txt'))) fail('clone missing attachments')
if (cloned.profile.port !== 'auto') fail(`clone port ${cloned.profile.port}`)
if (cloned.workspace.path !== src.workspace.path) fail('clone must keep existing workspace')
if (listInstances(root).filter((i) => i.id === cloned.id || i.id === src.id).length !== 2) fail('both instances')
console.log('✓ clone home isolated')

const renamed = renameInstance(root, cloned.id, 'clone-renamed')
if (renamed.id !== cloned.id) fail('rename must keep id')
if (renamed.name !== 'clone-renamed') fail(`rename name ${renamed.name}`)
const starred = setInstanceDisplay(root, src.id, { star: true, info: '写作', category: 'work' })
if (!starred.display?.star || starred.display.info !== '写作') fail(`display ${JSON.stringify(starred.display)}`)
console.log('✓ rename + display')

const pinned = pinInstance(root, src.id, '0.1.0-rc.3')
if (pinned.value.dsh.version !== '0.1.0-rc.3') fail(`pin ${pinned.value.dsh.version}`)
if (!pinned.warnings.some((w) => w.code === 'PA103')) fail(`PA103 ${JSON.stringify(pinned.warnings)}`)
pinInstance(root, src.id, '0.1.0-rc.6')
console.log('✓ pin PA103')

const verDir = join(root.path, 'versions', '0.1.0-rc.6', 'version.json')
const verJson = JSON.parse(readFileSync(verDir, 'utf8')) as { patchRowIds?: string[] }
verJson.patchRowIds = []
writeFileSync(verDir, JSON.stringify(verJson, null, 2))
const drift = pinInstance(root, src.id, '0.1.0-rc.6')
if (!drift.warnings.some((w) => w.code === 'PA107')) fail(`PA107 ${JSON.stringify(drift.warnings)}`)
verJson.patchRowIds = ['session-persistence-jsonl']
writeFileSync(verDir, JSON.stringify(verJson, null, 2))
console.log('✓ PA107 overlay drift')

const listedBefore = pluginList(root, src.id)
if (!Array.isArray(listedBefore.bundles)) fail('plugin list shape')
const addWithSession = pluginAddToInstance(root, src.id, '@fake/ok@1.0.0')
if (!addWithSession.warnings.some((w) => w.code === 'PA021')) fail(`PA021 ${JSON.stringify(addWithSession.warnings)}`)
const afterAdd = pluginList(root, src.id)
if (!afterAdd.bundles.some((b) => b.includes('@fake/ok'))) fail(`bundles ${JSON.stringify(afterAdd.bundles)}`)
if (getInstance(root, src.id).status !== 'restart-required') fail('restart-required after add')

const localBad = join(tmp, 'bad-engine')
mkdirSync(localBad, { recursive: true })
writeFileSync(
  join(localBad, 'package.json'),
  JSON.stringify({ name: 'bad-engine', version: '1.0.0', engines: { dsh: '>=99.0.0' }, dsh: { bundle: { patch: './x.yml' } } }),
)
try {
  pluginAddToInstance(root, src.id, localBad)
  fail('engines mismatch must PA002')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA002') fail(`expected PA002, got ${e}`)
}

const localPlain = join(tmp, 'plain-dep')
mkdirSync(localPlain, { recursive: true })
writeFileSync(join(localPlain, 'package.json'), JSON.stringify({ name: 'plain-dep', version: '1.0.0' }))
const plain = pluginAddToInstance(root, src.id, localPlain)
if (!plain.warnings.some((w) => w.code === 'PA101')) fail('PA101 undeclared engines')
if (!plain.warnings.some((w) => w.code === 'PA105')) fail('PA105 no dsh.bundle')

pluginAddToInstance(root, src.id, '@sakikotgw/pack-agent-dsh@1.0.0')
const off = pluginDisable(root, src.id, 'pack-agent')
if (!off.warnings.some((w) => w.code === 'PA110')) fail(`PA110 ${JSON.stringify(off.warnings)}`)
pluginRemove(root, src.id, '@fake/ok')
console.log('✓ plugin PA021/002/101/105/110')

const sessions = listSessions(root, src.id)
if (!sessions.value.some((s) => s.id === sid)) fail(`list sessions ${JSON.stringify(sessions.value)}`)
inspectSession(root, src.id, sid)

const mismatchDir = join(src.home, 'sessions', slug, encodeSegment('gone-cwd'))
mkdirSync(mismatchDir, { recursive: true })
writeFileSync(
  join(mismatchDir, 'session.jsonl'),
  JSON.stringify({ version: 0, id: 'gone-cwd', createdAt: 2, cwd: join(tmp, 'no-such-ws'), dshVersion: '0.1.0-rc.6' }) + '\n',
)
try {
  inspectSession(root, src.id, 'gone-cwd')
  fail('cwd mismatch must PA007')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA007') fail(`expected PA007, got ${e}`)
}

const fmtDir = join(src.home, 'sessions', slug, encodeSegment('fmt-bad'))
mkdirSync(fmtDir, { recursive: true })
writeFileSync(
  join(fmtDir, 'session.jsonl'),
  JSON.stringify({ version: 99, id: 'fmt-bad', createdAt: 3, cwd: src.workspace.path }) + '\n',
)
try {
  inspectSession(root, src.id, 'fmt-bad')
  fail('format mismatch must PA015')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA015') fail(`expected PA015, got ${e}`)
}

const driftDir = join(src.home, 'sessions', slug, encodeSegment('ver-drift'))
mkdirSync(driftDir, { recursive: true })
writeFileSync(
  join(driftDir, 'session.jsonl'),
  JSON.stringify({ version: 0, id: 'ver-drift', createdAt: 4, cwd: src.workspace.path, dshVersion: '0.1.0-rc.3' }) + '\n',
)
const driftSess = inspectSession(root, src.id, 'ver-drift')
if (!driftSess.warnings.some((w) => w.code === 'PA102')) fail(`PA102 ${JSON.stringify(driftSess.warnings)}`)

const lock1 = acquireSidLock(root, src.id, sid)
const lock2pid = lock1.pid
try {
  acquireSidLock(root, src.id, sid)
  fail('second lock must PA003')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA003') fail(`expected PA003, got ${e}`)
}
writeFileSync(
  join(sidDir, 'owner.json'),
  JSON.stringify({ instance: src.id, pid: 1, dshVersion: '0.1.0-rc.6', acquiredAt: new Date().toISOString() }),
)
const recycled = acquireSidLock(root, src.id, sid)
if (recycled.pid === 1) fail('stale lock not recycled')
void lock2pid

const collideSlug = '--E-other-slug--'
const collideDir = join(src.home, 'sessions', collideSlug, encodeSegment('collide'))
mkdirSync(collideDir, { recursive: true })
writeFileSync(
  join(collideDir, 'session.jsonl'),
  JSON.stringify({ version: 0, id: 'collide', createdAt: 5, cwd: src.workspace.path, dshVersion: '0.1.0-rc.6' }) + '\n',
)
const listedCollide = listSessions(root, src.id)
if (!listedCollide.warnings.some((w) => w.code === 'PA108')) fail(`PA108 ${JSON.stringify(listedCollide.warnings)}`)

writeFileSync(join(src.home, 'session-query.sqlite'), 'idx')
const del = deleteSession(root, src.id, 'fmt-bad')
if (!del.warnings.some((w) => w.code === 'PA111')) fail(`PA111 ${JSON.stringify(del.warnings)}`)
if (existsSync(fmtDir)) fail('delete left session dir')
const bak = backupSessions(root, src.id)
if (!existsSync(bak.path)) fail('backup missing')
if (!bak.path.endsWith('.zip') && !existsSync(join(bak.path, 'sessions'))) fail(`backup ${bak.path}`)
console.log('✓ session PA003/007/015/102/108/111')

try {
  acquireInstanceLock(root, src.id, 'job-hold')
  cloneInstance(root, src.id, 'locked-clone')
  fail('held lock must PA008')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA008') fail(`expected PA008, got ${e}`)
}
const jobs = listJobs(root)
if (!jobs.some((j) => j.id === 'job-hold' && j.status === 'running')) fail(`jobs ${JSON.stringify(jobs)}`)
cancelJob(root, 'job-hold')
const afterCancel = listJobs(root)
if (!afterCancel.some((j) => j.id === 'job-hold' && (j.status === 'cancelled' || j.status === 'done'))) {
  fail(`cancel ${JSON.stringify(afterCancel)}`)
}
cloneInstance(root, src.id, 'after-unlock')
console.log('✓ job PA008')

const logDir = join(root.path, 'instances', src.id, 'logs')
mkdirSync(logDir, { recursive: true })
writeFileSync(join(logDir, 'web-crash.log'), 'Error: listen EADDRINUSE: address already in use :::3080\n')
const crash = analyzeCrash(root, src.id)
if (crash.category !== 'port') fail(`crash category ${crash.category}`)
if (!crash.faq?.length) fail(`crash faq ${JSON.stringify(crash)}`)
console.log('✓ crash analyzer')

const packs = await packList(root, src.id)
if (!Array.isArray(packs)) fail('pack list')
console.log('✓ pack list')

for (const m of [
  'instance.clone',
  'instance.rename',
  'instance.pin',
  'plugin.list',
  'plugin.add',
  'session.list',
  'job.list',
  'crash.analyze',
  'pack.list',
]) {
  if (!LAUNCHER_API_METHODS.includes(m as (typeof LAUNCHER_API_METHODS)[number])) fail(`missing api ${m}`)
}
const viaApi = (await invokeLauncherApi(root, 'instance.info', { id: src.id })) as { id: string }
if (viaApi.id !== src.id) fail('api info')
const emptyRoot = ensureRoot(join(tmp, 'empty-api'))
const emptyList = (await invokeLauncherApi(emptyRoot, 'version.list', {})) as {
  items: unknown[]
  emptyState?: { title: string }
}
if (!emptyList.emptyState || emptyList.items.length !== 0) fail(`api empty ${JSON.stringify(emptyList)}`)
console.log('✓ launcher-api methods')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ p1-commands')
