/**
 * 按 sid 全根扫描。读 jsonl 第一帧。owner.json 写锁 PA003。
 */
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, realpathSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { encodeSegment } from './project-key.js'
import {
  getInstance,
  pa,
  sameHomePath,
  type Diagnostic,
  type LauncherRoot,
} from './launcher.js'

export type SessionRow = {
  id: string
  projectKey: string
  cwd?: string
  formatVersion?: number
  dshVersion?: string
  path: string
}

export type OpResult<T> = { value: T; warnings: Diagnostic[] }

type SidLock = { instance: string; pid: number; dshVersion: string; acquiredAt: string }

function pidAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

function decodeSessionBytes(buf: Buffer): string {
  if (buf.length >= 4 && buf[0] === 0x28 && buf[1] === 0xb5 && buf[2] === 0x2f && buf[3] === 0xfd) {
    const zlib = require('node:zlib') as { zstdDecompressSync?: (b: Buffer) => Buffer }
    if (typeof zlib.zstdDecompressSync === 'function') {
      return zlib.zstdDecompressSync(buf).toString('utf8')
    }
    throw new Error('zstd session log requires node zlib.zstdDecompressSync')
  }
  return buf.toString('utf8')
}

function firstJsonLine(text: string): Record<string, unknown> | null {
  const line = text.split(/\r?\n/).find((l) => l.trim())
  if (!line) return null
  try {
    return JSON.parse(line) as Record<string, unknown>
  } catch {
    return null
  }
}

type Found = { projectKey: string; dir: string; log: string; encodedSid: string; header: Record<string, unknown> }

function walkSessions(home: string): Found[] {
  const root = join(home, 'sessions')
  if (!existsSync(root)) return []
  const out: Found[] = []
  for (const proj of readdirSync(root, { withFileTypes: true })) {
    if (!proj.isDirectory()) continue
    const projPath = join(root, proj.name)
    for (const sidEnt of readdirSync(projPath, { withFileTypes: true })) {
      if (!sidEnt.isDirectory()) continue
      const dir = join(projPath, sidEnt.name)
      const log = ['session.jsonl', 'session.jsonl.zstd'].map((n) => join(dir, n)).find((p) => existsSync(p))
      if (!log) continue
      const header = firstJsonLine(decodeSessionBytes(readFileSync(log))) || {}
      out.push({ projectKey: proj.name, dir, log, encodedSid: sidEnt.name, header })
    }
  }
  return out
}

export function hasSessionArtifacts(home: string): boolean {
  return walkSessions(home).length > 0
}

function sessionFormatVersion(root: LauncherRoot, dshVersion: string): number {
  const path = join(root.path, 'versions', dshVersion, 'version.json')
  if (!existsSync(path)) return 0
  const rec = JSON.parse(readFileSync(path, 'utf8')) as { sessionFormatVersion?: number }
  return typeof rec.sessionFormatVersion === 'number' ? rec.sessionFormatVersion : 0
}

function findSid(home: string, sid: string): Found | undefined {
  const encoded = encodeSegment(sid)
  return walkSessions(home).find((s) => {
    const hid = typeof s.header.id === 'string' ? s.header.id : ''
    return hid === sid || s.encodedSid === encoded || s.encodedSid === sid
  })
}

function warn(code: string, message: string, location: string, extra: Partial<Diagnostic> = {}): Diagnostic {
  return { code, level: 'warning', message, location, ...extra }
}

export function listSessions(root: LauncherRoot, id: string): OpResult<SessionRow[]> {
  const inst = getInstance(root, id)
  const found = walkSessions(inst.home)
  const warnings: Diagnostic[] = []
  const byCwd = new Map<string, Set<string>>()
  const value: SessionRow[] = found.map((s) => {
    const cwd = typeof s.header.cwd === 'string' ? s.header.cwd : undefined
    if (cwd) {
      let key = cwd
      try {
        if (existsSync(cwd)) key = realpathSync(cwd)
      } catch {
        /* keep */
      }
      if (process.platform === 'win32') key = key.toLowerCase()
      if (!byCwd.has(key)) byCwd.set(key, new Set())
      byCwd.get(key)!.add(s.projectKey)
    }
    return {
      id: typeof s.header.id === 'string' ? s.header.id : s.encodedSid,
      projectKey: s.projectKey,
      cwd,
      formatVersion: typeof s.header.version === 'number' ? s.header.version : undefined,
      dshVersion: typeof s.header.dshVersion === 'string' ? s.header.dshVersion : undefined,
      path: s.log,
    }
  })
  for (const [cwd, slugs] of byCwd) {
    if (slugs.size > 1) {
      warnings.push(
        warn('PA108', 'two projectKey directories resolve to the same cwd', `instance \`${id}\` / sessions`, {
          context: [`cwd: ${cwd}`, `slugs: ${[...slugs].join(', ')}`],
          help: ['normalize the workspace path'],
        }),
      )
    }
  }
  return { value, warnings }
}

