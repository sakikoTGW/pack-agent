#!/usr/bin/env bun
/**
 * PackForge `.dspack` 适配器：读取 / 校验 / 转换 / 导出 / 版本决策 / launcher 判定
 * 运行：bun run agent-pack-dsh/modpack/packforge.test.ts
 */
import { promises as fs } from 'node:fs'
import { createHash } from 'node:crypto'
import { join, resolve } from 'node:path'
import { writeZipFromDir } from '../../src/pack-archive.js'
import {
  PackforgeError,
  compareLauncherVersion,
  checkFileDigest,
  decideDshVersion,
  dspackToPadPack,
  evaluateLaunchers,
  isExcludedRelPath,
  isSafeRelPath,
  normalizeLaunchers,
  padPackToDspack,
  readDspack,
  validateManifest,
  type PadPackV2,
} from './packforge.js'

const ROOT = resolve(import.meta.dirname, '../../.tmp/packforge-fixtures')
const PROFILE_DIR = join(ROOT, 'profile')
const HOME_DIR = join(ROOT, 'dshhome')
const OUT_DIR = join(ROOT, 'out')

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

async function writeFiles(dir: string, files: Record<string, string | Uint8Array>): Promise<void> {
  for (const [rel, content] of Object.entries(files)) {
    const dst = join(dir, rel)
    await fs.mkdir(join(dst, '..'), { recursive: true })
    await fs.writeFile(dst, content as never)
  }
}

async function freshDir(dir: string): Promise<void> {
  await fs.rm(dir, { recursive: true, force: true })
  await fs.mkdir(dir, { recursive: true })
}

function expectCode(code: string, fn: () => unknown, label: string): void {
  try {
    fn()
  } catch (e) {
    if (e instanceof PackforgeError && e.code === code) {
      console.log(`✔ ${label} → ${code}`)
      return
    }
    fail(`${label}: expected ${code}, got ${(e as Error).message}`)
  }
  fail(`${label}: expected ${code}, but no error`)
}

async function expectAsyncCode(code: string, fn: () => Promise<unknown>, label: string): Promise<void> {
  try {
    await fn()
  } catch (e) {
    if (e instanceof PackforgeError && e.code === code) {
      console.log(`✔ ${label} → ${code}`)
      return
    }
    fail(`${label}: expected ${code}, got ${(e as Error).message}`)
  }
  fail(`${label}: expected ${code}, but no error`)
}

// ────────────────────── 夹具 ──────────────────────

const PROFILE_MANIFEST = {
  manifestVersion: 5,
  type: 'profile',
  name: 'demo-pack',
  version: '1.0.0',
  displayName: { 'zh-CN': '演示包' },
  description: 'fixture for packforge adapter',
  author: 'pad',
  dshVersion: '0.2.1-rc.2',
  dshVersions: ['0.2.1-rc.2', '0.2.2-alpha.2'],
  profileName: 'web',
  bundles: ['@deepseek-ai/dsh-base', '@deepseek-ai/dsh-web-app'],
  dependencies: { 'demo-plugin': '1.2.3', 'github:owner/theme': 'abc1234' },
  launchers: ['pad', 'hdsl'],
  files: [{ path: 'assets/big.bin', sha256: 'a'.repeat(64), size: 3, urls: ['https://example.invalid/big.bin'] }],
}

const HOME_MANIFEST = {
  manifestVersion: 5,
  type: 'dshhome',
  name: 'demo-home',
  version: '0.2.0',
  dshVersion: '0.2.1-rc.2',
  defaultProfile: 'dev',
  profiles: {
    dev: { bundles: ['@deepseek-ai/dsh-base'], dependencies: { 'dev-tool': '0.1.0' } },
    writer: { bundles: ['@deepseek-ai/dsh-base'], dependencies: {} },
  },
  presets: { coding: { path: '.agent-presets/coding/agent.cordis.yml' } },
  skills: [{ path: 'skills/whale-writer' }],
  instructions: 'AGENTS.md',
  launchers: { pad: { supported: true }, hdsl: { supported: false, reason: '需要设置界面' } },
}

