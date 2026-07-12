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

  // pack-a 和 pack-b 都是从同一个 src 导出的，skill 内容逐字节相同、contentHash 相同，
  // 只是包名不同——就像两个 MC 整合包都塞了同一个 JEI.jar。这不该报冲突，
  // 哪怕 on_conflict 是默认的 stop（否则任何"两个包共享一个 skill"的场景都装不上第二个包）。
  const twoPack = join(root, 'two-pack-same-content')
  await fs.mkdir(join(twoPack, '.claude'), { recursive: true })
  await fs.writeFile(join(twoPack, 'CLAUDE.md'), '# two-pack\n', 'utf8')
  await installPackFile(twoPack, packA, { noBootstrap: true, runtimes: ['claude-code'], bootstrapMcp: false })
  const r3 = await installPackFile(twoPack, packB, {
    noBootstrap: true,
    runtimes: ['claude-code'],
    bootstrapMcp: false,
    // 故意不传 onConflict —— 默认 stop，验证「内容相同」根本不会走到冲突这一步
  })
  if (!r3.ok) fail('installing a second pack with byte-identical skill content must not conflict (default stop policy)')
  console.log('✓ two packs, identical skill content, different pack name → no conflict')

  process.exit(0)
} finally {
  await fs.rm(root, { recursive: true, force: true }).catch(() => {})
}
