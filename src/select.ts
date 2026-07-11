/**
 * 选件封包 — 从全量 scan 结果按 manifest 筛选。
 */
import type { PackDoc } from './types.js'
import type { PackModules } from './modules.js'

export type PackSelectManifest = {
  name?: string
  skills?: string[] | '*'
  rules?: string[] | '*'
  mcp?: string[] | '*'
  subagents?: string[] | '*'
  /** 模块开关（与 project.yaml / CLI --modules 合并） */
  modules?: PackModules
  /** @deprecated 用 captureAs: skill */
  harness?: boolean
  /** @deprecated 用 captureAs: experience */
  experience?: boolean
  /** 抓包蒸馏交付：skill | experience */
  captureAs?: 'skill' | 'experience'
}

function matchList(name: string, sel: string[] | '*' | undefined): boolean {
  if (sel === undefined) return true
  if (sel === '*') return true
  return sel.includes(name)
}

export function filterPackBySelection(pack: PackDoc, sel: PackSelectManifest): PackDoc {
  const out: PackDoc = { ...pack, name: sel.name || pack.name }

  if (pack.knowledge?.skills && sel.skills !== undefined && sel.skills !== '*') {
    const set = new Set(sel.skills)
    out.knowledge = {
      ...pack.knowledge,
      skills: pack.knowledge.skills.filter(s => set.has(String(s.name || ''))),
    }
    if (pack.bundle?.files) {
      const prefixes = [...set].map(n => `skills/${n}/`)
      out.bundle = {
        ...pack.bundle,
        files: pack.bundle.files.filter(f => prefixes.some(p => f.path.startsWith(p))),
      }
    }
  }

  if (pack.knowledge?.rules && sel.rules !== undefined && sel.rules !== '*') {
    const set = new Set(sel.rules)
    out.knowledge = {
      ...(out.knowledge ?? pack.knowledge),
      rules: pack.knowledge.rules.filter(r => set.has(String(r.name || ''))),
    }
    if (pack.bundle?.files) {
      out.bundle = {
        ...(out.bundle ?? pack.bundle),
        files: (out.bundle?.files ?? pack.bundle!.files!).filter(f => {
          if (!f.path.startsWith('rules/')) return true
          const base = f.path.slice('rules/'.length)
          return set.has(base)
        }),
      }
    }
  }

  if (pack.tools?.mcp && sel.mcp !== undefined && sel.mcp !== '*') {
    const set = new Set(sel.mcp)
    out.tools = {
      ...pack.tools,
      mcp: pack.tools.mcp.filter(m => set.has(String(m.name || ''))),
    }
  }

  if (pack.agents?.subagents && sel.subagents !== undefined && sel.subagents !== '*') {
    const set = new Set(sel.subagents)
    out.agents = {
      ...pack.agents,
      subagents: pack.agents.subagents.filter(s => set.has(String(s.name || ''))),
    }
    if (pack.bundle?.files) {
      out.bundle = {
        ...(out.bundle ?? pack.bundle),
        files: (out.bundle?.files ?? pack.bundle!.files!).filter(f => {
          if (!f.path.startsWith('agents/')) return true
          const base = f.path.slice('agents/'.length).replace(/\.md$/, '')
          return set.has(base)
        }),
      }
    }
  }

  if (sel.harness === false) {
    out.harness = { base_system_prompt: '', tool_schemas: [], system_reminders: [] }
    if (out.meta) out.meta = { ...out.meta, fidelity: 'L1' }
  }

  return out
}

/** 从 capture 草稿合并 L2 harness（不臆造） */
export async function mergeHarnessFromCapture(
  cwd: string,
  pack: PackDoc,
  stateDir = '.agent-pack',
): Promise<PackDoc> {
  const { promises: fs } = await import('node:fs')
  const { join } = await import('node:path')

  const dirs = [join(cwd, stateDir, 'capture'), join(cwd, '.ccui', 'packs')]
  let best: { pack: Record<string, unknown>; mtime: number } | null = null

  for (const dir of dirs) {
    let names: string[] = []
    try {
      names = (await fs.readdir(dir)).filter(n => n.endsWith('.pack.json') || n.endsWith('.json'))
    } catch {
      continue
    }
    for (const n of names) {
      const p = join(dir, n)
      try {
        const st = await fs.stat(p)
        const doc = JSON.parse(await fs.readFile(p, 'utf8')) as Record<string, unknown>
        if (!doc.harness && !doc.assembly) continue
        if (!best || st.mtimeMs > best.mtime) best = { pack: doc, mtime: st.mtimeMs }
      } catch {
        /* skip */
      }
    }
  }

  if (!best) return pack

  const h = best.pack.harness as PackDoc['harness']
  const assembly = best.pack.assembly
  const model = best.pack.model
  const prompt = h?.base_system_prompt?.trim()
  const hasL2 = Boolean(prompt && prompt.length > 20) || (h?.tool_schemas?.length ?? 0) > 0

  return {
    ...pack,
    harness: h ?? pack.harness,
    ...(assembly ? { assembly } : {}),
    ...(model ? { model } : {}),
    meta: {
      ...pack.meta,
      fidelity: hasL2 ? 'L2' : pack.meta?.fidelity ?? 'L1',
      capturedFrom: String(best.pack.name ?? 'capture-draft'),
      source: 'wire+filesystem',
    },
  }
}

export function filterBundleFilesForPack(pack: PackDoc): PackDoc {
  if (!pack.bundle?.files?.length) return pack
  const skillNames = new Set((pack.knowledge?.skills ?? []).map(s => String(s.name || '')))
  const ruleNames = new Set((pack.knowledge?.rules ?? []).map(r => String(r.name || '')))
  const files = pack.bundle.files.filter(f => {
    const sm = f.path.match(/^skills\/([^/]+)\//)
    if (sm) return skillNames.has(sm[1])
    const rm = f.path.match(/^rules\/(.+)$/)
    if (rm) return ruleNames.has(rm[1])
    return true
  })
  return { ...pack, bundle: { portable: true, files } }
}
