#!/usr/bin/env bun
/**
 * 该实例 Harness 内的 agent-preset 名册：用户层 copy/remove，随附层只读。
 */
import { mkdirSync, writeFileSync, existsSync, rmSync } from 'node:fs'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { createInstance, ensureRoot, seedFakeVersion, LauncherError } from './launcher.js'
import {
  copyAgentPreset,
  listAgentPresets,
  removeAgentPreset,
  shippedPresetRoot,
  userPresetRoot,
} from './agent-preset-ops.js'
import { listSessions } from './session-ops.js'
import { encodeSegment, projectKey } from './project-key.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const fakeJs = `const args = process.argv.slice(2)
if (args.includes('--version') || args.includes('-V')) {
  process.stdout.write('@@VERSION@@\\n')
  process.exit(0)
}
if (args.includes('--dump-config')) {
  process.stdout.write('- id: session-persistence-jsonl\\n')
  process.exit(0)
}
process.exit(0)
`

const tmp = packTestTmp(`agent-preset-${Date.now()}`)
const root = ensureRoot(join(tmp, 'launcher'))
seedFakeVersion(root, '0.1.0-rc.6', fakeJs)
const inst = createInstance(root, { name: 'ap', version: '0.1.0-rc.6', profile: 'web' })

const shipped = shippedPresetRoot(root, '0.1.0-rc.6')
mkdirSync(join(shipped, 'standard'), { recursive: true })
writeFileSync(join(shipped, 'standard', 'agent.cordis.yml'), '- name: demo\n')

const listed0 = listAgentPresets(root, inst.id)
if (!listed0.some((p) => p.id === 'standard' && p.trust === 'system')) {
  fail(`shipped missing ${JSON.stringify(listed0)}`)
}

const copied = copyAgentPreset(root, inst.id, 'standard', 'mine')
if (copied.id !== 'mine' || copied.trust !== 'user') fail(`copy ${JSON.stringify(copied)}`)
if (!existsSync(join(userPresetRoot(inst.home), 'mine', 'agent.cordis.yml'))) fail('user composition missing')

try {
  copyAgentPreset(root, inst.id, 'standard', 'mine')
  fail('copy overwrite must fail')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA113') fail(`expected PA113 exists, got ${e}`)
}

try {
  copyAgentPreset(root, inst.id, 'standard', 'Bad_Id')
  fail('invalid id must fail')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA113') fail(`expected PA113 id, got ${e}`)
}

try {
  removeAgentPreset(root, inst.id, 'standard')
  fail('remove shipped must fail')
} catch (e) {
  if (!(e instanceof LauncherError) || e.diagnostic?.code !== 'PA112') fail(`expected PA112, got ${e}`)
}

removeAgentPreset(root, inst.id, 'mine')
if (listAgentPresets(root, inst.id).some((p) => p.id === 'mine')) fail('user preset still listed')

const slug = projectKey(inst.workspace.path)
const sidDir = join(inst.home, 'sessions', slug, encodeSegment('talk-p'))
mkdirSync(sidDir, { recursive: true })
writeFileSync(
  join(sidDir, 'session.jsonl'),
  [
    JSON.stringify({
      type: 'session',
      version: 0,
      id: 'talk-p',
      createdAt: 1,
      cwd: inst.workspace.path,
      dshVersion: '0.1.0-rc.6',
      agentPreset: 'standard',
    }),
    JSON.stringify({ type: 'agent-preset/selected', data: { agentPreset: 'mine' } }),
  ].join('\n') + '\n',
)
const sessions = listSessions(root, inst.id)
const row = sessions.value.find((s) => s.id === 'talk-p')
if (row?.agentPreset !== 'mine') fail(`agentPreset last-wins ${JSON.stringify(row)}`)

rmSync(tmp, { recursive: true, force: true })
console.log('✓ agent-preset roster + session.agentPreset')
