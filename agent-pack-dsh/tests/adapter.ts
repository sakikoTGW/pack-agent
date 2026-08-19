#!/usr/bin/env bun
/**
 * DeepSeek Harness (dsh) adapter: detect → .dsh/skills → cordis MCP overlay.
 * 对照源码：packages/skill/skill-filesystem（项目根 .dsh/skills rank 100）
 * 与 examples/mcp-memory/*.cordis.yml（--patch insert + dsh-mcp-client）。
 */
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import { detectRuntimes, getAdapter, scanRuntime } from '../../src/adapters.js'
import { validateExperienceAdapterCoverage } from '../../src/experience-projection.js'
import { installPack } from '../../src/install.js'
import { mcpTargetFor } from '../../src/projection.js'
import type { PackDoc } from '../../src/types.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const adapter = getAdapter('dsh')
if (!adapter) fail('adapter dsh missing')
if (adapter.label !== 'DeepSeek Harness') fail(`unexpected label ${adapter.label}`)
if (!adapter.verified) fail('dsh adapter should be source-verified')
if (!adapter.skills.some(s => s.token === '.dsh/skills')) fail('skills must include .dsh/skills')
if (!adapter.detect.includes('.dsh')) fail('detect must include .dsh')
if (!adapter.mcp.some(m => m.token === '.dsh/agent-pack.cordis.yml')) {
  fail('mcp target must be .dsh/agent-pack.cordis.yml')
}
console.log('✓ adapter table')

const coverageGap = validateExperienceAdapterCoverage()
if (coverageGap.includes('dsh')) fail(`experience coverage missing dsh: ${coverageGap.join(',')}`)
console.log('✓ experience slot covers dsh')

const mcpT = mcpTargetFor('dsh', packTestTmp('dsh-proj'))
if (!mcpT.projectLocal) fail('dsh MCP must be project-local by default')
if (!mcpT.absFile.replace(/\\/g, '/').endsWith('.dsh/agent-pack.cordis.yml')) {
  fail(`mcpTargetFor dsh path ${mcpT.absFile}`)
}
if (mcpT.format !== 'yaml-cordis-patch') fail(`unexpected mcp format ${mcpT.format}`)
console.log('✓ mcpTargetFor dsh')

const root = await mkdtemp(packTestTmp('pack-dsh-'))
try {
  await mkdir(join(root, '.dsh'), { recursive: true })
  const detected = await detectRuntimes(root)
  if (!detected.includes('dsh')) fail(`detect missed dsh: ${detected.join(',')}`)
  console.log('✓ detect .dsh')

  await mkdir(join(root, '.dsh', 'skills', 'already'), { recursive: true })
  await writeFile(
    join(root, '.dsh', 'skills', 'already', 'SKILL.md'),
    '---\nname: already\ndescription: pre-existing dsh skill\n---\n# already\n',
    'utf8',
  )
  const scanned = await scanRuntime(root, adapter)
  if (!scanned.skills.some(s => s.name === 'already')) fail('scanRuntime missed .dsh/skills/already')
  console.log('✓ scan .dsh/skills')

  const pack: PackDoc = {
    schema: 'ccui-pack/v0.2',
    name: 'dsh-demo',
    version: '0.1.0',
    knowledge: {
      skills: [{ name: 'demo', source: 'bundled', version: '0.1.0' }],
      rules: [{ name: 'AGENTS.md', format: 'agents-md', ref: 'AGENTS.md' }],
    },
    tools: {
      mcp: [
        {
          name: 'demo-fs',
          type: 'stdio',
          command: 'npx',
          args: ['-y', '@modelcontextprotocol/server-filesystem', '.'],
        },
      ],
    },
    bundle: {
      portable: true,
      files: [
        {
          path: 'skills/demo/SKILL.md',
          content: '---\nname: demo\ndescription: dsh install smoke\n---\n# demo\n\nDSH_SKILL_MARKER\n',
        },
        {
          path: 'rules/AGENTS.md',
          content: '# agents\nDSH_RULE_MARKER\n',
        },
      ],
    },
    meta: { fidelity: 'L1', source: 'test-fixture' },
  }

  const report = await installPack(root, pack, {
    runtime: 'dsh',
    noBootstrap: true,
    bootstrapMcp: false,
    onConflict: 'replace',
  })
  if (!report.ok) fail(`install not ok projected=${report.projected} skipped=${JSON.stringify(report.skipped)}`)
  if (!report.projected.includes('dsh')) fail(`projected ${report.projected.join(',')}`)

  const skillMd = await readFile(join(root, '.dsh', 'skills', 'demo', 'SKILL.md'), 'utf8')
  if (!skillMd.includes('DSH_SKILL_MARKER')) fail('skill not written to .dsh/skills/demo')
  console.log('✓ install skill → .dsh/skills')

  const agents = await readFile(join(root, 'AGENTS.md'), 'utf8')
  if (!agents.includes('DSH_RULE_MARKER')) fail('rule not appended to AGENTS.md')
  console.log('✓ install rule → AGENTS.md')

  const overlay = await readFile(join(root, '.dsh', 'agent-pack.cordis.yml'), 'utf8')
  if (!overlay.includes('@deepseek-ai/dsh-mcp-client')) fail('overlay missing dsh-mcp-client')
  if (!overlay.includes('serverName: demo-fs') && !overlay.includes('serverName: demo-fs')) {
    // yaml may quote
    if (!/serverName:\s*['"]?demo-fs['"]?/.test(overlay)) fail(`overlay missing serverName demo-fs:\n${overlay}`)
  }
  if (!overlay.includes('insert:')) fail('overlay must be a cordis insert patch')
  if (overlay.includes('mcpServers')) fail('must not write fake mcpServers JSON for DSH')
  console.log('✓ MCP → cordis insert overlay')

  const notes = report.notes ?? []
  if (!notes.some(n => n.includes('--patch') || n.includes('cordis.patch.yml'))) {
    fail(`install notes must tell user how to apply overlay: ${JSON.stringify(notes)}`)
  }
  console.log('✓ install notes mention --patch')
} finally {
  await rm(root, { recursive: true, force: true })
}

console.log('[OK] dsh-adapter')
