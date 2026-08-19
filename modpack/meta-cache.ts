/**
 * library/meta TTL 缓存。列表页读缓存；过期后台刷新，主流程可不等待。
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import type { LauncherRoot } from './launcher.js'

export type MetaEnvelope<T = unknown> = {
  fetchedAt: number
  ttlSec: number
  body: T
}

export type MetaFetch = () => unknown | Promise<unknown>

const DEFAULT_TTL = 3600

export function metaDir(root: LauncherRoot): string {
  return join(root.path, 'library', 'meta')
}

export function metaPath(root: LauncherRoot, key: string): string {
  return join(metaDir(root), `${key}.json`)
}

export function getMeta<T>(root: LauncherRoot, key: string): MetaEnvelope<T> | null {
  const path = metaPath(root, key)
  if (!existsSync(path)) return null
  try {
    return JSON.parse(readFileSync(path, 'utf8')) as MetaEnvelope<T>
  } catch {
    return null
  }
}

export function putMeta<T>(root: LauncherRoot, key: string, body: T, fetchedAt: number, ttlSec = DEFAULT_TTL): MetaEnvelope<T> {
  mkdirSync(metaDir(root), { recursive: true })
  const env: MetaEnvelope<T> = { fetchedAt, ttlSec, body }
  writeFileSync(metaPath(root, key), JSON.stringify(env, null, 2))
  return env
}

export function metaFresh<T>(env: MetaEnvelope<T>, now: number): boolean {
  return now - env.fetchedAt < env.ttlSec * 1000
}

function asVersionList(body: unknown): string[] {
  if (Array.isArray(body)) return body.map(String)
  if (body && typeof body === 'object') {
    const row = body as { versions?: unknown }
    if (Array.isArray(row.versions)) return row.versions.map(String)
    if (row.versions && typeof row.versions === 'object' && !Array.isArray(row.versions)) {
      return Object.keys(row.versions as Record<string, unknown>)
    }
  }
  return []
}

export async function refreshMeta(
  root: LauncherRoot,
  key: string,
  opts: { fetch: MetaFetch; now?: number; ttlSec?: number },
): Promise<unknown> {
  const body = await opts.fetch()
  putMeta(root, key, body, opts.now ?? Date.now(), opts.ttlSec ?? DEFAULT_TTL)
  return body
}

export async function publishedDshVersions(
  root: LauncherRoot,
  opts: { fetch?: MetaFetch; now?: number; wait?: boolean; ttlSec?: number } = {},
): Promise<string[]> {
  const now = opts.now ?? Date.now()
  const hit = getMeta<{ versions?: string[] } | string[]>(root, 'dsh-versions')
  if (hit && metaFresh(hit, now)) return asVersionList(hit.body)
  if (hit && opts.wait === false) return asVersionList(hit.body)
  if (!opts.fetch && hit) return asVersionList(hit.body)
  if (!opts.fetch) return []
  const body = await opts.fetch()
  const versions = asVersionList(body)
  putMeta(root, 'dsh-versions', { versions }, now, opts.ttlSec ?? DEFAULT_TTL)
  return versions
}
