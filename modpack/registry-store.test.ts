#!/usr/bin/env bun
/**
 * 注册表：路径映射、多余字段 PA017、覆盖 PA109、disabled 留在映射里。
 */
import { mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { loadRegistryStore, RegistryError } from './registry-store.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

function writeReg(dir: string, name: string, body: unknown): void {
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, `${name}.json`), JSON.stringify(body, null, 2))
}

const tmp = packTestTmp(`reg-store-${Date.now()}`)
const builtin = join(tmp, 'builtin')
const user = join(tmp, 'user')

writeReg(builtin, 'format-sniff', {
  schema: 'pack-agent.registry/v1',
  registry: 'format-sniff',
  version: 1,
  entries: [
    {
      id: 'ccui-pack',
      match: { file: 'pack.json' },
      kind: 'pack',
      handler: 'import-pack',
    },
  ],
})

const ok = loadRegistryStore({ builtinDir: builtin, userDir: user })
const hit = ok.entry('format-sniff', 'ccui-pack')
if (!hit) fail('builtin ccui-pack missing from map')
if (!hit.path.endsWith('format-sniff.json')) fail(`path map ${hit.path}`)
if (hit.body.id !== 'ccui-pack') fail('body not loaded')
console.log('✓ id maps to path')

writeReg(builtin, 'format-sniff', {
  schema: 'pack-agent.registry/v1',
  registry: 'format-sniff',
  version: 1,
  entries: [
    {
      id: 'ccui-pack',
      match: { file: 'pack.json' },
      kind: 'pack',
      handler: 'import-pack',
      extraWild: true,
    },
  ],
})
try {
  loadRegistryStore({ builtinDir: builtin, userDir: user })
  fail('extra field must PA017')
} catch (e) {
  if (!(e instanceof RegistryError) || e.code !== 'PA017') fail(`expected PA017, got ${e}`)
}
console.log('✓ extra field PA017')

writeReg(builtin, 'format-sniff', {
  schema: 'pack-agent.registry/v1',
  registry: 'format-sniff',
  version: 1,
  entries: [{ match: { file: 'pack.json' }, kind: 'pack', handler: 'import-pack' }],
})
try {
  loadRegistryStore({ builtinDir: builtin, userDir: user })
  fail('missing id must PA017')
} catch (e) {
  if (!(e instanceof RegistryError) || e.code !== 'PA017') fail(`expected PA017, got ${e}`)
}
console.log('✓ missing id PA017')

writeReg(builtin, 'format-sniff', {
  schema: 'pack-agent.registry/v1',
  registry: 'format-sniff',
  version: 1,
  entries: [
    {
      id: 'ccui-pack',
      match: { file: 'pack.json' },
      kind: 'pack',
      handler: 'import-pack',
    },
  ],
})
writeReg(user, 'format-sniff', {
  schema: 'pack-agent.registry/v1',
  registry: 'format-sniff',
  version: 1,
  entries: [
    {
      id: 'ccui-pack',
      match: { file: 'pack.json' },
      kind: 'pack',
      handler: 'import-pack',
      disabled: true,
    },
  ],
})
const over = loadRegistryStore({ builtinDir: builtin, userDir: user })
if (!over.warnings.some((w) => w.code === 'PA109')) fail(`overlay must PA109, got ${JSON.stringify(over.warnings)}`)
const disabled = over.entry('format-sniff', 'ccui-pack')
if (!disabled) fail('disabled entry must stay in map')
if (!disabled.body.disabled) fail('disabled flag dropped')
console.log('✓ overlay PA109; disabled stays in map')

writeReg(builtin, 'format-sniff', {
  schema: 'pack-agent.registry/v1',
  registry: 'format-sniff',
  version: 1,
  entries: [
    { id: 'a', match: { file: 'a' }, kind: 'pack', handler: 'import-pack', requires: ['b'] },
    { id: 'b', match: { file: 'b' }, kind: 'pack', handler: 'import-pack', requires: ['a'] },
  ],
})
writeReg(user, 'format-sniff', { schema: 'pack-agent.registry/v1', registry: 'format-sniff', version: 1, entries: [] })
try {
  loadRegistryStore({ builtinDir: builtin, userDir: user })
  fail('cycle must PA017')
} catch (e) {
  if (!(e instanceof RegistryError) || e.code !== 'PA017') fail(`expected PA017 cycle, got ${e}`)
}
console.log('✓ requires cycle PA017')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ registry-store')