async function buildFixtures(): Promise<void> {
  await fs.rm(ROOT, { recursive: true, force: true })
  await fs.mkdir(PROFILE_DIR, { recursive: true })
  await fs.mkdir(HOME_DIR, { recursive: true })
  await writeFiles(PROFILE_DIR, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 3 }),
    'manifest.json': JSON.stringify(PROFILE_MANIFEST),
    'overrides/cordis.patch.yml': '- insert:\n    - id: demo\n',
    'home/skills/whale-writer/SKILL.md': '# Whale writer\n',
  })
  await writeFiles(HOME_DIR, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 3 }),
    'manifest.json': JSON.stringify(HOME_MANIFEST),
    'overrides/AGENTS.md': '# agents\n',
    'overrides/profiles/dev/cordis.patch.yml': '- insert: []\n',
    'overrides/.agent-presets/coding/agent.cordis.yml': 'name: coding\n',
    'overrides/skills/whale-writer/SKILL.md': '# whale\n',
    'overrides/sessions/evil.json': '{"should":"not be exported"}\n',
    'overrides/.credentials.yaml': 'api_key: should-not-leak\n',
  })
  await fs.mkdir(OUT_DIR, { recursive: true })
  await writeZipFromDir(PROFILE_DIR, join(OUT_DIR, 'profile.dspack'))
  await writeZipFromDir(HOME_DIR, join(OUT_DIR, 'dshhome.dspack'))
  // 坏包：无 dspack.json
  const bad1 = join(ROOT, 'bad-no-dspack')
  await freshDir(bad1)
  await writeFiles(bad1, { 'manifest.json': JSON.stringify(PROFILE_MANIFEST) })
  await writeZipFromDir(bad1, join(OUT_DIR, 'bad-no-dspack.dspack'))
  // 坏包：容器版本 4
  const bad2 = join(ROOT, 'bad-version')
  await freshDir(bad2)
  await writeFiles(bad2, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 4 }),
    'manifest.json': JSON.stringify(PROFILE_MANIFEST),
  })
  await writeZipFromDir(bad2, join(OUT_DIR, 'bad-version.dspack'))
  // 坏包：profile 缺 bundles
  const bad3 = join(ROOT, 'bad-manifest')
  await freshDir(bad3)
  await writeFiles(bad3, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 3 }),
    'manifest.json': JSON.stringify({ ...PROFILE_MANIFEST, bundles: [] }),
    'overrides/x.txt': 'x',
  })
  await writeZipFromDir(bad3, join(OUT_DIR, 'bad-manifest.dspack'))
  // 坏包：dshhome 含 web profile
  const bad4 = join(ROOT, 'bad-home')
  await freshDir(bad4)
  await writeFiles(bad4, {
    'dspack.json': JSON.stringify({ format: 'dspack', version: 3 }),
    'manifest.json': JSON.stringify({ ...HOME_MANIFEST, profiles: { web: { bundles: ['x'], dependencies: {} } }, defaultProfile: 'web' }),
    'overrides/a.txt': 'a',
  })
  await writeZipFromDir(bad4, join(OUT_DIR, 'bad-home.dspack'))
  // 布局目录（导出测试用）
  await writeFiles(join(ROOT, 'layout'), {
    'overrides/cordis.patch.yml': '- insert: []\n',
    'overrides/.credentials.yaml': 'should-not-export\n',
    'home/skills/whale/SKILL.md': '# whale\n',
    'ignored.txt': 'x\n',
  })
}

// ────────────────────── 用例 ──────────────────────

