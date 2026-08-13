import { describe, expect, test } from 'bun:test'
import { diag, formatDiag } from '../../src/lang/diagnostics.js'

describe('diagnostics', () => {
  test('E-EDITION-UNKNOWN is stable', () => {
    const d = diag('E-EDITION-UNKNOWN', { edition: '1999' })
    expect(d.code).toBe('E-EDITION-UNKNOWN')
    expect(formatDiag(d)).toContain('1999')
  })

  test('formatDiag includes the error code string', () => {
    const d = diag('E-KIND-NOT-FOUND', { kind: 'demo.kind' })
    expect(formatDiag(d)).toContain('E-KIND-NOT-FOUND')
  })
})
