#!/usr/bin/env node
'use strict'
/**
 * agent-pack experience SessionStart hook (harness-neutral).
 * Reads .agent-pack/experiences/ → stdout JSON for Claude-style hooks.
 * Not a skill — ambient session injection.
 */
const fs = require('fs')
const path = require('path')

const cwd = process.cwd()
const stateDir = process.env.AGENT_PACK_STATE_DIR || '.agent-pack'
const hookEvent = process.env.AGENT_PACK_HOOK_EVENT || 'SessionStart'
const indexPath = path.join(cwd, stateDir, 'experiences', 'index.json')

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
  return {
    hookSpecificOutput: {
      hookEventName: hookEvent,
      additionalContext: '',
    },
  }
}

function main() {
  let index
  try {
    index = JSON.parse(fs.readFileSync(indexPath, 'utf8'))
  } catch {
    process.stdout.write(JSON.stringify(emptyOut()))
    return
  }
  if (index.deliver !== 'experience' || !index.experiences?.length) {
    process.stdout.write(JSON.stringify(emptyOut()))
    return
  }
  const blocks = []
  for (const entry of index.experiences) {
    const safe = String(entry.id).replace(/[^\w.-]+/g, '_')
    const expPath = path.join(cwd, stateDir, 'experiences', `${safe}.exp.json`)
    try {
      const exp = JSON.parse(fs.readFileSync(expPath, 'utf8'))
      const w = exp.offset?.weight ?? 1
      if (w <= 0) continue
      blocks.push(formatBlock(exp))
    } catch {
      /* skip */
    }
  }
  if (!blocks.length) {
    process.stdout.write(JSON.stringify(emptyOut()))
    return
  }
  const additionalContext = [
    '--- agent-pack experiences (session injection, not skills) ---',
    ...blocks,
    '--- end agent-pack experiences ---',
  ].join('\n\n')

  const hookStyle = process.env.AGENT_PACK_HOOK_STYLE || 'claude'
  if (hookStyle === 'cursor') {
    process.stdout.write(JSON.stringify({ additional_context: additionalContext }))
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

main()
