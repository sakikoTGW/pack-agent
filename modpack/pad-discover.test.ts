#!/usr/bin/env bun
/**
 * PAD 自动探测：假桌面壳（asar 里捆内核）/ TUI home / 启动脚本 / appdata 形 home /
 * npx 形内核，全塞进 PAD_DISCOVER_EXTRA，跑真 exe 的 `cli discover --json` 核对 Inventory。
 * 另加源码断言，防止 asar 读取器 / 内核扫描 / 发现逻辑被删回旧版。
 *
 * 契约（与 pad/Core/Inventory.cs、Asar.cs、DshKernels.cs、Discover.cs 对齐）：
 * - asar 头 16 字节 = [u32=4][u32=L+8][u32=L+4][u32=L]，L = headerJson 字节数；
 *   JSON 从 offset 16 开始，数据区起点 = 8 + u32[4] = L+16；文件 entry = { size, offset: String }。
 * - 内核 Kernel{Version,Identity,Source,Bin,PackageDir,OwnerExe}，asar 内 Identity 形如
 *   "<asar 路径>!<内部目录>"，只有启动脚本声明时为 "declared:@deepseek-ai/dsh@<ver>"。
 * - 载具 Shell{Exe,...,KernelVersion,KernelIdentity} / Tui{Package,...,DeclaredKernel}。
 * - Home 是带 profiles/ 或 storages/ 的目录。
 * - 二期：起一个真子进程（env DSH_HOME=fixture tui-home、argv --profile dsh-tui）当「正在跑的 TUI」，
 *   断言 Inventory.Tui 的 Running/Pid 和 `pad cli procs --json` 的 DshHome/ProfileArg 都能看到它。
 * 输出 JSON 字段是 PascalCase（Cli.cs 的 JsonSerializer 没配置命名策略）。
 */
import { spawn, spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTmpDir } from '../../src/tmp-root.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const pretty = (v: unknown) => JSON.stringify(v, null, 2)
const pad = join(import.meta.dirname, '../pad')
const coreDir = join(pad, 'Core')

// ---- 手写 asar 打包器（~30 行；格式与 pad/Core/Asar.cs 的读法互为契约）----

type AsarNode = { content?: string; files?: Record<string, AsarNode> }

/** 把 { 内部路径: 文本 } 按 asar 布局打成二进制。 */
function packAsar(entries: Record<string, string>): Buffer {
  const root: AsarNode = { files: {} }
  for (const [innerPath, content] of Object.entries(entries)) {
    const parts = innerPath.split('/').filter((p) => p.length > 0)
    let dir = root
    for (let i = 0; i < parts.length; i++) {
      const key = parts[i]!
      const isLeaf = i === parts.length - 1
      const files = dir.files ?? (dir.files = {})
      const child = files[key] ?? (files[key] = isLeaf ? { content } : { files: {} })
      dir = child
    }
  }

  const chunks: Buffer[] = []
  let cursor = 0
  const emit = (node: AsarNode, into: Record<string, unknown>): void => {
    for (const [name, child] of Object.entries(node.files ?? {})) {
      if (child.content !== undefined) {
        const bytes = Buffer.from(child.content, 'utf8')
        into[name] = { size: bytes.length, offset: String(cursor) }
        cursor += bytes.length
        chunks.push(bytes)
      } else {
        const sub: Record<string, unknown> = {}
        emit(child, sub)
        into[name] = { files: sub }
      }
    }
  }
  const tree: Record<string, unknown> = {}
  emit(root, tree)

  const headerJson = JSON.stringify({ files: tree })
  const L = Buffer.byteLength(headerJson, 'utf8')
  const header = Buffer.alloc(16)
  header.writeUInt32LE(4, 0)
  header.writeUInt32LE(L + 8, 4)
  header.writeUInt32LE(L + 4, 8)
  header.writeUInt32LE(L, 12)
  return Buffer.concat([header, Buffer.from(headerJson, 'utf8'), ...chunks])
}

