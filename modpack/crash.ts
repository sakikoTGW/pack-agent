/**
 * 非零退出 / crashed：聚合 logs + dump-config，对照 crash-rules 与 faq。
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { dumpConfig, getInstance, type LauncherRoot } from './launcher.js'
import { builtinRegistryDir, loadRegistryStore } from './registry-store.js'

export type CrashReport = {
  instance: string
  category: string
  matched: string | null
  logs: string[]
  dumpConfig?: string
  faq: { id: string; title: string; markdown: string }[]
}

export function analyzeCrash(root: LauncherRoot, id: string): CrashReport {
  const inst = getInstance(root, id)
  const logDir = join(root.path, 'instances', id, 'logs')
  const files = existsSync(logDir)
    ? readdirSync(logDir).filter((n) => n.endsWith('.log')).map((n) => join(logDir, n))
    : []
  const text = files.map((f) => readFileSync(f, 'utf8')).join('\n')
  let dump = ''
  try {
    dump = dumpConfig(root, id)
  } catch {
    dump = ''
  }
  const hay = `${text}\n${dump}`
  const store = loadRegistryStore({
    builtinDir: builtinRegistryDir(),
    userDir: join(root.path, 'library', 'registries'),
  })
  let category = 'unknown'
  let matched: string | null = null
  const faq: CrashReport['faq'] = []
  for (const rule of store.entries('crash-rules')) {
    if (rule.disabled) continue
    const pattern = String(rule.body.pattern || '')
    if (!pattern) continue
    let re: RegExp
    try {
      re = new RegExp(pattern, 'i')
    } catch {
      continue
    }
    if (re.test(hay)) {
      category = String(rule.body.category || 'unknown')
      matched = rule.id
      const faqId = String(rule.body.faq || '')
      const ent = faqId ? store.entry('faq', faqId) : undefined
      if (ent && !ent.disabled) {
        faq.push({
          id: ent.id,
          title: String(ent.body.title || ent.id),
          markdown: String(ent.body.markdown || ''),
        })
      }
      break
    }
  }
  return { instance: id, category, matched, logs: files, dumpConfig: dump || undefined, faq }
}
