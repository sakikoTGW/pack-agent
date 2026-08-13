import { describe, expect, test } from 'bun:test'
import { upgradePackDocToIr } from '../../src/index.js'

describe('upgradePackDocToIr', () => {
  test('upgrades a minimal v0.2 pack doc to agent-pack-ir/2026', () => {
    const ir = upgradePackDocToIr({
      schema: 'ccui-pack/v0.2',
      name: 'demo-pack',
      knowledge: {
        skills: [{ name: 'demo-skill', ref: 'skills/demo' }],
      },
    })

    expect(ir.schema).toBe('agent-pack-ir/2026')
    expect(ir.edition).toBe('2026')
    expect(ir.units).toHaveLength(1)
    expect(ir.units[0]?.kind).toBe('agent.skill')
  })
})
