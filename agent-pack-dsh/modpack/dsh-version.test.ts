#!/usr/bin/env bun
/**
 * dsh.version：精确号不在已发布列表 PA012；range 取最高满足号。
 */
import { resolveDshVersion, DshVersionError } from './dsh-version.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const published = ['0.1.0-rc.3', '0.1.0-rc.6', '0.1.0']

const exact = resolveDshVersion('0.1.0-rc.6', published)
if (exact !== '0.1.0-rc.6') fail(`exact ${exact}`)
console.log('✓ exact published')

try {
  resolveDshVersion('0.9.9-rc.1', published)
  fail('missing exact must PA012')
} catch (e) {
  if (!(e instanceof DshVersionError) || e.code !== 'PA012') fail(`expected PA012, got ${e}`)
}
console.log('✓ exact missing PA012')

try {
  resolveDshVersion('', published)
  fail('empty must PA012')
} catch (e) {
  if (!(e instanceof DshVersionError) || e.code !== 'PA012') fail(`expected PA012, got ${e}`)
}
console.log('✓ empty PA012')

const ranged = resolveDshVersion('>=0.1.0-rc.3 <0.1.0', published)
if (ranged !== '0.1.0-rc.6') fail(`range with prerelease comparator should pick highest rc, got ${ranged}`)
console.log('✓ range picks highest prerelease')

const stable = resolveDshVersion('^0.1.0', published)
if (stable !== '0.1.0') fail(`^0.1.0 must not pick prerelease, got ${stable}`)
console.log('✓ caret skips prerelease')

try {
  resolveDshVersion('^2.0.0', published)
  fail('unmatched range must PA012')
} catch (e) {
  if (!(e instanceof DshVersionError) || e.code !== 'PA012') fail(`expected PA012, got ${e}`)
}
console.log('✓ unmatched range PA012')

console.log('✓ dsh-version')
