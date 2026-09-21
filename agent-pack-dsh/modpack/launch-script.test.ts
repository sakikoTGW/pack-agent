#!/usr/bin/env bun
/**
 * 启动脚本 / 便携导出 / 悬浮窗等待：契约先于实现。
 * 对照 pad/Core，不启 WPF。
 */
import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import {
  cmdCdOwnedWorkspace,
  cmdNode,
  cmdRelativeToRoot,
  cmdSetDshHome,
  depRangeMajor,
  isFrameworkDependentHost,
  isSecretEnvKey,
  junctionHealthy,
  shouldPromoteNestedDep,
  isolatedImporterNeedsSiblingLink,
  pickPnpmPhys,
  winPathTooLong,  launchHarnessReady,
  launchScriptLeaksKey,
  launchWaitGiveUp,
  pathUnder,
  portableEphemeral,
  shouldSkipDuplicateLaunch,
  stripSecretEnv,
} from './launch-script.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const secret = 'sk-LEAK-ME-NOW-NOT-FOR-DISK'
if (launchScriptLeaksKey(`set "DEEPSEEK_API_KEY=${secret}"`)) {
  /* expected leak */
} else fail('烘焙进 cmd 的 API Key 必须被判定为泄露')
if (launchScriptLeaksKey('set "DEEPSEEK_API_KEY="')) fail('空 set 是运行时占位，不是泄露')
if (launchScriptLeaksKey('set "DEEPSEEK_API_KEY=!PAD_CRED_VAL!"')) fail('延迟展开不是泄露')
if (launchScriptLeaksKey('set "DEEPSEEK_API_KEY=%PAD_CRED_VAL%"')) fail('% 引用不是泄露')
if (!isSecretEnvKey('DEEPSEEK_API_KEY')) fail('DEEPSEEK_API_KEY 是密钥环境变量')
if (isSecretEnvKey('FOO')) fail('普通 extraEnv 不是密钥')
if (stripSecretEnv(`FOO=1\nDEEPSEEK_API_KEY=${secret}\nBAR=2`).includes(secret))
  fail('stripSecretEnv 还留着 API Key')
if (!stripSecretEnv(`FOO=1\nDEEPSEEK_API_KEY=${secret}\nBAR=2`).includes('FOO=1'))
  fail('stripSecretEnv 误删了普通 extraEnv')

const root = String.raw`E:\kit\.pack-launcher`
const home = String.raw`E:\kit\.pack-launcher\instances\tui\home`
const pfNode = String.raw`C:\Program Files\nodejs\node.EXE`
const bundled = String.raw`E:\kit\.pack-launcher\runtime\node\22.19.0\x\node.exe`
if (!pathUnder(home, root)) fail('实例 home 应算在启动器根下')
if (pathUnder(pfNode, root)) fail('Program Files 的 node 不算启动器根下')
if (cmdRelativeToRoot(bundled, root) !== String.raw`%PAD_ROOT%runtime\node\22.19.0\x\node.exe`)
  fail(`bundled node 相对路径不对: ${cmdRelativeToRoot(bundled, root)}`)
if (cmdRelativeToRoot(pfNode, root) !== null) fail('系统 node 不能相对化进脚本')
if (cmdNode(pfNode, root) !== 'node') fail('系统 node 应改成 PATH 上的 node，禁止写进盘符路径')
if (!cmdNode(bundled, root).includes('%PAD_ROOT%runtime\\node\\22.19.0'))
  fail('bundled node 应写 %PAD_ROOT%')
if (cmdSetDshHome(false) !== 'set "DSH_HOME=%PAD_INST%home"') fail('自有实例 DSH_HOME 跟 launch-*.cmd 走')
if (!cmdSetDshHome(true).startsWith('set "DSH_HOME=')) fail('收编实例仍用绝对 DSH_HOME')
if (cmdCdOwnedWorkspace() !== 'cd /d "%PAD_INST%workspace"') fail('自有工作区跟实例目录走')

