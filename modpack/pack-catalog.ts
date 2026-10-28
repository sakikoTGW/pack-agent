/**
 * 本机插件包目录：`packagent dsh launcher packs` 的发现 + 解析。
 *
 * 只读：不导入包、不创建实例、不删用户文件（只清理 readPackZip 自己的临时解压目录）。
 *
 * 发现（全部有界，不 shell out 找包；默认 ≤200 个文件 / <5s）：
 *   1. 默认根：<launcherRoot> 根文件、<launcherRoot>/library/packs、<launcherRoot>/library（一层）、
 *      exe 旁（默认 exe 目录 + `--roots` 追加的根）、%USERPROFILE% 的 Downloads / Desktop（一层）
 *   2. 盘根命名启发：固定盘 ≤8，根下一层名字含 pack/modpack/dsh/harness/整合 的目录（≤400/盘），
 *      向下再 ≤2 层找 *.pack.zip / *.pack.json / *.pinst.zip
 *   3. `--deep <dir>`：深度 4、目录 20000 的有界遍历
 *
 * 解析（复用 src/pack-archive.ts / src/types.ts，不重写格式）：
 *   .pack.json  JSON.parse 后按 PackDoc 读 name/version/description/dsh/模块字段
 *   .pack.zip   直接读 zip 根 pack.json 条目（毫秒级）；旧包把 pack.json 套了目录时
 *               才退回 readPackZip()（整包解压 + 现有重建逻辑，慢但兼容）
 *   .pinst.zip  直接读 zip 根 instance.json（不整包解压：pinst 里的 home 可能几个 G）
 *   坏包不抛：收进 errors: [{ path, message }]，继续扫下一个。
 */

import {
  closeSync,
  existsSync,
  fstatSync,
  openSync,
  readdirSync,
  readFileSync,
  readSync,
  rmSync,
} from 'node:fs'
import { basename, dirname, join, resolve } from 'node:path'
import { inflateRawSync } from 'node:zlib'
import { readPackZip } from '../../src/pack-archive.js'
import type { PackDoc } from '../../src/types.js'

export type PackComponentKind = 'skill' | 'mcp' | 'plugin' | 'rule' | 'command' | 'hook'

/** 每类组件计数。key 是单数形（与契约一致）。 */
export type PackComponents = Record<PackComponentKind, number>

export type PackKind = 'pack.json' | 'pack.zip' | 'pinst.zip' | 'projected'

export type PackCatalogRow = {
  /** 稳定 id：pinst 用内部 id/source.packId，包文件用文件名去扩展名 */
  id: string
  title: string
  version: string
  /** 包文件完整路径 */
  path: string
  kind: PackKind
  /** pack.dsh.version（range 保留原文），pinst 取 instance.json 的 dsh.version */
  kernel: string | null
  /** pack.dsh.profile，pinst 取 instance.json 的 profile.name */
  profile: string | null
  /** pinst 无组件清单：null */
  components: PackComponents | null
  /** 组件总数 */
  units: number
  description?: string
  /** 发现来源：launcher root | library | sidecar: <dir> | drive: E:\\ | deep: <root> | user: <dir> */
  source: string
  /** 从 <launcherRoot>/instances/<id>/instance.json 的 source.packId 对上的实例 id */
  installed: string[]
}

export type PackCatalogError = { path: string; message: string }

export type PackCatalogResult = {
  ok: true
  scanned: number
  packs: PackCatalogRow[]
  errors: PackCatalogError[]
}

export type PackCatalogOptions = {
  /** 子串筛选（id/title/version/description/path/kernel/profile，大小写不敏感） */
  query?: string
  /** `--roots a;b`：显式扫描根（追加在默认根之后一起扫） */
  roots?: string[]
  /** `--deep <dir>`：有界深扫根（可重复） */
  deep?: string[]
}

type CandidateKind = 'pack.json' | 'pack.zip' | 'pinst.zip'
type Candidate = { path: string; kind: CandidateKind; source: string }

const MAX_PACK_FILES = 200
const MAX_DEEP_DIRS = 20_000
const MAX_DEEP_DEPTH = 4
const MAX_DRIVES = 8
const MAX_HEURISTIC_DIRS_PER_DRIVE = 400
const MAX_DRIVE_TREE_DEPTH = 2
/** 发现阶段预算：留出解析余量，保证总耗时 <5s */
const DISCOVERY_BUDGET_MS = 4_200
/** 解析兜底：个别超大包不能把命令拖过 5s */
const PARSE_BUDGET_MS = 4_900

