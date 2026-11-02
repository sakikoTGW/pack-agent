#!/usr/bin/env bun
/**
 * 工作区快照导出测试：workspace + session 选择、裁剪、敏感排除、workspace.json 重写。
 * 运行：bun run agent-pack-dsh/modpack/workspace-snapshot.test.ts
 */
import { promises as fs } from 'node:fs'
import { join, resolve } from 'node:path'
import { exportWorkspaceSnapshot, readWorkspaceSnapshot } from './workspace-snapshot.js'

const ROOT = resolve(import.meta.dirname, '../../.tmp/workspace-snapshot-fixtures')

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}
function ok(msg: string): void {
  console.log(`✔ ${msg}`)
}

async function writeFiles(root: string, files: Record<string, string>): Promise<void> {
  for (const [rel, body] of Object.entries(files)) {
    const dst = join(root, rel)
    await fs.mkdir(join(dst, '..'), { recursive: true })
    await fs.writeFile(dst, body, 'utf8')
  }
}

async function buildInstance(): Promise<string> {
  const inst = join(ROOT, 'instances', 'm1')
  const home = join(inst, 'home')
  await fs.rm(ROOT, { recursive: true, force: true })
  await fs.mkdir(home, { recursive: true })
  await writeFiles(inst, {
    'instance.json': JSON.stringify({
      schema: 'pack-agent.launcher.instance/v2',
      id: 'm1',
      name: 'Map One',
      dsh: { version: '0.2.1-rc.2' },
      home,
      lastProfile: 'dev',
    }),
  })
  await writeFiles(home, {
    'storages/workspace.json': JSON.stringify({
      unit: { name: 'workspace', version: 2 },
      global: { initialized: true, workspaceIds: ['ws1', 'ws2', 'ws3'], archivedSessionIds: [] },
      tables: {
        workspaces: {
          ws1: { path: 'E:\\proj-one', title: 'Proj One', sessionIds: ['s1', 's2'], createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-02T00:00:00Z' },
          ws2: { path: 'E:\\proj-two', title: 'Proj Two', sessionIds: ['s3'], createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-02T00:00:00Z' },
          ws3: { path: 'E:\\proj-three', title: 'Proj Three', sessionIds: ['missing'] },
        },
      },
    }),
    'sessions/--E-proj-one--/s1/session.jsonl.zstd': 'session-1-old',
    'sessions/--E-proj-one--/s2/session.jsonl.zstd': 'session-2-new',
    'sessions/--E-proj-two--/s3/session.jsonl.zstd': 'session-3',
    'profiles/dev/package.json': JSON.stringify({
      name: 'dsh-profile-dev',
      private: true,
      dsh: { profile: { bundles: ['@deepseek-ai/dsh-base'] } },
      dependencies: { ranged: '^1.2.0' },
    }),
    '.credentials.yaml': 'api_key: nope\n',
    'settings.yaml': 'secret: true\n',
    'launcher.patch.yml': '- id: x\n',
  })
  // 控制 mtime：s2 比 s1 新，maxSessions=1 应留 s2
  const old = new Date('2026-01-01T00:00:00Z')
  const fresh = new Date('2026-02-01T00:00:00Z')
  await fs.utimes(join(home, 'sessions/--E-proj-one--/s1/session.jsonl.zstd'), old, old)
  await fs.utimes(join(home, 'sessions/--E-proj-one--/s2/session.jsonl.zstd'), fresh, fresh)
  return home
}

async function main(): Promise<void> {
  const home = await buildInstance()
  console.log(`fixtures: ${ROOT}`)

  // 1) 全量导出
  const all = await exportWorkspaceSnapshot({ home, out: join(ROOT, 'out', 'all.workspace.zip') })
  if (all.stats.workspaces !== 2) fail(`workspaces ${all.stats.workspaces}`)
  if (all.stats.sessions !== 3) fail(`sessions ${all.stats.sessions}`)
  if (!all.warnings.some(w => w.includes('missing'))) fail('missing-session warning absent')
  const rAll = await readWorkspaceSnapshot(all.path)
  try {
    if (rAll.snapshot.schema !== 'pad-workspace-snapshot/v1') fail('schema')
    if (rAll.snapshot.requires.plugins.ranged !== '^1.2.0') fail('requires plugins')
    if (!rAll.snapshot.requires.profiles.includes('dev')) fail('requires profiles')
    if (!rAll.files.includes('state/storages/workspace.json')) fail('missing workspace.json')
    if (!rAll.files.some(f => f.includes('s2') && f.endsWith('session.jsonl.zstd'))) fail('missing s2')
    for (const banned of ['.credentials.yaml', 'settings.yaml', 'launcher.patch.yml']) {
      if (rAll.files.some(f => f.includes(banned))) fail(`sensitive leaked: ${banned}`)
    }
    const ws = JSON.parse(await fs.readFile(join(rAll.extractedRoot, 'state/storages/workspace.json'), 'utf8'))
    if (Object.keys(ws.tables.workspaces).length !== 2) fail(`trimmed workspaces ${Object.keys(ws.tables.workspaces).length}`)
    if (ws.tables.workspaces.ws1.sessionIds.length !== 2) fail('trimmed ws1 ids')
    ok('export all: 2 workspaces / 3 sessions, trimmed workspace.json, sensitive excluded')
  } finally {
    await rAll.cleanup()
  }

  // 2) 单 workspace + maxSessions（按 mtime 保最新）
  const one = await exportWorkspaceSnapshot({
    home,
    out: join(ROOT, 'out', 'one.workspace.zip'),
    workspaces: ['ws1'],
    maxSessions: 1,
    name: 'proj-one',
    version: '2.0.0',
  })
  if (one.stats.workspaces !== 1 || one.stats.sessions !== 1) fail(`filtered stats ${JSON.stringify(one.stats)}`)
  if (one.snapshot.state.workspaces[0].originPath !== 'E:\\proj-one') fail('originPath')
  if (one.snapshot.state.workspaces[0].sessionIds[0] !== 's2') fail(`maxSessions kept ${one.snapshot.state.workspaces[0].sessionIds[0]}`)
  if (one.snapshot.name !== 'proj-one' || one.snapshot.snapshotVersion !== '2.0.0') fail('name/version override')
  const rOne = await readWorkspaceSnapshot(one.path)
  try {
    const ws = JSON.parse(await fs.readFile(join(rOne.extractedRoot, 'state/storages/workspace.json'), 'utf8'))
    if (Object.keys(ws.tables.workspaces).length !== 1) fail('trimmed to one workspace')
    if (JSON.stringify(ws.tables.workspaces.ws1.sessionIds) !== JSON.stringify(['s2'])) fail('trimmed sessionIds')
    if (!rOne.files.some(f => f.includes('s2'))) fail('s2 not packed')
    if (rOne.files.some(f => f.includes('s1'))) fail('s1 should be dropped by maxSessions')
    ok('export filtered: originPath kept, newest session only, workspace.json trimmed')
  } finally {
    await rOne.cleanup()
  }

  // 3) 不存在的 workspace → PA150
  try {
    await exportWorkspaceSnapshot({ home, out: join(ROOT, 'out', 'bad.workspace.zip'), workspaces: ['nope'] })
    fail('unknown workspace must fail')
  } catch (e) {
    if ((e as { code?: string }).code !== 'PA150') fail(`expected PA150, got ${(e as Error).message}`)
    ok('unknown workspace → PA150')
  }

  console.log('\nALL WORKSPACE SNAPSHOT TESTS PASSED')
}

await main()
