#!/usr/bin/env bun
/**
 * 主矛盾：经验活在上下文、跟着对话变（相对 skill 固化）。
 * 验收：同一会话两轮不同输入 → live 注入文案必须变。
 */
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { packTestTmp } from './tmp-root.js'
import {
  applyLiveTurn,
  formatLiveInjection,
  loadLiveState,
  type HookStdinPayload,
} from '../src/experience-live.js'

async function main(): Promise<void> {
  const cwd = await mkdtemp(packTestTmp('pack-exp-live-'))
  try {
    await mkdir(join(cwd, '.agent-pack', 'experiences'), { recursive: true })

    const turn1: HookStdinPayload = {
      session_id: 'sess-a',
      hook_event_name: 'UserPromptSubmit',
      prompt: '把 packer 角色从 Claude Code 迁到 Hermes，保留驯服过的打包习惯',
      cwd,
    }
    const live1 = await applyLiveTurn(cwd, turn1)
    const text1 = formatLiveInjection(live1)
    if (!text1.includes('Claude Code') && !text1.includes('Hermes') && !text1.includes('packer')) {
      throw new Error('turn1 live text missing prompt signal')
    }

    const turn2: HookStdinPayload = {
      session_id: 'sess-a',
      hook_event_name: 'UserPromptSubmit',
      prompt: '冲突时默认 stop，不要静默 replace',
      cwd,
    }
    const live2 = await applyLiveTurn(cwd, turn2)
    const text2 = formatLiveInjection(live2)

    if (text1 === text2) {
      throw new Error('live injection did not change across turns (main contradiction unmet)')
    }
    if (!text2.includes('stop') && !text2.includes('replace') && !text2.includes('冲突')) {
      throw new Error('turn2 live text missing new prompt signal')
    }
    if (live2.turns.length < 2) {
      throw new Error(`expected >=2 turns, got ${live2.turns.length}`)
    }

    const disk = await loadLiveState(cwd)
    if (!disk || disk.turns.length !== live2.turns.length) {
      throw new Error('live state not persisted')
    }

    // 新会话应重新开一条 live，不把旧会话焊死成「假 skill」
    const turnB: HookStdinPayload = {
      session_id: 'sess-b',
      hook_event_name: 'UserPromptSubmit',
      prompt: '全新会话只谈 eject 账本',
      cwd,
    }
    const liveB = await applyLiveTurn(cwd, turnB)
    if (liveB.sessionId !== 'sess-b') throw new Error('session reset failed')
    if (liveB.turns.length !== 1) throw new Error('new session should start at 1 turn')
    const textB = formatLiveInjection(liveB)
    if (textB.includes('冲突时默认 stop')) {
      throw new Error('new session leaked previous session live text')
    }

    // hook 脚本：stdin 两轮后 stdout 上下文必须变
    const hook = join(import.meta.dir, '..', 'scripts', 'experience-session-hook.cjs')
    await writeFile(
      join(cwd, '.agent-pack', 'experiences', 'index.json'),
      JSON.stringify({
        schema: 'agent-pack/experience-index/v1',
        installedAt: new Date().toISOString(),
        deliver: 'experience',
        experiences: [{ id: 'base', path: 'base.exp.json', scope: 'session' }],
      }),
      'utf8',
    )
    await writeFile(
      join(cwd, '.agent-pack', 'experiences', 'base.exp.json'),
      JSON.stringify({
        id: 'base',
        name: 'base',
        kind: 'distill',
        scope: 'session',
        harness: { base_system_prompt: 'BASE_CAN_MARKER', system_reminders: [], tool_schemas: [] },
        offset: { weight: 1 },
      }),
      'utf8',
    )

    const { spawnSync } = await import('node:child_process')
    const runHook = (prompt: string) => {
      const r = spawnSync(process.execPath, [hook], {
        cwd,
        input: JSON.stringify({
          session_id: 'sess-hook',
          hook_event_name: 'UserPromptSubmit',
          prompt,
        }),
        encoding: 'utf8',
        env: { ...process.env, AGENT_PACK_HOOK_STYLE: 'claude', AGENT_PACK_HOOK_EVENT: 'UserPromptSubmit' },
      })
      if (r.status !== 0) throw new Error(`hook exit ${r.status}: ${r.stderr}`)
      return r.stdout
    }

    const out1 = runHook('第一轮：只要 detect 列表')
    const out2 = runHook('第二轮：只要 ledger eject')
    if (out1 === out2) throw new Error('hook stdout did not change across prompts')
    const j1 = JSON.parse(out1) as { hookSpecificOutput?: { additionalContext?: string } }
    const j2 = JSON.parse(out2) as { hookSpecificOutput?: { additionalContext?: string } }
    const c1 = j1.hookSpecificOutput?.additionalContext ?? ''
    const c2 = j2.hookSpecificOutput?.additionalContext ?? ''
    if (!c1.includes('BASE_CAN_MARKER') || !c2.includes('BASE_CAN_MARKER')) {
      throw new Error('base can missing from hook injection')
    }
    if (c1 === c2) throw new Error('hook additionalContext identical across turns')
    if (!c2.includes('ledger') && !c2.includes('eject')) {
      throw new Error('hook live layer missing turn2 signal')
    }

    console.log('✓ experience live evolves within session')
  } finally {
    await rm(cwd, { recursive: true, force: true })
  }
}

main().catch(err => {
  console.error(err)
  process.exit(1)
})
