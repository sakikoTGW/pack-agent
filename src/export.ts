/**
 * 从当前项目扫描 L1 → 打成便携 pack（内嵌 bundle）。
 */
import { promises as fs } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import {
  RUNTIME_ADAPTERS,
  detectRuntimes,
  getAdapter,
  scanRuntime,
  scanUniversal,
  type RuntimeScan,
} from './adapters.js'
import { embedPortableFiles } from './portable.js'
import type { PackDoc } from './types.js'
import { DEFAULT_STATE_DIR } from './project.js'
import {
  filterPackBySelection,
  filterBundleFilesForPack,
  mergeHarnessFromCapture,
  type PackSelectManifest,
} from './select.js'
import { injectBootstrapIntoPack } from './bootstrap.js'
import { loadPackProjectConfig } from './project-config.js'
import { writePackLock } from './lock.js'
import { enrichPackVersions, PACK_SCHEMA_V02 } from './versioning.js'
import { mergeExperiencesFromCapture } from './experience.js'
import type { CaptureDeliver } from './types.js'
import { loadPackIgnore, filterByIgnore, ensureDefaultPackIgnore } from './pack-ignore.js'
import { resolvePackModules, filterPackByModules, parseModulesList, type PackModules } from './modules.js'
import { scanExtendedModules, mergeExtendedIntoPack, embedExtendedBundleFiles } from './scan-modules.js'
import {
  loadAgentsRegistry,
  resolveAgentForExport,
  requireAgentOrSelection,
} from './agents.js'
import { PackConflictError } from './errors.js'

async function readAgentPackCliVersion(): Promise<string> {
  try {
    const raw = await fs.readFile(join(import.meta.dir, '..', 'package.json'), 'utf8')
    return (JSON.parse(raw) as { version?: string }).version ?? '0.2.0'
  } catch {
    return '0.2.0'
  }
}

export type ExportOpts = {
  runtime?: string
  name?: string
  out?: string
  stateDir?: string
  noHarness?: boolean
  /** 选件 manifest 路径或对象 */
  select?: string | PackSelectManifest
  /** 合并抓包/蒸馏 L2（.agent-pack/capture 或 .ccui/packs） */
  withHarness?: boolean
  /** 抓包交付：skill=规则/harness 投射；experience=经验罐头（默认） */
  captureAs?: CaptureDeliver
  /** 同 withHarness 且 captureAs=skill */
  withExperience?: boolean
  /** 跳过自举 agent-pack skill（默认 sync/export 会带上） */
  noBootstrap?: boolean
  /** 模块开关：skills,hooks,memory 或 --no-memory */
  modules?: PackModules | string[]
  /** pack.ignore 路径（相对项目根） */
  ignoreFile?: string
  /** agents.yaml 里的 agent id — 只封该 agent 清单内的 skills/rules/mcp */
  agent?: string
  /** 显式全项目扫描（legacy；默认必须 --agent 或 select） */
  allowFullScan?: boolean
}

async function readJson<T>(p: string): Promise<T | null> {
  try {
    return JSON.parse(await fs.readFile(p, 'utf8')) as T
  } catch {
    return null
  }
}

const RUNTIME_SKILL_PICK_ORDER = [
  'codex',
  'claude-code',
  'cursor',
  'opencode',
  'hermes',
  'openclaw',
  'gemini-cli',
  'windsurf',
  'github-copilot',
  'astrbot',
  'generic-agents',
]

function runtimePickRank(id: string): number {
  const i = RUNTIME_SKILL_PICK_ORDER.indexOf(id)
  return i === -1 ? 999 : i
}

