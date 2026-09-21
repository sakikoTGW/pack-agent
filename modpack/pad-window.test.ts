#!/usr/bin/env bun
/**
 * PAD 窗口这一层：空状态、拖 zip、web 不卡死、收编/崩溃/升级分开。
 * 对照源码，不启 WPF。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTmpDir } from '../../src/tmp-root.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const pad = join(import.meta.dirname, '../pad')
const read = (rel: string) => {
  const p = join(pad, rel)
  if (!existsSync(p)) fail(`缺 ${rel}`)
  return readFileSync(p, 'utf8')
}

// ---- 纯函数契约（与 LaunchPolicy.cs 同口径）----

function parseWebUrl(text: string): string | null {
  const m = text.match(/dsh web: (https?:\/\/\S+)/i)
  return m ? m[1].replace(/[.,;]+$/, '') : null
}

function isPackDrop(name: string): boolean {
  const n = name.toLowerCase()
  return n.endsWith('.pack.zip') || n.endsWith('.pinst.zip') || n.endsWith('.pack.json')
}

function isImageDrop(name: string): boolean {
  const n = name.toLowerCase()
  return ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp'].some((ext) => n.endsWith(ext))
}

function dropUsesImport(hasSelectedInstance: boolean): boolean {
  return !hasSelectedInstance
}

function offerAdopt(homeExists: boolean, already: boolean, dismissed: boolean): boolean {
  return homeExists && !already && !dismissed
}

function waitForWebReady(isWeb: boolean): boolean {
  return isWeb
}

if (parseWebUrl('boot\ndsh web: http://127.0.0.1:3080\n') !== 'http://127.0.0.1:3080')
  fail('parseWebUrl 没吃到 dsh web 行')
if (parseWebUrl('dsh-tui started') !== null) fail('TUI 输出不该被当成网址')
if (!isPackDrop('foo.pack.zip') || !isPackDrop('a.pinst.zip') || !isPackDrop('x.pack.json'))
  fail('isPackDrop 漏了合法包名')
if (isPackDrop('notes.zip')) fail('普通 zip 不该当包')
if (!isImageDrop('logo.png') || !isImageDrop('a.JPEG') || !isImageDrop('x.webp'))
  fail('isImageDrop 漏了图片')
if (isImageDrop('mod.jar') || isImageDrop('foo.pack.zip')) fail('非图片不该当图标拖入')

function mdHeading(line: string): string | null {
  const t = line.trim()
  let n = 0
  while (n < t.length && t[n] === '#' && n < 6) n++
  if (n === 0 || n === t.length) return null
  const raw = t.slice(n)
  const hadSpace = raw.startsWith(' ') || raw.startsWith('\t')
  const body = raw.trim()
  const stripped = body.replace(/#+$/, '')
  const hadTrail = stripped.length < body.length
  if (!hadSpace && !hadTrail) return null
  const text = stripped.trim()
  return text.length ? text : null
}
if (mdHeading('##2222##') !== '2222') fail('##2222## 应渲成标题 2222，不能当原文')
if (mdHeading('## 你好') !== '你好') fail('## 空格标题没吃到')
if (mdHeading('#not') !== null) fail('#not 不该当标题')
if (!dropUsesImport(false) || dropUsesImport(true)) fail('空机 Import，有选中实例 Project')
if (!offerAdopt(true, false, false)) fail('该问收编')
if (offerAdopt(true, true, false) || offerAdopt(true, false, true) || offerAdopt(false, false, false))
  fail('已收编 / 已拒绝 / 没有 ~/.dsh 不该再问')
if (waitForWebReady(true) !== true || waitForWebReady(false) !== false)
  fail('只有 web 才等网址，TUI 不等')

function looksLikeLauncherRoot(instanceRows: number, versionRows: number, hasPadJson: boolean): boolean {
  return instanceRows > 0 || versionRows > 0 || hasPadJson
}

function sameRoot(a: string, b: string): boolean {
  return a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase()
}

/** Empty exe-side root yields to last / richest sniffed. Env or a portable kit with instances stays. */
function pickBootRoot(
  envPinned: boolean,
  localRoot: string,
  localInstances: number,
  last: string | null,
  lastInstances: number,
  sniffed: { path: string; instances: number }[],
): string | null {
  if (envPinned) return null
  if (localInstances > 0) return null
  if (last && lastInstances > 0 && !sameRoot(last, localRoot)) return last
  let best: string | null = null
  let n = 0
  for (const s of sniffed) {
    if (s.instances <= 0) continue
    if (sameRoot(s.path, localRoot)) continue
    if (s.instances > n) {
      n = s.instances
      best = s.path
    }
  }
  return best
}

if (!looksLikeLauncherRoot(1, 0, false) || !looksLikeLauncherRoot(0, 1, false) || !looksLikeLauncherRoot(0, 0, true))
  fail('有 instance.json / version.json / pad.json 就算启动器根')
if (looksLikeLauncherRoot(0, 0, false)) fail('空目录不算启动器根')
if (pickBootRoot(true, 'E:\\tmp\\empty\\.pack-launcher', 0, 'E:\\pad\\.pack-launcher', 3, [
  { path: 'E:\\pad\\.pack-launcher', instances: 3 },
]) !== null)
  fail('PACK_LAUNCHER_ROOT 钉死时不抢根')
if (pickBootRoot(false, 'E:\\kit\\.pack-launcher', 2, 'E:\\pad\\.pack-launcher', 5, [
  { path: 'E:\\pad\\.pack-launcher', instances: 5 },
]) !== null)
  fail('便携包自己有实例时不切走')
if (pickBootRoot(false, 'E:\\tmp\\empty\\.pack-launcher', 0, 'E:\\pad\\.pack-launcher', 3, [
  { path: 'E:\\other\\.pack-launcher', instances: 9 },
  { path: 'E:\\pad\\.pack-launcher', instances: 3 },
]) !== 'E:\\pad\\.pack-launcher')
  fail('空根优先切到上次用过的那份')
if (pickBootRoot(false, 'E:\\tmp\\empty\\.pack-launcher', 0, null, 0, [
  { path: 'E:\\tmp\\empty\\.pack-launcher', instances: 0 },
  { path: 'E:\\a\\.pack-launcher', instances: 1 },
  { path: 'E:\\b\\.pack-launcher', instances: 4 },
]) !== 'E:\\b\\.pack-launcher')
  fail('没有上次记录时切到实例最多的那份')
if (pickBootRoot(false, 'E:\\tmp\\empty\\.pack-launcher', 0, 'E:\\gone\\.pack-launcher', 0, []) !== null)
  fail('嗅探全空就留在本地空根')

function commandLineIsDsh(cl, profile, bin) {
  if (!cl || !profile) return false
  const mark = `--profile ${profile}`
  const i = cl.toLowerCase().indexOf(mark.toLowerCase())
  const eq = cl.toLowerCase().includes(`--profile=${profile.toLowerCase()}`)
  if (i < 0 && !eq) return false
  if (i >= 0) {
    const after = i + mark.length
    if (after < cl.length && !' "\t'.includes(cl[after])) return false
  }
  if (bin && cl.toLowerCase().includes(bin.toLowerCase())) return true
  return /bin\.js/i.test(cl) || /@deepseek-ai[/\\]dsh/i.test(cl)
}
function keepTrackedRun(launcherAlive, harnessAlive, startedMs, nowMs) {
  if (launcherAlive || harnessAlive) return true
  return nowMs - startedMs < 45_000
}
function pickJobId(jobs) {
  for (let i = jobs.length - 1; i >= 0; i--) if (jobs[i].status === 'running') return jobs[i].id
  return [...jobs].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))[0]?.id
}
const bin = String.raw`E:\pad\versions\0.1.0-rc.7\node_modules\@deepseek-ai\dsh\lib\bin.js`
if (!commandLineIsDsh(`node.exe "${bin}" --profile dsh-tui`, 'dsh-tui', bin))
  fail('启动命令行认不出 dsh-tui')
if (commandLineIsDsh(`node.exe "${bin}" --profile dsh-tui-extra`, 'dsh-tui', bin))
  fail('dsh-tui 不该配到 dsh-tui-extra')
if (!keepTrackedRun(false, false, 0, 10_000)) fail('启动后 45 秒内 wt 退出仍该算在跑')
if (keepTrackedRun(false, false, 0, 60_000)) fail('超过宽限且没有 Harness 就该从在跑列表拿掉')
if (!keepTrackedRun(false, true, 0, 60_000)) fail('认到 node 后即使 wt 已退出也算在跑')
if (pickJobId([
  { id: 'old-fail', status: 'failed', updatedAt: '2026-01-01' },
  { id: 'install', status: 'running', updatedAt: '2026-01-02' },
]) !== 'install') fail('任务页该盯着正在进行的，不是列表第一条失败')

const policyCs = read('Core/LaunchPolicy.cs')
if (!policyCs.includes('KeepTrackedRun') || !policyCs.includes('CommandLineIsDsh') || !policyCs.includes('PickJob'))
  fail('启动在跑 / 任务自选没有写进 LaunchPolicy')
if (!read('Core/Proc.cs').includes('FindDshPid')) fail('找不到 Harness 的 node pid')
if (!read('Core/Launcher.cs').includes('LiveHarnessPid')) fail('runtime 扫进程不认 Harness pid')
if (!read('Views/TaskView.xaml.cs').includes('PickJob')) fail('任务页还在 SelectedItem = _jobs[0]')
function looksLikeHome(hasProfiles: boolean, hasSessions: boolean, hasCred: boolean): boolean {
  return hasProfiles || hasSessions || hasCred
}

function pickHome(configured: string, env: string, fallback: string): string {
  if (configured.trim()) return configured.trim()
  if (env.trim()) return env.trim()
  return fallback
}

if (!looksLikeHome(true, false, false) || !looksLikeHome(false, true, false) || !looksLikeHome(false, false, true))
  fail('有 profiles / sessions / 凭据就该认成 DSH_HOME')
if (looksLikeHome(false, false, false)) fail('空目录不该认成 DSH_HOME')
if (pickHome('D:\\dsh', 'C:\\env', 'C:\\Users\\a\\.dsh') !== 'D:\\dsh') fail('设置里的 DSH_HOME 优先')
if (pickHome('', 'C:\\env', 'C:\\Users\\a\\.dsh') !== 'C:\\env') fail('DSH_HOME 环境变量次之')
if (pickHome('', '', 'C:\\Users\\a\\.dsh') !== 'C:\\Users\\a\\.dsh') fail('最后才用 ~/.dsh')

function relocateOwned(adopted, home, kind, ws, instanceDir, homeExists, wsExists) {
  if (adopted || !homeExists) return null
  const wantHome = instanceDir + '\\home'
  const wantWs = instanceDir + '\\workspace'
  const same = (a, b) => String(a).replace(/[\\/]+$/, '').toLowerCase() === String(b).replace(/[\\/]+$/, '').toLowerCase()
  const homeMoved = !home || !same(home, wantHome)
  const wsMoved = kind === 'owned' && wsExists && (!ws || !same(ws, wantWs))
  if (!homeMoved && !wsMoved) return null
  return { home: wantHome, workspace: kind === 'owned' && wsExists ? wantWs : ws }
}
if (relocateOwned(true, 'E:\\old\\home', 'owned', 'E:\\old\\ws', 'E:\\kit\\instances\\tui', true, true))
  fail('收编的 home 不解压改写')
if (relocateOwned(false, 'E:\\kit\\instances\\tui\\home', 'owned', 'E:\\kit\\instances\\tui\\workspace', 'E:\\kit\\instances\\tui', true, true))
  fail('已经在这份实例目录里还改写')
const moved = relocateOwned(false, 'E:\\build\\instances\\tui\\home', 'owned', 'E:\\build\\instances\\tui\\workspace', 'E:\\kit\\instances\\tui', true, true)
if (!moved || moved.home !== 'E:\\kit\\instances\\tui\\home' || moved.workspace !== 'E:\\kit\\instances\\tui\\workspace')
  fail('解压后的实例还钉着打包机路径')
if (relocateOwned(false, 'E:\\old\\home', 'owned', 'E:\\old\\ws', 'E:\\kit\\instances\\tui', false, true))
  fail('这份拷贝里没有 home 不该瞎改')
if (!policyCs.includes('RelocateOwned')) fail('解压路径改写没写进 LaunchPolicy')
const exportFn = read('Core/Launcher.cs').split('ExportPortable')[1]?.split('\n    public ')[0] ?? ''
if (!exportFn.includes('VersionDir') && !exportFn.includes('CopyTree(VersionDir'))
  fail('portable-export 带实例时不带版本库，解压后还要再下 DSH')
if (!exportFn.includes('.credentials.yaml')) fail('portable-export 不剥钥匙')
if (!exportFn.includes('RelocateOwned') && !exportFn.includes('RelocateIfNeeded') && !exportFn.includes('destRoot.Instance'))
  fail('portable-export 不改写打包机路径')
if (!exportFn.includes('.pack-agent-repo'))
  fail('portable-export 要把打包机仓库路径挡在目录外')
if (exportFn.includes('EnumerateFiles(src)'))
  fail('portable-export 把运行时 dll 跟 exe 混在一起')
if (!exportFn.includes('CopyHostExe'))
  fail('portable-export 不单独拷 PAD exe，根目录又会一堆文件')
if (!exportFn.includes('RewriteLaunchScripts'))
  fail('portable-export 拷完不重生相对路径脚本')
if (!exportFn.includes('library') || !exportFn.includes('credentials.yaml'))
  fail('portable-export 不剥 library 里的 API Key')
const portableTs = readFileSync(join(import.meta.dirname, 'portable.ts'), 'utf8')
if (portableTs.includes('copyTree(CACHE_DIR, kit)'))
  fail('portable-init 把 publish 目录整份拷进包根，dll 又跟 exe 混在一起')
if (!portableTs.includes('PublishSingleFile'))
  fail('portable-init 不打单文件 exe，对方解压仍是一堆 dll')
if (!portableTs.includes('packPlayZip') || !portableTs.includes('7-Zip'))
  fail('解压包还在用 Windows tar 单线程 zip，pnpm 小文件会打一小时')
if (!read('Core/PadRegistry.cs').includes('ReadEmbedded'))
  fail('单文件 exe 没有内嵌 pad-pages，解压包不能没有旁边的 registries 文件夹')
console.log('✓ 窗口策略：网址 / 拖包 / 收编 / 不卡 TUI / 嗅探 home')

