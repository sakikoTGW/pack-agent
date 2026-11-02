#!/usr/bin/env bun
/**
 * PackForge 扩展层：市场 / launcher registry / files[] 下载 / compat 安装计划与执行。
 *
 * 设计：agent-pack-dsh/docs/pack-v2-superset.md
 * 依赖：packforge.ts（读取/校验/转换）
 */
import { promises as fs } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { createHash } from 'node:crypto'
import {
  PackforgeError,
  assertSafeRelPath,
  checkFileDigest,
  isExcludedRelPath,
  isSafeRelPath,
  readDspack,
  type DspackDoc,
  type LauncherDecl,
} from './packforge.js'

// ────────────────────────────── 常量 / 错误 ──────────────────────────────

export const MARKET_INDEX_URL = 'https://dsh-packforge.github.io/dsh-pack-market/index.json'
export const LAUNCHER_REGISTRY_URL = 'https://dsh-packforge.github.io/dsh-pack-market/launchers.json'

/** PAD 待认领的 launcher 记录（向 DSH-PackForge 提 PR 后进入官方 registry）。 */
export const PAD_LAUNCHER_RECORD = {
  id: 'pad',
  name: 'PAD · pack-agent for DSH',
  url: 'https://github.com/sakikoTGW/pack-agent',
  support: 'Windows（WPF 启动器 + 整合包/实例管理）',
  desc: 'DSH 启动器与整合包管理器；自报版本 `pad cli version --json`。',
}

/** 提 PR 用的表格行（launcher-registry.md §1；与 packforge-registry-pr/0001 patch 一致）。 */
export const REGISTRY_CLAIM_ROW =
  '| `pad` | [PAD · pack-agent for DSH](https://github.com/sakikoTGW/pack-agent)（Windows 启动器 + 整合包管理器） | ✅ `pad cli version --json` |'

class ExtError extends PackforgeError {}

function extFail(code: string, message: string, details?: unknown): never {
  throw new ExtError(code, message, details)
}

export type FetchLike = (url: string) => Promise<Response>

function defaultFetch(): FetchLike {
  return (url: string) => fetch(url)
}

function sha256Hex(buf: Uint8Array): string {
  return createHash('sha256').update(buf).digest('hex')
}

function isObject(v: unknown): v is Record<string, unknown> {
  return !!v && typeof v === 'object' && !Array.isArray(v)
}

async function fetchJson(url: string, fetchImpl: FetchLike, code: string): Promise<unknown> {
  let res: Response
  try {
    res = await fetchImpl(url)
  } catch (e) {
    extFail(code, `fetch failed: ${url}: ${(e as Error).message}`)
  }
  if (!res.ok) extFail(code, `HTTP ${res.status}: ${url}`)
  try {
    return JSON.parse(await res.text())
  } catch (e) {
    extFail(code, `invalid JSON from ${url}: ${(e as Error).message}`)
  }
}

// ────────────────────────────── 市场 ──────────────────────────────

export type MarketPack = {
  id: string
  name: string
  version: string
  type: 'profile' | 'dshhome'
  dshVersion?: string
  dshVersions?: string[]
  manifestVersion?: number
  downloadUrl: string
  downloadUrls?: string[]
  sha256?: string
  size?: number
  description?: string
}

export type MarketIndex = { schemaVersion: number; generatedAt?: string; packs: MarketPack[]; warnings: string[] }

