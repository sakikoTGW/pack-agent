#!/usr/bin/env bun
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

function fail(msg: string): never {
  console.error(`✗ ${msg}`)
  process.exit(1)
}

const here = dirname(fileURLToPath(import.meta.url))
const home = 'E:\\tmp\\pack-agent\\newbie-scene1\\.pack-launcher\\instances\\frompost\\home'
const transport = new StdioClientTransport({
  command: 'bun',
  args: [join(here, 'index.ts')],
  env: { ...process.env, DSH_HOME: home },
  stderr: 'pipe',
})

const client = new Client({ name: 'dsh-tui-mcp-selftest', version: '0.0.0' })
await client.connect(transport)
try {
  const listed = await client.listTools()
  const names = listed.tools.map((t) => t.name).sort()
  for (const need of [
    'dsh_ping',
    'dsh_session_list',
    'dsh_session_prompt',
    'dsh_session_history',
    'dsh_wait_turn',
    'dsh_credentials_describe',
  ]) {
    if (!names.includes(need)) fail(`missing tool ${need}: ${names.join(',')}`)
  }
  const ping = await client.callTool({ name: 'dsh_ping', arguments: {} })
  const text = JSON.stringify(ping)
  if (/"token"\s*:/.test(text)) fail('token leaked from dsh_ping')
} finally {
  await client.close()
}

console.log('✓ mcp stdio')