/** 自检：头字段 / 数据区起点 / 每个 entry 的字节都对得上，免得测试自己把格式写错。 */
function checkAsar(blob: Buffer, entries: Record<string, string>): void {
  const L = blob.readUInt32LE(12)
  const dataBase = 8 + blob.readUInt32LE(4)
  if (blob.readUInt32LE(0) !== 4 || blob.readUInt32LE(4) !== L + 8 || blob.readUInt32LE(8) !== L + 4)
    fail('asar 自检：头 16 字节不对（应为 [4][L+8][L+4][L]）')
  if (dataBase !== L + 16) fail(`asar 自检：数据区起点应 ${L + 16}，实际 ${dataBase}`)
  const json = JSON.parse(blob.subarray(16, 16 + L).toString('utf8')) as {
    files?: Record<string, { files?: Record<string, unknown> }>
  }
  for (const [innerPath, content] of Object.entries(entries)) {
    let node: Record<string, unknown> | undefined = json as Record<string, unknown>
    for (const part of innerPath.split('/')) {
      node = (node?.files as Record<string, Record<string, unknown>> | undefined)?.[part]
    }
    if (!node) fail(`asar 自检：${innerPath} 不在头 JSON 里`)
    const offset = Number(node.offset)
    const size = Number(node.size)
    if (!Number.isFinite(offset) || !Number.isFinite(size)) fail(`asar 自检：${innerPath} 缺 size/offset`)
    const got = blob.subarray(dataBase + offset, dataBase + offset + size).toString('utf8')
    if (got !== content) fail(`asar 自检：${innerPath} 的字节对不上`)
  }
}

// 打包器自检：格式写错当场炸，别把锅甩给读方（也保证 fixture 一定是合法 asar）。
checkAsar(packAsar({ 'a/b.txt': 'hello', 'c/d/e.json': '{"k":1}' }), {
  'a/b.txt': 'hello',
  'c/d/e.json': '{"k":1}',
})
console.log('✓ asar 打包器格式自检（[4][L+8][L+4][L] / dataBase=L+16 / entry offset 字符串）')

// ---- 源码断言：实现还在、关键路径没被删 ----
//
// 只做「有这回事」的宽松匹配（措辞可能被改），宁可少断言也不把好实现判红。

const coreSources = new Map<string, string>()
for (const name of readdirSync(coreDir)) {
  if (name.endsWith('.cs')) coreSources.set(name, readFileSync(join(coreDir, name), 'utf8'))
}
const has = (file: string, ...patterns: (string | RegExp)[]): boolean => {
  const text = coreSources.get(file)
  if (text === undefined) return false
  return patterns.some((p) => (typeof p === 'string' ? text.includes(p) : p.test(text)))
}
/** 某个标记实际落在 Core 的哪个文件里（文件改名时给个定位线索）。 */
const whereIs = (...patterns: (string | RegExp)[]): string | null => {
  for (const [name, text] of coreSources) {
    if (patterns.some((p) => (typeof p === 'string' ? text.includes(p) : p.test(text)))) return name
  }
  return null
}

if (!coreSources.has('Asar.cs'))
  fail(`缺 pad/Core/Asar.cs（手写 asar 读取器）；Core 里${whereIs('UInt32LE') ?? '没有任何文件'}含 UInt32LE`)
if (!has('Asar.cs', 'UInt32LE')) fail('Asar.cs 没按小端读头（找不到 UInt32LE）')
if (!has('Asar.cs', 'dataBase', /data(?:Base|Start|Offset)/i))
  fail('Asar.cs 没算出数据区起点（找不到 dataBase 或等价偏移常量）')

if (!coreSources.has('DshKernels.cs'))
  fail(`缺 pad/Core/DshKernels.cs（内核扫描）；Core 里${whereIs(/npx/i) ?? '没有任何文件'}含 npx`)
if (!has('DshKernels.cs', /npx/i)) fail('DshKernels.cs 没扫 npx 缓存里的内核')
if (!has('DshKernels.cs', /declared/i)) fail('DshKernels.cs 没处理启动脚本声明的内核（declared）')
if (!has('DshKernels.cs', /npm-cache/i, /_npx/)) fail('DshKernels.cs 没认 npm-cache / _npx 这个位置')