/** 读取并校验 PackForge 市场索引（index schemaVersion 2）。 */
export async function fetchMarketIndex(opts: { url?: string; fetchImpl?: FetchLike } = {}): Promise<MarketIndex> {
  const url = opts.url ?? MARKET_INDEX_URL
  const raw = await fetchJson(url, opts.fetchImpl ?? defaultFetch(), 'PA152')
  if (!isObject(raw)) extFail('PA152', 'market index must be an object')
  if (raw.schemaVersion !== 2) extFail('PA152', `unsupported market schemaVersion: ${String(raw.schemaVersion)}`)
  if (!Array.isArray(raw.modpacks)) extFail('PA152', 'market index.modpacks must be array')
  const warnings: string[] = []
  const packs: MarketPack[] = []
  for (const item of raw.modpacks) {
    if (!isObject(item)) {
      warnings.push('skip non-object modpack entry')
      continue
    }
    const name = typeof item.name === 'string' ? item.name : ''
    const version = typeof item.version === 'string' ? item.version : ''
    const downloadUrl = typeof item.downloadUrl === 'string' ? item.downloadUrl : ''
    if (!name || !version || !downloadUrl) {
      warnings.push(`skip incomplete modpack entry: ${JSON.stringify({ name, version, hasUrl: !!downloadUrl })}`)
      continue
    }
    const type = item.type === 'dshhome' ? 'dshhome' : item.type === 'profile' ? 'profile' : undefined
    if (!type) {
      warnings.push(`skip unsupported type: ${name}`)
      continue
    }
    const id = typeof item.id === 'string' && item.id ? item.id : `${String(item.owner ?? 'unknown')}.${String(item.repo ?? name)}`
    packs.push({
      id,
      name,
      version,
      type,
      dshVersion: typeof item.dshVersion === 'string' ? item.dshVersion : undefined,
      dshVersions: Array.isArray(item.dshVersions) ? (item.dshVersions as string[]) : undefined,
      manifestVersion: typeof item.manifestVersion === 'number' ? item.manifestVersion : undefined,
      downloadUrl,
      downloadUrls: Array.isArray(item.downloadUrls) ? (item.downloadUrls as string[]) : undefined,
      sha256: typeof item.sha256 === 'string' ? item.sha256 : undefined,
      size: typeof item.size === 'number' ? item.size : undefined,
      description: typeof item.description === 'string' ? item.description : undefined,
    })
  }
  return {
    schemaVersion: 2,
    generatedAt: typeof raw.generatedAt === 'string' ? raw.generatedAt : undefined,
    packs,
    warnings,
  }
}

/** 按 id 或 name 找市场包。 */
export function findMarketPack(index: MarketIndex, idOrName: string): MarketPack | undefined {
  return index.packs.find(p => p.id === idOrName) ?? index.packs.find(p => p.name === idOrName)
}

export type DownloadedPack = { entry: MarketPack; bytes: Uint8Array; sha256: string; sourceUrl: string }

/** 下载并校验市场包（多个 URL 依次尝试；hash/size 不符 → PA153）。 */
export async function fetchMarketPack(
  entry: MarketPack,
  opts: { fetchImpl?: FetchLike } = {},
): Promise<DownloadedPack> {
  const fetchImpl = opts.fetchImpl ?? defaultFetch()
  const urls = [entry.downloadUrl, ...(entry.downloadUrls ?? [])].filter(Boolean)
  const failures: string[] = []
  for (const url of urls) {
    try {
      const res = await fetchImpl(url)
      if (!res.ok) {
        failures.push(`${url}: HTTP ${res.status}`)
        continue
      }
      const bytes = new Uint8Array(await res.arrayBuffer())
      if (!checkFileDigest(bytes, entry.sha256, entry.size)) {
        failures.push(`${url}: sha256/size mismatch`)
        continue
      }
      return { entry, bytes, sha256: sha256Hex(bytes), sourceUrl: url }
    } catch (e) {
      failures.push(`${url}: ${(e as Error).message}`)
    }
  }
  extFail('PA153', `pack download failed (${entry.name}@${entry.version}): ${failures.join(' | ')}`)
}

// ────────────────────────────── launcher registry ──────────────────────────────

export type LauncherRecord = { id: string; name: string; url?: string; support?: string; desc?: string }
export type LauncherRegistry = { schemaVersion: number; launchers: LauncherRecord[]; fallback: boolean; warning?: string }

/** 内置回落清单（registry 拉取失败时用；不得阻断安装）。 */
export function builtinLauncherRegistry(): LauncherRecord[] {
  return [
    { id: 'dshl', name: 'DSHL · DeepSeek Harness Launcher', support: 'Windows 一站式' },
    { id: 'hdsl', name: 'HDSL · Hello DeepSeek Launcher', support: '基于 HMCL' },
    { id: 'dsh-packforge-app', name: 'DSH PackForge GUI / dspack CLI', support: '官方工具链' },
    { id: 'official-desktop', name: 'DeepSeek Harness 官方桌面端', support: '官方' },
    { id: 'dsh-cli', name: '裸 dsh 命令行', support: '无启动器' },
  ]
}

