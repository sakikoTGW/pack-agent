#!/usr/bin/env bun
/**
 * 三套 harness 配置并行：Cursor / Claude Code / DeepSeek Harness。
 * 各自独立目录，Promise.all 同时 detect + install；DSH 再跑 map/allow/search。
 */
import { access, mkdir, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { detectRuntimes } from '../src/adapters.js'
import { installPackFile } from '../src/install.js'
import { catalogSearch, mapPackToDsh } from '../dsh-modpack/catalog.js'
import { loadPackDoc } from '../dsh-modpack/compile.js'
import { packTestTmp } from './tmp-root.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const fixture = join(import.meta.dir, 'fixtures', 'demo.pack.json')
const stamp = Date.now()

async function mustExist(path: string, label: string): Promise<void> {
  try {
    await access(path)
  } catch {
    fail(`${label} missing ${path}`)
  }
}

async function runCursor(): Promise<string> {
  const root = packTestTmp(`mh-cursor-${stamp}`)
  await mkdir(join(root, '.cursor', 'rules'), { recursive: true })
  await writeFile(join(root, '.cursor', 'mcp.json'), '{"mcpServers":{}}\n', 'utf8')
  await writeFile(join(root, '.cursor', 'rules', 'always.mdc'), '---\nalwaysApply: true\n---\n# always\n', 'utf8')
  const detected = await detectRuntimes(root)
  if (!detected.includes('cursor')) fail(`cursor detect got ${detected.join(',')}`)
  const report = await installPackFile(root, fixture, { runtime: 'cursor', noBootstrap: true, onConflict: 'replace' })
  if (!report.projected.includes('cursor')) fail(`cursor projected ${report.projected.join(',')}`)
  await mustExist(join(root, '.cursor', 'skills', 'demo', 'SKILL.md'), 'cursor skill')
  return `cursor detect=${detected.join(',')} skill=.cursor/skills/demo`
}

async function runClaudeCode(): Promise<string> {
  const root = packTestTmp(`mh-claude-${stamp}`)
  await mkdir(join(root, '.claude'), { recursive: true })
  await writeFile(join(root, 'CLAUDE.md'), '# claude\n', 'utf8')
  const detected = await detectRuntimes(root)
  if (!detected.includes('claude-code')) fail(`claude-code detect got ${detected.join(',')}`)
  const report = await installPackFile(root, fixture, { runtime: 'claude-code', noBootstrap: true, onConflict: 'replace' })
  if (!report.projected.includes('claude-code')) fail(`claude projected ${report.projected.join(',')}`)
  await mustExist(join(root, '.claude', 'skills', 'demo', 'SKILL.md'), 'claude skill')
  return `claude-code detect=${detected.join(',')} skill=.claude/skills/demo`
}

async function runDsh(): Promise<string> {
  const root = packTestTmp(`mh-dsh-${stamp}`)
  await mkdir(join(root, '.dsh', 'skills'), { recursive: true })
  const detected = await detectRuntimes(root)
  if (!detected.includes('dsh')) fail(`dsh detect got ${detected.join(',')}`)
  const report = await installPackFile(root, fixture, { runtime: 'dsh', noBootstrap: true, onConflict: 'replace' })
  if (!report.projected.includes('dsh')) fail(`dsh projected ${report.projected.join(',')}`)
  await mustExist(join(root, '.dsh', 'skills', 'demo', 'SKILL.md'), 'dsh adapter skill')
  const pack = await loadPackDoc(fixture)
  const mapped = await mapPackToDsh(pack, root, { allow: true })
  await mustExist(join(mapped.dir, 'mods', 'demo', 'mod.json'), 'dsh mods/demo')
  const hits = await catalogSearch(root, 'demo')
  if (!hits.some(h => h.kind === 'skill' && h.name === 'demo')) {
    fail(`dsh search missed demo: ${JSON.stringify(hits)}`)
  }
  return `dsh detect=${detected.join(',')} adapter=.dsh/skills/demo map=${mapped.id}`
}

const started = Date.now()
const rows = await Promise.all([runCursor(), runClaudeCode(), runDsh()])
for (const row of rows) console.log(`✓ ${row}`)
console.log(`✓ three harness configs ran concurrently in ${Date.now() - started}ms`)
console.log('[OK] multi-harness-parallel')
