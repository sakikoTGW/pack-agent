import { describe, expect, test } from 'bun:test'
import { parsePackToml } from '../../src/lang/parse.js'

describe('parsePackToml', () => {
  test('parses edition, name, and unit ref mapping', () => {
    const ast = parsePackToml(`
edition = "2026"
name = "demo-pack"
version = "1.2.3"

[[unit]]
kind = "agent.skill"
ref = "skills/demo"

[world]
exports = ["pack.export"]
imports = ["pack.import"]
`)

    expect(ast).toStrictEqual({
      edition: '2026',
      name: 'demo-pack',
      version: '1.2.3',
      units: [
        {
          name: 'skills/demo',
          kind: 'agent.skill',
          path: 'skills/demo',
        },
      ],
      world: {
        exports: ['pack.export'],
        imports: ['pack.import'],
      },
    })
  })

  test('parses world exports and imports', () => {
    const ast = parsePackToml(`
edition = "2026"
name = "world-pack"

[[unit]]
kind = "agent.skill"
ref = "skills/world"

[world]
exports = ["pack.export"]
imports = ["pack.import"]
`)

    expect(ast.world).toStrictEqual({
      exports: ['pack.export'],
      imports: ['pack.import'],
    })
  })

  test('returns a partial AST when name is missing', () => {
    const ast = parsePackToml(`
edition = "2026"

[[unit]]
kind = "agent.skill"
ref = "skills/nameless"
`)

    expect(ast.edition).toBe('2026')
    expect(ast.name).toBe('')
    expect(ast.units).toHaveLength(1)
  })

  test('throws on invalid TOML syntax', () => {
    expect(() =>
      parsePackToml(`
edition = "2026"
name = "broken"

[[unit]]
kind = "agent.skill"
ref = [
`)
    ).toThrow()
  })

  test('keeps unknown edition at parse time', () => {
    const ast = parsePackToml(`
edition = "1999"
name = "future-pack"

[[unit]]
kind = "agent.skill"
ref = "skills/future"
`)

    expect(ast.edition).toBe('1999')
    expect(ast.name).toBe('future-pack')
  })
})
