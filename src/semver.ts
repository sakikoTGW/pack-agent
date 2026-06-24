/**
 * 轻量 semver 解析（skill requires: other@^1.0）
 */
export type Semver = { major: number; minor: number; patch: number; prerelease?: string }

export function parseSemver(raw: string): Semver | null {
  const m = raw.trim().match(/^(\d+)\.(\d+)\.(\d+)(?:-([\w.-]+))?/)
  if (!m) return null
  return {
    major: +m[1],
    minor: +m[2],
    patch: +m[3],
    prerelease: m[4],
  }
}

/** 从 "brainstorming@^1.0.0" 解析 */
export function parseRequiresEntry(entry: string): { name: string; range: string } | null {
  const s = entry.trim()
  if (!s) return null
  const at = s.lastIndexOf('@')
  if (at <= 0) return { name: s, range: '*' }
  return { name: s.slice(0, at), range: s.slice(at + 1) || '*' }
}

export function satisfiesVersion(version: string, range: string): boolean {
  if (range === '*' || range === '') return true
  const v = parseSemver(version.replace(/^0\.0\.0\+.*/, '0.0.0'))
  if (!v) return true

  if (range.startsWith('^')) {
    const r = parseSemver(range.slice(1))
    if (!r) return true
    if (v.major !== r.major) return false
    if (v.major === 0) return v.minor > r.minor || (v.minor === r.minor && v.patch >= r.patch)
    return v.minor > r.minor || (v.minor === r.minor && v.patch >= r.patch)
  }
  if (range.startsWith('~')) {
    const r = parseSemver(range.slice(1))
    if (!r) return true
    return v.major === r.major && v.minor === r.minor && v.patch >= r.patch
  }
  if (range.startsWith('>=')) {
    const r = parseSemver(range.slice(2))
    if (!r) return true
    return compareSemver(v, r) >= 0
  }
  const exact = parseSemver(range)
  if (exact) return v.major === exact.major && v.minor === exact.minor && v.patch === exact.patch
  return true
}

function compareSemver(a: Semver, b: Semver): number {
  if (a.major !== b.major) return a.major - b.major
  if (a.minor !== b.minor) return a.minor - b.minor
  return a.patch - b.patch
}

export function parseRequiresList(raw: string | undefined): string[] {
  if (!raw?.trim()) return []
  const t = raw.trim()
  if (t.startsWith('[')) {
    try {
      const arr = JSON.parse(t.replace(/'/g, '"')) as unknown
      if (Array.isArray(arr)) return arr.map(String)
    } catch {
      /* fall through */
    }
  }
  return t.split(/[,;\s]+/).map(s => s.trim()).filter(Boolean)
}
