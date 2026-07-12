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
import { PackConflictError, buildFileErrorDetail } from './errors.js'
import { DEFAULT_STATE_DIR } from './project.js'

function isEnoent(e: unknown): boolean {
  return Boolean(e && typeof e === 'object' && (e as NodeJS.ErrnoException).code === 'ENOENT')
}

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

/**
 * 读 agents.yaml —— **不存在**（ENOENT）才当「无 registry」返回 null，
 * 让调用方走「agents init」的引导。文件**存在但解析失败**（YAML 语法错等）
 * 绝不能悄悄当成"没有"——那样用户会被 requireAgentOrSelection 误导去 `agents init`，
 * 覆盖/掩盖一个其实已经写了内容、只是有语法错的文件。
 */
export async function loadAgentsRegistry(cwd: string, stateDir = DEFAULT_STATE_DIR): Promise<AgentsRegistry | null> {
  const path = agentsYamlPath(cwd, stateDir)
  let text: string
  try {
    text = await fs.readFile(path, 'utf8')
  } catch (e) {
    if (isEnoent(e)) return null
    throw new PackConflictError(
      buildFileErrorDetail({
        kind: 'file-invalid',
        what: 'agents.yaml',
        path,
        cause: e as Error,
        help: [`fix the read error at \`${path}\` (permissions? disk?), or delete it and run \`agent-pack agents init\``],
      }),
    )
  }
  let doc: AgentsRegistry | null
  try {
    doc = YAML.parse(text) as AgentsRegistry | null
  } catch (e) {
    throw new PackConflictError(
      buildFileErrorDetail({
        kind: 'file-invalid',
        what: 'agents.yaml',
        path,
        cause: e as Error,
        help: [`fix the YAML syntax at \`${path}\``, `or back it up and run \`agent-pack agents init\` to regenerate a template`],
      }),
    )
  }
  if (!doc || typeof doc !== 'object' || !doc.agents || typeof doc.agents !== 'object') {
    // 语法上合法的 YAML，只是没有 agents: 段——视为「还没定义任何 agent」，不是错误。
    return null
  }
  return { schema: doc.schema ?? 'agent-pack/agents/v1', agents: doc.agents }
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