export function inspectSession(root: LauncherRoot, id: string, sid: string): OpResult<SessionRow> {
  const inst = getInstance(root, id)
  const found = findSid(inst.home, sid)
  if (!found) throw pa('PA007', `session \`${sid}\` not found`, `instance \`${id}\` / sessions`)
  const cwd = typeof found.header.cwd === 'string' ? found.header.cwd : undefined
  const formatVersion = typeof found.header.version === 'number' ? found.header.version : undefined
  const expected = sessionFormatVersion(root, inst.dsh.version)
  if (typeof formatVersion === 'number' && formatVersion !== expected) {
    throw pa('PA015', 'session format version does not match the pinned release', `session \`${sid}\``, {
      context: [`session format: ${formatVersion}`, `pinned sessionFormatVersion: ${expected}`],
      help: ['pin a release with the same format version', 'do not open this session'],
    })
  }
  if (cwd === undefined) {
    throw pa('PA007', 'DSH refused to open this session (cwd mismatch or not a directory)', `session \`${sid}\``, {
      note: 'header has no cwd',
      help: ['do not open this session'],
    })
  }
  if (!existsSync(cwd)) {
    throw pa('PA007', 'DSH refused to open this session (cwd mismatch or not a directory)', `session \`${sid}\``, {
      context: [`header.cwd: ${cwd}`],
      help: ['restore the workspace path in header.cwd', 'do not open this session'],
    })
  }
  if (!sameHomePath(cwd, inst.workspace.path)) {
    throw pa('PA007', 'DSH refused to open this session (cwd mismatch or not a directory)', `session \`${sid}\``, {
      context: [`header.cwd: ${cwd}`, `workspace: ${inst.workspace.path}`],
      help: ['restore the workspace path in header.cwd', 'do not open this session'],
    })
  }
  const warnings: Diagnostic[] = []
  const written = typeof found.header.dshVersion === 'string' ? found.header.dshVersion : undefined
  if (written && written !== inst.dsh.version) {
    warnings.push(
      warn('PA102', 'session last-writer dsh version differs from the pinned release', `session \`${sid}\``, {
        context: [`written: ${written}`, `pinned: ${inst.dsh.version}`],
        note: 'format is compatible; behavior may drift',
      }),
    )
  }
  return {
    value: {
      id: typeof found.header.id === 'string' ? found.header.id : sid,
      projectKey: found.projectKey,
      cwd,
      formatVersion,
      dshVersion: written,
      path: found.log,
    },
    warnings,
  }
}

export function acquireSidLock(root: LauncherRoot, id: string, sid: string): SidLock {
  const inst = getInstance(root, id)
  const found = findSid(inst.home, sid)
  if (!found) throw pa('PA007', `session \`${sid}\` not found`, `instance \`${id}\` / sessions`)
  const lock = join(found.dir, 'owner.json')
  const body: SidLock = {
    instance: id,
    pid: process.pid,
    dshVersion: inst.dsh.version,
    acquiredAt: new Date().toISOString(),
  }
  if (existsSync(lock)) {
    const cur = JSON.parse(readFileSync(lock, 'utf8')) as SidLock
    if (pidAlive(cur.pid)) {
      throw pa('PA003', 'session id already has a writable process in this DSH_HOME', `session \`${sid}\``, {
        note: `holder pid ${cur.pid} dsh ${cur.dshVersion}`,
        help: ['stop that process'],
      })
    }
    writeFileSync(lock, JSON.stringify(body, null, 2))
    return body
  }
  writeFileSync(lock, JSON.stringify(body, null, 2), { flag: 'wx' })
  return body
}

function sqliteEnabled(home: string): boolean {
  return existsSync(join(home, 'session-query.sqlite'))
}

export function deleteSession(root: LauncherRoot, id: string, sid: string): OpResult<{ removed: string }> {
  const inst = getInstance(root, id)
  const found = findSid(inst.home, sid)
  if (!found) throw pa('PA007', `session \`${sid}\` not found`, `instance \`${id}\` / sessions`)
  const warnings: Diagnostic[] = []
  if (sqliteEnabled(inst.home)) {
    warnings.push(
      warn('PA111', 'session deleted but full-text index may remain', `session \`${sid}\``, {
        help: ['rebuild the session-query-sqlite index'],
      }),
    )
  }
  rmSync(found.dir, { recursive: true, force: true })
  return { value: { removed: sid }, warnings }
}

export function backupSessions(root: LauncherRoot, id: string, sid?: string): { path: string } {
  const inst = getInstance(root, id)
  const dest = join(root.path, 'instances', id, 'backups', `sessions-${Date.now()}`)
  mkdirSync(dest, { recursive: true })
  if (sid) {
    const found = findSid(inst.home, sid)
    if (!found) throw pa('PA007', `session \`${sid}\` not found`, `instance \`${id}\` / sessions`)
    mkdirSync(join(dest, 'sessions', found.projectKey), { recursive: true })
    cpSync(found.dir, join(dest, 'sessions', found.projectKey, found.encodedSid), { recursive: true })
  } else if (existsSync(join(inst.home, 'sessions'))) {
    cpSync(join(inst.home, 'sessions'), join(dest, 'sessions'), { recursive: true })
  }
  const att = join(inst.home, 'attachments')
  if (existsSync(att)) cpSync(att, join(dest, 'attachments'), { recursive: true })
  return { path: dest }
}
