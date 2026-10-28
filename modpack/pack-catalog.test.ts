#!/usr/bin/env bun
/**
 * 本机插件包目录（`packagent dsh launcher packs`）：
 * fixture 造 .pack.json / .pack.zip / .pinst.zip / 坏 zip + 坏 json，
 * 断言 kind/kernel/profile/components/units/installed、坏包进 errors（不抛）、
 * --query 筛选、--deep 有界遍历、只读（包文件与实例数不变），以及 launcher 命令注册 + --json 输出。
 * 另回归 `pack project <inst> <*.pack.zip>`：catalog 里的 zip 行点「用这个包建实例」要能投影，
 * 不能退化成对 zip 做 JSON.parse。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { writePackZip, writeZipFromDir } from '../../src/pack-archive.js'
import { packTmpDir } from '../../src/tmp-root.js'
import type { PackDoc } from '../../src/types.js'
import { createInstance, ensureRoot, seedFakeVersion } from './launcher.js'
import type { PackCatalogError, PackCatalogRow } from './pack-catalog.js'
import { scanPackCatalog } from './pack-catalog.js'
import { packProject } from './pack-ops.js'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const eqJson = (got: unknown, want: unknown, what: string): void => {
  const g = JSON.stringify(got)
  const w = JSON.stringify(want)
  if (g !== w) fail(`${what}: got ${g} want ${w}`)
}

const pick = (rows: PackCatalogRow[], kind: string): PackCatalogRow => {
  const row = rows.find((r) => r.kind === kind)
  if (!row) fail(`missing ${kind} row: ${JSON.stringify(rows.map((r) => r.kind))}`)
  return row
}

/** stdout 里最后一个 JSON 对象（契约只保证它是最后一个，不保证没有别的输出） */
function parseLastJsonObject(text: string): unknown {
  const trimmed = text.trim()
  try {
    return JSON.parse(trimmed)
  } catch {
    /* fall through */
  }
  const at = trimmed.lastIndexOf('\n{')
  if (at < 0) throw new Error('no JSON object found in stdout')
  return JSON.parse(trimmed.slice(at + 1))
}

