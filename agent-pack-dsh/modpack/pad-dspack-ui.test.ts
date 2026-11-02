#!/usr/bin/env bun
/**
 * 源码契约测试：PAD 窗口把 `.dspack` 拖入/选中后必须走 compat 引擎（PacksForge.ImportAuto），
 * 不能落到原生 .pack.zip 投影路径。UI 行为本身由 WPF 手动验证，这里锁接线不被改回去。
 * 运行：bun run agent-pack-dsh/modpack/pad-dspack-ui.test.ts
 */
import { readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'

const PAD = resolve(import.meta.dirname, '../pad')
const read = (rel: string) => readFileSync(join(PAD, rel), 'utf8')

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

function must(file: string, text: string, what: string): void {
  if (!text.includes(what)) fail(`${file} missing: ${what}`)
  console.log(`✔ ${file} ← ${what}`)
}

const launchPolicy = read('Core/LaunchPolicy.cs')
must('LaunchPolicy.cs', launchPolicy, 'IsDspackDrop')
must('LaunchPolicy.cs', launchPolicy, 'DspackPaths')
must('LaunchPolicy.cs', launchPolicy, 'EndsWith(".dspack")')

const main = read('MainWindow.xaml.cs')
must('MainWindow.xaml.cs', main, 'AllowDrop = true')
must('MainWindow.xaml.cs', main, 'LaunchPolicy.DspackPaths')
must('MainWindow.xaml.cs', main, 'PacksForge.ImportAuto')
must('MainWindow.xaml.cs', main, 'Window_Drop')

const versions = read('Views/VersionsView.xaml.cs')
must('VersionsView.xaml.cs', versions, '*.dspack')
must('VersionsView.xaml.cs', versions, 'LaunchPolicy.IsDspackDrop')
must('VersionsView.xaml.cs', versions, 'PacksForge.ImportAuto')

const forge = read('Core/PacksForge.cs')
must('PacksForge.cs', forge, 'ImportAuto')
must('PacksForge.cs', forge, 'PackforgeInstall')
must('PacksForge.cs', forge, 'CreateInstance')
must('PacksForge.cs', forge, 'PA151')

const cli = read('Core/Cli.cs')
must('Cli.cs', cli, '"import-auto"')
must('Cli.cs', cli, 'PacksForge.ImportAuto')
must('Cli.cs', cli, '"export-instance"')
must('Cli.cs', cli, 'PackforgeExportInstance')

const instanceView = read('Views/InstanceView.xaml')
must('InstanceView.xaml', instanceView, 'ExportForgeBtn')
must('InstanceView.xaml', instanceView, '导出 .dspack')

const instanceCs = read('Views/InstanceView.xaml.cs')
must('InstanceView.xaml.cs', instanceCs, 'ExportPackforge_Click')
must('InstanceView.xaml.cs', instanceCs, 'PackforgeExportInstance')

const packs = read('Core/Packs.cs')
must('Packs.cs', packs, 'PackforgeExportInstance')
must('Packs.cs', packs, 'export-instance')
must('Packs.cs', packs, 'PackforgeExportWorkspace')
must('Packs.cs', packs, 'export-workspace')

must('Cli.cs', cli, '"export-workspace"')
must('Cli.cs', cli, 'PackforgeExportWorkspace')

console.log('\nPAD DSPACK UI WIRING OK')
