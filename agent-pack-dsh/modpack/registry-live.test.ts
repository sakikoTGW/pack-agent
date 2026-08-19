#!/usr/bin/env bun
/**
 * 注册表：snapshot 开工冻结；watch 更新映射；坏文件 PA017 不切走。
 */
import { mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { RegistryError } from './registry-store.js'
import { openRegistryStore } from './registry-live.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

function writeReg(dir: string, name: string, body: unknown): void {
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, `${name}.json`), JSON.stringify(body, null, 2))
}

const envelope = (registry: string, entries: unknown[]) => ({
  schema: 'pack-agent.registry/v1',
  registry,
  version: 1,
  entries,
})

const tmp = packTestTmp(`reg-live-${Date.now()}`)
const builtin = join(tmp, 'builtin')
const user = join(tmp, 'user')
writeReg(builtin, 'format-sniff', envelope('format-sniff', [
  { id: 'ccui-pack', match: { file: 'pack.json' }, kind: 'pack', handler: 'import-pack' },
]))
writeReg(user, 'format-sniff', envelope('format-sniff', []))

const live = openRegistryStore({ builtinDir: builtin, userDir: user })
try {
  const shot = live.snapshot()
  writeReg(user, 'format-sniff', envelope('format-sniff', [
    { id: 'ccui-pack', match: { file: 'pack.json' }, kind: 'pack', handler: 'import-pack', disabled: true },
  ]))
  const deadline = Date.now() + 4000
  let saw = false
  while (Date.now() < deadline) {
    if (live.store.entry('format-sniff', 'ccui-pack')?.body.disabled === true) {
      saw = true
      break
    }
    await Bun.sleep(50)
  }
  if (!saw) fail('watch did not pick up overlay')
  if (!live.store.warnings.some((w) => w.code === 'PA109')) fail('overlay must PA109')
  if (shot.entry('format-sniff', 'ccui-pack')?.body.disabled === true) fail('snapshot must stay frozen')
  console.log('✓ watch overlay; snapshot frozen')

  writeFileSync(join(user, 'format-sniff.json'), '{not json')
  const waitBad = Date.now() + 4000
  let err: RegistryError | undefined
  while (Date.now() < waitBad) {
    err = live.lastError()
    if (err?.code === 'PA017') break
    await Bun.sleep(50)
  }
  if (err?.code !== 'PA017') fail(`bad file must PA017, got ${err}`)
  if (!live.store.entry('format-sniff', 'ccui-pack')) fail('map dropped ccui-pack after bad file')
  console.log('✓ bad file PA017 keeps map')
} finally {
  live.close()
}

rmSync(tmp, { recursive: true, force: true })
console.log('✓ registry-live')
