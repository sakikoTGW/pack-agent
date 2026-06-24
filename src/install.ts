import type { InstallOpts, InstallReport, PackDoc } from './types.js'
import { projectPackToRuntimes, type PackProjectOpts } from './project.js'
import { readPackFile, materializePortableBundle } from './portable.js'
import { injectBootstrapIntoPack } from './bootstrap.js'
import { writePackLock } from './lock.js'
import { installExperiences, resolveCaptureDeliver, distillTranscriptsToExperiences } from './experience.js'
import { projectExperienceToHarnesses, resolveExperienceRuntimes } from './experience-projection.js'
import { installExtendedModules } from './install-modules.js'
import { resolvePackModules, filterPackByModules } from './modules.js'
import { loadPackProjectConfig } from './project-config.js'
import {
  validateSkillRequires,
  validatePackSkillsResolvable,
  validateMinPackCli,
  readAgentPackCliVersion,
} from './requires-check.js'
import { bootstrapAgentPackMcp } from './mcp-bootstrap.js'
import { PackConflictError } from './errors.js'
import { buildInstallLedger, writeInstallLedger } from './install-ledger.js'

function aggregate(reports: InstallReport['runtimes']): Pick<InstallReport, 'skills' | 'rules' | 'mcp' | 'skipped'> {
  const skills = new Set<string>()
  const rules = new Set<string>()
  const mcp = new Set<string>()
  const skipped: string[] = []
  for (const r of reports) {
    for (const s of r.skills) skills.add(`${r.runtime}:${s}`)
    for (const x of r.rules) rules.add(`${r.runtime}:${x}`)
    for (const m of r.mcp) mcp.add(`${r.runtime}:${m}`)
    skipped.push(...r.skipped.map(x => `${r.runtime}: ${x}`))
  }
  return { skills: [...skills], rules: [...rules], mcp: [...mcp], skipped }
}

