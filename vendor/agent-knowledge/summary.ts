/**
 * Adapted from agent-knowledge (MIT) — https://github.com/keshrath/agent-knowledge
 * Deterministic session summary (no LLM).
 */
import {
  extractMessages,
  getSessionMeta,
  normalizeUserTopic,
  parseSessionJsonl,
  type SessionEntry,
  type SessionMessage,
  type SessionMeta,
} from './parser.js'

export interface SessionSummary {
  meta: SessionMeta
  topicCount: number
  topics: Array<{ timestamp: string | null; content: string }>
  toolsUsed: string[]
  filesModified: string[]
  gitCommits: string[]
  errorPatterns: string[]
  urlsAccessed: string[]
  packagesChanged: string[]
  assistantHighlights: string[]
}

const FILE_PATH_RE =
  /(?:^|[\s"'`(])([./~]?(?:[\w.-]+\/)+[\w.-]+\.(?:ts|tsx|js|jsx|py|rs|go|java|vue|svelte|css|scss|html|json|yaml|yml|toml|md|txt|sh|sql|prisma|graphql|proto))\b/g

const TOOL_NAME_RE = /^(?:\w+(?:_\w+)*):|^(\w+(?:_\w+)*)/

function extractFilePaths(text: string): string[] {
  const paths = new Set<string>()
  FILE_PATH_RE.lastIndex = 0
  let match: RegExpExecArray | null
  while ((match = FILE_PATH_RE.exec(text)) !== null) {
    paths.add(match[1])
  }
  return [...paths]
}

function extractToolName(content: string): string | null {
  const match = content.match(TOOL_NAME_RE)
  if (!match) return null
  if (content.includes(':')) return content.split(':')[0]?.trim() ?? null
  return match[1] ?? match[0] ?? null
}

const GIT_COMMIT_RE = /\b([0-9a-f]{7,40})\b/g
const GIT_COMMIT_CONTEXT_RE = /(?:commit|merge|cherry-pick|revert|rebase|push|pull|checkout)\b/i

function extractGitCommits(messages: SessionMessage[]): string[] {
  const commits = new Set<string>()
  for (const msg of messages) {
    if (msg.role !== 'tool_result') continue
    if (!GIT_COMMIT_CONTEXT_RE.test(msg.content)) continue
    GIT_COMMIT_RE.lastIndex = 0
    let match: RegExpExecArray | null
    while ((match = GIT_COMMIT_RE.exec(msg.content)) !== null) {
      const sha = match[1]
      if (sha.length >= 7 && sha.length <= 40 && !/^0+$/.test(sha)) {
        commits.add(sha.slice(0, 7))
      }
    }
  }
  return [...commits].slice(0, 20)
}

const ERROR_LINE_RE = /^.*(?:Error|Exception|FAIL|FATAL|panic|Traceback)[:.\s].{10,200}/gm

function extractErrorPatterns(messages: SessionMessage[]): string[] {
  const errors = new Set<string>()
  for (const msg of messages) {
    if (msg.role !== 'tool_result') continue
    ERROR_LINE_RE.lastIndex = 0
    let match: RegExpExecArray | null
    while ((match = ERROR_LINE_RE.exec(msg.content)) !== null) {
      const line = match[0].trim()
      if (line.length > 10 && line.length < 200) errors.add(line)
      if (errors.size >= 10) break
    }
  }
  return [...errors]
}

const URL_RE = /https?:\/\/[^\s"'<>)\]]+/g
const URL_NOISE = /\.(png|jpg|jpeg|gif|svg|ico|woff|ttf|eot|map)(\?|$)|fonts\.googleapis|cdnjs|unpkg/i

function extractUrls(messages: SessionMessage[]): string[] {
  const urls = new Set<string>()
  for (const msg of messages) {
    if (msg.role !== 'tool_result' && msg.role !== 'tool_use') continue
    URL_RE.lastIndex = 0
    let match: RegExpExecArray | null
    while ((match = URL_RE.exec(msg.content)) !== null) {
      const url = match[0].replace(/[.,;:!?)]+$/, '')
      if (!URL_NOISE.test(url) && url.length < 200) urls.add(url)
      if (urls.size >= 15) break
    }
  }
  return [...urls]
}

const NPM_INSTALL_RE = /npm\s+(?:install|i|add)\s+([^\s&|;]+(?:\s+[^\s&|;-][^\s&|;]*)*)/g
const PIP_INSTALL_RE = /pip\s+install\s+([^\s&|;]+(?:\s+[^\s&|;-][^\s&|;]*)*)/g

function extractPackageChanges(messages: SessionMessage[]): string[] {
  const packages = new Set<string>()
  for (const msg of messages) {
    if (msg.role !== 'tool_use' && msg.role !== 'tool_result') continue
    for (const re of [NPM_INSTALL_RE, PIP_INSTALL_RE]) {
      re.lastIndex = 0
      let match: RegExpExecArray | null
      while ((match = re.exec(msg.content)) !== null) {
        const args = match[1].split(/\s+/).filter(a => !a.startsWith('-') && a.length > 0)
        for (const pkg of args) {
          packages.add(pkg.replace(/@[\d^~>=<.*]+$/, ''))
        }
      }
    }
  }
  return [...packages].slice(0, 20)
}

function isHumanMessage(text: string): boolean {
  const t = text.trimStart()
  if (t.startsWith('[{') || t.startsWith('{"')) return false
  if (t.includes('tool_use_id') || t.includes('tool_result')) return false
  if (t.includes('base64') || t.includes('media_type')) return false
  if (t.includes('<system_reminder>')) return false
  if (t.length < 3) return false
  return true
}

function assistantHighlights(messages: SessionMessage[], max = 8): string[] {
  const out: string[] = []
  for (const msg of messages) {
    if (msg.role !== 'assistant') continue
    const t = msg.content.trim()
    if (t.length < 40 || t === '[REDACTED]') continue
    if (t.startsWith('[') && t.length < 80) continue
    out.push(t.length > 400 ? `${t.slice(0, 400)}...` : t)
  }
  return out.slice(-max)
}

export function summarizeSessionEntries(entries: SessionEntry[]): SessionSummary | null {
  if (entries.length === 0) return null
  const meta = getSessionMeta(entries)
  const messages = extractMessages(entries)

  const topics = messages
    .filter(m => m.role === 'user')
    .filter(m => isHumanMessage(m.content))
    .map(m => ({
      timestamp: m.timestamp,
      content: (() => {
        const normalized = normalizeUserTopic(m.content)
        return normalized.length > 300 ? `${normalized.slice(0, 300)}...` : normalized
      })(),
    }))

  const toolNames = new Set<string>()
  for (const msg of messages) {
    if (msg.role === 'tool_use') {
      const name = extractToolName(msg.content)
      if (name) toolNames.add(name)
    }
  }

  const allFiles = new Set<string>()
  for (const msg of messages) {
    if (msg.role === 'tool_result' || msg.role === 'tool_use') {
      for (const fp of extractFilePaths(msg.content)) allFiles.add(fp)
    }
  }

  return {
    meta,
    topicCount: topics.length,
    topics,
    toolsUsed: [...toolNames].sort(),
    filesModified: [...allFiles].sort(),
    gitCommits: extractGitCommits(messages),
    errorPatterns: extractErrorPatterns(messages),
    urlsAccessed: extractUrls(messages),
    packagesChanged: extractPackageChanges(messages),
    assistantHighlights: assistantHighlights(messages),
  }
}

export function summarizeSessionJsonl(raw: string): SessionSummary | null {
  return summarizeSessionEntries(parseSessionJsonl(raw))
}

export function formatSummaryAsPrompt(summary: SessionSummary, sourceLabel: string): string {
  const { meta } = summary
  const lines: string[] = [
    `# Distilled session experience`,
    ``,
    `Source: ${sourceLabel}`,
    `Distiller: agent-knowledge deterministic pre-extraction (MIT, vendored)`,
    ``,
    `## Context`,
    `- cwd: ${meta.cwd}`,
    `- branch: ${meta.branch}`,
    `- period: ${meta.startTime} → ${meta.endTime}`,
    `- entries: ${meta.messageCount} (${meta.userMessageCount} user turns)`,
  ]

  if (summary.topics.length) {
    lines.push(``, `## User topics (${summary.topicCount})`)
    summary.topics.slice(0, 12).forEach((t, i) => {
      lines.push(`${i + 1}. ${t.content.replace(/\n/g, ' ')}`)
    })
  }

  if (summary.assistantHighlights.length) {
    lines.push(``, `## Assistant conclusions (excerpt)`)
    summary.assistantHighlights.forEach((h, i) => {
      lines.push(`${i + 1}. ${h.replace(/\n/g, ' ')}`)
    })
  }

  const listSection = (title: string, items: string[], limit = 20) => {
    if (!items.length) return
    lines.push(``, `## ${title}`)
    items.slice(0, limit).forEach(x => lines.push(`- ${x}`))
  }

  listSection('Tools used', summary.toolsUsed)
  listSection('Files touched', summary.filesModified, 30)
  listSection('Git commits', summary.gitCommits)
  listSection('Errors seen', summary.errorPatterns)
  listSection('Packages changed', summary.packagesChanged)
  listSection('URLs accessed', summary.urlsAccessed)

  return lines.join('\n')
}
