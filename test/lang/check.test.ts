import { describe, expect, test } from 'bun:test'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { checkPackToml } from '../../src/lang/check.js'
import { packTestTmp } from '../tmp-root.js'

describe('checkPackToml', () => {
  test('reports unknown edition', () => {
    const result = checkPackToml(`
edition = "1999"
name = "future-pack"

[[unit]]
kind = "agent.skill"
path = "skills/demo"
`)

    expect(result.ok).toBe(false)
    expect(result.diagnostics.some((diag) => diag.code === 'E-EDITION-UNKNOWN')).toBe(true)
  })

  test('reports missing kind', () => {
    const result = checkPackToml(`
edition = "2026"
name = "demo-pack"

[[unit]]
kind = "no.such"
path = "skills/demo"
`)

    expect(result.ok).toBe(false)
    expect(result.diagnostics.some((diag) => diag.code === 'E-KIND-NOT-FOUND')).toBe(true)
  })

  test('accepts valid edition and bundled agent.skill kind', () => {
    const result = checkPackToml(`
edition = "2026"
name = "demo-pack"

[[unit]]
kind = "agent.skill"
path = "skills/demo"
`)

    expect(result.ok).toBe(true)
    expect(result.diagnostics).toHaveLength(0)
    expect(result.ast?.edition).toBe('2026')
  })

  test('reports missing describe.title on a loaded kind', async () => {
    const root = await mkdtemp(packTestTmp('pack-kind-'))
    try {
      const kindRoots = [join(root, 'kinds')]
      await mkdir(kindRoots[0], { recursive: true })
      await writeFile(
        join(kindRoots[0], 'no.title.kind.json'),
        JSON.stringify({
          id: 'no.title',
          version: '1.0.0',
          traits: ['Describe'],
          describe: {
            summary: 'missing title on purpose',
          },
          layout: {},
          abi: { exports: ['skill'], imports: [] },
        }),
        'utf8',
      )

      const result = checkPackToml(
        `
edition = "2026"
name = "demo-pack"

[[unit]]
kind = "no.title"
path = "skills/demo"
`,
        { kindRoots },
      )

      expect(result.ok).toBe(false)
      expect(result.diagnostics.some((diag) => diag.code === 'E-SCHEMA-INVALID')).toBe(true)
      expect(result.diagnostics.some((diag) => diag.params?.field === 'describe.title')).toBe(true)
    } finally {
      await rm(root, { recursive: true, force: true })
    }
  })
})
