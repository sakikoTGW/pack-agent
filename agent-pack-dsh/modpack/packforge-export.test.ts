#!/usr/bin/env bun
/**
 * 实例 → .dspack 导出测试：dshhome 多 profile、单 profile、排除敏感内容、往返导入、未锁依赖拒绝。
 * 运行：bun run agent-pack-dsh/modpack/packforge-export.test.ts
 */
import { promises as fs } from 'node:fs'
import { join, resolve } from 'node:path'
import { readDspack } from './packforge.js'
import { applyCompatInstall, planCompatInstall } from './packforge-ext.js'
import { exportInstanceToDspack, isPinnedDependencyValue } from './packforge-export.js'

const ROOT = resolve(import.meta.dirname, '../../.tmp/packforge-export-fixtures')

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}
function ok(msg: string): void {
  console.log(`✔ ${msg}`)
}
async function exists(p: string): Promise<boolean> {
  try {
    await fs.stat(p)
    return true
  } catch {
    return false
  }
}

async function writeFiles(root: string, files: Record<string, string>): Promise<void> {
  for (const [rel, body] of Object.entries(files)) {
    const dst = join(root, rel)
    await fs.mkdir(join(dst, '..'), { recursive: true })
    await fs.writeFile(dst, body, 'utf8')
  }
}

async function buildInstance(): Promise<string> {
  const inst = join(ROOT, 'instances', 'demo')
  const home = join(inst, 'home')
  await fs.rm(ROOT, { recursive: true, force: true })
  await fs.mkdir(home, { recursive: true })
  await writeFiles(inst, {
    'instance.json': JSON.stringify({
      schema: 'pack-agent.launcher.instance/v2',
      id: 'demo',
      name: 'Demo Instance',
      dsh: { version: '0.2.1-rc.2' },
      home,
      lastProfile: 'dev',
    }),
  })
  await writeFiles(home, {
    'profiles/dev/package.json': JSON.stringify({
      name: 'dsh-profile-dev',
      private: true,
      dsh: { profile: { bundles: ['@deepseek-ai/dsh-base', 'dsh-pet'] } },
      dependencies: { json5: '2.2.3' },
    }),
    'profiles/dev/cordis.patch.yml': '- insert: []\n',
    'profiles/dev/node_modules/json5/package.json': '{"name":"json5"}\n',
    'profiles/dev/.plugin-manager/logs/x.log': 'log\n',
    'profiles/dev/pnpm-lock.yaml': 'lock\n',
    'profiles/writer/package.json': JSON.stringify({
      name: 'dsh-profile-writer',
      private: true,
      dsh: { profile: { bundles: ['@deepseek-ai/dsh-base'] } },
      dependencies: { 'is-number': '7.0.0' },
    }),
    'profiles/writer/cordis.patch.yml': '- insert: []\n',
    'skills/whale/SKILL.md': '# whale\n',
    '.agent-presets/coding/agent.cordis.yml': 'name: coding\n',
    'AGENTS.md': '# global agents\n',
    'sessions/evil.json': '{"session":"x"}\n',
    'storages/workspace.json': '{"tables":{}}\n',
    'logs/run.log': 'log\n',
    'workspace/secret.txt': 'secret\n',
    '.credentials.yaml': 'api_key: nope\n',
    'settings.yaml': 'secret: true\n',
    'launcher.patch.yml': '- id: x\n',
  })
  return home
}

