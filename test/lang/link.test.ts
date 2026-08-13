import { describe, expect, test } from 'bun:test'
import { checkPackToml } from '../../src/lang/check.js'

describe('ABI link in checkPackToml', () => {
  test('reports unsatisfied world import when no unit provides it', () => {
    const result = checkPackToml(`
edition = "2026"
name = "demo-pack"

[[unit]]
name = "other"
kind = "agent.skill"
path = "skills/other"

[world]
imports = ["skill:missing"]
`)

    expect(result.ok).toBe(false)
    expect(result.diagnostics.some((diag) => diag.code === 'E-ABI-UNSATISFIED')).toBe(true)
  })

  test('reports duplicate exported symbol conflict', () => {
    const result = checkPackToml(`
edition = "2026"
name = "demo-pack"

[[unit]]
name = "dup"
kind = "agent.skill"
path = "skills/dup-a"

[[unit]]
name = "dup"
kind = "agent.skill"
path = "skills/dup-b"

[world]
exports = ["skill:dup"]
`)

    expect(result.ok).toBe(false)
    expect(result.diagnostics.some((diag) => diag.code === 'E-ABI-CONFLICT')).toBe(true)
  })

  test('accepts matching world import and export symbols', () => {
    const result = checkPackToml(`
edition = "2026"
name = "demo-pack"

[[unit]]
name = "foo"
kind = "agent.skill"
path = "skills/foo"

[world]
exports = ["skill:foo"]
imports = ["skill:foo"]
`)

    expect(result.ok).toBe(true)
    expect(result.diagnostics).toHaveLength(0)
  })
})