if (launchHarnessReady(false)) fail('runtime.json 自己写的条目不算 Harness 已起')
if (!launchHarnessReady(true)) fail('认到 Harness pid 才算已起')
if (launchWaitGiveUp(2_500)) fail('2.5 秒就放弃，wt 还没把 tab 交出去')
if (!launchWaitGiveUp(45_000)) fail('宽限到点仍该结束等待')
if (!shouldSkipDuplicateLaunch(true)) fail('Harness 已在跑时不能再开一份')
if (shouldSkipDuplicateLaunch(false)) fail('没在跑才允许启动')

if (!isFrameworkDependentHost(true)) fail('旁边有 Pad.dll 就是开发宿主，不能当玩包 exe')
if (!isFrameworkDependentHost(false, true)) fail('旁边有 pack-agent-for DSH.dll 也是开发宿主')
if (isFrameworkDependentHost(false)) fail('单文件 exe 旁边没有宿主 dll')
for (const n of ['pad-cli.log', 'runtime.json', 'jobs.json']) {
  if (!portableEphemeral().includes(n)) fail(`便携包还带着打包机状态文件 ${n}`)
}
if (junctionHealthy(true, false)) fail('junction 指到这份 .pnpm 外面，换目录会裂')
if (!junctionHealthy(true, true)) fail('指着这份 .pnpm 的 junction 是好的')
if (junctionHealthy(false, true)) fail('目标不存在的 junction 要重建')
if (winPathTooLong(259)) fail('259 字符仍在 MAX_PATH 内')
if (!winPathTooLong(260)) fail('260 含 NUL 超限')
if (!winPathTooLong(263)) fail('同学桌面 resolve-3X6ausgz.js 是 263，index.js 是 252')
if (shouldPromoteNestedDep(true, false)) fail('已经是 junction 的嵌套依赖不要再搬')
if (shouldPromoteNestedDep(false, true)) fail('已经在 .pnpm 店里的包不要再搬')
if (!shouldPromoteNestedDep(false, false)) fail('拓成真目录的嵌套 @dsh-std/connection 必须提进店')
if (isolatedImporterNeedsSiblingLink(true, true)) fail('店里已有兄弟依赖就不用再接')
if (!isolatedImporterNeedsSiblingLink(true, false)) fail('manifest 提进自己的店后必须接上 @dsh-std/core，否则 Node realpath 找不到包')
{
  const stores = ['ansi-styles@4.3.0', 'ansi-styles@6.2.3', 'chalk@4.1.2']
  if (pickPnpmPhys(stores, 'ansi-styles', null) !== null)
    fail('两个 ansi-styles 版本时禁止随便取第一个')
  if (depRangeMajor('^6.2.1') !== '6')
    fail('@alcalzone/ansi-tokenize 的 ansi-styles ^6.2.1 主版本是 6')
  if (depRangeMajor('^4.3.0') !== '4')
    fail('chalk 的 ansi-styles ^4 主版本是 4')
  if (depRangeMajor('^8.0.0 || ^9.0.0') !== '9')
    fail('dsh-tui 的 wrap-ansi ^8 || ^9 必须接到 9，取第一个数字会去找不存在的 8.x')
  if (pickPnpmPhys(stores, 'ansi-styles', '6.2.3') !== 'ansi-styles@6.2.3')
    fail('tokenize 钉 6.2.3 时必须接到 6.2.3 店')
  if (pickPnpmPhys(stores, 'ansi-styles', null, '6') !== 'ansi-styles@6.2.3')
    fail('父包 ^6.2.1 必须接到 6.2.3；4.3.0 的 color.ansi 是对象不是函数')
  if (pickPnpmPhys(stores, 'ansi-styles', '4.3.0') !== 'ansi-styles@4.3.0')
    fail('chalk 要 4.3.0 时必须接到 4.3.0 店')
  if (pickPnpmPhys(stores, 'chalk', null) !== 'chalk@4.1.2')
    fail('只有一个 chalk 店时应直接用')
  {
    const wrap = ['wrap-ansi@7.0.0', 'wrap-ansi@9.0.2']
    if (pickPnpmPhys(wrap, 'wrap-ansi', null, depRangeMajor('^8.0.0 || ^9.0.0')) !== 'wrap-ansi@9.0.2')
      fail('wrap-ansi ^8 || ^9 必须接到 9.0.2 店，不能因双版本 FindPhys 放弃')
  }
}
if (isolatedImporterNeedsSiblingLink(false, false)) fail('还在原嵌套目录里解析时不靠店内兄弟链接')

