#!/usr/bin/env bun
/**
 * 纵深验收：已有设置必须贯通到 launch-*.cmd，读文件，不搜 XAML。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { packTmpDir } from '../../src/tmp-root.ts'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const pad = join(import.meta.dirname, '../pad')
const runner = readFileSync(join(pad, 'Core/Runner.cs'), 'utf8')
const cli = readFileSync(join(pad, 'Core/Cli.cs'), 'utf8')

const term = runner.split('LaunchTerminal')[1]?.slice(0, 1200) ?? ''
if (term.includes('FormatTitle(settings.Launch.Title') && !term.includes('inst.Launch'))
  fail('终端窗口标题不吃 instance.json 的 launch.title')
if (!cli.includes('case "script"')) fail('pad cli 不能只写脚本、不启动，无法验收贯通')
if (!cli.includes('instance" when cmd == "clone"') && !cli.includes('cmd == "clone"'))
  fail('pad cli 没有 instance clone，窗口克隆是第二条链')
if (!cli.includes('cmd == "workspace"'))
  fail('pad cli 不能切换工作区隔离，窗口会是第二条链')
if (!cli.includes('case "plugin"'))
  fail('pad cli 没有 plugin list/update，管理页会是第二条链')
if (!readFileSync(join(pad, 'Core/Launcher.cs'), 'utf8').includes('RewriteLaunchScripts'))
  fail('改 pad.json 不重生 launch-*.cmd，双击还是旧制度')
if (!readFileSync(join(pad, 'Core/Launcher.cs'), 'utf8').includes('NodeForRelease'))
  fail('发行号 engines.node 对不上 runtime/node，启动还在跟 PATH 漂')
if (!readFileSync(join(pad, 'Core/LaunchPolicy.cs'), 'utf8').includes('NodeAuto'))
  fail('实例自动选择 Node 没有 auto 标记')
if (!runner.includes('EffectiveNodePath') && !readFileSync(join(pad, 'Core/Launcher.cs'), 'utf8').includes('EffectiveNodePath'))
  fail('launch 脚本没走 EffectiveNodePath，auto 会当路径')
if (!readFileSync(join(pad, 'Core/Launcher.cs'), 'utf8').includes('SetWorkspaceIndie'))
  fail('实例不能切换工作区隔离')
if (!readFileSync(join(pad, 'Core/Models.cs'), 'utf8').includes('WorkspaceIndieDefault'))
  fail('新建实例没有全局默认隔离策略')

console.log('✓ 终端标题与 pad cli script 入口')

const exe = join(pad, 'bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
if (process.platform !== 'win32' || !existsSync(exe)) {
  console.log('· 无 Windows 编译产物，跳过实跑')
  process.exit(0)
}

const root = packTmpDir('pad-depth-script')
try {
  const inst = 'depth-demo'
  const home = join(root, 'instances', inst, 'home')
  const workspace = join(root, 'instances', inst, 'workspace')
  const nodeFake = join(root, 'node.exe')
  mkdirSync(join(root, 'instances', inst, 'logs'), { recursive: true })
  mkdirSync(home, { recursive: true })
  mkdirSync(workspace, { recursive: true })
  writeFileSync(nodeFake, '')
  mkdirSync(join(root, 'library'), { recursive: true })
  writeFileSync(join(root, 'library', 'credentials.yaml'), 'token: dummy\n')
  writeFileSync(join(root, 'pad.json'), JSON.stringify({
    schema: 'pack-agent.pad.settings/v4',
    launch: {
      title: 'GLOBAL-TITLE',
      extraEnv: 'FOO=global\nBAZ=from-global',
      preCommand: 'echo GLOBAL-PRE',
      nodePath: nodeFake,
      pauseOnError: true,
      telemetryDisabled: true,
    },
  }))
  writeFileSync(join(root, 'instances', inst, 'instance.json'), JSON.stringify({
    schema: 'pack-agent.launcher.instance/v2',
    id: inst,
    name: '纵深实例',
    dsh: { version: '0.0.0' },
    home,
    workspace: { kind: 'owned', path: workspace },
    launch: {
      title: 'ONLY-THIS-INST',
      extraEnv: 'FOO=from-instance',
      preCommand: 'echo INST-PRE',
    },
  }))

  const run = spawnSync(exe, ['cli', 'script', inst, 'dsh-tui'], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: root },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const logFile = join(root, 'pad-cli.log')
  const text = [run.stdout, run.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (run.status !== 0) fail(`pad cli script 退出 ${run.status}\n${text}`)

  const cmdPath = join(root, 'instances', inst, 'launch-dsh-tui.cmd')
  if (!existsSync(cmdPath)) fail(`没写出 ${cmdPath}\n${text}`)
  const cmd = readFileSync(cmdPath, 'utf8')
  if (!cmd.includes('title ONLY-THIS-INST')) fail(`脚本标题没吃实例覆盖\n${cmd}`)
  if (cmd.includes('title GLOBAL-TITLE')) fail(`脚本标题还在用全局\n${cmd}`)
  if (!cmd.includes('FOO=from-instance')) fail(`实例 extraEnv 没写进脚本\n${cmd}`)
  if (!cmd.includes('BAZ=from-global')) fail(`全局 extraEnv 没留下未覆盖的键\n${cmd}`)
  if (!cmd.includes('echo INST-PRE')) fail(`实例 preCommand 没写进脚本\n${cmd}`)
  if (cmd.includes('echo GLOBAL-PRE')) fail(`实例已覆盖 preCommand 还在跑全局\n${cmd}`)
  if (!cmd.includes('%PAD_ROOT%node.exe')) fail(`跟随全局没把指定 Node 写进脚本\n${cmd}`)
  if (cmd.includes('sk-') && /set\s+"DEEPSEEK_API_KEY=sk-/i.test(cmd))
    fail(`脚本把 API Key 写进了 cmd\n${cmd}`)

  const instFile = join(root, 'instances', inst, 'instance.json')
  const instJson = JSON.parse(readFileSync(instFile, 'utf8')) as { launch?: Record<string, unknown> }
  instJson.launch = { ...instJson.launch, nodePath: 'auto' }
  writeFileSync(instFile, JSON.stringify(instJson))
  const autoRw = spawnSync(exe, ['cli', 'rewrite-scripts'], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: root },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const autoText = [autoRw.stdout, autoRw.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (autoRw.status !== 0) fail(`实例 auto 后 rewrite-scripts 退出 ${autoRw.status}\n${autoText}`)
  const autoCmd = readFileSync(cmdPath, 'utf8')
  if (autoCmd.includes('%PAD_ROOT%node.exe')) fail(`实例 auto 还在用全局指定的 node.exe\n${autoCmd}`)
  console.log('✓ 实例 auto 忽略全局 nodePath')

  writeFileSync(join(root, 'library', 'credentials.yaml'), 'DEEPSEEK_API_KEY: "REF: sk-DEPTH-SECRET"\n')
  const keyed = spawnSync(exe, ['cli', 'rewrite-scripts'], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: root },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const keyedText = [keyed.stdout, keyed.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (keyed.status !== 0) fail(`带 API Key 后 rewrite-scripts 退出 ${keyed.status}\n${keyedText}`)
  const keyedCmd = readFileSync(cmdPath, 'utf8')
  if (keyedCmd.includes('sk-DEPTH-SECRET')) fail('重生后的脚本仍烘焙了 API Key')
  if (!keyedCmd.includes('.credentials.yaml')) fail('重生后的脚本不从 home yaml 加载 API Key')
  if (!keyedCmd.includes('PAD_INST')) fail('重生后的脚本没有 %~dp0 根')
  console.log('✓ launch-*.cmd 吃到全局+实例覆盖，密钥不进脚本')

  const padJson = JSON.parse(readFileSync(join(root, 'pad.json'), 'utf8')) as {
    launch: Record<string, unknown>
  }
  padJson.launch.extraEnv = 'FOO=global\nBAZ=from-global\nREWRITE=after-save'
  writeFileSync(join(root, 'pad.json'), JSON.stringify(padJson))
  const rw = spawnSync(exe, ['cli', 'rewrite-scripts'], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: root },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const rwText = [rw.stdout, rw.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (rw.status !== 0) fail(`pad cli rewrite-scripts 退出 ${rw.status}\n${rwText}`)
  const after = readFileSync(cmdPath, 'utf8')
  if (!after.includes('REWRITE=after-save')) fail(`改 pad.json 后双击脚本还是旧 extraEnv\n${after}`)
  console.log('✓ 改全局设置后重生 launch-*.cmd')

  const cloned = spawnSync(exe, ['cli', 'instance', 'clone', inst, 'clone-depth'], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: root },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const cloneText = [cloned.stdout, cloned.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (cloned.status !== 0) fail(`pad cli instance clone 退出 ${cloned.status}\n${cloneText}`)
  const cloneJson = JSON.parse(readFileSync(join(root, 'instances', 'clone-depth', 'instance.json'), 'utf8')) as {
    launch?: { title?: string }
    home: string
  }
  if (cloneJson.launch?.title !== 'ONLY-THIS-INST') fail(`克隆丢掉了 launch 覆盖\n${JSON.stringify(cloneJson.launch)}`)
  if (cloneJson.home === home) fail('克隆和源实例共用 home')
  console.log('✓ 克隆保留 launch 覆盖且 home 隔离')

  const share = join(root, 'shared-ws')
  mkdirSync(share, { recursive: true })
  const shareRun = spawnSync(exe, ['cli', 'instance', 'workspace', 'share', inst, share], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: root },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const shareText = [shareRun.stdout, shareRun.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (shareRun.status !== 0) fail(`pad cli instance workspace share 退出 ${shareRun.status}\n${shareText}`)
  const sharedJson = JSON.parse(readFileSync(join(root, 'instances', inst, 'instance.json'), 'utf8')) as {
    workspace: { kind: string; path: string }
  }
  if (sharedJson.workspace.kind !== 'existing') fail(`共用工作区后 kind 仍是 ${sharedJson.workspace.kind}`)
  if (sharedJson.workspace.path.replaceAll('/', '\\').toLowerCase() !== share.replaceAll('/', '\\').toLowerCase())
    fail(`共用工作区路径不对：${sharedJson.workspace.path}`)
  const sharedCmd = readFileSync(cmdPath, 'utf8').replaceAll('/', '\\')
  if (!sharedCmd.toLowerCase().includes(share.replaceAll('/', '\\').toLowerCase()))
    fail(`脚本 cwd 没换成共用工作区\n${sharedCmd}`)

  const isoRun = spawnSync(exe, ['cli', 'instance', 'workspace', 'isolate', inst], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: root },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const isoText = [isoRun.stdout, isoRun.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (isoRun.status !== 0) fail(`pad cli instance workspace isolate 退出 ${isoRun.status}\n${isoText}`)
  const isoJson = JSON.parse(readFileSync(join(root, 'instances', inst, 'instance.json'), 'utf8')) as {
    workspace: { kind: string; path: string }
  }
  if (isoJson.workspace.kind !== 'owned') fail(`隔离后 kind 仍是 ${isoJson.workspace.kind}`)
  const ownedWs = join(root, 'instances', inst, 'workspace')
  if (isoJson.workspace.path.replaceAll('/', '\\').toLowerCase() !== ownedWs.replaceAll('/', '\\').toLowerCase())
    fail(`隔离后工作区不在实例目录：${isoJson.workspace.path}`)
  const isoCmd = readFileSync(cmdPath, 'utf8')
  if (!isoCmd.includes('%PAD_INST%workspace'))
    fail(`隔离工作区脚本还钉绝对路径\n${isoCmd}`)
  console.log('✓ 工作区隔离切换贯通到 instance.json 和 launch-*.cmd')
} finally {
  try { rmSync(root, { recursive: true, force: true }) } catch { /* lock */ }
}