const policy = read('Core/LaunchPolicy.cs')
if (!policy.includes('dsh web: ')) fail('LaunchPolicy 不解析 dsh web 行')
if (!policy.includes('.pack.zip')) fail('LaunchPolicy 不认 pack.zip')
if (!policy.includes('IsSidecarZip')) fail('LaunchPolicy 不认 exe 旁 zip')
if (!policy.includes('SidecarZips')) fail('LaunchPolicy 不列出 exe 旁 zip')
if (!policy.includes('OfferAdopt')) fail('LaunchPolicy 缺 OfferAdopt')
if (!policy.includes('LooksLikeLauncherRoot')) fail('LaunchPolicy 缺 LooksLikeLauncherRoot')
if (!policy.includes('PickBootRoot')) fail('LaunchPolicy 缺 PickBootRoot')
if (!policy.includes('DropUsesImport')) fail('LaunchPolicy 缺 DropUsesImport')
if (!policy.includes('WaitForWebReady')) fail('LaunchPolicy 缺 WaitForWebReady')
if (!policy.includes('configured')) fail('LaunchPolicy.DefaultDshHome 不吃设置里的路径')

const runner = read('Core/Runner.cs')
if (!runner.includes('LaunchWeb')) fail('Runner 没有网页启动')
if (!runner.includes('ParseWebUrl')) fail('网页启动不读网址')
if (runner.includes('ready timeout')) fail('PAD 不许抄 TS 那套等网址超时就杀进程')
if (!read('Core/Reveal.cs').includes('UseShellExecute')) fail('打开网址不用 shell')
if (!runner.includes('Reveal.Url')) fail('网页启动不打开浏览器')
const writeCmd = runner.split('File.WriteAllText(path, sb.ToString()')[1]?.slice(0, 180) ?? ''
if (!writeCmd.includes('UTF8Encoding(true)') && !writeCmd.includes('encoderShouldEmitUTF8Identifier: true'))
  fail('launch-*.cmd 无 UTF-8 BOM，cmd 把中文 DSH_HOME 读裂，TUI 起不来')
if (!runner.includes('cmd /d /q /k'))
  fail('启动 cmd 不带 /q：UTF-8 BOM 让 @echo off 失效，终端会回显每一行，包括从 yaml 读出的钥匙')

const launchXaml = read('Views/LaunchView.xaml')
if (launchXaml.includes('也可以把')) fail('空状态还在写拖包提醒')
if (launchXaml.includes('把 pack.zip 拖进来')) fail('空状态把拖进来写成了按钮')
if (launchXaml.includes('点左边启动')) fail('空跑态还在教人点启动')
if (launchXaml.includes('还没东西可启动')) fail('右栏还在演空态卡片')
if (launchXaml.includes('还没选好要跑的')) fail('左栏还在写没选好')
if (!launchXaml.includes('添加已有文件夹')) fail('空状态没添加已有文件夹')
if (!launchXaml.includes('新建')) fail('空状态缺新建')
if (launchXaml.includes('runtime.json')) fail('启动页还在提 runtime.json')
if (launchXaml.includes('还没有可启动的 profile')) fail('空态还在讲 profile')

const launchCs = read('Views/LaunchView.xaml.cs')
if (launchCs.includes('pid {result.Pid}')) fail('toast 还在报 pid')
if (launchCs.includes('Zip_Click')) fail('启动页还留着假拖入按钮')
if (!launchCs.includes('AllowDrop') && !read('MainWindow.xaml').includes('AllowDrop'))
  fail('窗口不能拖 zip')

const mainXaml = read('MainWindow.xaml')
if (!mainXaml.includes('AllowDrop')) fail('主窗口没开拖放')
if (!read('MainWindow.xaml.cs').includes('PickupSidecarPacks')) fail('启动不扫 exe 旁整合包')
if (!read('Core/Packs.cs').includes('CollectSidecar')) fail('PAD 不把 exe 旁 zip 收进启动器根')

const manageXaml = read('Views/ManageView.xaml')
if (manageXaml.includes('workspace / session / agent')) fail('管理页标题还是内部词')

function bundleKind(spec: string): string {
  const s = spec.toLowerCase()
  if (s.includes('dsh-tui')) return 'TUI'
  if (s.includes('pad-gateway') || s.includes('host-apiproxy')) return '管理口'
  return '组合包'
}
function bundlePackageDir(profileDir: string, spec: string): string {
  return profileDir + '\\node_modules\\' + spec.replaceAll('/', '\\')
}
if (bundleKind('@deepseek-harness-tui/dsh-tui') !== 'TUI') fail('TUI 组合包要标成 TUI，才能在管理页更新')
if (bundleKind('@sakikotgw/pad-gateway') !== '管理口') fail('pad-gateway 不是普通组合包')
if (bundleKind('@liustack/modlens') !== '组合包') fail('其余有 dsh.bundle 的是组合包')
if (bundlePackageDir('E:\\i\\home\\profiles\\dsh-tui', '@deepseek-harness-tui/dsh-tui') !==
    String.raw`E:\i\home\profiles\dsh-tui\node_modules\@deepseek-harness-tui\dsh-tui`)
  fail('组合包装在该 profile 的 node_modules 里')
if (!policyCs.includes('BundleKind') || !policyCs.includes('BundlePackageDir'))
  fail('组合包种类和磁盘路径没写进 LaunchPolicy')
if (!policyCs.includes('BundleIconFiles') || !policyCs.includes('GithubRepo'))
  fail('组合包图标路径和 GitHub 仓库没写进 LaunchPolicy')