/** 盘根命名启发：一层目录名含这些词才往下找 */
const HEURISTIC_DIR_NAME = /pack|modpack|dsh|harness|整合/i
/** 不做遍历的目录（小写比较） */
const SKIP_DIR_NAMES = new Set(['node_modules', '.git', 'obj', 'bin', 'cache', 'system volume information'])

function listDir(dir: string): import('node:fs').Dirent[] {
  try {
    return readdirSync(dir, { withFileTypes: true })
  } catch {
    return []
  }
}

function listNames(dir: string): string[] {
  try {
    return readdirSync(dir)
  } catch {
    return []
  }
}

function candidateKind(fileName: string): CandidateKind | null {
  const lower = fileName.toLowerCase()
  if (lower.endsWith('.pinst.zip')) return 'pinst.zip'
  if (lower.endsWith('.pack.zip')) return 'pack.zip'
  if (lower.endsWith('.pack.json')) return 'pack.json'
  return null
}

/** 包文件名 → 稳定 id（去 .pack.json / .pack.zip / .pinst.zip 扩展名） */
function stemOf(path: string): string {
  const name = basename(path)
  const lower = name.toLowerCase()
  for (const suffix of ['.pinst.zip', '.pack.zip', '.pack.json'] as const) {
    if (lower.endsWith(suffix)) return name.slice(0, name.length - suffix.length) || name
  }
  return name.replace(/\.[^.]+$/, '') || name
}

/** 发现预算 + 去重（同 path 先到先得；Windows 路径大小写不敏感） */
class Discovery {
  readonly candidates: Candidate[] = []
  readonly dirs = { scanned: 0 }
  private readonly seen = new Set<string>()
  private readonly deadline: number

  constructor(budgetMs: number) {
    this.deadline = Date.now() + budgetMs
  }

  get full(): boolean {
    return this.candidates.length >= MAX_PACK_FILES
  }

  get expired(): boolean {
    return Date.now() > this.deadline
  }

  get done(): boolean {
    return this.full || this.expired
  }

  add(path: string, kind: CandidateKind, source: string): void {
    if (this.full) return
    const key = process.platform === 'win32' ? path.toLowerCase() : path
    if (this.seen.has(key)) return
    this.seen.add(key)
    this.candidates.push({ path, kind, source })
  }
}

/** 只扫 dir 这一层的文件（不递归） */
function scanDirect(dir: string, source: string, discovery: Discovery): void {
  if (!dir || discovery.done) return
  for (const entry of listDir(dir)) {
    if (discovery.done) return
    if (!entry.isFile()) continue
    const kind = candidateKind(entry.name)
    if (kind) discovery.add(join(dir, entry.name), kind, source)
  }
}

/** dir 这一层 + 直接子目录这一层的文件（"只一层"） */
function scanOneLevel(dir: string, source: string, discovery: Discovery): void {
  scanDirect(dir, source, discovery)
  if (discovery.done) return
  for (const entry of listDir(dir)) {
    if (discovery.done) return
    if (!entry.isDirectory()) continue
    if (SKIP_DIR_NAMES.has(entry.name.toLowerCase())) continue
    scanDirect(join(dir, entry.name), source, discovery)
  }
}

/** 盘根启发命中后的有界 BFS：root 起向下 maxDepth 层 */
function scanTree(root: string, maxDepth: number, source: string, discovery: Discovery): void {
  let frontier: string[] = [root]
  let depth = 0
  while (frontier.length && depth <= maxDepth && !discovery.done) {
    const next: string[] = []
    for (const dir of frontier) {
      if (discovery.done) return
      for (const entry of listDir(dir)) {
        if (discovery.done) return
        if (entry.isDirectory()) {
          if (!SKIP_DIR_NAMES.has(entry.name.toLowerCase())) next.push(join(dir, entry.name))
          continue
        }
        if (!entry.isFile()) continue
        const kind = candidateKind(entry.name)
        if (kind) discovery.add(join(dir, entry.name), kind, source)
      }
    }
    frontier = next
    depth++
  }
}