async function main(): Promise<void> {
  await buildFixtures()
  console.log(`fixtures: ${ROOT}`)

  // 1. 读取 profile
  {
    const doc = await readDspack(join(OUT_DIR, 'profile.dspack'))
    try {
      if (doc.type !== 'profile') fail(`type ${doc.type}`)
      if (doc.name !== 'demo-pack' || doc.version !== '1.0.0') fail(`name/version ${doc.name}@${doc.version}`)
      if (doc.dshVersion !== '0.2.1-rc.2') fail(`dshVersion ${doc.dshVersion}`)
      if ((doc.dshVersions ?? []).length !== 2) fail('dshVersions')
      if (doc.layout.overrides.length !== 1 || doc.layout.home.length !== 1) fail(`layout ${JSON.stringify(doc.layout)}`)
      if (!doc.launchers.pad?.supported || !doc.launchers.hdsl?.supported) fail('launchers')
      console.log('✔ read profile .dspack')
    } finally {
      await doc.cleanup()
    }
  }

  // 2. 读取 dshhome + launcher 复合形态
  {
    const doc = await readDspack(join(OUT_DIR, 'dshhome.dspack'))
    try {
      if (doc.type !== 'dshhome') fail(`type ${doc.type}`)
      if (!doc.launchers.hdsl || doc.launchers.hdsl.supported !== false) fail('launchers full form')
      if (doc.launchers.hdsl.reason !== '需要设置界面') fail('launcher reason')
      const profiles = (doc.manifest.profiles ?? {}) as Record<string, unknown>
      if (!('dev' in profiles) || !('writer' in profiles)) fail('profiles')
      console.log('✔ read dshhome .dspack + full-form launchers')
    } finally {
      await doc.cleanup()
    }
  }

  // 3. 坏包
  await expectAsyncCode('PA140', () => readDspack(join(OUT_DIR, 'bad-no-dspack.dspack')), 'missing dspack.json')
  await expectAsyncCode('PA142', () => readDspack(join(OUT_DIR, 'bad-version.dspack')), 'container version 4')
  await expectAsyncCode('PA141', () => readDspack(join(OUT_DIR, 'bad-manifest.dspack')), 'profile bundles empty')
  await expectAsyncCode('PA141', () => readDspack(join(OUT_DIR, 'bad-home.dspack')), 'dshhome contains web profile')

  // 4. 路径安全 / 排除
  for (const p of ['../evil', '/abs', 'C:\\x', 'a/../../b', '']) {
    if (isSafeRelPath(p)) fail(`isSafeRelPath should reject ${JSON.stringify(p)}`)
  }
  expectCode('PA143', () => validateManifest({ ...PROFILE_MANIFEST, files: [{ path: '../evil' }] }), 'files[].path traversal')
  // 真实市场里存在 dshVersion: "" 的包：按“未声明”处理，不报错
  if (validateManifest({ ...PROFILE_MANIFEST, dshVersion: '' }) === undefined) fail('empty dshVersion should be accepted')
  console.log('✔ empty dshVersion treated as unset')
  if (!isSafeRelPath('overrides/cordis.patch.yml')) fail('safe path rejected')
  if (!isExcludedRelPath('overrides/.credentials.yaml')) fail('credentials not excluded')
  if (!isExcludedRelPath('home/sessions/x.json')) fail('sessions not excluded')
  if (isExcludedRelPath('overrides/cordis.patch.yml')) fail('cordis.patch wrongly excluded')
  console.log('✔ path safety & export exclusions')

  // 5. digest
  {
    const buf = new TextEncoder().encode('abc')
    const sha = createSha(buf)
    if (!checkFileDigest(buf, sha, 3)) fail('digest ok case')
    if (checkFileDigest(buf, sha, 4)) fail('size mismatch must fail')
    if (checkFileDigest(buf, 'f'.repeat(64), 3)) fail('sha mismatch must fail')
    console.log('✔ digest/size verification')
  }

  // 6. 版本决策
  {
    const d1 = decideDshVersion({ dshVersion: '0.2.1-rc.2', dshVersions: ['0.2.1-rc.2', '0.2.2-alpha.2'], installed: ['0.2.1-rc.2'] })
    if (d1.version !== '0.2.1-rc.2' || d1.source !== 'pinned') fail(`decide pinned ${JSON.stringify(d1)}`)
    const d2 = decideDshVersion({ dshVersion: '0.2.1-rc.2', dshVersions: ['0.2.1-rc.2', '0.2.2-alpha.2'], installed: ['0.2.2-alpha.2'] })
    if (d2.version !== '0.2.2-alpha.2' || d2.source !== 'set-latest') fail(`decide set ${JSON.stringify(d2)}`)
    const d3 = decideDshVersion({ dshVersion: '0.2.1-rc.2', installed: [] })
    if (d3.source !== 'needs-install' || d3.version !== '0.2.1-rc.2') fail(`decide needs-install ${JSON.stringify(d3)}`)
    const d4 = decideDshVersion({ installed: ['0.1.0-rc.7'] })
    if (d4.source !== 'latest-installed' || d4.version !== '0.1.0-rc.7') fail(`decide latest-installed ${JSON.stringify(d4)}`)
    const d5 = decideDshVersion({ installed: [] })
    if (d5.source !== 'none') fail(`decide none ${JSON.stringify(d5)}`)
    if (compareLauncherVersion('1.2', '1.2.0') !== 0) fail('compare 1.2 vs 1.2.0')
    if (compareLauncherVersion('0.1.9', '0.1.10') >= 0) fail('compare numeric segments')
    console.log('✔ dsh version decision + launcher version compare')
  }

  // 7. launcher 判定
  {
    const norm = normalizeLaunchers({ pad: true, hdsl: '0.2.0', dshl: { supported: false, reason: 'x' }, official: { supported: true, minVersion: '1.0.0' } })
    if (!norm.pad.supported || norm.hdsl.minVersion !== '0.2.0' || norm.dshl.supported !== false) fail(`normalize ${JSON.stringify(norm)}`)
    const v1 = evaluateLaunchers(norm, { id: 'pad', version: '0.4.5' })
    if (v1.level !== 'ok') fail(`evaluate pad ${JSON.stringify(v1)}`)
    const v2 = evaluateLaunchers(norm, { id: 'hdsl', version: '0.1.0' })
    if (v2.level !== 'notice') fail(`evaluate hdsl ${JSON.stringify(v2)}`)
    const v3 = evaluateLaunchers(norm, { id: 'dshl' })
    if (v3.level !== 'warn') fail(`evaluate dshl ${JSON.stringify(v3)}`)
    const v4 = evaluateLaunchers(norm, { id: 'unknown' })
    if (v4.level !== 'notice') fail(`evaluate unknown ${JSON.stringify(v4)}`)
    if (evaluateLaunchers({}, { id: 'pad' }).level !== 'ok') fail('no decls must be ok')
    console.log('✔ launcher declarations + verdicts')
  }

  // 8. 转换 dspack → pad-pack v2
  let converted: PadPackV2 | undefined
  {
    const doc = await readDspack(join(OUT_DIR, 'profile.dspack'))
    try {
      const { pack, warnings } = dspackToPadPack(doc)
      converted = pack
      if (pack.schema !== 'pad-pack/v2' || pack.type !== 'profile') fail('convert schema/type')
      const dsh = pack.dsh as Record<string, unknown>
      if (dsh.version !== '0.2.1-rc.2') fail(`convert dsh.version ${dsh.version}`)
      if (dsh.profile !== 'web') fail(`convert profile ${dsh.profile}`)
      if ((dsh.deps as Record<string, string>)['demo-plugin'] !== '1.2.3') fail('convert deps')
      if (!Array.isArray(dsh.files)) fail('convert files')
      if (warnings.length > 0) fail(`unexpected profile warnings: ${warnings.join('; ')}`)
      console.log('✔ convert profile dspack → pad-pack v2')
    } finally {
      await doc.cleanup()
    }
  }

  // 9. dshhome 转换带 M4 警告
  {
    const doc = await readDspack(join(OUT_DIR, 'dshhome.dspack'))
    try {
      const { pack, warnings } = dspackToPadPack(doc)
      if (pack.type !== 'dshhome') fail('convert dshhome type')
      if (!warnings.some(w => w.includes('dshhome'))) fail(`missing dshhome warning: ${warnings.join('; ')}`)
      console.log('✔ convert dshhome dspack (with M4 warning)')
    } finally {
      await doc.cleanup()
    }
  }

  // 10. 导出 round-trip
  {
    if (!converted) fail('converted not set')
    const out = join(OUT_DIR, 'roundtrip.dspack')
    const result = await padPackToDspack(converted, { out })
    const doc = await readDspack(out)
    try {
      if (doc.name !== 'demo-pack' || doc.version !== '1.0.0') fail('roundtrip name/version')
      if (doc.dshVersion !== '0.2.1-rc.2') fail('roundtrip dshVersion')
      if (!Array.isArray(doc.dshVersions) || doc.dshVersions.length !== 2) fail('roundtrip dshVersions')
      const m = doc.manifest as Record<string, unknown>
      if (m.profileName !== 'web') fail(`roundtrip profileName ${m.profileName}`)
      if ((m.dependencies as Record<string, string>)['demo-plugin'] !== '1.2.3') fail('roundtrip dependencies')
      console.log(`✔ export round-trip (${result.path})`)
    } finally {
      await doc.cleanup()
    }
  }

  // 11. 导出安全过滤（layout）
  {
    const pack: PadPackV2 = {
      schema: 'pad-pack/v2',
      name: 'layout-pack',
      version: '1.0.0',
      type: 'profile',
      dsh: {
        version: '0.2.1-rc.2',
        profile: 'web',
        bundles: ['@deepseek-ai/dsh-base'],
        deps: { 'demo-plugin': '1.2.3' },
      },
    }
    const out = join(OUT_DIR, 'layout.dspack')
    const result = await padPackToDspack(pack, { out, layout: join(ROOT, 'layout') })
    const doc = await readDspack(out)
    try {
      if (doc.layout.overrides.includes('.credentials.yaml')) fail('credentials leaked into export')
      if (doc.layout.overrides.includes('cordis.patch.yml') === false) fail('normal override missing')
      if (doc.layout.home.includes('skills/whale/SKILL.md') === false) fail('home skill missing')
      if (!result.warnings.some(w => w.includes('排除'))) fail(`missing exclusion warning: ${result.warnings.join('; ')}`)
      console.log('✔ export excludes credentials/sessions, keeps normal files')
    } finally {
      await doc.cleanup()
    }
  }

  // 12. 导出拒绝未锁死依赖 / 缺版本
  {
    const unpinned: PadPackV2 = {
      schema: 'pad-pack/v2',
      name: 'unpinned',
      version: '1.0.0',
      type: 'profile',
      dsh: { version: '0.2.1-rc.2', profile: 'web', bundles: ['@deepseek-ai/dsh-base'], deps: { x: '^1.0.0' } },
    }
    await expectAsyncCode('PA148', () => padPackToDspack(unpinned, { out: join(OUT_DIR, 'unpinned.dspack') }), 'unpinned dependency')
    const noVersion: PadPackV2 = { ...unpinned, name: 'no-version', dsh: { profile: 'web', bundles: ['x'], deps: {} } } as PadPackV2
    await expectAsyncCode('PA145', () => padPackToDspack(noVersion, { out: join(OUT_DIR, 'no-version.dspack') }), 'missing dsh.version')
    console.log('✔ export guards (PA148 pinned deps, PA145 exact dsh version)')
  }

  console.log('\nALL PACKFORGE TESTS PASSED')
}

function createSha(buf: Uint8Array): string {
  return createHash('sha256').update(buf).digest('hex')
}

await main()
