/**
 * 注册表路径映射。查表 O(1)。多余字段 PA017。同 id 覆盖 PA109。
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'

export class RegistryError extends Error {
  code: string
  constructor(code: string, message: string) {
    super(message)
    this.name = 'RegistryError'
    this.code = code
  }
}

export type RegistryWarning = { code: string; message: string; id: string }

export type RegistryEntry = {
  id: string
  path: string
  body: Record<string, unknown>
  disabled?: boolean
}

const ENVELOPE_KEYS = new Set(['schema', 'registry', 'version', 'entries'])
const ENTRY_KEYS: Record<string, Set<string>> = {
  'format-sniff': new Set(['id', 'match', 'kind', 'handler', 'requires', 'disabled']),
  'task-kinds': new Set(['id', 'steps', 'spec', 'requires', 'disabled']),
  'pa-codes': new Set(['id', 'level', 'message', 'help', 'note', 'requires', 'disabled']),
  'profiles': new Set(['id', 'port', 'ready', 'requires', 'disabled']),
  'crash-rules': new Set(['id', 'pattern', 'category', 'faq', 'requires', 'disabled']),
  'faq': new Set(['id', 'title', 'markdown', 'requires', 'disabled']),
  'migrate': new Set(['id', 'fromSchema', 'toSchema', 'primitive', 'requires', 'disabled']),
  'compat-snapshot-spec': new Set(['id', 'field', 'source', 'value', 'requires', 'disabled']),
}

type Envelope = {
  schema: string
  registry: string
  version: number
  entries: Record<string, unknown>[]
}

function extraKeys(obj: Record<string, unknown>, allowed: Set<string>): string[] {
  return Object.keys(obj).filter((k) => !allowed.has(k))
}

function asRecord(v: unknown, loc: string): Record<string, unknown> {
  if (!v || typeof v !== 'object' || Array.isArray(v)) {
    throw new RegistryError('PA017', `registry ${loc} is not an object`)
  }
  return v as Record<string, unknown>
}

function parseEnvelope(raw: string, path: string, expectName: string): Envelope {
  let json: unknown
  try {
    json = JSON.parse(raw)
  } catch (e) {
    throw new RegistryError('PA017', `invalid JSON ${path}: ${e}`)
  }
  const obj = asRecord(json, path)
  const extra = extraKeys(obj, ENVELOPE_KEYS)
  if (extra.length) throw new RegistryError('PA017', `extra fields ${extra.join(',')} in ${path}`)
  if (obj.schema !== 'pack-agent.registry/v1') {
    throw new RegistryError('PA017', `bad schema in ${path}`)
  }
  if (obj.registry !== expectName) {
    throw new RegistryError('PA017', `registry field ${String(obj.registry)} != ${expectName}`)
  }
  if (typeof obj.version !== 'number') throw new RegistryError('PA017', `missing version in ${path}`)
  if (!Array.isArray(obj.entries)) throw new RegistryError('PA017', `entries must be array in ${path}`)
  return obj as Envelope
}

function parseEntry(registry: string, row: unknown, path: string): Record<string, unknown> {
  const obj = asRecord(row, path)
  const allowed = ENTRY_KEYS[registry]
  if (!allowed) throw new RegistryError('PA017', `unknown registry ${registry}`)
  const extra = extraKeys(obj, allowed)
  if (extra.length) throw new RegistryError('PA017', `extra fields ${extra.join(',')} in ${path}`)
  if (typeof obj.id !== 'string' || !obj.id) {
    throw new RegistryError('PA017', `missing id in ${path}`)
  }
  return obj
}

function listJson(dir: string): string[] {
  if (!existsSync(dir)) return []
  return readdirSync(dir).filter((n) => n.endsWith('.json'))
}

function loadFile(path: string, expectName: string): Record<string, unknown>[] {
  const env = parseEnvelope(readFileSync(path, 'utf8'), path, expectName)
  return env.entries.map((row) => parseEntry(expectName, row, path))
}

function cycleError(nodes: Map<string, string[]>): string | null {
  const visiting = new Set<string>()
  const seen = new Set<string>()
  const visit = (id: string): boolean => {
    if (visiting.has(id)) return true
    if (seen.has(id)) return false
    visiting.add(id)
    for (const d of nodes.get(id) ?? []) {
      if (visit(d)) return true
    }
    visiting.delete(id)
    seen.add(id)
    return false
  }
  for (const id of nodes.keys()) {
    if (visit(id)) return id
  }
  return null
}

export type RegistryStore = {
  warnings: RegistryWarning[]
  entry: (registry: string, id: string) => RegistryEntry | undefined
  entries: (registry: string) => RegistryEntry[]
  snapshot: () => RegistryStore
}

export function loadRegistryStore(opts: { builtinDir: string; userDir: string }): RegistryStore {
  const warnings: RegistryWarning[] = []
  const map = new Map<string, RegistryEntry>()
  const builtinDir = resolve(opts.builtinDir)
  const userDir = resolve(opts.userDir)

  const names = new Set<string>()
  for (const file of listJson(builtinDir)) names.add(file.slice(0, -5))
  for (const file of listJson(userDir)) names.add(file.slice(0, -5))

  for (const name of names) {
    if (!ENTRY_KEYS[name]) throw new RegistryError('PA017', `unknown registry ${name}`)
    const builtinPath = join(builtinDir, `${name}.json`)
    const userPath = join(userDir, `${name}.json`)
    const builtinRows = existsSync(builtinPath) ? loadFile(builtinPath, name) : []
    const userRows = existsSync(userPath) ? loadFile(userPath, name) : []
    const builtinIds = new Set(builtinRows.map((r) => String(r.id)))
    for (const row of builtinRows) {
      const id = String(row.id)
      map.set(`${name}/${id}`, {
        id,
        path: builtinPath,
        body: row,
        disabled: row.disabled === true,
      })
    }
    for (const row of userRows) {
      const id = String(row.id)
      const key = `${name}/${id}`
      if (builtinIds.has(id)) {
        warnings.push({
          code: 'PA109',
          message: `user overlay replaces builtin ${key}`,
          id: key,
        })
      }
      map.set(key, {
        id,
        path: userPath,
        body: row,
        disabled: row.disabled === true,
      })
    }
  }

  const byReg = new Map<string, Map<string, string[]>>()
  for (const [key, ent] of map) {
    const [reg, id] = key.split('/')
    const req = ent.body.requires
    const deps = Array.isArray(req) ? req.map(String) : []
    if (!byReg.has(reg)) byReg.set(reg, new Map())
    byReg.get(reg)!.set(id, deps)
    for (const d of deps) {
      if (!map.has(`${reg}/${d}`)) {
        throw new RegistryError('PA018', `requires missing ${reg}/${d} from ${key}`)
      }
    }
  }
  for (const [reg, nodes] of byReg) {
    const cyc = cycleError(nodes)
    if (cyc) throw new RegistryError('PA017', `requires cycle in ${reg} at ${cyc}`)
  }

  const store: RegistryStore = {
    warnings,
    entry(registry: string, id: string) {
      return map.get(`${registry}/${id}`)
    },
    entries(registry: string) {
      const prefix = `${registry}/`
      const out: RegistryEntry[] = []
      for (const [key, ent] of map) {
        if (key.startsWith(prefix) && key.slice(prefix.length).indexOf('/') === -1) out.push(ent)
      }
      return out
    },
    snapshot() {
      const frozen = new Map(map)
      const warningCopy = [...warnings]
      const snap: RegistryStore = {
        warnings: warningCopy,
        entry(registry: string, id: string) {
          return frozen.get(`${registry}/${id}`)
        },
        entries(registry: string) {
          const prefix = `${registry}/`
          const out: RegistryEntry[] = []
          for (const [key, ent] of frozen) {
            if (key.startsWith(prefix) && key.slice(prefix.length).indexOf('/') === -1) out.push(ent)
          }
          return out
        },
        snapshot() {
          return snap
        },
      }
      return snap
    },
  }
  return store
}

export function builtinRegistryDir(): string {
  return join(import.meta.dirname, 'registries')
}
