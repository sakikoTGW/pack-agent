#!/usr/bin/env bun
/**
 * 工作区快照导出（MODPACK-STATE.md）：把实例里「workspace 条目 + 它的 session 文件」打成 `.workspace.zip`。
 *
 *   state/storages/workspace.json   —— 裁剪成只含选中 workspace
 *   state/sessions/<escaped>/<id>/  —— 选中的 session 目录
 *   snapshot.json                   —— originPath / sessionIds / requires（安装端据此重映射）
 *
 * 只导出：不碰凭据、settings、启动器产物。安装（写回与路径重写）不在本模块范围。
 */
import { promises as fs } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { spawnSync } from 'node:child_process'
import { writeZipFromDir } from '../../src/pack-archive.js'
import { PackforgeError, isSafeRelPath } from './packforge.js'

export const WORKSPACE_SNAPSHOT_SCHEMA = 'pad-workspace-snapshot/v1'
export const WORKSPACE_SNAPSHOT_VERSION = 1

export type SnapshotWorkspace = {
  id: string
  title: string
  originPath: string
  sessionIds: string[]
  sessionCount: number
  bytes: number
  encodedDir?: string
}

export type WorkspaceSnapshotDoc = {
  schema: string
  version: number
  name: string
  snapshotVersion: string
  kind: 'workspace' | 'both'
  dshVersion?: string
  requires: { profiles: string[]; plugins: Record<string, string> }
  state: { workspaces: SnapshotWorkspace[]; sessionsRoot: string }
  source: { instance?: string; tool: string; createdAt: string }
}

export type ExportWorkspaceSnapshotOptions = {
  home: string
  out: string
  /** 只导出这些 workspace id；缺省全部 */
  workspaces?: string[]
  /** 每个 workspace 最多带最近 N 个 session（按 session 文件 mtime 倒序） */
  maxSessions?: number
  name?: string
  version?: string
}

export type ExportWorkspaceSnapshotResult = {
  path: string
  snapshot: WorkspaceSnapshotDoc
  stats: { workspaces: number; sessions: number; bytes: number }
  warnings: string[]
}

function fail(code: string, msg: string): never {
  throw new PackforgeError(code, msg)
}

function runTar(args: string[], cwd?: string): void {
  const r = spawnSync('tar', args, { cwd, encoding: 'utf8', shell: false })
  if (r.status !== 0) fail('PA140', `tar failed: ${(r.stderr || r.stdout || '').trim() || r.status}`)
}

async function readJson<T>(path: string): Promise<T | undefined> {
  try {
    return JSON.parse(await fs.readFile(path, 'utf8')) as T
  } catch {
    return undefined
  }
}

function slug(s: string): string {
  const out = String(s)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  return out || 'workspace-snapshot'
}

type SessionFile = { abs: string; rel: string; mtime: number; bytes: number }

/** 在 sessions/<escapedDir>/<sessionId>/ 下找这个 session，返回目录名 + 文件。 */
async function findSession(home: string, sessionId: string): Promise<{ dirName: string; files: SessionFile[] } | undefined> {
  const sessionsRoot = join(home, 'sessions')
  let dirs: string[] = []
  try {
    dirs = (await fs.readdir(sessionsRoot, { withFileTypes: true })).filter(e => e.isDirectory()).map(e => e.name)
  } catch {
    return undefined
  }
  for (const dirName of dirs) {
    const dir = join(sessionsRoot, dirName, sessionId)
    let st
    try {
      st = await fs.stat(dir)
    } catch {
      continue
    }
    if (!st.isDirectory()) continue
    const files: SessionFile[] = []
    const walk = async (root: string, base: string): Promise<void> => {
      for (const e of await fs.readdir(root, { withFileTypes: true })) {
        const abs = join(root, e.name)
        if (e.isDirectory()) await walk(abs, base)
        else if (e.isFile()) {
          const s = await fs.stat(abs)
          files.push({ abs, rel: abs.slice(base.length).replace(/^[\\/]/, '').replace(/\\/g, '/'), mtime: s.mtimeMs, bytes: s.size })
        }
      }
    }
    await walk(dir, dir)
    return { dirName, files }
  }
  return undefined
}

type WorkspaceStore = {
  unit?: unknown
  global?: { initialized?: boolean; workspaceIds?: string[]; archivedSessionIds?: string[] }
  tables?: { workspaces?: Record<string, Record<string, unknown>> }
}