export async function installPack(cwd: string, pack: PackDoc, opts: InstallOpts = {}): Promise<InstallReport> {
  const name = pack.name || 'unnamed-pack'
  let doc = pack
  if (!opts.noBootstrap) {
    doc = await injectBootstrapIntoPack(pack, cwd)
  }
  const stateDir = opts.stateDir ?? '.agent-pack'
  const projectCfg = await loadPackProjectConfig(cwd, stateDir)
  const modules = resolvePackModules(projectCfg.modules, pack.modules, opts.modules)
  doc = filterPackByModules(doc, modules)

  const cliVer = await readAgentPackCliVersion()
  const minCli = validateMinPackCli(doc, cliVer, projectCfg.constraints?.minAgentPackCli)
  if (!minCli.ok) {
    throw new PackConflictError({
      kind: 'min-pack-cli',
      summary: `agent-pack CLI version too old for this pack`,
      context: [
        `pack requires agent-pack ${minCli.required}`,
        `installed agent-pack ${minCli.current}`,
      ],
      help: ['upgrade packages/pack-cli or the global agent-pack binary, then retry'],
    })
  }

  const stagingRoot = await materializePortableBundle(cwd, doc, stateDir)
  doc = await distillTranscriptsToExperiences(cwd, doc, stagingRoot)
  const resolvable = await validatePackSkillsResolvable(cwd, doc, stagingRoot)
  const requiresCheck = await validateSkillRequires(cwd, doc, stagingRoot)
  if (!resolvable.ok) {
    const first = resolvable.missing[0]
    throw new PackConflictError({
      kind: 'skill-unresolved',
      summary: `skill \`${first.name}\` cannot be resolved for install`,
      skillName: first.name,
      packName: name,
      context: resolvable.missing.map(m => `${m.name}: ${m.hint}`),
      help: [
        're-export on the source machine with embedPortableFiles (sync/export default)',
        'ensure bundle.files contains skills/<name>/SKILL.md before copying the pack elsewhere',
      ],
    })
  }
  if (!requiresCheck.ok) {
    const first = requiresCheck.missing[0]
    throw new PackConflictError({
      kind: 'requires-unmet',
      summary: `skill dependency \`${first.name}\` ${first.range} not satisfied`,
      skillName: first.name,
      packName: name,
      context: requiresCheck.missing.map(m => `${m.required}: ${m.hint ?? ''}`.trim()),
      help: [
        'include the dependency skill in the same pack export',
        'or install the dependency skill on the target project before installing this pack',
      ],
    })
  }

  const deliver = resolveCaptureDeliver(doc, opts.captureAs)
  const skipped: string[] = []
  const projectResult = await projectPackToRuntimes(cwd, doc, {
    ...(opts as PackProjectOpts),
    captureAs: deliver,
  })
  const { detected, projected, runtimes, skipped: projSkipped, manifest, conflictsResolved } = projectResult
  skipped.push(...projSkipped)

  const expInstalled = deliver === 'experience' ? await installExperiences(cwd, doc, stateDir) : []
  const expRuntimes =
    expInstalled.length > 0 ? resolveExperienceRuntimes(detected, projected, opts.runtimes ?? (opts.runtime ? [opts.runtime] : undefined)) : []
  const expProjection =
    expInstalled.length > 0
      ? await projectExperienceToHarnesses(cwd, expRuntimes, stateDir)
      : { wired: [], skipped: [] }
  const extInstalled = await installExtendedModules(cwd, doc, stateDir)
  const lockPath = await writePackLock(cwd, doc, stateDir).catch(() => undefined)

  const bootstrapMcp = opts.bootstrapMcp !== false
  const mcpBoot = bootstrapMcp ? await bootstrapAgentPackMcp(cwd, { skipIfPresent: true }) : { wired: [], skipped: [] }

  const agg = aggregate(runtimes)
  const partialReport: InstallReport = {
    ok: false,
    name,
    detected,
    projected,
    runtimes,
    skills: agg.skills,
    rules: agg.rules,
    mcp: agg.mcp,
    skipped: [...skipped, ...agg.skipped, ...extInstalled.skipped, ...expProjection.skipped, ...mcpBoot.skipped],
    lockPath,
    experiences: expInstalled.map(e => ({ id: e.id, path: e.path })),
    experienceHooks: expProjection.wired.map(w => `${w.runtime}:${w.config}#${w.event}`),
    captureDeliver: deliver,
    hooks: extInstalled.hooks,
    subagents: extInstalled.subagents,
    memory: extInstalled.memory,
    settings: extInstalled.settings,
    requiresCheck: { satisfied: requiresCheck.satisfied, missing: requiresCheck.missing },
    mcpBootstrap: mcpBoot.wired,
    conflictsResolved,
  }

  const ok =
    projected.length > 0 &&
    (runtimes.some(r => r.skills.length + r.rules.length + r.mcp.length > 0) ||
      expInstalled.length > 0 ||
      expProjection.wired.length > 0 ||
      extInstalled.hooks.length + extInstalled.subagents.length + extInstalled.memory.length > 0)

  partialReport.ok = ok

  const ledger = buildInstallLedger({
    pack: doc,
    manifest,
    install: partialReport,
    stateDir,
    expProjection,
    extInstalled,
    mcpBootstrapFiles: mcpBoot.wired,
    stagingRoot,
  })
  partialReport.ledgerPath = await writeInstallLedger(cwd, ledger, stateDir)
  partialReport.ejectHint = `卸载: agent-pack eject --name ${name} 或 MCP pack_eject`

  let harnessPresetHint: string | undefined
  const prompt = doc.harness?.base_system_prompt?.trim()
  if (deliver === 'skill' && prompt && prompt.length > 20) {
    const l2Paths = runtimes.map(r => r.harnessL2?.path).filter(Boolean)
    harnessPresetHint =
      l2Paths.length > 0
        ? `Pack「${name}」L2 已写入：${l2Paths.join(', ')}`
        : `Pack「${name}」含 L2 脚手架，当前引擎无文件口可读。`
  } else if (deliver === 'experience' && expInstalled.length) {
    const hookN = expProjection.wired.length
    harnessPresetHint =
      hookN > 0
        ? `Pack「${name}」经验罐头 ${expInstalled.length} 个 + ${hookN} 路 harness 会话注入（非 skill）`
        : `Pack「${name}」经验罐头已装入 .agent-pack/experiences/（${expInstalled.length} 个；harness hook 未接上：${expProjection.skipped.join('; ') || '无投射目标'}）`
  }
  partialReport.harnessPresetHint = harnessPresetHint

  return partialReport
}

export async function installPackFile(cwd: string, path: string, opts: InstallOpts = {}): Promise<InstallReport> {
  const pack = await readPackFile(path)
  return installPack(cwd, pack, opts)
}

export { readPackFile } from './portable.js'
