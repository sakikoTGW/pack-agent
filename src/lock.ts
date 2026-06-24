/**
 * lock 文件 — 记录已解析的 pack / skill / mcp / rule 版本（类似 package-lock）。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import type { PackDoc } from './types.js'
import { LOCK_SCHEMA } from './versioning.js'

export type PackLockComponent = {
  version: string
  contentHash?: string
  configHash?: string
  package?: string
  packageVersion?: string
  ref?: string
  fileCount?: number
}

export type PackLock = {
  schema: typeof LOCK_SCHEMA
  packName: string
  packVersion: string
  packContentHash?: string
  lockedAt: string
  agentPackCli?: string
    components: {
    skills: Record<string, PackLockComponent>
    rules: Record<string, PackLockComponent>
    mcp: Record<string, PackLockComponent>
    experiences: Record<string, PackLockComponent>
    hooks: Record<string, PackLockComponent>
    subagents: Record<string, PackLockComponent>
    memory: Record<string, PackLockComponent>
  }
  captureDeliver?: string
}

export function packToLock(pack: PackDoc): PackLock {
  const skills: Record<string, PackLockComponent> = {}
  for (const s of pack.knowledge?.skills ?? []) {
    const n = String(s.name || '')
    if (!n) continue
    skills[n] = {
      version: s.version ?? '0.0.0',
      contentHash: s.contentHash,
      ref: s.ref,
      fileCount: s.fileCount,
    }
  }

  const rules: Record<string, PackLockComponent> = {}
  for (const r of pack.knowledge?.rules ?? []) {
    const n = String(r.name || '')
    if (!n) continue
    rules[n] = {
      version: r.version ?? '0.0.0',
      contentHash: r.contentHash,
      ref: r.ref,
    }
  }

  const mcp: Record<string, PackLockComponent> = {}
  for (const m of pack.tools?.mcp ?? []) {
    const n = String(m.name || '')
    if (!n) continue
    mcp[n] = {
      version: m.version ?? '0.0.0',
      configHash: m.configHash,
      package: m.package,
      packageVersion: m.packageVersion,
    }
  }

  const experiences: Record<string, PackLockComponent> = {}
  for (const e of pack.experiences ?? []) {
    experiences[e.id] = {
      version: e.version ?? '0.0.0',
      contentHash: e.contentHash,
    }
  }

  const hooks: Record<string, PackLockComponent> = {}
  for (const h of pack.automation?.hooks ?? []) {
    const n = String(h.name || '')
    if (!n) continue
    hooks[n] = { version: '0.0.0', contentHash: h.contentHash, ref: h.ref }
  }

  const subagents: Record<string, PackLockComponent> = {}
  for (const a of pack.agents?.subagents ?? []) {
    const n = String(a.name || '')
    if (!n) continue
    subagents[n] = { version: '0.0.0', contentHash: a.contentHash, ref: a.ref }
  }

  const memory: Record<string, PackLockComponent> = {}
  for (const m of pack.memory?.files ?? []) {
    const n = String(m.name || '')
    if (!n) continue
    memory[n] = { version: '0.0.0', contentHash: m.contentHash, ref: m.ref }
  }

  return {
    schema: LOCK_SCHEMA,
    packName: pack.name ?? 'unnamed-pack',
    packVersion: pack.version ?? '0.1.0',
    packContentHash: pack.resolution?.packContentHash,
    lockedAt: pack.resolution?.lockedAt ?? new Date().toISOString(),
    agentPackCli: pack.resolution?.agentPackCli,
    captureDeliver: pack.policy?.captureAs,
    components: { skills, rules, mcp, experiences, hooks, subagents, memory },
  }
}

export async function writePackLock(cwd: string, pack: PackDoc, stateDir = '.agent-pack'): Promise<string> {
  const lock = packToLock(pack)
  const path = join(cwd, stateDir, 'lock.json')
  await fs.mkdir(join(cwd, stateDir), { recursive: true })
  await fs.writeFile(path, JSON.stringify(lock, null, 2), 'utf8')
  return path
}

export async function readPackLock(cwd: string, stateDir = '.agent-pack'): Promise<PackLock | null> {
  try {
    const raw = await fs.readFile(join(cwd, stateDir, 'lock.json'), 'utf8')
    return JSON.parse(raw) as PackLock
  } catch {
    return null
  }
}