const nodeRoot = packTmpDir('pad-depth-node')
try {
  const inst = 'node-pin'
  const home = join(nodeRoot, 'instances', inst, 'home')
  const workspace = join(nodeRoot, 'instances', inst, 'workspace')
  const dshPkg = join(nodeRoot, 'versions', '0.0.0', 'node_modules', '@deepseek-ai', 'dsh')
  mkdirSync(join(dshPkg, 'lib'), { recursive: true })
  mkdirSync(home, { recursive: true })
  mkdirSync(workspace, { recursive: true })
  mkdirSync(join(nodeRoot, 'runtime', 'node', '22.19.0', 'x'), { recursive: true })
  mkdirSync(join(nodeRoot, 'runtime', 'node', '18.0.0', 'x'), { recursive: true })
  writeFileSync(join(dshPkg, 'lib', 'bin.js'), 'console.log("dsh")\n')
  writeFileSync(join(dshPkg, 'package.json'), JSON.stringify({
    name: '@deepseek-ai/dsh',
    version: '0.0.0',
    engines: { node: '>=22.19.0' },
  }))
  writeFileSync(join(nodeRoot, 'versions', '0.0.0', 'version.json'), JSON.stringify({
    schema: 'pack-agent.launcher.version/v1',
    version: '0.0.0',
    verified: true,
    enginesNode: '22.19.0',
  }))
  writeFileSync(join(nodeRoot, 'runtime', 'node', '22.19.0', 'x', 'node.exe'), '')
  writeFileSync(join(nodeRoot, 'runtime', 'node', '18.0.0', 'x', 'node.exe'), '')
  writeFileSync(join(nodeRoot, 'pad.json'), JSON.stringify({
    schema: 'pack-agent.pad.settings/v4',
    launch: { title: 'GLOBAL-TITLE', pauseOnError: true, telemetryDisabled: true },
  }))
  writeFileSync(join(nodeRoot, 'instances', inst, 'instance.json'), JSON.stringify({
    schema: 'pack-agent.launcher.instance/v2',
    id: inst,
    name: '钉 Node',
    dsh: { version: '0.0.0' },
    home,
    workspace: { kind: 'owned', path: workspace },
  }))

  const run = spawnSync(exe, ['cli', 'script', inst, 'dsh-tui'], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: nodeRoot },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const logFile = join(nodeRoot, 'pad-cli.log')
  const text = [run.stdout, run.stderr, existsSync(logFile) ? readFileSync(logFile, 'utf8') : ''].join('\n')
  if (run.status !== 0) fail(`钉 Node 的 pad cli script 退出 ${run.status}\n${text}`)
  const cmd = readFileSync(join(nodeRoot, 'instances', inst, 'launch-dsh-tui.cmd'), 'utf8').replaceAll('/', '\\')
  if (!cmd.includes('runtime\\node\\22.19.0')) fail(`脚本没用该发行号的 Node\n${cmd}`)
  if (cmd.includes('runtime\\node\\18.0.0')) fail(`脚本跟了磁盘上较新的错 Node\n${cmd}`)
  console.log('✓ launch-*.cmd 钉该发行号 engines.node 对应的 runtime/node')
} finally {
  try { rmSync(nodeRoot, { recursive: true, force: true }) } catch { /* lock */ }
}

