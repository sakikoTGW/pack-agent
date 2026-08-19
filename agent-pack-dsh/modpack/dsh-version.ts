/**
 * 用 node-semver 把 dsh.version 精确号/range 解析到已发布发行号。
 */
import semver from 'semver'

/** Latest published @deepseek-ai/dsh this launcher defaults to. */
export const DEFAULT_DSH_VERSION = '0.1.0-rc.7'

export class DshVersionError extends Error {
  code: string
  constructor(code: string, message: string) {
    super(message)
    this.name = 'DshVersionError'
    this.code = code
  }
}

export function resolveDshVersion(spec: string, published: string[]): string {
  const trimmed = spec.trim()
  if (!trimmed) {
    throw new DshVersionError('PA012', 'pack is missing dsh.version, or that release is not on npm')
  }
  if (semver.valid(trimmed)) {
    if (!published.includes(trimmed)) {
      throw new DshVersionError('PA012', 'pack is missing dsh.version, or that release is not on npm')
    }
    return trimmed
  }
  if (!semver.validRange(trimmed)) {
    throw new DshVersionError('PA012', 'pack is missing dsh.version, or that release is not on npm')
  }
  const best = semver.maxSatisfying(published, trimmed)
  if (!best) {
    throw new DshVersionError('PA012', 'pack is missing dsh.version, or that release is not on npm')
  }
  return best
}
