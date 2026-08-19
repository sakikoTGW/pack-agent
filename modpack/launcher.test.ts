#!/usr/bin/env bun
/**
 * P0：诊断形状、两版本并存、实例 home 隔离、双进程 run/stop。
 */
import { spawnSync } from 'node:child_process'
import { mkdirSync, writeFileSync, existsSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import {
  renderDiag,
  ensureRoot,
  seedFakeVersion,
  listVersions,
  createInstance,
  removeVersion,
  runInstance,
  stopInstance,
  readRuntime,
  LauncherError,
} from './launcher.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const text = renderDiag({
  code: 'PA001',
  level: 'error',
  message: 'pinned dsh 0.1.0-rc.6 is not installed',
  location: 'instance `writing` / dsh.version',
  context: ['pinned dsh : 0.1.0-rc.6', 'versions/  : missing'],
  note: 'install the version before creating or starting the instance',
  help: ['packagent dsh launcher version install 0.1.0-rc.6'],
})
if (!text.includes('error[PA001]')) fail(`diag missing error[PA001]: ${text}`)
if (!text.includes('-->')) fail(`diag missing -->: ${text}`)
if (!text.includes('= help:')) fail(`diag missing help: ${text}`)
console.log('✓ PA001 rustc shape')

const rootDir = packTestTmp(`launcher-p0-${Date.now()}`)
mkdirSync(rootDir, { recursive: true })
const root = ensureRoot(rootDir)

const fakeJs = `const args = process.argv.slice(2)
if (args.includes('--version') || args.includes('-V')) {
  process.stdout.write('@@VERSION@@\\n')
  process.exit(0)
}
let port = '3080'
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--port' && args[i + 1]) port = String(args[i + 1])
}
process.stdout.write('dsh web: http://127.0.0.1:' + port + '\\n')
setInterval(() => {}, 60000)
`

seedFakeVersion(root, '0.1.0-rc.3', fakeJs)
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)
const vers = listVersions(root).map((v) => v.version).sort()
if (vers.join(',') !== '0.1.0-rc.3,0.1.0-rc.6') fail(`versions: ${vers.join(',')}`)
if (!existsSync(join(rootDir, 'versions', '0.1.0-rc.3'))) fail('missing rc.3 dir')
if (!existsSync(join(rootDir, 'versions', '0.1.0-rc.6'))) fail('missing rc.6 dir')
console.log('✓ two version dirs')

const a = createInstance(root, { name: 'alpha', version: '0.1.0-rc.6', profile: 'web', port: 31911 })
const b = createInstance(root, { name: 'beta', version: '0.1.0-rc.6', profile: 'web', port: 31912 })
if (a.profile.name !== 'web' || b.profile.name !== 'web') fail(`profile.name: ${a.profile.name}`)
if ('shell' in a || 'kind' in a.profile) fail(`instance still has shell/kind: ${JSON.stringify(a)}`)
const marker = join(rootDir, 'instances', a.id, 'home', 'sessions', 'only-a.txt')
writeFileSync(marker, 'secret-a')
if (existsSync(join(rootDir, 'instances', b.id, 'home', 'sessions', 'only-a.txt'))) {
  fail('beta home contains alpha session marker')
}
console.log('✓ instance homes isolated')

try {
  removeVersion(root, '0.1.0-rc.6')
  fail('remove pinned version should throw PA006')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA006') {
    fail(`expected PA006, got ${e}`)
  }
  const rendered = renderDiag(e.diagnostic)
  if (!rendered.includes('error[PA006]')) fail(rendered)
}
console.log('✓ PA006 blocks remove of pinned version')

const ra = await runInstance(root, a.id, { detach: true })
const rb = await runInstance(root, b.id, { detach: true })
const rt = readRuntime(root)
if (rt.entries.length !== 2) fail(`runtime entries ${rt.entries.length}`)
if (!rt.entries.some((e) => e.pid === ra.pid) || !rt.entries.some((e) => e.pid === rb.pid)) {
  fail(`runtime pids ${JSON.stringify(rt.entries)}`)
}
console.log('✓ two pids in runtime.json')

await stopInstance(root, a.id)
const after = readRuntime(root)
if (after.entries.some((e) => e.instance === a.id && e.status === 'running')) {
  fail('alpha still running')
}
if (!after.entries.some((e) => e.instance === b.id && e.status === 'running')) {
  fail('beta should still be running')
}
try {
  process.kill(rb.pid, 0)
} catch {
  fail(`beta pid ${rb.pid} died after stopping alpha`)
}
await stopInstance(root, b.id)
console.log('✓ stop A leaves B running')

rmSync(rootDir, { recursive: true, force: true })

const help = spawnSync(process.execPath, [join(import.meta.dirname, '../..', 'src', 'cli.ts'), 'dsh', 'launcher', '--help'], {
  encoding: 'utf8',
})
if (help.status !== 0) fail(`launcher --help exit ${help.status}: ${help.stderr}`)
if (!help.stdout.includes('packagent dsh launcher version')) {
  fail(`parent ate launcher --help:\n${help.stdout}`)
}
if (!help.stdout.includes('--profile')) fail(`help missing --profile:\n${help.stdout}`)
if (help.stdout.includes('--shell')) fail(`help still has --shell:\n${help.stdout}`)
console.log('✓ packagent dsh launcher --help')

console.log('✓ launcher P0')
