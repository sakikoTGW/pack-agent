import { resolve } from 'node:path'
import {
  RUNTIME_ADAPTERS,
  detectRuntimes,
  getAdapter,
  scanRuntime,
  scanUniversal,
} from '../src/adapters.js'
import { PACK_APPLY_SKIP } from '../src/project.js'
import { buildPackFromProject, exportPackFromProject } from '../src/export.js'
import { installPackFile } from '../src/install.js'
import { PackConflictError } from '../src/errors.js'
import { syncPack } from '../src/sync.js'
import { packFromProject } from '../src/pack.js'
import { diffLockFiles, diffPackFiles, formatDiffReport } from '../src/diff.js'
import type { PackSelectManifest } from '../src/select.js'
import {
  jsonToolResult,
  resolveProjectCwd,
  toExportOpts,
  toInstallOpts,
  toSyncOpts,
  toolError,
  toolConflictResult,
  type McpPackOpts,
} from './util.js'
import { ejectPack, packStatus } from '../src/eject.js'
import { applyExperienceOffset } from '../src/experience.js'

export async function handlePackDetect(args: { cwd?: string }) {
  const cwd = resolveProjectCwd(args.cwd)
  const detected = await detectRuntimes(cwd)
  const installable = detected.filter(id => !PACK_APPLY_SKIP.has(id))
  return jsonToolResult({
    ok: true,
    cwd,
    detected,
    installable,
    adapters: RUNTIME_ADAPTERS.map(a => ({
      id: a.id,
      label: a.label,
      verified: a.verified,
      skipL1: PACK_APPLY_SKIP.has(a.id),
    })),
  })
}

export async function handlePackScan(args: { cwd?: string; runtime?: string }) {
  const cwd = resolveProjectCwd(args.cwd)
  const detected = await detectRuntimes(cwd)
  const runtimeId = args.runtime?.trim()

  if (runtimeId && runtimeId !== 'universal') {
    const adapter = getAdapter(runtimeId)
    if (!adapter) {
      return toolError(`Unknown runtime "${runtimeId}"`, {
        choices: RUNTIME_ADAPTERS.map(a => a.id),
      })
    }
    const scan = await scanRuntime(cwd, adapter)
    return jsonToolResult({ ok: true, cwd, detected, scan })
  }

  if (runtimeId === 'universal') {
    const scan = await scanUniversal(cwd)
    return jsonToolResult({ ok: true, cwd, detected, scan })
  }

  const scans: Array<{ runtime: string; skills: unknown[]; rules: unknown[]; mcp: unknown[] }> = []
  for (const id of detected) {
    const adapter = getAdapter(id)
    if (!adapter) continue
    const scan = await scanRuntime(cwd, adapter)
    scans.push({
      runtime: id,
      skills: scan.skills,
      rules: scan.rules,
      mcp: scan.mcp,
    })
  }
  return jsonToolResult({ ok: true, cwd, detected, scans })
}

export async function handlePackExport(
  args: McpPackOpts & { name?: string; write?: boolean; dry_run?: boolean },
) {
  const cwd = resolveProjectCwd(args.cwd)
  const opts = toExportOpts(args)
  if (args.dry_run) {
    const built = await buildPackFromProject(cwd, opts)
    return jsonToolResult({
      ok: true,
      cwd,
      dry_run: true,
      outPath: built.outPath,
      stats: built.stats,
      scan: {
        runtime: built.scan.runtime,
        skills: built.scan.skills.map(s => s.name),
        rules: built.scan.rules.map(r => r.name),
        mcp: built.scan.mcp.map(m => m.name),
      },
    })
  }
  const { pack, outPath, stats } = await exportPackFromProject(cwd, opts)
  return jsonToolResult({
    ok: true,
    cwd,
    exportPath: outPath,
    name: pack.name,
    stats,
  })
}

export async function handlePackInstall(args: McpPackOpts & { pack_path: string }) {
  const cwd = resolveProjectCwd(args.cwd)
  if (!args.pack_path?.trim()) return toolError('pack_path is required')
  const abs = resolve(cwd, args.pack_path)
  try {
    const report = await installPackFile(cwd, abs, toInstallOpts(args))
    return jsonToolResult({ ok: report.ok, cwd, pack_path: abs, report })
  } catch (e) {
    if (e instanceof PackConflictError) {
      return toolConflictResult(e)
    }
    throw e
  }
}

