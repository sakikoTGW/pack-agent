#!/usr/bin/env bun
/**
 * PackForge 扩展层测试：市场 / registry / files[] 下载 / compat 安装计划与执行。
 * 运行：bun run agent-pack-dsh/modpack/packforge-ext.test.ts
 */
import { promises as fs } from 'node:fs'
import { createHash } from 'node:crypto'
import { join, resolve } from 'node:path'
import { writeZipFromDir } from '../../src/pack-archive.js'
import { PackforgeError, readDspack } from './packforge.js'
import {
  PAD_LAUNCHER_RECORD,
  applyCompatInstall,
  builtinLauncherRegistry,
  downloadDeclaredFiles,
  fetchLauncherRegistry,
  fetchMarketIndex,
  fetchMarketPack,
  findMarketPack,
  lookupLauncher,
  planCompatInstall,
  type FetchLike,
} from './packforge-ext.js'

const ROOT = resolve(import.meta.dirname, '../../.tmp/packforge-ext-fixtures')

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}
function ok(msg: string): void {
  console.log(`✔ ${msg}`)
}
const sha = (buf: Uint8Array) => createHash('sha256').update(buf).digest('hex')

async function writeZip(stage: string, out: string, files: Record<string, string | Uint8Array>): Promise<void> {
  await fs.rm(stage, { recursive: true, force: true })
  for (const [rel, body] of Object.entries(files)) {
    const dst = join(stage, rel)
    await fs.mkdir(join(dst, '..'), { recursive: true })
    await fs.writeFile(dst, body as never)
  }
  await fs.mkdir(join(out, '..'), { recursive: true })
  await writeZipFromDir(stage, out)
}

async function expectAsyncCode(code: string, fn: () => Promise<unknown>, label: string): Promise<void> {
  try {
    await fn()
  } catch (e) {
    if (e instanceof PackforgeError && e.code === code) {
      ok(`${label} → ${code}`)
      return
    }
    fail(`${label}: expected ${code}, got ${(e as Error).message}`)
  }
  fail(`${label}: expected ${code}, but no error`)
}

function jsonResponse(obj: unknown, status = 200): Response {
  return new Response(JSON.stringify(obj), { status, headers: { 'content-type': 'application/json' } })
}
function bytesResponse(buf: Uint8Array, status = 200): Response {
  return new Response(buf, { status })
}

function stubFetch(map: Record<string, () => Response>): FetchLike {
  return async (url: string) => {
    const hit = map[url]
    if (!hit) return new Response('not found', { status: 404 })
    return hit()
  }
}

// ────────────────────── 夹具 ──────────────────────

const PROFILE_MANIFEST = {
  manifestVersion: 5,
  type: 'profile',
  name: 'ext-demo',
  version: '1.0.0',
  dshVersion: '0.2.1-rc.2',
  profileName: 'demo',
  bundles: ['@deepseek-ai/dsh-base', '@deepseek-ai/dsh-web-app'],
  dependencies: { 'demo-plugin': '1.2.3', 'github:owner/theme': 'abc1234' },
  launchers: ['pad'],
}

const V4_MANIFEST = {
  manifestVersion: 4,
  name: 'legacy-v4',
  version: '0.9.0',
  dshVersion: '0.1.1-rc.2',
  profileName: 'legacy',
  bundles: ['@deepseek-ai/dsh-base'],
  dependencies: { 'legacy-plugin': '0.1.0' },
}

const HOME_MANIFEST = {
  manifestVersion: 5,
  type: 'dshhome',
  name: 'ext-home',
  version: '1.0.0',
  dshVersion: '0.2.1-rc.2',
  defaultProfile: 'dev',
  profiles: {
    dev: { bundles: ['@deepseek-ai/dsh-base'], dependencies: { 'dev-plug': '0.1.0' } },
    writer: { bundles: ['@deepseek-ai/dsh-base'], dependencies: {} },
  },
  presets: { coding: { path: '.agent-presets/coding' } },
  skills: [{ path: 'skills/whale' }],
  instructions: 'AGENTS.md',
}

