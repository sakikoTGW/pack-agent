#!/usr/bin/env bun
/**
 * 老版 Cursor/ccui-pack → DSH 整合包（mods/）。
 */
import { access, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { packTestTmp } from '../test/tmp-root.js'
import type { PackDoc } from '../src/types.js'
import { compilePackToDshBundle } from './compile.js'
import { mapLegacyPack } from './map.js'
import { mapPackToDsh } from './catalog.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const cursorPack: PackDoc = {
  schema: 'ccui-pack/v0.2',
  name: 'cursor-legacy',
  version: '0.2.0',
  runtime: { id: 'cursor', label: 'Cursor', verified: true },
  knowledge: {
    skills: [{ name: 'legacy-skill', source: 'cursor', ref: '.cursor/skills/legacy-skill' }],
  },
  tools: {
    mcp: [{ name: 'cursor-fs', command: 'npx', args: ['-y', '@modelcontextprotocol/server-filesystem', '.'] }],
  },
  bundle: {
    portable: true,
    files: [
      {
        path: '.cursor/skills/legacy-skill/SKILL.md',
        content: '---\nname: legacy-skill\ndescription: from cursor\n---\n# legacy\n\nCURSORTOKENSIG\n',
      },
      { path: '.cursor/rules/always.mdc', content: '# always\n' },
      { path: '.cursor/commands/hello.md', content: '# hello\n' },
      { path: '.cursor/hooks.json', content: '{"hooks":{}}\n' },
    ],
  },
}

const remapped = mapLegacyPack(cursorPack)
const skillFile = remapped.bundle?.files?.find(f => f.path.replace(/\\/g, '/').endsWith('legacy-skill/SKILL.md'))
if (!skillFile || skillFile.path.replace(/\\/g, '/') !== 'skills/legacy-skill/SKILL.md') {
  fail(`cursor skill path not remapped: ${skillFile?.path}`)
}
if (!remapped.bundle?.files?.some(f => f.path.replace(/\\/g, '/') === 'rules/always.mdc')) {
  fail('cursor rules not remapped to rules/')
}
if (!remapped.bundle?.files?.some(f => f.path.replace(/\\/g, '/') === 'commands/hello.md')) {
  fail('cursor commands not remapped')
}
if (!remapped.bundle?.files?.some(f => f.path.replace(/\\/g, '/') === 'automation/hooks.json')) {
  fail('cursor hooks not remapped')
}
if (remapped.meta?.mappedTo !== 'dsh') fail(`mappedTo ${remapped.meta?.mappedTo}`)
console.log('✓ mapLegacyPack remaps .cursor/* to portable bundle paths')

const out = packTestTmp(`dsh-map-${Date.now()}`)
const compiled = await compilePackToDshBundle(cursorPack, out)
for (const rel of [
  'mods/legacy-skill/mod.json',
  'mods/legacy-skill/SKILL.md',
  'mods/cursor-fs/mod.json',
  'mods/rule-always/mod.json',
  'mods/command-hello/mod.json',
  'mods/hook-hooks/mod.json',
  'instructions/always.mdc',
  'commands/hello.md',
  'automation/hooks.json',
]) {
  try {
    await access(join(compiled.dir, ...rel.split('/')))
  } catch {
    fail(`mapped DSH pack missing ${rel}`)
  }
}
const catalog = JSON.parse(await readFile(join(compiled.dir, 'catalog.json'), 'utf8')) as {
  mods?: Array<{ id?: string; kind?: string }>
}
for (const kind of ['skill', 'mcp', 'rule', 'command', 'hook']) {
  if (!catalog.mods?.some(m => m.kind === kind)) fail(`catalog.mods missing kind ${kind}: ${JSON.stringify(catalog.mods)}`)
}
const body = await readFile(join(compiled.dir, 'mods', 'legacy-skill', 'SKILL.md'), 'utf8')
if (!body.includes('CURSORTOKENSIG')) fail('skill body not in DSH mods/')
const mcpBody = await readFile(join(compiled.dir, 'mods', 'cursor-fs', 'mcp.json'), 'utf8')
if (!mcpBody.includes('server-filesystem')) fail('mcp mod missing command')
console.log('✓ compile of cursor pack ships skill/mcp/rule/command/hook as mods/')

const workspace = packTestTmp(`dsh-map-ws-${Date.now()}`)
const projected = await mapPackToDsh(cursorPack, workspace)
if (!projected.dir.replace(/\\/g, '/').includes('.agent-pack/modpacks/')) {
  fail(`mapPackToDsh must project, got ${projected.dir}`)
}
try {
  await access(join(projected.dir, 'mods', 'legacy-skill', 'mod.json'))
} catch {
  fail('mapPackToDsh must write mods/')
}
console.log('✓ mapPackToDsh projects into modpacks')

