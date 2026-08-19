/**
 * Spawned by dsh/tests/node-entry.ts. Must run under node, not bun.
 * argv: <module-href> <fixture.pack.json> <out-dir>
 */
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'

const href = process.argv[2]
const fixture = process.argv[3]
const out = process.argv[4]
if (!href || !fixture || !out) {
  console.error('usage: node dsh/tests/node-smoke.mjs <href> <fixture> <out>')
  process.exit(1)
}

mkdirSync(out, { recursive: true })
const m = await import(href)
if (m.name !== 'pack-agent') throw new Error('name ' + m.name)
if (!Array.isArray(m.inject) || !m.inject.includes('tools') || !m.inject.includes('skills')) {
  throw new Error('inject ' + String(m.inject))
}
if (typeof m.apply !== 'function') throw new Error('apply')

const tools = []
m.apply(
  {
    tools: {
      register(def) {
        if (!def?.output || typeof def.output.render !== 'function') {
          throw new Error(`tool ${def?.name} missing output.render`)
        }
        tools.push(def)
      },
    },
  },
  { cwd: out },
)

const compile = tools.find(t => t.name === 'packagent_compile')
if (!compile) throw new Error('missing packagent_compile')
const result = await compile.execute({ pack: fixture, out: join(out, 'bundle') })
if (!result?.ok || !result.dir) throw new Error('compile ' + JSON.stringify(result))
console.log('ok')
