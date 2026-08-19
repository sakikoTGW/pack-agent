#!/usr/bin/env bun
/**
 * 启动器根目录旁路：.pack.zip / .pinst.zip 走 import，成功后删包。子目录不扫。
 */
import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { createInstance, ensureRoot, listInstances, seedFakeVersion } from './launcher.js'
import { exportInstance } from './pinst.js'
import { scanDropZips } from './drop-scan.js'

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

const tmp = packTestTmp(`drop-scan-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)
createInstance(root, { name: 'alpha', version: '0.1.0-rc.6', profile: 'web' })

const nested = join(root.path, 'instances', 'ignore-me.pinst.zip')
writeFileSync(nested, 'not-a-zip')
const garbage = join(root.path, 'broken.pack.zip')
writeFileSync(garbage, 'not-a-zip')
const zip = join(root.path, 'alpha.pinst.zip')
await exportInstance(root, 'alpha', { out: zip })
if (!existsSync(zip)) fail('export missing')

const result = await scanDropZips(root, { publishedVersions: ['0.1.0-rc.6'] })
if (existsSync(zip)) fail('successful pinst zip must be deleted')
if (!existsSync(garbage)) fail('failed pack zip must stay')
if (!existsSync(nested)) fail('nested zip must not be scanned')
if (!result.imported.some((id) => id !== 'alpha')) fail(`imported ${JSON.stringify(result.imported)}`)
if (!result.failed.some((f) => f.path.endsWith('broken.pack.zip'))) fail(`failed ${JSON.stringify(result.failed)}`)
const ids = listInstances(root).map((i) => i.id).sort()
if (!ids.includes('alpha') || ids.length < 2) fail(`instances ${ids}`)
console.log('✓ scan-drop imports pinst, deletes on success, keeps failures, skips nested')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ drop-scan')