function githubRepo(url: string): string | null {
  let s = url.trim()
  if (s.toLowerCase().startsWith('git+')) s = s.slice(4)
  const i = s.toLowerCase().indexOf('github.com')
  if (i < 0) return null
  s = s.slice(i + 'github.com'.length).replace(/^[/:]+/, '')
  const parts = s.split('/')
  if (parts.length < 2) return null
  return parts[0] + '/' + parts[1].replace(/\.git$/i, '').split(/[?#]/)[0]
}
if (githubRepo('git+https://github.com/ccch1mneyyy/dsh-TUI.git') !== 'ccch1mneyyy/dsh-TUI')
  fail('TUI 仓库 URL 解析不出 ccch1mneyyy/dsh-TUI')
if (!policyCs.includes('docs/assets/logo.svg'))
  fail('TUI 自己的 logo 在 docs/assets/logo.svg，管理页没去读')
if (!existsSync(join(pad, 'Core/BundleIcon.cs')))
  fail('组合包图标没写成 BundleIcon')
if (!read('Core/BundleIcon.cs').includes('docs/assets/logo.svg'))
  fail('BundleIcon 不读 TUI 仓库那张 logo.svg')
if (!read('Views/ManageView.xaml').includes('IconPath'))
  fail('管理页组合包行不绑图标')
if (!read('Views/InstMark.xaml.cs').includes('BundleIcon'))
  fail('TUI 实例标还在用通用终端几何，不读组合包自己的 logo')
if (!read('Views/ManageView.xaml.cs').includes('EnsureBundleIcons'))
  fail('管理页不把 GitHub 上的 TUI logo 拉下来')
if (!read('Core/Launcher.cs').includes('ListBundles'))
  fail('管理页读不到 profile 的 dsh.profile.bundles 和已装版本')
if (!manageXaml.includes('x:Name="BundleList"')) fail('管理页没有组合包名单')
if (!manageXaml.includes('全部更新') && !manageXaml.includes('更新全部'))
  fail('管理页不能一次更新 TUI 和组合包')
if (!read('Views/ManageView.xaml.cs').includes('PluginOp'))
  fail('管理页更新不走 dsh plugin --profile update')
if (!read('Views/ManageView.xaml.cs').includes('LoadBundles'))
  fail('没启动时管理页也不列出 TUI / 组合包')
if (!read('Views/ManageView.xaml.cs').includes('组合包更新不需要管理口'))
  fail('没有管理口时人不知道还能更新 TUI')
if (!read('Core/Cli.cs').includes('case "plugin"'))
  fail('pad cli 不能 plugin list/update，管理页和脚本会对不上')

const settingsXaml = read('Views/SettingsView.xaml')
if (settingsXaml.includes('内容底洗') || settingsXaml.includes('顶栏底洗'))
  fail('设置还在用底洗')
if (settingsXaml.includes('像 Steam 那样换图')) fail('设置还在拿 Steam 当开场')
if (settingsXaml.includes('用哪个终端跑 dsh-tui')) fail('终端设置还在用讲座标题')
if (settingsXaml.includes('Windows 自带。')) fail('conhost 还在写 Windows 自带')
if (settingsXaml.includes('启动会打开你选的终端')) fail('终端设置还在写启动会打开')
if (!settingsXaml.includes('$DSH_HOME')) fail('设置里没有 DSH_HOME')
if (!settingsXaml.includes('使用本机 DSH')) fail('设置里不能把自动搜索到的 dsh 钉进版本库')
if (!settingsXaml.includes('选择 DSH')) fail('设置里不能手选 bin.js')
if (!settingsXaml.includes('选择 dsh-tui')) fail('设置里不能手选 dsh-tui')
if (!settingsXaml.includes('启动器') || !settingsXaml.includes('DSH 发行号'))
  fail('设置没有把启动器和 DSH 发行号分开升')

const settingsCs = read('Views/SettingsView.xaml.cs')
if (settingsCs.includes('PinBinBtn.IsEnabled')) fail('没嗅探到就把用本机命令关掉了')
if (!settingsCs.includes('PickBin_Click')) fail('设置不能手选命令')
if (!settingsCs.includes('PickTui_Click')) fail('设置不能手选组合包')
if (!settingsCs.includes('OpenFileDialog')) fail('选命令没有文件对话框')

const models = read('Core/Models.cs')
if (!models.includes('Adopted')) fail('实例记录没收编标记')
if (!models.includes('AdoptDismissed')) fail('pad.json 不能记住「不问收编」')
if (!models.includes('DshHome')) fail('pad.json 不能记下 DSH_HOME')
if (!models.includes('tuiPackage') && !models.includes('TuiPackage')) fail('pad.json 不能记下 dsh-tui 组合包路径')
if (!models.includes('[JsonPropertyName("bin")]')) fail('发行号记录不能指向本机已有的 bin.js')

const launcher = read('Core/Launcher.cs')
if (!launcher.includes('AdoptHome')) fail('Launcher 没收编')
if (!launcher.includes('LogsDir')) fail('Launcher 没日志目录')
if (!launcher.includes('WriteShortcut')) fail('Launcher 没快捷方式')
if (!launcher.includes('Adopted = true')) fail('收编不打 adopted 标记')
if (!launcher.includes('PinExisting')) fail('Launcher 不能钉本机已有的 dsh')
if (!launcher.includes('CopyTree')) fail('钉本机命令不拷进版本库')
const copyTreeFn = launcher.split('static void CopyTree')[1]?.split('\n    static ')[0] ?? ''
if (!copyTreeFn.includes('ReparsePoint'))
  fail('CopyTree 把 pnpm junction 拓成真目录，解压包 bin.js 找不到 dsh-app-boot')
if (!existsSync(join(pad, 'Core/PnpmHeal.cs')))
  fail('缺 PnpmHeal：解压后要重接 .pnpm，对方机器没有 pnpm 也能起')
const heal = read('Core/PnpmHeal.cs')
if (!heal.includes('.pnpm')) fail('PnpmHeal 不认 .pnpm')
if (!heal.includes('mklink') && !heal.includes('CreateSymbolicLink'))
  fail('PnpmHeal 不建 junction')
if (!runner.includes('PnpmHeal.Relink'))
  fail('启动不重接 pnpm，解压包 TUI 仍缺 emoji-regex')
function patchVirtualStoreDir(yaml, dir) {
  const line = 'virtualStoreDir: ' + dir
  const stripped = yaml.replace(/^virtualStoreDir: .*$(\r?\n)?/gm, '')
  if (/^virtualStoreDirMaxLength:/m.test(stripped))
    return stripped.replace(/^virtualStoreDirMaxLength:/m, line + '\nvirtualStoreDirMaxLength:')
  return stripped.replace(/\s*$/, '\n') + line + '\n'
}
{
  const y = 'storeDir: X\nvirtualStoreDir: E:\\old\\node_modules\\.pnpm\nlayoutVersion: 5\n'
  const p = patchVirtualStoreDir(y, 'E:\\tmp\\kit\\node_modules\\.pnpm')
  if (p.includes('E:\\old\\')) fail('旧 virtualStoreDir 还在')
  if (!p.includes('E:\\tmp\\kit\\node_modules\\.pnpm')) fail('没写成这份 .pnpm')
  const dup = 'storeDir: X\nvirtualStoreDir: old\nvirtualStoreDirMaxLength: 120\nvirtualStoreDir: old2\n'
  const d = patchVirtualStoreDir(dup, 'E:\\tmp\\kit\\node_modules\\.pnpm')
  if ((d.match(/^virtualStoreDir: /gm) || []).length !== 1) fail('virtualStoreDir 重复键 pnpm 会挂')
}
if (!read('Core/LaunchPolicy.cs').includes('PatchVirtualStoreDir'))
  fail('virtualStoreDir 改写没写成策略，解压后 pnpm add 报 UNEXPECTED_VIRTUAL_STORE')
if (!heal.includes('PatchVirtualStoreDir') && !heal.includes('.modules.yaml'))
  fail('Relink 不改 .modules.yaml，组合包装不上')
{
  const addFn = launcher.split('public async Task AddBundles')[1]?.split('\n    public ')[0] ?? ''
  if (!addFn.includes('PnpmHeal.Relink'))
    fail('装组合包前不 Relink，解压包 pnpm add 报 UNEXPECTED_VIRTUAL_STORE')
  if (!addFn.includes('Task.Run'))
    fail('装组合包 Relink 在界面线程会 PA040')
}
if (read('Core/AppState.cs').includes('先装一份 DSH')) fail('空态讲座文案又回来了')
if (read('Core/AppState.cs').includes('StepWhy')) fail('死了的空态标注又回来了')
if (!read('Core/DshSniff.cs').includes('LooksLikeHome')) fail('缺 DshSniff.LooksLikeHome')
if (!read('Core/DshSniff.cs').includes('@deepseek-ai')) fail('嗅探不找 @deepseek-ai/dsh')
if (!read('Core/DshSniff.cs').includes('_npx')) fail('嗅探不找 npm-cache _npx')
if (!read('Core/DshSniff.cs').includes('ResolveBin')) fail('选的文件不能解析成 bin.js')
if (!read('Core/DshSniff.cs').includes('apps') || !read('Core/DshSniff.cs').includes('cli'))
  fail('嗅探不看源码树 apps/cli')
if (!read('Core/DshSniff.cs').includes('LauncherRootCandidates')) fail('嗅探不找本机其它启动器根')
if (!read('Core/DshSniff.cs').includes('CountInstances')) fail('嗅探不数实例')
if (!read('Core/DshSniff.cs').includes('AGENT_PACK_TMP')) fail('嗅探不看 AGENT_PACK_TMP 自己的 .pack-launcher')
if (!read('Core/DshSniff.cs').includes('PACK_AGENT_REPO')) fail('嗅探不看 PACK_AGENT_REPO 里的 Debug 根')
{
  const sniff = read('Core/DshSniff.cs')
  const rawStart = sniff.indexOf('static IEnumerable<string> LauncherRootCandidateRaw')
  const rawEnd = sniff.indexOf('static IEnumerable<string> RepoHints')
  const raw = rawStart >= 0 && rawEnd > rawStart ? sniff.slice(rawStart, rawEnd) : ''
  if (!raw || raw.includes('SafeDirs'))
    fail('启动选根枚举 AGENT_PACK_TMP 下一层会 PA040：本机 tmp 有几百个目录')
  const checkoutStart = sniff.indexOf('static IEnumerable<string> CheckoutRoots')
  const checkoutEnd = sniff.indexOf('static IEnumerable<string> SafeDirs')
  const checkout = checkoutStart >= 0 && checkoutEnd > checkoutStart ? sniff.slice(checkoutStart, checkoutEnd) : ''
  if (!checkout || checkout.includes('SafeDirs'))
    fail('DshSniff.Bin 枚举 AGENT_PACK_TMP 下一层同样会 PA040')
}

const appState = read('Core/AppState.cs')
if (appState.includes('AppContext.BaseDirectory') && appState.includes('.pack-launcher-roots.json')
  && !appState.includes('pack-agent-dsh'))
  fail('启动器根名册还钉在每个 exe 目录，换文件夹就丢')
if (!appState.includes('LocalApplicationData') || !appState.includes('pack-agent-dsh'))
  fail('启动器根名册要写到用户 LocalAppData\\pack-agent-dsh')
if (!appState.includes('PickBootRoot')) fail('启动不按 PickBootRoot 共用有实例的根')
if (!appState.includes('PACK_LAUNCHER_ROOT')) fail('显式 PACK_LAUNCHER_ROOT 时启动仍会抢根')
if (appState.includes('AdoptNow()')) fail('自动切根不许默默收编 ~/.dsh')
if ((appState.match(/LauncherRootCandidates\(\)/g) || []).length > 1)
  fail('启动扫两遍启动器根，界面线程会再卡一轮')
{
  const ctor = appState.split('AppState()')[1]?.split('Instance? _selectedInstance')[0] ?? ''
  const timerAt = ctor.indexOf('_timer = new DispatcherTimer')
  const preferAt = ctor.indexOf('PreferSharedRoot')
  if (timerAt < 0 || preferAt < 0 || timerAt > preferAt)
    fail('PreferSharedRoot 在 DispatcherTimer 之前，空根切走时 ApplyUiBoot 会空引用')
}

const packs = read('Core/Packs.cs')
if (!packs.includes('Task<string> Import')) fail('Packs 没接 import')

const crashCs = read('Core/CrashAnalyzer.cs')
if (!crashCs.includes('crash-rules.json')) fail('崩溃分析不读 crash-rules')
if (!crashCs.includes('Regex.IsMatch')) fail('崩溃分析不配规则')

const adoptCs = read('MainWindow.xaml.cs')
if (!adoptCs.includes('EnsureRelease')) fail('收编前不确保发行号')
if (adoptCs.includes('先装一个 DSH 发行号，再收编')) fail('收编还在把人撵去下载页')
if (!read('Core/Launcher.cs').includes('EnsureRelease')) fail('Launcher 缺 EnsureRelease')
if (!read('MainWindow.xaml.cs').includes('MessageBox.Show') ||
    !read('MainWindow.xaml.cs').includes('if (bad)'))
  fail('报错还在用 toast 一闪而过')

const cli = read('Core/Cli.cs')
for (const verb of ['import', 'adopt', 'crash', 'update', 'shortcut', 'logs']) {
  if (!cli.includes(`"${verb}"`) && !cli.includes(`case "${verb}"`) && !cli.includes(`"${verb} "`)) {
    if (!cli.toLowerCase().includes(verb)) fail(`pad cli 缺 ${verb}`)
  }
}
if (!cli.includes('import') || !cli.includes('adopt') || !cli.includes('crash'))
  fail('pad cli 缺 import / adopt / crash')
if (!cli.includes('shortcut') || !cli.includes('logs') || !cli.includes('update'))
  fail('pad cli 缺 shortcut / logs / update')
if (!cli.includes('CrashAnalyzer')) fail('pad cli crash 没走 CrashAnalyzer')
if (cli.includes('Packs(launcher).Crash') || cli.includes('new Packs(launcher).Crash'))
  fail('pad cli crash 还在走 pack-agent')
if (!cli.includes('EnsureRelease')) fail('pad cli adopt 不确保发行号')
if (!cli.includes('case "sniff"')) fail('pad cli 缺 sniff')
if (!cli.includes('case "release" when cmd == "pin"')) fail('pad cli 缺 release pin')

const versions = read('Views/VersionsView.xaml')
if (!versions.includes('ContextMenu')) fail('实例列表没有右键菜单')
if (!read('Views/VersionsView.xaml.cs').includes('OpenLogs')) fail('版本页不能开日志')
if (versions.includes('左下角起个名字')) fail('版本选择还在教人往左下角看')
if (versions.includes('先到下载页装一个')) fail('新建实例还在把人撵去下载页')
if (versions.includes('选一个用来启动')) fail('profile 列表还在写操作说明')
if (versions.includes('这个实例还没有 profile')) fail('空 profile 还在写下面建一个')
if (versions.includes('终端会装上 dsh-tui')) fail('新建 profile 还在预告会装什么')
if (versions.includes('IsEnabled="{Binding HasReleases}"')) fail('没发行号时新建被灰掉')
if (!read('Views/VersionsView.xaml.cs').includes('EnsureRelease')) fail('新建实例不确保发行号')

if (read('Views/ManageView.xaml').includes('启动之后，这边列出')) fail('管理空态还在写启动之后')
if (read('Views/DownloadView.xaml').includes('填版本号再点安装')) fail('下载空态还在教人填版本号')
if (read('Views/DownloadView.xaml').includes('货架')) fail('下载页还在用货架')
if (read('Views/DownloadView.xaml').includes('装一个 DSH')) fail('下载页标题不是 PCL 下载游戏句式')
if (!read('Views/DownloadView.xaml').includes('下载 DSH')) fail('下载页没有对照 PCL 下载游戏')
if (!read('Views/DownloadView.xaml').includes('重置条件')) fail('搜索没有对照 PCL 重置条件')
if (read('Views/DownloadView.xaml').includes('关键词')) fail('搜索标签还在用关键词')
if (read('Views/DownloadView.xaml').includes('里的 --profile')) fail('下载页还在写实例里的 --profile')
if (!read('Views/VersionsView.xaml').includes('版本列表') && !read('Views/VersionsView.xaml').includes('x:Name="VerGroups"'))
  fail('版本选择右栏没有对照 PCL 的分组版本列表')
if (!read('Views/VersionsView.xaml').includes('实例列表'))
  fail('版本选择左栏没有实例列表，对照 PCL 文件夹列表')
if (!read('Views/VersionsView.xaml').includes('添加或导入'))
  fail('版本选择左栏底下没有添加或导入')
if (!read('Views/VersionsView.xaml').includes('导入整合包'))
  fail('添加或导入没有导入整合包')
if (read('Views/VersionsView.xaml').includes('Tag="实例名"'))
  fail('新建实例还摊着输入框，对照 PCL 添加或导入是动作行')
if (!read('Views/VersionsView.xaml').includes('Binding Home') &&
    !read('Views/VersionsView.xaml').includes('Binding Path=Home'))
  fail('实例行没有路径，对照 PCL 文件夹 Location')
if (!read('Views/VersionsView.xaml').includes('无可用版本'))
  fail('空态标题不是无可用版本')
if (!read('Views/VersionsView.xaml').includes('GroupStyle') &&
    !read('Views/VersionsView.xaml').includes('x:Name="VerGroups"'))
  fail('右边没有按终端/网页分组')
if (!read('Core/AppState.cs').includes('GroupOrder'))
  fail('profile 行不会按终端/网页分组')
if (read('Views/SettingsView.xaml').includes('社区货架')) fail('设置下载层还在用货架')
if (read('Views/SettingsView.xaml').includes('本机嗅探')) fail('设置还在写本机嗅探')
if (read('Views/InstanceView.xaml').includes('喜欢再固化')) fail('试验还在写喜欢再固化')
if (read('Views/InstanceView.xaml').includes('关掉就不用了')) fail('整合包还在解释关掉的意思')
if (read('Views/InstanceView.xaml').includes('这个实例里聊过的')) fail('session 还在解释这是聊过的')
if (read('Views/InstanceView.xaml').includes('就会出现在这里')) fail('session 空态还在预告会出现')
if (read('Views/InstanceView.xaml.cs').includes('选一个 .pack.zip 装进来')) fail('整合包空列表还在教人选文件')

const instanceCs = read('Views/InstanceView.xaml.cs')
if (!instanceCs.includes('OpenLogs') || !instanceCs.includes('AnalyzeCrash'))
  fail('版本设置缺日志 / 崩溃分析')
const analyzeFn = instanceCs.split('AnalyzeCrash_Click')[1]?.slice(0, 800) ?? ''
if (analyzeFn.includes('Unavailable')) fail('崩溃分析还在等 pack-agent 源码')
if (!analyzeFn.includes('CrashAnalyzer')) fail('崩溃分析没走 CrashAnalyzer')

// ---- 学 PCL 纵深：窗口必须露出 CLI 已有链，禁止第二套逻辑 ----
if (!versions.includes('装整合包') && !versions.includes('导入整合包')) fail('版本选择左栏没装整合包')
if (!versions.includes('添加已有文件夹')) fail('版本选择左栏没添加已有文件夹')
const versionsCs = read('Views/VersionsView.xaml.cs')
if (!versionsCs.includes('ImportPack_Click') && !versionsCs.includes('Import_Click'))
  fail('版本选择不能走 import')
if (!versionsCs.includes('AdoptNow') && !versionsCs.includes('Adopt_Click'))
  fail('版本选择不能收编')
if (!versionsCs.includes('Clone_Click')) fail('版本选择不能克隆')
if (!versionsCs.includes('Export_Click')) fail('版本选择不能导出 pinst')

const instanceXaml = read('Views/InstanceView.xaml')
if (!instanceXaml.includes('InstCred')) fail('实例设置不能覆盖凭据')
if (!instanceXaml.includes('IsThreeState')) fail('实例开关不能三态跟随全局')
if (!instanceXaml.includes('恢复跟随全局')) fail('实例设置不能清 launch 覆盖')
{
  const settings = instanceXaml.slice(
    instanceXaml.indexOf('x:Name="PaneSettings"'),
    instanceXaml.indexOf('x:Name="PanePresets"'),
  )
  if (!settings.includes('Style="{StaticResource CardHead}"'))
    fail('实例设置卡标题不是 CardHead，对照 PCL MyCard')
  if (!settings.includes('Text="启动选项"')) fail('实例设置没有启动选项卡')
  if (!settings.includes('Text="高级选项"')) fail('实例设置没有高级选项卡，对照 PCL PageInstanceSetup')
  if (settings.includes('SlabHead'))
    fail('实例设置卡标题还在用 SlabHead 图标砖')
  if (settings.includes('实例覆盖')) fail('实例设置还在用 Rule 实例覆盖，对照 PCL 用卡片')
  if (!settings.includes('Grid.IsSharedSizeScope="True"'))
    fail('实例设置没有 SharedSizeScope，对照 PCL 标签列对齐')
  if (!settings.includes('SharedSizeGroup="Name"'))
    fail('实例设置标签没有 SharedSizeGroup Name')
  if (!settings.includes('Style="{StaticResource Banner}"'))
    fail('实例设置没有只对该实例生效的提示条')
  if (!settings.includes('只对该实例生效'))
    fail('实例设置没有写清只对该实例生效')
  if (settings.includes('Style="{StaticResource Muted}" VerticalAlignment="Center"'))
    fail('实例设置字段名还是 Muted 微字，对照 PCL 正文')
  if (/Grid Height="28"[\s\S]{0,500}x:Name="InstEnvBox"/.test(settings))
    fail('额外环境变量还塞在 28 高行里，会裁切叠到下一栏')
  if (/Grid Height="28"[\s\S]{0,500}x:Name="InstPreCommandBox"/.test(settings))
    fail('启动前命令还塞在 28 高行里，会裁切叠到下一栏')
  if (!/HorizontalAlignment="Center"[\s\S]{0,240}全局启动设置/.test(settings)
    && !/全局启动设置[\s\S]{0,240}HorizontalAlignment="Center"/.test(settings))
    fail('全局启动设置没有居中，对照 PCL 版本设置底的全局设置')
}
if (!instanceXaml.includes('NavPresets')) fail('实例页没有 agent-preset 分段')
if (!instanceXaml.includes('保存当前')) fail('整合包没有白名单套装')
if (!instanceCs.includes('PluginAdd_Click')) fail('组合包没有正式 add')
if (!instanceCs.includes('CopyPreset_Click')) fail('不能复制 agent-preset')
if (!instanceCs.includes('DumpConfig_Click')) fail('概览没有 dump-config')
{
  const overviewEnd = instanceXaml.includes('x:Name="PanePlugins"')
    ? instanceXaml.indexOf('x:Name="PanePlugins"')
    : instanceXaml.indexOf('x:Name="PaneBundles"')
  const overview = instanceXaml.slice(
    instanceXaml.indexOf('x:Name="PaneOverview"'),
    overviewEnd,
  )
  if (!overview.includes('个性化')) fail('概览没有个性化卡')
  if (overview.includes('SlabHead'))
    fail('概览卡标题还在用 SlabHead 图标砖，对照 PCL MyCard 只有字')
  if (!overview.includes('Style="{StaticResource CardHead}"'))
    fail('概览卡标题不是 CardHead，对照 PCL MyCard 主题色 13px 粗体')
  if (!overview.includes('Height="42"')) fail('概览身份卡不是 PCL 列表项 42 高')
  if (!overview.includes('SharedSizeGroup')) fail('概览按钮没有 SharedSizeGroup，对照 PCL 三键等宽')
  if (overview.includes('Style="{StaticResource Label}"'))
    fail('概览图标/分类还是微字 Label，对照 PCL 正文')
  if (!overview.includes('修改版本名')) fail('概览不能改名')
  if (!overview.includes('修改版本描述')) fail('概览不能改描述')
  if (!overview.includes('加入收藏夹')) fail('概览不能收藏')
  if (!overview.includes('快捷方式')) fail('概览没有快捷方式卡')
  if (!overview.includes('实例文件夹')) fail('概览快捷方式没有实例文件夹')
  if (!overview.includes('高级管理')) fail('概览没有高级管理卡')
  if (!overview.includes('导出启动脚本')) fail('概览不能导出启动脚本')
  if (!overview.includes('删除实例')) fail('概览高级管理不能删实例')
  if (overview.includes('这份实例')) fail('概览还在摊只读字段')
  if (overview.includes('选 logo')) fail('概览图标还不是下拉')
  if (overview.includes('打开 $DSH_HOME')) fail('概览快捷方式还在写打开路径')
  if (overview.includes('Content="启动脚本"')) fail('概览还在列启动脚本路径')
  if (overview.includes('Content="改名"')) fail('概览改名还是行内输入框')
  if (overview.includes('NoteBox')) fail('概览还在摊 note 输入框')
}
if (!instanceCs.includes('OverviewPipe')) fail('自定义描述时身份卡没有 | 发行号，对照 PCL CustomInfo')
if (!instanceCs.includes('DisplayLine')) fail('概览副标题没有凝练成发行号, 终端/网页')
if (!versionsCs.includes('ShowHidden')) fail('版本列表没有 ShowHidden，对照 PCL F11')
if (!versions.includes('x:Key="InstSrc"')) fail('版本列表没有独立 CollectionViewSource，不能按 hidden 过滤')
if (!read('MainWindow.xaml.cs').includes('Key.F11'))
  fail('版本列表没有 F11 看隐藏实例，对照 PCL')
if (!read('MainWindow.xaml').includes('x:Name="MsgLayer"'))
  fail('主窗没有 PCL 式层内弹窗 MsgLayer')
if (!read('MainWindow.xaml.cs').includes('AskInput'))
  fail('主窗没有 AskInput，改名还得开系统小窗')
if (!read('MainWindow.xaml.cs').includes('MsgCancel.Focus'))
  fail('警告确认没有把焦点放在取消上，回车会直接删')
if (/void DeleteInstance_Click[\s\S]{0,800}MessageBox\.Show/.test(instanceCs))
  fail('删除实例还在用系统 MessageBox，对照 PCL 层内确认')
if (!instanceCs.includes('_win.AskInput') || !instanceCs.includes('_win.Ask('))
  fail('概览改名/删除没走层内弹窗')
if (/string\? Prompt\([\s\S]{0,400}new Window/.test(instanceCs))
  fail('Prompt 还在 new Window 系统框')
if (/void Delete_Click[\s\S]{0,800}MessageBox\.Show/.test(versionsCs))
  fail('版本列表删除还在用系统 MessageBox')
if (/static string\? Prompt\([\s\S]{0,400}new Window/.test(versionsCs))
  fail('版本列表 Prompt 还在系统框')
if (!instanceCs.includes('已加入收藏夹') || !instanceCs.includes('已从收藏夹中移除'))
  fail('加入/移出收藏夹点完没有文案反馈')
if (!instanceXaml.includes('x:Name="LogoPathBox"') || !instanceXaml.includes('LogoBrowse'))
  fail('自定义图标没有路径框和选文件')
if (!instanceXaml.includes('x:Name="LogoLivePreview"'))
  fail('自定义图标没有预览')
if (/void LogoBox_Sel[\s\S]{0,700}OpenFileDialog/.test(instanceCs))
  fail('选自定义还直接弹系统选文件，没有先出路径框')
if (instanceXaml.includes('x:Name="NavPacks"'))
  fail('版本设置左栏还有整合包，对照 PCL 版本设置就是当前这份')
if (!instanceXaml.includes('x:Name="OverviewIntro"'))
  fail('概览不展示介绍文本')
if (instanceXaml.includes('x:Name="EditIntroBtn"'))
  fail('介绍/详细还在身份卡右端单独编辑，应并进修改版本描述')
if (!/void Desc_Click[\s\S]{0,400}OpenDescEdit/.test(instanceCs))
  fail('修改版本描述没有接管介绍和 readme')
if (!instanceXaml.includes('x:Name="InfoBox"'))
  fail('修改版本描述编辑里没有版本描述一行')
{
  const card = instanceXaml.slice(
    instanceXaml.indexOf('x:Name="OverviewMark"'),
    instanceXaml.indexOf('Style="{StaticResource Slab}"'),
  )
  if (!card.includes('x:Name="OverviewIntro"') || !card.includes('x:Name="OpenReadmeBtn"'))
    fail('介绍和详细介绍不在身份卡里面')
}
if (!instanceCs.includes('readme.md') || !instanceXaml.includes('x:Name="IntroBox"'))
  fail('不能编辑介绍和 readme.md')
if (!instanceXaml.includes('x:Name="IntroEditScroll"'))
  fail('修改版本描述卡没有滚动区，矮窗会裁掉确定/取消')
if (!read('MainWindow.xaml').includes('x:Name="MdLayer"'))
  fail('详细介绍还在另开系统小窗，应盖在主窗里渲染')
if (!read('MainWindow.xaml.cs').includes('ShowMd') || !instanceCs.includes('ShowMd'))
  fail('详细介绍没有走主窗 ShowMd')
if (!read('Views/Md.cs').includes('TryHeading'))
  fail('markdown 不能解析 ##2222## 这种标题')
if (!read('Views/Fx.cs').includes('PulseIn'))
  fail('层内卡片没有 PulseIn 入场')
if (!read('Views/Md.cs').includes('FlowDocument'))
  fail('markdown 没有渲成 FlowDocument')
if (!read('MainWindow.xaml').includes('x:Name="MsgPreview"'))
  fail('拖图确认没有预览')
if (!read('Core/LaunchPolicy.cs').includes('ImagePaths'))
  fail('主窗拖图没有 ImagePaths，会被当成非包丢掉')
if (!read('Core/Models.cs').includes('[JsonPropertyName("intro")]'))
  fail('instance.json 没有 intro 介绍字段')

if (instanceXaml.includes('Style="{StaticResource SegTab}"'))
  fail('版本设置还在用顶栏分段，没有左栏')
if (!instanceXaml.includes('Style="{StaticResource NavItem}"'))
  fail('版本设置左栏没有对照设置页 NavItem')
if (!instanceXaml.includes('ColumnDefinition Width="168"'))
  fail('版本设置没有左栏')
if (!read('MainWindow.xaml.cs').includes('版本设置 - '))
  fail('版本设置标题没有版本名')
if (!versions.includes('Content="DSH 版本"')) fail('版本选择不能钉 DSH 版本')
if (!versions.includes('x:Name="MatchPacks"')) fail('版本选择不能匹配整合包')
if (!versionsCs.includes('PinApply_Click')) fail('版本选择不能改钉发行号')
if (launchXaml.includes('个组合包')) fail('启动页还在用组合包数当启动匹配')
if (!launchXaml.includes('个整合包')) fail('启动页不显示整合包')
if (!read('Core/AppState.cs').includes('PackSummary') &&
    !read('Core/AppState.cs').includes('EnabledPackCount'))
  fail('AppState 没有整合包匹配给启动页')

if (!models.includes('pack-agent.pad.settings/v4')) fail('pad.json schema 不是 v4')
if (!models.includes('credentialsSet')) fail('instance.json 不能覆盖凭据')
if (!models.includes('CredentialsDefault')) fail('pad.json 没有默认凭据')

if (!settingsXaml.includes('导出设置') || !settingsXaml.includes('导入设置'))
  fail('设置不能导入导出')
if (!settingsXaml.includes('复制识别码')) fail('设置没有识别码')
if (!settingsCs.includes('ExportSettings_Click') || !settingsCs.includes('ImportSettings_Click'))
  fail('设置导入导出没接到按钮')
if (!settingsXaml.includes('x:Name="NavApi"')) fail('设置左栏没有 API Key 层')
if (!settingsXaml.includes('Tag="api"')) fail('设置 API Key 层没有 Tag=api')
if (!existsSync(join(pad, 'Views/ApiImportWindow.xaml'))) fail('导入 API Key 没有独立窗口')
if (!existsSync(join(pad, 'Views/ApiConfigWindow.xaml'))) fail('配置管理没有独立窗口')
const importXaml = read('Views/ApiImportWindow.xaml')
const importCs = read('Views/ApiImportWindow.xaml.cs')
const configXaml = read('Views/ApiConfigWindow.xaml')
const configCs = read('Views/ApiConfigWindow.xaml.cs')
if (!settingsXaml.includes('x:Name="ApiImportBtn"')) fail('API 管理主页没有导入入口')
if (!settingsXaml.includes('x:Name="ApiConfigBtn"')) fail('API 管理主页没有配置管理入口')
if (!settingsXaml.includes('x:Name="ApiReadyList"')) fail('主页看不到已导入的 API')
if (!settingsXaml.includes('{Binding Company}') && !settingsXaml.includes('所属公司'))
  fail('主页看不到导入 API 的所属公司')
if (!settingsXaml.includes('x:Name="ApiStatImported"')) fail('API 管理没有计数，对照 geo-validator Overview')
if (!settingsXaml.includes('{Binding Instances}')) fail('API 行看不到用在哪些实例')
if (!settingsXaml.includes('用在哪些实例') && !settingsXaml.includes('用在'))
  fail('API 名册没有实例列')
if (settingsXaml.includes('x:Name="ApiStatRow"'))
  fail('stats 还是三个裸数字，对照 geo-validator 是四格看板')
if (settingsXaml.includes('x:Name="ApiStatChannels"') || settingsXaml.includes('>渠道<') ||
    settingsXaml.includes('Text="渠道"'))
  fail('API 管理页写成了渠道，这一页只做 API')
if (!settingsXaml.includes('x:Name="ApiStatApis"')) fail('没有 API 条数')
if (!settingsXaml.includes('UniformGrid')) fail('stats 不是四格看板')
if (!read('Core/CredentialsFile.cs').includes('Catalog'))
  fail('API 没有公司名册，空页只能干等导入')
if (!read('Core/CredentialsGate.cs').includes('CredentialsFile.Catalog'))
  fail('ListImported 不走公司名册，空 API 出不来')
if (!read('Core/CredentialsGate.cs').includes('RefMissing'))
  fail('API 行分不出暂无 Key')
if (!settingsXaml.includes('Style="{StaticResource Card}"')) fail('详情层没有卡')
if (!settingsXaml.includes('暂无 Key')) fail('没 Key 的 API 要显示暂无 Key')
if (!settingsXaml.includes('x:Name="ApiHeroBoard"')) fail('没有 credentials 实况板，对照 Overview hero-board')
if (!importCs.includes('StartRef')) fail('导入不能预填该公司 refs')
if (!settingsCs.includes('ApiImportRow_Click')) fail('详情不能开导入窗')
if (settingsXaml.includes('Width="340"')) fail('定宽卡会把后面的 API 挤出视口')
if (settingsXaml.includes('ItemsControl.ItemsPanel')) fail('API 还在两列换行，后面的看不见')
if (!settingsXaml.includes('x:Name="ApiDetailStage"')) fail('API 不能点进去看信息')
if (settingsCs.includes('ApiChannel_Click')) fail('还在用渠道点法')
if (!settingsCs.includes('ApiRow_Click')) fail('列表项点不进第二层')
if (!settingsXaml.includes('Style="{StaticResource PickRow}"')) fail('API 列表不是 PCL 那种可点的框')
if (settingsXaml.includes('{Binding Glyph}')) fail('还在用字母砖冒充厂家图标')
if (!settingsXaml.includes('Source="{Binding Icon}"')) fail('厂家 API 没有用对应图标')
for (const f of [
  'deepseek.png', 'siliconflow.png', 'openrouter.png', 'openai.png',
  'anthropic.png', 'kimi.png', 'qwen.png', 'glm.png', 'doubao.png',
  'gemini.png', 'hunyuan.png',
]) {
  const p = join(pad, 'Assets/api', f)
  if (!existsSync(p)) fail(`缺厂家图标 ${f}`)
  if (statSync(p).size < 2000) fail(`${f} 还是糊 favicon，要用厂家图标`)
}
if (!read('Pad.csproj').includes('Assets\\api\\')) fail('厂家图标没编进 exe')
if (!read('Core/CredentialsFile.cs').includes('SILICONFLOW_API_KEY'))
  fail('公司名册还是手写四家，没学 geo 各公司')
if (!read('Core/CredentialsFile.cs').includes('BaseUrl'))
  fail('API 没有 baseURL，不能拉 /models')
if (!existsSync(join(pad, 'Core/ApiModels.cs'))) fail('没有拉上游 /models')
if (!read('Core/ApiModels.cs').includes('/models')) fail('检测不走上游 /models')
if (!read('Core/ApiModels.cs').includes('ParseIds')) fail('上游 /models 的 id 解析没写')
if (!read('Core/CredentialsFile.cs').includes('ReadRef'))
  fail('检测要用 yaml 里的 ref，不能只读 DEEPSEEK')
if (!importXaml.includes('IsReadOnly')) fail('refs 还在手填')
if (!settingsXaml.includes('x:Name="ApiLiveModels"')) fail('详情层看不到上游模型')
if (settingsXaml.includes('装载配置')) fail('API 管理还在装载配置')
if (settingsXaml.includes('x:Name="CredentialsDefaultBox"')) fail('装载默认配置还在 API 页')
if (settingsXaml.includes('x:Name="ApiMatchList"')) fail('匹配实例还在 API 页')
if (settingsXaml.includes('{Binding SetName}')) fail('API 名单还挂着配置名')
if (importXaml.includes('x:Name="ImportSetBox"') || importXaml.includes('写入配置'))
  fail('导入窗还在选配置')
if (!configXaml.includes('装载配置')) fail('装载配置不在配置管理')
if (!configXaml.includes('x:Name="CredentialsDefaultBox"')) fail('配置管理不能选默认装载')
if (!configXaml.includes('x:Name="ApiMatchList"')) fail('配置管理看不到哪份匹配哪些实例')
if (settingsXaml.includes('PasswordBox')) fail('API 管理主页还在填密钥')
if (settingsXaml.includes('x:Name="DeepseekKeyBox"')) fail('API 管理主页还在填 DEEPSEEK_API_KEY')
if (settingsXaml.includes('x:Name="RefNameBox"') || settingsXaml.includes('x:Name="RefSecretBox"'))
  fail('API 管理主页还在写 refs')
if (settingsXaml.includes('x:Name="CredEditor"')) fail('API 管理主页还在编辑 yaml')
if (settingsXaml.includes('x:Name="ProviderRoster"')) fail('API 管理主页还在配配方')
if (settingsXaml.includes('供应商配方')) fail('API 管理主页还堆着配方')
if (!importXaml.includes('PasswordBox')) fail('导入必须是 PasswordBox，不能明文 TextBox')
if (!importXaml.includes('DEEPSEEK_API_KEY')) fail('导入窗没有 DEEPSEEK_API_KEY，YAML 空框不算配置口')
if (!importXaml.includes('x:Name="DeepseekKeyBox"') && !importXaml.includes('x:Name="RefSecretBox"'))
  fail('导入窗没有密钥框')
if (!importXaml.includes('x:Name="RefNameBox"')) fail('导入窗不能指定 refs 名')
if (!importCs.includes('IconPack')) fail('导入窗公司没有厂家图标')
{
  const themeKeys = new Set<string>()
  const keyRe = /x:Key="([^"]+)"/g
  for (const f of ['Palette.xaml', 'Controls.xaml', 'Inputs.xaml', 'Icons.xaml']) {
    const t = read('Theme/' + f)
    let m
    while ((m = keyRe.exec(t))) themeKeys.add(m[1])
  }
  for (const [name, xaml] of [['导入', importXaml], ['配置管理', configXaml]] as const) {
    const usedRe = /\{StaticResource\s+([^}]+)\}/g
    let m
    while ((m = usedRe.exec(xaml))) {
      const key = m[1].trim()
      if (!themeKeys.has(key)) fail(`${name}窗引用了不存在的 ${key}，点按钮会崩`)
    }
  }
}
if (!importCs.includes('UpsertRef') && !importCs.includes('UpsertDeepseekKey'))
  fail('导入窗没有把 PasswordBox 写进 credentials.yaml')
if (!read('Core/CredentialsFile.cs').includes('UpsertRef'))
  fail('API 管理器不能写任意 refs，只有 DEEPSEEK_API_KEY')
if (!read('Core/CredentialsFile.cs').includes('CompanyOf'))
  fail('导入的 API 没有所属公司')
if (!read('Core/CredentialsGate.cs').includes('ListImported'))
  fail('主页列不出已导入的 API')
function companyOf(ref: string): string {
  if (ref === 'DEEPSEEK_API_KEY') return 'DeepSeek'
  if (ref === 'OPENAI_API_KEY') return 'OpenAI'
  if (ref === 'ANTHROPIC_API_KEY') return 'Anthropic'
  if (ref === 'ACME_GATEWAY_API_KEY') return 'Acme'
  return '未标注'
}
if (companyOf('DEEPSEEK_API_KEY') !== 'DeepSeek') fail('DEEPSEEK_API_KEY 所属公司必须是 DeepSeek')
if (companyOf('OPENAI_API_KEY') !== 'OpenAI') fail('OPENAI_API_KEY 所属公司必须是 OpenAI')
if (companyOf('FOO_KEY') !== '未标注') fail('未知 refs 不能冒充公司名')
if (!configXaml.includes('x:Name="CredList"')) fail('配置管理不能管 credentials 配置')
if (importXaml.includes('套装') || settingsXaml.includes('>套装<'))
  fail('API Key 三层用了「套装」，用户原话是配置')
if (!configXaml.includes('x:Name="CredEditor"')) fail('配置管理不能编辑 credentials.yaml')
if (!configXaml.includes('x:Name="ProviderRouteBox"') || !configXaml.includes('x:Name="ProviderUrlBox"'))
  fail('配置管理没有路由名和 baseURL')
if (!configXaml.includes('apiKeyEnv')) fail('供应商配方没有 apiKeyEnv，会对着 DSH 契约把密钥写进方法文件')
if (!configXaml.includes('x:Name="ProviderModelsBox"')) fail('供应商配方不能填模型列表')
if (!configXaml.includes('x:Name="ProviderRoster"'))
  fail('供应商配方没有名册，还是下拉加一张表')
if (configXaml.includes('x:Name="ProviderList"'))
  fail('供应商名册还在用 ProviderList 下拉，看不出已配/缺 ref')
if (!configXaml.includes('x:Name="ProviderEmpty"'))
  fail('供应商名册没有空状态')
if (!configXaml.includes('x:Name="ProviderPreset"'))
  fail('不能从 DSH 常见路由套预设')
if (!configXaml.includes('x:Name="ProviderHint"'))
  fail('明细没有将会进入 session.models 的提示条')
if (!configXaml.includes('x:Name="ProviderEnvBox"') || !configXaml.includes('IsEditable="True"'))
  fail('apiKeyEnv 不能从已有 refs 里选')
if (settingsCs.includes('session.selectModel') || importCs.includes('session.selectModel') ||
    configCs.includes('session.selectModel'))
  fail('设置页掺进了 session.selectModel，通信回退还没到')
if (!instanceXaml.includes('x:Name="InstWsPath"') || !instanceXaml.includes('x:Name="InstWsBrowse"'))
  fail('工作区不能自己填路径或浏览')
if (!read('Core/Launcher.cs').includes('SetWorkspacePath'))
  fail('工作区路径没有 SetWorkspacePath')
if (!instanceXaml.includes('x:Name="InstNodeKind"'))
  fail('实例 Node 没有对照 PCL Java 的下拉')
if (!instanceXaml.includes('x:Name="InstNodeHint"'))
  fail('实例 Node 没有将会使用提示条')
if (!instanceXaml.includes('将会使用'))
  fail('实例 Node 提示条没有将会使用')
if (!read('Theme/Controls.xaml').includes('x:Key="HintBar"'))
  fail('没有 PCL 左侧色条 HintBar')
if (!read('Core/LaunchPolicy.cs').includes('DescribeNodeWillUse'))
  fail('Node 提示文案没有 DescribeNodeWillUse')
if (!read('Core/LaunchPolicy.cs').includes('NodeAuto'))
  fail('实例自动选择 Node 没有 auto 标记，会和跟随全局分不开')
if (!settingsXaml.includes('x:Name="NodeKind"') || !settingsXaml.includes('x:Name="NodeHint"'))
  fail('全局 Node 还是纯路径框，没有对照 PCL Java')
if (!settingsXaml.includes('x:Name="NodeBrowse"'))
  fail('指定 Node 没有浏览')

function nodeWillUse(exe: string | null, engines: string | null): string {
  if (!exe) {
    return engines
      ? `找不到 Node ${engines}。勾选「发行号缺 Node 时装进 runtime/node」，启动时会下载。`
      : '找不到 Node。勾选「发行号缺 Node 时装进 runtime/node」，启动时会下载。'
  }
  return `将会使用：Node ${engines || 'node'}: ${exe}`
}
if (!nodeWillUse('E:\\n\\node.exe', '22.19.0').startsWith('将会使用：Node 22.19.0:'))
  fail('有路径时提示必须是将会使用')
if (!nodeWillUse(null, '22.19.0').includes('找不到 Node 22.19.0'))
  fail('缺 Node 时要写出发行号要求的版本')
if (settingsXaml.includes('钥匙')) fail('设置还在叫钥匙，窗口里就叫 API Key')

function credNamedPath(name: string, libraryDir: string) {
  return name === 'global' ? libraryDir + '\\credentials.yaml' : libraryDir + '\\credentials\\' + name + '.yaml'
}
function credYamlPath(set: string | null, def: string, home: string, libraryDir: string) {
  if (!set) {
    if (def === 'none') return home + '\\.credentials.yaml'
    return credNamedPath(def, libraryDir)
  }
  if (set === 'instance') return home + '\\.credentials.yaml'
  return credNamedPath(set, libraryDir)
}
if (credYamlPath(null, 'global', 'E:\\i\\tui\\home', 'E:\\L\\library') !== 'E:\\L\\library\\credentials.yaml')
  fail('默认 API Key 必须写启动器 library，不跟实例走')
if (credYamlPath('instance', 'global', 'E:\\i\\tui\\home', 'E:\\L\\library') !== 'E:\\i\\tui\\home\\.credentials.yaml')
  fail('显式 instance 才用这份 home')
if (credYamlPath('work', 'global', 'E:\\i\\tui\\home', 'E:\\L\\library') !== 'E:\\L\\library\\credentials\\work.yaml')
  fail('具名钥匙仍在 library/credentials/')

function credInspectLocation(hasLibrary: boolean, hasHome: boolean, set: string | null) {
  if (set === 'instance') return hasHome ? 'home' : 'home-missing'
  if (hasLibrary) return 'library'
  if (hasHome) return 'home'
  return 'library'
}
if (credInspectLocation(true, false, null) !== 'library') fail('library 有钥匙就算配好，home 空也行')
if (credInspectLocation(false, true, null) !== 'home') fail('library 空时才认 home 投影')
if (credInspectLocation(false, false, null) !== 'library') fail('缺钥匙时 PA116 要指 library，不是某份实例 home')

function credCopyToHome(adopted: boolean, set: string | null, def: string) {
  if (adopted) return false
  if (set === 'instance') return false
  const name = set || def
  return name !== 'none'
}
if (!credCopyToHome(false, null, 'global')) fail('保存分发器后要拷进非收编实例 home')
if (credCopyToHome(true, null, 'global')) fail('收编的 home 不分发覆盖')
if (credCopyToHome(false, 'instance', 'global')) fail('instance 覆盖不分发')

const gateCs = read('Core/CredentialsGate.cs')
if (!gateCs.includes('DistributeToHomes')) fail('凭据分发器没把 library 拷进实例 home')
if (!gateCs.includes('CopyDistributorToHome')) fail('缺单实例分发')
const createInstFn = read('Core/Launcher.cs').split('CreateInstance(')[1]?.split('\n    public ')[0] ?? ''
if (!createInstFn.includes('CopyDistributorToHome') && !createInstFn.includes('DistributeToHomes'))
  fail('新建实例不从 library 分发钥匙')
if (launchXaml.includes('DEEPSEEK_API_KEY')) fail('启动页还钉着 API Key，污染启动页')
if (launchXaml.includes('DeepseekKeyBox') || launchXaml.includes('SaveKey_Click'))
  fail('启动页还在填 API Key')
if (launchXaml.includes('钥匙')) fail('启动页还在叫钥匙')
if (!launchCs.includes('PA116') && !launchCs.includes('CredentialsFile.MissingError'))
  fail('缺 DEEPSEEK_API_KEY 时启动页不能静默点启动')
if (!launchCs.includes('ShowTab("manage"') && !launchCs.includes('Show("manage"'))
  fail('点启动不切到管理顶栏')
if (launchCs.includes('ShowInner("manage"'))
  fail('点启动还把管理做成内页，顶栏就没有管理')
if (!existsSync(join(pad, 'Views/LaunchFloat.xaml')))
  fail('缺启动进度悬浮窗')
if (!read('Views/LaunchFloat.xaml').includes('DropShadowEffect'))
  fail('启动悬浮窗没有阴影')
if (!read('Views/LaunchFloat.xaml').includes('x:Name="LaunchMeter"'))
  fail('启动悬浮窗没有进度条')
if (!read('Views/LaunchFloat.xaml.cs').includes('Begin(') && !read('Views/LaunchFloat.xaml.cs').includes('Run('))
  fail('启动悬浮窗不接启动过程')
if (!read('Views/LaunchFloat.xaml').includes('Dot0') && !read('Views/LaunchFloat.xaml.cs').includes('Bounce'))
  fail('启动悬浮窗没有小动画')
if (read('Views/ManageView.xaml').includes('x:Name="LaunchMeter"') ||
    read('Views/ManageView.xaml').includes('x:Name="LaunchPad"'))
  fail('管理页还塞着启动进度，进度要单独悬浮窗')
if (read('Views/ManageView.xaml.cs').includes('public async Task Begin('))
  fail('管理页还在自己跑启动过程')
if (!read('Views/ManageView.xaml').includes('ItemsSource="{Binding Instances}"'))
  fail('管理左栏不是实例名单')
if (!read('MainWindow.xaml').includes('Content="管理"'))
  fail('顶栏没有「管理」')
if (settingsXaml.includes('Tag="providers"') || settingsXaml.includes('x:Name="NavProviders"'))
  fail('设置左栏不能再单开供应商')
if (settingsCs.includes('"providers"'))
  fail('OpenLayer 还在切供应商层')
if (settingsXaml.includes('x:Name="PaneProviders"'))
  fail('供应商还是独立层')
if (!settingsCs.includes('"api"')) fail('OpenLayer 切不到 API Key 层')
if (!read('MainWindow.xaml.cs').includes('OpenSettingsApi'))
  fail('缺 API Key 时不能跳到设置 API Key')
if (!read('Views/Fx.cs').includes('MeterTo'))
  fail('进度条没有动画')
if (read('Core/CredentialsGate.cs').includes('GetEnvironmentVariable'))
  fail('Inspect 把 PAD 进程 env 当成 DSH 已配好，进去还是没 key')
if (!read('Core/CredentialsFile.cs').includes('CmdLoadFromHome'))
  fail('启动脚本必须运行时从 %DSH_HOME%\\.credentials.yaml 加载 DEEPSEEK_API_KEY')
if (!read('Core/CredentialsFile.cs').includes('@set "DEEPSEEK_API_KEY='))
  fail('读钥匙的 set 没有 @，echo on 会把密钥打到终端')
if (!read('Core/Runner.cs').includes('CmdLoadFromHome'))
  fail('WriteScript 不加载 home yaml，wt 新开的 cmd 吃不到 API Key')
if (read('Core/Runner.cs').includes('CmdExport'))
  fail('WriteScript 还在 CmdExport 把密钥写进 launch-*.cmd')
if (!read('Views/ManageView.xaml.cs').includes('DeepseekConfigured'))
  fail('管理口通了也不查 credentials.describe，缺 key 不会报')
if (!read('Core/Gateway.cs').includes('credentials.describe'))
  fail('Gateway 没有 credentials.describe')
if (!read('Core/CredentialsFile.cs').includes('DEEPSEEK_API_KEY')) fail('缺 CredentialsFile')
if (!readFileSync(join(import.meta.dirname, 'registries/pa-codes.json'), 'utf8').includes('PA116'))
  fail('pa-codes 缺 PA116')
if (!read('Core/Launcher.cs').includes('always listed even if the file is missing'))
  fail('global 凭据在文件不存在时必须仍出现在列表里')
if (!read('Core/Cli.cs').includes('credentials" when cmd == "has"') &&
    !read('Core/Cli.cs').includes('cmd == "has"'))
  fail('pad cli credentials has 要能查出缺 key')

{
  const pagesJson = JSON.parse(readFileSync(join(import.meta.dirname, 'registries/pad-pages.json'), 'utf8'))
  const tasks = pagesJson.entries.find((e: { id: string }) => e.id === 'tasks')
  const manage = pagesJson.entries.find((e: { id: string }) => e.id === 'manage')
  if (!tasks?.disabled) fail('下载管理在下载页左栏，不要占顶栏任务')
  if (manage?.disabled) fail('管理必须出现在顶栏')
  if (manage?.title !== '管理') fail('顶栏条目要叫管理')
}
if (!mainXaml.includes('Tag="launch"') || !mainXaml.includes('Tag="manage"') ||
    !mainXaml.includes('Tag="download"') || !mainXaml.includes('Tag="settings"'))
  fail('顶栏必须有启动 / 管理 / 下载 / 设置')
if (!/x:Name="TabTasks"[\s\S]{0,280}Visibility="Collapsed"/.test(mainXaml))
  fail('顶栏任务还露着，下载管理在下载页')
if (!read('Views/TaskView.xaml.cs').includes('ListJobs')) fail('任务页不读 job 队列')
if (read('Views/TaskView.xaml').includes('Content="{Binding}"'))
  fail('任务页 Content="{Binding}" 会把本页塞进自己，点任务卡死')

function isSelfHosted(root: string, childContents: string[]): boolean {
  return childContents.some((c) => c === root)
}
if (!isSelfHosted('TaskView', ['TaskView'])) fail('页把自己塞进自己时应拦住')
if (isSelfHosted('TaskView', ['跑着', '0%'])) fail('普通绑定不该当自宿主')

const appCs = read('App.xaml.cs')
const hangCs = existsSync(join(pad, 'Core/UiHang.cs')) ? read('Core/UiHang.cs') : ''
const mainCs = read('MainWindow.xaml.cs')
if (!hangCs.includes('user32.dll')) fail('界面卡住时不能走 WPF MessageBox，必须 user32 弹窗')
if (!hangCs.includes('界面卡住了')) fail('卡住没有中文报错文案')
if (!hangCs.includes('点确定结束 PAD')) fail('卡住弹窗不说明会结束进程')
if (!hangCs.includes('ThrowIfSelfHosted')) fail('切页不检查自己塞自己')
if (!hangCs.includes('QuietFor')) fail('切页后看门狗没有宽限，首帧会误报 PA040')
if (!mainCs.includes('UiWatchdog.QuietFor')) fail('切页不通知看门狗')
if (!hangCs.includes('MainHwnd')) fail('卡住弹窗没有缓存的窗句柄')
if (!existsSync(join(pad, 'Core/WinIcon.cs')) || !read('Core/WinIcon.cs').includes('WM_SETICON'))
  fail('透明窗 HWND 不设图标，任务栏和 MessageBox 是系统默认')
if (!read('Core/Proc.cs').includes('await Task.Run'))
  fail('Proc.Run 在界面线程上扫 PATH')
{
  const procCs = read('Core/Proc.cs')
  if (!procCs.includes('ThreadPool') && !procCs.includes('QueueUserWorkItem'))
    fail('FindDshPid 在界面线程等 powershell 查 node，点启动后 PA040')
  if (/WaitForExit\(4000\)[\s\S]{0,800}ReadToEnd/.test(procCs))
    fail('查 node 命令行先 WaitForExit(4s) 再 ReadToEnd：管道一满就被杀掉，TUI 已起来仍 PA025')
  if (!procCs.includes('BeginOutputReadLine') && !procCs.includes('ManagementObjectSearcher') && !procCs.includes('CimSession'))
    fail('查 node 命令行必须边读 stdout 边等，或走 WMI API，禁止同步堵管道')
}
{
  const floatCs = read('Views/LaunchFloat.xaml.cs')
  if (!floatCs.includes('Task.Run'))
    fail('LaunchFloat 在界面线程 WriteScript/Relink，点启动 PA040')
  if (!floatCs.includes('QuietFor'))
    fail('点启动不通知看门狗')
}
if (read('Views/VersionsView.xaml.cs').includes('Dispatcher.Invoke'))
  fail('版本选择日志用 Invoke 会堵住看门狗心跳')
if (read('Views/DownloadView.xaml.cs').includes('Dispatcher.Invoke'))
  fail('下载页日志用 Invoke 会堵住看门狗心跳')
if (!read('Core/Launcher.cs').includes('RelayProgress'))
  fail('任务日志写盘还走 Progress 进界面线程')
if (/ApplyUiBoot\(\)[\s\S]{0,400}SaveSettings/.test(read('Core/AppState.cs')))
  fail('启动就写 pad.json，第二份 PAD 会锁文件')
if (!appCs.includes('UiWatchdog')) fail('启动窗没有界面卡住看门狗')
if (!appCs.includes('UnobservedTaskException')) fail('后台任务出错不报')
if (!/void Swap\([\s\S]*ThrowIfSelfHosted[\s\S]*catch/.test(mainCs))
  fail('Swap 抛错不弹窗')
if (!read('Views/TaskView.xaml.cs').includes('ThrowIfSelfHosted'))
  fail('任务页 DataContext=this 之后不检查自宿主')
if (!readFileSync(join(import.meta.dirname, 'registries/pa-codes.json'), 'utf8').includes('PA040'))
  fail('pa-codes 缺 PA040 界面卡住')
if (read('Views/LaunchView.xaml').includes('ItemsSource="{Binding Instances}"'))
  fail('启动页还钉着实例列表，实例名单在版本选择')
if (read('Views/LaunchView.xaml').includes('Style="{StaticResource SlabHead}" Content="这份实例"'))
  fail('启动页还钉着这份实例卡')
if (read('Views/LaunchView.xaml').includes('x:Name="LogBox"'))
  fail('启动页还钉着日志，过程在启动悬浮窗')
if (!existsSync(join(pad, '../modpack/registries/pad-pages.json')) &&
    !existsSync(join(import.meta.dirname, 'registries/pad-pages.json')))
  fail('缺 pad-pages 注册表')
if (!read('Core/PadRegistry.cs').includes('pad-pages')) fail('启动器页不是注册表条目')
if (!read('Core/PadRegistry.cs').includes('disabled')) fail('注册表不能 disabled')
if (!read('Views/SettingsView.xaml.cs').includes('DshBin('))
  fail('设置启动页还在拿嗅探 npx 当版本库入口')
if (!read('Pad.csproj').includes('<ApplicationIcon>'))
  fail('exe 没有 Windows 图标')
if (!existsSync(join(pad, 'Assets/pad.ico')))
  fail('缺 Assets/pad.ico')
if (!read('MainWindow.xaml').includes('pad.ico'))
  fail('窗没有图标')
if (!read('Theme/Controls.xaml').includes('x:Key="Pict"'))
  fail('没有卡片小标识（圆角色块里的小图案）')
if (!read('Theme/Controls.xaml').includes('x:Key="SlabHead"'))
  fail('卡片标题没有小标识槽')
if (!read('Theme/Icons.xaml').includes('I.Folder') || !read('Theme/Icons.xaml').includes('I.Plus'))
  fail('图标集缺文件夹 / 新建')
if (!read('Views/SettingsView.xaml').includes('SlabHead'))
  fail('设置卡片标题没有小标识')
if (!read('Views/LaunchView.xaml').includes('PictLg'))
  fail('启动空态没有大号小图案')
if (!read('Views/SettingsView.xaml').includes('Style="{StaticResource SlabHead}" Content="DSH"'))
  fail('设置 DSH 卡片标题没有小标识')
if (!read('Views/DownloadView.xaml').includes('SlabHead'))
  fail('下载卡片标题没有小标识')
if (!/SettingsView\(\)\s*\{[\s\S]{0,120}_loading = true[\s\S]{0,80}InitializeComponent/.test(read('Views/SettingsView.xaml.cs')))
  fail('设置页滑条在控件建完前会空引用')
if (!read('Views/SettingsView.xaml.cs').includes('WallOpVal is null'))
  fail('滑条标签空守卫没写')
if (!/OnDispatcherError[\s\S]*ShotPath[\s\S]*Shutdown/.test(read('App.xaml.cs')))
  fail('--shot 出错还弹窗卡住')
if (/if \(App.ShotPath is null\) return;/.test(read('MainWindow.xaml.cs')))
  fail('--shot-page 没有 --shot 时 Loaded 直接 return，做完打不开那一页')
if (!read('Views/TaskView.xaml').includes('I.Tasks') && !read('Views/TaskView.xaml.cs').includes('ListJobs'))
  fail('任务队列控件丢了，下载页还要用')
if (!read('Views/ManageView.xaml').includes('I.Folder') ||
    !read('Views/ManageView.xaml').includes('I.Stop'))
  fail('管理页 session 打开/取消没有小标识')
{
  const controls = read('Theme/Controls.xaml')
  const rule = controls.slice(controls.indexOf('x:Key="Rule"'), controls.indexOf('x:Key="Quant"'))
  if (!rule.includes('Ico.Of')) fail('分组横线标题没有小标识槽')
}
if (!read('Theme/Inputs.xaml').includes('Ico.Of') && !read('Theme/Inputs.xaml').includes('v:Ico'))
  fail('实例分段页没有小标识槽')
if (!read('Views/InstanceView.xaml').includes('NavOverview') ||
    !read('Views/InstanceView.xaml').includes('I.Window'))
  fail('实例分段页概览没有小标识')
if (!read('Views/ManageView.xaml').includes('I.Stop') || !read('Views/ManageView.xaml').includes('I.Log'))
  fail('实例管理页停止/日志没有小标识')
if (!read('Theme/Icons.xaml').includes('I.Trash'))
  fail('图标集缺删除')
if (!read('Theme/Icons.xaml').includes('I.Log'))
  fail('图标集缺日志')
if (!read('Views/InstanceView.xaml').includes('Style="{StaticResource SlabHead}" Content="整合包"'))
  fail('整合包页标题没有小标识')

if (!read('Views/Fx.cs').includes('fe.Opacity = 0'))
  fail('列表入场延迟期间会先闪全不透明')
if (!read('Views/Fx.cs').includes('SetSpeed'))
  fail('动效没有速度倍率')
if (!read('Views/SettingsView.xaml').includes('动画速度'))
  fail('个性化没有动画速度')
if (!read('Views/SettingsView.xaml').includes('顶栏文字'))
  fail('个性化不能改顶栏文字')
if (!read('Views/SettingsView.xaml').includes('刷新壁纸'))
  fail('个性化缺刷新壁纸，对照 PCL 刷新背景图片')
if (!read('Views/SettingsView.xaml').includes('PadSlider') &&
    !read('Theme/Inputs.xaml').includes('PadSlider'))
  fail('透明度还是纯手填，没有滑条')
if (!read('Views/LaunchView.xaml').includes('EmptyWell'))
  fail('启动页空态还是一块空白')
if (!/Run_Click[\s\S]{0,400}GoToStep/.test(read('Views/LaunchView.xaml.cs')))
  fail('没有可启动的版本时大按钮不对照 PCL 去下一页')
if (!read('Core/AppState.cs').includes('NeedsSetup => Instances.Count == 0'))
  fail('有实例没 --profile 还显示还没有实例')
function instIconKind(pack: boolean, tui: boolean, web: boolean): 'pack' | 'tui' | 'web' | 'ds' {
  if (pack) return 'pack'
  if (tui) return 'tui'
  if (web) return 'web'
  return 'ds'
}
if (instIconKind(true, true, true) !== 'pack') fail('有整合包图标时不该落到字母块')
if (instIconKind(false, true, true) !== 'tui') fail('无包图标时 tui 优先于 web')
if (instIconKind(false, false, true) !== 'web') fail('无 tui 应用网页图标')
if (instIconKind(false, false, false) !== 'ds') fail('都没有时用 ds 图标')
if (!read('Core/InstIcon.cs').includes('PackImage') || !read('Core/InstIcon.cs').includes('Kind'))
  fail('实例图标没有整合包 → tui → web → ds')
if (read('Views/LaunchView.xaml').includes('Converter={StaticResource Initial}'))
  fail('启动页还在用名字字母当图标')
if (!read('Views/LaunchView.xaml').includes('InstMark'))
  fail('启动页选中实例没有 InstMark')
if (!read('Views/VersionsView.xaml').includes('InstMark'))
  fail('版本选择实例列表还在用字母')
if (!read('Views/InstanceView.xaml').includes('InstMark'))
  fail('实例页头还在用字母')
if (read('Views/LaunchView.xaml').includes('x:Name="InstanceSheet"'))
  fail('启动页还留着这份实例卡')
if (!read('MainWindow.xaml.cs').includes('BrandText.Text'))
  fail('顶栏文字不吃设置')
if (!read('Core/Models.cs').includes('brandText') || !read('Core/Models.cs').includes('animSpeed'))
  fail('pad.json 没有 brandText / animSpeed')
if (!/var same = ReferenceEquals[\s\S]*PageIn/.test(read('MainWindow.xaml.cs')) &&
    !/same[\s\S]*PageIn/.test(read('MainWindow.xaml.cs')))
  fail('切到同一页还重播入场')

const pages = JSON.parse(readFileSync(join(import.meta.dirname, 'registries/pad-pages.json'), 'utf8'))
if (pages.registry !== 'pad-pages') fail('pad-pages 注册表名不对')
if (!Array.isArray(pages.entries) || !pages.entries.some((e: { id: string }) => e.id === 'launch'))
  fail('pad-pages 没有 launch 条目')
if (read('Views/InstanceView.xaml').includes('个 profile')) fail('界面把 --profile 写成了英语口语')
if (read('Views/SettingsView.xaml').includes('默认 profile')) fail('设置把 --profile 写成了英语口语')
if (read('Views/VersionsView.xaml').includes('新建 profile')) fail('版本选择把 --profile 写成了英语口语')
if (read('Views/LaunchView.xaml').includes('Content="home"')) fail('启动页按钮写成英语 home')
if (!read('Views/TaskView.xaml.cs').includes('ReadJobLog') &&
    !read('Views/TaskView.xaml.cs').includes('JobLogPath'))
  fail('任务页不读 job 日志尾')
if (!launcher.includes('AppendJobLog') || !launcher.includes('JobLogPath'))
  fail('Launcher 不把进度写进 job 日志')

if (!launchCs.includes('LaunchFailed') && !read('Views/ManageView.xaml.cs').includes('LaunchFailed'))
  fail('启动失败不能崩溃分析')
if (!read('Views/ManageView.xaml').includes('工作区'))
  fail('实例管理页不能打开工作区')

if (!read('Views/DownloadView.xaml').includes('校验')) fail('下载页不能校验发行号')
if (!read('Views/DownloadView.xaml.cs').includes('Verify_Click')) fail('下载页校验没接到')
if (!read('Views/DownloadView.xaml.cs').includes('OpenVersion_Click')) fail('下载页不能打开 versions 目录')

if (!read('Core/Runner.cs').includes('CredentialsSet')) fail('启动脚本不吃实例凭据覆盖')
if (!read('Core/Runner.cs').includes('PreCommand')) fail('启动脚本不吃 preCommand')
if (!read('Views/InstanceView.xaml').includes('工作区隔离')) fail('实例设置没有工作区隔离')
if (!read('Views/InstanceView.xaml.cs').includes('跟随全局：')) fail('实例设置不显示当前全局值')
if (!read('Views/SettingsView.xaml').includes('新建实例工作区')) fail('全局设置没有新建实例默认隔离')
if (read('Views/InstanceView.xaml').includes('NoteBox') ||
    read('Views/InstanceView.xaml.cs').includes('NoteBox'))
  fail('概览还在摊 note 输入框，对照 PCL 用按钮弹窗改描述')
if (read('Views/InstanceView.xaml.cs').includes('LaunchScripts('))
  fail('概览还在列 launch-*.cmd，对照 PCL 快捷方式只开文件夹')

// ---- 能玩路径：对照 PCL 打开就能走完，禁止点了没反应 / 空页没出口 ----
const downloadCs = read('Views/DownloadView.xaml.cs')
if (/AutoFetchOnOpen && !string.IsNullOrEmpty\(_shelf\)/.test(downloadCs))
  fail('打开下载页时自动获取版本列表只对社区资源生效，发行号页永远不拉')
if (!downloadCs.includes('FetchVersions'))
  fail('获取版本列表没有可复用方法，OnShown 不能自动拉发行号')
if (!/version\.Length == 0[\s\S]{0,200}(MessageBox|Toast)/.test(downloadCs))
  fail('版本框空点安装直接 return，没有提示')
if (downloadCs.includes('ShowTab("tasks")'))
  fail('点安装还切到任务顶栏，进度留在下载页')
const downloadXaml = read('Views/DownloadView.xaml')
if (!downloadXaml.includes('x:Name="InstallMeter"'))
  fail('下载页安装没有进度条')
if (!downloadXaml.includes('x:Name="RemoteList"'))
  fail('下载页没有 npm 发行号列表，对照 PCL 选版本')
if (!read('Core/AppState.cs').includes('HasRemoteReleases') && !read('Core/AppState.cs').includes('ShowReleaseEmpty'))
  fail('AppState 没有远程发行号，空卡只能看本地版本库')
if (!existsSync(join(pad, 'Core/DshPackument.cs'))) fail('缺 Core/DshPackument.cs')
if (!read('Core/DshPackument.cs').includes('0.0.1-rc.1'))
  fail('DshPackument 不丢掉 0.0.1-rc.1')
const addFn = downloadCs.split('InstallFrom')[1]?.split('\n    void ')[0] ?? ''
if (addFn.includes('先到版本选择') || addFn.includes('再回来装') || addFn.includes('再选上面'))
  fail('没实例时装组合包还在撵人，该切页')
if (!addFn.includes('ShowInner("versions"'))
  fail('没实例时装组合包不切到版本选择')
if (!addFn.includes('ShowInner("instance"'))
  fail('没 profile 时装组合包不切到版本设置')
{
  const searchCard = downloadXaml.split('x:Name="ShelfTitle"')[1]?.split('搜索')[0] ?? downloadXaml
  const aroundName = downloadXaml.split('Text="名称"')[1]?.slice(0, 2500) ?? ''
  if (!aroundName.includes('Text="版本"') || !aroundName.includes('x:Name="TargetRelease"'))
    fail('搜索卡没有「版本」发行号，对照 PCL 下载 Mod 的 1.20.1')
  if (!aroundName.includes('Text="装到"') || !aroundName.includes('x:Name="TargetInstance"'))
    fail('搜索卡「装到」不是实例名单')
  if (!aroundName.includes('ItemsSource="{Binding Instances}"'))
    fail('「装到」没绑 Instances')
  if (aroundName.includes('TargetLaunch') || aroundName.includes('TargetText'))
    fail('「装到」还在拼 实例 · profile，对照 PCL 应对上版本号和实例')
  if (downloadXaml.includes('Text="--profile"'))
    fail('搜索卡把 --profile 旗标画给人看')
  if (!downloadCs.includes('TargetInstance_Changed'))
    fail('换装到的实例不刷新这份实例的发行号')
  if (!addFn.includes('TargetInstance'))
    fail('点安装没用「装到」所选实例')
  if (downloadXaml.includes('当前选择的实例'))
    fail('装到旁还用灰字暗示实例')
}
{
  const sideStart = downloadXaml.indexOf('Background="{StaticResource BgSidebar}"')
  const sideEnd = downloadXaml.indexOf('<!-- 发行号 -->')
  const leftNav = sideStart >= 0 && sideEnd > sideStart
    ? downloadXaml.slice(sideStart, sideEnd)
    : downloadXaml.split('Grid.Column="0"')[1]?.split('Grid.Column="1"')[0] ?? ''
  if (leftNav.includes('正在下') || leftNav.includes('已下好') || leftNav.includes('DownloadDock'))
    fail('下载左栏还把正在下塞在分类下面，对照 PCL 左栏只有分类')
  if (!leftNav.includes('下载任务'))
    fail('宿主与官方下面没有下载任务，对照应用商店下载管理入口')
  if (leftNav.includes('正在下载的') || leftNav.includes('下载完成的'))
    fail('正在下载的名单塞进左栏了，该在右边')
  if (!downloadXaml.includes('x:Name="NavDlTasks"') || !downloadXaml.includes('x:Name="PaneDlTasks"'))
    fail('点下载任务没有右边那一页')
  if (!downloadXaml.includes('正在下载的') || !downloadXaml.includes('下载完成的'))
    fail('下载任务右边没有正在下载的 / 下载完成的')
  if (!downloadXaml.includes('x:Name="DlRunning"') || !downloadXaml.includes('x:Name="DlDone"'))
    fail('下载任务没有两份名单')
  if (!downloadCs.includes('DownloadDock.Running') || !downloadCs.includes('DownloadDock.Done'))
    fail('下载任务没筛下载 job')
  if (!downloadCs.includes('ListJobs'))
    fail('下载任务不读 job 队列')
  if (downloadCs.includes('ChromeList') || downloadCs.includes('RunningAll'))
    fail('下载任务用了全量 job，会混进克隆实例')
  if (!downloadCs.includes('StartDlPoll'))
    fail('下载任务停在该页不轮询 jobs.json')
  if (!downloadXaml.includes('在文件夹中显示'))
    fail('下载完成的不能在文件夹中显示')
  if (!downloadCs.includes('OpenShelf') || !downloadCs.includes('dltasks'))
    fail('打不开下载任务这一栏')
  if (!read('MainWindow.xaml.cs').includes('"dl-tasks"'))
    fail('--shot-page dl-tasks 打不开下载任务')
  if (!downloadXaml.includes('x:Name="PaneDetail"'))
    fail('下载页没有插件详情页，对照 PCL 点进 Mod')
  if (!downloadXaml.includes('x:Name="DetailHead"'))
    fail('详情没有返回标题栏，对照 PCL ← 钠')
  if (!downloadXaml.includes('x:Name="DetailChips"'))
    fail('详情没有版本条，对照 PCL 全部 / 1.20')
  if (!downloadXaml.includes('x:Name="DetailFileList"'))
    fail('详情没有可下版本名单，对照 PCL Fabric 1.20.1 那一列')
  if (downloadXaml.includes('x:Name="DetailVersions"'))
    fail('详情还在用下拉选版本，对照 PCL 是芯片+名单')
  if (!downloadCs.includes('OpenDetail'))
    fail('点进行列表不能进详情')
  if (!downloadXaml.includes('Row_OpenDetail'))
    fail('插件行点不进详情')
  if (!read('Core/Market.cs').includes('ParsePackument') || !read('Core/Market.cs').includes('FetchPackument'))
    fail('详情不读 npm packument 版本列表')
  if (!read('Core/Market.cs').includes('ChipOf'))
    fail('详情版本条不会按 1.20 这种号分组')
  if (!downloadCs.includes('开始装'))
    fail('点下载没有立刻反馈')
  if (!downloadCs.includes('装进') && !downloadCs.includes('没装成'))
    fail('下载好之后没有反馈')
}
if (!/DefaultShelves\(\) =>\s*\[[\s\S]*?@deepseek-harness-tui/.test(models))
  fail('终端货架搜不到 @deepseek-harness-tui/dsh-tui')
if (!/AutoFetchOnOpen\s*\{\s*get;\s*set;\s*\}\s*=\s*true/.test(models))
  fail('打开下载页自动获取默认关着，勾了才有效的开关用户找不到')
const createFn = read('Views/VersionsView.xaml.cs').split('Create_Click')[1]?.split('\n    void ')[0] ?? ''
if (!createFn.includes('ShowInner("instance"'))
  fail('新建实例不进版本设置')
const addProfileFn = read('Views/VersionsView.xaml.cs').split('AddProfile_Click')[1]?.split('\n    void ')[0] ?? ''
if (!addProfileFn.includes('Show("launch")'))
  fail('创建并安装 profile 成功不回启动页')

const mainWindowCs = read('MainWindow.xaml.cs')
if (!mainWindowCs.includes('OpenFolderDialog'))
  fail('添加已有文件夹不弹选夹')
if (!mainWindowCs.includes('AdoptFolder'))
  fail('选夹收编没接到 AdoptHome')
if (read('Views/LaunchView.xaml.cs').includes('AdoptNow()'))
  fail('启动页添加已有文件夹还在收编默认 home，不选夹')
if (read('Views/LaunchView.xaml').includes('CanOfferAdopt'))
  fail('添加已有文件夹只在能嗅探到默认 home 时出现')
if (read('Views/VersionsView.xaml.cs').includes('AdoptNow()'))
  fail('版本选择添加已有文件夹还在收编默认 home，不选夹')
if (read('Views/SettingsView.xaml.cs').includes('AdoptNow()'))
  fail('设置添加已有文件夹还在收编默认 home，不选夹')

if (!read('Views/VersionsView.xaml').includes('下载 DSH'))
  fail('版本选择空卡没有下载 DSH，对照 PCL 无可用版本 → 下载游戏')
if (!read('Views/TaskView.xaml').includes('去下载') && !read('Views/TaskView.xaml').includes('下载 DSH'))
  fail('任务空态没有下一步')
if (!read('Views/TaskView.xaml').includes('搜索下载内容') && !read('Views/TaskView.xaml').includes('x:Name="SearchBox"'))
  fail('对照 Chrome/Edge 下载页没有搜索')
if (!read('Views/TaskView.xaml').includes('x:Name="JobList"'))
  fail('对照 Chrome/Edge 该是一条下载名单')
if (read('Views/TaskView.xaml').includes('正在下') || read('Views/TaskView.xaml').includes('已下好'))
  fail('对照 Chrome/Edge 下载页不是正在下/已下好两块卡')
if (read('Views/TaskView.xaml').includes('Width="244"'))
  fail('任务管理还把名单塞进左栏边框')
if (read('Views/TaskView.xaml').includes('LogBox'))
  fail('对照 Chrome/Edge 名单不摊 CLI 日志')
if (!read('Views/TaskView.xaml').includes('Style="{StaticResource Meter}"'))
  fail('对照 Chrome 进行中的没有进度条')
if (!read('Views/TaskView.xaml').includes('在文件夹中显示'))
  fail('对照 Chrome 没有在文件夹中显示')
if (!read('Views/TaskView.xaml').includes('从列表中删除') && !read('Core/Launcher.cs').includes('ForgetJob'))
  fail('对照 Chrome 不能从列表中删除')
if (!read('Views/TaskView.xaml').includes('没有下载内容'))
  fail('对照 Chrome 空态不叫没有下载内容')
if (read('Core/DownloadDock.cs').includes('ShowLog => Selected'))
  fail('点进任务页就把日志摊开，下载管理不是黑窗')
if (!read('Views/TaskView.xaml.cs').includes('DispatcherTimer'))
  fail('任务页停在该页不轮询 jobs.json')
if (!read('Views/ManageView.xaml').includes('去启动'))
  fail('管理空态没有去启动')
if (!read('Views/InstanceView.xaml.cs').includes('TrialEmpty.Text'))
  fail('试验空行可见但从未写字')

if (!read('Views/InstanceView.xaml').includes('x:Name="PluginProfile"'))
  fail('正式 add / 更新 / 移除没有选目标 --profile')
if (!/PluginOp\([\s\S]{0,200}profile/.test(read('Views/InstanceView.xaml.cs')))
  fail('正式操作没把所选 profile 传给 PluginOp')

{
  const nav = instanceXaml.slice(
    instanceXaml.indexOf('Style="{StaticResource NavItem}"'),
    instanceXaml.indexOf('x:Name="PaneOverview"'),
  )
  if (/Content="组合包"/.test(nav))
    fail('版本设置左栏还把插件页写成组合包')
  if (!/Content="插件"/.test(nav))
    fail('版本设置左栏没有插件页，对照 PCL Mod 管理')
  const pluginStart = instanceXaml.indexOf('x:Name="PanePlugins"') >= 0
    ? instanceXaml.indexOf('x:Name="PanePlugins"')
    : instanceXaml.indexOf('x:Name="PaneBundles"')
  const packComment = instanceXaml.indexOf('<!-- 整合包')
  const packPane = instanceXaml.indexOf('x:Name="PanePacks"')
  const pluginEnd = packComment >= 0 && packComment < packPane ? packComment : packPane
  const plugins = instanceXaml.slice(pluginStart, pluginEnd)
  if (!plugins.includes('x:Name="PluginSearch"'))
    fail('版本设置插件页没有搜索框，对照 PCL Mod 管理')
  if (!plugins.includes('搜索插件名称'))
    fail('搜索框没有对照 PCL「搜索 Mod 名称 / 描述 / 标签」')
  if (!plugins.includes('打开文件夹')) fail('插件页没有打开文件夹，对照 PCL')
  if (!plugins.includes('从文件安装')) fail('插件页没有从文件安装，对照 PCL')
  if (!plugins.includes('下载新插件')) fail('插件页没有下载新插件，对照 PCL 下载新 Mod')
  if (!plugins.includes('全选')) fail('插件页没有全选，对照 PCL')
  if (!plugins.includes('可更新')) fail('插件页没有可更新过滤，对照 PCL')
  if (!plugins.includes('x:Name="PluginList"')) fail('插件页没有统一名单，对照 PCL 列表')
  if (plugins.includes('ItemsSource="{Binding Launchables}"')
    && plugins.includes('ItemsSource="{Binding Profile.Bundles}"'))
    fail('插件页还在按 profile 分组只印包名，对照 PCL 一行一条')
  if (plugins.includes('组合包'))
    fail('版本设置插件页还在写组合包，对照 PCL Mod 管理不塞加载器')
  if (plugins.includes('整合包'))
    fail('版本设置插件页还在塞整合包')
}
if (!existsSync(join(pad, 'Core/PluginList.cs')))
  fail('插件搜索/过滤没写成 PluginList')
if (!read('Core/PluginList.cs').includes('Matches') || !read('Core/PluginList.cs').includes('Apply'))
  fail('PluginList 没有 Matches/Apply')
{
  const tagsFn = read('Core/PluginList.cs')
  const start = tagsFn.indexOf('public IReadOnlyList<string> Tags')
  const end = tagsFn.indexOf('public static class PluginList')
  const body = start >= 0 && end > start ? tagsFn.slice(start, end) : ''
  if (!body) fail('PluginRow 没有 Tags')
  if (body.includes('tags.Add(Kind)') || body.includes('Kind) tags.Add'))
    fail('插件行标签还在打组合包/TUI 种类')
  if (body.includes('组合包') || body.includes('整合包'))
    fail('插件行标签还写组合包或整合包')
}
if (!read('Core/LaunchPolicy.cs').includes('IsProfileLayer'))
  fail('没有把随附层/TUI/管理口从插件页拆走')
if (!read('Views/InstanceView.xaml.cs').includes('IsProfileLayer'))
  fail('插件页还在把组合包全表塞进名单')
if (!read('Views/ManageView.xaml.cs').includes('IsProfileLayer'))
  fail('管理页组合包名单还和插件页共用全表')
if (read('Views/InstanceView.xaml.cs').includes('种类：{row.Kind}'))
  fail('插件详情还在报组合包种类')
if (!read('Core/Launcher.cs').includes('PluginList.ReadMeta')
  && !read('Core/Launcher.cs').includes('ReadPackageMeta'))
  fail('ListBundles 没读 package.json description / keywords')
if (!read('Views/InstanceView.xaml.cs').includes('OpenDownloadShelf')
  && !read('MainWindow.xaml.cs').includes('OpenDownloadShelf'))
  fail('下载新插件不能切到下载页插件货架')
if (!read('MainWindow.xaml.cs').includes('instance-plugins'))
  fail('--shot-page instance-plugins 打不开插件页')
if (!read('Views/InstanceView.xaml.cs').includes('instance-plugins'))
  fail('插件页 --shot-page 没有切到 NavPlugins')

function isProfileLayer(spec: string): boolean {
  const s = spec.toLowerCase()
  return s.includes('dsh-base') || s.includes('dsh-web-app') || s.includes('dsh-headless')
    || s.includes('dsh-tui') || s.includes('pad-gateway') || s.includes('host-apiproxy')
}
if (!isProfileLayer('@deepseek-ai/dsh-base')) fail('dsh-base 是随附层，不进插件页')
if (!isProfileLayer('@deepseek-ai/dsh-web-app')) fail('dsh-web-app 是随附层，不进插件页')
if (!isProfileLayer('@deepseek-harness-tui/dsh-tui')) fail('TUI 是组合包，进管理页')
if (!isProfileLayer('@sakikotgw/pad-gateway')) fail('管理口是组合包，进管理页')
if (isProfileLayer('@liustack/modlens')) fail('第三方插件被当成随附层')
if (isProfileLayer('dsh-cost-meter')) fail('另装的插件被当成随附层')

function pluginPageTags(keywords: string[], trial: boolean, updatable: boolean): string[] {
  const tags: string[] = []
  if (trial) tags.push('试验')
  if (updatable) tags.push('可更新')
  for (const k of keywords)
    if (!tags.some((t) => t.toLowerCase() === k.toLowerCase())) tags.push(k)
  return tags
}
if (pluginPageTags(['skill'], false, true).includes('组合包'))
  fail('插件标签打出了组合包')
if (pluginPageTags(['仓储', '实用'], false, false).join() !== '仓储,实用')
  fail('插件标签应对照 PCL 分类词，不打种类')

type PluginRowFix = {
  spec: string
  description: string
  keywords: string[]
  version: string
  latest: string
  trial: boolean
}
function pluginMatches(row: PluginRowFix, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (!q) return true
  const hay = [row.spec, row.description, row.version, row.latest, ...row.keywords]
    .join('\n').toLowerCase()
  return hay.includes(q)
}
function pluginApply(rows: PluginRowFix[], query: string, filter: 'all' | 'updatable' | 'trial') {
  return rows.filter((r) => !isProfileLayer(r.spec) && pluginMatches(r, query) && (
    filter === 'updatable' ? r.latest.length > 0 && r.latest !== r.version
      : filter === 'trial' ? r.trial : true
  ))
}
const pluginSample: PluginRowFix[] = [
  { spec: '@deepseek-harness-tui/dsh-tui', description: '终端', keywords: ['tui'], version: '0.9.3', latest: '0.9.4', trial: false },
  { spec: 'dsh-cost-meter', description: '费用', keywords: ['cost'], version: '1.1.0', latest: '1.2.0', trial: false },
  { spec: '@michengai/dsh-skills-manager', description: 'skill', keywords: ['skill'], version: '0.1.0', latest: '', trial: true },
]
if (pluginApply(pluginSample, '', 'all').some((r) => r.spec.includes('dsh-tui')))
  fail('插件页还列出 TUI 组合包')
if (pluginApply(pluginSample, '费用', 'all').map((r) => r.spec).join() !== 'dsh-cost-meter')
  fail('搜索描述应对上 description')
if (pluginApply(pluginSample, 'cost', 'all').map((r) => r.spec).join() !== 'dsh-cost-meter')
  fail('搜索插件名称 / 标签应对上 keywords')
if (pluginApply(pluginSample, '', 'updatable').map((r) => r.spec).join() !== 'dsh-cost-meter')
  fail('可更新只留下 latest ≠ version 的插件')
if (pluginApply(pluginSample, '', 'trial').map((r) => r.spec).join() !== '@michengai/dsh-skills-manager')
  fail('试验中只留下 trial')
if (pluginApply(pluginSample, '没有这个', 'all').length !== 0)
  fail('搜不到的应空')
if (!launcher.includes('string? profile') && !/PluginOp\([\s\S]*string\? profile/.test(launcher))
  fail('PluginOp 不能指定 --profile，永远打到 LastProfile')
if (!read('Views/InstanceView.xaml.cs').includes('ListCredentials'))
  fail('实例凭据下拉选不到设置里新建的具名凭据')
if (!mainWindowCs.includes('ShowTab'))
  fail('空态按钮不能切顶栏')

console.log('✓ PAD 窗口接到 import / 收编 / 崩溃 / 分开升 / 拖 zip')
console.log('✓ PAD 入口契约：clone/export/v4/任务/凭据/job 日志')
console.log('✓ pad-window')

// ---- 有编译产物时实跑 pad cli crash：不经过 packagent ----
const exe = join(pad, 'bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
if (process.platform === 'win32' && existsSync(exe)) {
  const root = packTmpDir('pad-crash')
  try {
    const inst = 'crash-demo'
    mkdirSync(join(root, 'instances', inst, 'logs'), { recursive: true })
    writeFileSync(join(root, 'instances', inst, 'instance.json'), JSON.stringify({
      schema: 'pack-agent.launcher.instance/v2',
      id: inst,
      name: inst,
      dsh: { version: '0.0.0' },
      home: join(root, 'instances', inst, 'home'),
      workspace: { kind: 'owned', path: join(root, 'instances', inst, 'workspace') },
    }))
    writeFileSync(
      join(root, 'instances', inst, 'logs', 'web.log'),
      'Error: listen EADDRINUSE: address already in use :::3080\n',
    )
    const run = spawnSync(exe, ['cli', 'crash', inst], {
      env: { ...process.env, PACK_LAUNCHER_ROOT: root },
      encoding: 'utf8',
      timeout: 30_000,
    })
    const logFile = join(root, 'pad-cli.log')
    const text = [run.stdout, run.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
    if (run.status !== 0) fail(`pad cli crash 退出 ${run.status}\n${text}`)
    if (!/类别 port/.test(text) || !/eaddrinuse/.test(text) || !/web 端口被占/.test(text))
      fail(`pad cli crash 没对上规则\n${text}`)
    console.log('✓ pad cli crash 不经过 pack-agent')
  } finally {
    try { rmSync(root, { recursive: true, force: true }) } catch { /* keep tmp on lock */ }
  }

  const sniffRoot = packTmpDir('pad-sniff')
  try {
    const run = spawnSync(exe, ['cli', 'sniff'], {
      env: { ...process.env, PACK_LAUNCHER_ROOT: sniffRoot },
      encoding: 'utf8',
      timeout: 30_000,
    })
    const logFile = join(sniffRoot, 'pad-cli.log')
    const text = [run.stdout, run.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
    if (run.status !== 0) fail(`pad cli sniff 退出 ${run.status}\n${text}`)
    const npx = join(process.env.LOCALAPPDATA ?? '', 'npm-cache', '_npx')
    let npxBin: string | null = null
    if (existsSync(npx)) {
      for (const hash of readdirSync(npx)) {
        const bin = join(npx, hash, 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'bin.js')
        if (existsSync(bin)) { npxBin = bin; break }
      }
    }
    if (npxBin && !text.includes('@deepseek-ai') && !text.toLowerCase().includes('bin.js'))
      fail(`本机有 npx 的 dsh，sniff 没报出来\n${text}`)
    if (npxBin) console.log('✓ pad cli sniff 找到本机 dsh')
    else console.log('✓ pad cli sniff 能跑')
  } finally {
    try { rmSync(sniffRoot, { recursive: true, force: true }) } catch { /* keep tmp on lock */ }
  }

  const pinRoot = packTmpDir('pad-pin')
  try {
    const pkg = join(pinRoot, 'src', 'node_modules', '@deepseek-ai', 'dsh')
    mkdirSync(join(pkg, 'lib'), { recursive: true })
    writeFileSync(join(pkg, 'package.json'), JSON.stringify({
      name: '@deepseek-ai/dsh',
      version: '0.0.0-pin',
    }))
    writeFileSync(join(pkg, 'lib', 'bin.js'), "console.log('0.0.0-pin')\n")
    const bin = join(pkg, 'lib', 'bin.js')
    const run = spawnSync(exe, ['cli', 'release', 'pin', bin], {
      env: { ...process.env, PACK_LAUNCHER_ROOT: pinRoot },
      encoding: 'utf8',
      timeout: 30_000,
    })
    const logFile = join(pinRoot, 'pad-cli.log')
    const text = [run.stdout, run.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
    if (run.status !== 0) fail(`pad cli release pin 退出 ${run.status}\n${text}`)
    const stored = join(pinRoot, 'versions', '0.0.0-pin', 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'bin.js')
    if (!existsSync(stored)) fail(`pin 没拷进版本库\n${text}`)
    const rec = JSON.parse(readFileSync(join(pinRoot, 'versions', '0.0.0-pin', 'version.json'), 'utf8'))
    if (rec.bin && String(rec.bin).includes('src')) fail(`pin 还指着源路径 ${rec.bin}`)
    console.log('✓ pad cli release pin 拷进版本库')
  } finally {
    try { rmSync(pinRoot, { recursive: true, force: true }) } catch { /* keep tmp on lock */ }
  }

  const plugRoot = packTmpDir('pad-plugin-list')
  try {
    const inst = 'tui-demo'
    const home = join(plugRoot, 'instances', inst, 'home')
    const profile = join(home, 'profiles', 'dsh-tui')
    const tuiPkg = join(profile, 'node_modules', '@deepseek-harness-tui', 'dsh-tui')
    mkdirSync(join(plugRoot, 'instances', inst, 'logs'), { recursive: true })
    mkdirSync(tuiPkg, { recursive: true })
    mkdirSync(join(plugRoot, 'instances', inst, 'workspace'), { recursive: true })
    writeFileSync(join(plugRoot, 'instances', inst, 'instance.json'), JSON.stringify({
      schema: 'pack-agent.launcher.instance/v2',
      id: inst,
      name: inst,
      dsh: { version: '0.0.0' },
      home,
      workspace: { kind: 'owned', path: join(plugRoot, 'instances', inst, 'workspace') },
      lastProfile: 'dsh-tui',
    }))
    writeFileSync(join(profile, 'package.json'), JSON.stringify({
      dsh: { profile: { bundles: ['@deepseek-harness-tui/dsh-tui'] } },
    }))
    writeFileSync(join(tuiPkg, 'package.json'), JSON.stringify({
      name: '@deepseek-harness-tui/dsh-tui',
      version: '0.9.3',
      repository: { type: 'git', url: 'git+https://github.com/ccch1mneyyy/dsh-TUI.git' },
    }))
    mkdirSync(join(tuiPkg, 'docs', 'assets'), { recursive: true })
    writeFileSync(join(tuiPkg, 'docs', 'assets', 'logo.svg'),
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><path fill="#4b6fff" d="M4 16h8V8h8v8h8v8H4z"/></svg>\n')
    const run = spawnSync(exe, ['cli', 'plugin', 'list', inst], {
      env: { ...process.env, PACK_LAUNCHER_ROOT: plugRoot },
      encoding: 'utf8',
      timeout: 30_000,
    })
    const logFile = join(plugRoot, 'pad-cli.log')
    const text = [run.stdout, run.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
    if (run.status !== 0) fail(`pad cli plugin list 退出 ${run.status}\n${text}`)
    if (!text.includes('@deepseek-harness-tui/dsh-tui')) fail(`plugin list 没有 TUI 组合包\n${text}`)
    if (!text.includes('"Kind": "TUI"') && !text.includes('"Kind":"TUI"'))
      fail(`plugin list 没把 TUI 标出来\n${text}`)
    if (!text.includes('0.9.3')) fail(`plugin list 没读到磁盘版本\n${text}`)
    if (!text.includes('logo.svg') && !text.includes('icon.png'))
      fail(`plugin list 没带上 TUI logo\n${text}`)
    console.log('✓ pad cli plugin list 不靠管理口也能列出 TUI')
  } finally {
    try { rmSync(plugRoot, { recursive: true, force: true }) } catch { /* keep tmp on lock */ }
  }
}