/** 读取 launcher registry；任何失败 → 内置回落（带 warning），绝不阻断。 */
export async function fetchLauncherRegistry(
  opts: { url?: string; fetchImpl?: FetchLike } = {},
): Promise<LauncherRegistry> {
  try {
    const raw = await fetchJson(opts.url ?? LAUNCHER_REGISTRY_URL, opts.fetchImpl ?? defaultFetch(), 'PA154')
    if (!isObject(raw) || !Array.isArray(raw.launchers)) {
      return { schemaVersion: 1, launchers: builtinLauncherRegistry(), fallback: true, warning: 'registry shape invalid; using builtin list' }
    }
    const launchers: LauncherRecord[] = []
    for (const item of raw.launchers) {
      if (!isObject(item) || typeof item.id !== 'string' || !item.id) continue
      launchers.push({
        id: item.id,
        name: typeof item.name === 'string' ? item.name : item.id,
        url: typeof item.url === 'string' ? item.url : undefined,
        support: typeof item.support === 'string' ? item.support : undefined,
        desc: typeof item.desc === 'string' ? item.desc : undefined,
      })
    }
    return { schemaVersion: typeof raw.schemaVersion === 'number' ? raw.schemaVersion : 1, launchers, fallback: false }
  } catch (e) {
    return {
      schemaVersion: 1,
      launchers: builtinLauncherRegistry(),
      fallback: true,
      warning: `registry unavailable (${(e as Error).message}); using builtin list`,
    }
  }
}

/** 查 registry 中的启动器记录。 */
export function lookupLauncher(registry: LauncherRegistry, id: string): LauncherRecord | undefined {
  return registry.launchers.find(l => l.id === id)
}

// ────────────────────────────── files[] 下载 ──────────────────────────────

export type DeclaredFile = { path: string; sha256?: string; size?: number; urls?: string[] }
export type DownloadFilesReport = {
  written: string[]
  failed: { path: string; reason: string }[]
  skipped: { path: string; reason: string }[]
}

/** 下载 manifest.files[] 并校验；逐个失败记录，成功文件用临时名 + rename 落地。 */
export async function downloadDeclaredFiles(
  files: DeclaredFile[],
  destRoot: string,
  opts: { fetchImpl?: FetchLike } = {},
): Promise<DownloadFilesReport> {
  const fetchImpl = opts.fetchImpl ?? defaultFetch()
  const report: DownloadFilesReport = { written: [], failed: [], skipped: [] }
  const root = resolve(destRoot)
  for (const f of files) {
    if (!isSafeRelPath(f.path)) {
      report.failed.push({ path: f.path, reason: 'unsafe path (PA143)' })
      continue
    }
    const urls = (f.urls ?? []).filter(Boolean)
    if (urls.length === 0) {
      report.skipped.push({ path: f.path, reason: 'no urls' })
      continue
    }
    const dest = join(root, f.path)
    let ok = false
    const failures: string[] = []
    for (const url of urls) {
      try {
        const res = await fetchImpl(url)
        if (!res.ok) {
          failures.push(`${url}: HTTP ${res.status}`)
          continue
        }
        const bytes = new Uint8Array(await res.arrayBuffer())
        if (!checkFileDigest(bytes, f.sha256, f.size)) {
          failures.push(`${url}: sha256/size mismatch`)
          continue
        }
        await fs.mkdir(dirname(dest), { recursive: true })
        const tmp = `${dest}.tmp-${process.pid}-${Math.random().toString(36).slice(2, 8)}`
        await fs.writeFile(tmp, bytes)
        await fs.rename(tmp, dest)
        report.written.push(f.path)
        ok = true
        break
      } catch (e) {
        failures.push(`${url}: ${(e as Error).message}`)
      }
    }
    if (!ok) report.failed.push({ path: f.path, reason: failures.join(' | ') || 'all urls failed' })
  }
  return report
}

// ────────────────────────────── compat 安装 ──────────────────────────────

export type ProfileUnitPlan = {
  name: string
  profileDir: string
  bundles: string[]
  deps: Record<string, string>
}

