import { describe, expect, test } from 'bun:test'
import { checkPackToml } from '../../src/lang/check.js'
import { defaultKindRoots, loadKind } from '../../src/lang/kind-load.js'
import { getStdlibInstallTemplate, STDLIB_KIND_IDS } from '../../src/lang/stdlib.js'

const stdlibKindIds = [
  'agent.skill',
  'agent.rule',
  'agent.mcp',
  'agent.command',
  'agent.hook',
  'agent.loop',
]

describe('stdlib kinds', () => {
  test('loadKind finds bundled titles and install templates', () => {
    const roots = defaultKindRoots()

    for (const kindId of stdlibKindIds) {
      const kind = loadKind(kindId, roots)
      expect(kind?.describe.title).toBeTruthy()
      expect(getStdlibInstallTemplate(kindId)).toBe(kind?.install?.default)
    }

    expect(STDLIB_KIND_IDS).toEqual(expect.arrayContaining(stdlibKindIds))
  })

  test('checkPackToml accepts stdlib unit kinds without world', () => {
    for (const kindId of stdlibKindIds) {
      const result = checkPackToml(`
edition = "2026"
name = "stdlib-${kindId}"

[[unit]]
name = "x"
kind = "${kindId}"
path = "units/x"
`)

      expect(result.ok).toBe(true)
      expect(result.diagnostics).toHaveLength(0)
    }
  })

  test('checkPackToml accepts a skill world import roundtrip', () => {
    const result = checkPackToml(`
edition = "2026"
name = "stdlib-skill-world"

[[unit]]
name = "x"
kind = "agent.skill"
path = "skills/x"

[world]
imports = ["skill:x"]
`)

    expect(result.ok).toBe(true)
    expect(result.diagnostics).toHaveLength(0)
  })
})
