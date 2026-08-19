#!/usr/bin/env bun
/**
 * 投影目录 + SQLite 注册表 + Rust FTS：多包并存，会话只见允许集。
 */
import { access, readFile, rm } from 'node:fs/promises'
import { join } from 'node:path'
import { packTestTmp } from '../../test/tmp-root.js'
import type { PackDoc } from '../../src/types.js'
import { compilePackToDshBundle } from './compile.js'
import {
  catalogAllow,
  catalogDeny,
  catalogList,
  catalogPaths,
  catalogSearch,
  catalogSetList,
  catalogSetLoad,
  catalogSetSave,
  catalogSnapshot,
  projectPack,
} from './catalog.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

function packWithSkill(name: string, skillName: string, token: string): PackDoc {
  return {
    name,
    version: '0.1.0',
    description: `${name} pack`,
    knowledge: { skills: [{ name: skillName, source: 'bundled' }] },
    bundle: {
      portable: true,
      files: [{
        path: `skills/${skillName}/SKILL.md`,
        content: `---\nname: ${skillName}\ndescription: ${name} skill\n---\n# ${skillName}\n\n${token}\n`,
      }],
    },
  }
}

const workspace = packTestTmp(`dsh-catalog-${Date.now()}`)
const alpha = packWithSkill('alpha-lib', 'alpha-skill', 'ALPHATOKENSIG unique body for search')
const beta = packWithSkill('beta-lib', 'beta-skill', 'BETATOKENSIG unique body for search')

const a = await projectPack(alpha, workspace)
const b = await projectPack(beta, workspace)

for (const p of [
  join(a.dir, 'skills', 'alpha-skill', 'SKILL.md'),
  join(b.dir, 'skills', 'beta-skill', 'SKILL.md'),
  join(a.dir, 'catalog.json'),
  join(a.dir, 'mods', 'alpha-skill', 'mod.json'),
  join(a.dir, 'mods', 'alpha-skill', 'SKILL.md'),
]) {
  try {
    await access(p)
  } catch {
    fail(`missing ${p}`)
  }
}

if (!a.dir.replace(/\\/g, '/').includes('.agent-pack/modpacks/')) {
  fail(`projected dir must be under .agent-pack/modpacks, got ${a.dir}`)
}

const paths = catalogPaths(workspace)
if (!paths.modpacksDir.replace(/\\/g, '/').includes('.agent-pack/modpacks')) {
  fail(`modpacksDir ${paths.modpacksDir}`)
}
if (!paths.db.replace(/\\/g, '/').includes('.agent-pack/catalog.sqlite')) {
  fail(`db ${paths.db}`)
}
try {
  await access(paths.db)
} catch {
  fail(`catalog db missing at ${paths.db}`)
}

const listed = await catalogList(workspace)
if (listed.length < 2) fail(`list ${JSON.stringify(listed)}`)
if (!listed.some(p => p.id === a.id) || !listed.some(p => p.id === b.id)) {
  fail(`list missing packs: ${JSON.stringify(listed)}`)
}
if (listed.some(p => p.enabled)) fail(`new packs must start disabled: ${JSON.stringify(listed)}`)

const snap0 = await catalogSnapshot(workspace)
if (snap0.skills.length !== 0) {
  fail(`empty allow-list must hide skills, got ${JSON.stringify(snap0)}`)
}

const hits = await catalogSearch(workspace, 'ALPHATOKENSIG')
if (!hits.some(h => h.pack_id === a.id)) fail(`search missed alpha: ${JSON.stringify(hits)}`)
if (hits.some(h => h.pack_id === b.id)) fail(`search leaked beta: ${JSON.stringify(hits)}`)

await catalogAllow(workspace, a.id)
const snap1 = await catalogSnapshot(workspace)
if (!snap1.skills.some(s => s.name === 'alpha-skill')) {
  fail(`allow alpha not in snapshot ${JSON.stringify(snap1)}`)
}
if (snap1.skills.some(s => s.name === 'beta-skill')) {
  fail('beta must stay hidden until allowed')
}

try {
  await access(join(b.dir, 'skills', 'beta-skill', 'SKILL.md'))
} catch {
  fail('denied pack must remain on disk')
}

await catalogDeny(workspace, a.id)
const snap2 = await catalogSnapshot(workspace)
if (snap2.skills.some(s => s.name === 'alpha-skill')) fail('deny should hide alpha')

await catalogAllow(workspace, a.id)
await catalogSetSave(workspace, 'alpha-only')
await catalogAllow(workspace, b.id)
const snapBoth = await catalogSnapshot(workspace)
if (!snapBoth.skills.some(s => s.name === 'alpha-skill') || !snapBoth.skills.some(s => s.name === 'beta-skill')) {
  fail(`live whitelist after second allow should contain both: ${JSON.stringify(snapBoth)}`)
}
await catalogSetLoad(workspace, 'alpha-only')
const snapLoaded = await catalogSnapshot(workspace)
if (!snapLoaded.skills.some(s => s.name === 'alpha-skill')) fail(`set-load alpha-only missing alpha: ${JSON.stringify(snapLoaded)}`)
if (snapLoaded.skills.some(s => s.name === 'beta-skill')) fail('set-load alpha-only must not keep beta in the live whitelist')
const sets = await catalogSetList(workspace)
if (sets.active !== 'alpha-only') fail(`active set ${sets.active}`)
if (!sets.sets.some(s => s.name === 'alpha-only' && s.pack_ids.includes(a.id) && !s.pack_ids.includes(b.id))) {
  fail(`set list ${JSON.stringify(sets)}`)
}
console.log('✓ named allow-set save/load is instance whitelist, not per-chat')

console.log('✓ project + sqlite catalog + rust search + allow-list')

const compileOut = packTestTmp(`dsh-compile-install-${Date.now()}`)
const compiled = await compilePackToDshBundle(alpha, compileOut)
const installMd = await readFile(join(compiled.dir, 'INSTALL.md'), 'utf8')
if (installMd.includes(`dsh plugin --profile web add "${compiled.dir}`)) {
  fail('INSTALL.md must not dsh plugin add the projected pack')
}
if (!installMd.includes('allow')) fail('INSTALL.md must tell allow')
if (!String(compiled.installCommand).includes('allow')) fail(`installCommand ${compiled.installCommand}`)
await rm(compileOut, { recursive: true, force: true }).catch(() => {})
await rm(workspace, { recursive: true, force: true }).catch(() => {})

console.log('[OK] dsh catalog')
