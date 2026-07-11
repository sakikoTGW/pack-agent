#!/usr/bin/env bun
/** A→B portable round-trip: export with bundle → install on fresh dir */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { exportPackFromProject } from '../src/export.js'
import { installPack, installPackFile } from '../src/install.js'
import { loadExperienceInjection } from '../src/experience-loader.js'
import type { PackDoc } from '../src/types.js'

const MARKER_SKILL = 'PORTABLE_AB_SKILL_MARKER_7f3a'
const MARKER_EXP = 'PORTABLE_AB_EXPERIENCE_INJECT_9c2e'

function fail(msg: string): never {
  console.error(`[FAIL] ${msg}`)
  process.exit(1)
}

const root = join(tmpdir(), `pack-portable-ab-${Date.now()}`)
const dirA = join(root, 'A')
const dirB = join(root, 'B')

try {
  await fs.mkdir(join(dirA, '.claude', 'skills', 'portable-demo'), { recursive: true })
  await fs.writeFile(join(dirA, 'CLAUDE.md'), '# project A\n', 'utf8')
  await fs.writeFile(join(dirA, 'AGENTS.md'), '# agents A\n', 'utf8')
  await fs.writeFile(
    join(dirA, '.claude', 'skills', 'portable-demo', 'SKILL.md'),
    `---
name: portable-demo
description: Portable AB test skill
---
# Portable demo

Unique marker: ${MARKER_SKILL}
`,
    'utf8',
  )

  const { pack, outPath } = await exportPackFromProject(dirA, {
    name: 'portable-ab',
    runtime: 'claude-code',
    noBootstrap: true,
    allowFullScan: true,
  })

  if (!pack.bundle?.portable || !(pack.bundle.files?.length ?? 0)) {
    fail('export must produce portable bundle.files')
  }
  const bundledSkill = pack.bundle.files!.some(
    f => f.path.includes('portable-demo') && f.content.includes(MARKER_SKILL),
  )
  if (!bundledSkill) fail('bundle must embed portable-demo skill content')

  const packWithExp: PackDoc = {
    ...pack,
    experiences: [
      {
        id: 'portable-ab-exp',
        name: 'portable-ab-exp',
        version: '1.0.0',
        kind: 'distill',
        scope: 'session',
        ttl: 'session',
        source: 'capture',
        harness: {
          base_system_prompt: `You carry this distilled habit: ${MARKER_EXP}`,
          tool_schemas: [],
          system_reminders: [],
        },
      },
    ],
    policy: { captureAs: 'experience' },
    harness: { base_system_prompt: '', tool_schemas: [], system_reminders: [] },
  }
  const expPackPath = join(dirA, '.agent-pack', 'exports', 'portable-ab-exp.pack.json')
  await fs.mkdir(join(dirA, '.agent-pack', 'exports'), { recursive: true })
  await fs.writeFile(expPackPath, JSON.stringify(packWithExp, null, 2), 'utf8')

  await fs.mkdir(join(dirB, '.claude'), { recursive: true })
  await fs.writeFile(join(dirB, 'CLAUDE.md'), '# project B\n', 'utf8')
  await fs.writeFile(join(dirB, 'AGENTS.md'), '# agents B\n', 'utf8')

  const reportB = await installPackFile(dirB, expPackPath, {
    noBootstrap: true,
    runtimes: ['claude-code', 'codex'],
    bootstrapMcp: false,
  })

  const claudeSkill = join(dirB, '.claude', 'skills', 'portable-demo', 'SKILL.md')
  const codexSkill = join(dirB, '.agents', 'skills', 'portable-demo', 'SKILL.md')
  const claudeText = await fs.readFile(claudeSkill, 'utf8')
  const codexText = await fs.readFile(codexSkill, 'utf8')

  if (!claudeText.includes(MARKER_SKILL)) fail('B: claude skill missing marker')
  if (!codexText.includes(MARKER_SKILL)) fail('B: codex skill missing marker')

  const injection = await loadExperienceInjection(dirB, '.agent-pack')
  if (!injection.systemPromptDelta.includes(MARKER_EXP)) {
    fail('B: experience injection text missing marker')
  }

  const hookScript = join(dirB, '.agent-pack', 'bin', 'experience-session-hook.cjs')
  await fs.access(hookScript)

  let claudeHasHook = false
  try {
    const s = JSON.parse(await fs.readFile(join(dirB, '.claude', 'settings.json'), 'utf8'))
    claudeHasHook = JSON.stringify(s.hooks ?? {}).includes('experience-session-hook')
  } catch {
    /* */
  }

  const reportB2 = await installPack(dirB, packWithExp, {
    noBootstrap: true,
    runtimes: ['claude-code'],
    bootstrapMcp: false,
  })
  const sameDirErr = (reportB2.skipped ?? []).some(s =>
    s.includes('Source and destination must not be the same'),
  )
  if (sameDirErr) fail('re-install must not fail on same-directory skill copy')

  const checks: [string, boolean][] = [
    ['A export portable bundle', Boolean(pack.bundle?.portable)],
    ['B install ok', reportB.ok],
    ['B claude skill', claudeText.includes(MARKER_SKILL)],
    ['B codex skill', codexText.includes(MARKER_SKILL)],
    ['B experience inject', injection.systemPromptDelta.includes(MARKER_EXP)],
    ['B experience hook wired', (reportB.experienceHooks?.length ?? 0) > 0],
    ['B claude SessionStart hook', claudeHasHook],
    ['B re-install same-dir skip', !sameDirErr],
  ]

  let failed = 0
  for (const [name, ok] of checks) {
    console.log(ok ? `✓ ${name}` : `✗ ${name}`)
    if (!ok) failed++
  }

  console.log('\npack:', outPath)
  console.log('B projected:', reportB.projected.join(', '))
  if (reportB.experienceHooks?.length) {
    console.log('B hooks:', reportB.experienceHooks.join(', '))
  }

  process.exit(failed ? 1 : 0)
} finally {
  await fs.rm(root, { recursive: true, force: true }).catch(() => {})
}
