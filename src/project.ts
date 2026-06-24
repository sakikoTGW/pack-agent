/**
 * 整合包多 harness 投射 — 一个 pack，检测在场引擎，按适配表分别装 L1（+ L2 尽力）。
 */
import { promises as fs } from 'node:fs'
import { realpath } from 'node:fs/promises'
import { basename, dirname, join } from 'node:path'
import {
  detectRuntimes,
  getAdapter,
  runtimeProjectionDirs,
  type RuntimeAdapter,
} from './adapters.js'
import type { InstallOpts, PackDoc } from './types.js'
import type { CaptureDeliver } from './types.js'
import { resolveCaptureDeliver } from './experience.js'
import { materializePortableBundle, resolveRuleFile, resolveSkillDir } from './portable.js'
import { writeAstrbotPlugin } from './astrbot.js'
import {
  addHermesExternalDir,
  mcpTargetFor,
  mergeMcp,
  type McpFormat,
  type McpServers,
} from './projection.js'
import { appendMarkedBlock, packMarker, readSkillOriginMarker, writeSkillOriginMarker } from './markers.js'
import {
  buildSkillConflictDetail,
  resolveInstallConflict,
  type PackConflictDetail,
} from './errors.js'
import type { ConflictPolicy } from './types.js'

/** 装包时跳过（generic 会与具体 harness 重复；cursor 非 CLI 目标） */
export const PACK_APPLY_SKIP = new Set(['cursor', 'generic-agents'])

export type PackProjectOpts = InstallOpts & {
  stateDir?: string
  captureAs?: CaptureDeliver
  onConflict?: ConflictPolicy
}

export const DEFAULT_STATE_DIR = '.agent-pack'

export type RuntimeProjectReport = {
  runtime: string
  label: string
  skills: string[]
  rules: string[]
  mcp: string[]
  skipped: string[]
  harnessL2?: { path?: string; skipped?: string }
}

