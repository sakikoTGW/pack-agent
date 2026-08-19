#!/usr/bin/env bun
/**
 * 凭据库：全局钥匙 0600；新建实例默认拷贝；kind=instance 不拷。钥匙不进 instance.json。
 */
import { existsSync, readFileSync, rmSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { createInstance, ensureRoot, getInstance, seedFakeVersion } from './launcher.js'
import { globalCredentialsPath, setGlobalCredentials } from './credentials.js'

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

const tmp = packTestTmp(`creds-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)
setGlobalCredentials(root, 'DEEPSEEK_API_KEY: "REF: secret-from-global"\n')
const g = globalCredentialsPath(root)
if (!existsSync(g)) fail('global credentials missing')
if (process.platform !== 'win32') {
  const mode = statSync(g).mode & 0o777
  if (mode !== 0o600) fail(`global mode ${mode.toString(8)} want 600`)
}
const body = readFileSync(g, 'utf8')
if (!body.includes('REF: secret-from-global')) fail(body)

const copied = createInstance(root, { name: 'copied', version: '0.1.0-rc.6' })
const homeCred = join(root.path, 'instances', copied.id, 'home', '.credentials.yaml')
if (!existsSync(homeCred)) fail('global key not copied to instance')
if (!readFileSync(homeCred, 'utf8').includes('REF: secret-from-global')) fail('copied body')
if (JSON.stringify(getInstance(root, copied.id)).includes('secret-from-global')) fail('secret leaked into instance.json')
if (copied.credentials?.kind !== 'global') fail(`kind ${copied.credentials?.kind}`)

const own = createInstance(root, { name: 'own', version: '0.1.0-rc.6', credentialsKind: 'instance' })
if (existsSync(join(root.path, 'instances', own.id, 'home', '.credentials.yaml'))) {
  fail('instance kind must not copy global key')
}
if (own.credentials?.kind !== 'instance') fail(`own kind ${own.credentials?.kind}`)
console.log('✓ credentials vault')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ credentials')
