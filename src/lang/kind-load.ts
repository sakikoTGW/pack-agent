import { readdirSync, readFileSync, statSync } from 'node:fs'
import { basename, dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

export type KindDoc = {
  id: string
  version: string
  traits: string[]
  describe: {
    title: string
    summary?: string
    [key: string]: unknown
  }
  layout: {
    required?: string[]
    [key: string]: unknown
  }
  abi: {
    exports?: string[]
    imports?: string[]
    [key: string]: unknown
  }
  install?: {
    default?: string
    [key: string]: unknown
  }
  portability?: {
    exclude?: string[]
    [key: string]: unknown
  }
  [key: string]: unknown
}

const bundledKindsRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'kinds')

function normalizeRoots(searchRoots: string[]): string[] {
  const seen = new Set<string>()
  const roots: string[] = []
  for (const root of searchRoots) {
    if (!root) continue
    const abs = resolve(root)
    const key = abs.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    roots.push(abs)
  }
  return roots
}

function readKindDoc(absPath: string, id: string): KindDoc | null {
  let entry
  try {
    const raw = readFileSync(absPath, 'utf8')
    const json = JSON.parse(raw) as Record<string, unknown>
    if (json && typeof json.id === 'string' && json.id === id) return json as KindDoc
  } catch {
    return null
  }
  return null
}

function* walkKindFiles(root: string): Generator<string> {
  let entry
  try {
    entry = statSync(root)
  } catch {
    return
  }

  if (entry.isFile()) {
    if (root.endsWith('.kind.json')) yield root
    return
  }

  let dirents
  try {
    dirents = readdirSync(root, { withFileTypes: true })
  } catch {
    return
  }

  for (const dirent of dirents) {
    const abs = join(root, dirent.name)
    if (dirent.isDirectory()) {
      yield* walkKindFiles(abs)
      continue
    }
    if (dirent.isFile() && dirent.name.endsWith('.kind.json')) {
      yield abs
    }
  }
}

export function loadKind(id: string, searchRoots: string[]): KindDoc | null {
  const roots = normalizeRoots(searchRoots)
  const fileName = `${id}.kind.json`
  const checked = new Set<string>()

  for (const root of roots) {
    const direct = basename(root) === fileName ? root : join(root, fileName)
    const key = direct.toLowerCase()
    if (!checked.has(key)) {
      checked.add(key)
      const directDoc = readKindDoc(direct, id)
      if (directDoc) return directDoc
    }
  }

  for (const root of roots) {
    for (const abs of walkKindFiles(root)) {
      const key = abs.toLowerCase()
      if (checked.has(key)) continue
      checked.add(key)
      const doc = readKindDoc(abs, id)
      if (doc) return doc
    }
  }

  return null
}

export function defaultKindRoots(): string[] {
  return [bundledKindsRoot]
}