const pad = join(import.meta.dirname, '../pad')
const read = (rel: string) => {
  const p = join(pad, rel)
  if (!existsSync(p)) fail(`缺 ${rel}`)
  return readFileSync(p, 'utf8')
}

const cred = read('Core/CredentialsFile.cs')
if (cred.includes('CmdExport') && !cred.includes('CmdLoadFromHome'))
  fail('CmdExport 把 API Key 写进 launch-*.cmd，换 CmdLoadFromHome 运行时读 yaml')
if (!cred.includes('CmdLoadFromHome')) fail('缺 CmdLoadFromHome：子进程要从 %DSH_HOME%\\.credentials.yaml 取 key')
if (!cred.includes('.credentials.yaml')) fail('加载器不读 home yaml')

const policy = read('Core/LaunchPolicy.cs')
const runner = read('Core/Runner.cs')
if (runner.includes('CmdExport')) fail('WriteScript 还在调用 CmdExport，密钥会进 zip')
if (!runner.includes('CmdLoadFromHome')) fail('WriteScript 不在运行时加载 API Key，wt 子进程吃不到')
if (!runner.includes('CmdClearProfilesFallback'))
  fail('WriteScript 不把清 profiles\\node_modules 写进 cmd')
if (!runner.includes('CmdCleanNodeEnv') || !policy.includes('NODE_OPTIONS'))
  fail('launch-*.cmd 不清 NODE_OPTIONS，Cursor 留下的 --preserve-symlinks 会让 persona 双注册')
if (!runner.includes('PAD_INST') && !policy.includes('PAD_INST'))
  fail('启动脚本没有 %~dp0 根，解压换目录就裂')
if (!policy.includes('%PAD_INST%home') && !runner.includes('%PAD_INST%home'))
  fail('自有实例 DSH_HOME 还钉绝对路径')

if (!policy.includes('PathUnder')) fail('LaunchPolicy 不能判断路径是否在启动器根下')
if (!policy.includes('CmdRelativeToRoot')) fail('LaunchPolicy 不把版本库/node 收成 %PAD_ROOT%')
if (!policy.includes('LaunchHarnessReady')) fail('等待就绪没写成策略')
if (!policy.includes('LaunchWaitGiveUp')) fail('等待放弃没写成策略')
if (!policy.includes('IsFrameworkDependentHost')) fail('单文件 vs 开发宿主没写成策略')
if (!policy.includes('GetFileNameWithoutExtension'))
  fail('只认 Pad.dll，dotnet build -o 的 pack-agent-for DSH.dll 会被当成单文件打进包')
if (!policy.includes('PortableEphemeral')) fail('便携包要剥哪些短暂文件没写成策略')
if (!policy.includes('StripSecretEnv')) fail('extraEnv 里的 API Key 导出时不剥')
if (!policy.includes('IsSecretEnvKey')) fail('缺密钥环境变量判定')

const launcher = read('Core/Launcher.cs')
const exportFn = launcher.split('ExportPortable')[1]?.split('\n    public ')[0] ?? ''
if (!exportFn.includes('StripPortableEphemeral') && !exportFn.includes('PortableEphemeral'))
  fail('portable-export 不剥 pad-cli.log / runtime.json')
if (!exportFn.includes('RewriteLaunchScripts'))
  fail('portable-export 拷完不重生脚本，zip 里仍是打包机 cmd')
if (!exportFn.includes('ResolvePortableHost') && !launcher.includes('ResolvePortableHost'))
  fail('CopyHostExe 还拷当前进程，F5 会打出 336KB 宿主')
