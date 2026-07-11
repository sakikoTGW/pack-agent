/**
 * Transcript → experience distillation via vendored agent-knowledge deterministic layer.
 * @see vendor/agent-knowledge/ATTRIBUTION.md
 */
import { promises as fs } from 'node:fs'
import { basename, join } from 'node:path'
import type { PackDoc, PackExperience } from './types.js'
import {
  formatSummaryAsPrompt,
  summarizeSessionJsonl,
} from '../vendor/agent-knowledge/summary.js'
import { sha256Full } from './versioning.js'

export type TranscriptRef = { name: string; ref: string }

function bundleTranscriptPath(name: string): string {
  return `transcripts/${name}`
}

function findBundledTranscript(pack: PackDoc, name: string): string | null {
  const p = bundleTranscriptPath(name)
  const hit = pack.bundle?.files?.find(f => f.path === p || f.path.endsWith(`/${name}`))
  return hit?.content ?? null
}

async function readTranscriptRaw(
  cwd: string,
  t: TranscriptRef,
  pack: PackDoc,
  stagingRoot: string | null,
): Promise<string | null> {
  const bundled = findBundledTranscript(pack, t.name)
  if (bundled) return bundled

  if (stagingRoot) {
    const staged = join(stagingRoot, 'transcripts', t.name)
    try {
      return await fs.readFile(staged, 'utf8')
    } catch {
      /* fall through */
    }
  }

  const abs = join(cwd, t.ref)
  try {
    return await fs.readFile(abs, 'utf8')
  } catch {
    return null
  }
}

export function distillTranscriptContent(name: string, raw: string, sourceRef: string): PackExperience | null {
  const s = summarizeSessionJsonl(raw)
  if (!s) return null
  const hasSignal =
    s.topicCount > 0 ||
    s.toolsUsed.length > 0 ||
    s.filesModified.length > 0 ||
    s.assistantHighlights.length > 0
  if (!hasSignal) return null

  const prompt = formatSummaryAsPrompt(s, sourceRef)
  if (prompt.trim().length < 80) return null

  const id = `transcript-${name.replace(/[^\w.-]+/g, '_').replace(/\.jsonl$/i, '')}`
  const payload = JSON.stringify({ prompt, summary: s })
  return {
    id,
    name: name.replace(/\.jsonl$/i, ''),
    kind: 'distill',
    scope: 'session',
    ttl: 'session',
    source: sourceRef,
    contentHash: sha256Full(payload),
    harness: {
      base_system_prompt: prompt,
      system_reminders: [],
      tool_schemas: [],
    },
    meta: {
      transcriptRef: sourceRef,
      distiller: 'agent-knowledge/vendored-deterministic',
      stub: false,
      topicCount: s.topicCount,
      toolsUsed: s.toolsUsed,
      filesModified: s.filesModified.slice(0, 30),
    },
  }
}

export async function distillTranscriptsToExperiences(
  cwd: string,
  pack: PackDoc,
  stagingRoot: string | null = null,
): Promise<PackDoc> {
  const index = pack.meta?.transcriptIndex as TranscriptRef[] | undefined
  if (!index?.length) return pack

  const existing = new Set((pack.experiences ?? []).map(e => e.id))
  const added: PackExperience[] = []

  for (const t of index) {
    const id = `transcript-${t.name.replace(/[^\w.-]+/g, '_').replace(/\.jsonl$/i, '')}`
    if (existing.has(id)) continue

    const raw = await readTranscriptRaw(cwd, t, pack, stagingRoot)
    if (!raw) continue

    const exp = distillTranscriptContent(t.name, raw, t.ref)
    if (!exp) continue
    existing.add(exp.id)
    added.push(exp)
  }

  if (!added.length) return pack
  return {
    ...pack,
    experiences: [...(pack.experiences ?? []), ...added],
    policy: { ...pack.policy, captureAs: pack.policy?.captureAs ?? 'experience' },
    meta: {
      ...pack.meta,
      transcriptDistilled: added.length,
      fidelity: pack.meta?.fidelity ?? 'L1+transcript-distill',
    },
  }
}

/** Resolve transcript file content for portable bundle embedding. */
export async function readTranscriptForEmbed(cwd: string, ref: string): Promise<string | null> {
  try {
    return await fs.readFile(join(cwd, ref), 'utf8')
  } catch {
    return null
  }
}

export function transcriptBundlePath(name: string): string {
  return bundleTranscriptPath(name)
}
