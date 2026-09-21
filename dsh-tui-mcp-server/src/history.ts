const DROP_TYPE = /available_skills|tree-truncated|request\/header|request\/context|agent\/inbox\/spliced|permission\/preset|sandbox\/mode|approval\/policy/i
const MAX_TEXT = 2000
const MAX_DUMP = 16_000

export type CompactEvent = {
  type: string
  seq?: number
  time?: number
  text?: string
}

export type CompactHistory = {
  hasMore: boolean
  droppedTypes: string[]
  events: CompactEvent[]
}

export function compactHistory(page: unknown): CompactHistory {
  const root = (page && typeof page === 'object' ? page : {}) as {
    hasMore?: unknown
    events?: unknown
  }
  const droppedTypes: string[] = []
  const events: CompactEvent[] = []
  const rows = Array.isArray(root.events) ? root.events : []
  for (const row of rows) {
    const ev = unwrapEvent(row)
    if (!ev) continue
    const type = String(ev.type ?? '')
    if (DROP_TYPE.test(type)) {
      if (!droppedTypes.includes(type)) droppedTypes.push(type)
      continue
    }
    events.push({
      type,
      seq: typeof ev.seq === 'number' ? ev.seq : undefined,
      time: typeof ev.time === 'number' ? ev.time : undefined,
      text: extractText(ev.data),
    })
  }
  const out: CompactHistory = {
    hasMore: root.hasMore === true,
    droppedTypes,
    events,
  }
  const dumped = JSON.stringify(out)
  if (dumped.length > MAX_DUMP) {
    return {
      ...out,
      events: events.slice(-12),
    }
  }
  return out
}

function unwrapEvent(row: unknown): { type?: unknown; seq?: unknown; time?: unknown; data?: unknown } | null {
  if (!row || typeof row !== 'object') return null
  const r = row as { event?: unknown }
  if (r.event && typeof r.event === 'object') return r.event as { type?: unknown; seq?: unknown; time?: unknown; data?: unknown }
  return row as { type?: unknown; seq?: unknown; time?: unknown; data?: unknown }
}

export function extractText(data: unknown): string | undefined {
  if (data == null) return undefined
  if (typeof data === 'string') return clip(stripSkills(data), MAX_TEXT)
  if (typeof data !== 'object') return clip(String(data), MAX_TEXT)
  const d = data as Record<string, unknown>
  const fromParts = fromContent(d.content)
  if (fromParts) return clip(stripSkills(fromParts), MAX_TEXT)
  if (typeof d.message === 'string' && d.message.length > 0) return clip(stripSkills(d.message), MAX_TEXT)
  if (typeof d.text === 'string' && d.text.length > 0) return clip(stripSkills(d.text), MAX_TEXT)
  try {
    return clip(stripSkills(JSON.stringify(d)), MAX_TEXT)
  } catch {
    return undefined
  }
}

function fromContent(content: unknown): string | undefined {
  if (!Array.isArray(content)) return undefined
  const parts: string[] = []
  for (const c of content) {
    if (!c || typeof c !== 'object') continue
    const p = c as { type?: unknown; text?: unknown }
    if (p.type === 'text' && typeof p.text === 'string') parts.push(p.text)
  }
  if (parts.length === 0) return undefined
  return parts.join('\n')
}

function stripSkills(s: string): string {
  return s.replace(/<available_skills>[\s\S]*?<\/available_skills>/g, '[available_skills omitted]')
}

function clip(s: string, n: number): string {
  return s.length <= n ? s : `${s.slice(0, n)}…(${s.length} chars)`
}