/** 实例 home → 工作区快照 `.workspace.zip`。 */
export async function exportWorkspaceSnapshot(opts: ExportWorkspaceSnapshotOptions): Promise<ExportWorkspaceSnapshotResult> {
  const home = resolve(opts.home)
  try {
    const st = await fs.stat(home)
    if (!st.isDirectory()) fail('PA150', `instance home is not a directory: ${home}`)
  } catch {
    fail('PA150', `instance home does not exist: ${home}`)
  }
  const storePath = join(home, 'storages', 'workspace.json')
  const store = await readJson<WorkspaceStore>(storePath)
  const tables = store?.tables?.workspaces
  if (!tables || Object.keys(tables).length === 0) fail('PA150', `no storages/workspace.json workspaces under ${home}`)

  const instanceMeta = await readJson<Record<string, unknown>>(join(home, '..', 'instance.json'))
  const dshMeta = instanceMeta?.dsh && typeof instanceMeta.dsh === 'object' ? (instanceMeta.dsh as Record<string, unknown>) : {}
  const dshVersion = typeof dshMeta.version === 'string' ? dshMeta.version : undefined
  const name = slug(opts.name ?? (typeof instanceMeta?.name === 'string' ? instanceMeta.name : 'workspace-snapshot'))
  const snapshotVersion = opts.version ?? '1.0.0'
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(snapshotVersion)) fail('PA141', `invalid snapshot version: ${snapshotVersion}`)

  // profiles / 依赖（requires）
  const profilesDir = join(home, 'profiles')
  let profileNames: string[] = []
  try {
    profileNames = (await fs.readdir(profilesDir, { withFileTypes: true })).filter(e => e.isDirectory()).map(e => e.name).sort()
  } catch {
    /* optional */
  }
  const plugins: Record<string, string> = {}
  const lastProfile = typeof instanceMeta?.lastProfile === 'string' ? instanceMeta.lastProfile : profileNames[0]
  if (lastProfile) {
    const pkg = await readJson<Record<string, unknown>>(join(profilesDir, lastProfile, 'package.json'))
    const deps = pkg?.dependencies && typeof pkg.dependencies === 'object' ? (pkg.dependencies as Record<string, string>) : {}
    for (const [k, v] of Object.entries(deps)) if (typeof v === 'string') plugins[k] = v
  }

  const wantedIds = opts.workspaces?.length ? new Set(opts.workspaces) : null
  const warnings: string[] = []
  const chosen: SnapshotWorkspace[] = []
  const keptByWs = new Map<string, { id: string; files: SessionFile[]; dirName: string }[]>()
  for (const [id, raw] of Object.entries(tables)) {
    if (wantedIds && !wantedIds.has(id)) continue
    const w = raw ?? {}
    const originPath = typeof w.path === 'string' ? w.path : ''
    if (!originPath) {
      warnings.push(`workspace ${id} has no path; skipped`)
      continue
    }
    const sessionIds = Array.isArray(w.sessionIds) ? (w.sessionIds as string[]).filter(s => typeof s === 'string') : []
    const picked: { id: string; files: SessionFile[]; dirName: string; mtime: number }[] = []
    for (const sid of sessionIds) {
      const hit = await findSession(home, sid)
      if (!hit || hit.files.length === 0) {
        warnings.push(`session ${sid} not found on disk; skipped`)
        continue
      }
      picked.push({ id: sid, files: hit.files, dirName: hit.dirName, mtime: Math.max(...hit.files.map(f => f.mtime)) })
    }
    picked.sort((a, b) => b.mtime - a.mtime)
    const kept = typeof opts.maxSessions === 'number' && opts.maxSessions > 0 ? picked.slice(0, opts.maxSessions) : picked
    if (kept.length === 0 && sessionIds.length > 0) {
      warnings.push(`workspace ${id} has no sessions on disk; skipped`)
      continue
    }
    const bytes = kept.reduce((n, s) => n + s.files.reduce((m, f) => m + f.bytes, 0), 0)
    chosen.push({
      id,
      title: typeof w.title === 'string' ? w.title : id,
      originPath,
      sessionIds: kept.map(s => s.id),
      sessionCount: kept.length,
      bytes,
      encodedDir: kept[0]?.dirName,
    })
    keptByWs.set(id, kept)
  }
  if (chosen.length === 0) fail('PA150', 'no exportable workspace (check --workspace / session data)')

  const sessionsRoot = 'state/sessions'
  const stage = await fs.mkdtemp(join(await fs.realpath(process.env.TEMP || process.env.TMPDIR || '.'), 'pad-workspace-snapshot-export-'))
  const out = resolve(opts.out)
  try {
    await fs.mkdir(join(stage, 'state', 'storages'), { recursive: true })
    // 裁剪 workspace.json：只保留选中的 workspace 与 session
    const trimmed: WorkspaceStore = {
      unit: store?.unit ?? { name: 'workspace', version: 2 },
      global: {
        initialized: true,
        workspaceIds: chosen.map(w => w.id),
        archivedSessionIds: [],
      },
      tables: { workspaces: {} },
    }
    for (const w of chosen) {
      trimmed.tables!.workspaces![w.id] = {
        path: w.originPath,
        title: w.title,
        sessionIds: w.sessionIds,
        createdAt: (tables[w.id] as Record<string, unknown>).createdAt,
        updatedAt: (tables[w.id] as Record<string, unknown>).updatedAt,
      }
    }
    await fs.writeFile(join(stage, 'state', 'storages', 'workspace.json'), JSON.stringify(trimmed, null, 2), 'utf8')

    let totalSessions = 0
    let totalBytes = 0
    for (const w of chosen) {
      const kept = keptByWs.get(w.id) ?? []
      for (const s of kept) {
        for (const f of s.files) {
          if (!isSafeRelPath(f.rel)) fail('PA143', `unsafe session file path: ${f.rel}`)
          const dst = join(stage, sessionsRoot, s.dirName, s.id, f.rel)
          await fs.mkdir(join(dst, '..'), { recursive: true })
          await fs.copyFile(f.abs, dst)
        }
        totalSessions++
        totalBytes += s.files.reduce((m, f) => m + f.bytes, 0)
      }
    }

    const snapshot: WorkspaceSnapshotDoc = {
      schema: WORKSPACE_SNAPSHOT_SCHEMA,
      version: WORKSPACE_SNAPSHOT_VERSION,
      name,
      snapshotVersion,
      kind: 'workspace',
      dshVersion,
      requires: { profiles: profileNames, plugins },
      state: { workspaces: chosen, sessionsRoot },
      source: { instance: typeof instanceMeta?.id === 'string' ? instanceMeta.id : undefined, tool: 'PAD workspace snapshot export', createdAt: new Date().toISOString() },
    }
    await fs.writeFile(join(stage, 'snapshot.json'), JSON.stringify(snapshot, null, 2), 'utf8')
    await fs.mkdir(dirname(out), { recursive: true })
    await writeZipFromDir(stage, out)
    return { path: out, snapshot, stats: { workspaces: chosen.length, sessions: totalSessions, bytes: totalBytes }, warnings }
  } finally {
    await fs.rm(stage, { recursive: true, force: true }).catch(() => {})
  }
}

