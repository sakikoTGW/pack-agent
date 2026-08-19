#!/usr/bin/env bun
/**
 * projectKey / encodeSegment：与 DSH format.ts UTF-16 code unit 规则对齐。
 */
import { encodeSegment, projectDir, projectKey } from './project-key.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

if (encodeSegment('.') !== '~002E') fail(`dot ${encodeSegment('.')}`)
if (encodeSegment('..') !== '~002E~002E') fail(`dotdot ${encodeSegment('..')}`)
try {
  encodeSegment('')
  fail('empty encodeSegment must throw')
} catch {
  /* expected */
}
if (encodeSegment('项目') !== '~9879~76EE') fail(`zh ${encodeSegment('项目')}`)
if (encodeSegment('😀') !== '~D83D~DE00') fail(`emoji ${encodeSegment('😀')}`)
console.log('✓ encodeSegment golden')

if (projectKey('E:\\pack-agent') !== '--E-pack-agent--') {
  fail(`win path ${projectKey('E:\\pack-agent')}`)
}
if (projectKey('E:/pack-agent') !== '--E-pack-agent--') fail(`slash path ${projectKey('E:/pack-agent')}`)
if (projectKey('项目') !== '--~9879~76EE--') fail(`zh key ${projectKey('项目')}`)
if (projectKey(':::') !== '--root--') fail(`seps ${projectKey(':::')}`)
if (projectDir('/sessions', undefined) !== '/sessions/_no-cwd' && !projectDir('C:\\s', undefined).endsWith('_no-cwd')) {
  fail(`no-cwd ${projectDir('C:\\s', undefined)}`)
}
const long = 'a'.repeat(300)
const sliced = projectKey(long)
if (!sliced.startsWith('--') || !sliced.endsWith('--')) fail(`wrap ${sliced}`)
if (sliced.length !== 2 + 251 + 2) fail(`trunc len ${sliced.length}`)
if (sliced.slice(2, -2) !== 'a'.repeat(251)) fail('trunc content')
try {
  projectKey('')
  fail('empty projectKey must throw')
} catch {
  /* expected */
}
console.log('✓ projectKey golden')
