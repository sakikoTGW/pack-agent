/**
 * 经验罐头 → 会话注入文本（读 `.agent-pack/experiences/` 契约）。
 *
 * 消费者各自适配：Claude Code hooks、CCUI SessionStart / appendSystemPrompt 等。
 * 本模块属于 agent-pack 库，不依赖 CCUI/GUI。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import type { PackExperience } from './types.js'
import type { ExperienceIndex } from './experience.js'

export type ExperienceInjection = {
  systemPromptDelta: string
  reminders: string[]
  experienceIds: string[]
}

function formatExperienceBlock(exp: PackExperience): string {
  const parts: string[] = []
  const w = exp.offset?.weight ?? 1
  const header = `[Experience: ${exp.name ?? exp.id}${exp.version ? ` v${exp.version}` : ''}]`
  parts.push(header)

  const prompt = exp.harness?.base_system_prompt?.trim()
  if (prompt) parts.push(prompt)

  const delta = exp.offset?.promptDelta?.trim()
  if (delta) parts.push(delta)

  const reminders = [...(exp.harness?.system_reminders ?? []), ...(exp.offset?.reminders ?? [])]
  if (reminders.length) {
    parts.push('Reminders:\n' + reminders.map(r => `- ${r}`).join('\n'))
  }

  if (w !== 1 && w > 0) {
    parts.push(`(experience weight: ${w})`)
  }
  return parts.join('\n\n')
}

export async function loadExperienceInjection(
  cwd: string,
  stateDir = '.agent-pack',
): Promise<ExperienceInjection> {
  const indexPath = join(cwd, stateDir, 'experiences', 'index.json')
  const empty: ExperienceInjection = { systemPromptDelta: '', reminders: [], experienceIds: [] }

  let index: ExperienceIndex
  try {
    index = JSON.parse(await fs.readFile(indexPath, 'utf8')) as ExperienceIndex
  } catch {
    return empty
  }

  if (index.deliver !== 'experience' || !index.experiences?.length) return empty

  const blocks: string[] = []
  const ids: string[] = []
  const reminders: string[] = []

  for (const entry of index.experiences) {
    const safe = entry.id.replace(/[^\w.-]+/g, '_')
    const expPath = join(cwd, stateDir, 'experiences', `${safe}.exp.json`)
    try {
      const exp = JSON.parse(await fs.readFile(expPath, 'utf8')) as PackExperience
      const w = exp.offset?.weight ?? 1
      if (w <= 0) continue
      blocks.push(formatExperienceBlock(exp))
      ids.push(exp.id)
      reminders.push(...(exp.offset?.reminders ?? []), ...(exp.harness?.system_reminders ?? []))
    } catch {
      /* skip missing */
    }
  }

  if (!blocks.length) return empty

  const systemPromptDelta = [
    '--- agent-pack experiences (session injection, not skills) ---',
    ...blocks,
    '--- end agent-pack experiences ---',
  ].join('\n\n')

  return { systemPromptDelta, reminders: [...new Set(reminders)], experienceIds: ids }
}

export function mergeSystemPromptWithExperiences(
  base: string | undefined,
  injection: ExperienceInjection,
): string | undefined {
  if (!injection.systemPromptDelta.trim()) return base
  if (!base?.trim()) return injection.systemPromptDelta
  return `${base.trim()}\n\n${injection.systemPromptDelta}`
}
