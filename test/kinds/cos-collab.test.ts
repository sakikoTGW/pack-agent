import { describe, expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { checkPackToml } from '../../src/lang/check.js'
import { loadKind } from '../../src/lang/kind-load.js'
import { DEFAULT_PACK_MODULES, parseModulesList } from '../../src/modules.js'

const root = join(import.meta.dir, '..', '..')
const examplesKindsRoot = join(root, 'examples', 'kinds')
const srcKindsRoot = join(root, 'src', 'kinds')
const packTomlPath = join(root, 'examples', 'packs', 'cos-collab', 'Pack.toml')

describe('cos.collab dialect kind sample', () => {
  test('loadKind finds the example kind doc', () => {
    const kind = loadKind('cos.collab', [examplesKindsRoot])

    expect(kind?.describe.title).toBe('Cos Collab Workspace')
    expect(kind?.install?.default).toBe('.collab')
    expect(kind?.abi?.exports).toEqual(['collab'])
  })

  test('checkPackToml accepts the example Pack.toml with custom kind roots', () => {
    const text = readFileSync(packTomlPath, 'utf8')
    const result = checkPackToml(text, { kindRoots: [examplesKindsRoot, srcKindsRoot] })

    expect(result.ok).toBe(true)
    expect(result.diagnostics).toHaveLength(0)
  })

  test('built-in module switches no longer expose collab', () => {
    expect('collab' in DEFAULT_PACK_MODULES).toBe(false)
    expect(parseModulesList(['all'])).not.toHaveProperty('collab')
  })
})
