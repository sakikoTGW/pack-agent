#!/usr/bin/env bun
/** Multi-harness install smoke test */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { installPackFile } from '../src/install.js'

const root = join(tmpdir(), `packagent-install-${Date.now()}`)
await fs.mkdir(root, { recursive: true })

await fs.mkdir(join(root, '.claude'), { recursive: true })
await fs.writeFile(join(root, 'CLAUDE.md'), '# test\n', 'utf8')
await fs.writeFile(join(root, 'AGENTS.md'), '# agents\n', 'utf8')

const packPath = join(import.meta.dir, 'fixtures', 'demo.pack.json')
const report = await installPackFile(root, packPath)

const claudeSkill = join(root, '.claude', 'skills', 'demo', 'SKILL.md')
const agentsSkill = join(root, '.agents', 'skills', 'demo', 'SKILL.md')

const checks = [
  ['projected.length >= 1', report.projected.length >= 1],
  ['claude skill', await fs.access(claudeSkill).then(() => true).catch(() => false)],
  ['codex skill', await fs.access(agentsSkill).then(() => true).catch(() => false)],
  ['manifest', await fs.access(join(root, '.agent-pack', 'applied', 'demo.json')).then(() => true).catch(() => false)],
]

let failed = 0
for (const [name, ok] of checks) {
  console.log(ok ? `✓ ${name}` : `✗ ${name}`)
  if (!ok) failed++
}

console.log('\nprojected:', report.projected.join(', '))
console.log('detected:', report.detected.join(', '))

await fs.rm(root, { recursive: true, force: true })
process.exit(failed ? 1 : 0)
