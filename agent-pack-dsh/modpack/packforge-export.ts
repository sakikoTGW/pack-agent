#!/usr/bin/env bun
/**
 * 反向导出：把 PAD 的一个实例 home 导出成 PackForge `.dspack`（v3 容器 / manifest v5）。
 *
 * - 单 profile → `type:"profile"`（overrides/ 落 profile 根，home/ 带全局 skill/preset）
 * - 多 profile → `type:"dshhome"`（overrides/profiles/* 逐 profile，其余 overrides/ 落 home 根）
 *
 * 安全：sessions / storages / logs / workspace / 凭据 / settings.yaml 一律不导出。
 */
import { promises as fs } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { writeZipFromDir } from '../../src/pack-archive.js'
import {
  DSPACK_FORMAT,
  DSPACK_VERSION,
  MANIFEST_VERSION,
  PackforgeError,
  isExcludedRelPath,
  isSafeRelPath,
  readDspack,
  validateManifest,
} from './packforge.js'

export type ExportInstanceOptions = {
  home: string
  out: string
  name?: string
  version?: string
  /** 只导出该 profile（profile 形态）；缺省按 profile 数量自动判 profile/dshhome。 */
  profile?: string
  /** 缺省读实例 instance.json 的 dsh.version。 */
  dshVersion?: string
  includeHome?: boolean
}

export type ExportInstanceResult = {
  path: string
  type: 'profile' | 'dshhome'
  name: string
  version: string
  manifest: Record<string, unknown>
  stats: { profiles: number; overrideFiles: number; homeFiles: number }
  warnings: string[]
}

// 实例里这些条目永不进包。
const HOME_SKIP_DIRS = new Set([
  'profiles',
  'sessions',
  'storages',
  'logs',
  'workspace',
  'node_modules',
  '.plugin-manager',
  'attachments',
  '.pad',
])
const HOME_SKIP_FILES = new Set([
  '.credentials.yaml',
  '.anonymous-user-id',
  'settings.yaml',
  'launcher.patch.yml',
  'pad-gateway.json',
  'pad-crash.log',
])
// 全局内容（home/ 或 dshhome 的 overrides 根）只收这些顶层。
const HOME_KEEP_DIRS = new Set(['skills', '.agent-presets', 'data'])
const HOME_KEEP_FILES = new Set(['AGENTS.md', 'CLAUDE.md'])
// profile 根里属于安装产物的文件，不进包。
const PROFILE_SKIP_FILES = new Set(['package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml', '.npmrc', '.modules.yaml'])
const PROFILE_SKIP_DIRS = new Set(['node_modules', '.plugin-manager'])

function slug(s: string): string {
  const out = String(s)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  return out || 'exported-instance'
}

