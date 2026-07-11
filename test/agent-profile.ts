#!/usr/bin/env bun
/** agents.yaml profiles: --agent export + agent-required guard */
import { join } from 'node:path'
import { mkdtemp, rm, writeFile, mkdir, readFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { exportPackFromProject } from '../src/export.js'
import { PackConflictError } from '../src/errors.js'

const repo = join(import.meta.dir, '..')

// Dev harness shim for packer profile (skill lives under skills/)
await mkdir(join(repo, '.claude', 'skills', 'agent-pack'), { recursive: true })
await writeFile(
  join(repo, '.claude', 'skills', 'agent-pack', 'SKILL.md'),
  await readFile(join(repo, 'skills', 'agent-pack', 'SKILL.md'), 'utf8'),
)

async function expectAgentRequired(cwd: string): Promise<void> {
  try {
    await exportPackFromProject(cwd, { name: 'should-fail' })
    console.error('✗ export without --agent should fail')
    process.exit(1)
  } catch (e) {
    if (!(e instanceof PackConflictError) || e.detail.kind !== 'agent-required') {
      throw e
    }
    console.log('✓ bare export rejected (agent-required)')
  }
}

await expectAgentRequired(repo)

const { pack, stats } = await exportPackFromProject(repo, { agent: 'packer' })
const skillNames = (pack.knowledge?.skills ?? []).map(s => s.name)
const checks: [string, boolean][] = [
  ['agent.id=packer', pack.agent?.id === 'packer'],
  ['author=pack-agent', pack.author === 'pack-agent'],
  ['description set', Boolean(pack.description?.includes('modpack') || pack.description?.includes('Modpack'))],
  ['includes agent-pack skill', skillNames.includes('agent-pack')],
  ['small skill set', (stats.skills as number) <= 3],
  ['pack name defaults to agent id', pack.name === 'packer'],
]

let failed = 0
for (const [name, ok] of checks) {
  console.log(ok ? `✓ ${name}` : `✗ ${name}`)
  if (!ok) failed++
}

const tmp = await mkdtemp(join(tmpdir(), 'pack-agent-'))
try {
  await mkdir(join(tmp, '.agents', 'skills', 'solo'), { recursive: true })
  await writeFile(join(tmp, 'AGENTS.md'), '# solo\n', 'utf8')
  await writeFile(
    join(tmp, '.agents', 'skills', 'solo', 'SKILL.md'),
    '---\nname: solo\ndescription: only one\n---\n# solo\n',
    'utf8',
  )
  await mkdir(join(tmp, '.agent-pack'), { recursive: true })
  await writeFile(
    join(tmp, '.agent-pack', 'agents.yaml'),
    `schema: agent-pack/agents/v1
agents:
  solo:
    author: Tester
    description: Single skill agent
    skills: [solo]
`,
    'utf8',
  )

  await expectAgentRequired(tmp)

  const { pack: solo } = await exportPackFromProject(tmp, { agent: 'solo', noBootstrap: true })
  if ((solo.knowledge?.skills?.length ?? 0) !== 1) {
    console.error('✗ solo agent should export exactly 1 skill')
    failed++
  } else {
    console.log('✓ temp project solo agent → 1 skill')
  }
} finally {
  await rm(tmp, { recursive: true, force: true })
}

process.exit(failed ? 1 : 0)