/** 读回工作区快照自检（供测试/检查用）。 */
export async function readWorkspaceSnapshot(file: string): Promise<{ snapshot: WorkspaceSnapshotDoc; files: string[]; extractedRoot: string; cleanup: () => Promise<void> }> {
  const abs = resolve(file)
  let head: Uint8Array
  const fh = await fs.open(abs, 'r')
  try {
    head = new Uint8Array(4)
    await fh.read(head, 0, 4, 0)
  } finally {
    await fh.close()
  }
  if (!(head[0] === 0x50 && head[1] === 0x4b)) fail('PA140', `not a zip: ${abs}`)
  const root = await fs.mkdtemp(join(await fs.realpath(process.env.TEMP || process.env.TMPDIR || '.'), 'pad-workspace-snapshot-read-'))
  const cleanup = async () => fs.rm(root, { recursive: true, force: true }).catch(() => {})
  try {
    runTar(['-xf', abs, '-C', root])
    const snapshot = await readJson<WorkspaceSnapshotDoc>(join(root, 'snapshot.json'))
    if (!snapshot || snapshot.schema !== WORKSPACE_SNAPSHOT_SCHEMA) fail('PA141', 'snapshot.json missing or wrong schema')
    const files: string[] = []
    const walk = async (dir: string, base: string): Promise<void> => {
      for (const e of await fs.readdir(dir, { withFileTypes: true })) {
        const p = join(dir, e.name)
        if (e.isDirectory()) await walk(p, base)
        else if (e.isFile()) files.push(p.slice(base.length).replace(/^[\\/]/, '').replace(/\\/g, '/'))
      }
    }
    await walk(root, root)
    return { snapshot, files, extractedRoot: root, cleanup }
  } catch (e) {
    await cleanup()
    throw e
  }
}