/** PackForge dependencies 只收精确坐标：x.y.z / 7-40 位 sha / github:…#sha / 含 #sha。 */
export function isPinnedDependencyValue(spec: string): boolean {
  const s = String(spec).trim()
  if (!s) return false
  if (/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(s)) return true
  if (/^[0-9a-f]{7,40}$/i.test(s)) return true
  if (/^github:[^#\s]+#[0-9a-f]{7,40}$/i.test(s)) return true
  if (s.includes('#') && /^[0-9a-f]{7,40}$/i.test(s.split('#').pop() ?? '')) return true
  return false
}

async function walkFiles(root: string, skip: (rel: string) => boolean, base = root): Promise<string[]> {
  const out: string[] = []
  let entries
  try {
    entries = await fs.readdir(root, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    const abs = join(root, e.name)
    const rel = abs.slice(base.length).replace(/^[\\/]/, '').replace(/\\/g, '/')
    if (e.isDirectory()) {
      if (skip(rel)) continue
      out.push(...(await walkFiles(abs, skip, base)))
    } else if (e.isFile()) {
      if (skip(rel)) continue
      out.push(rel)
    }
  }
  return out
}

async function readJson<T>(path: string): Promise<T | undefined> {
  try {
    return JSON.parse(await fs.readFile(path, 'utf8')) as T
  } catch {
    return undefined
  }
}

type ProfileDraft = { name: string; dir: string; bundles: string[]; deps: Record<string, string>; patch?: string }

async function readProfiles(home: string): Promise<ProfileDraft[]> {
  const profilesDir = join(home, 'profiles')
  let names: string[] = []
  try {
    names = (await fs.readdir(profilesDir, { withFileTypes: true })).filter(e => e.isDirectory()).map(e => e.name)
  } catch {
    return []
  }
  const out: ProfileDraft[] = []
  for (const name of names.sort()) {
    const dir = join(profilesDir, name)
    const pkg = await readJson<Record<string, unknown>>(join(dir, 'package.json'))
    if (!pkg) continue
    const dsh = pkg.dsh && typeof pkg.dsh === 'object' ? (pkg.dsh as Record<string, unknown>) : {}
    const meta = dsh.profile && typeof dsh.profile === 'object' ? (dsh.profile as Record<string, unknown>) : {}
    const bundles = Array.isArray(meta.bundles) ? (meta.bundles as string[]).filter(b => typeof b === 'string') : []
    const depsRaw = pkg.dependencies && typeof pkg.dependencies === 'object' ? (pkg.dependencies as Record<string, string>) : {}
    const deps: Record<string, string> = {}
    for (const [k, v] of Object.entries(depsRaw)) if (typeof v === 'string') deps[k] = v
    let patch: string | undefined
    try {
      await fs.access(join(dir, 'cordis.patch.yml'))
      patch = 'cordis.patch.yml'
    } catch {
      /* optional */
    }
    out.push({ name, dir, bundles, deps, patch })
  }
  return out
}

function fail(code: string, msg: string): never {
  throw new PackforgeError(code, msg)
}

/** 把范围依赖锁成安装到 node_modules 的精确版本；锁不了才 PA148。 */
export async function pinDependencies(
  profileDir: string,
  deps: Record<string, string>,
  warnings: string[],
): Promise<Record<string, string>> {
  const out: Record<string, string> = {}
  for (const [name, spec] of Object.entries(deps)) {
    if (isPinnedDependencyValue(spec)) {
      out[name] = spec
      continue
    }
    if (/^(github:|git\+|file:|link:|workspace:|portal:)/.test(spec) || name.startsWith('github:')) {
      fail('PA148', `dependency ${name}@${spec} cannot be pinned automatically (non-registry coordinate)`)
    }
    let installed: string | undefined
    // node_modules/<name>/package.json（含 @scope/name）
    try {
      const pkg = await readJson<{ version?: string }>(join(profileDir, 'node_modules', name, 'package.json'))
      if (pkg?.version && /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(pkg.version)) installed = pkg.version
    } catch {
      /* 下面统一报错 */
    }
    if (!installed) fail('PA148', `dependency ${name}@${spec} is not pinned and no installed version was found in node_modules`)
    out[name] = installed
    warnings.push(`pinned dependency ${name}: ${spec} → ${installed}（按实例里已装版本锁定）`)
  }
  return out
}

/** 实例 home → .dspack。 */
export async function exportInstanceToDspack(opts: ExportInstanceOptions): Promise<ExportInstanceResult> {
  const home = resolve(opts.home)
  let st
  try {
    st = await fs.stat(home)
  } catch {
    fail('PA150', `instance home does not exist: ${home}`)
  }
  if (!st.isDirectory()) fail('PA150', `instance home is not a directory: ${home}`)

  const warnings: string[] = []
  const instanceMeta = await readJson<Record<string, unknown>>(join(home, '..', 'instance.json'))
  const dshMeta = instanceMeta?.dsh && typeof instanceMeta.dsh === 'object' ? (instanceMeta.dsh as Record<string, unknown>) : {}
  const dshVersion = opts.dshVersion ?? (typeof dshMeta.version === 'string' ? dshMeta.version : undefined)
  if (!dshVersion) fail('PA145', 'cannot determine dshVersion (pass --dsh-version or export from an instance with instance.json)')

  const rawName = opts.name ?? (typeof instanceMeta?.name === 'string' ? instanceMeta.name : 'exported-instance')
  const name = slug(rawName)
  const version = opts.version ?? '1.0.0'
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) fail('PA141', `invalid pack version: ${version}`)

  const allProfiles = await readProfiles(home)
  if (allProfiles.length === 0) fail('PA150', `no profile package.json under ${join(home, 'profiles')}`)
  const wanted = opts.profile
  const selected = wanted ? allProfiles.filter(p => p.name === wanted) : allProfiles
  if (selected.length === 0) fail('PA150', `profile not found: ${wanted}`)
  const type: 'profile' | 'dshhome' = wanted || selected.length === 1 ? 'profile' : 'dshhome'

  // 依赖必须精确（PackForge 契约）；范围依赖按 node_modules 已装版本自动锁定。
  for (const p of selected) p.deps = await pinDependencies(p.dir, p.deps, warnings)

  const homeFiles: string[] = []
  if (opts.includeHome !== false) {
    const keep = (rel: string): boolean => {
      const top = rel.split('/')[0]
      if (HOME_KEEP_DIRS.has(top)) return !HOME_SKIP_DIRS.has(top) && !isExcludedRelPath(rel)
      if (!rel.includes('/')) return HOME_KEEP_FILES.has(rel)
      return false
    }
    homeFiles.push(...(await walkFiles(home, rel => !keep(rel))))
  }

  const stage = await fs.mkdtemp(join(await fs.realpath(process.env.TEMP || process.env.TMPDIR || '.'), 'pad-dspack-export-'))
  const out = resolve(opts.out)
  try {
    await fs.writeFile(join(stage, 'dspack.json'), JSON.stringify({ format: DSPACK_FORMAT, version: DSPACK_VERSION }, null, 2), 'utf8')

    const manifest: Record<string, unknown> = {
      manifestVersion: MANIFEST_VERSION,
      type,
      name,
      version,
      dshVersion,
    }
    let overrideFiles = 0
    const copyToStage = async (srcRoot: string, rel: string, dstRel: string): Promise<void> => {
      if (!isSafeRelPath(rel) || isExcludedRelPath(dstRel)) {
        warnings.push(`skip sensitive/unsafe path: ${dstRel}`)
        return
      }
      const dst = join(stage, dstRel)
      await fs.mkdir(dirname(dst), { recursive: true })
      await fs.copyFile(join(srcRoot, rel), dst)
      overrideFiles++
    }

    if (type === 'profile') {
      const p = selected[0]
      const profileFiles = await walkFiles(p.dir, rel => {
        const top = rel.split('/')[0]
        if (!rel.includes('/') && PROFILE_SKIP_FILES.has(rel)) return true
        if (PROFILE_SKIP_DIRS.has(top)) return true
        return isExcludedRelPath(rel)
      })
      for (const rel of profileFiles) await copyToStage(p.dir, rel, `overrides/${rel}`)
      for (const rel of homeFiles) await copyToStage(home, rel, `home/${rel}`)
      manifest.profileName = p.name
      manifest.bundles = p.bundles
      manifest.dependencies = p.deps
      if (p.patch) manifest.patch = p.patch
      if (homeFiles.length > 0) warnings.push(`home/ 携带全局内容 ${homeFiles.length} 个文件（skill/preset/AGENTS.md）`)
    } else {
      const profiles: Record<string, unknown> = {}
      for (const p of selected) {
        const prefix = `profiles/${p.name}`
        const profileFiles = await walkFiles(p.dir, rel => {
          const top = rel.split('/')[0]
          if (!rel.includes('/') && PROFILE_SKIP_FILES.has(rel)) return true
          if (PROFILE_SKIP_DIRS.has(top)) return true
          return isExcludedRelPath(rel)
        })
        for (const rel of profileFiles) await copyToStage(p.dir, rel, `overrides/${prefix}/${rel}`)
        const unit: Record<string, unknown> = { bundles: p.bundles, dependencies: p.deps }
        if (p.patch) unit.patch = p.patch
        profiles[p.name] = unit
      }
      for (const rel of homeFiles) await copyToStage(home, rel, `overrides/${rel}`)
      const lastProfile = typeof instanceMeta?.lastProfile === 'string' ? instanceMeta.lastProfile : undefined
      manifest.profiles = profiles
      manifest.defaultProfile =
        lastProfile && selected.some(p => p.name === lastProfile) ? lastProfile : selected[0].name
      if (homeFiles.length > 0) warnings.push(`overrides/ 携带 home 级内容 ${homeFiles.length} 个文件（skill/preset/AGENTS.md）`)
    }

    validateManifest(manifest)
    await fs.writeFile(join(stage, 'manifest.json'), JSON.stringify(manifest, null, 2), 'utf8')
    await fs.mkdir(join(stage, 'overrides'), { recursive: true })
    await writeZipFromDir(stage, out)

    // 自检：刚写出的文件必须能被自家读取器读回。
    const check = await readDspack(out)
    try {
      if (check.type !== type) fail('PA141', `exported type mismatch: ${check.type} != ${type}`)
    } finally {
      await check.cleanup()
    }
    return {
      path: out,
      type,
      name,
      version,
      manifest,
      stats: { profiles: selected.length, overrideFiles, homeFiles: homeFiles.length },
      warnings,
    }
  } finally {
    await fs.rm(stage, { recursive: true, force: true }).catch(() => {})
  }
}

/** CLI：`packforge export-instance`。 */
export async function runExportInstanceCli(argv: string[]): Promise<void> {
  const positional: string[] = []
  const flags = new Map<string, string | true>()
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    if (a.startsWith('--')) {
      const next = argv[i + 1]
      if (next !== undefined && !next.startsWith('--')) {
        flags.set(a.slice(2), next)
        i++
      } else flags.set(a.slice(2), true)
    } else positional.push(a)
  }
  const home = positional[0]
  const out = typeof flags.get('out') === 'string' ? String(flags.get('out')) : ''
  if (!home || !out) fail('PA150', 'Usage: packforge export-instance <instance-home> --out <file.dspack> [--profile p] [--name n] [--version v] [--dsh-version x] [--json]')
  const result = await exportInstanceToDspack({
    home,
    out,
    name: typeof flags.get('name') === 'string' ? String(flags.get('name')) : undefined,
    version: typeof flags.get('version') === 'string' ? String(flags.get('version')) : undefined,
    profile: typeof flags.get('profile') === 'string' ? String(flags.get('profile')) : undefined,
    dshVersion: typeof flags.get('dsh-version') === 'string' ? String(flags.get('dsh-version')) : undefined,
  })
  if (flags.get('json')) console.log(JSON.stringify(result, null, 2))
  else {
    console.log(result.path)
    console.log(`type=${result.type} profiles=${result.stats.profiles} overrideFiles=${result.stats.overrideFiles} homeFiles=${result.stats.homeFiles}`)
    for (const w of result.warnings) console.error(`warning: ${w}`)
  }
}