async function main(): Promise<void> {
  const home = await buildInstance()
  console.log(`fixtures: ${ROOT}`)

  // 1) pinned 判定
  for (const s of ['2.2.3', '0.2.1-rc.2', '99e8c57', 'github:owner/repo#abc1234']) {
    if (!isPinnedDependencyValue(s)) fail(`should be pinned: ${s}`)
  }
  for (const s of ['^1.0.0', '~2.0.0', 'latest', 'file:../x', 'workspace:*', '']) {
    if (isPinnedDependencyValue(s)) fail(`should NOT be pinned: ${s}`)
  }
  ok('pinned dependency predicate')

  // 2) 多 profile → dshhome
  const outHome = join(ROOT, 'out', 'demo.dspack')
  const rHome = await exportInstanceToDspack({ home, out: outHome })
  if (rHome.type !== 'dshhome') fail(`expected dshhome, got ${rHome.type}`)
  if (rHome.stats.profiles !== 2) fail(`profiles ${rHome.stats.profiles}`)
  if (rHome.name !== 'demo-instance') fail(`name ${rHome.name}`)
  const docH = await readDspack(outHome)
  try {
    if (docH.type !== 'dshhome') fail('readback type')
    if (docH.manifest.defaultProfile !== 'dev') fail(`defaultProfile ${docH.manifest.defaultProfile}`)
    const ins = docH.layout.overrides
    if (!ins.includes('profiles/dev/cordis.patch.yml')) fail(`missing profile patch: ${ins.join(',')}`)
    if (!ins.includes('profiles/writer/cordis.patch.yml')) fail('missing writer patch')
    if (!ins.includes('skills/whale/SKILL.md')) fail('missing global skill')
    if (!ins.includes('.agent-presets/coding/agent.cordis.yml')) fail('missing preset')
    if (!ins.includes('AGENTS.md')) fail('missing AGENTS.md')
    for (const banned of ['sessions', 'storages', 'logs', 'workspace', '.credentials.yaml', 'settings.yaml', 'launcher.patch.yml', 'node_modules', 'package.json', 'pnpm-lock.yaml']) {
      if (ins.some(p => p === banned || p.startsWith(banned + '/'))) fail(`sensitive/derived leaked: ${banned}`)
    }
    ok('export dshhome: profiles/home content, sensitive+derived excluded')
  } finally {
    await docH.cleanup()
  }

  // 3) 往返：导出 → compat 导入到新 home
  const fresh = join(ROOT, 'fresh-home')
  await fs.mkdir(fresh, { recursive: true })
  const doc2 = await readDspack(outHome, { keepExtracted: true })
  try {
    const plan = await planCompatInstall(doc2, { home: fresh })
    if (plan.type !== 'dshhome' || plan.profiles?.length !== 2) fail('roundtrip plan')
    const rep = await applyCompatInstall(doc2, plan, { dryRun: false })
    if (rep.copiedOverrides < 4) fail(`roundtrip copied ${rep.copiedOverrides}`)
    if (!(await exists(join(fresh, 'profiles/dev/package.json')))) fail('roundtrip dev package.json')
    if (!(await exists(join(fresh, 'profiles/dev/cordis.patch.yml')))) fail('roundtrip dev patch')
    if (!(await exists(join(fresh, 'AGENTS.md')))) fail('roundtrip AGENTS.md')
    if (!(await exists(join(fresh, 'skills/whale/SKILL.md')))) fail('roundtrip skill')
    const devPkg = JSON.parse(await fs.readFile(join(fresh, 'profiles/dev/package.json'), 'utf8'))
    if (JSON.stringify(devPkg.dsh.profile.bundles) !== JSON.stringify(['@deepseek-ai/dsh-base', 'dsh-pet'])) fail('roundtrip bundles')
    if (await exists(join(fresh, 'sessions/evil.json'))) fail('roundtrip leaked sessions')
    ok('export → import round-trip (dshhome)')
  } finally {
    await doc2.cleanup()
  }

  // 4) 单 profile → profile 形态 + home/
  const outProfile = join(ROOT, 'out', 'demo-dev.dspack')
  const rProfile = await exportInstanceToDspack({ home, out: outProfile, profile: 'dev' })
  if (rProfile.type !== 'profile') fail(`expected profile, got ${rProfile.type}`)
  const docP = await readDspack(outProfile)
  try {
    if (docP.type !== 'profile') fail('profile readback type')
    const m = docP.manifest as Record<string, unknown>
    if (m.profileName !== 'dev') fail(`profileName ${m.profileName}`)
    if (JSON.stringify(m.bundles) !== JSON.stringify(['@deepseek-ai/dsh-base', 'dsh-pet'])) fail('profile bundles')
    if (m.patch !== 'cordis.patch.yml') fail(`profile patch ${m.patch}`)
    if (docP.layout.home.length < 3) fail(`profile home/ files ${docP.layout.home.length}`)
    if (!docP.layout.home.includes('skills/whale/SKILL.md')) fail('profile home skill missing')
    ok('export single profile (profile form + home/)')
  } finally {
    await docP.cleanup()
  }

  // 5) 未锁依赖 → PA148
  await fs.writeFile(
    join(home, 'profiles/dev/package.json'),
    JSON.stringify({
      name: 'dsh-profile-dev',
      private: true,
      dsh: { profile: { bundles: ['@deepseek-ai/dsh-base'] } },
      dependencies: { bad: '^1.0.0' },
    }),
  )
  try {
    await exportInstanceToDspack({ home, out: join(ROOT, 'out', 'bad.dspack') })
    fail('unpinned dependency must PA148')
  } catch (e) {
    if ((e as { code?: string }).code !== 'PA148') fail(`expected PA148, got ${(e as Error).message}`)
    ok('unpinned dependency → PA148')
  }

  // 6) 范围依赖 + node_modules 已装版本 → 自动锁定为精确版本
  await fs.mkdir(join(home, 'profiles/dev/node_modules/ranged'), { recursive: true })
  await fs.writeFile(join(home, 'profiles/dev/node_modules/ranged/package.json'), '{"name":"ranged","version":"1.2.3"}\n', 'utf8')
  await fs.writeFile(
    join(home, 'profiles/dev/package.json'),
    JSON.stringify({
      name: 'dsh-profile-dev',
      private: true,
      dsh: { profile: { bundles: ['@deepseek-ai/dsh-base'] } },
      dependencies: { ranged: '^1.2.0' },
    }),
  )
  const outPinned = join(ROOT, 'out', 'pinned.dspack')
  const rPinned = await exportInstanceToDspack({ home, out: outPinned })
  const devUnit = (rPinned.manifest.profiles as Record<string, { dependencies: Record<string, string> }>).dev
  if (devUnit.dependencies.ranged !== '1.2.3') fail(`auto-pin got ${devUnit.dependencies.ranged}`)
  if (!rPinned.warnings.some(w => w.includes('pinned dependency'))) fail('missing auto-pin warning')
  ok('range dependency auto-pinned from node_modules (1.2.3)')

  console.log('\nALL PACKFORGE EXPORT TESTS PASSED')
}

await main()