async function buildFixtures(): Promise<{ profile: string; v4: string; home: string }> {
  await fs.rm(ROOT, { recursive: true, force: true })
  const out = join(ROOT, 'out')
  const profile = join(out, 'ext-demo.dspack')
  await writeZip(join(ROOT, 'stage-profile'), profile, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 3 }),
    'manifest.json': JSON.stringify(PROFILE_MANIFEST),
    'overrides/cordis.patch.yml': '- insert: []\n',
    'overrides/.credentials.yaml': 'api_key: nope\n',
    'overrides/README.md': '# demo\n',
    'home/skills/whale/SKILL.md': '# whale\n',
    'home/sessions/evil.json': '{"session":"x"}\n',
  })
  const v4 = join(out, 'legacy-v4.dspack')
  await writeZip(join(ROOT, 'stage-v4'), v4, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 3 }),
    'manifest.json': JSON.stringify(V4_MANIFEST),
    'overrides/x.yml': 'a: 1\n',
  })
  const home = join(out, 'ext-home.dspack')
  await writeZip(join(ROOT, 'stage-home'), home, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 3 }),
    'manifest.json': JSON.stringify(HOME_MANIFEST),
    'overrides/AGENTS.md': '# agents\n',
    'overrides/profiles/dev/cordis.patch.yml': '- insert: []\n',
    'overrides/.agent-presets/coding/agent.cordis.yml': 'name: coding\n',
    'overrides/skills/whale/SKILL.md': '# whale\n',
  })
  return { profile, v4, home }
}

async function freshHome(name: string): Promise<string> {
  const dir = join(ROOT, 'homes', name)
  await fs.rm(dir, { recursive: true, force: true })
  await fs.mkdir(dir, { recursive: true })
  return dir
}