export type PackProjectManifest = {
  appliedAt: string
  name: string
  version?: string
  detected: string[]
  projected: string[]
  runtimes: Array<{
    runtime: string
    skillsDir?: string
    ruleDir?: string
    skills: string[]
    rules: string[]
    mcp: string[]
    mcpFileAbs?: string
    mcpFormat?: McpFormat
    astrbotPluginDirs?: string[]
    hermesExternalDir?: { configAbs: string; skillsAbs: string }
    harnessL2?: { path: string; kind: 'file' | 'append' }
    skipped: string[]
  }>
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

export async function resolveApplyRuntimes(cwd: string, opts: PackProjectOpts = {}): Promise<string[]> {
  if (opts.runtime) return [opts.runtime]
  if (opts.runtimes?.length) return opts.runtimes
  const detected = await detectRuntimes(cwd)
  return detected.filter(id => !PACK_APPLY_SKIP.has(id))
}

type ResolvedSkill = { name: string; dir: string }
type ResolvedRule = { name: string; abs: string; format: string }

async function resolvePackAssets(
  cwd: string,
  pack: PackDoc,
  stagingRoot: string | null,
): Promise<{ skills: ResolvedSkill[]; rules: ResolvedRule[]; skipped: string[] }> {
  const skills: ResolvedSkill[] = []
  const rules: ResolvedRule[] = []
  const skipped: string[] = []

  for (const s of pack.knowledge?.skills ?? []) {
    const skillName = String(s.name || '').trim()
    const ref = String(s.ref || '').trim()
    if (!skillName && !ref) {
      skipped.push('skill:? (no name/ref)')
      continue
    }
    const dir = await resolveSkillDir(cwd, skillName || 'unknown', ref, stagingRoot)
    if (!dir) {
      skipped.push(`skill:${skillName || ref} (missing)`)
      continue
    }
    skills.push({ name: basename(dir), dir })
  }

  for (const r of pack.knowledge?.rules ?? []) {
    const ruleName = String(r.name || '').trim()
    const ref = String(r.ref || '').trim()
    const format = String(r.format || 'md').trim()
    if (!ruleName && !ref) {
      skipped.push('rule:? (no name/ref)')
      continue
    }
    const abs = await resolveRuleFile(cwd, ruleName || basename(ref), ref, stagingRoot)
    if (!abs) {
      skipped.push(`rule:${ruleName || ref} (missing file)`)
      continue
    }
    rules.push({ name: basename(abs), abs, format })
  }

  return { skills, rules, skipped }
}

function packMcpServers(pack: PackDoc): McpServers {
  const out: McpServers = {}
  for (const m of pack.tools?.mcp ?? []) {
    const n = String(m.name || '').trim()
    if (!n) continue
    const cfg: Record<string, unknown> = {}
    if (m.url) {
      cfg.type = m.type || 'http'
      cfg.url = m.url
    } else if (m.command) {
      cfg.type = 'stdio'
      cfg.command = m.command
      if (m.args?.length) cfg.args = m.args
    } else continue
    if (m.env && Object.keys(m.env).length) cfg.env = m.env
    out[n] = cfg
  }
  return out
}

function adapterAcceptsFormat(adapter: RuntimeAdapter, format: string): boolean {
  const fmts = new Set(adapter.rules.map(r => r.format))
  if (fmts.has(format)) return true
  // agents-md 内容可投射到认 AGENTS.md / GEMINI.md / copilot 的引擎
  if (format === 'agents-md' && (fmts.has('agents-md') || fmts.has('gemini-md') || fmts.has('copilot-md'))) return true
  if (format === 'claude-md' && fmts.has('claude-md')) return true
  return format === 'mdc' && fmts.has('mdc')
}

async function sameResolvedPath(a: string, b: string): Promise<boolean> {
  try {
    const [ra, rb] = await Promise.all([realpath(a), realpath(b)])
    return ra.toLowerCase() === rb.toLowerCase()
  } catch {
    return join(a).replace(/\\/g, '/').toLowerCase() === join(b).replace(/\\/g, '/').toLowerCase()
  }
}

/** 安装 skill：冲突按 onConflict；同包幂等 skip */
async function copySkillDir(
  src: string,
  dest: string,
  origin: { packName: string; packVersion?: string; skillName: string; contentHash?: string },
  policy: ConflictPolicy,
  onResolved?: (action: 'skip' | 'replace', detail: PackConflictDetail) => void,
  runtime?: string,
): Promise<'copied' | 'skipped' | 'replaced' | 'conflict-skipped'> {
  const destSkillMd = join(dest, 'SKILL.md')

  const detectConflict = async (): Promise<PackConflictDetail | null> => {
    if (!(await exists(destSkillMd)) && !(await sameResolvedPath(src, dest))) return null
    const existing = await readSkillOriginMarker(dest)
    if (!existing) {
      if (await exists(destSkillMd)) {
        return buildSkillConflictDetail({
          kind: 'skill-handcrafted',
          dest,
          skillName: origin.skillName,
          packName: origin.packName,
          runtime,
        })
      }
      return null
    }
    if (existing.packName !== origin.packName) {
      return buildSkillConflictDetail({
        kind: 'skill-ownership',
        dest,
        skillName: origin.skillName,
        packName: origin.packName,
        runtime,
        ownerPack: existing.packName,
      })
    }
    if (
      origin.contentHash &&
      existing.contentHash &&
      origin.contentHash !== existing.contentHash
    ) {
      return buildSkillConflictDetail({
        kind: 'skill-version',
        dest,
        skillName: origin.skillName,
        packName: origin.packName,
        runtime,
        expectedHash: origin.contentHash,
        actualHash: existing.contentHash,
      })
    }
    return null
  }

  const conflict = await detectConflict()
  if (conflict) {
    const action = resolveInstallConflict(policy, conflict)
    onResolved?.(action, conflict)
    if (action === 'skip') return 'conflict-skipped'
    await fs.rm(dest, { recursive: true, force: true }).catch(() => {})
  } else if (await sameResolvedPath(src, dest) || (await exists(destSkillMd))) {
    await writeSkillOriginMarker(dest, origin)
    return 'skipped'
  }

  await fs.mkdir(dirname(dest), { recursive: true })
  await fs.cp(src, dest, { recursive: true, force: true, dereference: true })
  await writeSkillOriginMarker(dest, origin)
  return conflict ? 'replaced' : 'copied'
}

function skillOriginMeta(
  pack: PackDoc,
  packName: string,
  skillName: string,
): { packName: string; packVersion?: string; skillName: string; contentHash?: string } {
  const entry = pack.knowledge?.skills?.find(sk => String(sk.name) === skillName)
  return {
    packName,
    packVersion: pack.version,
    skillName,
    contentHash: entry?.contentHash,
  }
}

async function projectRules(
  cwd: string,
  runtime: string,
  adapter: RuntimeAdapter,
  ruleDir: string,
  rules: ResolvedRule[],
): Promise<{ applied: string[]; skipped: string[] }> {
  const applied: string[] = []
  const skipped: string[] = []
  const dirRules = adapter.rules.filter((r): r is { kind: 'dir'; token: string; exts: string[]; format: string } => r.kind === 'dir')
  const fileRules = adapter.rules.filter(r => r.kind === 'file')

  const hasDirTarget = dirRules.length > 0
  if (hasDirTarget) {
    await fs.mkdir(join(cwd, ruleDir), { recursive: true })
  }

  for (const rule of rules) {
    if (!adapterAcceptsFormat(adapter, rule.format)) {
      if (rule.format === 'mdc') skipped.push(`rule:${rule.name} (mdc 仅 Cursor，已跳过)`)
      else skipped.push(`rule:${rule.name} (${rule.format} 不被 ${runtime} 读取)`)
      continue
    }

    let placed = false

    for (const fr of fileRules) {
      const formatOk =
        fr.format === rule.format ||
        (rule.format === 'agents-md' && ['agents-md', 'gemini-md', 'copilot-md'].includes(fr.format)) ||
        (rule.format === 'claude-md' && fr.format === 'claude-md')
      if (!formatOk) continue
      if (fr.token.startsWith('~/')) continue
      const dest = join(cwd, fr.token)
      if (fr.token === 'AGENTS.md' || fr.token.endsWith('/AGENTS.md')) {
        await appendMarkedBlock(dest, packMarker('rule', rule.name), await fs.readFile(rule.abs, 'utf8'))
        applied.push(`${fr.token}←${rule.name}`)
        placed = true
        break
      }
      if (fr.token === 'GEMINI.md') {
        await appendMarkedBlock(dest, packMarker('rule', rule.name), await fs.readFile(rule.abs, 'utf8'))
        applied.push(`${fr.token}←${rule.name}`)
        placed = true
        break
      }
      if (fr.token === '.github/copilot-instructions.md') {
        await appendMarkedBlock(dest, packMarker('rule', rule.name), await fs.readFile(rule.abs, 'utf8'))
        applied.push(`${fr.token}←${rule.name}`)
        placed = true
        break
      }
      if (fr.token === 'CLAUDE.md') {
        await appendMarkedBlock(dest, packMarker('rule', rule.name), await fs.readFile(rule.abs, 'utf8'))
        applied.push(`${fr.token}←${rule.name}`)
        placed = true
        break
      }
    }

    if (!placed && hasDirTarget) {
      const destName = rule.format === 'mdc' ? rule.name.replace(/\.mdc$/, '.md') : rule.name
      const dest = join(cwd, ruleDir, destName)
      await fs.copyFile(rule.abs, dest)
      applied.push(destName)
      placed = true
    }

    if (!placed) skipped.push(`rule:${rule.name} (无 ${runtime} 投射位)`)
  }

  return { applied, skipped }
}

async function writeHarnessSidecars(
  cwd: string,
  stateDir: string,
  packName: string,
  pack: PackDoc,
): Promise<{ toolSchemasPath?: string; assemblyPath?: string }> {
  const safe = packName.replace(/[^\w.-]+/g, '_')
  const dir = join(cwd, stateDir, 'applied')
  await fs.mkdir(dir, { recursive: true })
  const out: { toolSchemasPath?: string; assemblyPath?: string } = {}

  const schemas = pack.harness?.tool_schemas ?? []
  if (schemas.length) {
    const toolSchemasPath = join(dir, `${safe}-tool-schemas.json`)
    await fs.writeFile(toolSchemasPath, JSON.stringify(schemas, null, 2), 'utf8')
    out.toolSchemasPath = toolSchemasPath
  }

  if (pack.assembly || pack.model) {
    const assemblyPath = join(dir, `${safe}-assembly.json`)
    await fs.writeFile(
      assemblyPath,
      JSON.stringify({ assembly: pack.assembly ?? null, model: pack.model ?? null }, null, 2),
      'utf8',
    )
    out.assemblyPath = assemblyPath
  }

  return out
}

async function projectHarnessL2(
  cwd: string,
  runtime: string,
  packName: string,
  pack: PackDoc,
  stateDir: string,
): Promise<{ path?: string; skipped?: string; kind?: 'file' | 'append'; sidecars?: string[] }> {
  const prompt = pack.harness?.base_system_prompt?.trim()
  const reminders = (pack.harness?.system_reminders ?? []).filter(Boolean)
  const schemas = pack.harness?.tool_schemas ?? []
  const hasL2 = Boolean(prompt && prompt.length >= 20) || reminders.length > 0 || schemas.length > 0
  if (!hasL2) return { skipped: '无 L2 脚手架' }

  const sidecar = await writeHarnessSidecars(cwd, stateDir, packName, pack)
  const sidecarLines: string[] = []
  if (sidecar.toolSchemasPath) {
    sidecarLines.push(`- tool_schemas: \`${sidecar.toolSchemasPath}\` (${schemas.length} entries)`)
  }
  if (sidecar.assemblyPath) {
    sidecarLines.push(`- assembly/model: \`${sidecar.assemblyPath}\``)
  }

  const body = [
    prompt,
    ...reminders.map(r => `\n---\n${r}`),
    sidecarLines.length
      ? `\n---\n## L2 sidecars (for replay / proxy bottleneck)\n${sidecarLines.join('\n')}`
      : '',
  ]
    .filter(Boolean)
    .join('\n')
    .trim()

  const marker = packMarker('harness', packName)
  const sidecars = [sidecar.toolSchemasPath, sidecar.assemblyPath].filter(Boolean) as string[]

  if (runtime === 'claude-code' || runtime === 'opencode') {
    const { ruleDir } = runtimeProjectionDirs(runtime)
    const file = join(cwd, ruleDir, `agent-pack-${packName.replace(/[^\w.-]+/g, '_')}-harness.md`)
    await fs.mkdir(join(cwd, ruleDir), { recursive: true })
    await fs.writeFile(file, `# Agent pack harness: ${packName}\n\n${body}\n`, 'utf8')
    return { path: file, kind: 'file', sidecars }
  }

  if (['codex', 'openclaw', 'hermes', 'generic-agents', 'gemini-cli', 'windsurf', 'github-copilot'].includes(runtime)) {
    const agentsMd = runtime === 'gemini-cli' ? join(cwd, 'GEMINI.md') : join(cwd, 'AGENTS.md')
    await appendMarkedBlock(agentsMd, marker, `# Agent pack: ${packName}\n\n${body}`)
    return { path: agentsMd, kind: 'append', sidecars }
  }

  if (runtime === 'astrbot') {
    return { skipped: 'AstrBot L2 请通过插件 README / WebUI 配置', sidecars: sidecars.length ? sidecars : undefined }
  }

  return { skipped: `${runtime} 无 L2 文件口（sidecar 已写）`, sidecars: sidecars.length ? sidecars : undefined }
}

async function projectToRuntime(
  cwd: string,
  pack: PackDoc,
  runtime: string,
  assets: { skills: ResolvedSkill[]; rules: ResolvedRule[] },
  stateDir: string,
  captureAs: CaptureDeliver | undefined,
  onConflict: ConflictPolicy,
  onResolved: (action: 'skip' | 'replace', detail: PackConflictDetail) => void,
): Promise<RuntimeProjectReport & { manifest: PackProjectManifest['runtimes'][0] }> {
  const adapter = getAdapter(runtime)
  const label = adapter?.label || runtime
  const skills: string[] = []
  const rules: string[] = []
  const mcp: string[] = []
  const skipped: string[] = []
  const packName = pack.name || 'unnamed-pack'
  const { skillsDir, ruleDir } = runtimeProjectionDirs(runtime)

  const man: PackProjectManifest['runtimes'][0] = {
    runtime,
    skillsDir,
    ruleDir,
    skills: [],
    rules: [],
    mcp: [],
    skipped: [],
  }

  if (runtime === 'astrbot') {
    const pluginsRoot = join(cwd, 'data', 'plugins')
    await fs.mkdir(pluginsRoot, { recursive: true })
    man.astrbotPluginDirs = []
    try {
      const r = await writeAstrbotPlugin(cwd, pack, pluginsRoot)
      man.astrbotPluginDirs.push(join('data', 'plugins', r.dirName))
      skills.push(...r.skills)
      man.skills.push(...r.skills)
    } catch (e) {
      skipped.push(`astrbot-plugin (${(e as Error).message})`)
    }
  } else if (runtime === 'hermes') {
    const stagingSkills = join(cwd, stateDir, 'applied-skills', packName.replace(/[^\w.-]+/g, '_'))
    await fs.rm(stagingSkills, { recursive: true, force: true }).catch(() => {})
    await fs.mkdir(stagingSkills, { recursive: true })
    for (const s of assets.skills) {
      const meta = skillOriginMeta(pack, packName, s.name)
      const r = await copySkillDir(
        s.dir,
        join(stagingSkills, s.name),
        meta,
        onConflict,
        onResolved,
        runtime,
      )
      if (r === 'conflict-skipped') skipped.push(`skill:${s.name} (conflict, skipped)`)
      else skills.push(s.name)
    }
    man.skills.push(...skills)
    man.skillsDir = stagingSkills
    const target = mcpTargetFor('hermes', cwd)
    const ok = await addHermesExternalDir(target.absFile, stagingSkills).catch(() => false)
    if (ok) man.hermesExternalDir = { configAbs: target.absFile, skillsAbs: stagingSkills }
    else if (skills.length) skipped.push('hermes external_dirs (config.yaml 不存在或未写入)')

    const ruleRes = await projectRules(cwd, runtime, adapter!, ruleDir, assets.rules)
    rules.push(...ruleRes.applied)
    man.rules.push(...ruleRes.applied)
    skipped.push(...ruleRes.skipped)
  } else {
    await fs.mkdir(join(cwd, skillsDir), { recursive: true })
    for (const s of assets.skills) {
      const dest = join(cwd, skillsDir, s.name)
      const meta = skillOriginMeta(pack, packName, s.name)
      const r = await copySkillDir(s.dir, dest, meta, onConflict, onResolved, runtime)
      if (r === 'conflict-skipped') skipped.push(`skill:${s.name} (conflict, skipped)`)
      else skills.push(s.name)
    }
    man.skills.push(...skills)

    if (adapter) {
      const ruleRes = await projectRules(cwd, runtime, adapter, ruleDir, assets.rules)
      rules.push(...ruleRes.applied)
      man.rules.push(...ruleRes.applied)
      skipped.push(...ruleRes.skipped)
    }
  }

  const servers = packMcpServers(pack)
  if (Object.keys(servers).length) {
    const target = mcpTargetFor(runtime, cwd)
    const res = await mergeMcp(target, servers, { packName, runtime, onConflict, onResolved })
    mcp.push(...res.added, ...res.unchanged)
    man.mcp.push(...res.added, ...res.unchanged)
    if (res.added.length) {
      man.mcpFileAbs = res.file
      man.mcpFormat = res.format
    }
  }

  const h2 =
    resolveCaptureDeliver(pack, captureAs) === 'experience' && (pack.experiences?.length ?? 0) > 0
      ? { skipped: '经验罐头：L2 不写 rules/skills；SessionStart hook 由 install 投射' }
      : await projectHarnessL2(cwd, runtime, packName, pack, stateDir)
  if (h2.path && h2.kind) man.harnessL2 = { path: h2.path, kind: h2.kind }

  man.skipped.push(...skipped)

  return {
    runtime,
    label,
    skills,
    rules,
    mcp,
    skipped,
    harnessL2: h2.path ? { path: h2.path } : h2.skipped ? { skipped: h2.skipped } : undefined,
    manifest: man,
  }
}

export async function projectPackToRuntimes(
  cwd: string,
  pack: PackDoc,
  opts: PackProjectOpts = {},
): Promise<{
  detected: string[]
  projected: string[]
  runtimes: RuntimeProjectReport[]
  skipped: string[]
  manifest: PackProjectManifest
  conflictsResolved: Array<{ action: 'skip' | 'replace'; detail: PackConflictDetail }>
}> {
  const detected = await detectRuntimes(cwd)
  const targets = await resolveApplyRuntimes(cwd, opts)
  const skipped: string[] = []
  const onConflict = opts.onConflict ?? 'stop'
  const conflictsResolved: Array<{ action: 'skip' | 'replace'; detail: PackConflictDetail }> = []
  const onResolved = (action: 'skip' | 'replace', detail: PackConflictDetail) => {
    conflictsResolved.push({ action, detail })
  }

  if (!targets.length) {
    return {
      detected,
      projected: [],
      runtimes: [],
      skipped: ['未检测到可安装的 harness（请先初始化 Claude Code / Codex / 等，或 --runtime 指定）'],
      manifest: {
        appliedAt: new Date().toISOString(),
        name: pack.name || 'unnamed-pack',
        version: pack.version,
        detected,
        projected: [],
        runtimes: [],
      },
      conflictsResolved: [],
    }
  }

  const stateDir = opts.stateDir ?? DEFAULT_STATE_DIR
  const stagingRoot = await materializePortableBundle(cwd, pack, stateDir)
  const assets = await resolvePackAssets(cwd, pack, stagingRoot)
  skipped.push(...assets.skipped)

  const runtimes: RuntimeProjectReport[] = []
  const manifestRuntimes: PackProjectManifest['runtimes'] = []

  for (const runtime of targets) {
    if (!getAdapter(runtime)) {
      skipped.push(`runtime:${runtime} (适配表无此项)`)
      continue
    }
    const report = await projectToRuntime(
      cwd,
      pack,
      runtime,
      assets,
      stateDir,
      opts.captureAs,
      onConflict,
      onResolved,
    )
    runtimes.push(report)
    manifestRuntimes.push(report.manifest)
  }

  const manifest: PackProjectManifest = {
    appliedAt: new Date().toISOString(),
    name: pack.name || 'unnamed-pack',
    version: pack.version,
    detected,
    projected: targets,
    runtimes: manifestRuntimes,
  }

  const manifestDir = join(cwd, stateDir, 'applied')
  await fs.mkdir(manifestDir, { recursive: true })
  await fs.writeFile(
    join(manifestDir, `${(pack.name || 'unnamed-pack').replace(/[^\w.-]+/g, '_')}.json`),
    JSON.stringify(manifest, null, 2),
    'utf8',
  )

  return { detected, projected: targets, runtimes, skipped, manifest, conflictsResolved }
}
