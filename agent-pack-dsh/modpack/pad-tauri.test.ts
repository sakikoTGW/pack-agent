#!/usr/bin/env bun
/**
 * tauri 形态（D107）：PAD 的第 4 个 surface = dsh-tauri 桌面端。
 * 刻意不做单独的 Tauri UI —— 它只是 surface，走已有的 surfaces / node open / 账本 / stop 链。
 *
 * 1) 源码契约：Surface.Tauri、Nodes.OpenTauri、CLI case、PA 码、jump 描述符、文档。
 * 2) 真跑 CLI：surfaces 里出现 tauri（独占、无端点）；缺 --exe / exe 不存在 / exe 秒退 → PA146/147/149。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTmpDir } from '../../src/tmp-root.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const pad = join(import.meta.dirname, '../pad')
const surfaceCs = readFileSync(join(pad, 'Core/Surface.cs'), 'utf8')
const nodesCs = readFileSync(join(pad, 'Core/Nodes.cs'), 'utf8')
const cliCs = readFileSync(join(pad, 'Core/Cli.cs'), 'utf8')
const jumpCs = readFileSync(join(pad, 'Core/Jump/JumpModels.cs'), 'utf8')
const paCodes = JSON.parse(readFileSync(join(import.meta.dirname, 'registries/pa-codes.json'), 'utf8')) as {
  entries: { id: string }[]
}
const surfaceDoc = readFileSync(join(pad, '../docs/SURFACE.md'), 'utf8')

// ---- 1) 源码契约 --------------------------------------------------------
for (const need of ['public static readonly Surface Tauri', '"tauri"', '[Tui, Web, App, Tauri]'])
  if (!surfaceCs.includes(need)) fail(`Surface.cs 缺 ${need}`)

for (const need of ['OpenTauri', 'DSH_HOME', 'PA147', 'PA148', 'PA149'])
  if (!nodesCs.includes(need)) fail(`Nodes.cs 缺 ${need}`)

for (const need of ['case "tauri"', 'PA146', 'Nodes.OpenTauri', 'tauri desktop window'])
  if (!cliCs.includes(need)) fail(`Cli.cs 缺 ${need}`)

for (const need of ['pad-tauri', '"tauri"'])
  if (!jumpCs.includes(need)) fail(`JumpModels.cs 缺 ${need}`)

for (const code of ['PA146', 'PA147', 'PA148', 'PA149'])
  if (!paCodes.entries.some((e) => e.id === code)) fail(`pa-codes.json 缺 ${code}`)

if (!surfaceDoc.includes('`tauri`')) fail('docs/SURFACE.md 没写 tauri 形态')
if (!surfaceDoc.includes('DSH_HOME')) fail('docs/SURFACE.md 没写 tauri 的 home 隔离条件')

console.log('✓ tauri 形态源码契约（Surface / Nodes / CLI / jump / PA 码 / 文档）')

// ---- 2) 真跑 CLI --------------------------------------------------------
const exe = process.env.PAD_EXE ?? join(pad, 'bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
if (process.platform !== 'win32' || !existsSync(exe)) {
  console.log('· 无 Windows 编译产物，跳过实跑')
  process.exit(0)
}

const root = packTmpDir('pad-tauri-surface')
mkdirSync(join(root, '.test-out'), { recursive: true })
const env = { ...process.env, PACK_LAUNCHER_ROOT: root }

function runCli(args: string[]): { status: number; text: string } {
  const r = spawnSync(exe, args, { env, encoding: 'utf8', timeout: 60000 })
  return { status: r.status ?? -1, text: `${r.stdout ?? ''}${r.stderr ?? ''}` }
}

// 2.1 surfaces 里出现 tauri：独占、不 announce 端点
const rs = runCli(['cli', 'surfaces'])
if (rs.status !== 0) fail(`pad cli surfaces 失败：${rs.text}`)
const cliLog = readFileSync(join(root, 'pad-cli.log'), 'utf8')
const tauriEntry = new RegExp(
  '"name": "tauri",[\\s\\S]{0,240}?"exclusive": true,[\\s\\S]{0,240}?"needsEndpoint": false',
).test(cliLog)
if (!tauriEntry) fail('surfaces 输出里 tauri 应是 exclusive=true / needsEndpoint=false')

// 2.2 缺 --exe → PA146；exe 不存在 → PA147；exe 秒退 → PA149
const home = join(root, 'instances', 't1', 'dsh-home')
mkdirSync(home, { recursive: true })

const noExe = runCli(['cli', 'node', 'open', home, '--surface', 'tauri'])
if (noExe.status === 0 || !noExe.text.includes('PA146')) fail(`缺 --exe 应以 PA146 拒绝，实际：${noExe.text}`)

const missing = runCli(['cli', 'node', 'open', home, '--surface', 'tauri', '--exe', 'C:\\no-such-tauri\\x.exe'])
if (missing.status === 0 || !missing.text.includes('PA147')) fail(`不存在的 exe 应以 PA147 拒绝，实际：${missing.text}`)

const fastExit = 'C:\\Windows\\System32\\where.exe'
if (existsSync(fastExit)) {
  const r = runCli(['cli', 'node', 'open', home, '--surface', 'tauri', '--exe', fastExit])
  if (r.status === 0) fail('秒退的 exe 竟被记成打开了窗口')
  if (!r.text.includes('PA149')) fail(`秒退应以 PA149 报「没窗口」，实际：${r.text}`)
}

console.log('✓ tauri 形态实跑：surfaces 可见、PA146/PA147/PA149 行为正确')
