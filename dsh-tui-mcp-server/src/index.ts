import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js'
import { z } from 'zod'
import {
  ALLOWED_RPC,
  callAllowed,
  describeCredentials,
  historySession,
  listSessions,
  pingStatus,
  promptSession,
  waitTurn,
} from './client.ts'

function ok(data: unknown) {
  let text = JSON.stringify(data, null, 2)
  if (text.length > 80_000) text = `${text.slice(0, 80_000)}\n…truncated`
  return { content: [{ type: 'text' as const, text }] }
}

function fail(e: unknown) {
  const text = e instanceof Error ? e.message : String(e)
  return { content: [{ type: 'text' as const, text }], isError: true }
}

const home = z.string().optional().describe('Instance DSH_HOME. Default: env DSH_HOME or PAD_GATEWAY_AD parent.')
const sessionId = z.string().min(1).describe('session.list row.sessionId')

const server = new McpServer({
  name: 'dsh-tui-mcp-server',
  version: '0.1.0',
})

server.registerTool(
  'dsh_ping',
  {
    title: 'Ping pad-gateway',
    description: 'Read $DSH_HOME/pad-gateway.json and GET /pad/ping. Confirms the running TUI management door is alive. Never returns the bearer token.',
    inputSchema: { home },
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false },
  },
  async ({ home }) => {
    try {
      return ok(await pingStatus(home))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_session_list',
  {
    title: 'List sessions',
    description: 'Official apiproxy session.list on the running instance. running=true means the agent is in a turn. blank=true means preset can still be switched.',
    inputSchema: { home },
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false },
  },
  async ({ home }) => {
    try {
      return ok(await listSessions(home))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_session_create',
  {
    title: 'Create session',
    description: 'Official session.create. Pass cwd or workspaceId, not both. Returns the new sessionId.',
    inputSchema: {
      home,
      cwd: z.string().optional().describe('Workspace path for the new session'),
      workspaceId: z.string().optional().describe('Existing workspace id'),
      agentPreset: z.string().optional(),
    },
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
  },
  async (args) => {
    try {
      const payload: Record<string, string> = {}
      if (args.cwd) payload.cwd = args.cwd
      if (args.workspaceId) payload.workspaceId = args.workspaceId
      if (args.agentPreset) payload.agentPreset = args.agentPreset
      return ok(await callAllowed({ home: args.home, method: 'session.create', payload }))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_session_prompt',
  {
    title: 'Prompt a session',
    description: 'Official session.prompt: enqueue one user text turn on the running TUI (mode queue|steer). Does not wait for the model. Follow with dsh_wait_turn or dsh_session_history.',
    inputSchema: {
      home,
      sessionId,
      text: z.string().min(1).describe('User message text'),
      mode: z.enum(['queue', 'steer']).optional().describe('queue (default) or steer'),
    },
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
  },
  async (args) => {
    try {
      return ok(await promptSession(args))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_session_history',
  {
    title: 'Read session history',
    description: 'Official session.history, compacted: skill-catalog dumps and tree-truncated events are dropped so the reply fits in context. Use beforeSeq to page older.',
    inputSchema: {
      home,
      sessionId,
      maxMessages: z.number().int().positive().max(100).optional(),
      beforeSeq: z.number().int().nonnegative().optional(),
    },
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false },
  },
  async (args) => {
    try {
      return ok(await historySession(args))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_wait_turn',
  {
    title: 'Wait for turn to finish',
    description: 'Poll session.list until this session is not running, then return compacted history. Use after dsh_session_prompt.',
    inputSchema: {
      home,
      sessionId,
      timeoutMs: z.number().int().min(500).max(120000).optional(),
    },
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false },
  },
  async (args) => {
    try {
      return ok(await waitTurn(args))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_session_cancel',
  {
    title: 'Cancel session turn',
    description: 'Official session.cancel on the running agent turn.',
    inputSchema: { home, sessionId },
    annotations: { readOnlyHint: false, destructiveHint: true, idempotentHint: true, openWorldHint: false },
  },
  async (args) => {
    try {
      return ok(await callAllowed({ home: args.home, method: 'session.cancel', payload: { sessionId: args.sessionId } }))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_session_models',
  {
    title: 'Session models',
    description: 'Official session.models: current selection, routable flag, provider groups. routable=false usually means the provider key is missing.',
    inputSchema: { home, sessionId },
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false },
  },
  async (args) => {
    try {
      return ok(await callAllowed({ home: args.home, method: 'session.models', payload: { sessionId: args.sessionId } }))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_credentials_describe',
  {
    title: 'Describe credentials',
    description: 'Official credentials.describe. Returns configured/source/writable only — never the secret value. Default ref is DEEPSEEK_API_KEY. credentials.set is not exposed.',
    inputSchema: {
      home,
      refs: z.array(z.string()).max(16).optional(),
    },
    annotations: { readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false },
  },
  async (args) => {
    try {
      return ok(await describeCredentials(args.home, args.refs))
    } catch (e) {
      return fail(e)
    }
  },
)

server.registerTool(
  'dsh_rpc',
  {
    title: 'Allowlisted apiproxy rpc',
    description: `Call one official apiproxy method through pad-gateway. Allowed: ${[...ALLOWED_RPC].join(', ')}. Blocked: credentials.set / settings.* / anything else.`,
    inputSchema: {
      home,
      method: z.string().min(1),
      payload: z.record(z.string(), z.unknown()).optional(),
    },
    annotations: { readOnlyHint: false, destructiveHint: false, idempotentHint: false, openWorldHint: false },
  },
  async (args) => {
    try {
      return ok(await callAllowed({ home: args.home, method: args.method, payload: args.payload }))
    } catch (e) {
      return fail(e)
    }
  },
)

const transport = new StdioServerTransport()
await server.connect(transport)

