#!/usr/bin/env bun
/**
 * pad-gateway.json advert + ping URL. Must fail closed on a non-loopback ad.
 */
import { mkdirSync, writeFileSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { loadAd, pingUrl, publicAd } from './ad.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const dir = join('E:\\tmp\\pack-agent', `dsh-mcp-ad-${Date.now()}`)
mkdirSync(dir, { recursive: true })
try {
  writeFileSync(join(dir, 'pad-gateway.json'), JSON.stringify({
    schema: 'pack-agent.pad-gateway/v1',
    url: 'http://127.0.0.1:60932/api',
    token: 'a'.repeat(32),
    pid: 14664,
    profile: 'dsh-tui',
  }))

  const ad = loadAd(dir)
  if (ad.url !== 'http://127.0.0.1:60932/api') fail(`url ${ad.url}`)
  if (ad.token.length !== 32) fail('token')
  if (pingUrl(ad) !== 'http://127.0.0.1:60932/pad/ping') fail(`ping ${pingUrl(ad)}`)

  const pub = publicAd(ad)
  if ('token' in pub) fail('token leaked in publicAd')
  if (pub.hasToken !== true) fail('hasToken')
  if (pub.pid !== 14664) fail('pid')

  writeFileSync(join(dir, 'bad.json'), JSON.stringify({
    schema: 'pack-agent.pad-gateway/v1',
    url: 'http://example.com/api',
    token: 'a'.repeat(32),
    pid: 1,
  }))
  let threw = false
  try {
    loadAd(join(dir, 'missing-home'))
  } catch {
    threw = true
  }
  if (!threw) fail('missing home should throw')

  writeFileSync(join(dir, 'pad-gateway.json'), JSON.stringify({
    schema: 'pack-agent.pad-gateway/v1',
    url: 'http://8.8.8.8:80/api',
    token: 'a'.repeat(32),
    pid: 1,
  }))
  threw = false
  try {
    loadAd(dir)
  } catch (e) {
    threw = String(e).includes('loopback')
  }
  if (!threw) fail('non-loopback ad must be rejected as loopback')
} finally {
  rmSync(dir, { recursive: true, force: true })
}

console.log('✓ ad')