export type CompatPlan = {
  home: string
  type: 'profile' | 'dshhome'
  /** profile 形态 */
  profile?: string
  profileDir?: string
  bundles: string[]
  deps: Record<string, string>
  overrides: string[]
  homeFiles: string[]
  /** dshhome 形态：逐 profile 安装 */
  profiles?: ProfileUnitPlan[]
  defaultProfile?: string
  presets?: string[]
  skills?: DeclaredFile[]
  instructions?: string
  declaredFiles: DeclaredFile[]
  warnings: string[]
}

/** 生成 compat 安装计划（只做检查/枚举，不落盘）。目标 profile 已存在 → PA150；collection 等 → PA151。 */
export async function planCompatInstall(
  doc: DspackDoc,
  opts: { home: string; profile?: string; allowExistingProfile?: boolean },
): Promise<CompatPlan> {
  if (doc.type !== 'profile' && doc.type !== 'dshhome') extFail('PA151', `unsupported pack type: ${doc.type}`)
  const home = resolve(opts.home)
  let st
  try {
    st = await fs.stat(home)
  } catch {
    extFail('PA150', `target home does not exist: ${home}`)
  }
  if (!st.isDirectory()) extFail('PA150', `target home is not a directory: ${home}`)
  const m = doc.manifest
  const warnings: string[] = []
  if (doc.manifestVersion === 4) warnings.push('manifest v4 历史格式：按 profile 安装（导出时升级为 v5）')
  if (doc.layout.rootFiles.length > 0) warnings.push(`归档根目录额外文件被忽略：${doc.layout.rootFiles.join(', ')}`)
  const excluded = [...doc.layout.overrides, ...doc.layout.home].filter(isExcludedRelPath)
  if (excluded.length) warnings.push(`安装时跳过敏感/运行数据文件 ${excluded.length} 个：${excluded.slice(0, 5).join(', ')}`)
  const declaredFiles = Array.isArray(m.files) ? (m.files as DeclaredFile[]) : []

  if (doc.type === 'dshhome') {
    const profilesMeta = isObject(m.profiles) ? (m.profiles as Record<string, unknown>) : {}
    const names = Object.keys(profilesMeta)
    if (names.length === 0) extFail('PA141', 'dshhome manifest.profiles is empty')
    const profiles: ProfileUnitPlan[] = []
    for (const name of names) {
      const unit = profilesMeta[name]
      if (!isObject(unit)) extFail('PA141', `dshhome.profiles.${name} must be object`)
      const bundles = Array.isArray(unit.bundles) ? (unit.bundles as string[]) : []
      if (bundles.length === 0) extFail('PA141', `dshhome.profiles.${name}.bundles is empty`)
      const deps = isObject(unit.dependencies) ? (unit.dependencies as Record<string, string>) : {}
      const profileDir = join(home, 'profiles', name)
      if (!opts.allowExistingProfile) {
        try {
          const entries = await fs.readdir(profileDir)
          if (entries.length > 0) extFail('PA150', `profile already exists/non-empty: ${profileDir}（PAD 只允许装新实例；如确需覆盖用 --allow-existing）`)
        } catch (e) {
          if (e instanceof PackforgeError) throw e
        }
      }
      profiles.push({ name, profileDir, bundles, deps })
    }
    const defaultProfile = typeof m.defaultProfile === 'string' ? m.defaultProfile : names[0]
    if (!names.includes(defaultProfile)) extFail('PA141', `dshhome.defaultProfile not in profiles: ${defaultProfile}`)
    const presets = isObject(m.presets)
      ? Object.entries(m.presets).map(([name, unit]) =>
          isObject(unit) && typeof unit.path === 'string' ? unit.path : `.agent-presets/${name}`,
        )
      : []
    // 重技能（带 urls 的 skills[]）走下载；小技能已在 overrides/skills/ 里随包落盘。
    const skills: DeclaredFile[] = []
    if (Array.isArray(m.skills)) {
      for (const s of m.skills) {
        if (!isObject(s) || typeof s.path !== 'string') continue
        const urls = Array.isArray(s.urls) ? (s.urls as string[]) : []
        if (urls.length > 0) skills.push({ path: s.path, sha256: s.sha256 as string | undefined, size: s.size as number | undefined, urls })
      }
    }
    warnings.push(`dshhome 形态：逐 profile 安装 ${names.join(', ')}（默认 ${defaultProfile}）`)
    return {
      home,
      type: 'dshhome',
      profiles,
      defaultProfile,
      presets,
      skills,
      instructions: typeof m.instructions === 'string' ? m.instructions : 'AGENTS.md',
      bundles: [],
      deps: {},
      overrides: [...doc.layout.overrides],
      homeFiles: [...doc.layout.home],
      declaredFiles: [...declaredFiles, ...skills],
      warnings,
    }
  }

  const profile = opts.profile || (typeof m.profileName === 'string' && m.profileName) || 'pack'
  if (!/^[a-z0-9][a-z0-9-]*$/.test(profile)) extFail('PA150', `invalid profile name: ${profile}`)
  const profileDir = join(home, 'profiles', profile)
  if (!opts.allowExistingProfile) {
    try {
      const entries = await fs.readdir(profileDir)
      if (entries.length > 0) extFail('PA150', `profile already exists/non-empty: ${profileDir}（PAD 只允许装新实例；如确需覆盖用 --allow-existing）`)
    } catch (e) {
      if (e instanceof PackforgeError) throw e
      /* 不存在 → ok */
    }
  }
  const bundles = Array.isArray(m.bundles) ? (m.bundles as string[]) : []
  if (bundles.length === 0) extFail('PA141', 'profile manifest.bundles is empty')
  const deps = isObject(m.dependencies) ? (m.dependencies as Record<string, string>) : {}
  return { home, type: 'profile', profile, profileDir, bundles, deps, overrides: [...doc.layout.overrides], homeFiles: [...doc.layout.home], declaredFiles, warnings }
}

