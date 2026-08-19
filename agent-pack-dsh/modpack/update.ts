/**
 * 启动器自更新：通道 + 整包替换到 library/updates/，不改 git 工作树。
 */
import { cpSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import semver from 'semver'
import type { LauncherRoot } from './launcher.js'

export type UpdateChannel = 'stable' | 'dev'

export type UpdateCheck = {
  current: string
  latest: string | null
  channel: UpdateChannel
  updateAvailable: boolean
  published: string[]
}

export function pickLatest(published: string[], channel: UpdateChannel): string | null {
  const pool = channel === 'stable' ? published.filter((v) => !semver.prerelease(v)) : published
  const valid = pool.filter((v) => semver.valid(v))
  if (!valid.length) return null
  return semver.rsort(valid)[0] || null
}

export function checkUpdate(
  root: LauncherRoot,
  opts: { current: string; published: string[]; channel?: UpdateChannel },
): UpdateCheck {
  void root
  const channel = opts.channel || 'stable'
  const latest = pickLatest(opts.published, channel)
  const updateAvailable = Boolean(latest && semver.valid(opts.current) && semver.gt(latest, opts.current))
  return { current: opts.current, latest, channel, updateAvailable, published: opts.published }
}

export function applyUpdate(root: LauncherRoot, opts: { version: string; sourceDir: string }): { dir: string; version: string } {
  const dir = join(root.path, 'library', 'updates', opts.version)
  mkdirSync(dir, { recursive: true })
  if (!existsSync(opts.sourceDir)) throw new Error(`update source missing: ${opts.sourceDir}`)
  cpSync(opts.sourceDir, dir, { recursive: true })
  const current = join(root.path, 'library', 'updates', 'current.json')
  writeFileSync(
    current,
    JSON.stringify({ schema: 'pack-agent.launcher.update/v1', version: opts.version, dir, appliedAt: new Date().toISOString() }, null, 2),
  )
  return { dir, version: opts.version }
}

export function readAppliedUpdate(root: LauncherRoot): { version: string; dir: string } | null {
  const path = join(root.path, 'library', 'updates', 'current.json')
  if (!existsSync(path)) return null
  return JSON.parse(readFileSync(path, 'utf8')) as { version: string; dir: string }
}
