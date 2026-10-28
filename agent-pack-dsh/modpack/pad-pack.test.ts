#!/usr/bin/env bun
/**
 * PAD「插件包」二期：本机包目录（`pad cli pack list`）与「用包建实例」的失败链
 * （`pad cli instance create --pack`），外加源码回归。
 *
 * 契约（与 pad/Core/Packs.cs、Launcher.cs、Cli.cs、Models.cs、AppState.cs、
 * Views/VersionsView.xaml、modpack/pack-catalog.ts 对齐）：
 * - `<PACK_LAUNCHER_ROOT>/library/packs/*.pack.json` 是启动器自己的包库；C# 的目录命令
 *   转发给 TypeScript：`packagent dsh launcher --root <root> --json packs`，
 *   收到的字段是 lowerCamel（PackCard 上全是 JsonPropertyName）：id/title/version/path/
 *   kind/kernel/profile/description/source/installed/components。
 *   - source 是发现来源：library/packs 来的行 = "library"；exe 旁 = "sidecar: ..."。
 *   - components = { skill, mcp, plugin, rule, command, hook } 计数；读不懂时为 null。
 * - `pack list --json --out <file>` 把整个目录写进文件，文件是纯 JSON 数组（没有人类可读行）。
 * - `instance create --pack <id|path> [--name <n>] [--profile <p>]`：先在本机目录里找卡，
 *   找不到 = PA012 "no such pack"（help 指向 `pad cli pack list`）；找到就按包声明的内核
 *   建实例 —— 该发行号没装 = CreateInstance 的 PA001 "that release is not installed"，
 *   而 PA001 在 CreateDirectory 之前抛出，所以失败后不能留下 instances/<id> 目录。
 * - 成功建实例 + 投影链需要 rust+bun+完整 repo（CI 太慢），本测试不跑；成功链路由
 *   Lead 真机证据覆盖，这里只锁「目录 JSON」与「失败的两种诊断」。
 *
 * 全程幂等：fixture 在 packTmpDir 下，finally 删除；PACK_LAUNCHER_ROOT 隔离，不碰真实
 * launcher；总耗时 < 40s。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTmpDir } from '../../src/tmp-root.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const pretty = (v: unknown): string => {
  try {
    return JSON.stringify(v, null, 2) ?? String(v)
  } catch {
    return String(v)
  }
}

const pad = join(import.meta.dirname, '../pad') // agent-pack-dsh/pad
const repoRoot = join(import.meta.dirname, '../..') // 仓库根（PACK_AGENT_REPO）
const exampleSrc = join(pad, '../portable/example-pack/pack.json')
const exe = join(pad, 'bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
const SPAWN_TIMEOUT = 45_000
const TEST_BUDGET = 40_000

// ---- 源码回归：实现还在、关键路径没被删 ----
//
// 只做「有这回事」的宽松匹配（措辞可能被改），宁可少断言也不把好实现判红。

const sourceFiles = new Map<string, string>()
for (const rel of [
  'Core/Models.cs',
  'Core/Packs.cs',
  'Core/Launcher.cs',
  'Core/Cli.cs',
  'Core/AppState.cs',
  'Core/PackMaker.cs',
  'Views/VersionsView.xaml',
]) {
  const abs = join(pad, rel)
  if (existsSync(abs)) sourceFiles.set(rel, readFileSync(abs, 'utf8'))
}
const textOf = (rel: string): string => sourceFiles.get(rel) ?? ''
const hit = (text: string, needle: string | RegExp): boolean =>
  typeof needle === 'string' ? text.includes(needle) : needle.test(text)
/** 某个标记实际落在哪个源文件里（文件改名/挪位置时给个定位线索）。 */
const whereIs = (needle: string | RegExp): string | null => {
  for (const [rel, text] of sourceFiles) if (hit(text, needle)) return rel
  return null
}
const must = (rel: string, needle: string | RegExp, why: string): void => {
  if (!sourceFiles.has(rel)) fail(`缺 pad/${rel}（${why}）`)
  if (hit(textOf(rel), needle)) return
  const at = whereIs(needle)
  fail(`pad/${rel} ${why}：找不到 ${String(needle)}${at ? `（这个标记现在在 ${at} 里）` : ''}`)
}