if (!exportFn.includes('StripSecretEnv') && !launcher.includes('StripSecretEnv'))
  fail('portable-export 不剥 instance extraEnv 里的 API Key')

const dshBin = launcher.split('public string DshBin')[1]?.split('\n    public ')[0] ?? ''
if (!dshBin.includes('PathUnder'))
  fail('DshBin 还认打包机 version.json 里的绝对 Bin，同一台机器解压会打到舞台目录')

const heal = read('Core/PnpmHeal.cs')
if (heal.includes('if ((sa & FileAttributes.ReparsePoint) != 0) continue;'))
  fail('Relink 跳过已有 junction，换目录后仍指向打包机 .pnpm')
if (!heal.includes('LinkHealthy') && !heal.includes('JunctionHealthy'))
  fail('Relink 不检查 junction 目标是否还在这份 .pnpm 里')
if (!heal.includes('.modules.yaml') && !heal.includes('virtualStoreDir'))
  fail('Relink 不改 .modules.yaml virtualStoreDir，解压后装组合包失败')
if (!heal.includes('ClearProfilesFallback'))
  fail('不清 $DSH_HOME/profiles/node_modules 安装回退，DSH ensureSymlink 会炸')
if (!heal.includes('PromoteOrphans') || !heal.includes('MoveToStore'))
  fail('Relink 不把 .pnpm 包内嵌套 node_modules 提进店，桌面路径 263 丢 hashed chunk')
if (!heal.includes('WalkPnpmStore'))
  fail('Relink 不走进 .pnpm 店里的包，嵌套 @dsh-std/connection 仍是超长真目录')
{
  const walk = heal.split('static int WalkPnpmStore')[1]?.split('\n    static ')[0] ?? ''
  if (!walk.includes('RelinkChildren(pnpm, isolated)'))
    fail('7z -snl 把 isolated 根上的 auto-bind 收成空目录，不 RelinkChildren 店根 TUI 找不到 ink 依赖')
}
if (!heal.includes('LinkIsolatedSiblings'))
  fail('提进店后不接兄弟依赖，@dsh-std/manifest 找不到 @dsh-std/core，TUI plugin tree 起不来')
if (!heal.includes('preferVer'))
  fail('FindPhys 多个 ansi-styles@ 版本时取第一个，tokenize 会接到 4.3.0')
{
  const depMaj = heal.split('static string? DepRangeMajor')[1]?.split('\n    static ')[0] ?? ''
  if (!depMaj.includes('||'))
    fail('DepRangeMajor 不处理 ^8 || ^9，wrap-ansi 会去找不存在的 8.x')
}
if (!heal.includes('ReadStoreOwnerDepRange'))
  fail('RelinkChildren 在 isolated 根上无父 package.json，会把 tokenize 的 ansi-styles 拧回 4.3.0')
if (!heal.includes('_physCache'))
  fail('WalkPnpmStore 对每店 LinkIsolatedSiblings 会把 FindPhys 扫成 O(n²) 挂死')
{
  const sib = heal.split('static int LinkIsolatedSiblings')[1]?.split('\n    static int PromoteOne')[0] ?? ''
  if (!sib.includes('ReadDepRangeFile') || !sib.includes('importerJson') || !sib.includes('ReadDepNames'))
    fail('店内兄弟链接不看 importer 的 ^6.2.1，tokenize 根上的 ansi-styles 会钉在 4.3.0')
  if (sib.includes('foreach (var pkg in EnumerateRealPackages'))
    fail('只读真目录 importer，7z 后 ink 是 junction，wrap-ansi 接不上')
  if (!sib.includes('StoreOwnerSpec') && !heal.includes('StoreOwnerSpec'))
    fail('同一 isolated 里 chalk 会把 tokenize 的 ansi-styles 拧回 4.3.0')
  if (!heal.includes('IsPnpmStoreIsolated'))
    fail('LinkIsolatedSiblings 对 profile 顶层 node_modules 接线会把 dsh-system-prompt 提升上来，persona 双注册')
  if (sib.includes('foreach (var (spec2, phys2) in members)'))
    fail('LinkIsolatedSiblings 对店内每个成员两两互接会把 Relink 拖成几十分钟')
}
{
  const clear = heal.split('ClearProfilesFallback')[1]?.split('\n    public ')[0] ?? ''
  if (clear.includes('Directory.Delete(dir, recursive: true)') && !clear.includes('ReparsePoint'))
    fail('ClearProfilesFallback 递归删会顺着 junction 进版本库，导出时 sdk 拒绝访问')
}
if (!exportFn.includes('ClearProfilesFallback'))
  fail('portable-export 把 profiles/node_modules 拓成真目录带上')