async function main(): Promise<void> {
  const fx = await buildFixtures()
  console.log(`fixtures: ${ROOT}`)

  // 1. v4 历史格式读取 + 转换警告
  {
    const doc = await readDspack(fx.v4)
    try {
      if (doc.manifestVersion !== 4) fail(`v4 manifestVersion ${doc.manifestVersion}`)
      if (doc.type !== 'profile') fail(`v4 type ${doc.type}`)
      if (doc.name !== 'legacy-v4') fail('v4 name')
      ok('read manifest v4 (legacy profile)')
    } finally {
      await doc.cleanup()
    }
  }

  // 2. 市场索引 + 包下载校验
  {
    const packBytes = new TextEncoder().encode('real-pack-bytes')
    const index = {
      schemaVersion: 2,
      generatedAt: '2026-11-02T00:00:00Z',
      modpacks: [
        {
          manifestVersion: 5,
          type: 'profile',
          name: 'demo-pack',
          version: '1.0.0',
          id: 'owner.demo-pack',
          downloadUrl: 'https://example.test/demo-pack.dspack',
          sha256: sha(packBytes),
          size: packBytes.byteLength,
          dshVersion: '0.2.1-rc.2',
        },
        { name: 'broken', version: '1.0.0' }, // 缺 downloadUrl → warning
      ],
    }
    const fetchImpl = stubFetch({
      [MARKET]: () => jsonResponse(index),
      'https://example.test/demo-pack.dspack': () => bytesResponse(packBytes),
    })
    // 用本地 URL 常量替换（fetchMarketIndex 支持 opts.url）
    const mi = await fetchMarketIndex({ url: MARKET, fetchImpl })
    if (mi.packs.length !== 1) fail(`market packs ${mi.packs.length}`)
    if (mi.warnings.length !== 1) fail(`market warnings ${mi.warnings.length}`)
    const entry = findMarketPack(mi, 'owner.demo-pack')
    if (!entry) fail('findMarketPack by id')
    if (findMarketPack(mi, 'demo-pack')?.id !== 'owner.demo-pack') fail('findMarketPack by name')
    const got = await fetchMarketPack(entry, { fetchImpl })
    if (sha(got.bytes) !== sha(packBytes)) fail('market pack bytes')
    ok('market index + pack download (sha256/size verified)')

    const bad = { ...entry, sha256: 'f'.repeat(64) }
    await expectAsyncCode('PA153', () => fetchMarketPack(bad, { fetchImpl }), 'market pack hash mismatch')
  }

  // 3. launcher registry：正常 / 拉取失败回落 / 查询
  {
    const good = { schemaVersion: 1, launchers: [{ id: 'pad', name: 'PAD', url: 'https://x' }, { id: 'hdsl', name: 'HDSL' }] }
    const reg = await fetchLauncherRegistry({ url: 'https://example.test/launchers.json', fetchImpl: stubFetch({ 'https://example.test/launchers.json': () => jsonResponse(good) }) })
    if (reg.fallback) fail('registry should not fallback')
    if (lookupLauncher(reg, 'pad')?.name !== 'PAD') fail('lookupLauncher pad')
    if (!PAD_LAUNCHER_RECORD.id) fail('PAD record')
    const broken = await fetchLauncherRegistry({
      url: 'https://example.test/launchers.json',
      fetchImpl: async () => {
        throw new Error('network down')
      },
    })
    if (!broken.fallback) fail('registry should fallback')
    if (!lookupLauncher(broken, 'dshl')) fail('builtin fallback missing dshl')
    if (lookupLauncher({ ...broken, launchers: builtinLauncherRegistry() }, 'pad')) fail('builtin should not claim pad yet')
    ok('launcher registry: fetch / fallback / lookup')
  }

  // 4. files[] 下载
  {
    const goodBytes = new TextEncoder().encode('hello-file')
    const fetchImpl = stubFetch({
      'https://example.test/a.bin': () => bytesResponse(goodBytes),
      'https://example.test/bad.bin': () => bytesResponse(new TextEncoder().encode('tampered')),
    })
    const files = [
      { path: 'assets/a.bin', sha256: sha(goodBytes), size: goodBytes.byteLength, urls: ['https://example.test/a.bin'] },
      { path: 'assets/bad.bin', sha256: '0'.repeat(64), size: goodBytes.byteLength, urls: ['https://example.test/bad.bin'] },
      { path: '../escape.bin', urls: ['https://example.test/a.bin'] },
      { path: 'assets/none.bin' },
    ]
    const dest = join(ROOT, 'files-home')
    const rep = await downloadDeclaredFiles(files as never, dest, { fetchImpl })
    if (rep.written.length !== 1 || rep.failed.length !== 2 || rep.skipped.length !== 1) {
      fail(`files report ${JSON.stringify(rep)}`)
    }
    if (!(await fs.stat(join(dest, 'assets/a.bin')).then(() => true, () => false))) fail('good file missing')
    ok('files[]: verified write / hash fail / unsafe path / no-urls')
  }

  // 5. compat plan + apply
  {
    const home = await freshHome('compat-apply')
    const doc = await readDspack(fx.profile, { keepExtracted: true })
    try {
      const plan = await planCompatInstall(doc, { home })
      if (plan.profile !== 'demo') fail(`plan profile ${plan.profile}`)
      if (plan.profileDir !== join(home, 'profiles', 'demo')) fail('plan profileDir')
      if (plan.bundles.length !== 2 || plan.overrides.length !== 3 || plan.homeFiles.length !== 2) {
        fail(`plan counts ${JSON.stringify({ o: plan.overrides, h: plan.homeFiles })}`)
      }
      if (!plan.warnings.some(w => w.includes('敏感'))) fail('missing sensitive warning')
      ok('compat plan (profile/bundles/overrides/home/warnings)')

      const installed: string[] = []
      const runDep = async (spec: string) => {
        installed.push(spec)
      }
      const report = await applyCompatInstall(doc, plan, { dryRun: false, runDep })
      if (!report.dryRun === false) fail('report dryRun flag')
      if (report.copiedOverrides !== 2) fail(`copiedOverrides ${report.copiedOverrides}`)
      if (report.copiedHome !== 1) fail(`copiedHome ${report.copiedHome}`)
      if (report.skippedFiles.length !== 2) fail(`skipped ${JSON.stringify(report.skippedFiles)}`)
      if (report.deps.installed.length !== 2) fail(`deps installed ${report.deps.installed.length}`)
      if (installed[0] !== 'demo-plugin@1.2.3') fail(`runDep first ${installed[0]}`)
      const pkg = JSON.parse(await fs.readFile(join(report.profileDir, 'package.json'), 'utf8'))
      if (JSON.stringify(pkg.dsh.profile.bundles) !== JSON.stringify(PROFILE_MANIFEST.bundles)) fail('bundles not written')
      if (await fs.stat(join(report.profileDir, '.credentials.yaml')).then(() => true, () => false)) fail('credentials copied')
      if (await fs.stat(join(home, 'sessions/evil.json')).then(() => true, () => false)) fail('sessions copied')
      if (!(await fs.stat(join(home, 'skills/whale/SKILL.md')).then(() => true, () => false))) fail('skill not copied')
      ok('compat apply: overrides/home 落盘、bundles 写 package.json、deps runner、敏感文件跳过')

      // dry-run：不产生任何写入
      const dryHome = await freshHome('compat-dry')
      const doc2 = await readDspack(fx.profile)
      try {
        const plan2 = await planCompatInstall(doc2, { home: dryHome })
        const rep2 = await applyCompatInstall(doc2, plan2, { dryRun: true })
        if (!rep2.dryRun) fail('dry-run flag')
        if (await fs.stat(join(dryHome, 'profiles/demo')).then(() => true, () => false)) fail('dry-run wrote profile')
      } finally {
        await doc2.cleanup()
      }
      ok('compat dry-run writes nothing')

      // 已存在 profile → PA150
      await expectAsyncCode('PA150', () => planCompatInstall(doc, { home }), 'existing non-empty profile')
    } finally {
      await doc.cleanup()
    }

    // dshhome：逐 profile + home 根（M4）
    const home2 = await freshHome('compat-dshhome')
    const docH = await readDspack(fx.home)
    try {
      const planH = await planCompatInstall(docH, { home: home2 })
      if (planH.type !== 'dshhome') fail(`dshhome plan type ${planH.type}`)
      if (planH.profiles?.length !== 2) fail(`dshhome profiles ${planH.profiles?.length}`)
      if (planH.defaultProfile !== 'dev') fail(`dshhome defaultProfile ${planH.defaultProfile}`)
      if (!planH.presets?.includes('.agent-presets/coding')) fail(`dshhome presets ${JSON.stringify(planH.presets)}`)
      if (planH.instructions !== 'AGENTS.md') fail(`dshhome instructions ${planH.instructions}`)
      ok('dshhome plan (2 profiles / default / presets / instructions)')

      const depsSeen: string[] = []
      const repH = await applyCompatInstall(docH, planH, {
        dryRun: false,
        runDep: async (spec, profile) => {
          depsSeen.push(`${profile}:${spec}`)
        },
      })
      const exists = async (p: string) => fs.stat(p).then(() => true, () => false)
      if (!(await exists(join(home2, 'AGENTS.md')))) fail('dshhome AGENTS.md not copied')
      if (!(await exists(join(home2, 'profiles/dev/cordis.patch.yml')))) fail('dshhome profile patch not copied')
      if (!(await exists(join(home2, '.agent-presets/coding/agent.cordis.yml')))) fail('dshhome preset not copied')
      if (!(await exists(join(home2, 'skills/whale/SKILL.md')))) fail('dshhome skill not copied')
      const devPkg = JSON.parse(await fs.readFile(join(home2, 'profiles/dev/package.json'), 'utf8'))
      if (JSON.stringify(devPkg.dsh.profile.bundles) !== JSON.stringify(['@deepseek-ai/dsh-base'])) fail('dshhome dev bundles')
      if (!(await exists(join(home2, 'profiles/writer/package.json')))) fail('dshhome writer package.json missing')
      if (repH.defaultProfile !== 'dev') fail('dshhome report defaultProfile')
      if (depsSeen.length !== 1 || depsSeen[0] !== 'dev:dev-plug@0.1.0') fail(`dshhome deps runner ${JSON.stringify(depsSeen)}`)
      ok('dshhome apply (profiles/home root/presets/skills/per-profile deps)')
    } finally {
      await docH.cleanup()
    }
  }

  console.log('\nALL PACKFORGE EXT TESTS PASSED')
}

const MARKET = 'https://example.test/index.json'
await main()
