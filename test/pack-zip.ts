#!/usr/bin/env bun
/** export → .pack.zip（含实体 skills + mcp.json）→ install from zip */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import { spawnSync } from 'node:child_process'
import { packTestTmp } from './tmp-root.js'
import { exportPackFromProject } from '../src/export.js'
import { installPackFile } from '../src/install.js'
import { readPackZip } from '../src/pack-archive.js'

function fail(msg: string): never {
  console.error(`[FAIL] ${msg}`)
  process.exit(1)
}

const MARKER = 'PACK_ZIP_SKILL_MARKER_a91c'
const root = packTestTmp(`pack-zip-${Date.now()}`)
const dirA = join(root, 'A')
const dirB = join(root, 'B')

try {
  await fs.mkdir(join(dirA, '.claude', 'skills', 'zip-demo'), { recursive: true })
  await fs.writeFile(join(dirA, 'CLAUDE.md'), '# A\n', 'utf8')
  await fs.writeFile(
    join(dirA, '.claude', 'skills', 'zip-demo', 'SKILL.md'),
    `---
name: zip-demo
description: zip archive test
---
# Zip demo
${MARKER}
`,
    'utf8',
  )
  await fs.writeFile(
    join(dirA, '.mcp.json'),
    JSON.stringify(
      {
        mcpServers: {
          'zip-demo-mcp': { command: 'npx', args: ['-y', 'dummy-mcp'], env: { X: '1' } },
        },
      },
      null,
      2,
    ),
    'utf8',
  )

  const { pack, outPath, zipPath, jsonPath } = await exportPackFromProject(dirA, {
    name: 'zip-demo-pack',
    runtime: 'claude-code',
    noBootstrap: true,
    allowFullScan: true,
    select: { name: 'zip-demo-pack', skills: ['zip-demo'], mcp: ['zip-demo-mcp'], rules: [] },
  })

  if (!outPath.endsWith('.pack.zip')) fail(`primary outPath must be .pack.zip, got ${outPath}`)
  if (zipPath !== outPath) fail('zipPath must equal outPath')
  try {
    await fs.access(jsonPath)
  } catch {
    fail('compat .pack.json must still be written')
  }
  try {
    await fs.access(zipPath)
  } catch {
    fail('zip missing')
  }

  const listed = spawnSync('tar', ['-tf', zipPath], { encoding: 'utf8' })
  if (listed.status !== 0) fail(`tar -tf failed: ${listed.stderr}`)
  const entries = listed.stdout.split(/\r?\n/).filter(Boolean)
  const need = ['pack.json', 'mcp.json', 'INSTALL.md', 'skills/zip-demo/SKILL.md']
  for (const n of need) {
    if (!entries.some(e => e.replace(/^\.\//, '') === n || e.endsWith(n))) fail(`zip missing entry ${n}; have=${entries.slice(0, 20).join(',')}`)
  }

  const { pack: fromZip } = await readPackZip(zipPath)
  if (!fromZip.bundle?.files?.some(f => f.content.includes(MARKER))) fail('zip pack must carry skill content')
  if (!(fromZip.tools?.mcp ?? []).some(m => m.name === 'zip-demo-mcp')) fail('zip pack must carry mcp')

  await fs.mkdir(dirB, { recursive: true })
  const report = await installPackFile(dirB, zipPath, {
    runtime: 'claude-code',
    noBootstrap: true,
    onConflict: 'replace',
  })
  if (!report.ok) fail(`install from zip failed: ${JSON.stringify(report.skipped)}`)
  const skillBody = await fs.readFile(join(dirB, '.claude', 'skills', 'zip-demo', 'SKILL.md'), 'utf8')
  if (!skillBody.includes(MARKER)) fail('installed skill content mismatch')

  console.log('[OK] pack-zip: export zip + install from zip')
  await fs.rm(root, { recursive: true, force: true }).catch(() => {})
  process.exit(0)
} catch (e) {
  console.error(e)
  process.exit(1)
}