if (!coreSources.has('Discover.cs')) fail('缺 pad/Core/Discover.cs')
if (!has('Discover.cs', 'Uninstall')) fail('Discover.cs 没查卸载注册表（Uninstall）')
if (!has('Discover.cs', /Start Menu|WScript|\.lnk/i))
  fail('Discover.cs 没查开始菜单快捷方式（Start Menu / WScript / .lnk）')
if (!has('Discover.cs', /running process|GetProcesses/i))
  fail('Discover.cs 没查运行中的进程（running process / Process.GetProcesses）')
if (!has('Discover.cs', 'PAD_DISCOVER_EXTRA'))
  fail('Discover.cs 没吃 PAD_DISCOVER_EXTRA（fixture 根，测试用）')
if (!has('Discover.cs', /dsh-tui/i)) fail('Discover.cs 没扫 TUI profile 里的 dsh-tui 依赖')
if (!has(
  'Discover.cs',
  /StartsWith\(\s*["']\.["']/, // n.StartsWith(".")
  /TrimStart\(\s*["']\.["']/, // name.TrimStart('.')
  /\[\s*0\s*\]\s*(?:[!=]=?|is\s+(?:not\s+)?)\s*["']\.["']/, // name[0] == '.' / != '.'
  /["']\.["']\s*[!=]=?\s*[A-Za-z_][\w.]*\[\s*0\s*\]/, // '.' != name[0]
)) fail("Discover.cs 的 Profiles() 没过滤 '.' 开头的目录（node_modules / 隐藏目录）")

if (!has('Inventory.cs', 'declared:', 'Identity'))
  fail('Inventory.cs 找不到内核 Identity（也没有 declared: 约定）')

// 二期：进程现场（PEB）+ `pad cli procs`，在跑判定靠它们。
if (!coreSources.has('ProcTable.cs'))
  fail(`缺 pad/Core/ProcTable.cs（PEB 读进程环境/命令行）；Core 里${whereIs('ReadProcessMemory') ?? '没有任何文件'}读进程内存`)
if (!has('ProcTable.cs', 'NtQueryInformationProcess', 'ReadProcessMemory'))
  fail('ProcTable.cs 没读 PEB（找不到 NtQueryInformationProcess / ReadProcessMemory）')
if (!has('ProcTable.cs', 'DSH_HOME')) fail('ProcTable.cs 没认环境变量 DSH_HOME（进程归属最硬的证据）')
if (!has('Cli.cs', /case\s+"procs"/)) fail('Cli.cs 没有 `pad cli procs` 命令（进程现场没法自查）')

// ---- 实跑：造 fixture，让 exe 自己发现 ----

const exe = join(pad, 'bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
if (process.platform !== 'win32' || !existsSync(exe)) {
  console.log(`· 跳过 pad discover 实跑：${process.platform === 'win32' ? '没有构建产物 ' : '非 win32 平台 '}${exe}`)
  console.log('✓ pad-discover')
  process.exit(0)
}

type Row = Record<string, unknown>
const rows = (v: unknown): Row[] => (Array.isArray(v) ? (v as Row[]) : [])
const pick = (row: unknown, name: string): unknown => {
  if (row === null || typeof row !== 'object') return undefined
  const obj = row as Row
  return obj[name] ?? obj[name[0]!.toLowerCase() + name.slice(1)]
}
const str = (row: unknown, name: string): string => String(pick(row, name) ?? '')
/** Windows 路径比较：斜杠归一、忽略大小写和尾部反斜杠。 */
const norm = (p: unknown): string => String(p ?? '').trim().replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase()
const samePath = (a: unknown, b: unknown): boolean => norm(a) === norm(b)

const started = Date.now()
const tmp = packTmpDir('pad-discover')
const root = join(tmp, 'extra') // PAD_DISCOVER_EXTRA
const launchroot = join(tmp, 'launchroot') // PACK_LAUNCHER_ROOT，别污染真实 launcher
const out = join(tmp, 'discover.json')
mkdirSync(launchroot, { recursive: true })
/** 假的「正在跑的 TUI」子进程；finally / exit 钩子都会收尸。 */
let child: ReturnType<typeof spawn> | null = null

try {
  // 1) 假桌面壳：Deck.exe 隔壁 resources/app.asar 里捆着 DSH 内核 9.9.9-test
  const deckDir = join(root, 'apps', 'Deck Shell')
  mkdirSync(join(deckDir, 'resources'), { recursive: true })
  writeFileSync(join(deckDir, 'Deck.exe'), 'MZ fake electron shell\n')
  const asarEntries = {
    'node_modules/@deepseek-ai/dsh/package.json': JSON.stringify({
      name: '@deepseek-ai/dsh',
      version: '9.9.9-test',
    }),
    'node_modules/@deepseek-ai/dsh/lib/bin.js': '//x\n',
  }
  const asar = packAsar(asarEntries)
  checkAsar(asar, asarEntries)
  writeFileSync(join(deckDir, 'resources', 'app.asar'), asar)

  // 2) TUI home：profile 声明 dsh-tui 依赖，node_modules 里有实体包 1.2.3
  const tuiHome = join(root, 'homes', 'tui-home')
  const profile = join(tuiHome, 'profiles', 'dsh-tui')
  const tuiPkg = join(profile, 'node_modules', '@deepseek-harness-tui', 'dsh-tui')
  mkdirSync(tuiPkg, { recursive: true })
  writeFileSync(join(profile, 'package.json'), JSON.stringify({
    name: 'dsh-profile-dsh-tui',
    dependencies: { '@deepseek-harness-tui/dsh-tui': '^1.2.3' },
  }, null, 2))
  writeFileSync(join(tuiPkg, 'package.json'), JSON.stringify({
    name: '@deepseek-harness-tui/dsh-tui',
    version: '1.2.3',
  }, null, 2))

  // 3) 启动脚本：DSH_HOME 指到 tui-home，npx 声明内核 0.1.0-rc.9 + --profile dsh-tui
  const launcherDir = join(root, 'launchers', 'tui')
  mkdirSync(launcherDir, { recursive: true })
  writeFileSync(join(launcherDir, 'start.cmd'), [
    '@echo off',
    `set "DSH_HOME=${tuiHome}"`,
    'npx --yes @deepseek-ai/dsh@0.1.0-rc.9 --profile dsh-tui',
    '',
  ].join('\r\n'))

  // 4) appdata 形 home：%APPDATA%\dsh-desktop\harness 里只有 profiles/web
  const appdataHome = join(root, 'appdata', 'dsh-desktop', 'harness')
  mkdirSync(join(appdataHome, 'profiles', 'web'), { recursive: true })

  // 5) npx 形内核：<root>/node_modules/@deepseek-ai/dsh 1.0.0-fix
  const npxKernel = join(root, 'node_modules', '@deepseek-ai', 'dsh')
  mkdirSync(join(npxKernel, 'lib'), { recursive: true })
  writeFileSync(join(npxKernel, 'package.json'), JSON.stringify({
    name: '@deepseek-ai/dsh',
    version: '1.0.0-fix',
  }, null, 2))
  writeFileSync(join(npxKernel, 'lib', 'bin.js'), '//x\n')

  // 6) 真子进程当「正在跑的 TUI」：env DSH_HOME 指 fixture home，argv 带 --profile dsh-tui。
  //    Discover 判 Running/Pid 与 `cli procs` 的 DshHome/ProfileArg 都只能从进程现场（PEB）拿到。
  child = spawn(process.execPath, ['-e', 'setTimeout(()=>{}, 60000)', '--profile', 'dsh-tui'], {
    env: { ...process.env, DSH_HOME: tuiHome },
    stdio: 'ignore',
  })
  const tuiPid = child.pid
  if (!tuiPid) fail(`起不了假 TUI 子进程（process.execPath=${process.execPath}）`)
  // 断言失败会直接 process.exit，出口钩子负责收尸，别把 60s 的假 TUI 留在机器上。
  process.once('exit', () => { try { child?.kill() } catch { /* 已退出 */ } })
  await new Promise((resolve) => setTimeout(resolve, 400))
  if (child.exitCode !== null) fail(`假 TUI 子进程 ${tuiPid} 起后立刻退出（exit ${child.exitCode}）`)

  // ---- 跑真 exe：cli discover --json --out ----
  const run = spawnSync(exe, ['cli', 'discover', '--json', '--out', out], {
    env: { ...process.env, PAD_DISCOVER_EXTRA: root, PACK_LAUNCHER_ROOT: launchroot },
    encoding: 'utf8',
    timeout: 60_000,
  })
  const logFile = join(launchroot, 'pad-cli.log')
  const text = [run.stdout, run.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (run.status !== 0) fail(`pad cli discover 退出 ${run.status}（exe 还是旧版？）\n${text}`)
  if (!existsSync(out)) fail(`pad cli discover 没写 --out ${out}\n${text}`)

  const inventory = JSON.parse(readFileSync(out, 'utf8')) as Row
  const kernels = rows(pick(inventory, 'Kernels'))
  const shells = rows(pick(inventory, 'Shells'))
  const tuis = rows(pick(inventory, 'Tuis'))
  const homes = rows(pick(inventory, 'Homes'))

  // ---- 内核：asar 里 / npx 形 / 启动脚本声明 ----
  const asarKernel = kernels.find((k) => str(k, 'Version') === '9.9.9-test')
  if (!asarKernel) fail(`Kernels 里没有 asar 里捆的内核 9.9.9-test\n${pretty(kernels)}`)
  if (!str(asarKernel, 'Identity').includes('.asar!'))
    fail(`asar 内核 Identity 应带 ".asar!" 分段（asar 路径!内部目录）\n${pretty(asarKernel)}`)
  if (!kernels.some((k) => str(k, 'Version') === '1.0.0-fix'))
    fail(`Kernels 里没有 npx 形内核 1.0.0-fix\n${pretty(kernels)}`)
  const declared = kernels.some((k) => {
    const identity = str(k, 'Identity')
    return (identity.includes('declared:') && identity.includes('0.1.0-rc.9'))
      || str(k, 'Version') === '0.1.0-rc.9'
  })
  if (!declared) fail(`Kernels 里没有启动脚本声明的内核 0.1.0-rc.9\n${pretty(kernels)}`)

  // ---- 桌面壳：Deck.exe 且 KernelVersion 来自 asar ----
  const deck = shells.find((s) => str(s, 'Exe').toLowerCase().endsWith('deck.exe'))
  if (!deck) fail(`Shells 里没有 fixture 的 Deck.exe\n${pretty(shells)}`)
  if (str(deck, 'KernelVersion') !== '9.9.9-test')
    fail(`Deck.exe 的 KernelVersion 应是 asar 里的 9.9.9-test\n${pretty(deck)}`)

  // ---- TUI：Package 含 dsh-tui / 版本 1.2.3 / Home 对 / 启动脚本的 declared 内核合并上 ----
  const tui = tuis.find((t) => str(t, 'Package').toLowerCase().includes('dsh-tui')
    && str(t, 'Version') === '1.2.3')
  if (!tui) fail(`Tuis 里没有 fixture 的 dsh-tui 1.2.3（launcher 合并没做？）\n${pretty(tuis)}`)
  if (!samePath(pick(tui, 'Home'), tuiHome))
    fail(`TUI Home 应是 fixture 的 ${tuiHome}\n${pretty(tui)}`)
  if (!str(tui, 'DeclaredKernel').includes('0.1.0-rc.9'))
    fail(`TUI DeclaredKernel 应含启动脚本声明的 0.1.0-rc.9（launcher 合并没做？）\n${pretty(tui)}`)

  // ---- 在跑：真子进程的 env DSH_HOME=fixture home + argv --profile dsh-tui ----
  const fixtureTuis = tuis.filter((t) => str(t, 'Package').toLowerCase().includes('dsh-tui')
    && samePath(pick(t, 'Home'), tuiHome))
  if (fixtureTuis.length === 0)
    fail(`Tuis 里没有 fixture home 下的 dsh-tui 条目\n${pretty(tuis)}`)
  const runningTui = fixtureTuis.find((t) => pick(t, 'Running') === true)
  if (!runningTui)
    fail(`fixture TUI 应判成在跑（子进程 ${tuiPid}：env DSH_HOME=${tuiHome}、argv --profile dsh-tui）\n${pretty(tuis)}`)
  if (Number(pick(runningTui, 'Pid')) !== tuiPid)
    fail(`在跑 TUI 的 Pid 应是子进程 ${tuiPid}，实际 ${String(pick(runningTui, 'Pid'))}\n${pretty(runningTui)}`)

  // ---- Homes：fixture 的 tui-home 与 appdata 形 harness ----
  if (!homes.some((h) => samePath(pick(h, 'Path'), tuiHome)))
    fail(`Homes 里没有 fixture 的 tui-home\n${pretty(homes)}`)
  const rootKey = norm(root) + '\\'
  const appdataHomeSeen = homes.some((h) => {
    const path = norm(pick(h, 'Path'))
    return (path === norm(appdataHome) || (path.startsWith(rootKey) && path.endsWith('\\harness')))
      && path.includes('dsh-desktop')
  })
  if (!appdataHomeSeen)
    fail(`Homes 里没有 appdata 形 home（<fixture>/appdata/dsh-desktop/harness）\n${pretty(homes)}`)

  // ---- `pad cli procs --json`：进程现场（PEB）里能看到假 TUI 的 home 与 profile ----
  const out2 = join(tmp, 'procs.json')
  const run2 = spawnSync(exe, ['cli', 'procs', '--json', '--out', out2], {
    env: { ...process.env, PAD_DISCOVER_EXTRA: root, PACK_LAUNCHER_ROOT: launchroot },
    encoding: 'utf8',
    timeout: 60_000,
  })
  const text2 = [run2.stdout, run2.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (run2.status !== 0) fail(`pad cli procs 退出 ${run2.status}（exe 还是旧版？）\n${text2}`)
  if (!existsSync(out2)) fail(`pad cli procs 没写 --out ${out2}\n${text2}`)
  const procJson = JSON.parse(readFileSync(out2, 'utf8')) as unknown
  const procs = Array.isArray(procJson) ? rows(procJson) : rows(pick(procJson, 'Procs'))
  const procRow = procs.find((p) => Number(pick(p, 'Pid')) === tuiPid)
  if (!procRow) fail(`pad cli procs 里没有假 TUI 子进程 ${tuiPid}（ProcTable 没读到 / 被过滤掉了）\n${pretty(procs)}`)
  if (!samePath(pick(procRow, 'DshHome'), tuiHome))
    fail(`procs 里 ${tuiPid} 的 DshHome 应是 ${tuiHome}，实际 ${String(pick(procRow, 'DshHome'))}\n${pretty(procRow)}`)
  if (str(procRow, 'ProfileArg') !== 'dsh-tui')
    fail(`procs 里 ${tuiPid} 的 ProfileArg 应是 dsh-tui，实际 ${String(pick(procRow, 'ProfileArg'))}\n${pretty(procRow)}`)

  console.log(`  Kernels ${kernels.length} · Shells ${shells.length} · Tuis ${tuis.length} · Homes ${homes.length}` +
    ` · procs ${procs.length}（含假 TUI pid ${tuiPid}）`)
} finally {
  try { child?.kill() } catch { /* 已退出 */ }
  try { rmSync(tmp, { recursive: true, force: true }) } catch { /* keep tmp on lock */ }
}

const elapsed = Date.now() - started
if (elapsed >= 60_000) fail(`pad discover 全程 ${elapsed}ms ≥ 60s（性能回退？）`)
console.log(`✓ pad-discover（asar 内核 / npx 内核 / declared 内核 / 桌面壳 / TUI / homes 全命中；` +
  `在跑 TUI 的 Running+Pid 与 cli procs 的 DshHome+ProfileArg 都对上，${elapsed}ms）`)
