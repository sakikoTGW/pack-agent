#!/usr/bin/env node
/**
 * 同 bin/packagent-mcp.js 的智能转发——MCP server 入口，目标是 mcp/server.ts 而不是 CLI。
 */
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))
const target = join(here, '..', 'mcp', 'server.ts')
const isWin = process.platform === 'win32'

function bunAvailable() {
  const probe = spawnSync('bun', ['--version'], { stdio: 'ignore', shell: isWin })
  return probe.status === 0
}

if (!bunAvailable()) {
  console.error(
    [
      '',
      'agent-pack MCP server runs its TypeScript source directly via Bun (no build step) — Bun was not found on PATH.',
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