// Models：实例来源（source 字段 + InstanceSource 类型），老 instance.json 缺省 = kernel。
must('Core/Models.cs', 'InstanceSource', '没有实例来源类型（用包建的实例要记住从哪个包来）')
must(
  'Core/Models.cs',
  /JsonPropertyName\("source"\)\]\s*public\s+InstanceSource\?/,
  'Instance 上没有 Json source 字段（instance.json 记不住包的来源）',
)
// Packs：本机包目录 + 收集进 library/packs + 从 CLI 输出里取 JSON（FirstJson/CliJson）。
must('Core/Packs.cs', 'Catalog(', '没有本机包目录（Catalog）')
must('Core/Packs.cs', 'CollectToLibrary', '没有把包收进 library/packs 的 CollectToLibrary')
if (!hit(textOf('Core/Packs.cs'), 'FirstJson') && !hit(textOf('Core/Packs.cs'), 'CliJson'))
  fail(`pad/Core/Packs.cs ${'没有解析 CLI JSON 的 FirstJson / CliJson（目录命令读不到 packagent 的输出）'}`)
// Launcher：从包建实例的 CreateFromPack。
must('Core/Launcher.cs', 'CreateFromPack', '没有 CreateFromPack（无法用包建实例）')
// Cli：目录命令与 --pack 参数都接上了。
must('Core/Cli.cs', 'case "pack" when cmd == "list"', '没有 `pad cli pack list` 命令分派')
must('Core/Cli.cs', '--pack', '`instance create` 没接 --pack 参数')
// 视图：整合包目录与「用这个整合包新建实例」入口。用户 2026-10-27：「插件不叫插件包」。
must('Views/VersionsView.xaml', '整合包', '版本页没有「整合包」目录入口')
must('Views/VersionsView.xaml', '新建实例', '没有「新建实例」按钮文案')
must('Views/VersionsView.xaml', 'MakePack_Click', '版本页没有「制作整合包」入口')
must('Core/PackMaker.cs', 'packmaker.exe', '制作整合包没有走 Go 的 packmaker')
// AppState：目录的加载入口与可观察集合。
must('Core/AppState.cs', 'ReloadPackCatalog', '没有 ReloadPackCatalog（界面没法刷新包目录）')
must('Core/AppState.cs', 'PackCatalog', '没有 PackCatalog 可观察集合（界面绑不到包目录）')

console.log('✓ 源码回归：Models/Packs/Launcher/Cli/AppState/VersionsView 关键实现都在')

// ---- 实跑前置：PACK_AGENT_REPO 指向仓库根（C# 用 <repo>/bin/packagent.js 跑 TS） ----
if (!existsSync(join(repoRoot, 'bin', 'packagent.js')))
  fail(`找不到 ${join(repoRoot, 'bin', 'packagent.js')}（PACK_AGENT_REPO 需要它；repoRoot=${repoRoot}）`)

if (process.platform !== 'win32' || !existsSync(exe)) {
  console.log(`· 跳过 PAD 实跑：${process.platform === 'win32' ? '没有构建产物 ' : '非 win32 平台 '}${exe}`)
  console.log('✓ pad-pack（源码回归已过；PAD 实跑跳过）')
  process.exit(0)
}

// 包目录行的字段容错读取（契约是 lowerCamel；万一改成 PascalCase 也不该误判）。
type Row = Record<string, unknown>
const rows = (v: unknown): Row[] => (Array.isArray(v) ? (v as Row[]) : [])
const pick = (row: unknown, name: string): unknown => {
  if (row === null || typeof row !== 'object') return undefined
  const obj = row as Row
  return obj[name] ?? obj[name[0]!.toUpperCase() + name.slice(1)]
}
const str = (row: unknown, name: string): string => String(pick(row, name) ?? '')
/** Windows 路径比较：斜杠归一、忽略大小写和尾部反斜杠。 */
const norm = (p: unknown): string =>
  String(p ?? '').trim().replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase()
const samePath = (a: unknown, b: unknown): boolean => norm(a) === norm(b)

