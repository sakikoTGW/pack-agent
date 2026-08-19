#!/usr/bin/env bun
/**
 * 收编：home 指向已有目录，不改里面的文件；两实例同 home → PA020；外部改动 PA106。
 */
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import {
  createInstance,
  ensureRoot,
  getInstance,
  instanceHome,
  LauncherError,
  renderDiag,
  seedFakeVersion,
} from './launcher.js'
import { adoptExistingHome, checkAdoptedHome } from './adopt.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const fakeJs = `const args = process.argv.slice(2)
if (args.includes('--version') || args.includes('-V')) {
  process.stdout.write('@@VERSION@@\\n')
  process.exit(0)
}
process.exit(0)
`

const tmp = packTestTmp(`adopt-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)

const external = join(tmp, 'fake-dsh-home')
mkdirSync(join(external, 'sessions'), { recursive: true })
writeFileSync(join(external, '.credentials.yaml'), 'REF: keep-me\n')
writeFileSync(join(external, 'sessions', 'old.txt'), 'untouched')

const adopted = adoptExistingHome(root, {
  home: external,
  name: '本机原有',
  id: 'local-dsh',
  version: '0.1.0-rc.6',
})
if (adopted.id !== 'local-dsh') fail(`id ${adopted.id}`)
if (!adopted.adopted) fail('missing adopted flag')
if (instanceHome(root, adopted.id) !== external && !instanceHome(root, adopted.id).endsWith('fake-dsh-home')) {
  fail(`home ${instanceHome(root, adopted.id)}`)
}
if (!existsSync(join(external, 'sessions', 'old.txt'))) fail('adopt wrote/deleted session')
if (!readFileSync(join(external, '.credentials.yaml'), 'utf8').includes('keep-me')) fail('adopt mutated credentials')
if (existsSync(join(external, 'launcher.patch.yml'))) fail('adopt wrote launcher.patch.yml')
if (existsSync(join(external, '.pa-write-probe'))) fail('adopt left probe')
console.log('✓ adopt points at existing home, does not write')

try {
  adoptExistingHome(root, { home: external, name: 'dup', id: 'dup-dsh', version: '0.1.0-rc.6' })
  fail('second adopt of same home must PA020')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA020') fail(`expected PA020, got ${e}`)
}
const regular = createInstance(root, { name: 'regular', version: '0.1.0-rc.6' })
try {
  adoptExistingHome(root, {
    home: instanceHome(root, regular.id),
    name: 'steal',
    id: 'steal',
    version: '0.1.0-rc.6',
  })
  fail('adopt of another instance home must PA020')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA020') fail(`expected PA020, got ${e}`)
}
console.log('✓ PA020 unique home')

const warn0 = checkAdoptedHome(root, adopted.id)
if (warn0) fail(`fresh adopt should be clean, got ${warn0.message}`)
writeFileSync(join(external, '.credentials.yaml'), 'REF: changed-outside\n')
const warn = checkAdoptedHome(root, adopted.id)
if (!warn || warn.code !== 'PA106') fail(`expected PA106, got ${JSON.stringify(warn)}`)
const text = renderDiag(warn)
if (!text.includes('warning[PA106]')) fail(text)
console.log('✓ PA106 when adopted home credentials change')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ adopt')
