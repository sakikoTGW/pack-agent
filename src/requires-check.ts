/**
 * skill requires[] 安装前校验 + 自搓 skill / 便携包兜底说明。
 */
import { basename, join } from 'node:path'
import type { PackDoc } from './types.js'
import { parseRequiresEntry, satisfiesVersion } from './semver.js'
import { resolveSkillDir, skillHasBundleFiles } from './portable.js'
import { parseSkillFrontmatter } from './versioning.js'
import { promises as fs } from 'node:fs'

export type RequiresSatisfiedVia = 'bundled' | 'local' | 'target-installed'

export type RequiresCheckEntry = {
  required: string
  name: string
  range: string
  via?: RequiresSatisfiedVia
  version?: string
  hint?: string
}

export type RequiresCheckResult = {
  ok: boolean
  satisfied: RequiresCheckEntry[]
  missing: RequiresCheckEntry[]
}

export function listPackSkillNames(pack: PackDoc): Set<string> {
  return new Set((pack.knowledge?.skills ?? []).map(s => String(s.name || '').trim()).filter(Boolean))
}

async function readInstalledSkillVersion(cwd: string, skillName: string): Promise<string | undefined> {
  for (const base of ['.claude/skills', '.agents/skills', '.cursor/skills', '.gemini/skills']) {
    const skillMd = join(cwd, base, skillName, 'SKILL.md')
    try {
      const content = await fs.readFile(skillMd, 'utf8')
      const fm = parseSkillFrontmatter(content)
      return fm.version || undefined
    } catch {
      /* next */
    }
  }
  return undefined
}

function collectRequiresFromPack(pack: PackDoc): RequiresCheckEntry[] {
  const out: RequiresCheckEntry[] = []
  for (const s of pack.knowledge?.skills ?? []) {
    for (const raw of s.requires ?? []) {
      const parsed = parseRequiresEntry(raw)
      if (!parsed) continue
      out.push({ required: raw, name: parsed.name, range: parsed.range })
    }
    // frontmatter requires in bundle
    const bundleSkill = pack.bundle?.files?.find(f => f.path === `skills/${s.name}/SKILL.md`)
    if (bundleSkill && !s.requires?.length) {
      const fm = parseSkillFrontmatter(bundleSkill.content)
      for (const raw of (fm.requires || '').split(/[,;\s]+/).filter(Boolean)) {
        const parsed = parseRequiresEntry(raw)
        if (!parsed) continue
        out.push({ required: raw, name: parsed.name, range: parsed.range })
      }
    }
  }
  const seen = new Set<string>()
  return out.filter(e => {
    const k = `${e.name}@${e.range}`
    if (seen.has(k)) return false
    seen.add(k)
    return true
  })
}

export async function validateSkillRequires(
  cwd: string,
  pack: PackDoc,
  stagingRoot: string | null,
): Promise<RequiresCheckResult> {
  const packSkills = listPackSkillNames(pack)
  const satisfied: RequiresCheckEntry[] = []
  const missing: RequiresCheckEntry[] = []

  for (const req of collectRequiresFromPack(pack)) {
    if (packSkills.has(req.name)) {
      const entry = pack.knowledge?.skills?.find(s => String(s.name) === req.name)
      const ver = entry?.version || '0.0.0'
      if (satisfiesVersion(ver, req.range)) {
        satisfied.push({ ...req, via: 'bundled', version: ver })
        continue
      }
      missing.push({
        ...req,
        hint: `依赖 skill「${req.name}」已在包内但版本 ${ver} 不满足 ${req.range}；请升级包内 skill 或放宽 requires`,
      })
      continue
    }

    if (skillHasBundleFiles(pack, req.name)) {
      satisfied.push({ ...req, via: 'bundled', version: 'bundle' })
      continue
    }

    const localDir = await resolveSkillDir(cwd, req.name, '', stagingRoot)
    if (localDir) {
      let ver = '0.0.0'
      try {
        const fm = parseSkillFrontmatter(await fs.readFile(join(localDir, 'SKILL.md'), 'utf8'))
        ver = fm.version || ver
      } catch {
        /* keep default */
      }
      if (satisfiesVersion(ver, req.range)) {
        satisfied.push({ ...req, via: 'local', version: ver })
        continue
      }
      missing.push({
        ...req,
        hint: `本机有 skill「${req.name}」但版本 ${ver} 不满足 ${req.range}`,
      })
      continue
    }

    const installedVer = await readInstalledSkillVersion(cwd, req.name)
    if (installedVer && satisfiesVersion(installedVer, req.range)) {
      satisfied.push({ ...req, via: 'target-installed', version: installedVer })
      continue
    }

    missing.push({
      ...req,
      hint:
        `依赖 skill「${req.name}」${req.range} 未找到。` +
        `自搓 skill 无法「下载」——须 (1) 导出时 embed 进 bundle（export/sync 默认便携化），` +
        `(2) 或把依赖 skill 一并选进 pack，(3) 或目标机已安装该 skill。` +
        `不可从公共 registry 拉取（agent-pack 无 npm-for-skills）。`,
    })
  }

  return { ok: missing.length === 0, satisfied, missing }
}

/** 安装前：包内 skill 必须能在 bundle 或本机/staging 解析 */
export async function validatePackSkillsResolvable(
  cwd: string,
  pack: PackDoc,
  stagingRoot: string | null,
): Promise<{ ok: boolean; missing: Array<{ name: string; hint: string }> }> {
  const missing: Array<{ name: string; hint: string }> = []
  for (const s of pack.knowledge?.skills ?? []) {
    const name = String(s.name || basename(String(s.ref || ''))).trim()
    if (!name) continue
    const dir = await resolveSkillDir(cwd, name, String(s.ref || ''), stagingRoot)
    if (dir) continue
    if (skillHasBundleFiles(pack, name)) {
      missing.push({
        name,
        hint: `skill「${name}」在 bundle 中但 staging 失败；请重新 export 或检查 .agent-pack/staging`,
      })
      continue
    }
    missing.push({
      name,
      hint:
        `skill「${name}」既不在 bundle 也无法在本机解析 ref=${s.ref || '∅'}。` +
        `自搓 skill 必须在源项目 export 时 embedPortableFiles 打进 bundle；` +
        `不要只写 ref 指望目标机「下载」。`,
    })
  }
  return { ok: missing.length === 0, missing }
}

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

export async function readAgentPackCliVersion(): Promise<string> {
  try {
    const raw = await fs.readFile(new URL('../package.json', import.meta.url), 'utf8')
    return (JSON.parse(raw) as { version?: string }).version ?? '0.1.0'
  } catch {
    return '0.1.0'
  }
}

export function validateMinPackCli(
  pack: PackDoc,
  currentCli: string,
  projectMin?: string,
): { ok: boolean; required?: string; current: string } {
  const required =
    pack.resolution?.minPackCli || projectMin || (pack.meta?.minPackCli as string | undefined)
  if (!required) return { ok: true, current: currentCli }
  const range = required.startsWith('>=') || required.startsWith('^') || required.startsWith('~') ? required : `>=${required}`
  const ok = satisfiesVersion(currentCli, range)
  return { ok, required: range, current: currentCli }
}