/** `--deep <dir>`：深度 4 / 目录 20000 的有界遍历 */
function scanDeep(root: string, source: string, discovery: Discovery): void {
  if (!existsSync(root)) return
  const stack: Array<{ dir: string; depth: number }> = [{ dir: root, depth: 0 }]
  while (stack.length) {
    if (discovery.done || discovery.dirs.scanned >= MAX_DEEP_DIRS) return
    const item = stack.pop()!
    discovery.dirs.scanned++
    for (const entry of listDir(item.dir)) {
      if (entry.isDirectory()) {
        if (item.depth < MAX_DEEP_DEPTH && !SKIP_DIR_NAMES.has(entry.name.toLowerCase())) {
          stack.push({ dir: join(item.dir, entry.name), depth: item.depth + 1 })
        }
        continue
      }
      if (!entry.isFile()) continue
      const kind = candidateKind(entry.name)
      if (kind) discovery.add(join(item.dir, entry.name), kind, source)
    }
  }
}

/** 固定盘（Windows）＝ C:..Z:，取 ≤8 个存在的盘根 */
function windowsDriveRoots(): string[] {
  if (process.platform !== 'win32') return []
  const roots: string[] = []
  for (const letter of 'CDEFGHIJKLMNOPQRSTUVWXYZ') {
    if (roots.length >= MAX_DRIVES) break
    const root = `${letter}:\\`
    try {
      if (existsSync(root)) roots.push(root)
    } catch {
      /* 光驱/网络盘探测失败就当没有 */
    }
  }
  return roots
}

/** 盘根命名启发：根下一层名字含 pack/modpack/dsh/harness/整合 的目录，向下 ≤2 层找包 */
function discoverDrives(discovery: Discovery): void {
  for (const drive of windowsDriveRoots()) {
    if (discovery.done) return
    let matchedDirs = 0
    for (const entry of listDir(drive)) {
      if (discovery.done) return
      if (!entry.isDirectory()) continue
      if (!HEURISTIC_DIR_NAME.test(entry.name)) continue
      if (++matchedDirs > MAX_HEURISTIC_DIRS_PER_DRIVE) break
      scanTree(join(drive, entry.name), MAX_DRIVE_TREE_DEPTH, `drive: ${drive}`, discovery)
    }
  }
}

/** 默认根 + 显式根 + --deep + 盘根启发，统一的发现入口 */
function discoverAll(rootPath: string, opts: PackCatalogOptions, discovery: Discovery): void {
  // --roots：调用方显式给的根（exe 旁 / 用户选的目录），按一层找
  for (const raw of opts.roots ?? []) {
    if (discovery.done) return
    const trimmed = raw.trim()
    if (!trimmed) continue
    const dir = resolve(trimmed)
    scanOneLevel(dir, `sidecar: ${dir}`, discovery)
  }

  // 默认根：launcher 的库/根
  scanDirect(join(rootPath, 'library', 'packs'), 'library', discovery)
  scanOneLevel(join(rootPath, 'library'), 'library', discovery)
  scanDirect(rootPath, 'launcher root', discovery)

  // exe 旁（默认就扫 exe 目录这一层；--roots 可追加真正的安装旁路目录）
  const exeDir = dirname(process.execPath)
  scanDirect(exeDir, `sidecar: ${exeDir}`, discovery)

  // %USERPROFILE% 下一层的 Downloads / Desktop（只找一层，不递归）
  const home = process.env.USERPROFILE?.trim() || process.env.HOME?.trim() || ''
  if (home && !discovery.done) {
    for (const sub of ['Downloads', 'Desktop']) {
      if (discovery.done) break
      const dir = join(home, sub)
      scanDirect(dir, `user: ${dir}`, discovery)
    }
  }

  // --deep：显式深扫优先于盘根启发（预算先给明确目标）
  for (const raw of opts.deep ?? []) {
    if (discovery.done) break
    const trimmed = raw.trim()
    if (!trimmed) continue
    const dir = resolve(trimmed)
    scanDeep(dir, `deep: ${dir}`, discovery)
  }

  // 盘根命名启发放最后，命中预算不足时先牺牲它
  discoverDrives(discovery)
}

