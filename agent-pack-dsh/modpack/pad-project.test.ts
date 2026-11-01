#!/usr/bin/env bun
/**
 * 项目轴（project axis）验收：project.json 主数据、`storages/workspace.json` 合并写入、
 * 二次 seed 保留 sessionIds、坏工作区不写、自动收编、删除。
 *
 * 复用 pad-depth 的口径：临时 root + 真跑 `pad cli project …`；WinExe 的 stdout 不能可靠重定向，
 * 所以机器可读输出走 `--json --out <file>`。
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
const projectCs = join(pad, 'Core/Project.cs')
const projectsCs = join(pad, 'Core/Projects.cs')
const registryCs = join(pad, 'Core/WorkspaceRegistry.cs')
const cliCs = join(pad, 'Core/ProjectCli.cs')

// ---- 1) 源码契约：不启 exe 也能查的一半 ---------------------------------

for (const f of [projectCs, projectsCs, registryCs, cliCs])
  if (!existsSync(f)) fail(`缺少 ${f}`)

const projectSrc = readFileSync(projectCs, 'utf8')
if (!projectSrc.includes('pack-agent.launcher.project/v1')) fail('Project.cs 没有 project/v1 schema')
for (const key of ['workspaces', 'agents', 'instance'])
  if (!projectSrc.includes(`"${key}"`)) fail(`Project.cs 缺字段 ${key}`)

const projectsSrc = readFileSync(projectsCs, 'utf8')
if (!projectsSrc.includes('.pad', ) || !projectsSrc.includes('project.json'))
  fail('Projects.cs 没有项目内 .pad/project.json 覆盖')
if (!projectsSrc.includes('PA122') || !projectsSrc.includes('PA123'))
  fail('Projects.cs 没有 PA122/PA123 校验')

const registrySrc = readFileSync(registryCs, 'utf8')
for (const need of ['storage', 'workspace.json', 'initialized', 'workspaceIds', '.bak', 'PA124'])
  if (!registrySrc.includes(need)) fail(`WorkspaceRegistry.cs 缺 ${need}`)
if (!registrySrc.includes('LeafTitle')) fail('WorkspaceRegistry.cs 没有标题规则（home/workspace 取上级）')

const cliSrc = readFileSync(cliCs, 'utf8')
for (const cmd of ['case "list"', 'case "show"', 'case "add"', 'case "remove"', 'case "seed"', 'case "run"'])
  if (!cliSrc.includes(cmd)) fail(`pad cli project 缺 ${cmd}`)

const runnerCs = join(pad, 'Core/ProjectRunner.cs')
if (!existsSync(runnerCs)) fail('缺少 Core/ProjectRunner.cs')
const runnerSrc = readFileSync(runnerCs, 'utf8')
for (const need of ['EnsureInstance', 'SeedAsync', 'RunAsync', 'RunWatch.Read', 'RunFacts.Collect', 'ShellActivate.Try', 'EnsureRuntimeNode', 'PA126', 'AgentPresets'])
  if (!runnerSrc.includes(need)) fail(`ProjectRunner.cs 缺 ${need}`)

const presetsCs = join(pad, 'Core/AgentPresets.cs')
if (!existsSync(presetsCs)) fail('缺少 Core/AgentPresets.cs')
const presetsSrc = readFileSync(presetsCs, 'utf8')
for (const need of ['cordis.patch.yml', 'ManagedBegin', 'agent-preset-registry', 'ProbeAsync', 'PA125', '--dump-config'])
  if (!presetsSrc.includes(need)) fail(`AgentPresets.cs 缺 ${need}`)

// UI 接线（D65）：项目页读的是同一份 ProjectRunner/Projects，不在视图里复制一套逻辑。
const wbSrc = readFileSync(join(pad, 'Views/WorkbenchView.xaml.cs'), 'utf8')
for (const need of ['ShowProject', 'StartProjectAsync', 'ProjectPresetCard', 'ProjectAxisBody',
  'PaintProjectCards', 'ProjectRunner.RunAsync', 'AgentPresets.Apply'])
  if (!wbSrc.includes(need)) fail(`WorkbenchView 缺项目轴接线：${need}`)

console.log('✓ 项目轴源码契约（schema / 覆盖 / 校验 / seed / run / preset / 项目页）')

// ---- 2) 真跑 CLI ------------------------------------------------------

const exe = process.env.PAD_EXE ?? join(pad, 'bin/Debug/net9.0-windows/pack-agent-for DSH.exe')
if (process.platform !== 'win32' || !existsSync(exe)) {
  console.log('· 无 Windows 编译产物，跳过实跑')
  process.exit(0)
}

const root = packTmpDir('pad-project-axis')
const cleanup = () => rmSync(root, { recursive: true, force: true })

function runCli(args: string[]): { status: number; text: string } {
  const env = { ...process.env, PACK_LAUNCHER_ROOT: root }
  const r = spawnSync(exe, args, { env, encoding: 'utf8', timeout: 60000 })
  return { status: r.status ?? -1, text: `${r.stdout ?? ''}${r.stderr ?? ''}` }
}

function outPath(name: string): string {
  return join(root, '.test-out', name)
}

function readJson<T>(file: string): T {
  if (!existsSync(file)) fail(`期望输出文件不存在：${file}`)
  try {
    return JSON.parse(readFileSync(file, 'utf8')) as T
  } catch (error) {
    fail(`输出不是 JSON（${file}）：${String(error)}`)
  }
}

try {
  const w1 = join(root, 'work', 'alpha')
  const w2 = join(root, 'work', 'beta')
  const wMissing = join(root, 'work', 'does-not-exist')
  const home = join(root, 'home')
  for (const d of [w1, w2]) mkdirSync(d, { recursive: true })
  mkdirSync(join(home, 'storages'), { recursive: true })

  // 2.1 add：项目落盘 + 路径规范化
  let out = outPath('add.json')
  let r = runCli(['cli', 'project', 'add', 'alpha', '--workspace', w1, '--workspace', w2, '--json', '--out', out])
  if (r.status !== 0) fail(`project add 失败：${r.text}`)
  const projectFile = join(root, 'projects', 'alpha', 'project.json')
  if (!existsSync(projectFile)) fail('project add 没有写 projects/alpha/project.json')
  const project = JSON.parse(readFileSync(projectFile, 'utf8')) as {
    schema: string
    id: string
    name: string
    workspaces: string[]
  }
  if (project.schema !== 'pack-agent.launcher.project/v1') fail(`schema 不对：${project.schema}`)
  if (project.id !== 'alpha' || project.name !== 'alpha') fail(`id/name 不对：${project.id}/${project.name}`)
  if (project.workspaces.length !== 2) fail(`工作区应有 2 个，实际 ${project.workspaces.length}`)
  for (const w of project.workspaces)
    if (!w.toLowerCase().startsWith(root.toLowerCase())) fail(`工作区没有被规范成绝对路径：${w}`)

  // 2.2 list
  out = outPath('list.json')
  r = runCli(['cli', 'project', 'list', '--json', '--out', out])
  if (r.status !== 0) fail(`project list 失败：${r.text}`)
  const list = readJson<Array<{ id: string; workspaces: number }>>(out)
  const row = list.find((p) => p.id === 'alpha')
  if (!row || row.workspaces !== 2) fail(`list 里没有 alpha/2：${JSON.stringify(list)}`)

  // 2.3 seed 第一次：写 registry，两个新 uuid，initialized=true
  out = outPath('seed1.json')
  r = runCli(['cli', 'project', 'seed', 'alpha', '--home', home, '--json', '--out', out])
  if (r.status !== 0) fail(`project seed 失败：${r.text}`)
  const registryFile = join(home, 'storages', 'workspace.json')
  if (!existsSync(registryFile)) fail('seed 没有写 home/storages/workspace.json')
  const reg1 = JSON.parse(readFileSync(registryFile, 'utf8')) as {
    unit: { name: string; version: number }
    global: { initialized: boolean; workspaceIds: string[]; defaultWorkspaceId?: string }
    tables: { workspaces: Record<string, { path: string; title: string; sessionIds: string[] }> }
  }
  if (reg1.unit.name !== 'workspace' || reg1.unit.version !== 2) fail('registry unit 不是 workspace/2')
  if (!reg1.global.initialized) fail('registry 没有 initialized=true')
  if (reg1.global.workspaceIds.length !== 2) fail(`workspaceIds 应有 2 个，实际 ${reg1.global.workspaceIds.length}`)
  if (Object.keys(reg1.tables.workspaces).length !== 2) fail('registry 表里不是 2 条')
  if (!reg1.global.defaultWorkspaceId) fail('registry 没有 defaultWorkspaceId')
  const paths1 = reg1.global.workspaceIds.map((id) => reg1.tables.workspaces[id].path.toLowerCase())
  if (!paths1.some((p) => p.endsWith('alpha')) || !paths1.some((p) => p.endsWith('beta')))
    fail(`registry 路径不对：${JSON.stringify(paths1)}`)

  // 2.4 先塞一条带 sessionIds 的旧记录 → seed 必须保留 uuid 与 sessionIds
  const oldId = reg1.global.workspaceIds.find((id) => reg1.tables.workspaces[id].path.toLowerCase().endsWith('alpha'))!
  reg1.tables.workspaces[oldId].sessionIds = ['session-keep-1', 'session-keep-2']
  writeFileSync(registryFile, JSON.stringify(reg1, null, 2))
  out = outPath('seed2.json')
  r = runCli(['cli', 'project', 'seed', 'alpha', '--home', home, '--json', '--out', out])
  if (r.status !== 0) fail(`第二次 seed 失败：${r.text}`)
  const reg2 = JSON.parse(readFileSync(registryFile, 'utf8')) as typeof reg1
  const stillThere = reg2.global.workspaceIds.some((id) => id === oldId)
  if (!stillThere) fail('第二次 seed 换了 uuid（sessionIds 会丢）')
  if (reg2.tables.workspaces[oldId].sessionIds.length !== 2) fail('第二次 seed 没保留 sessionIds')
  if (existsSync(join(home, 'storages', 'workspace.json.bak')) === false) fail('seed 没有留 .bak 备份')
  const seed2 = readJson<{ kept: number; added: number }>(out)
  if (seed2.kept !== 2 || seed2.added !== 0) fail(`二次 seed 计数不对：kept=${seed2.kept} added=${seed2.added}`)

  // 2.5 不存在的目录：PA123，registry 一个字节都不动
  const before = readFileSync(registryFile, 'utf8')
  r = runCli(['cli', 'project', 'add', 'broken', '--workspace', wMissing])
  if (r.status === 0) fail('绑定不存在目录居然成功了')
  if (readFileSync(registryFile, 'utf8') !== before) fail('失败的 add 改动了 registry')

  // 2.6 remove
  r = runCli(['cli', 'project', 'remove', 'alpha'])
  if (r.status !== 0) fail(`project remove 失败：${r.text}`)
  if (existsSync(join(root, 'projects', 'alpha'))) fail('remove 之后 projects/alpha 还在')

  // 2.8 run：不存在 → 失败；shell 窗口 → PA126（不启动进程，也不建实例）
  r = runCli(['cli', 'project', 'run', 'no-such-project'])
  if (r.status === 0) fail('run 一个不存在的项目居然成功了')

  r = runCli(['cli', 'project', 'add', 'shellproj', '--workspace', w1])
  if (r.status !== 0) fail(`建 shellproj 失败：${r.text}`)
  const shellFile = join(root, 'projects', 'shellproj', 'project.json')
  const shellProject = JSON.parse(readFileSync(shellFile, 'utf8')) as { window: { kind: string } }
  shellProject.window.kind = 'shell'
  writeFileSync(shellFile, JSON.stringify(shellProject, null, 2))
  r = runCli(['cli', 'project', 'run', 'shellproj'])
  if (r.status === 0) fail('shell 窗口居然被当成已实现')
  if (!r.text.includes('PA126')) fail(`shell 窗口应以 PA126 拒绝，实际：${r.text}`)
  if (existsSync(join(root, 'instances', 'shellproj'))) fail('被拒绝的 run 不该建实例')

  // 2.9 preset：patch 覆盖层合并（无发行号也能验，--force 跳过探测）
  const presetWs = join(root, 'preset-ws')
  mkdirSync(presetWs, { recursive: true })
  r = runCli(['cli', 'project', 'add', 'presetproj', '--workspace', presetWs, '--version', '9.9.9', '--profile', 'web'])
  if (r.status !== 0) fail(`建 presetproj 失败：${r.text}`)
  const presetHome = join(root, 'preset-home')
  const profileDir = join(presetHome, 'profiles', 'web')
  mkdirSync(profileDir, { recursive: true })
  const patchFile = join(profileDir, 'cordis.patch.yml')
  const applyPreset = (id: string) =>
    runCli(['cli', 'project', 'preset', 'presetproj', id, '--home', presetHome, '--profile', 'web', '--force', '--json', '--out', outPath('preset.json')])

  // A) 空的 patch 层是 []：写完后应是合法数组 + 管理块
  writeFileSync(patchFile, '# user layer\n[]\n')
  r = applyPreset('minimal')
  if (r.status !== 0) fail(`preset set 失败：${r.text}`)
  let patchText = readFileSync(patchFile, 'utf8')
  if (!patchText.includes('# >>> pad project preset (managed) >>>')) fail('preset 没写管理块')
  if (!patchText.includes('default: minimal')) fail('preset 没写 default: minimal')
  if (patchText.includes('\n[]\n')) fail('[] 占位没有清掉，patch 层不合法')
  if (patchText.startsWith('# user layer') === false) fail('用户注释被删了')

  // B) 已有用户 patch 行：必须原样保留，块追加在后面
  writeFileSync(patchFile, '- id: llm-pi-ai\n  name: "@deepseek-ai/dsh-llm-pi-ai"\n  config:\n    providers: {}\n')
  r = applyPreset('standard')
  if (r.status !== 0) fail(`preset set（有用户行）失败：${r.text}`)
  patchText = readFileSync(patchFile, 'utf8')
  if (!patchText.includes('id: llm-pi-ai')) fail('用户 patch 行被删了')
  if (!patchText.includes('default: standard')) fail('第二次 set 没生效')

  // C) 幂等：再 set 一次只留一个管理块
  r = applyPreset('minimal')
  if (r.status !== 0) fail(`preset set（幂等）失败：${r.text}`)
  patchText = readFileSync(patchFile, 'utf8')
  const blocks = patchText.split('# >>> pad project preset (managed) >>>').length - 1
  if (blocks !== 1) fail(`管理块应只有 1 个，实际 ${blocks}`)
  if (!patchText.includes('default: minimal')) fail('幂等 set 的 default 不对')

  // D) clear：管理块移除，用户行保留
  r = runCli(['cli', 'project', 'preset', 'presetproj', '--clear', '--home', presetHome, '--profile', 'web', '--json', '--out', outPath('clear.json')])
  if (r.status !== 0) fail(`preset clear 失败：${r.text}`)
  patchText = readFileSync(patchFile, 'utf8')
  if (patchText.includes('pad project preset (managed)')) fail('clear 之后管理块还在')
  if (!patchText.includes('id: llm-pi-ai')) fail('clear 把用户 patch 行也删了')

  // E) 非法 id → PA125；没有发行号又不开 --force → PA125
  r = runCli(['cli', 'project', 'preset', 'presetproj', 'BAD!', '--home', presetHome, '--profile', 'web', '--force'])
  if (r.status === 0) fail('非法 preset id 居然写进去了')
  if (!r.text.includes('PA125')) fail(`非法 id 应以 PA125 拒绝，实际：${r.text}`)
  r = runCli(['cli', 'project', 'preset', 'presetproj', 'minimal', '--home', presetHome, '--profile', 'web'])
  if (r.status === 0) fail('没有发行号也没 --force 居然绑定成功了')
  if (!r.text.includes('PA125')) fail(`探测失败应以 PA125 拒绝，实际：${r.text}`)

  // F) 多 preset（P2）：add 挂选项、use 换默认、remove 摘掉、清空后 patch 仍合法
  const presetProjectFile = join(root, 'projects', 'presetproj', 'project.json')
  const agentsOf = () => (JSON.parse(readFileSync(presetProjectFile, 'utf8')) as { agents: string[] }).agents
  const presetVerb = (verb: string[] | string, extra: string[] = []) =>
    runCli(['cli', 'project', 'preset', ...(Array.isArray(verb) ? verb : [verb]),
      'presetproj', ...extra, '--home', presetHome, '--profile', 'web', '--force', '--json',
      '--out', outPath('preset2.json')])

  r = presetVerb('add', ['minimal'])
  if (r.status !== 0) fail(`preset add 失败：${r.text}`)
  if (JSON.stringify(agentsOf()) !== '["minimal"]') fail(`add 后 agents=${JSON.stringify(agentsOf())}`)
  r = presetVerb('add', ['ptc'])
  if (r.status !== 0) fail(`preset add 第二个失败：${r.text}`)
  if (JSON.stringify(agentsOf()) !== '["minimal","ptc"]') fail(`两个 agent 不对：${JSON.stringify(agentsOf())}`)
  if (!readFileSync(patchFile, 'utf8').includes('default: minimal')) fail('add 不该换默认')

  r = presetVerb('use', ['ptc'])
  if (r.status !== 0) fail(`preset use 失败：${r.text}`)
  if (JSON.stringify(agentsOf()) !== '["ptc","minimal"]') fail(`use 后顺序不对：${JSON.stringify(agentsOf())}`)
  if (!readFileSync(patchFile, 'utf8').includes('default: ptc')) fail('use 没换 patch 默认')

  r = presetVerb('remove', ['minimal'])
  if (r.status !== 0) fail(`preset remove 失败：${r.text}`)
  if (JSON.stringify(agentsOf()) !== '["ptc"]') fail(`remove 后不对：${JSON.stringify(agentsOf())}`)
  r = presetVerb('remove', ['ptc'])
  if (r.status !== 0) fail(`preset remove 最后一个失败：${r.text}`)
  if (JSON.stringify(agentsOf()) !== '[]') fail(`清空后 agents 不对：${JSON.stringify(agentsOf())}`)
  patchText = readFileSync(patchFile, 'utf8')
  if (patchText.includes('pad project preset (managed)')) fail('清空后管理块还在')
  if (!patchText.includes('id: llm-pi-ai')) fail('清空把用户行删了')

  console.log('✓ 项目轴实跑（add/list/seed×2 保留 sessionIds/坏路径不写/remove/run 拒绝 shell/preset 合并与 PA125/多 preset 增删换）')
  cleanup()
} catch (error) {
  cleanup()
  fail(String(error))
}
// v4 页面（D69）：现在流 / 实例清单 / 项目页状态条。
const v4Src = readFileSync(join(pad, 'Views/WorkbenchV4.cs'), 'utf8')
for (const need of ['PaintNowCards', 'NowSection', 'PaintInstanceCards',
  'ProjectStatusStrip', 'ProjectTeamCard', 'ProjectTasksCard', 'ProjectCapsCard', 'ProjectRecentCard'])
  if (!v4Src.includes(need)) fail(`WorkbenchV4 缺 v4 页面/卡片：${need}`)
if (v4Src.includes('_adoptMode') || v4Src.includes('ProjectAdopt'))
  fail('自动收编已砍掉，WorkbenchV4 不该再有 adopt 代码')
const railSrc = readFileSync(join(pad, 'Views/ProjectRail.xaml.cs'), 'utf8')
for (const need of ['FillInstancesV4', 'ShowHub', 'SegmentChanged', 'ImportClicked'])
  if (!railSrc.includes(need)) fail(`ProjectRail 缺 v4 分段/实例清单：${need}`)
if (railSrc.includes('AdoptClicked')) fail('自动收编已砍掉，ProjectRail 不该再有收编入口')
console.log('✓ v4 页面契约（现在/实例/项目页卡片/侧栏分段）')