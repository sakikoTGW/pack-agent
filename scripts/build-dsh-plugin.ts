#!/usr/bin/env bun
/**
 * 把 DSH 插件打成 Node 能加载的 ESM。官方 bundle 交 index.js，不交 .ts。
 * Windows 下必须 spawn bun.exe（process.execPath），不能 spawn `bun`（那是 .cmd）。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { packAgentRoot } from '../src/package-root.js'
import { packTmpRoot } from '../src/tmp-root.js'

const root = packAgentRoot()
const entry = join(root, 'dsh-plugin', 'src', 'index.ts')
const outfile = join(root, 'dsh-plugin', 'lib', 'index.js')
mkdirSync(join(root, 'dsh-plugin', 'lib'), { recursive: true })
const tmp = packTmpRoot()
const r = spawnSync(
  process.execPath,
  ['build', entry, '--outfile', outfile, '--target', 'node', '--format', 'esm'],
  {
    cwd: root,
    stdio: 'inherit',
    env: { ...process.env, TMP: tmp, TEMP: tmp, TMPDIR: tmp, BUN_TMPDIR: tmp },
  },
)
if (r.status !== 0) {
  if (r.error) console.error(r.error)
  process.exit(r.status ?? 1)
}
if (!existsSync(outfile)) {
  console.error(`missing ${outfile}`)
  process.exit(1)
}
console.log(outfile)