const mcpOnly: PackDoc = {
  schema: 'ccui-pack/v0.2',
  name: 'mcp-only',
  version: '0.1.0',
  runtime: { id: 'cursor' },
  bundle: {
    portable: true,
    files: [{
      path: '.cursor/mcp.json',
      content: JSON.stringify({
        mcpServers: {
          'legacy-browser': { command: 'npx', args: ['-y', '@playwright/mcp'] },
        },
      }),
    }],
  },
}
const mcpOut = packTestTmp(`dsh-map-mcp-${Date.now()}`)
const mcpCompiled = await compilePackToDshBundle(mcpOnly, mcpOut)
try {
  await access(join(mcpCompiled.dir, 'mods', 'legacy-browser', 'mod.json'))
  await access(join(mcpCompiled.dir, 'mods', 'legacy-browser', 'mcp.json'))
} catch {
  fail('`.cursor/mcp.json` must become an MCP mod even without tools.mcp')
}
const mcpMod = JSON.parse(await readFile(join(mcpCompiled.dir, 'mods', 'legacy-browser', 'mod.json'), 'utf8')) as { kind?: string }
if (mcpMod.kind !== 'mcp') fail(`mcp-only kind ${mcpMod.kind}`)
console.log('✓ bundle .cursor/mcp.json maps to MCP mods')
await rm(mcpOut, { recursive: true, force: true }).catch(() => {})

const fromRoot = packTestTmp(`dsh-map-from-${Date.now()}`)
const skillDir = join(fromRoot, '.cursor', 'skills', 'from-disk')
await mkdir(skillDir, { recursive: true })
await writeFile(join(skillDir, 'SKILL.md'), '---\nname: from-disk\n---\n# from disk\n\nFROMDISKTOKEN\n', 'utf8')
await mkdir(join(fromRoot, '.cursor', 'rules'), { recursive: true })
await writeFile(join(fromRoot, '.cursor', 'rules', 'team.mdc'), '# team rule\n', 'utf8')
await mkdir(join(fromRoot, '.cursor', 'commands'), { recursive: true })
await writeFile(join(fromRoot, '.cursor', 'commands', 'ship.md'), '# ship\n', 'utf8')
await writeFile(join(fromRoot, '.cursor', 'hooks.json'), '{"hooks":{"sessionStart":[]}}\n', 'utf8')
await writeFile(
  join(fromRoot, '.cursor', 'mcp.json'),
  JSON.stringify({ mcpServers: { 'from-fs': { command: 'npx', args: ['-y', 'mcp-server-fs'] } } }),
  'utf8',
)
const refOnly: PackDoc = {
  schema: 'ccui-pack/v0.2',
  name: 'ref-only',
  version: '0.1.0',
  runtime: { id: 'cursor' },
  knowledge: { skills: [{ name: 'from-disk', ref: '.cursor/skills/from-disk', source: 'cursor' }] },
  bundle: { portable: false, files: [] },
}
const hydratedOut = packTestTmp(`dsh-map-hydrated-${Date.now()}`)
const hydrated = await mapPackToDsh(refOnly, hydratedOut, { from: fromRoot })
const hydratedSkill = join(hydrated.dir, 'mods', 'from-disk', 'SKILL.md')
try {
  await access(hydratedSkill)
} catch {
  fail(`ref-only pack must hydrate from --from, missing ${hydratedSkill}`)
}
const hydratedBody = await readFile(hydratedSkill, 'utf8')
if (!hydratedBody.includes('FROMDISKTOKEN')) fail('hydrated skill body missing')
for (const rel of [
  'mods/from-fs/mcp.json',
  'mods/rule-team/mod.json',
  'mods/command-ship/mod.json',
  'mods/hook-hooks/mod.json',
]) {
  try {
    await access(join(hydrated.dir, ...rel.split('/')))
  } catch {
    fail(`--from must ingest MCP/rule/command/hook, missing ${rel}`)
  }
}
console.log('✓ --from hydrates skill + MCP + rule + command + hook')

await rm(out, { recursive: true, force: true }).catch(() => {})
await rm(workspace, { recursive: true, force: true }).catch(() => {})
await rm(fromRoot, { recursive: true, force: true }).catch(() => {})
await rm(hydratedOut, { recursive: true, force: true }).catch(() => {})

console.log('[OK] dsh map cursor pack')
