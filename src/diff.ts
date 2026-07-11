/**
 * 对比 pack / lock 组件漂移
 */
import { promises as fs } from 'node:fs'
import type { PackDoc } from './types.js'
import { readPackLock, type PackLock } from './lock.js'
import { readPackFile } from './portable.js'

export type ComponentDiff = {
  kind: 'skills' | 'rules' | 'mcp' | 'experiences'
  id: string
  status: 'added' | 'removed' | 'changed' | 'same'
  before?: { version?: string; hash?: string }
  after?: { version?: string; hash?: string }
  fields?: string[]
}

export type PackDiffReport = {
  ok: boolean
  left: string
  right: string
  summary: { added: number; removed: number; changed: number; same: number }
  diffs: ComponentDiff[]
}

type CompMap = Record<string, { version?: string; hash?: string }>

function lockToMaps(lock: PackLock): {
  skills: CompMap
  rules: CompMap
  mcp: CompMap
  experiences: CompMap
} {
  const skills: CompMap = {}
  const rules: CompMap = {}
  const mcp: CompMap = {}
  const experiences: CompMap = {}
  for (const [k, v] of Object.entries(lock.components.skills)) {
    skills[k] = { version: v.version, hash: v.contentHash }
  }
  for (const [k, v] of Object.entries(lock.components.rules)) {
    rules[k] = { version: v.version, hash: v.contentHash }
  }
  for (const [k, v] of Object.entries(lock.components.mcp)) {
    mcp[k] = { version: v.version, hash: v.configHash ?? v.packageVersion }
  }
  for (const [k, v] of Object.entries(lock.components.experiences ?? {})) {
    experiences[k] = { version: v.version, hash: v.contentHash }
  }
  return { skills, rules, mcp, experiences }
}

function packToMaps(pack: PackDoc): {
  skills: CompMap
  rules: CompMap
  mcp: CompMap
  experiences: CompMap
} {
  const skills: CompMap = {}
  const rules: CompMap = {}
  const mcp: CompMap = {}
  const experiences: CompMap = {}
  for (const s of pack.knowledge?.skills ?? []) {
    const n = String(s.name || '')
    if (n) skills[n] = { version: s.version, hash: s.contentHash }
  }
  for (const r of pack.knowledge?.rules ?? []) {
    const n = String(r.name || '')
    if (n) rules[n] = { version: r.version, hash: r.contentHash }
  }
  for (const m of pack.tools?.mcp ?? []) {
    const n = String(m.name || '')
    if (n) mcp[n] = { version: m.version, hash: m.configHash }
  }
  for (const e of pack.experiences ?? []) {
    experiences[e.id] = { version: e.version, hash: e.contentHash }
  }
  return { skills, rules, mcp, experiences }
}

function diffMaps(
  kind: ComponentDiff['kind'],
  left: CompMap,
  right: CompMap,
): ComponentDiff[] {
  const out: ComponentDiff[] = []
  const keys = new Set([...Object.keys(left), ...Object.keys(right)])
  for (const id of [...keys].sort()) {
    const a = left[id]
    const b = right[id]
    if (a && !b) {
      out.push({ kind, id, status: 'removed', before: a })
    } else if (!a && b) {
      out.push({ kind, id, status: 'added', after: b })
    } else if (a && b) {
      const fields: string[] = []
      if (a.version !== b.version) fields.push('version')
      if (a.hash !== b.hash) fields.push('hash')
      out.push({
        kind,
        id,
        status: fields.length ? 'changed' : 'same',
        before: a,
        after: b,
        fields: fields.length ? fields : undefined,
      })
    }
  }
  return out
}

export function diffPackMaps(
  leftLabel: string,
  rightLabel: string,
  left: ReturnType<typeof packToMaps>,
  right: ReturnType<typeof packToMaps>,
): PackDiffReport {
  const diffs = [
    ...diffMaps('skills', left.skills, right.skills),
    ...diffMaps('rules', left.rules, right.rules),
    ...diffMaps('mcp', left.mcp, right.mcp),
    ...diffMaps('experiences', left.experiences, right.experiences),
  ]
  const summary = {
    added: diffs.filter(d => d.status === 'added').length,
    removed: diffs.filter(d => d.status === 'removed').length,
    changed: diffs.filter(d => d.status === 'changed').length,
    same: diffs.filter(d => d.status === 'same').length,
  }
  return { ok: true, left: leftLabel, right: rightLabel, summary, diffs }
}

export async function diffLockFiles(leftPath: string, rightPath: string): Promise<PackDiffReport> {
  const left = JSON.parse(await fs.readFile(leftPath, 'utf8')) as PackLock
  const right = JSON.parse(await fs.readFile(rightPath, 'utf8')) as PackLock
  return diffPackMaps(leftPath, rightPath, lockToMaps(left), lockToMaps(right))
}

export async function diffPackFiles(leftPath: string, rightPath: string): Promise<PackDiffReport> {
  const left = await readPackFile(leftPath)
  const right = await readPackFile(rightPath)
  return diffPackMaps(leftPath, rightPath, packToMaps(left), packToMaps(right))
}

export function formatDiffReport(report: PackDiffReport): string {
  const lines: string[] = [
    `Diff: ${report.left}  →  ${report.right}`,
    `  +${report.summary.added} added  -${report.summary.removed} removed  ~${report.summary.changed} changed  =${report.summary.same} same`,
    '',
  ]
  for (const d of report.diffs) {
    if (d.status === 'same') continue
    const arrow =
      d.status === 'added'
        ? `(new ${d.after?.version ?? '?'})`
        : d.status === 'removed'
          ? `(was ${d.before?.version ?? '?'})`
          : `${d.before?.version ?? '?'} → ${d.after?.version ?? '?'} [${d.fields?.join(',')}]`
    lines.push(`  ${d.status.padEnd(8)} ${d.kind}/${d.id}  ${arrow}`)
  }
  return lines.join('\n')
}
