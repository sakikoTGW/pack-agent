#!/usr/bin/env bun
/** Transcript distillation → experience jar */
import { mkdtemp, readFile, rm, writeFile, mkdir } from 'node:fs/promises'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { distillTranscriptContent } from '../src/transcript-distill.js'
import { embedExtendedBundleFiles, mergeExtendedIntoPack } from '../src/scan-modules.js'
import { installPack } from '../src/install.js'
import { summarizeSessionJsonl } from '../vendor/agent-knowledge/summary.ts'

const SAMPLE = join(import.meta.dir, 'fixtures', 'sample-transcript.jsonl')

async function main(): Promise<void> {
  let raw: string
  try {
    raw = await readFile(SAMPLE, 'utf8')
  } catch {
    console.log('SKIP: no sample transcript fixture')
    process.exit(0)
  }

  const summary = summarizeSessionJsonl(raw)
  if (!summary || summary.topicCount === 0) {
    throw new Error('sample transcript produced empty summary')
  }
  console.log('summary topics:', summary.topicCount, 'tools:', summary.toolsUsed.length)

  const exp = distillTranscriptContent('t.jsonl', raw, SAMPLE)
  if (!exp?.harness?.base_system_prompt) throw new Error('distillTranscriptContent returned empty')
  if (exp.meta?.stub === true) throw new Error('still stub experience')
  if (!exp.harness.base_system_prompt.includes('User topics')) {
    throw new Error('prompt missing User topics section')
  }
  console.log('direct distill ok, prompt chars:', exp.harness.base_system_prompt.length)

  const tmpA = await mkdtemp(join(tmpdir(), 'pack-transcript-a-'))
  const tmpB = await mkdtemp(join(tmpdir(), 'pack-transcript-b-'))
  try {
    const relRef = '.cursor/projects/test/agent-transcripts/t.jsonl'
    await mkdir(join(tmpA, '.cursor/projects/test/agent-transcripts'), { recursive: true })
    await writeFile(join(tmpA, relRef), raw, 'utf8')

    let pack = {
      schema: 'ccui-pack/v0.2' as const,
      name: 'transcript-smoke',
      version: '1.0.0',
      harness: { base_system_prompt: '', tool_schemas: [], system_reminders: [] },
      meta: {
        transcriptIndex: [{ name: 't.jsonl', ref: relRef.replace(/\//g, '\\') }],
      },
    }

    pack = await mergeExtendedIntoPack(tmpA, pack, {
      hooks: [],
      subagents: [],
      memory: [],
      settings: [],
      transcripts: [{ name: 't.jsonl', ref: relRef }],
    })
    pack = await embedExtendedBundleFiles(tmpA, pack)
    const embedded = pack.bundle?.files?.find(f => f.path === 'transcripts/t.jsonl')
    if (!embedded?.content) throw new Error('transcript not embedded in bundle')

    const report = await installPack(tmpB, pack, {
      noBootstrap: true,
      modules: { transcripts: true, experiences: true },
    })
    if (!report.experiences?.length) throw new Error('install produced no experiences')

    const expPath = join(tmpB, '.agent-pack/experiences', `${exp.id}.exp.json`)
    const installed = JSON.parse(await readFile(expPath, 'utf8'))
    if (installed.meta?.stub === true) throw new Error('installed experience is still stub')
    if (!installed.harness?.base_system_prompt?.includes('Distilled session experience')) {
      throw new Error('installed prompt missing distiller header')
    }

    console.log('✓ portable transcript distill A→B')
  } finally {
    await rm(tmpA, { recursive: true, force: true })
    await rm(tmpB, { recursive: true, force: true })
  }
}

main().catch(e => {
  console.error(e)
  process.exit(1)
})
