#!/usr/bin/env bun
/**
 * 把 DSH 插件打成 Node 能加载的 ESM。官方 bundle 交 index.js，不交 .ts。
 * Windows 下必须 spawn bun.exe（process.execPath），不能 spawn `bun`（那是 .cmd）。
 *
 * 两个入口：主插件（tools/skills/commands）和 PAD 管理侧门（inject apiProxy）。
 * 侧门单独打，因为它是 cordis.patch.yml 里独立的一行。
 */
import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { packAgentRoot } from '../../src/package-root.js'
import { packTmpRoot } from '../../src/tmp-root.js'

const root = packAgentRoot()
const tmp = packTmpRoot()

// Two packages: the projection manager, and the PAD management side door. The
// side door is separate because it must insert DSH's official apiproxy, which a
// manager-only profile never installs.
const entries = [
  { pkg: 'plugin', src: 'index.ts', out: 'index.js' },
  { pkg: 'gateway', src: 'index.ts', out: 'index.js' },
]

for (const { pkg, src, out } of entries) {
  const libDir = join(root, 'agent-pack-dsh', pkg, 'lib')
  mkdirSync(libDir, { recursive: true })
  const entry = join(root, 'agent-pack-dsh', pkg, 'src', src)
  const outfile = join(libDir, out)
  const r = spawnSync(
    process.execPath,
    [
      'build', entry,
      '--outfile', outfile,
      '--target', 'node',
      '--format', 'esm',
      // apiproxy is resolved at runtime from the profile that installed it.
      '--external', '@deepseek-ai/*',
    ],
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
}
