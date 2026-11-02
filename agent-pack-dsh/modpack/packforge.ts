#!/usr/bin/env bun
/**
 * PackForge `.dspack` ↔ PAD pack v2 适配器（M1/M3 先行版）
 *
 * 只做“读 / 校验 / 转换 / 导出 / 版本决策 / launcher 判定”这些纯逻辑；
 * 真正把内容装进 profile/home 的 compat 安装引擎在 Core 侧（后续接）。
 *
 * 规范来源（2026-11-02 快照）：
 *   .tmp/research/packforge/manifest-v5.md   manifest v5
 *   .tmp/research/packforge/pack-v3.md       .dspack v3 容器
 *   .tmp/research/packforge/launcher-registry.md
 *
 * 命令（由 cli.ts 的 `packagent dsh packforge …` 转发）：
 *   inspect <file.dspack> [--json]
 *   convert <file.dspack> [--out pack.json] [--json]
 *   export <pack.json> [--layout dir] [--out file.dspack] [--json]
 *   decide <file.dspack> --installed a,b [--json]
 *   launchers <file.dspack> --id pad [--version 0.4.5] [--json]
 */
import { promises as fs } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { writeZipFromDir } from '../../src/pack-archive.js'

// ────────────────────────────── 常量与错误 ──────────────────────────────

export const DSPACK_FORMAT = 'dspack'
export const DSPACK_VERSION = 3
export const MANIFEST_VERSION = 5
export const PAD_PACK_SCHEMA = 'pad-pack/v2'

/** PackForge 规范里明令排除、或 PAD 额外要求排除的路径（导出时过滤）。 */
export const EXPORT_EXCLUDE_PREFIXES = [
  '.credentials.yaml',
  '.anonymous-user-id',
  'attachments/',
  'settings.yaml',
  'skills/.system/',
  'profiles/web/',
  'profiles/headless/',
  'node_modules/',
  'sessions/',
  'storages/',
  'logs/',
  'workspace/',
  '.pad/',
  'pad-crash.log',
]

export class PackforgeError extends Error {
  code: string
  details?: unknown
  constructor(code: string, message: string, details?: unknown) {
    super(`error[${code}]: ${message}`)
    this.name = 'PackforgeError'
    this.code = code
    this.details = details
  }
}

function fail(code: string, message: string, details?: unknown): never {
  throw new PackforgeError(code, message, details)
}

// ────────────────────────────── 路径安全 ──────────────────────────────

/** 判断相对路径是否安全（禁绝对路径 / 盘符 / .. / NUL / 反斜杠归一后穿越）。 */
export function isSafeRelPath(p: string): boolean {
  if (typeof p !== 'string') return false
  const raw = p.replace(/\\/g, '/')
  if (!raw || raw.startsWith('/') || raw.includes('\0')) return false
  if (/^[a-zA-Z]:/.test(raw)) return false
  const parts = raw.split('/')
  if (parts.some(s => s === '..')) return false
  if (raw.endsWith('/')) return true // 目录条目
  return true
}

export function assertSafeRelPath(p: string, what = 'path'): void {
  if (!isSafeRelPath(p)) fail('PA143', `unsafe ${what}: ${JSON.stringify(p)}`)
}

