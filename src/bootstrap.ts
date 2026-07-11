/**
 * 自举：sync/install 时始终把 agent-pack skill 打进包并投射到各 harness。
 */
import { promises as fs } from 'node:fs'
import { basename, dirname, join, relative } from 'node:path'
import type { PackDoc } from './types.js'

export const BOOTSTRAP_SKILL_NAME = 'agent-pack'

async function exists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

/** 解析 agent-pack skill 源码目录（CLI 包内优先） */
export async function resolveAgentPackSkillDir(cwd: string): Promise<string | null> {
  const candidates = [
    join(import.meta.dir, '..', 'skills', BOOTSTRAP_SKILL_NAME),
    join(cwd, 'packages', 'pack-cli', 'skills', BOOTSTRAP_SKILL_NAME),
    join(cwd, '.agents', 'skills', BOOTSTRAP_SKILL_NAME),
    join(cwd, '.claude', 'skills', BOOTSTRAP_SKILL_NAME),
  ]
  for (const dir of candidates) {
    if (await exists(join(dir, 'SKILL.md'))) return dir
  }
  return null
}

async function walkSkillFiles(dir: string, base = dir): Promise<Array<{ path: string; content: string }>> {
  const out: Array<{ path: string; content: string }> = []
  let entries: import('node:fs').Dirent[] = []
  try {
    entries = await fs.readdir(dir, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    const abs = join(dir, e.name)
    if (e.isDirectory()) {
      out.push(...(await walkSkillFiles(abs, base)))
      continue
    }
    if (!e.isFile()) continue
    try {
      const rel = relative(base, abs).replace(/\\/g, '/')
      out.push({ path: rel, content: await fs.readFile(abs, 'utf8') })
    } catch {
      /* skip */
    }
  }
  return out
}

function hasBootstrapSkill(pack: PackDoc): boolean {
  return (pack.knowledge?.skills ?? []).some(s => String(s.name || '') === BOOTSTRAP_SKILL_NAME)
}

function bundleHasBootstrap(pack: PackDoc): boolean {
  return (pack.bundle?.files ?? []).some(f => f.path.startsWith(`skills/${BOOTSTRAP_SKILL_NAME}/`))
}

async function readCliPackageVersion(): Promise<string> {
  try {
    const raw = await fs.readFile(join(import.meta.dir, '..', 'package.json'), 'utf8')
    return (JSON.parse(raw) as { version?: string }).version ?? '0.2.0'
  } catch {
    return '0.2.0'
  }
}

/** 确保 pack 含 agent-pack skill 及便携 bundle 条目 */
export async function injectBootstrapIntoPack(pack: PackDoc, cwd: string): Promise<PackDoc> {
  const dir = await resolveAgentPackSkillDir(cwd)
  const cliVersion = await readCliPackageVersion()
  if (!dir) {
    if (hasBootstrapSkill(pack) && bundleHasBootstrap(pack)) return pack
    return {
      ...pack,
      meta: {
        ...pack.meta,
        bootstrapSkipped: 'agent-pack skill source not found',
      },
    }
  }

  const skillFiles = await walkSkillFiles(dir)
  if (!skillFiles.length) return pack

  const bundleFiles = [...(pack.bundle?.files ?? [])]
  const byPath = new Map(bundleFiles.map(f => [f.path, f]))
  for (const f of skillFiles) {
    byPath.set(`skills/${BOOTSTRAP_SKILL_NAME}/${f.path}`, { path: `skills/${BOOTSTRAP_SKILL_NAME}/${f.path}`, content: f.content })
  }

  const skills = [...(pack.knowledge?.skills ?? [])]
  const existing = skills.findIndex(s => String(s.name || '') === BOOTSTRAP_SKILL_NAME)
  const bootstrapEntry = {
    name: BOOTSTRAP_SKILL_NAME,
    source: 'bootstrap',
    ref: dir,
    scope: 'project',
    version: cliVersion,
  }
  if (existing >= 0) {
    skills[existing] = { ...skills[existing], ...bootstrapEntry }
  } else {
    skills.push(bootstrapEntry)
  }

  return {
    ...pack,
    knowledge: {
      ...pack.knowledge,
      skills,
      rules: pack.knowledge?.rules ?? [],
    },
    bundle: { portable: true, files: [...byPath.values()] },
    meta: {
      ...pack.meta,
      portable: true,
      bootstrapSkills: [BOOTSTRAP_SKILL_NAME],
    },
  }
}
