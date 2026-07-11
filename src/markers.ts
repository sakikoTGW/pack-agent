import { promises as fs } from 'node:fs'
import { dirname, join } from 'node:path'

export function packMarker(kind: string, id: string): string {
  return `agent-pack:${kind}:${id}`
}

function escapeRe(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

export async function appendMarkedBlock(file: string, marker: string, body: string): Promise<void> {
  const start = `<!-- >>> ${marker} >>> -->`
  const end = `<!-- <<< ${marker} <<< -->`
  let text = ''
  try {
    text = await fs.readFile(file, 'utf8')
  } catch {
    /* new file */
  }
  const re = new RegExp(`${escapeRe(start)}[\\s\\S]*?${escapeRe(end)}\\n?`, 'g')
  text = text.replace(re, '')
  const block = `${start}\n${body.trim()}\n${end}\n`
  await fs.mkdir(dirname(file), { recursive: true }).catch(() => {})
  await fs.writeFile(file, text.trimEnd() ? `${text.trimEnd()}\n\n${block}` : block, 'utf8')
}

/** 移除标记块；文件不存在 → missing */
export async function removeMarkedBlock(
  file: string,
  marker: string,
): Promise<'removed' | 'missing' | 'unchanged'> {
  let text: string
  try {
    text = await fs.readFile(file, 'utf8')
  } catch {
    return 'missing'
  }
  const start = `<!-- >>> ${marker} >>> -->`
  const end = `<!-- <<< ${marker} <<< -->`
  const re = new RegExp(`\\n?${escapeRe(start)}[\\s\\S]*?${escapeRe(end)}\\n?`, 'g')
  if (!re.test(text)) return 'unchanged'
  const next = text.replace(re, '').trimEnd()
  if (!next) {
    await fs.rm(file, { force: true })
    return 'removed'
  }
  await fs.writeFile(file, `${next}\n`, 'utf8')
  return 'removed'
}

export type AgentPackOriginMarker = {
  packName: string
  packVersion?: string
  skillName: string
  contentHash?: string
  installedAt: string
}

const ORIGIN_FILE = '.agent-pack-origin.json'

export async function writeSkillOriginMarker(
  skillDir: string,
  meta: Omit<AgentPackOriginMarker, 'installedAt'>,
): Promise<void> {
  const payload: AgentPackOriginMarker = { ...meta, installedAt: new Date().toISOString() }
  await fs.writeFile(join(skillDir, ORIGIN_FILE), JSON.stringify(payload, null, 2), 'utf8')
}

export async function readSkillOriginMarker(skillDir: string): Promise<AgentPackOriginMarker | null> {
  try {
    return JSON.parse(await fs.readFile(join(skillDir, ORIGIN_FILE), 'utf8')) as AgentPackOriginMarker
  } catch {
    return null
  }
}

export const SKILL_ORIGIN_FILE = ORIGIN_FILE