export async function handlePackSync(
  args: McpPackOpts & { from?: string; name?: string },
) {
  const cwd = resolveProjectCwd(args.cwd)
  try {
    const report = await syncPack(cwd, toSyncOpts(args))
    return jsonToolResult({
      ok: report.ok,
      cwd,
      exportPath: report.exportPath,
      projected: report.projected,
      report,
    })
  } catch (e) {
    if (e instanceof PackConflictError) {
      return toolConflictResult(e)
    }
    throw e
  }
}

export async function handlePackSelect(
  args: McpPackOpts & {
    name?: string
    agent?: string
    skills?: string[]
    rules?: string[]
    mcp?: string[]
    install?: boolean
    with_harness?: boolean
  },
) {
  const cwd = resolveProjectCwd(args.cwd)
  if (args.agent) {
    const report = await packFromProject(cwd, {
      ...toExportOpts(args),
      agent: args.agent,
      install: args.install ?? false,
    })
    return jsonToolResult({
      ok: 'ok' in report ? report.ok : true,
      cwd,
      agent: args.agent,
      report,
    })
  }
  const select: PackSelectManifest = {
    ...(args.name ? { name: args.name } : {}),
    ...(args.skills?.length ? { skills: args.skills } : {}),
    ...(args.rules?.length ? { rules: args.rules } : {}),
    ...(args.mcp?.length ? { mcp: args.mcp } : {}),
    ...(args.with_harness ? { harness: true, captureAs: 'skill' as const } : {}),
    ...(args.capture_as && !args.with_harness ? { captureAs: args.capture_as } : {}),
  }
  const hasSelect =
    args.skills?.length || args.rules?.length || args.mcp?.length || args.with_harness
  const report = await packFromProject(cwd, {
    ...toExportOpts(args),
    select: hasSelect ? select : undefined,
    withHarness: args.with_harness,
    install: args.install ?? false,
  })
  return jsonToolResult({
    ok: 'ok' in report ? report.ok : true,
    cwd,
    selection: hasSelect ? select : undefined,
    report,
  })
}

export async function handlePackDiff(args: { left: string; right: string; cwd?: string }) {
  const cwd = resolveProjectCwd(args.cwd)
  if (!args.left?.trim() || !args.right?.trim()) {
    return toolError('left and right paths are required')
  }
  const left = resolve(cwd, args.left)
  const right = resolve(cwd, args.right)
  const isLock = (p: string) => p.endsWith('lock.json') || p.includes('lock.json')
  const report =
    isLock(left) && isLock(right)
      ? await diffLockFiles(left, right)
      : await diffPackFiles(left, right)
  return jsonToolResult({
    ok: true,
    cwd,
    left,
    right,
    report,
    summary: formatDiffReport(report),
  })
}

export async function handlePackEject(args: { cwd?: string; pack_name?: string; force?: boolean }) {
  const cwd = resolveProjectCwd(args.cwd)
  const report = await ejectPack(cwd, {
    packName: args.pack_name,
    force: args.force,
  })
  return jsonToolResult({
    ok: report.ok || report.summary.removed > 0,
    cwd,
    partial: report.summary.missing > 0 || report.summary.conflict > 0,
    report,
  })
}

export async function handlePackStatus(args: { cwd?: string; state_dir?: string }) {
  const cwd = resolveProjectCwd(args.cwd)
  const status = await packStatus(cwd, args.state_dir)
  return jsonToolResult({ ok: true, ...status })
}

export async function handlePackExperienceOffset(args: {
  cwd?: string
  experience_id: string
  weight?: number
  prompt_delta?: string
  reminders?: string[]
  state_dir?: string
}) {
  const cwd = resolveProjectCwd(args.cwd)
  if (!args.experience_id?.trim()) return toolError('experience_id is required')
  const ok = await applyExperienceOffset(cwd, args.experience_id, {
    ...(args.weight !== undefined ? { weight: args.weight } : {}),
    ...(args.prompt_delta ? { promptDelta: args.prompt_delta } : {}),
    ...(args.reminders?.length ? { reminders: args.reminders } : {}),
  }, args.state_dir)
  return jsonToolResult({ ok, cwd, experience_id: args.experience_id })
}
