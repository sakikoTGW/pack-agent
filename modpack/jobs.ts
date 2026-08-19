/**
 * 实例目录锁 + jobs.json。同一实例同一时刻一个写任务。
 */
import { existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { LauncherError, pa, type LauncherRoot } from './launcher.js'

export type JobLeaf = { id: string; weight: number; done: number; total: number }

export type JobRecord = {
  id: string
  kind: string
  instance?: string
  status: 'queued' | 'running' | 'done' | 'failed' | 'cancelled'
  pid?: number
  createdAt: string
  updatedAt: string
  leaves?: JobLeaf[]
}

export const INSTALL_VERSION_LEAVES: JobLeaf[] = [
  { id: 'resolve-meta', weight: 5, done: 0, total: 5 },
  { id: 'download', weight: 80, done: 0, total: 80 },
  { id: 'verify', weight: 10, done: 0, total: 10 },
  { id: 'record', weight: 5, done: 0, total: 5 },
]

type JobsFile = { schema: string; jobs: JobRecord[] }

function nowIso(): string {
  return new Date().toISOString()
}

function pidAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

function jobsPath(root: LauncherRoot): string {
  return join(root.path, 'jobs.json')
}

function lockPath(root: LauncherRoot, instanceId: string): string {
  return join(root.path, 'instances', instanceId, '.job.lock.json')
}

function readJobs(root: LauncherRoot): JobsFile {
  const path = jobsPath(root)
  if (!existsSync(path)) return { schema: 'pack-agent.launcher.jobs/v1', jobs: [] }
  return JSON.parse(readFileSync(path, 'utf8')) as JobsFile
}

function writeJobs(root: LauncherRoot, file: JobsFile): void {
  const path = jobsPath(root)
  mkdirSync(dirname(path), { recursive: true })
  const tmp = `${path}.${process.pid}.tmp`
  writeFileSync(tmp, JSON.stringify(file, null, 2))
  rmSync(path, { force: true })
  renameSync(tmp, path)
}

function upsertJob(root: LauncherRoot, rec: JobRecord): void {
  const file = readJobs(root)
  file.jobs = file.jobs.filter((j) => j.id !== rec.id)
  file.jobs.push(rec)
  writeJobs(root, file)
}

export function listJobs(root: LauncherRoot): JobRecord[] {
  return readJobs(root).jobs
}

export function acquireInstanceLock(root: LauncherRoot, instanceId: string, jobId: string): JobRecord {
  const lock = lockPath(root, instanceId)
  mkdirSync(dirname(lock), { recursive: true })
  if (existsSync(lock)) {
    const cur = JSON.parse(readFileSync(lock, 'utf8')) as { jobId: string; pid: number }
    if (pidAlive(cur.pid)) {
      throw pa('PA008', 'instance directory lock is held', `instance \`${instanceId}\``, {
        note: `holding job ${cur.jobId}`,
        help: [`packagent dsh launcher job cancel ${cur.jobId}`],
      })
    }
    rmSync(lock, { force: true })
  }
  const rec: JobRecord = {
    id: jobId,
    kind: jobId.replace(/-\d+$/, ''),
    instance: instanceId,
    status: 'running',
    pid: process.pid,
    createdAt: nowIso(),
    updatedAt: nowIso(),
  }
  writeFileSync(lock, JSON.stringify({ jobId, pid: process.pid, acquiredAt: rec.createdAt }, null, 2), { flag: 'wx' })
  upsertJob(root, rec)
  return rec
}

export function releaseInstanceLock(root: LauncherRoot, instanceId: string, jobId: string, status: JobRecord['status'] = 'done'): void {
  const lock = lockPath(root, instanceId)
  if (existsSync(lock)) {
    try {
      const cur = JSON.parse(readFileSync(lock, 'utf8')) as { jobId: string }
      if (cur.jobId === jobId) rmSync(lock, { force: true })
    } catch {
      rmSync(lock, { force: true })
    }
  }
  const file = readJobs(root)
  const row = file.jobs.find((j) => j.id === jobId)
  if (row) {
    row.status = status
    row.updatedAt = nowIso()
    writeJobs(root, file)
  }
}

export function cancelJob(root: LauncherRoot, jobId: string): JobRecord {
  const file = readJobs(root)
  const row = file.jobs.find((j) => j.id === jobId)
  if (!row) throw new LauncherError(`job \`${jobId}\` not found`)
  if (row.instance) {
    const lock = lockPath(root, row.instance)
    if (existsSync(lock)) {
      try {
        const cur = JSON.parse(readFileSync(lock, 'utf8')) as { jobId: string }
        if (cur.jobId === jobId) rmSync(lock, { force: true })
      } catch {
        rmSync(lock, { force: true })
      }
    }
  }
  row.status = 'cancelled'
  row.updatedAt = nowIso()
  writeJobs(root, file)
  return row
}

export function withInstanceLock<T>(root: LauncherRoot, instanceId: string, kind: string, fn: () => T): T {
  const jobId = `${kind}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`
  acquireInstanceLock(root, instanceId, jobId)
  try {
    const value = fn()
    releaseInstanceLock(root, instanceId, jobId, 'done')
    return value
  } catch (e) {
    releaseInstanceLock(root, instanceId, jobId, 'failed')
    throw e
  }
}

export function createProgressJob(
  root: LauncherRoot,
  opts: { kind: string; leaves: JobLeaf[]; instance?: string },
): JobRecord {
  const rec: JobRecord = {
    id: `${opts.kind}-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`,
    kind: opts.kind,
    instance: opts.instance,
    status: 'running',
    pid: process.pid,
    createdAt: nowIso(),
    updatedAt: nowIso(),
    leaves: opts.leaves.map((l) => ({ ...l })),
  }
  upsertJob(root, rec)
  return rec
}

export function reportLeafProgress(
  root: LauncherRoot,
  jobId: string,
  leafId: string,
  progress: { done: number; total: number },
): JobRecord {
  const file = readJobs(root)
  const row = file.jobs.find((j) => j.id === jobId)
  if (!row) throw new LauncherError(`job \`${jobId}\` not found`)
  if (!row.leaves) row.leaves = []
  const leaf = row.leaves.find((l) => l.id === leafId)
  if (!leaf) throw new LauncherError(`job leaf \`${leafId}\` not found`)
  leaf.done = progress.done
  leaf.total = progress.total || leaf.total
  row.updatedAt = nowIso()
  writeJobs(root, file)
  return row
}

export function jobPercent(root: LauncherRoot, jobId: string): number {
  const row = listJobs(root).find((j) => j.id === jobId)
  if (!row?.leaves?.length) return row?.status === 'done' ? 100 : 0
  const weightSum = row.leaves.reduce((s, l) => s + l.weight, 0) || 1
  const acc = row.leaves.reduce((s, l) => s + l.weight * (l.total ? l.done / l.total : 0), 0)
  return (acc / weightSum) * 100
}
