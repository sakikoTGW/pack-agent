/**
 * Agent 定义 — `.agent-pack/agents.yaml`
 * 一个 harness 里可有多个 agent；export/pack 必须指定 `--agent <id>`（或 `--all` / 显式 select）。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import YAML from 'yaml'
import type { CaptureDeliver } from './types.js'
import type { PackModules } from './modules.js'
import type { PackSelectManifest } from './select.js'
import { PackConflictError } from './errors.js'
import { DEFAULT_STATE_DIR } from './project.js'

export type AgentProfile = {
  id: string
  author?: string
  description?: string
  /** 覆盖 pack 输出名（默认用 agent id） */
  packName?: string
  runtime?: string
  skills?: string[] | '*'
  rules?: string[] | '*'
  mcp?: string[] | '*'
  subagents?: string[] | '*'
  captureAs?: CaptureDeliver
  modules?: PackModules
  bootstrap?: { skills?: string[] }
}

export type AgentsRegistry = {
  schema?: string
  agents: Record<string, Omit<AgentProfile, 'id'>>
}

const DEFAULT_AGENTS_REL = 'agents.yaml'

export function agentsYamlPath(cwd: string, stateDir = DEFAULT_STATE_DIR): string {
  return join(cwd, stateDir, DEFAULT_AGENTS_REL)
}

export async function loadAgentsRegistry(cwd: string, stateDir = DEFAULT_STATE_DIR): Promise<AgentsRegistry | null> {
  const path = agentsYamlPath(cwd, stateDir)
  try {
    const text = await fs.readFile(path, 'utf8')
    const doc = YAML.parse(text) as AgentsRegistry | null
    if (!doc || typeof doc !== 'object' || !doc.agents || typeof doc.agents !== 'object') {
      return null
    }
    return { schema: doc.schema ?? 'agent-pack/agents/v1', agents: doc.agents }
  } catch {
    return null
  }
}

export function listAgentProfiles(registry: AgentsRegistry): AgentProfile[] {
  return Object.entries(registry.agents).map(([id, body]) => ({ id, ...body }))
}

export function getAgentProfile(registry: AgentsRegistry, id: string): AgentProfile | null {
  const body = registry.agents[id]
  if (!body) return null
  return { id, ...body }
}

export function agentProfileToSelection(profile: AgentProfile): PackSelectManifest {
  const sel: PackSelectManifest = {
    name: profile.packName ?? profile.id,
    ...(profile.skills !== undefined ? { skills: profile.skills } : {}),
    ...(profile.rules !== undefined ? { rules: profile.rules } : {}),
    ...(profile.mcp !== undefined ? { mcp: profile.mcp } : {}),
    ...(profile.subagents !== undefined ? { subagents: profile.subagents } : {}),
    ...(profile.captureAs ? { captureAs: profile.captureAs } : {}),
    ...(profile.modules ? { modules: profile.modules } : {}),
  }
  return sel
}

export type ResolvedAgentExport = {
  profile: AgentProfile
  select: PackSelectManifest
  author?: string
  description?: string
  runtime?: string
}

export function resolveAgentForExport(registry: AgentsRegistry, agentId: string): ResolvedAgentExport {
  const profile = getAgentProfile(registry, agentId)
  if (!profile) {
    const known = Object.keys(registry.agents).sort()
    throw new PackConflictError({
      kind: 'agent-unknown',
      summary: `unknown agent \`${agentId}\``,
      context: known.length ? [`known agents: ${known.join(', ')}`] : ['agents.yaml has no entries'],
      help: [
        'list agents: agent-pack agents list',
        `add an entry under agents.${agentId} in .agent-pack/agents.yaml`,
      ],
    })
  }
  const hasIncludes =
    profile.skills !== undefined ||
    profile.rules !== undefined ||
    profile.mcp !== undefined ||
    profile.subagents !== undefined
  if (!hasIncludes) {
    throw new PackConflictError({
      kind: 'agent-empty',
      summary: `agent \`${agentId}\` has no skills/rules/mcp/subagents listed`,
      help: [
        `edit .agent-pack/agents.yaml and set agents.${agentId}.skills (and optional rules/mcp)`,
      ],
    })
  }
  return {
    profile,
    select: agentProfileToSelection(profile),
    author: profile.author,
    description: profile.description,
    runtime: profile.runtime,
  }
}

export function requireAgentOrSelection(opts: {
  agent?: string
  select?: unknown
  allowFullScan?: boolean
  registry: AgentsRegistry | null
}): void {
  if (opts.allowFullScan || opts.select || opts.agent) return
  const known = opts.registry ? Object.keys(opts.registry.agents).sort() : []
  throw new PackConflictError({
    kind: 'agent-required',
    summary: 'export/pack requires a target agent (harness may host many agents)',
    context: known.length ? [`defined agents: ${known.join(', ')}`] : ['no .agent-pack/agents.yaml found'],
    help: [
      'agent-pack export --agent <id>',
      'agent-pack agents list',
      'agent-pack pack --manifest select.json',
      'agent-pack export --all   # explicit full-project scan (legacy)',
    ],
  })
}

export const DEFAULT_AGENTS_YAML_TEMPLATE = `# agent-pack agent definitions (one harness, many agents)
schema: agent-pack/agents/v1

agents:
  example:
    author: you
    description: Short intro shown on the exported pack
    runtime: codex
    skills:
      - agent-pack
    rules:
      - AGENTS.md
    mcp: []
    captureAs: experience
`

export async function ensureAgentsYamlTemplate(cwd: string, stateDir = DEFAULT_STATE_DIR): Promise<string> {
  const path = agentsYamlPath(cwd, stateDir)
  try {
    await fs.access(path)
    return path
  } catch {
    /* create */
  }
  await fs.mkdir(join(cwd, stateDir), { recursive: true })
  await fs.writeFile(path, DEFAULT_AGENTS_YAML_TEMPLATE, 'utf8')
  return path
}
