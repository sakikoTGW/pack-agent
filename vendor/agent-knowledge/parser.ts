/**
 * Adapted from agent-knowledge (MIT) — https://github.com/keshrath/agent-knowledge
 * Session JSONL parser. Supports Claude Code (`type`) and Cursor Composer (`role`).
 */
import { readFileSync } from 'node:fs'

interface TextPart {
  type: string
  text?: string
  name?: string
  input?: unknown
}

function isTextPart(p: unknown): p is TextPart {
  return typeof p === 'object' && p !== null && 'type' in p
}

function extractText(content: unknown): string | null {
  if (typeof content === 'string') return content
  if (Array.isArray(content)) {
    const parts = content
      .filter((p: unknown) => typeof p === 'string' || (isTextPart(p) && p.type === 'text'))
      .map((p: unknown) => (typeof p === 'string' ? p : (p as TextPart).text))
      .filter(Boolean) as string[]
    return parts.length > 0 ? parts.join('\n') : null
  }
  return null
}

export interface SessionEntry {
  type?: string
  role?: string
  timestamp?: string
  sessionId?: string
  cwd?: string
  gitBranch?: string
  message?: { role?: string; content: unknown }
  content?: string
}

export interface SessionMessage {
  role: 'user' | 'assistant' | 'tool_use' | 'tool_result'
  content: string
  timestamp: string | null
}

export interface SessionMeta {
  startTime: string
  endTime: string
  cwd: string
  branch: string
  messageCount: number
  userMessageCount: number
  preview: string
}

export function parseSessionJsonl(raw: string): SessionEntry[] {
  const lines = raw.split('\n').filter(l => l.trim())
  const entries: SessionEntry[] = []
  for (const line of lines) {
    try {
      entries.push(JSON.parse(line) as SessionEntry)
    } catch {
      /* skip malformed */
    }
  }
  return entries
}

export function parseSessionFile(filePath: string): SessionEntry[] {
  try {
    return parseSessionJsonl(readFileSync(filePath, 'utf8'))
  } catch {
    return []
  }
}

function pushToolUse(messages: SessionMessage[], name: string, input: unknown, ts: string | null): void {
  const body = typeof input === 'object' && input !== null ? JSON.stringify(input) : String(input ?? name)
  messages.push({
    role: 'tool_use',
    content: `${name}: ${body}`.slice(0, 500),
    timestamp: ts,
  })
}

export function extractMessages(entries: SessionEntry[]): SessionMessage[] {
  const messages: SessionMessage[] = []

  for (const entry of entries) {
    const ts = entry.timestamp ?? null
    const entryType = entry.type ?? entry.role

    if (entryType === 'user' && entry.message?.content !== undefined) {
      const content = extractText(entry.message.content) ?? JSON.stringify(entry.message.content)
      messages.push({ role: 'user', content, timestamp: ts })
      continue
    }

    if (entryType === 'assistant' && entry.message?.content !== undefined) {
      const mc = entry.message.content
      if (Array.isArray(mc)) {
        for (const block of mc) {
          if (typeof block === 'string') {
            messages.push({ role: 'assistant', content: block, timestamp: ts })
            continue
          }
          if (!isTextPart(block)) continue
          if (block.type === 'text' && block.text) {
            messages.push({ role: 'assistant', content: block.text, timestamp: ts })
          } else if (block.type === 'tool_use' && block.name) {
            pushToolUse(messages, block.name, block.input, ts)
          }
        }
      } else {
        const content = extractText(mc)
        if (content) messages.push({ role: 'assistant', content, timestamp: ts })
      }
      continue
    }

    if (entryType === 'tool_use' || entryType === 'tool_result') {
      const content =
        typeof entry.content === 'string'
          ? entry.content
          : typeof entry.message?.content === 'string'
            ? entry.message.content
            : null
      if (content) {
        messages.push({
          role: entryType as 'tool_use' | 'tool_result',
          content: content.slice(0, 500),
          timestamp: ts,
        })
      }
    }
  }

  return messages
}

export function getSessionMeta(entries: SessionEntry[]): SessionMeta {
  if (entries.length === 0) {
    return {
      startTime: 'unknown',
      endTime: 'unknown',
      cwd: 'unknown',
      branch: 'unknown',
      messageCount: 0,
      userMessageCount: 0,
      preview: 'N/A',
    }
  }

  const firstWithTimestamp = entries.find(e => e.timestamp)
  const lastWithTimestamp = [...entries].reverse().find(e => e.timestamp)
  const first = firstWithTimestamp ?? entries[0]
  const last = lastWithTimestamp ?? entries[entries.length - 1]
  const userMessages = entries.filter(e => (e.type ?? e.role) === 'user')
  const firstUserMsg = userMessages[0]?.message?.content

  return {
    startTime: first?.timestamp ?? 'unknown',
    endTime: last?.timestamp ?? 'unknown',
    cwd: first?.cwd ?? 'unknown',
    branch: first?.gitBranch ?? 'unknown',
    messageCount: entries.length,
    userMessageCount: userMessages.length,
    preview: (extractText(firstUserMsg) ?? 'N/A').slice(0, 200),
  }
}

/** Strip Cursor `<user_query>` wrappers for cleaner topics. */
export function normalizeUserTopic(text: string): string {
  const m = text.match(/<user_query>\s*([\s\S]*?)\s*<\/user_query>/i)
  return (m?.[1] ?? text).trim()
}
