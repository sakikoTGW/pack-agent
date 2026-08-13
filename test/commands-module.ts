#!/usr/bin/env bun
/** commands 模块：export 含 slash command → install 落到 .cursor/commands 与 .claude/commands */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import { exportPackFromProject } from '../src/export.js'
import { packTestTmp } from './tmp-root.js'
import { installPackFile } from '../src/install.js'

function fail(msg: string): never {
  console.error(`[FAIL] ${msg}`)
  process.exit(1)
}

const MARKER = 'CMD_MODULE_MARKER_x7q'
const root = packTestTmp(`pack-cmd-${Date.now()}`)
const dirA = join(root, 'A')
const dirB = join(root, 'B')

try {
  await fs.mkdir(join(dirA, '.cursor', 'commands'), { recursive: true })
  await fs.mkdir(join(dirA, '.claude', 'skills', 'tiny'), { recursive: true })
  await fs.writeFile(
    join(dirA, '.cursor', 'commands', 'DEMO.md'),
    `# Demo command\n${MARKER}\n`,
    'utf8',
  )
  await fs.writeFile(
    join(dirA, '.claude', 'skills', 'tiny', 'SKILL.md'),
    `---\nname: tiny\ndescription: t\n---\n# t\n`,
    'utf8',
  )

  const { zipPath } = await exportPackFromProject(dirA, {
    name: 'cmd-pack',
    runtime: 'claude-code',
    noBootstrap: true,
    allowFullScan: true,
    select: { name: 'cmd-pack', skills: ['tiny'], rules: [], mcp: [] },
    modules: { commands: true, skills: true, rules: false, mcp: false, experiences: false },
  })

  const { readPackZip } = await import('../src/pack-archive.js')
  const { pack } = await readPackZip(zipPath)
  if (!(pack.commands?.files ?? []).some(c => c.name === 'DEMO')) fail('pack missing DEMO command entry')
  if (!pack.bundle?.files?.some(f => f.path === 'commands/DEMO.md' && f.content.includes(MARKER))) {
    fail('bundle missing commands/DEMO.md')
  }

  await fs.mkdir(dirB, { recursive: true })
  const report = await installPackFile(dirB, zipPath, {
    runtime: 'claude-code',
    noBootstrap: true,
    onConflict: 'replace',
    modules: { commands: true, skills: true },
  })
  if (!report.ok) fail(`install failed ${JSON.stringify(report.skipped)}`)

  const cursorCmd = await fs.readFile(join(dirB, '.cursor', 'commands', 'DEMO.md'), 'utf8')
  const claudeCmd = await fs.readFile(join(dirB, '.claude', 'commands', 'DEMO.md'), 'utf8')
  if (!cursorCmd.includes(MARKER) || !claudeCmd.includes(MARKER)) fail('command not installed to both harness dirs')

  console.log('[OK] commands-module')
  await fs.rm(root, { recursive: true, force: true }).catch(() => {})
  process.exit(0)
} catch (e) {
  console.error(e)
  process.exit(1)
}