async function resolveRuntime(
  cwd: string,
  arg?: string,
  selection?: PackSelectManifest,
): Promise<{ id: string; detected: string[] }> {
  const detected = await detectRuntimes(cwd)
  if (arg && arg !== 'auto') return { id: arg, detected }

  const skillNames =
    selection?.skills && selection.skills !== '*' && Array.isArray(selection.skills)
      ? selection.skills
      : undefined

  if (skillNames?.length) {
    const wanted = new Set(skillNames)
    let bestId: string | null = null
    let bestHits = 0
    for (const id of detected) {
      const adapter = getAdapter(id)
      if (!adapter) continue
      const scan = await scanRuntime(cwd, adapter)
      const names = new Set(scan.skills.map(s => String(s.name || '')))
      const hits = [...wanted].filter(n => names.has(n)).length
      if (
        hits > bestHits ||
        (hits === bestHits && hits > 0 && bestId && runtimePickRank(id) < runtimePickRank(bestId)) ||
        (hits === bestHits && hits > 0 && !bestId)
      ) {
        bestHits = hits
        bestId = id
      }
    }
    if (bestId && bestHits > 0) return { id: bestId, detected }
    return { id: 'universal', detected }
  }

  const verified = detected.find(id => getAdapter(id)?.verified)
  return { id: verified ?? 'universal', detected }
}

function dedupePackSkills<T extends { name?: string }>(items: T[]): T[] {
  const seen = new Set<string>()
  return items.filter(s => {
    const n = String(s.name || '')
    if (!n || seen.has(n)) return false
    seen.add(n)
    return true
  })
}

/** Agent 清单里的 MCP 名可能落在别的 harness 配置（如 codex scan 读 toml，agent-pack 在 .mcp.json） */
async function enrichScanWithRequestedMcp(
  cwd: string,
  scan: RuntimeScan,
  wantedMcp: string[],
  detected: string[],
): Promise<RuntimeScan> {
  const wanted = new Set(wantedMcp)
  const have = new Set(scan.mcp.map(m => String(m.name || '')))
  const missing = [...wanted].filter(n => !have.has(n))
  if (!missing.length) return scan

  const merged = [...scan.mcp]
  const seen = new Set(have)
  for (const id of detected) {
    if (id === scan.runtime) continue
    const adapter = getAdapter(id)
    if (!adapter) continue
    const other = await scanRuntime(cwd, adapter)
    for (const m of other.mcp) {
      const name = String(m.name || '')
      if (!wanted.has(name) || seen.has(name)) continue
      seen.add(name)
      merged.push({ ...m, scope: m.scope ?? id })
    }
  }
  return { ...scan, mcp: merged }
}

function scanToPack(scan: RuntimeScan, runtimeId: string, name: string, detected: string[]): PackDoc {
  const adapter = getAdapter(runtimeId)
  return {
    schema: 'ccui-pack/v0.1',
    name,
    version: '0.2.0',
    runtime: { id: runtimeId, label: adapter?.label || runtimeId, verified: adapter?.verified ?? false },
    knowledge: {
      skills: dedupePackSkills(
        scan.skills.map(s => ({ name: s.name, source: 'path', ref: s.ref, scope: s.scope })),
      ),
      rules: scan.rules.map(r => ({ name: r.name, format: r.format, ref: r.ref, scope: r.scope })),
    },
    tools: { mcp: scan.mcp },
    harness: { base_system_prompt: '', tool_schemas: [], system_reminders: [] },
    meta: {
      exportedAt: new Date().toISOString(),
      source: 'filesystem',
      detectedRuntimes: detected,
      fidelity: 'L1',
    },
  }
}

