/**
 * 从允许集加载 skill。未允许的投影包不出现在 list()。
 */
import { readFile } from 'node:fs/promises'
import { catalogSnapshot } from '../../dsh-modpack/catalog.js'

export const CATALOG_PROVIDER_NAME = 'pack-agent-catalog'
export const CATALOG_SKILL_RANK = 150

export type SkillInvocationPolicy = {
  modelInvocable: boolean
  userInvocable: boolean
}

export type SkillCandidate = {
  name: string
  description: string
  invocation: SkillInvocationPolicy
  source: string
  provider: string
  rank: number
  locator: { pack_id: string; path: string }
  path?: string
}

export type SkillDefinition = {
  name: string
  description: string
  invocation: SkillInvocationPolicy
  source: string
  provider: string
  content: string
  path?: string
}

export type SkillProvider = {
  name: string
  list: (options?: { cwd?: string; signal?: AbortSignal }) => Promise<readonly SkillCandidate[]>
  get: (candidate: SkillCandidate, options?: { cwd?: string; signal?: AbortSignal }) => Promise<SkillDefinition | undefined>
}

function parseFrontmatter(raw: string): { description?: string; body: string } {
  if (!raw.startsWith('---')) return { body: raw }
  const end = raw.indexOf('\n---', 3)
  if (end < 0) return { body: raw }
  const fm = raw.slice(3, end)
  const body = raw.slice(end + 4).replace(/^\s*\n/, '')
  const description = fm.match(/^description:\s*(.+)$/m)?.[1]?.trim()
  return { description, body }
}

export function createCatalogSkillProvider(workspace: string): SkillProvider {
  return {
    name: CATALOG_PROVIDER_NAME,
    async list() {
      const snap = await catalogSnapshot(workspace)
      return snap.skills.map((s, i) => ({
        name: s.name,
        description: s.description || s.name,
        invocation: { modelInvocable: true, userInvocable: true },
        source: 'pack-agent',
        provider: CATALOG_PROVIDER_NAME,
        rank: CATALOG_SKILL_RANK + i,
        locator: { pack_id: s.pack_id, path: s.path },
        path: s.path,
      }))
    },
    async get(candidate) {
      const p = candidate.locator?.path || candidate.path
      if (!p) return undefined
      let raw: string
      try {
        raw = await readFile(p, 'utf8')
      } catch {
        return undefined
      }
      const parsed = parseFrontmatter(raw)
      return {
        name: candidate.name,
        description: parsed.description || candidate.description,
        invocation: { modelInvocable: true, userInvocable: true },
        source: 'pack-agent',
        provider: CATALOG_PROVIDER_NAME,
        content: parsed.body,
        path: p,
      }
    },
  }
}