// ---------------------------------------------------------------------------
// zip 条目读取：只要 pinst.zip 根 instance.json / manifest.json，不解整包。
// ---------------------------------------------------------------------------

const EOCD_SIGNATURE = 0x06054b50
const CENTRAL_DIR_SIGNATURE = 0x02014b50
const LOCAL_HEADER_SIGNATURE = 0x04034b50

function readAt(fd: number, length: number, position: number): Buffer {
  const buf = Buffer.alloc(length)
  let read = 0
  while (read < length) {
    const got = readSync(fd, buf, read, length - read, position + read)
    if (got <= 0) throw new Error('unexpected end of file while reading zip')
    read += got
  }
  return buf
}

/**
 * 从 zip 中央目录直接读一个"根条目"（name 归一后等于 wantedName）。
 * 大 pinst.zip 里的 home 可能有几个 G，解整包会拖垮目录命令，所以只读需要的成员。
 * 返回 null = 没有这个条目；格式损坏/zip64 会 throw（调用方收进 errors）。
 */
function readZipRootEntry(zipPath: string, wantedName: string): string | null {
  const fd = openSync(zipPath, 'r')
  try {
    const size = fstatSync(fd).size
    if (size < 22) return null

    const tailLength = Math.min(size, 66 * 1024)
    const tail = readAt(fd, tailLength, size - tailLength)
    let eocd = -1
    for (let i = tail.length - 22; i >= 0; i--) {
      if (tail.readUInt32LE(i) === EOCD_SIGNATURE) {
        eocd = i
        break
      }
    }
    if (eocd < 0) throw new Error('not a zip: end-of-central-directory record not found')

    const entryCount = tail.readUInt16LE(eocd + 10)
    const centralSize = tail.readUInt32LE(eocd + 12)
    const centralOffset = tail.readUInt32LE(eocd + 16)
    if (centralOffset === 0xffffffff || centralSize === 0xffffffff || entryCount === 0xffff) {
      throw new Error('zip64 archives are not supported')
    }

    const central = readAt(fd, centralSize, centralOffset)
    let cursor = 0
    let hit: { method: number; compressedSize: number; localOffset: number } | null = null
    for (let i = 0; i < entryCount; i++) {
      if (cursor + 46 > central.length) throw new Error('corrupt zip: truncated central directory')
      if (central.readUInt32LE(cursor) !== CENTRAL_DIR_SIGNATURE) {
        throw new Error('corrupt zip: bad central directory entry signature')
      }
      const method = central.readUInt16LE(cursor + 10)
      const compressedSize = central.readUInt32LE(cursor + 20)
      const nameLength = central.readUInt16LE(cursor + 28)
      const extraLength = central.readUInt16LE(cursor + 30)
      const commentLength = central.readUInt16LE(cursor + 32)
      const localOffset = central.readUInt32LE(cursor + 42)
      const nameStart = cursor + 46
      const name = central
        .subarray(nameStart, nameStart + nameLength)
        .toString('utf8')
        .replace(/\\/g, '/')
        .replace(/^\.\//, '')
        .replace(/^\/+/, '')
      if (!hit && name === wantedName) hit = { method, compressedSize, localOffset }
      cursor = nameStart + nameLength + extraLength + commentLength
    }
    if (!hit) return null

    const localHeader = readAt(fd, 30, hit.localOffset)
    if (localHeader.readUInt32LE(0) !== LOCAL_HEADER_SIGNATURE) {
      throw new Error('corrupt zip: bad local file header signature')
    }
    const localNameLength = localHeader.readUInt16LE(26)
    const localExtraLength = localHeader.readUInt16LE(28)
    const dataOffset = hit.localOffset + 30 + localNameLength + localExtraLength
    const data = hit.compressedSize > 0 ? readAt(fd, hit.compressedSize, dataOffset) : Buffer.alloc(0)
    if (hit.method === 0) return data.toString('utf8')
    if (hit.method === 8) return inflateRawSync(data).toString('utf8')
    throw new Error(`unsupported zip compression method ${hit.method}`)
  } finally {
    closeSync(fd)
  }
}

// ---------------------------------------------------------------------------
// manifest 解析
// ---------------------------------------------------------------------------

function parseJsonText<T>(text: string): T {
  const clean = text.charCodeAt(0) === 0xfeff ? text.slice(1) : text
  return JSON.parse(clean) as T
}

function asObject(value: unknown): Record<string, unknown> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  return value as Record<string, unknown>
}