/** 扫描 → 便携 pack（不写盘） */
export async function buildPackFromProject(cwd: string, opts: ExportOpts = {}): Promise<{
  pack: PackDoc
  outPath: string
  scan: RuntimeScan
  stats: Record<string, unknown>
  lockPath: string
}> {
  const stateDir = opts.stateDir ?? DEFAULT_STATE_DIR
  const projectCfg = await loadPackProjectConfig(cwd, stateDir)
  await ensureDefaultPackIgnore(cwd, stateDir)
  const ignore = await loadPackIgnore(cwd, projectCfg.ignore ?? opts.ignoreFile)
  const registry = await loadAgentsRegistry(cwd, stateDir)

  let selection: PackSelectManifest | undefined
  let agentAuthor: string | undefined
  let agentDescription: string | undefined
  let agentId: string | undefined
  let agentRuntime: string | undefined

  if (opts.agent) {
    if (!registry) {
      throw new PackConflictError({
        kind: 'agent-required',
        summary: `agent \`${opts.agent}\` requested but .agent-pack/agents.yaml is missing`,
        help: [
          'create .agent-pack/agents.yaml (see agent-pack agents init)',
          'or use agent-pack export --all for full-project scan',
        ],
      })
    }
    const resolved = resolveAgentForExport(registry, opts.agent)
    selection = resolved.select
    agentAuthor = resolved.author
    agentDescription = resolved.description
    agentId = resolved.profile.id
    agentRuntime = resolved.runtime
    if (resolved.profile.modules) {
      opts.modules = { ...(typeof opts.modules === 'object' && !Array.isArray(opts.modules) ? opts.modules : {}), ...resolved.profile.modules }
    }
    if (resolved.profile.captureAs && !opts.captureAs) {
      opts.captureAs = resolved.profile.captureAs
    }
  } else if (opts.select) {
    selection = typeof opts.select === 'string' ? await readJson<PackSelectManifest>(opts.select) ?? undefined : opts.select
  }

  requireAgentOrSelection({
    agent: opts.agent,
    select: selection,
    allowFullScan: opts.allowFullScan,
    registry,
  })

  const cliModules = Array.isArray(opts.modules)
    ? parseModulesList(opts.modules)
    : opts.modules
  const modules = resolvePackModules(
    projectCfg.modules,
    selection?.modules,
    cliModules,
  )
  if (selection?.modules) {
    for (const [k, v] of Object.entries(selection.modules)) {
      if (v !== undefined) (modules as Record<string, boolean>)[k] = v
    }
  }
  if (selection?.subagents?.length) {
    modules.subagents = true
  }

  const { id: runtimeId, detected } = await resolveRuntime(cwd, agentRuntime ?? opts.runtime, selection)

  let scan: RuntimeScan
  if (runtimeId === 'universal') {
    scan = await scanUniversal(cwd)
  } else {
    const adapter = getAdapter(runtimeId)
    if (!adapter) {
      throw new Error(`未知 runtime "${runtimeId}"。可选：${RUNTIME_ADAPTERS.map(a => a.id).join(', ')}, universal`)
    }
    scan = await scanRuntime(cwd, adapter)
  }

  if (
    selection?.mcp &&
    selection.mcp !== '*' &&
    Array.isArray(selection.mcp) &&
    selection.mcp.length
  ) {
    scan = await enrichScanWithRequestedMcp(cwd, scan, selection.mcp, detected)
  }

  const name = opts.name || selection?.name || projectCfg.name || `${basename(cwd) || 'agent'}-${runtimeId}`
  let pack = scanToPack(scan, runtimeId, name, detected)

  if (ignore.rules.length) {
    scan.skills = filterByIgnore(scan.skills, s => s.ref, ignore, cwd)
    scan.rules = filterByIgnore(scan.rules, r => r.ref, ignore, cwd)
    pack = scanToPack(scan, runtimeId, name, detected)
  }

  if (modules.hooks || modules.subagents || modules.memory || modules.settings || modules.transcripts) {
    const ext = await scanExtendedModules(cwd, ignore, {
      hooks: modules.hooks,
      subagents: modules.subagents,
      memory: modules.memory,
      settings: modules.settings,
      transcripts: modules.transcripts,
    })
    pack = await mergeExtendedIntoPack(cwd, pack, ext)
  }

  pack.modules = { ...modules }
  pack.version = projectCfg.version ?? pack.version ?? '0.2.0'
  if (projectCfg.channel) pack.channel = projectCfg.channel
  pack = await embedPortableFiles(pack, cwd)
  pack = await embedExtendedBundleFiles(cwd, pack)

  if (selection) {
    if (selection.name && !opts.name) pack.name = selection.name
    pack = filterPackBySelection(pack, selection)
    pack = filterBundleFilesForPack(pack)
  }

  if (agentId) {
    pack.agent = { id: agentId, harness: runtimeId }
    if (agentAuthor) pack.author = agentAuthor
    if (agentDescription) pack.description = agentDescription
    pack.meta = {
      ...pack.meta,
      agentId,
      ...(agentAuthor ? { author: agentAuthor } : {}),
    }
  }

  const wantCapture =
    opts.withHarness ||
    opts.withExperience ||
    opts.captureAs !== undefined ||
    selection?.harness ||
    selection?.experience ||
    selection?.captureAs !== undefined

  let captureAs: CaptureDeliver =
    opts.captureAs ??
    selection?.captureAs ??
    (selection?.harness ? 'skill' : selection?.experience ? 'experience' : undefined) ??
    projectCfg.policy?.captureAs ??
    'experience'

  if (wantCapture && !opts.noHarness) {
    if (captureAs === 'skill') {
      pack = await mergeHarnessFromCapture(cwd, pack, stateDir)
      pack.policy = { ...pack.policy, captureAs: 'skill' }
    } else {
      pack = await mergeExperiencesFromCapture(cwd, pack, stateDir)
      captureAs = 'experience'
    }
  }

  pack.policy = { ...pack.policy, captureAs, knowledgeAs: 'skill' }

  if (!opts.noBootstrap) {
    pack = await injectBootstrapIntoPack(pack, cwd)
  }

  pack = filterPackByModules(pack, modules)
  pack.modules = { ...modules }

  pack = await enrichPackVersions(cwd, pack, {
    cliVersion: await readAgentPackCliVersion(),
    minPackCli: projectCfg.constraints?.minAgentPackCli,
  })
  pack.schema = projectCfg.packSchema ?? PACK_SCHEMA_V02

  const outPath = opts.out || join(cwd, stateDir, 'exports', `${pack.name || name}.pack.json`)
  const lockPath = await writePackLock(cwd, pack, stateDir)

  const stats = {
    runtime: runtimeId,
    name: pack.name || name,
    version: pack.version,
    schema: pack.schema,
    skills: pack.knowledge?.skills?.length ?? 0,
    rules: pack.knowledge?.rules?.length ?? 0,
    mcp: pack.tools?.mcp?.length ?? 0,
    bundleFiles: pack.bundle?.files?.length ?? 0,
    fidelity: pack.meta?.fidelity ?? 'L1',
    bootstrap: pack.meta?.bootstrapSkills ?? null,
    captureAs: pack.policy?.captureAs,
    experiences: pack.experiences?.length ?? 0,
    hooks: pack.automation?.hooks?.length ?? 0,
    subagents: pack.agents?.subagents?.length ?? 0,
    memory: pack.memory?.files?.length ?? 0,
    modules: pack.modules,
    ignoreSource: ignore.source,
    packContentHash: pack.resolution?.packContentHash,
    lockPath,
    detected,
    agent: agentId ?? null,
    author: pack.author ?? null,
  }
  return { pack, outPath, scan, stats, lockPath }
}

/** 扫描 → 写 pack 文件 */
export async function exportPackFromProject(cwd: string, opts: ExportOpts = {}): Promise<{
  pack: PackDoc
  outPath: string
  stats: Record<string, unknown>
  lockPath: string
}> {
  const { pack, outPath, stats, lockPath } = await buildPackFromProject(cwd, opts)
  await fs.mkdir(dirname(outPath), { recursive: true })
  await fs.writeFile(outPath, JSON.stringify(pack, null, 2), 'utf8')
  return { pack, outPath, stats, lockPath }
}
