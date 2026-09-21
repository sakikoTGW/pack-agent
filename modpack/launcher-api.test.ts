#!/usr/bin/env bun
/**
 * launcher-api 命令表：整窗拖入 / 选文件 / 根目录扫描 = import。
 */
import { existsSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { createInstance, ensureRoot, listInstances, seedFakeVersion } from './launcher.js'
import { invokeLauncherApi, LAUNCHER_API_METHODS } from './launcher-api.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

for (const m of [
  'instance.list',
  'import',
  'scanDrop',
  'run',
  'doctor',
  'credentials.get',
  'adopt',
]) {
  if (!LAUNCHER_API_METHODS.includes(m as (typeof LAUNCHER_API_METHODS)[number])) fail(`missing method ${m}`)
}

// 窗是 WPF 原生的：C# 直接调 launcher 核心，不再有 web 壳转发层。
const padCore = join(import.meta.dirname, '../pad/Core/Launcher.cs')
const padCli = join(import.meta.dirname, '../pad/Core/Cli.cs')
if (!existsSync(padCore)) fail(`missing ${padCore}`)
if (!existsSync(padCli)) fail(`missing ${padCli}`)

const fakeJs = `const args = process.argv.slice(2)
if (args.includes('--version') || args.includes('-V')) {
  process.stdout.write('@@VERSION@@\\n')
  process.exit(0)
}
process.exit(0)
`

const tmp = packTestTmp(`api-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)
await invokeLauncherApi(root, 'instance.create', { name: 'from-api', version: '0.1.0-rc.6' })
const listed = (await invokeLauncherApi(root, 'instance.list', {})) as { id: string }[]
if (!listed.some((i) => i.id === 'from-api')) fail(`list ${JSON.stringify(listed)}`)
if (!listInstances(root).some((i) => i.id === 'from-api')) fail('create via api missing')
try {
  await invokeLauncherApi(root, 'not-a-method', {})
  fail('unknown method must throw')
} catch {
  /* expected */
}
console.log('✓ launcher-api methods')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ launcher-api')