if (!exportFn.includes('EnginesNode') || !exportFn.includes('runtime') || !exportFn.includes('22.19.0'))
  fail('portable-export 不把发行号 Node 打进 runtime/node，同学 PATH 上的 Node 24 会漂')
if (!read('Core/Runner.cs').includes('ClearProfilesFallback'))
  fail('启动不清安装回退，同学解压后第一次启动仍炸')
if (!runner.includes('CmdClearProfilesFallback') || !policy.includes('CmdClearProfilesFallback'))
  fail('launch-*.cmd 不清 profiles\\node_modules，双击脚本仍会 persona 双注册')

const floatCs = read('Views/LaunchFloat.xaml.cs')
if (floatCs.includes('for (var i = 0; i < 10; i++)'))
  fail('WaitAlive 还是 10×250ms，找不到进程也报已启动')
if (!floatCs.includes('LaunchWaitGiveUp') && !floatCs.includes('LaunchHarnessReady'))
  fail('悬浮窗不等 LaunchPolicy 的 Harness 就绪')
if (!floatCs.includes('HarnessAlive') && !floatCs.includes('FindDshPid'))
  fail('悬浮窗把 runtime.json 自写条目当成 Harness')
if (!floatCs.includes('ShouldSkipDuplicateLaunch') && !floatCs.includes('_live'))
  fail('连点启动没有互斥')
const catchBlock = floatCs.split('catch (Exception ex)')[1]?.slice(0, 400) ?? ''
if (!catchBlock.includes('Close()')) fail('启动失败后悬浮窗不关')
if (floatCs.includes('new Progress<string>(_ => { })'))
  fail('准备 node 的进度被丢掉')

const gate = read('Core/CredentialsGate.cs')
if (gate.includes('EnvHasDeepseekKey(inst.Launch?.ExtraEnv)') ||
    gate.includes('EnvHasDeepseekKey(settings.Launch.ExtraEnv)'))
  fail('Inspect 把 extraEnv 里的密钥当成已配置，密钥只应活在 credentials.yaml')

const portableTs = readFileSync(join(import.meta.dirname, 'portable.ts'), 'utf8')
if (!portableTs.includes('assertPlayKitSafe') && !portableTs.includes('launchScriptLeaksKey'))
  fail('打 zip 前不检查 launch-*.cmd 是否烘焙了 API Key')
if (!portableTs.includes('pad-cli.log') || !portableTs.includes('runtime.json'))
  fail('packPlayZip 不排除打包机状态文件')
if (!portableTs.includes('packPlay7z') || !portableTs.includes('-t7z'))
  fail('pnpm 小文件超过 65535 条必须打 7z，Explorer 压缩文件夹打不开 ZIP64')
if (!portableTs.includes('-snl'))
  fail('7z 不带 -snl 会顺着 junction 把店打成真目录，解压再丢 hashed chunk')
if (!portableTs.includes('r.status !== 1') && !portableTs.includes('status === 1'))
  fail('7z 遇到坏 junction 警告会 exit 1，不能当打包失败扔掉已生成的档')
if (!portableTs.includes('ZIP64') && !portableTs.includes('Zip64'))
  fail('没写清 Windows 压缩文件夹会把 ZIP64 报成无效')

const codes = readFileSync(join(import.meta.dirname, 'registries/pa-codes.json'), 'utf8')
if (!codes.includes('PA117')) fail('portable-export 打到开发宿主时没有 PA117')

console.log('✓ launch-script 便携 / 密钥 / 等待契约')
