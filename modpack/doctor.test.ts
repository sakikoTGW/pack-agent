#!/usr/bin/env bun
/**
 * doctor：全表 schema/闭包/环；import 链原语必须能解析。
 */
import { mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { ensureRoot } from './launcher.js'
import { doctorLauncher } from './doctor.js'
import { builtinRegistryDir } from './registry-store.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const tmp = packTestTmp(`doctor-${Date.now()}`)
const root = ensureRoot(join(tmp, 'ok'))
const ok = doctorLauncher(root, { builtinDir: builtinRegistryDir() })
if (!ok.ok) fail(`builtin doctor failed: ${JSON.stringify(ok.errors)}`)
if (!ok.primitives.includes('sniff')) fail(`primitives ${ok.primitives}`)
if (!ok.registry.names.includes('format-sniff')) fail(`names ${ok.registry.names}`)
console.log('✓ doctor builtin ok')

const badRoot = ensureRoot(join(tmp, 'bad'))
mkdirSync(join(badRoot.path, 'library', 'registries'), { recursive: true })
writeFileSync(
  join(badRoot.path, 'library', 'registries', 'task-kinds.json'),
  JSON.stringify({
    schema: 'pack-agent.registry/v1',
    registry: 'task-kinds',
    version: 1,
    entries: [
      {
        id: 'import',
        steps: [{ primitive: 'not-a-real-primitive' }],
      },
    ],
  }),
)
const bad = doctorLauncher(badRoot, { builtinDir: builtinRegistryDir() })
if (bad.ok) fail('unknown primitive must fail doctor')
if (!bad.errors.some((e) => e.code === 'PA018')) fail(`expected PA018, got ${JSON.stringify(bad.errors)}`)
console.log('✓ doctor PA018 unknown primitive')

const extraRoot = ensureRoot(join(tmp, 'extra'))
writeFileSync(
  join(extraRoot.path, 'library', 'registries', 'format-sniff.json'),
  JSON.stringify({
    schema: 'pack-agent.registry/v1',
    registry: 'format-sniff',
    version: 1,
    entries: [{ id: 'ccui-pack', match: { file: 'pack.json' }, kind: 'pack', handler: 'import-pack', extraWild: true }],
  }),
)
const extra = doctorLauncher(extraRoot, { builtinDir: builtinRegistryDir() })
if (extra.ok) fail('extra field must fail doctor')
if (!extra.errors.some((e) => e.code === 'PA017')) fail(`expected PA017, got ${JSON.stringify(extra.errors)}`)
console.log('✓ doctor PA017 extra field')

rmSync(tmp, { recursive: true, force: true })
console.log('✓ doctor')