const tmp = packTmpDir('pack-catalog')
try {
  const root = ensureRoot(join(tmp, 'launcher'))
  const packsDir = join(tmp, 'packs')
  mkdirSync(packsDir, { recursive: true })

  // ---- fixture 1: .pack.json（dsh.version=0.1.0-rc.7 / profile=web / 3 个 skill+mcp 组件）----
  const alphaPath = join(packsDir, 'alpha.pack.json')
  const alphaDoc: PackDoc = {
    schema: 'pack-agent.pack/v1',
    name: 'Alpha Pack',
    version: '1.2.3',
    description: 'alpha fixture pack',
    knowledge: { skills: [{ name: 'skill-a' }, { name: 'skill-b' }], rules: [] },
    tools: { mcp: [{ name: 'mcp-a', url: 'https://example.test/mcp' }] },
    dsh: { version: '0.1.0-rc.7', profile: 'web' },
  }
  writeFileSync(alphaPath, JSON.stringify(alphaDoc, null, 2))

  // ---- fixture 2: .pack.zip（用仓库现成写包函数生成；带实体 skills/，可被 pack project 投影）----
  const betaPath = join(packsDir, 'beta.pack.zip')
  const betaDoc: PackDoc = {
    schema: 'pack-agent.pack/v1',
    name: 'Beta Pack',
    version: '2.0.0',
    description: 'beta zip pack',
    knowledge: { skills: [{ name: 'skill-c' }], rules: [{ name: 'rule-a' }] },
    commands: { files: [{ name: 'cmd-a' }] },
    bundle: {
      portable: true,
      files: [
        {
          path: 'skills/skill-c/SKILL.md',
          content: '---\nname: skill-c\ndescription: beta skill\n---\n# skill-c\n\nBETASKILLBODY\n',
        },
      ],
    },
    dsh: { version: '^0.1.0-rc.7', profile: 'headless', plugins: [{ name: 'plugin-a' }] },
  }
  await writePackZip(betaDoc, betaPath)

  // ---- fixture 3: .pinst.zip（最小 instance.json，zip 根条目）----
  const pinstStage = join(tmp, 'pinst-stage')
  mkdirSync(pinstStage, { recursive: true })
  writeFileSync(
    join(pinstStage, 'instance.json'),
    JSON.stringify(
      {
        schema: 'pack-agent.launcher.instance/v2',
        id: 'gamma-inst',
        name: 'Gamma Instance',
        dsh: { version: '0.1.0-rc.7' },
        profile: { name: 'web', port: 'auto' },
        home: join(tmp, 'gamma-home'),
        workspace: { kind: 'owned', path: join(tmp, 'gamma-workspace') },
        status: 'ready',
        created: '',
        updated: '',
      },
      null,
      2,
    ),
  )
  const gammaPath = join(packsDir, 'gamma.pinst.zip')
  await writeZipFromDir(pinstStage, gammaPath)

  // ---- fixture 4/5: 坏包不抛（坏 zip + 半截 json）----
  const brokenZip = join(packsDir, 'broken.pack.zip')
  writeFileSync(brokenZip, 'definitely not a zip file')
  const brokenJson = join(packsDir, 'broken.pack.json')
  writeFileSync(brokenJson, '{ "name": "half-written", ')

  // ---- fixture 6: --deep 有界遍历（深度 4 命中，深度 5 不命中）----
  const deepRoot = join(tmp, 'deep-root')
  const deepHitDir = join(deepRoot, 'a', 'b', 'c', 'd')
  mkdirSync(deepHitDir, { recursive: true })
  const deepHitPath = join(deepHitDir, 'delta.pack.json')
  writeFileSync(
    deepHitPath,
    JSON.stringify({ name: 'Delta Deep', version: '0.0.1', dsh: { version: '0.1.0-rc.7' } }, null, 2),
  )
  const deepMissDir = join(deepHitDir, 'e')
  mkdirSync(deepMissDir, { recursive: true })
  writeFileSync(join(deepMissDir, 'epsilon.pack.json'), JSON.stringify({ name: 'Epsilon Too Deep' }))

  // ---- fixture 7: 第二个 --roots 根（验证 `--roots a;b` 的分号切分）----
  const morePacksDir = join(tmp, 'more-packs')
  mkdirSync(morePacksDir, { recursive: true })
  const zetaPath = join(morePacksDir, 'zeta.pack.json')
  writeFileSync(
    zetaPath,
    JSON.stringify({ name: 'Zeta Pack', version: '9.9.9', dsh: { version: '0.1.0-rc.7', profile: 'web' } }, null, 2),
  )

  // ---- fixture 8: 默认根（launcher 根文件 + library/packs），不传 --roots 也能扫到 ----
  const omegaPath = join(root.path, 'library', 'packs', 'omega.pack.json')
  mkdirSync(join(root.path, 'library', 'packs'), { recursive: true })
  writeFileSync(omegaPath, JSON.stringify({ name: 'Omega Library', version: '0.1.0', dsh: { version: '0.1.0-rc.7' } }))
  const dropPath = join(root.path, 'root-drop.pack.json')
  writeFileSync(dropPath, JSON.stringify({ name: 'Root Drop', version: '0.0.2' }))

  // ---- installed：instance.json 的 source.packId 对上 alpha ----
  const instanceDir = join(root.path, 'instances', 'alpha-home')
  mkdirSync(instanceDir, { recursive: true })
  writeFileSync(
    join(instanceDir, 'instance.json'),
    JSON.stringify(
      {
        schema: 'pack-agent.launcher.instance/v2',
        id: 'alpha-home',
        name: 'Alpha Home',
        dsh: { version: '0.1.0-rc.7' },
        profile: { name: 'web', port: 'auto' },
        workspace: { kind: 'owned', path: join(instanceDir, 'workspace') },
        status: 'ready',
        home: join(instanceDir, 'home'),
        source: { kind: 'pack', packId: 'alpha' },
        created: '',
        updated: '',
      },
      null,
      2,
    ),
  )

  const fixtureFiles = [alphaPath, betaPath, gammaPath, brokenZip, brokenJson]
  const instancesBefore = readdirSync(join(root.path, 'instances')).sort()

  // ---- 全量目录 ----
  const result = await scanPackCatalog(root, { roots: [packsDir, morePacksDir] })
  if (!result.ok) fail(`ok should be true, got ${JSON.stringify(result)}`)
  if (result.scanned < fixtureFiles.length) fail(`scanned ${result.scanned} < ${fixtureFiles.length}`)

  // 只断言 fixture 内的行（默认根/盘根发现的本机包不参与断言）
  const inside = result.packs.filter((p) => p.path.startsWith(packsDir))
  eqJson(
    inside.map((p) => [p.kind, p.title, p.id]),
    [
      ['pack.json', 'Alpha Pack', 'alpha'],
      ['pack.zip', 'Beta Pack', 'beta'],
      ['pinst.zip', 'Gamma Instance', 'gamma-inst'],
    ],
    'fixture rows (按 title 排序)',
  )

  const alpha = pick(inside, 'pack.json')
  eqJson(
    {
      title: alpha.title,
      version: alpha.version,
      kernel: alpha.kernel,
      profile: alpha.profile,
      description: alpha.description,
      source: alpha.source,
      installed: alpha.installed,
    },
    {
      title: 'Alpha Pack',
      version: '1.2.3',
      kernel: '0.1.0-rc.7',
      profile: 'web',
      description: 'alpha fixture pack',
      source: `sidecar: ${packsDir}`,
      installed: ['alpha-home'],
    },
    'pack.json 元数据/来源/installed',
  )
  eqJson(
    alpha.components,
    { skill: 2, mcp: 1, plugin: 0, rule: 0, command: 0, hook: 0 },
    'pack.json components',
  )
  eqJson(alpha.units, 3, 'pack.json units')

  const beta = pick(inside, 'pack.zip')
  eqJson(
    {
      title: beta.title,
      version: beta.version,
      kernel: beta.kernel,
      profile: beta.profile,
      installed: beta.installed,
    },
    {
      title: 'Beta Pack',
      version: '2.0.0',
      kernel: '^0.1.0-rc.7',
      profile: 'headless',
      installed: [],
    },
    'pack.zip 元数据（kernel range 保留原文）',
  )
  eqJson(
    beta.components,
    { skill: 1, mcp: 0, plugin: 1, rule: 1, command: 1, hook: 0 },
    'pack.zip components',
  )
  eqJson(beta.units, 4, 'pack.zip units')

  const gamma = pick(inside, 'pinst.zip')
  eqJson(
    {
      id: gamma.id,
      title: gamma.title,
      kernel: gamma.kernel,
      profile: gamma.profile,
      components: gamma.components,
      units: gamma.units,
    },
    {
      id: 'gamma-inst',
      title: 'Gamma Instance',
      kernel: '0.1.0-rc.7',
      profile: 'web',
      components: null,
      units: 0,
    },
    'pinst.zip 字段（components=null）',
  )

  // 第二个 --roots 根（分号切分在 CLI 侧测；函数侧传数组）
  const zeta = result.packs.find((p) => p.path === zetaPath)
  if (!zeta) fail(`--roots 第二个根没扫到 ${zetaPath}`)
  eqJson([zeta.kind, zeta.title, zeta.source], ['pack.json', 'Zeta Pack', `sidecar: ${morePacksDir}`], '第二个 roots 根')

  // 坏包：不抛、进 errors、其余行还在
  for (const broken of [brokenZip, brokenJson]) {
    const hit = result.errors.find((e: PackCatalogError) => e.path === broken)
    if (!hit) fail(`errors 里没有坏包 ${broken}：${JSON.stringify(result.errors)}`)
    if (!hit.message) fail(`坏包 ${broken} 的 message 为空`)
  }
  if (inside.length !== 3) fail(`fixture 应恰好 3 行，实际 ${inside.length}`)

  // 只读：包文件没被删、没偷偷建实例
  for (const file of fixtureFiles) {
    if (!existsSync(file)) fail(`只读命令删了包文件：${file}`)
  }
  const instancesAfter = readdirSync(join(root.path, 'instances')).sort()
  eqJson(instancesAfter, instancesBefore, '只读：没创建/删除实例')

  // ---- --query 筛选（大小写不敏感）----
  const gammaQuery = await scanPackCatalog(root, { roots: [packsDir], query: 'GAMMA' })
  const gammaHits = gammaQuery.packs.filter((p) => p.path.startsWith(packsDir))
  eqJson(gammaHits.map((p) => p.id), ['gamma-inst'], '--query GAMMA')
  if (gammaQuery.scanned < fixtureFiles.length) fail('--query 不应影响 scanned')

  // ---- --deep 有界遍历：深度 4 命中 + 深度 5 不命中 + query 同时生效 ----
  const deepResult = await scanPackCatalog(root, { deep: [deepRoot], query: 'delta' })
  const deepHits = deepResult.packs.filter((p) => p.path.startsWith(deepRoot))
  eqJson(deepHits.map((p) => [p.id, p.kernel]), [['delta', '0.1.0-rc.7']], '--deep 深度 4 命中')
  if (deepResult.packs.some((p) => p.path.endsWith('epsilon.pack.json'))) {
    fail('--deep 深度 5 的文件不该被发现')
  }

  // ---- 默认根（不传 roots）：launcher 根文件 + library/packs 都能发现 ----
  const defaults = await scanPackCatalog(root, {})
  const defaultRows = defaults.packs.filter((p) => p.path.startsWith(root.path))
  const omega = defaultRows.find((p) => p.path === omegaPath)
  if (!omega) fail(`默认根没扫到 library/packs 里的 ${omegaPath}`)
  eqJson([omega.kind, omega.source], ['pack.json', 'library'], '默认根 library/packs 的 source')
  const drop = defaultRows.find((p) => p.path === dropPath)
  if (!drop) fail(`默认根没扫到 launcher 根文件 ${dropPath}`)
  eqJson([drop.kind, drop.source], ['pack.json', 'launcher root'], '默认根 launcher root 的 source')

  // ---- launcher 命令注册 + --json 输出（C# 侧真实调用形状：--root/--json 被顶层吃掉，
  //      packs 之后的 --roots 用分号切多个根、--deep 深扫）----
  const cliRun = spawnSync(
    process.execPath,
    [
      join(import.meta.dirname, 'cli.ts'),
      'launcher',
      '--root',
      root.path,
      '--json',
      'packs',
      '--roots',
      `${packsDir};${morePacksDir}`,
      '--deep',
      deepRoot,
    ],
    { encoding: 'utf8', timeout: 60_000 },
  )
  if (cliRun.status !== 0) fail(`launcher packs exit ${cliRun.status}\n${cliRun.stderr || cliRun.stdout}`)
  let cliJson: { ok?: boolean; scanned?: number; packs?: PackCatalogRow[]; errors?: PackCatalogError[] }
  try {
    cliJson = parseLastJsonObject(cliRun.stdout || '') as typeof cliJson
  } catch (error) {
    fail(`launcher packs --json 输出不是 JSON（${String(error)}）：${(cliRun.stdout || '').slice(0, 800)}`)
  }
  if (cliJson.ok !== true) fail(`CLI ok 应为 true：${JSON.stringify(cliJson).slice(0, 400)}`)
  const cliRows = cliJson.packs ?? []
  eqJson(
    cliRows.filter((p) => p.path.startsWith(packsDir)).map((p) => [p.kind, p.id]),
    [
      ['pack.json', 'alpha'],
      ['pack.zip', 'beta'],
      ['pinst.zip', 'gamma-inst'],
    ],
    `CLI 三行都对（all=${JSON.stringify(cliRows.map((p) => [p.kind, p.id, p.path]))}）`,
  )
  // `--roots a;b` 的第二段根也扫到了（source 能区分来源根）
  const cliZeta = cliRows.find((p) => p.path === zetaPath)
  if (!cliZeta) fail(`CLI --roots a;b 没扫到第二段根 ${morePacksDir}`)
  eqJson([cliZeta.kind, cliZeta.source], ['pack.json', `sidecar: ${morePacksDir}`], 'CLI 第二段 roots 的 source')
  // `--deep` 透传：深度 4 的 delta 命中，深度 5 的 epsilon 不命中
  const delta = cliRows.find((p) => p.path === deepHitPath)
  if (!delta) fail(`CLI --deep 没扫到 ${deepHitPath}`)
  eqJson([delta.kind, delta.kernel, delta.source], ['pack.json', '0.1.0-rc.7', `deep: ${deepRoot}`], 'CLI --deep 的 source')
  if (cliRows.some((p) => p.path.endsWith('epsilon.pack.json'))) fail('CLI --deep 深度 5 的文件不该被发现')
  if (!(cliJson.errors ?? []).some((e) => e.path === brokenZip)) fail('CLI errors 里没有坏 zip')

  // ---- 投影修复：catalog 的 .pack.zip 行能被 `pack project` 用（不是对 zip 做 JSON.parse）----
  const fakeJs = `const args = process.argv.slice(2)
if (args.includes('--version') || args.includes('-V')) {
  process.stdout.write('@@VERSION@@\\n')
  process.exit(0)
}
setInterval(() => {}, 60000)
`
  seedFakeVersion(root, '0.1.0-rc.7', fakeJs)
  const projectInst = createInstance(root, { name: 'project-home', version: '0.1.0-rc.7', profile: 'web' })
  const workspacePath = projectInst.workspace.path
  const modpacksDir = join(workspacePath, '.agent-pack', 'modpacks')
  const normPath = (p: string): string => p.replace(/\//g, '\\').toLowerCase()

  // 真 CLI 形状：launcher --root R --json pack project <inst> <zip>
  const projectRun = spawnSync(
    process.execPath,
    [
      join(import.meta.dirname, 'cli.ts'),
      'launcher',
      '--root',
      root.path,
      '--json',
      'pack',
      'project',
      projectInst.id,
      betaPath,
    ],
    { encoding: 'utf8', timeout: 120_000 },
  )
  if (projectRun.status !== 0) {
    fail(`pack project <zip> exit ${projectRun.status}\n${projectRun.stderr || projectRun.stdout}`)
  }
  let zipProject: { dir?: string; id?: string; npmName?: string }
  try {
    zipProject = parseLastJsonObject(projectRun.stdout || '') as typeof zipProject
  } catch (error) {
    fail(`pack project <zip> 输出不是 JSON（${String(error)}）：${(projectRun.stdout || '').slice(0, 800)}`)
  }
  if (!zipProject.dir || !zipProject.id) fail(`pack project <zip> 缺 dir/id：${JSON.stringify(zipProject)}`)
  const zipModpackDir = join(modpacksDir, zipProject.id)
  if (normPath(zipProject.dir) !== normPath(zipModpackDir)) {
    fail(`zip 投影目录应在实例 workspace modpacks 下：got ${zipProject.dir} want ${zipModpackDir}`)
  }
  const zipCatalogPath = join(zipProject.dir, 'catalog.json')
  if (!existsSync(zipCatalogPath)) fail(`zip 投影缺 catalog.json：${zipCatalogPath}`)
  const zipCatalog = JSON.parse(readFileSync(zipCatalogPath, 'utf8')) as { name?: string; units?: unknown[] }
  eqJson(zipCatalog.name, 'Beta Pack', 'zip 投影 catalog.name')
  if (!Array.isArray(zipCatalog.units) || zipCatalog.units.length === 0) fail('zip 投影 catalog.json units 为空')

  // .pack.json 老路径回归：直调 packProject，不能因这次改动退化
  const simpleJsonPath = join(tmp, 'project-inputs', 'simple.pack.json')
  mkdirSync(dirname(simpleJsonPath), { recursive: true })
  writeFileSync(
    simpleJsonPath,
    JSON.stringify({
      name: 'Simple Project',
      version: '0.1.0',
      knowledge: { skills: [{ name: 'simple-skill' }] },
      bundle: {
        portable: true,
        files: [
          {
            path: 'skills/simple-skill/SKILL.md',
            content: '---\nname: simple-skill\ndescription: simple\n---\n# simple-skill\n',
          },
        ],
      },
      dsh: { version: '0.1.0-rc.7' },
    } satisfies PackDoc),
  )
  const simpleResult = await packProject(root, projectInst.id, simpleJsonPath)
  if (!existsSync(join(simpleResult.dir, 'catalog.json'))) {
    fail(`.pack.json 投影缺 catalog.json：${simpleResult.dir}`)
  }
  const modpackDirs = readdirSync(modpacksDir).sort()
  if (!modpackDirs.includes(zipProject.id) || !modpackDirs.includes(simpleResult.id)) {
    fail(`多包并存失败（zip + json 应同时投影在 modpacks 下）：${JSON.stringify(modpackDirs)}`)
  }

  console.log(
    `✓ packs catalog：.pack.json/.pack.zip/.pinst.zip 三行 + 坏包 errors（不抛）+ --query/--deep + CLI --json` +
      `（scanned=${result.scanned}, fixture=3, errors=${result.errors.length}）`,
  )
  console.log(
    `✓ pack project：.pack.zip 投影成功（${zipProject.id}/catalog.json，units=${zipCatalog.units.length}）` +
      ` + .pack.json 老路径回归 + 多包并存`,
  )
} finally {
  rmSync(tmp, { recursive: true, force: true })
}

console.log('✓ pack-catalog')
