#!/usr/bin/env node
'use strict'
/**
 * agent-pack experience hook — 主矛盾：经验进上下文且随对话变（非 skill 固化）。
 *
 * SessionStart / UserPromptSubmit / pre_llm_call：
 * - 读 .agent-pack/experiences/*.exp.json（基座罐头）
 * - 读 stdin 本轮信号，更新 experiences/live.json
 * - stdout 注入 = 基座 + live（live 随轮次变）
 */
const fs = require('fs')
const path = require('path')

const cwd = process.cwd()
const stateDir = process.env.AGENT_PACK_STATE_DIR || '.agent-pack'
const hookEvent = process.env.AGENT_PACK_HOOK_EVENT || 'SessionStart'
const expRoot = path.join(cwd, stateDir, 'experiences')
const indexPath = path.join(expRoot, 'index.json')
const livePath = path.join(expRoot, 'live.json')
const MAX_TURNS = 40
const MAX_PROMPT_CHARS = 500

function readStdinSync() {
  try {
    return fs.readFileSync(0, 'utf8')
  } catch {
    return ''
  }
}

function parsePayload(raw) {
  const t = String(raw || '').trim()
  if (!t) return {}
  try {
    return JSON.parse(t)
  } catch {
    return { prompt: t.slice(0, MAX_PROMPT_CHARS) }
  }
}

function extractPrompt(payload) {
  const direct = String(payload.prompt || '').trim()
  if (direct) return direct.slice(0, MAX_PROMPT_CHARS)
  const extra = payload.extra
  if (extra && typeof extra === 'object') {
    for (const key of ['prompt', 'user_message', 'message', 'text']) {
      const v = extra[key]
      if (typeof v === 'string' && v.trim()) return v.trim().slice(0, MAX_PROMPT_CHARS)
    }
  }
  return ''
}

function loadLive() {
  try {
    const doc = JSON.parse(fs.readFileSync(livePath, 'utf8'))
    if (doc && doc.schema === 'agent-pack/experience-live/v1') return doc
  } catch {
    /* miss */
  }
  return null
}

function applyLive(payload) {
  const prompt = extractPrompt(payload)
  const sessionId = String(payload.session_id || 'default').trim() || 'default'
  const now = new Date().toISOString()
  let live = loadLive()
  if (!live || live.sessionId !== sessionId) {
    live = {
      schema: 'agent-pack/experience-live/v1',
      sessionId,
      updatedAt: now,
      turns: [],
    }
  }
  if (prompt) {
    const last = live.turns[live.turns.length - 1]
    if (!last || last.prompt !== prompt) {
      live.turns.push({ at: now, prompt })
      if (live.turns.length > MAX_TURNS) live.turns = live.turns.slice(-MAX_TURNS)
    }
  }
  live.updatedAt = now
  try {
    fs.mkdirSync(expRoot, { recursive: true })
    fs.writeFileSync(livePath, JSON.stringify(live, null, 2), 'utf8')
  } catch {
    /* ignore persist errors; still inject in-memory */
  }
  return live
}

function formatLive(live) {
  if (!live || !live.turns || !live.turns.length) return ''
  const lines = live.turns.map((t, i) => `${i + 1}. ${t.prompt}`)
  return [
    '[Live experience — evolves with this session; not a skill]',
    `session: ${live.sessionId}`,
    'recent turns:',
    ...lines,
  ].join('\n')
}

function formatBlock(exp) {
  const parts = []
  const w = exp.offset?.weight ?? 1
  parts.push(`[Experience: ${exp.name || exp.id}${exp.version ? ` v${exp.version}` : ''}]`)
  const prompt = exp.harness?.base_system_prompt?.trim()
  if (prompt) parts.push(prompt)
  const delta = exp.offset?.promptDelta?.trim()
  if (delta) parts.push(delta)
  const reminders = [...(exp.harness?.system_reminders || []), ...(exp.offset?.reminders || [])]
  if (reminders.length) parts.push('Reminders:\n' + reminders.map(r => `- ${r}`).join('\n'))
  if (w !== 1 && w > 0) parts.push(`(experience weight: ${w})`)
  return parts.join('\n\n')
}

function emptyOut() {
  const hookStyle = process.env.AGENT_PACK_HOOK_STYLE || 'claude'
  if (hookStyle === 'hermes') return {}
  if (hookStyle === 'cursor') return { additional_context: '' }
  return {
    hookSpecificOutput: {
      hookEventName: hookEvent,
      additionalContext: '',
    },
  }
}

function writeOut(additionalContext) {
  const hookStyle = process.env.AGENT_PACK_HOOK_STYLE || 'claude'
  if (hookStyle === 'cursor') {
    process.stdout.write(JSON.stringify({ additional_context: additionalContext }))
    return
  }
  if (hookStyle === 'hermes') {
    process.stdout.write(JSON.stringify({ context: additionalContext }))
    return
  }
  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: hookEvent,
        additionalContext,
      },
    }),
  )
}

function main() {
  const payload = parsePayload(readStdinSync())
  const live = applyLive(payload)

  let index
  try {
    index = JSON.parse(fs.readFileSync(indexPath, 'utf8'))
  } catch {
    const liveOnly = formatLive(live)
    if (!liveOnly) {
      process.stdout.write(JSON.stringify(emptyOut()))
      return
    }
    writeOut(
      [
        '--- agent-pack experiences (session injection, not skills) ---',
        liveOnly,
        '--- end agent-pack experiences ---',
      ].join('\n\n'),
    )
    return
  }

  const blocks = []
  if (index.deliver === 'experience' && index.experiences?.length) {
    for (const entry of index.experiences) {
      const safe = String(entry.id).replace(/[^\w.-]+/g, '_')
      const expPath = path.join(expRoot, `${safe}.exp.json`)
      try {
        const exp = JSON.parse(fs.readFileSync(expPath, 'utf8'))
        const w = exp.offset?.weight ?? 1
        if (w <= 0) continue
        blocks.push(formatBlock(exp))
      } catch {
        /* skip */
      }
    }
  }

  const liveText = formatLive(live)
  if (!blocks.length && !liveText) {
    process.stdout.write(JSON.stringify(emptyOut()))
    return
  }

  const parts = ['--- agent-pack experiences (session injection, not skills) ---', ...blocks]
  if (liveText) parts.push(liveText)
  parts.push('--- end agent-pack experiences ---')
  writeOut(parts.join('\n\n'))
}

main()
