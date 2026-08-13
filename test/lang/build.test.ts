import { describe, expect, test } from 'bun:test'
import { access, mkdir, mkdtemp, rm, readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { spawnSync } from 'node:child_process'
import { packTestTmp } from '../tmp-root.js'
import { buildPackToml } from '../../src/lang/build.js'

describe('buildPackToml', () => {
  test(
    'builds a minimal pack zip containing IR pack.json',
    async () => {
    const root = await mkdtemp(packTestTmp('pack-build-'))
    try {
      const outPath = join(root, 'demo.pack.zip')
      const text = `
edition = "2026"
name = "demo-pack"

[[unit]]
kind = "agent.skill"
path = "skills/demo"
`

      const result = await buildPackToml(text, { out: outPath })

      expect(result.ok).toBe(true)
      expect(result.diagnostics).toHaveLength(0)
      expect(result.zipPath).toBe(outPath)
      expect(result.ir?.schema).toBe('agent-pack-ir/2026')
      expect(result.ir?.name).toBe('demo-pack')

      await access(outPath)

      const extracted = join(root, 'unz')
      await mkdir(extracted, { recursive: true })
      const unzip = spawnSync('tar', ['-xf', outPath, '-C', extracted], { encoding: 'utf8' })
      expect(unzip.status).toBe(0)

      const packJson = await readFile(join(extracted, 'pack.json'), 'utf8')
      const ir = JSON.parse(packJson) as { schema: string; name: string }
      expect(ir.schema).toBe('agent-pack-ir/2026')
      expect(ir.name).toBe('demo-pack')
    } finally {
      await rm(root, { recursive: true, force: true })
    }
    },
    { timeout: 15000 },
  )
})
