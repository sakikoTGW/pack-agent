/**
 * 经验罐头（Experience Can）— 蒸馏态 agent 行为，非 skill 固化。
 *
 * 与 skill 约束的分工（打包者/安装者可选）：
 * - skill：装进 .claude/skills 等，持久约束，可被检索
 * - experience：装进 .agent-pack/experiences/，会话态注入，可 offset，不污染 skills 树
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import type { CaptureDeliver, PackDoc, PackExperience } from './types.js'
import { sha256Full } from './versioning.js'

export type ExperienceInstallReport = {
  id: string
  path: string
  scope: string
  ttl?: string
}

export type ExperienceIndex = {
  schema: 'agent-pack/experience-index/v1'
  installedAt: string
  packName?: string
  deliver: CaptureDeliver
  experiences: Array<{ id: string; path: string; scope: string; ttl?: string; version?: string }>
}

export function resolveCaptureDeliver(pack: PackDoc, override?: CaptureDeliver): CaptureDeliver {
  if (override) return override
  if (pack.policy?.captureAs) return pack.policy.captureAs
  if ((pack.experiences?.length ?? 0) > 0) return 'experience'
  if (pack.harness?.base_system_prompt?.trim()) return 'skill'
  return 'experience'
}

export function captureDocToExperience(doc: Record<string, unknown>, id?: string): PackExperience | null {
  const harness = doc.harness as PackExperience['harness']
  const assembly = doc.assembly as PackExperience['assembly']
  const model = doc.model as PackExperience['model']
  const prompt = harness?.base_system_prompt?.trim()
  const hasBody = Boolean(prompt && prompt.length > 10) || (harness?.tool_schemas?.length ?? 0) > 0
  if (!hasBody && !assembly) return null

  const expId = id ?? String(doc.name ?? 'capture').replace(/[^\w.-]+/g, '_')
  const payload = JSON.stringify({ harness, assembly, model })
  return {
    id: expId,
    name: String(doc.name ?? expId),
    version: String(doc.version ?? '1.0.0'),
    kind: 'distill',
    scope: 'session',
    ttl: 'session',
    source: 'capture',
    contentHash: sha256Full(payload),
    harness,
    assembly,
    model,
    offset: { weight: 1.0 },
    meta: {
      fidelity: doc.meta && typeof doc.meta === 'object' ? (doc.meta as Record<string, unknown>).fidelity : 'L2',
      capturedAt: doc.meta && typeof doc.meta === 'object' ? (doc.meta as Record<string, unknown>).capturedAt : undefined,
    },
  }
}

export async function mergeExperiencesFromCapture(
  cwd: string,
  pack: PackDoc,
  stateDir = '.agent-pack',
): Promise<PackDoc> {
  const dirs = [join(cwd, stateDir, 'capture'), join(cwd, '.ccui', 'packs')]
  const list: PackExperience[] = [...(pack.experiences ?? [])]
  const seen = new Set(list.map(e => e.id))

  for (const dir of dirs) {
    let names: string[] = []
    try {
      names = (await fs.readdir(dir)).filter(n => n.endsWith('.json'))
    } catch {
      continue
    }
    for (const n of names) {
      try {
        const doc = JSON.parse(await fs.readFile(join(dir, n), 'utf8')) as Record<string, unknown>
        const exp = captureDocToExperience(doc, String(doc.name ?? n.replace(/\.(pack\.)?json$/, '')))
        if (!exp || seen.has(exp.id)) continue
        seen.add(exp.id)
        list.push(exp)
      } catch {
        /* skip */
      }
    }
  }

  if (!list.length) return pack
  return {
    ...pack,
    experiences: list,
    policy: { ...pack.policy, captureAs: 'experience' },
    harness: { base_system_prompt: '', tool_schemas: [], system_reminders: [] },
    meta: { ...pack.meta, experienceCount: list.length, deliver: 'experience' },
  }
}

export async function installExperiences(
  cwd: string,
  pack: PackDoc,
  stateDir = '.agent-pack',
): Promise<ExperienceInstallReport[]> {
  const list = pack.experiences ?? []
  if (!list.length) return []

  const root = join(cwd, stateDir, 'experiences')
  await fs.mkdir(root, { recursive: true })

  const reports: ExperienceInstallReport[] = []
  const indexEntries: ExperienceIndex['experiences'] = []

  for (const exp of list) {
    const id = exp.id.replace(/[^\w.-]+/g, '_')
    const path = join(root, `${id}.exp.json`)
    await fs.writeFile(
      path,
      JSON.stringify({ ...exp, installedAt: new Date().toISOString(), packName: pack.name, deliver: 'experience' }, null, 2),
      'utf8',
    )
    reports.push({ id, path, scope: exp.scope ?? 'session', ttl: exp.ttl })
    indexEntries.push({ id, path, scope: exp.scope ?? 'session', ttl: exp.ttl, version: exp.version })
  }

  const index: ExperienceIndex = {
    schema: 'agent-pack/experience-index/v1',
    installedAt: new Date().toISOString(),
    packName: pack.name,
    deliver: 'experience',
    experiences: indexEntries,
  }
  await fs.writeFile(join(root, 'index.json'), JSON.stringify(index, null, 2), 'utf8')
  return reports
}

export async function applyExperienceOffset(
  cwd: string,
  experienceId: string,
  offset: NonNullable<PackExperience['offset']>,
  stateDir = '.agent-pack',
): Promise<boolean> {
  const safe = experienceId.replace(/[^\w.-]+/g, '_')
  const path = join(cwd, stateDir, 'experiences', `${safe}.exp.json`)
  try {
    const exp = JSON.parse(await fs.readFile(path, 'utf8')) as PackExperience
    exp.offset = { ...(exp.offset ?? {}), ...offset, updatedAt: new Date().toISOString() }
    await fs.writeFile(path, JSON.stringify(exp, null, 2), 'utf8')
    return true
  } catch {
    return false
  }
}

export { distillTranscriptsToExperiences } from './transcript-distill.js'

export async function ejectExperiences(cwd: string, stateDir = '.agent-pack'): Promise<number> {
  const root = join(cwd, stateDir, 'experiences')
  let n = 0
  try {
    for (const f of await fs.readdir(root)) {
      if (f.endsWith('.json')) {
        await fs.rm(join(root, f))
        n++
      }
    }
  } catch {
    return 0
  }
  return n
}