function strField(obj: unknown, name: string): string {
  const rec = asObject(obj)
  if (!rec) return ''
  const value = rec[name]
  return typeof value === 'string' ? value.trim() : ''
}

function objField(obj: unknown, name: string): Record<string, unknown> | undefined {
  const rec = asObject(obj)
  return rec ? asObject(rec[name]) : undefined
}

function arrLength(value: unknown): number {
  return Array.isArray(value) ? value.length : 0
}

/** 按 PackDoc 的模块字段计数（types.ts：knowledge.skills/rules、tools.mcp、dsh.plugins、commands.files、automation.hooks） */
function componentCounts(pack: PackDoc): PackComponents {
  return {
    skill: arrLength(pack.knowledge?.skills),
    mcp: arrLength(pack.tools?.mcp),
    plugin: arrLength(pack.dsh?.plugins),
    rule: arrLength(pack.knowledge?.rules),
    command: arrLength(pack.commands?.files),
    hook: arrLength(pack.automation?.hooks),
  }
}

function totalUnits(components: PackComponents): number {
  let total = 0
  for (const count of Object.values(components)) total += count
  return total
}

function rowFromPackDoc(pack: PackDoc, candidate: Candidate): PackCatalogRow {
  if (!asObject(pack)) throw new Error('manifest is not a JSON object')
  const components = componentCounts(pack)
  const description = (pack.description || '').trim()
  const row: PackCatalogRow = {
    id: stemOf(candidate.path),
    title: (pack.name || '').trim() || stemOf(candidate.path),
    version: (pack.version || '').trim(),
    path: candidate.path,
    kind: candidate.kind as 'pack.json' | 'pack.zip',
    kernel: (pack.dsh?.version || '').trim() || null,
    profile: (pack.dsh?.profile || '').trim() || null,
    components,
    units: totalUnits(components),
    source: candidate.source,
    installed: [],
  }
  if (description) row.description = description
  return row
}

/**
 * .pinst.zip：只读 zip 根 instance.json（其次 manifest.json）。
 * id：instance.json 的 source.packId（pinst 是从某个包建的实例导出的）＞ 导出时记录的实例 id ＞ 文件名；
 * components 固定 null（pinst 是整实例快照，不是包组件清单）。
 */
function rowFromPinst(candidate: Candidate): PackCatalogRow {
  const instanceText = readZipRootEntry(candidate.path, 'instance.json')
  const manifestText = readZipRootEntry(candidate.path, 'manifest.json')
  if (!instanceText && !manifestText) {
    throw new Error('pinst zip has neither instance.json nor manifest.json at its root')
  }
  const instance = instanceText ? parseJsonText<Record<string, unknown>>(instanceText) : {}
  const manifest = manifestText ? parseJsonText<Record<string, unknown>>(manifestText) : {}

  const manifestInstance = objField(manifest, 'instance')
  const dsh = objField(instance, 'dsh')
  const profile = objField(instance, 'profile')
  const source = objField(instance, 'source')
  const stem = stemOf(candidate.path)

  const id =
    strField(source, 'packId') || strField(manifestInstance, 'id') || strField(instance, 'id') || stem
  const title =
    strField(instance, 'name') || strField(manifestInstance, 'name') || stem
  const kernel = strField(dsh, 'version') || strField(manifestInstance, 'version') || null
  const profileName = strField(profile, 'name') || strField(manifestInstance, 'profile') || null
  const version = strField(manifestInstance, 'version') || strField(dsh, 'version')

  return {
    id,
    title,
    version,
    path: candidate.path,
    kind: 'pinst.zip',
    kernel,
    profile: profileName,
    components: null,
    units: 0,
    source: candidate.source,
    installed: [],
  }
}

function parsePackJson(candidate: Candidate): PackCatalogRow {
  const raw = readFileSync(candidate.path, 'utf8')
  const parsed = parseJsonText<PackDoc>(raw)
  if (!asObject(parsed)) throw new Error('pack.json is not a JSON object')
  return rowFromPackDoc(parsed, candidate)
}