export type CompatProfileReport = {
  name: string
  profileDir: string
  bundles: string[]
  deps: { planned: string[]; installed: string[] }
}

export type CompatReport = {
  home: string
  type: 'profile' | 'dshhome'
  profile?: string
  profileDir?: string
  profiles?: CompatProfileReport[]
  defaultProfile?: string
  presets?: string[]
  instructions?: string
  dryRun: boolean
  copiedOverrides: number
  copiedHome: number
  skippedFiles: { path: string; reason: string }[]
  bundles: string[]
  deps: { planned: string[]; installed: string[] }
  files: DownloadFilesReport
  warnings: string[]
}

export type ApplyCompatOptions = {
  dryRun?: boolean
  /** 传入即真实执行依赖安装；不传只记录 planned（PAD/C# 侧负责 pnpm/reconcile 或 `dsh plugin add`）。 */
  runDep?: (spec: string, profile?: string) => Promise<void>
  fetchImpl?: FetchLike
}

async function mergeProfilePackage(profileDir: string, profile: string, bundles: string[]): Promise<void> {
  const pkgPath = join(profileDir, 'package.json')
  let pkg: Record<string, unknown> = {}
  try {
    pkg = JSON.parse(await fs.readFile(pkgPath, 'utf8')) as Record<string, unknown>
  } catch (e) {
    if ((e as NodeJS.ErrnoException).code !== 'ENOENT') extFail('PA156', `invalid profile package.json: ${(e as Error).message}`)
    pkg = { name: `dsh-profile-${profile}`, private: true }
  }
  const dsh = isObject(pkg.dsh) ? (pkg.dsh as Record<string, unknown>) : {}
  const profileMeta = isObject(dsh.profile) ? (dsh.profile as Record<string, unknown>) : {}
  dsh.profile = { ...profileMeta, bundles }
  pkg.dsh = dsh
  await fs.mkdir(profileDir, { recursive: true })
  await fs.writeFile(pkgPath, JSON.stringify(pkg, null, 2), 'utf8')
}

