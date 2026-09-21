#!/usr/bin/env bun
/**
 * npm packument for @deepseek-ai/dsh. PAD 下载页必须列出全部发行号，不能只塞进 ComboBox。
 */
import { bannedRelease, parseDshPackument } from './dsh-packument.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const body = {
  'dist-tags': { latest: '0.1.0-rc.7' },
  versions: {
    '0.0.1-rc.1': {},
    '0.1.0-rc.3': {},
    '0.1.0-rc.6': {},
    '0.1.0-rc.7': {},
  },
  time: {
    created: '2026-01-01T00:00:00.000Z',
    '0.1.0-rc.7': '2026-08-01T00:00:00.000Z',
    '0.1.0-rc.6': '2026-07-01T00:00:00.000Z',
  },
}

if (!bannedRelease('0.0.1-rc.1')) fail('0.0.1-rc.1 is banned')
if (bannedRelease('0.0.1-rc.5')) fail('0.0.1-rc.5 is a published dsh release')
if (bannedRelease('0.1.0-rc.7')) fail('0.1.0-rc.7 is a real release')

const catalog = parseDshPackument(body)
if (catalog.latest !== '0.1.0-rc.7') fail(`latest ${catalog.latest}`)
if (catalog.versions.some((v) => v.version === '0.0.1-rc.1')) fail('banned version leaked')
if (catalog.versions.map((v) => v.version).join(',') !== '0.1.0-rc.7,0.1.0-rc.6,0.1.0-rc.3') {
  fail(`order ${catalog.versions.map((v) => v.version).join(',')}`)
}
if (catalog.versions[0]?.latest !== true) fail('head is latest')
if (catalog.versions[0]?.time !== '2026-08-01T00:00:00.000Z') fail('time on rc.7')

const empty = parseDshPackument({})
if (empty.versions.length !== 0) fail('empty packument')

console.log('✓ dsh packument')
