/**
 * npm packument for `@deepseek-ai/dsh`. PAD lists every published release.
 * `0.0.1-rc.1` is the in-box latest trap; it is not a usable DSH pin.
 */

export type DshReleaseRow = {
  version: string
  time?: string
  latest: boolean
}

export type DshCatalog = {
  latest: string | null
  versions: DshReleaseRow[]
}

export function bannedRelease(version: string): boolean {
  return version === '0.0.1-rc.1'
}

export function parseDshPackument(json: unknown): DshCatalog {
  if (json == null || typeof json !== 'object') return { latest: null, versions: [] }
  const obj = json as Record<string, unknown>
  const raw = obj.versions
  const keys: string[] = []
  if (raw && typeof raw === 'object' && !Array.isArray(raw)) {
    for (const key of Object.keys(raw)) {
      if (!bannedRelease(key)) keys.push(key)
    }
  }
  keys.reverse()

  let latest: string | null = null
  const tags = obj['dist-tags']
  if (tags && typeof tags === 'object' && tags !== null && 'latest' in tags) {
    const tag = (tags as { latest?: unknown }).latest
    if (typeof tag === 'string' && !bannedRelease(tag)) latest = tag
  }

  const time = obj.time && typeof obj.time === 'object' && obj.time !== null
    ? obj.time as Record<string, unknown>
    : {}

  const versions: DshReleaseRow[] = keys.map((version) => ({
    version,
    time: typeof time[version] === 'string' ? time[version] : undefined,
    latest: latest !== null && version === latest,
  }))

  if (latest === null && versions[0]) {
    versions[0] = { ...versions[0], latest: true }
    latest = versions[0].version
  }

  return { latest, versions }
}