const started = Date.now()
const tmp = packTmpDir('pad-pack')
const root = join(tmp, 'launcher') // PACK_LAUNCHER_ROOT，别污染真实 launcher
const packsDir = join(root, 'library', 'packs')
const out = join(tmp, 'pack-list.json')
const logFile = join(root, 'pad-cli.log')

try {
  // ---- fixture：<PACK_LAUNCHER_ROOT>/library/packs/example.pack.json（仓库例包的副本） ----
  mkdirSync(packsDir, { recursive: true })
  const exampleText = readFileSync(exampleSrc, 'utf8')
  const example = JSON.parse(exampleText) as {
    name?: string
    dsh?: { version?: string; profile?: string }
    knowledge?: { skills?: unknown[] }
  }
  if (example?.name !== 'example')
    fail(`例包名应是 example，实际 ${JSON.stringify(example?.name)}（${exampleSrc}）`)
  const wantKernel = '0.1.0-rc.7'
  if (example?.dsh?.version !== wantKernel)
    fail(`例包的 dsh.version 应是 ${wantKernel}，实际 ${JSON.stringify(example?.dsh?.version)}（${exampleSrc} 变了吗？）`)
  const fixtureSkills = example?.knowledge?.skills?.length ?? 0
  if (fixtureSkills < 1) fail(`例包应至少有 1 个 skill，实际 ${fixtureSkills}（${exampleSrc} 变了吗？）`)
  const packPath = join(packsDir, 'example.pack.json')
  writeFileSync(packPath, exampleText)

  const env = { ...process.env, PACK_LAUNCHER_ROOT: root, PACK_AGENT_REPO: repoRoot }
  /** WinExe 的 stdout 可能借不到控制台；pad-cli.log 一定收到 Say() 的每一行，所以两份都合。 */
  const logText = (): string => (existsSync(logFile) ? readFileSync(logFile, 'utf8') : '')
  const runCli = (args: string[]): { status: number | null; text: string; error: Error | undefined } => {
    const before = logText().length
    const run = spawnSync(exe, args, { env, encoding: 'utf8', timeout: SPAWN_TIMEOUT })
    const logDelta = logText().slice(before) // 只取本次新增，别把上一条命令的输出算进来
    const text = [run.stdout ?? '', run.stderr ?? '', logDelta].filter((s) => s.length > 0).join('\n')
    return { status: run.status, text, error: run.error }
  }

  // ---- 1) cli pack list --json --out：目录里能看到 fixture 里的 example 包 ----
  const list = runCli(['cli', 'pack', 'list', '--json', '--out', out])
  if (list.error) fail(`pad cli pack list 起不来/超时：${list.error.message}\n${list.text}`)
  if (list.status !== 0) fail(`pad cli pack list 退出 ${list.status}（bin 里还是旧版 exe？）\n${list.text}`)
  if (!existsSync(out)) fail(`pad cli pack list 没写 --out ${out}\n${list.text}`)

  let cards: unknown
  try {
    cards = JSON.parse(readFileSync(out, 'utf8'))
  } catch (error) {
    fail(`--out 应是纯 JSON（解析失败：${String(error)}）\n${readFileSync(out, 'utf8').slice(0, 2000)}\n--- 命令输出 ---\n${list.text}`)
  }
  if (!Array.isArray(cards))
    fail(`pack list --json 的 --out 应是 JSON 数组，实际 ${JSON.stringify(cards).slice(0, 400)}\n${list.text}`)
  if (cards.length < 1)
    fail(`本机包目录应至少 1 条（fixture 的 ${packPath}）\n${pretty(cards)}\n${list.text}`)

  const exampleRow = rows(cards).find((row) => str(row, 'title') === 'example')
  if (!exampleRow) fail(`包目录里没有 title=example 的一行\n${pretty(cards)}`)
  if (str(exampleRow, 'kernel') !== wantKernel)
    fail(`example 行的 kernel 应是 ${wantKernel}，实际 ${JSON.stringify(str(exampleRow, 'kernel'))}\n${pretty(exampleRow)}`)
  const source = str(exampleRow, 'source')
  if (!source.includes('library'))
    fail(`example 行的 source 应含 "library"（从 library/packs 发现），实际 ${JSON.stringify(source)}\n${pretty(exampleRow)}`)
  const components = pick(exampleRow, 'components')
  if (components === null || typeof components !== 'object')
    fail(`example 行的 components 应是对象（skill>=1），实际 ${JSON.stringify(components)}\n${pretty(exampleRow)}`)
  const skill = Number(pick(components, 'skill') ?? 0)
  const total = ['skill', 'mcp', 'plugin', 'rule', 'command', 'hook']
    .reduce((n, kind) => n + Number(pick(components, kind) ?? 0), 0)
  if (!(skill >= 1) || !(total >= 1))
    fail(`example 行应有 skill>=1（fixture 里 ${fixtureSkills} 个），实际 skill=${skill} total=${total}\n${pretty(exampleRow)}`)
  if (!samePath(pick(exampleRow, 'path'), packPath))
    fail(`example 行的 path 应指向 fixture ${packPath}，实际 ${JSON.stringify(str(exampleRow, 'path'))}\n${pretty(exampleRow)}`)
  if (!existsSync(str(exampleRow, 'path')))
    fail(`example 行的 path 在磁盘上不存在：${str(exampleRow, 'path')}\n${pretty(exampleRow)}`)
  console.log(
    `  目录 ${cards.length} 条 · example：内核 ${wantKernel} · 来源 ${source} ·` +
      ` 组件 ${skill} skill / ${total} 个 · ${str(exampleRow, 'path')}`,
  )

  // ---- 2) 该 root 没装内核：PA001，且不许留下实例目录 ----
  // CreateInstance 在 CreateDirectory 之前查 DshBin：失败后 instances/x 不能出现。
  const instX = join(root, 'instances', 'x')
  const missingKernel = runCli(['cli', 'instance', 'create', '--pack', 'example', '--name', 'x'])
  if (missingKernel.error)
    fail(`pad cli instance create --pack 起不来/超时：${missingKernel.error.message}\n${missingKernel.text}`)
  if (missingKernel.status === 0)
    fail(`该 root 没装内核 ${wantKernel}，instance create --pack example 应失败，实际退出 0\n${missingKernel.text}`)
  for (const needle of ['error[PA001]', 'that release is not installed']) {
    if (!missingKernel.text.includes(needle))
      fail(`缺内核的报错应含 ${JSON.stringify(needle)}（退出 ${missingKernel.status}）\n${missingKernel.text}`)
  }
  if (existsSync(instX))
    fail(`PA001 失败后不该建出实例目录 ${instX}（检查是不是把 CreateDirectory 挪到了内核检查之前）\n${missingKernel.text}`)
  console.log(`  缺内核：退出 ${missingKernel.status} + error[PA001] "that release is not installed"，且没建 instances/x`)

  // ---- 3) 找不到包：PA012，并指向 `pad cli pack list` ----
  const instY = join(root, 'instances', 'y')
  const noPack = runCli(['cli', 'instance', 'create', '--pack', 'no-such-pack', '--name', 'y'])
  if (noPack.error) fail(`pad cli instance create --pack 起不来/超时：${noPack.error.message}\n${noPack.text}`)
  if (noPack.status === 0)
    fail(`--pack no-such-pack 应失败（PA012），实际退出 0\n${noPack.text}`)
  for (const needle of ['error[PA012]', 'pad cli pack list']) {
    if (!noPack.text.includes(needle))
      fail(`找不到包的报错应含 ${JSON.stringify(needle)}（退出 ${noPack.status}）\n${noPack.text}`)
  }
  if (existsSync(instY)) fail(`PA012 失败后不该建出实例目录 ${instY}\n${noPack.text}`)
  console.log(`  找不到包：退出 ${noPack.status} + error[PA012] "no such pack"，help 指向 pad cli pack list`)
} finally {
  try {
    rmSync(tmp, { recursive: true, force: true })
  } catch {
    /* 被占用时留 tmp，不影响下次（目录名带时间戳） */
  }
}

const elapsed = Date.now() - started
if (elapsed >= TEST_BUDGET) fail(`pad-pack 全程 ${elapsed}ms ≥ ${TEST_BUDGET / 1000}s（性能回退？）`)
console.log(
  `✓ pad-pack（pack list --json --out 目录行 kernel/source/components/path；缺内核 PA001 不建目录；` +
    `找不到包 PA012；源码回归，${elapsed}ms）`,
)