const createRoot = packTmpDir('pad-depth-indie')
try {
  const dshPkg = join(createRoot, 'versions', '0.0.0', 'node_modules', '@deepseek-ai', 'dsh', 'lib')
  mkdirSync(dshPkg, { recursive: true })
  writeFileSync(join(dshPkg, 'bin.js'), 'console.log("dsh")\n')
  writeFileSync(join(createRoot, 'versions', '0.0.0', 'version.json'), JSON.stringify({
    schema: 'pack-agent.launcher.version/v1',
    version: '0.0.0',
    verified: true,
  }))
  const shared = join(createRoot, 'common-ws')
  mkdirSync(shared, { recursive: true })
  writeFileSync(join(createRoot, 'pad.json'), JSON.stringify({
    schema: 'pack-agent.pad.settings/v4',
    launch: { workspaceIndieDefault: 'shared', sharedWorkspace: shared },
  }))
  const made = spawnSync(exe, ['cli', 'instance', 'create', 'shared-new', '0.0.0'], {
    env: { ...process.env, PACK_LAUNCHER_ROOT: createRoot },
    encoding: 'utf8',
    timeout: 30_000,
  })
  const madeText = [made.stdout, made.stderr, existsSync(join(createRoot, 'pad-cli.log'))
    ? readFileSync(join(createRoot, 'pad-cli.log'), 'utf8') : ''].join('\n')
  if (made.status !== 0) fail(`全局默认共用时 create 退出 ${made.status}\n${madeText}`)
  const created = JSON.parse(readFileSync(join(createRoot, 'instances', 'shared-new', 'instance.json'), 'utf8')) as {
    workspace: { kind: string; path: string }
    home: string
  }
  if (created.workspace.kind !== 'existing') fail(`全局默认共用时新建实例仍隔离工作区：${created.workspace.kind}`)
  if (created.workspace.path.replaceAll('/', '\\').toLowerCase() !== shared.replaceAll('/', '\\').toLowerCase())
    fail(`新建实例没指到全局共用工作区：${created.workspace.path}`)
  if (created.home.replaceAll('/', '\\').toLowerCase() === shared.replaceAll('/', '\\').toLowerCase())
    fail('工作区共用时把 $DSH_HOME 也共用了')
  console.log('✓ 新建实例吃全局默认工作区隔离，home 仍隔离')
} finally {
  try { rmSync(createRoot, { recursive: true, force: true }) } catch { /* lock */ }
}