/** 执行 compat 安装：profile 形态落 profile 根；dshhome 形态逐 profile + home 根（pet/skills/AGENTS.md）。 */
export async function applyCompatInstall(doc: DspackDoc, plan: CompatPlan, opts: ApplyCompatOptions = {}): Promise<CompatReport> {
  const dryRun = opts.dryRun !== false
  const report: CompatReport = {
    home: plan.home,
    type: plan.type,
    profile: plan.profile,
    profileDir: plan.profileDir,
    profiles: plan.profiles?.map(p => ({
      name: p.name,
      profileDir: p.profileDir,
      bundles: p.bundles,
      deps: { planned: Object.entries(p.deps).map(([n, s]) => `${n}@${s}`), installed: [] },
    })),
    defaultProfile: plan.defaultProfile,
    presets: plan.presets,
    instructions: plan.instructions,
    dryRun,
    copiedOverrides: 0,
    copiedHome: 0,
    skippedFiles: [],
    bundles: plan.bundles,
    deps: { planned: Object.entries(plan.deps).map(([n, s]) => `${n}@${s}`), installed: [] },
    files: { written: [], failed: [], skipped: [] },
    warnings: [...plan.warnings],
  }
  if (dryRun) {
    report.copiedOverrides = plan.overrides.length
    report.copiedHome = plan.homeFiles.length
    return report
  }

  const copyFile = async (src: string, dst: string, label: string): Promise<boolean> => {
    if (isExcludedRelPath(label) || !isSafeRelPath(label)) {
      report.skippedFiles.push({ path: label, reason: 'sensitive/unsafe' })
      return false
    }
    await fs.mkdir(dirname(dst), { recursive: true })
    await fs.copyFile(src, dst)
    return true
  }

  if (plan.type === 'profile') {
    const profileDir = plan.profileDir!
    for (const rel of plan.overrides) {
      if (await copyFile(join(doc.extractedRoot, 'overrides', rel), join(profileDir, rel), `overrides/${rel}`)) report.copiedOverrides++
    }
    for (const rel of plan.homeFiles) {
      if (await copyFile(join(doc.extractedRoot, 'home', rel), join(plan.home, rel), `home/${rel}`)) report.copiedHome++
    }
    await mergeProfilePackage(profileDir, plan.profile!, plan.bundles)
    if (opts.runDep) {
      for (const [name, spec] of Object.entries(plan.deps)) {
        await opts.runDep(`${name}@${spec}`, plan.profile)
        report.deps.installed.push(`${name}@${spec}`)
      }
    }
  } else {
    // dshhome：overrides 下 profiles/<name>/ 进各自 profile，其余进 home 根（.agent-presets/ skills/ AGENTS.md data/）
    const prefixes = (plan.profiles ?? []).map(p => ({ name: p.name, prefix: `profiles/${p.name}/` }))
    for (const rel of plan.overrides) {
      const hit = prefixes.find(p => rel.startsWith(p.prefix))
      const dst = hit ? join(plan.home, 'profiles', hit.name, rel.slice(hit.prefix.length)) : join(plan.home, rel)
      if (await copyFile(join(doc.extractedRoot, 'overrides', rel), dst, `overrides/${rel}`)) report.copiedOverrides++
    }
    for (const rel of plan.homeFiles) {
      if (await copyFile(join(doc.extractedRoot, 'home', rel), join(plan.home, rel), `home/${rel}`)) report.copiedHome++
    }
    for (const unit of plan.profiles ?? []) {
      await mergeProfilePackage(unit.profileDir, unit.name, unit.bundles)
      const rep = report.profiles?.find(p => p.name === unit.name)
      if (opts.runDep && rep) {
        for (const [name, spec] of Object.entries(unit.deps)) {
          await opts.runDep(`${name}@${spec}`, unit.name)
          rep.deps.installed.push(`${name}@${spec}`)
        }
      }
    }
  }

  report.files = await downloadDeclaredFiles(plan.declaredFiles, plan.home, { fetchImpl: opts.fetchImpl })
  return report
}

/** 从文件一步完成：读 → plan → apply → cleanup。 */
export async function compatInstallFromFile(
  file: string,
  opts: { home: string; profile?: string; allowExistingProfile?: boolean } & ApplyCompatOptions,
): Promise<{ report: CompatReport; plan: CompatPlan; doc: { name: string; version: string; manifestVersion: number; type: string } }> {
  const doc = await readDspack(file, { keepExtracted: true })
  try {
    const plan = await planCompatInstall(doc, opts)
    const report = await applyCompatInstall(doc, plan, opts)
    return { report, plan, doc: { name: doc.name, version: doc.version, manifestVersion: doc.manifestVersion, type: doc.type } }
  } finally {
    await doc.cleanup()
  }
}

export { PackforgeError, type LauncherDecl }
