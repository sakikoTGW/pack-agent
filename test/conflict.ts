#!/usr/bin/env bun
/** Conflict policy: stop / skip / replace */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { exportPackFromProject } from '../src/export.js'
import { installPackFile } from '../src/install.js'
import { PackConflictError } from '../src/errors.js'
import { writeSkillOriginMarker } from '../src/markers.js'

function fail(msg: string): never {
  console.error(`[FAIL] ${msg}`)
  process.exit(1)
}

async function expectConflict(label: string, fn: () => Promise<unknown>): Promise<void> {
  try {
    await fn()
    fail(`${label}: expected PackConflictError`)
  } catch (e) {
    if (!(e instanceof PackConflictError)) throw e
    console.log(`✓ ${label}`)
    if (!e.message.includes('= choices:')) fail(`${label}: missing choices in message`)
    if (!e.choices.includes('skip') || !e.choices.includes('replace')) {
      fail(`${label}: missing skip/replace choices`)
    }
  }
}

const root = join(tmpdir(), `pack-conflict-${Date.now()}`)
const src = join(root, 'src')
const target = join(root, 'target')

try {
  await fs.mkdir(join(src, '.claude', 'skills', 'demo'), { recursive: true })
  await fs.writeFile(join(src, 'CLAUDE.md'), '# src\n', 'utf8')
  await fs.writeFile(
    join(src, '.claude', 'skills', 'demo', 'SKILL.md'),
    '---\nname: demo\ndescription: d\n---\n# demo v1\n',
    'utf8',
  )

  const { outPath: packA } = await exportPackFromProject(src, {
    name: 'pack-a',
    runtime: 'claude-code',
    noBootstrap: true,
    allowFullScan: true,
  })
  const { outPath: packB } = await exportPackFromProject(src, {
    name: 'pack-b',
    runtime: 'claude-code',
    noBootstrap: true,
    allowFullScan: true,
  })

  const handDir = join(root, 'handcrafted')
  await fs.mkdir(join(handDir, '.claude', 'skills', 'demo'), { recursive: true })
  await fs.writeFile(join(handDir, 'CLAUDE.md'), '# h\n', 'utf8')
  await fs.writeFile(
    join(handDir, '.claude', 'skills', 'demo', 'SKILL.md'),
    '---\nname: demo\ndescription: hand\n---\n# hand\n',
    'utf8',
  )

  await expectConflict('stop: handcrafted blocks', () =>
    installPackFile(handDir, packA, { noBootstrap: true, runtimes: ['claude-code'], bootstrapMcp: false }),
  )

  const skipReport = await installPackFile(handDir, packA, {
    noBootstrap: true,
    runtimes: ['claude-code'],
    bootstrapMcp: false,
    onConflict: 'skip',
  })
  if (!skipReport.conflictsResolved?.some(c => c.action === 'skip')) {
    fail('skip policy should record conflictsResolved')
  }
  const handAfterSkip = await fs.readFile(join(handDir, '.claude', 'skills', 'demo', 'SKILL.md'), 'utf8')
  if (!handAfterSkip.includes('# hand')) fail('skip must keep handcrafted content')
  console.log('✓ skip: keeps existing skill')

  await fs.rm(join(handDir, '.claude', 'skills', 'demo'), { recursive: true, force: true })
  await fs.mkdir(join(handDir, '.claude', 'skills', 'demo'), { recursive: true })
  await fs.writeFile(
    join(handDir, '.claude', 'skills', 'demo', 'SKILL.md'),
    '---\nname: demo\ndescription: hand\n---\n# hand\n',
    'utf8',
  )

  const replaceReport = await installPackFile(handDir, packA, {
    noBootstrap: true,
    runtimes: ['claude-code'],
    bootstrapMcp: false,
    onConflict: 'replace',
  })
  if (!replaceReport.conflictsResolved?.some(c => c.action === 'replace')) {
    fail('replace policy should record conflictsResolved')
  }
  const handAfterReplace = await fs.readFile(join(handDir, '.claude', 'skills', 'demo', 'SKILL.md'), 'utf8')
  if (!handAfterReplace.includes('# demo v1')) fail('replace must overwrite with pack skill')
  console.log('✓ replace: overwrites handcrafted skill')

  await fs.mkdir(join(target, '.claude'), { recursive: true })
  await fs.writeFile(join(target, 'CLAUDE.md'), '# t\n', 'utf8')
  await installPackFile(target, packA, {
    noBootstrap: true,
    runtimes: ['claude-code'],
    bootstrapMcp: false,
  })

  await writeSkillOriginMarker(join(target, '.claude', 'skills', 'demo'), {
    packName: 'other-pack',
    skillName: 'demo',
  })
  await expectConflict('stop: different owner blocks', () =>
    installPackFile(target, packB, { noBootstrap: true, runtimes: ['claude-code'], bootstrapMcp: false }),
  )

  await writeSkillOriginMarker(join(target, '.claude', 'skills', 'demo'), {
    packName: 'pack-a',
    skillName: 'demo',
  })
  const r2 = await installPackFile(target, packA, {
    noBootstrap: true,
    runtimes: ['claude-code'],
    bootstrapMcp: false,
  })
  if (!r2.ok) fail('idempotent reinstall should succeed')
  console.log('✓ idempotent same-pack reinstall')

  process.exit(0)
} finally {
  await fs.rm(root, { recursive: true, force: true }).catch(() => {})
}
