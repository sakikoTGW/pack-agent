#!/usr/bin/env bun
/**
 * agent-pack MCP server — agent 主入口（替代 Bash CLI hook）。
 *
 * Cursor / Claude Code 配置示例：
 * {
 *   "mcpServers": {
 *     "agent-pack": {
 *       "command": "bun",
 *       "args": ["<repo>/packages/pack-cli/mcp/server.ts"],
 *       "env": { "AGENT_PACK_CWD": "<project-root>" }
 *     }
 *   }
 * }
 */
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js'
import { z } from 'zod'
import {
  handlePackDetect,
  handlePackDiff,
  handlePackExport,
  handlePackInstall,
  handlePackScan,
  handlePackSelect,
  handlePackSync,
  handlePackEject,
  handlePackExperienceOffset,
  handlePackStatus,
} from './handlers.js'

const captureAsSchema = z.enum(['skill', 'experience']).optional()

const conflictSchema = z.enum(['stop', 'skip', 'replace']).optional()

const commonPackFields = {
  cwd: z.string().optional().describe('Project root (default: AGENT_PACK_CWD or process cwd)'),
  runtime: z.string().optional().describe('Target harness id, e.g. claude-code, codex, cursor'),
  agent: z.string().optional().describe('Agent id from .agent-pack/agents.yaml (required unless all=true or from=)'),
  all: z.boolean().optional().describe('Full-project scan (legacy)'),
  capture_as: captureAsSchema.describe('Capture deliver: skill=rules/harness, experience=ambient cans'),
  no_bootstrap: z.boolean().optional().describe('Skip injecting agent-pack bootstrap skill'),
  modules: z.array(z.string()).optional().describe('Module toggles: skills,hooks,memory,...'),
  state_dir: z.string().optional().describe('State directory (default .agent-pack)'),
  on_conflict: conflictSchema.describe('On file/MCP conflict: stop (default), skip, or replace'),
}

export function createServer(): McpServer {
  const server = new McpServer({
    name: 'agent-pack-mcp-server',
    version: '0.2.0',
  })

  server.registerTool(
    'pack_detect',
    {
      title: 'Detect harnesses',
      description:
        'Detect which agent harnesses are present in a project and list supported adapters. Use before export/install.',
      inputSchema: {
        cwd: commonPackFields.cwd,
      },
    },
    async args => handlePackDetect(args),
  )

  server.registerTool(
    'pack_scan',
    {
      title: 'Scan L1 resources',
      description:
        'Scan skills, rules, and MCP servers in the project. Optional runtime filter; omit to scan all detected harnesses.',
      inputSchema: {
        cwd: commonPackFields.cwd,
        runtime: commonPackFields.runtime,
      },
    },
    async args => handlePackScan(args),
  )

  server.registerTool(
    'pack_export',
    {
      title: 'Export portable pack',
      description: 'Scan project and write .agent-pack/exports/<name>.pack.json (does not install).',
      inputSchema: {
        ...commonPackFields,
        name: z.string().optional().describe('Pack name (default from project or folder)'),
        dry_run: z.boolean().optional().describe('Build pack in memory only, do not write file'),
      },
    },
    async args => handlePackExport(args),
  )

  server.registerTool(
    'pack_install',
    {
      title: 'Install pack',
      description: 'Install an existing .pack.json to all detected harnesses (skills + experience hooks).',
      inputSchema: {
        ...commonPackFields,
        pack_path: z.string().describe('Path to pack.json relative to cwd or absolute'),
      },
    },
    async args => handlePackInstall(args),
  )

  server.registerTool(
    'pack_sync',
    {
      title: 'Sync (export + install)',
      description:
        'Full pipeline: scan → portable pack → install to detected harnesses. Or install-only with from=existing pack.',
      inputSchema: {
        ...commonPackFields,
        name: z.string().optional(),
        from: z.string().optional().describe('Existing pack path; skip export when set'),
      },
    },
    async args => handlePackSync(args),
  )

  server.registerTool(
    'pack_select',
    {
      title: 'Selective pack',
      description:
        'Pack only chosen skills/rules/MCP. Set install=true to install immediately after export.',
      inputSchema: {
        ...commonPackFields,
        name: z.string().optional(),
        skills: z.array(z.string()).optional(),
        rules: z.array(z.string()).optional(),
        mcp: z.array(z.string()).optional(),
        install: z.boolean().optional(),
        with_harness: z.boolean().optional().describe('Same as capture_as=skill for capture merge'),
      },
    },
    async args => handlePackSelect(args),
  )

  server.registerTool(
    'pack_diff',
    {
      title: 'Diff packs or locks',
      description: 'Compare two pack.json or lock.json files.',
      inputSchema: {
        cwd: commonPackFields.cwd,
        left: z.string().describe('First pack or lock path'),
        right: z.string().describe('Second pack or lock path'),
      },
    },
    async args => handlePackDiff(args),
  )

  server.registerTool(
    'pack_eject',
    {
      title: 'Eject / uninstall pack',
      description:
        'Reverse install using install-ledger. Missing files → status missing (partial OK). force=true removes conflict skill dirs.',
      inputSchema: {
        cwd: commonPackFields.cwd,
        pack_name: z.string().optional().describe('Defaults from lock.json'),
        force: z.boolean().optional(),
      },
    },
    async args => handlePackEject(args),
  )

  server.registerTool(
    'pack_status',
    {
      title: 'Pack install status',
      description: 'Read lock, install-ledger, experiences index, MCP bootstrap paths.',
      inputSchema: {
        cwd: commonPackFields.cwd,
        state_dir: commonPackFields.state_dir,
      },
    },
    async args => handlePackStatus(args),
  )

  server.registerTool(
    'pack_experience_offset',
    {
      title: 'Tune experience offset',
      description: 'Adjust weight / promptDelta / reminders on an installed experience can.',
      inputSchema: {
        cwd: commonPackFields.cwd,
        state_dir: commonPackFields.state_dir,
        experience_id: z.string(),
        weight: z.number().optional(),
        prompt_delta: z.string().optional(),
        reminders: z.array(z.string()).optional(),
      },
    },
    async args => handlePackExperienceOffset(args),
  )

  return server
}

async function main(): Promise<void> {
  const server = createServer()
  const transport = new StdioServerTransport()
  await server.connect(transport)
}

if (import.meta.main) {
  main().catch(err => {
    console.error('[agent-pack-mcp]', (err as Error).message)
    process.exit(1)
  })
}
