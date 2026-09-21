/** DSH `$DSH_HOME/.credentials.yaml` 里 DEEPSEEK_API_KEY 的 REF 行。 */

const KEY = 'DEEPSEEK_API_KEY'
const LINE = /^DEEPSEEK_API_KEY\s*:\s*(.*)$/m

export function hasDeepseekKey(yaml: string | null | undefined): boolean {
  return readDeepseekKey(yaml).length > 0
}

export function readDeepseekKey(yaml: string | null | undefined): string {
  const m = String(yaml ?? '').match(LINE)
  if (!m) return ''
  let v = m[1].trim()
  if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) {
    v = v.slice(1, -1)
  }
  if (v.startsWith('REF:')) v = v.slice(4).trim()
  return v
}

export function upsertDeepseekKey(yaml: string | null | undefined, value: string): string {
  return upsertRef(yaml, KEY, value)
}

const REF_LINE = /^([A-Z][A-Z0-9_]*)\s*:\s*(.*)$/

export function upsertRef(yaml: string | null | undefined, name: string, value: string): string {
  const key = name.trim()
  if (!/^[A-Z][A-Z0-9_]*$/.test(key)) throw new Error('ref name must be ENV-style')
  const v = value.trim()
  const line = `${key}: "REF: ${v}"`
  const body = String(yaml ?? '')
  const re = new RegExp(`^${key}\\s*:.*$`, 'm')
  if (re.test(body)) return body.replace(re, line)
  const prefix = body.length === 0 ? '' : body.endsWith('\n') ? body : `${body}\n`
  return `${prefix}${line}\n`
}

export function removeRef(yaml: string | null | undefined, name: string): string {
  const key = name.trim()
  const re = new RegExp(`^${key}\\s*:.*\\r?\\n?`, 'm')
  return String(yaml ?? '').replace(re, '')
}

export function listRefs(yaml: string | null | undefined): string[] {
  const names: string[] = []
  for (const raw of String(yaml ?? '').split(/\r?\n/)) {
    const m = raw.trim().match(REF_LINE)
    if (!m) continue
    let v = m[2].trim()
    if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) {
      v = v.slice(1, -1)
    }
    if (v.startsWith('REF:')) v = v.slice(4).trim()
    if (v.length > 0) names.push(m[1])
  }
  return names
}

export function envHasDeepseekKey(envText: string | null | undefined): boolean {
  for (const raw of String(envText ?? '').split(/\r?\n/)) {
    const line = raw.trim()
    if (line.length === 0 || line.startsWith('#')) continue
    const eq = line.indexOf('=')
    if (eq <= 0) continue
    const key = line.slice(0, eq).trim()
    const val = line.slice(eq + 1).trim()
    if (key === KEY && val.length > 0) return true
  }
  return false
}
