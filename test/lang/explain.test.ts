import { describe, expect, test } from 'bun:test'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { checkPackToml } from '../../src/lang/check.js'
import { packTestTmp } from '../tmp-root.js'
import { explainPackToml } from '../../src/lang/explain.js'

describe('explainPackToml', () => {
  test('reports missing describe.title from a loaded kind', async () => {
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
      expect(
        result.diagnostics.some(
          (diag) => diag.code === 'E-SCHEMA-INVALID' && diag.params?.field === 'describe.title',
        ),
      ).toBe(true)
    } finally {
      await rm(root, { recursive: true, force: true })
    }
  })

  test('formats pack name, edition, and kind title', () => {
    const result = explainPackToml(`
edition = "2026"
name = "my-pack"

[[unit]]
name = "foo"
kind = "agent.skill"
path = "skills/foo"
`)

    expect(result.ok).toBe(true)
    expect(result.diagnostics).toHaveLength(0)
    expect(result.text).toBe(`pack: my-pack
edition: 2026
units:
  - name: foo  kind: agent.skill  title: Agent Skill  abi.exports: skill
world: (none)`)
  })
})