/**
 * .pack.zip：先直接读 zip 根 pack.json 条目（毫秒级）。
 * 不默认走 readPackZip() 是因为它会整包解压 —— 大包/杀软扫描下单个包就要 3s+，
 * 目录命令的 <5s 预算扛不住；只有旧包把 pack.json 套了目录（没有根条目）时才退回
 * readPackZip() 复用现成解包/重建逻辑。
 */
async function parsePackZip(candidate: Candidate): Promise<PackCatalogRow> {
  const rootManifest = readZipRootEntry(candidate.path, 'pack.json')
  if (rootManifest !== null) {
    const parsed = parseJsonText<PackDoc>(rootManifest)
    if (!asObject(parsed)) throw new Error('pack.zip root pack.json is not a JSON object')
    return rowFromPackDoc(parsed, candidate)
  }
  const { pack, extractedRoot } = await readPackZip(candidate.path)
  try {
    return rowFromPackDoc(pack, candidate)
  } finally {
    rmSync(extractedRoot, { recursive: true, force: true })
  }
}

async function parseCandidate(candidate: Candidate): Promise<PackCatalogRow> {
  if (candidate.kind === 'pack.json') return parsePackJson(candidate)
  if (candidate.kind === 'pack.zip') return parsePackZip(candidate)
  return rowFromPinst(candidate)
}

function errorText(error: unknown): string {
  const raw = error instanceof Error ? error.message : String(error)
  const flat = raw.replace(/\s+/g, ' ').trim()
  if (!flat) return 'unknown error'
  return flat.length > 400 ? `${flat.slice(0, 397)}...` : flat
}

// ---------------------------------------------------------------------------
// installed：instance.json 的 source.packId → 实例 id
// ---------------------------------------------------------------------------

function readInstalledIndex(rootPath: string): Map<string, string[]> {
  const index = new Map<string, string[]>()
  const instancesDir = join(rootPath, 'instances')
  for (const name of listNames(instancesDir)) {
    const file = join(instancesDir, name, 'instance.json')
    try {
      const record = parseJsonText<Record<string, unknown>>(readFileSync(file, 'utf8'))
      const packId = strField(objField(record, 'source'), 'packId')
      if (!packId) continue
      const instanceId = strField(record, 'id') || name
      const key = packId.toLowerCase()
      const holders = index.get(key) ?? []
      if (!holders.includes(instanceId)) holders.push(instanceId)
      index.set(key, holders)
    } catch {
      /* 单个坏 instance.json 不影响包目录，静默跳过 */
    }
  }
  for (const holders of index.values()) holders.sort()
  return index
}

function installedFor(index: Map<string, string[]>, packId: string): string[] {
  if (!packId) return []
  return index.get(packId.toLowerCase()) ?? []
}

function matchesQuery(row: PackCatalogRow, query: string): boolean {
  const needle = query.toLowerCase()
  const haystack = [
    row.id,
    row.title,
    row.version,
    row.description ?? '',
    row.path,
    row.kernel ?? '',
    row.profile ?? '',
  ]
  return haystack.some((field) => field.toLowerCase().includes(needle))
}

// ---------------------------------------------------------------------------
// 入口
// ---------------------------------------------------------------------------

export async function scanPackCatalog(
  root: { path: string },
  opts: PackCatalogOptions = {},
): Promise<PackCatalogResult> {
  const started = Date.now()
  const discovery = new Discovery(DISCOVERY_BUDGET_MS)
  discoverAll(root.path, opts, discovery)

  const installedIndex = readInstalledIndex(root.path)
  const packs: PackCatalogRow[] = []
  const errors: PackCatalogError[] = []

  for (const candidate of discovery.candidates) {
    // 兜底：极端情况下（超大/损坏包）不许把命令拖到 5s 之外
    if (Date.now() - started > PARSE_BUDGET_MS) break
    try {
      packs.push(await parseCandidate(candidate))
    } catch (error) {
      errors.push({ path: candidate.path, message: errorText(error) })
    }
  }

  for (const row of packs) row.installed = installedFor(installedIndex, row.id)

  const query = (opts.query || '').trim()
  const filtered = query ? packs.filter((row) => matchesQuery(row, query)) : packs
  filtered.sort((a, b) => a.title.localeCompare(b.title) || a.path.localeCompare(b.path))

  return { ok: true, scanned: discovery.candidates.length, packs: filtered, errors }
}