/** 导出时是否应排除（凭据 / 运行数据 / 日志 / workspace）；overrides/ 与 home/ 前缀会先剥掉再判。 */
export function isExcludedRelPath(rel: string): boolean {
  let p = rel.replace(/\\/g, '/').replace(/^\.\//, '')
  p = p.replace(/^(overrides|home)\//, '')
  return EXPORT_EXCLUDE_PREFIXES.some(prefix => (prefix.endsWith('/') ? p.startsWith(prefix) : p === prefix))
}

// ────────────────────────────── 小工具 ──────────────────────────────

function sha256Hex(buf: Uint8Array | string): string {
  return createHash('sha256').update(buf).digest('hex')
}

export function checkFileDigest(buf: Uint8Array, expectedSha256?: string, expectedSize?: number): boolean {
  if (expectedSize !== undefined && buf.byteLength !== expectedSize) return false
  if (expectedSha256 !== undefined && expectedSha256 !== '') return sha256Hex(buf) === expectedSha256.toLowerCase()
  return true
}

function runTar(args: string[], cwd?: string): string {
  const r = spawnSync('tar', args, { cwd, encoding: 'utf8', shell: false })
  if (r.status !== 0) {
    const err = (r.stderr || r.stdout || '').trim() || `tar exit ${r.status}`
    fail('PA140', `.dspack/tar failed: ${err}`)
  }
  return (r.stdout || '').trim()
}

function isObject(v: unknown): v is Record<string, unknown> {
  return !!v && typeof v === 'object' && !Array.isArray(v)
}

/** 启动器版本比较：按 . 分段数值比较，缺段视为 0；不做 rc 语义（同 PackForge）。 */
export function compareLauncherVersion(a: string, b: string): number {
  const pa = String(a).split('.')
  const pb = String(b).split('.')
  const n = Math.max(pa.length, pb.length)
  for (let i = 0; i < n; i++) {
    const x = Number.parseInt(pa[i] ?? '0', 10)
    const y = Number.parseInt(pb[i] ?? '0', 10)
    if (Number.isNaN(x) || Number.isNaN(y)) continue
    if (x !== y) return x < y ? -1 : 1
  }
  return 0
}

// ────────────────────────────── manifest 校验 ──────────────────────────────

export type DshVersions = { dshVersion?: string; dshVersions?: string[] }
export type LauncherDecl = { supported: boolean; minVersion?: string; reason?: string }

export function normalizeLaunchers(v: unknown): Record<string, LauncherDecl> {
  if (v === undefined || v === null) return {}
  const out: Record<string, LauncherDecl> = {}
  if (Array.isArray(v)) {
    for (const id of v) {
      if (typeof id !== 'string' || !/^[a-z0-9][a-z0-9-]*$/.test(id)) fail('PA141', `manifest.launchers bad id: ${JSON.stringify(id)}`)
      out[id] = { supported: true }
    }
    return out
  }
  if (!isObject(v)) fail('PA141', 'manifest.launchers must be array or object')
  for (const [id, raw] of Object.entries(v)) {
    if (!/^[a-z0-9][a-z0-9-]*$/.test(id)) fail('PA141', `manifest.launchers bad id: ${id}`)
    if (raw === true) out[id] = { supported: true }
    else if (raw === false) out[id] = { supported: false }
    else if (typeof raw === 'string') out[id] = { supported: true, minVersion: raw }
    else if (isObject(raw)) {
      const supported = raw.supported
      if (typeof supported !== 'boolean') fail('PA141', `manifest.launchers.${id}.supported must be boolean`)
      const decl: LauncherDecl = { supported }
      if (raw.minVersion !== undefined) {
        if (typeof raw.minVersion !== 'string') fail('PA141', `manifest.launchers.${id}.minVersion must be string`)
        decl.minVersion = raw.minVersion
      }
      if (raw.reason !== undefined) {
        if (typeof raw.reason !== 'string') fail('PA141', `manifest.launchers.${id}.reason must be string`)
        decl.reason = raw.reason
      }
      out[id] = decl
    } else fail('PA141', `manifest.launchers.${id} unsupported value`)
  }
  return out
}

/** 校验 manifest v5（兼容 v4 历史格式；返回归一化后的 launchers；失败抛 PA141/PA142）。 */
export function validateManifest(manifest: unknown): Record<string, LauncherDecl> {
  if (!isObject(manifest)) fail('PA141', 'manifest.json must be an object')
  const mv = manifest.manifestVersion
  if (mv !== 4 && mv !== MANIFEST_VERSION) {
    if (typeof mv === 'number' && mv > MANIFEST_VERSION) fail('PA142', `manifestVersion ${mv} > supported ${MANIFEST_VERSION}`)
    fail('PA141', `manifestVersion must be 4 or ${MANIFEST_VERSION}`)
  }
  // v4 是历史单 profile 契约（类型字段可能缺省）
  const type = mv === 4 ? 'profile' : manifest.type
  if (type !== 'profile' && type !== 'dshhome') fail('PA142', `unsupported manifest.type: ${JSON.stringify(manifest.type)}`)
  if (typeof manifest.name !== 'string' || !/^[a-z0-9][a-z0-9-]*$/.test(manifest.name)) fail('PA141', 'manifest.name must be kebab-case')
  if (typeof manifest.version !== 'string' || manifest.version.trim() === '') fail('PA141', 'manifest.version missing')

  const dshVersionRaw = manifest.dshVersion
  const dshVersion = typeof dshVersionRaw === 'string' && dshVersionRaw.trim() !== '' ? dshVersionRaw.trim() : undefined
  const dshVersions = manifest.dshVersions
  if (typeof dshVersionRaw !== 'undefined' && dshVersionRaw !== null && typeof dshVersionRaw !== 'string') fail('PA141', 'manifest.dshVersion must be string')
  if (dshVersions !== undefined) {
    if (!Array.isArray(dshVersions) || dshVersions.length === 0) fail('PA141', 'manifest.dshVersions must be non-empty array')
    const set = new Set<string>()
    for (const v of dshVersions) {
      if (typeof v !== 'string' || v.trim() === '') fail('PA141', 'manifest.dshVersions entries must be strings')
      if (set.has(v)) fail('PA141', `manifest.dshVersions duplicate: ${v}`)
      set.add(v)
    }
    if (dshVersion !== undefined && !set.has(dshVersion)) fail('PA141', 'manifest.dshVersion must be inside dshVersions')
  }

  if (manifest.files !== undefined) {
    if (!Array.isArray(manifest.files)) fail('PA141', 'manifest.files must be array')
    for (const f of manifest.files) {
      if (!isObject(f)) fail('PA141', 'manifest.files entry must be object')
      if (typeof f.path !== 'string') fail('PA141', 'manifest.files[].path missing')
      assertSafeRelPath(f.path, 'manifest.files[].path')
      if (f.sha256 !== undefined && (typeof f.sha256 !== 'string' || !/^[0-9a-fA-F]{64}$/.test(f.sha256))) {
        fail('PA141', `manifest.files[].sha256 invalid for ${f.path}`)
      }
      if (f.size !== undefined && typeof f.size !== 'number') fail('PA141', `manifest.files[].size invalid for ${f.path}`)
      if (f.urls !== undefined && (!Array.isArray(f.urls) || f.urls.some((u: unknown) => typeof u !== 'string'))) {
        fail('PA141', `manifest.files[].urls invalid for ${f.path}`)
      }
    }
  }

  if (type === 'profile') {
    if (!Array.isArray(manifest.bundles) || manifest.bundles.length === 0) fail('PA141', 'profile.bundles must be non-empty array')
    for (const b of manifest.bundles) if (typeof b !== 'string' || !b.trim()) fail('PA141', 'profile.bundles entries must be strings')
    if (!isObject(manifest.dependencies)) fail('PA141', 'profile.dependencies must be object')
    if (manifest.patch !== undefined) assertSafeRelPath(String(manifest.patch), 'manifest.patch')
  } else {
    const profiles = manifest.profiles
    if (!isObject(profiles) || Object.keys(profiles).length === 0) fail('PA141', 'dshhome.profiles must be non-empty object')
    for (const name of Object.keys(profiles)) {
      if (name === 'web' || name === 'headless') fail('PA141', `dshhome.profiles must not contain built-in profile ${name}`)
    }
    if (typeof manifest.defaultProfile !== 'string' || !(manifest.defaultProfile in profiles)) {
      fail('PA141', 'dshhome.defaultProfile must point to an existing profile')
    }
    for (const [name, unit] of Object.entries(profiles)) {
      if (!isObject(unit)) fail('PA141', `dshhome.profiles.${name} must be object`)
      if (!Array.isArray(unit.bundles) || unit.bundles.length === 0) fail('PA141', `dshhome.profiles.${name}.bundles must be non-empty array`)
      if (!isObject(unit.dependencies)) fail('PA141', `dshhome.profiles.${name}.dependencies must be object`)
      if (unit.patch !== undefined) assertSafeRelPath(String(unit.patch), `dshhome.profiles.${name}.patch`)
    }
    if (manifest.presets !== undefined) {
      if (!isObject(manifest.presets)) fail('PA141', 'dshhome.presets must be object')
      for (const [name, unit] of Object.entries(manifest.presets)) {
        if (!isObject(unit) || typeof unit.path !== 'string') fail('PA141', `dshhome.presets.${name}.path missing`)
        assertSafeRelPath(unit.path, `dshhome.presets.${name}.path`)
      }
    }
    if (manifest.skills !== undefined) {
      if (!Array.isArray(manifest.skills)) fail('PA141', 'dshhome.skills must be array')
      for (const s of manifest.skills) {
        if (!isObject(s) || typeof s.path !== 'string') fail('PA141', 'dshhome.skills[].path missing')
        assertSafeRelPath(s.path, 'dshhome.skills[].path')
      }
    }
    if (manifest.instructions !== undefined) assertSafeRelPath(String(manifest.instructions), 'dshhome.instructions')
  }

  return normalizeLaunchers(manifest.launchers)
}

// ────────────────────────────── .dspack 读取 ──────────────────────────────

export type DspackLayout = { overrides: string[]; home: string[]; rootFiles: string[] }
export type DspackDoc = {
  format: string
  version: number
  manifestVersion: number
  type: 'profile' | 'dshhome'
  name: string
  version: string
  dshVersion?: string
  dshVersions?: string[]
  launchers: Record<string, LauncherDecl>
  layout: DspackLayout
  manifest: Record<string, unknown>
  /** 解压目录（调用方负责清理；readDspack 默认保留到 cleanup()） */
  extractedRoot: string
  cleanup: () => Promise<void>
}

async function walkRel(root: string, base = root): Promise<string[]> {
  const out: string[] = []
  let entries
  try {
    entries = await fs.readdir(root, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    const abs = join(root, e.name)
    if (e.isDirectory()) out.push(...(await walkRel(abs, base)))
    else if (e.isFile()) {
      const rel = abs.slice(base.length).replace(/^[\\/]/, '').replace(/\\/g, '/')
      out.push(rel)
    }
  }
  return out
}

/** 读取并校验一个 `.dspack` 文件。 */
export async function readDspack(file: string, opts: { keepExtracted?: boolean } = {}): Promise<DspackDoc> {
  const abs = resolve(file)
  let head: Uint8Array
  try {
    const fh = await fs.open(abs, 'r')
    try {
      head = new Uint8Array(4)
      await fh.read(head, 0, 4, 0)
    } finally {
      await fh.close()
    }
  } catch (e) {
    fail('PA140', `cannot read ${abs}: ${(e as Error).message}`)
  }
  if (!(head[0] === 0x50 && head[1] === 0x4b)) fail('PA140', `not a ZIP container: ${abs}`)

  const root = await fs.mkdtemp(join(await fs.realpath(process.env.TEMP || process.env.TMPDIR || '.'), 'pad-packforge-'))
  const cleanup = async () => {
    if (!opts.keepExtracted) await fs.rm(root, { recursive: true, force: true }).catch(() => {})
  }
  try {
    // 先列出条目做路径安全校验，再解压（防 ../ 穿越）
    const listing = runTar(['-tf', abs])
    const entries = listing.split(/\r?\n/).map(s => s.trim()).filter(Boolean)
    if (entries.length === 0) fail('PA140', 'empty .dspack')
    if (entries.length > 20000) fail('PA140', `too many entries: ${entries.length}`)
    for (const entry of entries) {
      const rel = entry.replace(/^\.\//, '').replace(/^\.$/, '')
      if (rel === '') continue
      assertSafeRelPath(rel, '.dspack entry')
    }
    runTar(['-xf', abs, '-C', root])

    const dspackJsonPath = join(root, 'dspack.json')
    let dspackJson: unknown
    try {
      dspackJson = JSON.parse(await fs.readFile(dspackJsonPath, 'utf8'))
    } catch (e) {
      fail('PA140', `.dspack missing/invalid dspack.json: ${(e as Error).message}`)
    }
    if (!isObject(dspackJson)) fail('PA140', 'dspack.json must be an object')
    if (dspackJson.format !== DSPACK_FORMAT) fail('PA140', `dspack.json.format must be "${DSPACK_FORMAT}"`)
    if (dspackJson.version !== DSPACK_VERSION) {
      if (typeof dspackJson.version === 'number' && dspackJson.version > DSPACK_VERSION) {
        fail('PA142', `dspack version ${dspackJson.version} > supported ${DSPACK_VERSION}`)
      }
      fail('PA140', `dspack.json.version must be ${DSPACK_VERSION}`)
    }

    let manifest: unknown
    try {
      manifest = JSON.parse(await fs.readFile(join(root, 'manifest.json'), 'utf8'))
    } catch (e) {
      fail('PA141', `.dspack missing/invalid manifest.json: ${(e as Error).message}`)
    }
    const launchers = validateManifest(manifest)
    const m = manifest as Record<string, unknown>
    const manifestVersion = m.manifestVersion as number
    const type = (manifestVersion === 4 ? 'profile' : m.type) as 'profile' | 'dshhome'

    const allFiles = await walkRel(root)
    const overrides = allFiles.filter(f => f.startsWith('overrides/')).map(f => f.slice('overrides/'.length)).filter(Boolean)
    const home = allFiles.filter(f => f.startsWith('home/')).map(f => f.slice('home/'.length)).filter(Boolean)
    const rootFiles = allFiles.filter(f => !f.startsWith('overrides/') && !f.startsWith('home/')).filter(f => f !== 'dspack.json' && f !== 'manifest.json')

    if (type === 'profile') {
      try {
        await fs.access(join(root, 'overrides'))
      } catch {
        fail('PA141', 'profile .dspack must contain overrides/ directory')
      }
    }

    return {
      format: DSPACK_FORMAT,
      version: DSPACK_VERSION,
      manifestVersion,
      type,
      name: String(m.name),
      version: String(m.version),
      dshVersion: typeof m.dshVersion === 'string' && m.dshVersion.trim() !== '' ? m.dshVersion.trim() : undefined,
      dshVersions: Array.isArray(m.dshVersions) ? (m.dshVersions as string[]) : undefined,
      launchers,
      layout: { overrides, home, rootFiles },
      manifest: m,
      extractedRoot: root,
      cleanup,
    }
  } catch (e) {
    await cleanup()
    throw e
  }
}

// ────────────────────────────── 版本决策 / launcher 判定 ──────────────────────────────

export type VersionDecision = {
  version?: string
  source: 'pinned' | 'set-latest' | 'latest-installed' | 'needs-install' | 'none'
  notice?: string
}

/** PackForge §12：dshVersions ∩ 已装 → 优先 dshVersion，其次集合内最新；无交集按 dshVersion 提示安装。 */
export function decideDshVersion(input: { dshVersion?: string; dshVersions?: string[]; installed: string[] }): VersionDecision {
  const installed = input.installed.filter(Boolean)
  const set = input.dshVersions ?? (input.dshVersion ? [input.dshVersion] : [])
  if (set.length === 0) {
    if (installed.length === 0) return { source: 'none', notice: 'no dshVersion declared and no installed dsh' }
    return { version: installed[0], source: 'latest-installed', notice: `no dshVersion declared; using installed ${installed[0]}` }
  }
  const hit = set.filter(v => installed.includes(v))
  if (hit.length > 0) {
    if (input.dshVersion && hit.includes(input.dshVersion)) return { version: input.dshVersion, source: 'pinned' }
    const latest = [...hit].sort((a, b) => compareLauncherVersion(a, b)).at(-1)
    return { version: latest, source: 'set-latest', notice: `picked ${latest} from dshVersions ∩ installed` }
  }
  if (input.dshVersion) return { source: 'needs-install', version: input.dshVersion, notice: `install ${input.dshVersion} first` }
  return { source: 'needs-install', version: set.sort((a, b) => compareLauncherVersion(a, b)).at(-1), notice: `install one of: ${set.join(', ')}` }
}

export type LauncherVerdict = { level: 'ok' | 'notice' | 'warn'; message?: string }

/** PackForge §9.1：警告放行、不硬拒。 */
export function evaluateLaunchers(
  launchers: Record<string, LauncherDecl> | undefined,
  self: { id: string; version?: string },
): LauncherVerdict {
  const decls = launchers ?? {}
  const ids = Object.keys(decls)
  if (ids.length === 0) return { level: 'ok' }
  const mine = decls[self.id]
  const anySupported = ids.some(id => decls[id]?.supported === true)

  if (!mine) {
    if (!anySupported) return { level: 'ok' }
    return { level: 'notice', message: `包声明的支持列表不含 ${self.id}（支持：${ids.filter(id => decls[id]?.supported).join(', ')}），继续安装` }
  }
  if (!mine.supported) {
    return { level: 'warn', message: `包声明不支持 ${self.id}${mine.reason ? `：${mine.reason}` : ''}，继续安装` }
  }
  if (mine.minVersion) {
    if (!self.version) return { level: 'notice', message: `需要 ${self.id} ≥ ${mine.minVersion}（当前版本未知），继续安装` }
    if (compareLauncherVersion(self.version, mine.minVersion) < 0) {
      return { level: 'notice', message: `需要 ${self.id} ≥ ${mine.minVersion}（当前 ${self.version}），继续安装` }
    }
  }
  return { level: 'ok' }
}

// ────────────────────────────── 转换：.dspack → pad-pack v2 ──────────────────────────────

export type PadPackV2 = {
  schema: string
  name: string
  version: string
  displayName?: unknown
  description?: unknown
  author?: string
  icon?: string
  license?: string
  type: 'profile' | 'dshhome'
  dsh?: Record<string, unknown>
  launchers?: unknown
  pad?: Record<string, unknown>
}

function pickMetadata(m: Record<string, unknown>): Partial<PadPackV2> {
  const out: Partial<PadPackV2> = {}
  for (const key of ['displayName', 'description', 'author', 'icon', 'license']) {
    if (m[key] !== undefined) (out as Record<string, unknown>)[key] = m[key]
  }
  return out
}

export type ConvertResult = { pack: PadPackV2; warnings: string[] }

/** 把一个已读取的 .dspack 转成 PAD pack v2 文档。 */
export function dspackToPadPack(doc: DspackDoc): ConvertResult {
  const m = doc.manifest
  const warnings: string[] = []
  if (doc.manifestVersion === 4) warnings.push('manifest v4 历史格式：按单 profile 处理（导出时会升级为 v5）')
  const dsh: Record<string, unknown> = {}
  if (doc.dshVersion !== undefined) dsh.version = doc.dshVersion
  if (doc.dshVersions !== undefined) dsh.versions = doc.dshVersions

  if (doc.type === 'profile') {
    dsh.profile = typeof m.profileName === 'string' ? m.profileName : 'pack'
    dsh.bundles = m.bundles
    dsh.deps = m.dependencies
    if (m.patch !== undefined) dsh.patch = m.patch
  } else {
    dsh.defaultProfile = m.defaultProfile
    dsh.profiles = m.profiles
    if (m.presets !== undefined) dsh.presets = m.presets
    if (m.skills !== undefined) dsh.skills = m.skills
    if (m.instructions !== undefined) dsh.instructions = m.instructions
    warnings.push('dshhome 形态：转换保留 profiles / presets / skills / instructions（compat 引擎逐 profile 安装）')
  }

  if (Array.isArray(m.files)) dsh.files = m.files
  dsh.layout = { overrides: doc.layout.overrides, home: doc.layout.home }

  const padPack: PadPackV2 = {
    schema: PAD_PACK_SCHEMA,
    name: doc.name,
    version: doc.version,
    ...(pickMetadata(m) as object),
    type: doc.type,
    dsh,
    launchers: doc.launchers,
    pad: {
      // PAD 扩展：来自 PackForge 的导入记录（别家忽略）
      source: { format: 'dspack', version: DSPACK_VERSION, manifestVersion: doc.manifestVersion },
      projection: [],
      whitelist: { default: 'deny', entries: [] },
      overrides: [],
    },
  }
  if (doc.layout.rootFiles.length > 0) {
    warnings.push(`归档根目录带额外文件（${doc.layout.rootFiles.length} 个）：${doc.layout.rootFiles.slice(0, 5).join(', ')}${doc.layout.rootFiles.length > 5 ? ' …' : ''}`)
  }
  return { pack: padPack, warnings }
}

// ────────────────────────────── 导出：pad-pack v2 → .dspack ──────────────────────────────

export type ExportResult = { path: string; manifest: Record<string, unknown>; warnings: string[] }

const EXACT_NPM = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/
const COMMIT_SHA = /^[0-9a-f]{7,40}$/i

/** 校验依赖坐标是否“精确”（PackForge dependencies 要求精确版本 / commit sha）。 */
export function isPinnedDependency(spec: string): boolean {
  const s = String(spec).trim()
  if (!s) return false
  if (EXACT_NPM.test(s)) return true
  if (COMMIT_SHA.test(s)) return true
  if (s.includes('#') && COMMIT_SHA.test(s.split('#').pop() ?? '')) return true
  return false
}

export function assertPinnedDependencies(deps: Record<string, unknown>, where: string): void {
  for (const [name, spec] of Object.entries(deps)) {
    if (!isPinnedDependency(String(spec))) {
      fail('PA148', `${where} dependency "${name}" is not pinned (${JSON.stringify(spec)}); resolve it before export`)
    }
  }
}

export type ExportPadPackOptions = {
  out?: string
  /** 可选：与 pack.json 配套的 layout 目录（含 overrides/、home/ 实体文件）。 */
  layout?: string
}

/** 把 PAD pack v2 文档导出为 .dspack（M3 先行：profile/dshhome 头部转换 + 可选目录拷贝）。 */
export async function padPackToDspack(pack: PadPackV2, opts: ExportPadPackOptions = {}): Promise<ExportResult> {
  if (!pack || pack.schema !== PAD_PACK_SCHEMA) fail('PA141', `pack.schema must be ${PAD_PACK_SCHEMA}`)
  if (pack.type !== 'profile' && pack.type !== 'dshhome') fail('PA142', `unsupported pack.type: ${JSON.stringify(pack.type)}`)
  if (typeof pack.name !== 'string' || !/^[a-z0-9][a-z0-9-]*$/.test(pack.name)) fail('PA141', 'pack.name must be kebab-case')
  if (typeof pack.version !== 'string' || !pack.version.trim()) fail('PA141', 'pack.version missing')
  const dsh = (pack.dsh ?? {}) as Record<string, unknown>
  const dshVersion = typeof dsh.version === 'string' ? dsh.version : undefined
  if (!dshVersion) fail('PA145', 'pack.dsh.version is required for .dspack export (exact version)')

  const warnings: string[] = []
  const manifest: Record<string, unknown> = {
    manifestVersion: MANIFEST_VERSION,
    type: pack.type,
    name: pack.name,
    version: pack.version,
    dshVersion,
  }
  if (Array.isArray(dsh.versions)) manifest.dshVersions = dsh.versions
  for (const key of ['displayName', 'description', 'author', 'icon', 'license']) {
    if ((pack as Record<string, unknown>)[key] !== undefined) manifest[key] = (pack as Record<string, unknown>)[key]
  }

  if (pack.type === 'profile') {
    const bundles = dsh.bundles
    if (!Array.isArray(bundles) || bundles.length === 0) fail('PA141', 'profile export requires dsh.bundles')
    const deps = (dsh.deps ?? {}) as Record<string, unknown>
    if (!isObject(deps)) fail('PA141', 'dsh.deps must be object')
    assertPinnedDependencies(deps, 'profile')
    manifest.profileName = typeof dsh.profile === 'string' ? dsh.profile : 'pack'
    manifest.bundles = bundles
    manifest.dependencies = deps
    if (typeof dsh.patch === 'string') manifest.patch = dsh.patch
  } else {
    const profiles = dsh.profiles
    if (!isObject(profiles) || Object.keys(profiles).length === 0) fail('PA141', 'dshhome export requires dsh.profiles')
    const first = Object.values(profiles)[0]
    if (!isObject(first)) fail('PA141', 'dsh.profiles entries must be objects')
    manifest.profiles = profiles
    manifest.defaultProfile = typeof dsh.defaultProfile === 'string' ? dsh.defaultProfile : Object.keys(profiles)[0]
    for (const [name, unit] of Object.entries(profiles)) {
      if (!isObject(unit)) fail('PA141', `dsh.profiles.${name} must be object`)
      if (!Array.isArray(unit.bundles) || unit.bundles.length === 0) fail('PA141', `dsh.profiles.${name}.bundles missing`)
      const deps = (unit.dependencies ?? {}) as Record<string, unknown>
      assertPinnedDependencies(deps, `dsh.profiles.${name}`)
    }
    if (Array.isArray(dsh.presets)) manifest.presets = Object.fromEntries((dsh.presets as Array<Record<string, unknown>>).map(p => [String(p.name), { path: p.path }]))
    if (Array.isArray(dsh.skills)) manifest.skills = dsh.skills
    if (typeof dsh.instructions === 'string') manifest.instructions = dsh.instructions
  }
  if (pack.launchers !== undefined) manifest.launchers = pack.launchers
  if (Array.isArray(dsh.files) && dsh.files.length > 0) manifest.files = dsh.files

  validateManifest(manifest)

  const out = resolve(opts.out ?? `${pack.name}-${pack.version}.dspack`)
  const stage = await fs.mkdtemp(join(await fs.realpath(process.env.TEMP || process.env.TMPDIR || '.'), 'pad-dspack-'))
  try {
    await fs.writeFile(join(stage, 'dspack.json'), JSON.stringify({ format: DSPACK_FORMAT, version: DSPACK_VERSION }, null, 2), 'utf8')
    await fs.writeFile(join(stage, 'manifest.json'), JSON.stringify(manifest, null, 2), 'utf8')
    await fs.mkdir(join(stage, 'overrides'), { recursive: true })
    if (opts.layout) {
      const layoutRoot = resolve(opts.layout)
      const layoutFiles = await walkRel(layoutRoot)
      const skipped: string[] = []
      for (const rel of layoutFiles) {
        if (!isSafeRelPath(rel)) fail('PA143', `unsafe layout path: ${rel}`)
        if (!rel.startsWith('overrides/') && !rel.startsWith('home/')) continue
        if (isExcludedRelPath(rel)) {
          skipped.push(rel)
          continue
        }
        const dst = join(stage, rel)
        await fs.mkdir(dirname(dst), { recursive: true })
        await fs.copyFile(join(layoutRoot, rel), dst)
      }
      if (skipped.length > 0) {
        warnings.push(`已排除敏感/运行数据文件 ${skipped.length} 个：${skipped.slice(0, 5).join(', ')}${skipped.length > 5 ? ' …' : ''}`)
      }
    }
    await writeZipFromDir(stage, out)
    return { path: out, manifest, warnings }
  } finally {
    await fs.rm(stage, { recursive: true, force: true }).catch(() => {})
  }
}

// ────────────────────────────── CLI ──────────────────────────────

type FlagBag = { positional: string[]; flags: Map<string, string | boolean> }

function parseArgs(argv: string[]): FlagBag {
  const positional: string[] = []
  const flags = new Map<string, string | boolean>()
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    if (a.startsWith('--')) {
      const key = a.slice(2)
      const next = argv[i + 1]
      if (next !== undefined && !next.startsWith('--')) {
        flags.set(key, next)
        i++
      } else flags.set(key, true)
    } else positional.push(a)
  }
  return { positional, flags }
}

function jsonOut(v: unknown): void {
  console.log(JSON.stringify(v, null, 2))
}

function summary(doc: DspackDoc, decision?: VersionDecision, verdict?: LauncherVerdict) {
  return {
    file: doc.name,
    type: doc.type,
    name: doc.name,
    version: doc.version,
    dshVersion: doc.dshVersion,
    dshVersions: doc.dshVersions,
    profileName: doc.type === 'profile' && typeof doc.manifest.profileName === 'string' ? doc.manifest.profileName : (doc.type === 'profile' ? 'pack' : undefined),
    defaultProfile: doc.type === 'dshhome' && typeof doc.manifest.defaultProfile === 'string' ? doc.manifest.defaultProfile : undefined,
    launchers: doc.launchers,
    layout: { overrides: doc.layout.overrides.length, home: doc.layout.home.length, rootFiles: doc.layout.rootFiles.length },
    ...(decision ? { decision } : {}),
    ...(verdict ? { launcherVerdict: verdict } : {}),
  }
}

/** `packagent dsh packforge …` 的入口。 */
export async function runPackforgeCli(argv: string[]): Promise<void> {
  const cmd = argv[0]
  const { positional, flags } = parseArgs(argv.slice(1))
  const file = positional[0]

  if (!cmd || cmd === 'help' || cmd === '--help') {
    console.log(`Usage:
  packagent dsh packforge inspect <file.dspack> [--json]
  packagent dsh packforge convert <file.dspack> [--out pack.json] [--json]
  packagent dsh packforge export <pack.json> [--layout dir] [--out file.dspack] [--json]
  packagent dsh packforge export-instance <instance-home> --out file.dspack [--profile p] [--name n] [--version v] [--dsh-version x] [--json]
  packagent dsh packforge export-workspace <instance-home> --out file.workspace.zip [--workspace id[,id]] [--max-sessions N] [--json]
  packagent dsh packforge decide <file.dspack> --installed a,b [--json]
  packagent dsh packforge launchers <file.dspack> --id pad [--version 0.4.5] [--json]
  packagent dsh packforge market [--json] [--url index.json]
  packagent dsh packforge fetch <id|name> [--out dir] [--json]
  packagent dsh packforge registry [--id pad] [--json]
  packagent dsh packforge install <file.dspack> --home <dir> [--profile name] [--apply] [--deps] [--json]`)
    return
  }

  if (cmd === 'inspect') {
    if (!file) fail('PA140', 'inspect needs a .dspack file')
    const doc = await readDspack(file)
    try {
      const out = summary(doc)
      if (flags.get('json')) jsonOut(out)
      else {
        console.log(`${out.name}-${out.version}.dspack`)
        console.log(`type: ${out.type}`)
        console.log(`dshVersion: ${out.dshVersion ?? '(unset)'}${out.dshVersions ? `  versions: ${out.dshVersions.join(', ')}` : ''}`)
        console.log(`overrides: ${out.layout.overrides}  home: ${out.layout.home}  rootFiles: ${out.layout.rootFiles}`)
        console.log(`launchers: ${Object.keys(out.launchers).join(', ') || '(none)'}`)
      }
    } finally {
      await doc.cleanup()
    }
    return
  }

  if (cmd === 'convert') {
    if (!file) fail('PA140', 'convert needs a .dspack file')
    const doc = await readDspack(file)
    try {
      const { pack, warnings } = dspackToPadPack(doc)
      if (flags.get('json')) jsonOut({ pack, warnings })
      else {
        const out = resolve(String(flags.get('out') ?? `${doc.name}.pad-pack.json`))
        await fs.writeFile(out, JSON.stringify(pack, null, 2), 'utf8')
        console.log(out)
        for (const w of warnings) console.error(`warning: ${w}`)
      }
    } finally {
      await doc.cleanup()
    }
    return
  }

  if (cmd === 'export') {
    if (!file) fail('PA141', 'export needs a pad-pack v2 json file')
    let pack: PadPackV2
    try {
      pack = JSON.parse(await fs.readFile(resolve(file), 'utf8')) as PadPackV2
    } catch (e) {
      fail('PA141', `cannot read pack json: ${(e as Error).message}`)
    }
    const result = await padPackToDspack(pack, {
      out: flags.get('out') ? String(flags.get('out')) : undefined,
      layout: flags.get('layout') ? String(flags.get('layout')) : undefined,
    })
    if (flags.get('json')) jsonOut(result)
    else {
      console.log(result.path)
      for (const w of result.warnings) console.error(`warning: ${w}`)
    }
    return
  }

  if (cmd === 'decide') {
    if (!file) fail('PA140', 'decide needs a .dspack file')
    const installed = String(flags.get('installed') ?? '').split(',').map(s => s.trim()).filter(Boolean)
    const doc = await readDspack(file)
    try {
      const decision = decideDshVersion({ dshVersion: doc.dshVersion, dshVersions: doc.dshVersions, installed })
      if (flags.get('json')) jsonOut({ decision })
      else console.log(`${decision.version ?? '(none)'}  source=${decision.source}${decision.notice ? `  ${decision.notice}` : ''}`)
    } finally {
      await doc.cleanup()
    }
    return
  }

  if (cmd === 'launchers') {
    if (!file) fail('PA140', 'launchers needs a .dspack file')
    const id = String(flags.get('id') ?? 'pad')
    const version = flags.get('version') ? String(flags.get('version')) : undefined
    const doc = await readDspack(file)
    try {
      const verdict = evaluateLaunchers(doc.launchers, { id, version })
      if (flags.get('json')) jsonOut({ verdict })
      else console.log(`${verdict.level}${verdict.message ? `  ${verdict.message}` : ''}`)
    } finally {
      await doc.cleanup()
    }
    return
  }

  if (cmd === 'market') {
    const { fetchMarketIndex } = await import('./packforge-ext.js')
    const mi = await fetchMarketIndex({ url: flags.get('url') ? String(flags.get('url')) : undefined })
    if (flags.get('json')) jsonOut(mi)
    else {
      console.log(`market schemaVersion=${mi.schemaVersion} packs=${mi.packs.length}${mi.generatedAt ? ` generatedAt=${mi.generatedAt}` : ''}`)
      for (const p of mi.packs) {
        console.log(`${p.id}\t${p.name}@${p.version}\t${p.type}\tdsh=${p.dshVersion || '-'}\t${p.size ?? '?'}B`)
      }
      for (const w of mi.warnings) console.error(`warning: ${w}`)
    }
    return
  }

  if (cmd === 'fetch') {
    const { fetchMarketIndex, fetchMarketPack, findMarketPack } = await import('./packforge-ext.js')
    const id = positional[0]
    if (!id) fail('PA152', 'fetch needs a market id or pack name')
    const mi = await fetchMarketIndex({ url: flags.get('url') ? String(flags.get('url')) : undefined })
    const entry = findMarketPack(mi, id)
    if (!entry) fail('PA152', `market pack not found: ${id}`)
    const got = await fetchMarketPack(entry)
    const outDir = resolve(String(flags.get('out') ?? '.'))
    await fs.mkdir(outDir, { recursive: true })
    const dst = join(outDir, `${entry.name}-${entry.version}.dspack`)
    await fs.writeFile(dst, got.bytes)
    if (flags.get('json')) jsonOut({ path: dst, sha256: got.sha256, sourceUrl: got.sourceUrl, entry })
    else {
      console.log(dst)
      console.log(`sha256=${got.sha256}`)
    }
    return
  }

  if (cmd === 'registry') {
    const { fetchLauncherRegistry, lookupLauncher, PAD_LAUNCHER_RECORD, REGISTRY_CLAIM_ROW } = await import('./packforge-ext.js')
    const reg = await fetchLauncherRegistry({ url: flags.get('url') ? String(flags.get('url')) : undefined })
    const wantId = flags.get('id') ? String(flags.get('id')) : undefined
    const found = wantId ? lookupLauncher(reg, wantId) : undefined
    const out = {
      ...reg,
      padClaim: { record: PAD_LAUNCHER_RECORD, row: REGISTRY_CLAIM_ROW, present: !!lookupLauncher(reg, PAD_LAUNCHER_RECORD.id) },
      ...(wantId ? { lookup: { id: wantId, found: found ?? null } } : {}),
    }
    if (flags.get('json')) jsonOut(out)
    else {
      console.log(`registry schemaVersion=${reg.schemaVersion}${reg.fallback ? ' (fallback builtin)' : ''}`)
      if (reg.warning) console.error(`warning: ${reg.warning}`)
      for (const l of reg.launchers) console.log(`${l.id}\t${l.name}`)
      console.log(`pad claim: ${out.padClaim.present ? 'in registry' : 'pending PR'} → ${PAD_LAUNCHER_RECORD.id}`)
    }
    return
  }

  if (cmd === 'install') {
    if (!file) fail('PA140', 'install needs a .dspack file')
    const home = flags.get('home') ? String(flags.get('home')) : ''
    if (!home) fail('PA150', 'install needs --home <target DSH_HOME> (PAD 只装新实例)')
    const dryRun = !flags.get('apply')
    const { readDspack: read } = await import('./packforge.js')
    const { planCompatInstall, applyCompatInstall } = await import('./packforge-ext.js')
    const doc = await read(file, { keepExtracted: true })
    try {
      const plan = await planCompatInstall(doc, {
        home,
        profile: flags.get('profile') ? String(flags.get('profile')) : undefined,
        allowExistingProfile: !!flags.get('allow-existing'),
      })
      const depsFlag = !!flags.get('deps')
      const report = await applyCompatInstall(doc, plan, {
        dryRun,
        runDep: depsFlag
          ? async (spec: string, profileName?: string) => {
              const profile = profileName ?? plan.profile ?? plan.defaultProfile ?? 'pack'
              const r = spawnSync('dsh', ['plugin', '--profile', profile, 'add', spec], {
                encoding: 'utf8',
                shell: true,
                cwd: plan.home,
                env: { ...process.env, DSH_HOME: plan.home },
              })
              if (r.status !== 0) fail('PA155', `dsh plugin add failed for ${spec}: ${(r.stderr || r.stdout || '').trim()}`)
            }
          : undefined,
      })
      if (flags.get('json')) jsonOut(report)
      else {
        const shownOverrides = dryRun ? plan.overrides.length : report.copiedOverrides
        const shownHome = dryRun ? plan.homeFiles.length : report.copiedHome
        console.log(`${dryRun ? '[dry-run] ' : ''}${plan.profileDir}`)
        console.log(`bundles=${report.bundles.length} deps=${report.deps.planned.length} overrides=${shownOverrides} home=${shownHome} skipped=${report.skippedFiles.length}`)
        for (const w of report.warnings) console.error(`warning: ${w}`)
        if (dryRun) console.error('dry-run only; pass --apply to write')
      }
    } finally {
      await doc.cleanup()
    }
    return
  }

  if (cmd === 'export-instance') {
    const { runExportInstanceCli } = await import('./packforge-export.js')
    await runExportInstanceCli(argv.slice(1))
    return
  }

  if (cmd === 'export-workspace') {
    const home = positional[0]
    const out = flags.get('out')
    if (!home || typeof out !== 'string') {
      fail('PA150', 'Usage: packforge export-workspace <instance-home> --out <file.workspace.zip> [--workspace id[,id]] [--max-sessions N] [--name n] [--version v] [--json]')
    }
    const { exportWorkspaceSnapshot } = await import('./workspace-snapshot.js')
    const wsRaw = flags.get('workspace')
    const maxRaw = flags.get('max-sessions')
    const result = await exportWorkspaceSnapshot({
      home,
      out,
      workspaces: typeof wsRaw === 'string' ? wsRaw.split(',').map(s => s.trim()).filter(Boolean) : undefined,
      maxSessions: typeof maxRaw === 'string' && Number.isFinite(Number(maxRaw)) ? Number(maxRaw) : undefined,
      name: typeof flags.get('name') === 'string' ? String(flags.get('name')) : undefined,
      version: typeof flags.get('version') === 'string' ? String(flags.get('version')) : undefined,
    })
    if (flags.get('json')) jsonOut(result)
    else {
      console.log(result.path)
      console.log(`workspaces=${result.stats.workspaces} sessions=${result.stats.sessions} bytes=${result.stats.bytes}`)
      for (const w of result.warnings) console.error(`warning: ${w}`)
    }
    return
  }

  fail('PA140', `Unknown packforge subcommand: ${cmd}. Use: inspect | convert | export | export-instance | export-workspace | decide | launchers | market | fetch | registry | install`)
}
