#!/usr/bin/env node
/**
 * 同 bin/packagent.js —— node 打不开 TS 源码，先探测 bun 再转发，没装 bun 给清楚指引。
 * 两个 bin 名字（packagent / agent-pack）指向同一套逻辑，这里保持独立文件方便各自维护。
 */
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))
const target = join(here, '..', 'src', 'cli.ts')
const isWin = process.platform === 'win32'

function bunAvailable() {
  const probe = spawnSync('bun', ['--version'], { stdio: 'ignore', shell: isWin })
  return probe.status === 0
}

if (!bunAvailable()) {
  console.error(
    [
      '',
      'agent-pack CLI runs its TypeScript source directly via Bun (no build step) — Bun was not found on PATH.',
      '',
      'Install Bun, then re-run this command:',
      '  curl -fsSL https://bun.sh/install | bash        # macOS / Linux',
      '  powershell -c "irm bun.sh/install.ps1 | iex"     # Windows',
      '',
      'Docs: https://bun.sh',
    ].join('\n'),
  )
  process.exit(1)
}

const result = spawnSync('bun', [target, ...process.argv.slice(2)], {
  stdio: 'inherit',
  shell: isWin,
})

process.exit(result.status ?? 1)
