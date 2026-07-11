/**
 * 扩展模块安装 — hooks / subagents / memory / settings。
 */
import { promises as fs } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import type { PackDoc } from './types.js'
import { materializePortableBundle } from './portable.js'

export type ModuleInstallReport = {
  hooks: string[]
  subagents: string[]
  memory: string[]
  settings: string[]
  skipped: string[]
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

function marker(kind: string, name: string): string {
  return `<!-- agent-pack:${kind}:${name} -->`
}

async function appendMarkedBlock(dest: string, markerLine: string, body: string): Promise<void> {
  const block = `\n${markerLine}\n${body.trim()}\n${markerLine.replace('<!--', '<!-- /')}\n`
  if (await exists(dest)) {
    const cur = await fs.readFile(dest, 'utf8')
    if (cur.includes(markerLine)) return
    await fs.writeFile(dest, cur + block, 'utf8')
  } else {
    await fs.mkdir(dirname(dest), { recursive: true })
    await fs.writeFile(dest, block, 'utf8')
  }
}

async function readBundleJson(staging: string | null, pack: PackDoc, rel: string): Promise<unknown | null> {
  const fromBundle = pack.bundle?.files?.find(f => f.path === rel)
  if (fromBundle) {
    try {
      return JSON.parse(fromBundle.content)
    } catch {
      return null
    }
  }
  if (!staging) return null
  try {
    return JSON.parse(await fs.readFile(join(staging, rel.replace(/\//g, '\\')), 'utf8'))
  } catch {
    return null
  }
}

async function readBundleText(staging: string | null, pack: PackDoc, rel: string): Promise<string | null> {
  const fromBundle = pack.bundle?.files?.find(f => f.path === rel)
  if (fromBundle) return fromBundle.content
  if (!staging) return null
  try {
    return await fs.readFile(join(staging, rel.replace(/\//g, '\\')), 'utf8')
  } catch {
    return null
  }
}

async function mergeHooksIntoSettings(cwd: string, hooks: unknown, name: string): Promise<string | null> {
  const settingsPath = join(cwd, '.claude', 'settings.json')
  let doc: Record<string, unknown> = {}
  if (await exists(settingsPath)) {
    try {
      doc = JSON.parse(await fs.readFile(settingsPath, 'utf8')) as Record<string, unknown>
    } catch {
      doc = {}
    }
  }
  const existing = (doc.hooks ?? {}) as Record<string, unknown>
  const incoming = hooks as Record<string, unknown>
  const merged = { ...existing }
  for (const [event, cfg] of Object.entries(incoming)) {
    merged[event] = cfg
  }
  doc.hooks = merged
  doc._agentPack = {
    ...(typeof doc._agentPack === 'object' && doc._agentPack ? (doc._agentPack as object) : {}),
    [name]: { mergedAt: new Date().toISOString() },
  }
  await fs.mkdir(dirname(settingsPath), { recursive: true })
  await fs.writeFile(settingsPath, JSON.stringify(doc, null, 2), 'utf8')
  return settingsPath
}

export async function installExtendedModules(
  cwd: string,
  pack: PackDoc,
  stateDir = '.agent-pack',
): Promise<ModuleInstallReport> {
  const report: ModuleInstallReport = { hooks: [], subagents: [], memory: [], settings: [], skipped: [] }
  const staging = await materializePortableBundle(cwd, pack, stateDir)

  for (const h of pack.automation?.hooks ?? []) {
    const rel = `automation/${h.name ?? 'settings'}.hooks.json`
    const hooks = await readBundleJson(staging, pack, rel)
    if (!hooks) {
      report.skipped.push(`hook:${h.name ?? '?'} (missing bundle)`)
      continue
    }
    const dest = await mergeHooksIntoSettings(cwd, hooks, h.name ?? 'pack')
    if (dest) report.hooks.push(dest)
  }

  for (const a of pack.agents?.subagents ?? []) {
    const rel = `agents/${a.name ?? 'agent'}.md`
    const content = await readBundleText(staging, pack, rel)
    if (!content) {
      report.skipped.push(`subagent:${a.name ?? '?'} (missing bundle)`)
      continue
    }
    const destDir = join(cwd, '.claude', 'agents')
    const dest = join(destDir, `${a.name ?? basename(rel, '.md')}.md`)
    await fs.mkdir(destDir, { recursive: true })
    if (await exists(dest)) {
      await appendMarkedBlock(dest, marker('subagent', a.name ?? 'agent'), content)
    } else {
      await fs.writeFile(dest, content, 'utf8')
    }
    report.subagents.push(dest)
  }

  const memRoot = join(cwd, stateDir, 'memory')
  await fs.mkdir(memRoot, { recursive: true })
  for (const m of pack.memory?.files ?? []) {
    const rel = `memory/${m.name ?? basename(String(m.ref || ''))}`
    const content = await readBundleText(staging, pack, rel)
    if (!content) {
      report.skipped.push(`memory:${m.name ?? '?'} (missing bundle)`)
      continue
    }
    const dest = join(memRoot, m.name ?? basename(rel))
    await fs.writeFile(dest, content, 'utf8')
    report.memory.push(dest)

    if (m.kind === 'project-memory' && m.name === 'MEMORY.md') {
      const projectMem = join(cwd, 'MEMORY.md')
      if (!(await exists(projectMem))) {
        await fs.writeFile(projectMem, content, 'utf8')
        report.memory.push(projectMem)
      }
    }
  }

  for (const s of pack.settings?.fragments ?? []) {
    const key = String(s.key || '')
    const rel = `settings/${key}.json`
    const fragment = await readBundleJson(staging, pack, rel)
    if (!fragment) {
      report.skipped.push(`settings:${key} (missing bundle)`)
      continue
    }
    const settingsPath = join(cwd, '.claude', 'settings.json')
    let doc: Record<string, unknown> = {}
    if (await exists(settingsPath)) {
      try {
        doc = JSON.parse(await fs.readFile(settingsPath, 'utf8')) as Record<string, unknown>
      } catch {
        doc = {}
      }
    }
    doc[key] = fragment
    await fs.mkdir(dirname(settingsPath), { recursive: true })
    await fs.writeFile(settingsPath, JSON.stringify(doc, null, 2), 'utf8')
    report.settings.push(`${settingsPath}#${key}`)
  }

  return report
}
