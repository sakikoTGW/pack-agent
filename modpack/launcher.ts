/**
 * pack-agent 第三维：DSH 版本库 / 隔离实例 / 进程。
 * 走现有 packagent 进程，不另起 Rust crate，不另起后端。
 */
import { spawn, spawnSync, type ChildProcess } from 'node:child_process'
import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  realpathSync,
  renameSync,
  rmSync,
  writeFileSync,
  appendFileSync,
} from 'node:fs'
import { createWriteStream } from 'node:fs'
import { createServer } from 'node:net'
import { dirname, join, resolve } from 'node:path'
import { copyGlobalCredentialsToHome, copyNamedCredentialsToHome } from './credentials.js'
import { listOrEmpty } from './empty-state.js'
import { migrateInstanceRecord } from './migrate.js'
import { pnpmStoreEnv } from './pnpm-store.js'

export type DiagLevel = 'error' | 'warning'

export type Diagnostic = {
  code: string
  level: DiagLevel
  message: string
  location: string
  context?: string[]
  note?: string
  help?: string[]
}

export class LauncherError extends Error {
  diagnostic?: Diagnostic
  constructor(message: string, diagnostic?: Diagnostic) {
    super(message)
    this.name = 'LauncherError'
    this.diagnostic = diagnostic
  }
}

export function renderDiag(d: Diagnostic): string {
  const lines = [`${d.level}[${d.code}]: ${d.message}`, ` --> ${d.location}`]
  if (d.context?.length) {
    lines.push('  |')
    for (const row of d.context) lines.push(`  | ${row}`)
    lines.push('  |')
  }
  if (d.note) lines.push(`  = note: ${d.note}`)
  for (const h of d.help ?? []) lines.push(`  = help: ${h}`)
  return lines.join('\n') + '\n'
}

export function pa(code: string, message: string, location: string, extra: Partial<Diagnostic> = {}): LauncherError {
  const diagnostic: Diagnostic = { code, level: 'error', message, location, ...extra }
  return new LauncherError(renderDiag(diagnostic).trim(), diagnostic)
}

export type LauncherRoot = { path: string }

export type VersionRecord = {
  schema: string
  version: string
  verified: boolean
  verifyOutput: string
  installedAt: string
  sessionFormatVersion?: number
  patchRowIds?: string[]
  profileTemplates?: Record<string, string[]>
}

export type InstanceDisplay = {
  info: string
  logo: string | null
  star: boolean
  category: string
}

export type InstanceRecord = {
  schema: string
  id: string
  name: string
  dsh: { version: string }
  profile: { name: string; port: number | 'auto' }
  workspace: { kind: string; path: string }
  status: 'ready' | 'import-failed' | 'restart-required'
  home: string
  display?: InstanceDisplay
  packs?: { allowSet: string }
  plugins?: { disabled: string[] }
  adopted?: boolean
  credentials?: { kind: 'global' | 'instance' | 'named'; set?: string }
  adoptedFingerprint?: { credHash: string }
  pinnedAt?: string
  created: string
  updated: string
}

export type RuntimeEntry = {
  instance: string
  version: string
  pid: number
  port: number
  status: string
  startedAt: string
  log: string
}

export type RuntimeFile = {
  schema: string
  entries: RuntimeEntry[]
  ports: { claimed: Record<string, number> }
}

function nowIso(): string {
  return new Date().toISOString()
}

function atomicWrite(path: string, data: string): void {
  mkdirSync(dirname(path), { recursive: true })
  const tmp = `${path}.${process.pid}.tmp`
  writeFileSync(tmp, data)
  try {
    rmSync(path, { force: true })
  } catch {
    /* replace */
  }
  renameSync(tmp, path)
}

function readJson<T>(path: string, fallback: T): T {
  if (!existsSync(path)) return fallback
  return JSON.parse(readFileSync(path, 'utf8')) as T
}

export function ensureRoot(path: string): LauncherRoot {
  const root = resolve(path)
  mkdirSync(join(root, 'versions'), { recursive: true })
  mkdirSync(join(root, 'instances'), { recursive: true })
  mkdirSync(join(root, 'library', 'registries'), { recursive: true })
  mkdirSync(join(root, 'library', 'faq'), { recursive: true })
  mkdirSync(join(root, 'library', 'meta'), { recursive: true })
  mkdirSync(join(root, 'library', 'credentials'), { recursive: true })
  mkdirSync(join(root, 'library', 'pnpm-store'), { recursive: true })
  mkdirSync(join(root, 'library', 'updates'), { recursive: true })
  mkdirSync(join(root, 'library', 'shortcuts'), { recursive: true })
  const launcherJson = join(root, 'launcher.json')
  if (!existsSync(launcherJson)) {
    atomicWrite(
      launcherJson,
      JSON.stringify(
        {
          schema: 'pack-agent.launcher/v1',
          dataRoot: root,
          defaults: { profile: 'web', isolation: true, workspaceKind: 'owned', telemetryDisabled: true },
          update: { channel: 'stable' },
          adoptedHome: null,
          created: nowIso(),
          updated: nowIso(),
        },
        null,
        2,
      ),
    )
  }
  if (!existsSync(join(root, 'runtime.json'))) {
    writeRuntime({ path: root }, emptyRuntime())
  }
  return { path: root }
}

export function writeLauncherAdoptedHome(root: LauncherRoot, home: string | null): void {
  const path = join(root.path, 'launcher.json')
  const cur = readJson<Record<string, unknown>>(path, {
    schema: 'pack-agent.launcher/v1',
    dataRoot: root.path,
  })
  cur.adoptedHome = home
  cur.updated = nowIso()
  atomicWrite(path, JSON.stringify(cur, null, 2))
}

function emptyRuntime(): RuntimeFile {
  return { schema: 'pack-agent.launcher.runtime/v1', entries: [], ports: { claimed: {} } }
}

export function readRuntime(root: LauncherRoot): RuntimeFile {
  return readJson(join(root.path, 'runtime.json'), emptyRuntime())
}

export function writeRuntime(root: LauncherRoot, file: RuntimeFile): void {
  atomicWrite(join(root.path, 'runtime.json'), JSON.stringify(file, null, 2))
}

export function dshBin(root: LauncherRoot, version: string): string {
  return join(root.path, 'versions', version, 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'bin.js')
}

export function listVersions(root: LauncherRoot): VersionRecord[] {
  const dir = join(root.path, 'versions')
  if (!existsSync(dir)) return []
  return readdirSync(dir)
    .filter((name) => existsSync(join(dir, name, 'version.json')))
    .sort()
    .map((name) => readJson<VersionRecord>(join(dir, name, 'version.json'), {
      schema: 'pack-agent.launcher.version/v1',
      version: name,
      verified: false,
      verifyOutput: '',
      installedAt: '',
    }))
}

export function seedFakeVersion(root: LauncherRoot, version: string, jsSource: string): VersionRecord {
  const dir = join(root.path, 'versions', version)
  mkdirSync(join(dir, 'node_modules', '@deepseek-ai', 'dsh', 'lib'), { recursive: true })
  writeFileSync(join(dir, 'package.json'), JSON.stringify({ name: `dsh-pin-${version}`, private: true }))
  writeFileSync(join(dir, 'pnpm-lock.yaml'), "lockfileVersion: '9.0'\n")
  writeFileSync(dshBin(root, version), jsSource.replaceAll('@@VERSION@@', version))
  atomicWrite(
    join(dir, 'version.json'),
    JSON.stringify(
      {
        schema: 'pack-agent.launcher.version/v1',
        version,
        verified: false,
        verifyOutput: '',
        installedAt: nowIso(),
      } satisfies VersionRecord,
      null,
      2,
    ),
  )
  return verifyVersion(root, version)
}

export function verifyVersion(root: LauncherRoot, version: string): VersionRecord {
  const dir = join(root.path, 'versions', version)
  if (!existsSync(dir)) {
    throw pa('PA001', `pinned dsh ${version} is not installed`, `versions/${version}`, {
      help: [`packagent dsh launcher version install ${version}`],
    })
  }
  if (!existsSync(join(dir, 'pnpm-lock.yaml'))) {
    throw pa('PA001', `dsh ${version} is missing pnpm-lock.yaml; closure may drift`, `versions/${version}/pnpm-lock.yaml`, {
      help: [`reinstall ${version} so the lockfile is kept`],
    })
  }
  const bin = dshBin(root, version)
  if (!existsSync(bin)) {
    throw pa('PA001', `dsh ${version} bin.js is missing`, bin, {
      help: [`packagent dsh launcher version install ${version}`],
    })
  }
  const proc = spawnSync('node', [bin, '--version'], { encoding: 'utf8' })
  const printed = (proc.stdout || '').trim()
  if (proc.status !== 0 || printed !== version) {
    throw pa('PA001', `dsh --version printed ${JSON.stringify(printed)}, expected ${JSON.stringify(version)}`, bin, {
      context: [`exit: ${proc.status}`],
      help: ['reinstall this version'],
    })
  }
  const prev = existsSync(join(dir, 'version.json'))
    ? (JSON.parse(readFileSync(join(dir, 'version.json'), 'utf8')) as Partial<VersionRecord>)
    : {}
  const rec: VersionRecord = {
    schema: 'pack-agent.launcher.version/v1',
    version,
    verified: true,
    verifyOutput: printed,
    installedAt: nowIso(),
    sessionFormatVersion: typeof prev.sessionFormatVersion === 'number' ? prev.sessionFormatVersion : 0,
    patchRowIds: Array.isArray(prev.patchRowIds) ? prev.patchRowIds : ['session-persistence-jsonl'],
    profileTemplates: prev.profileTemplates ?? {
      web: ['@deepseek-ai/dsh-base', '@deepseek-ai/dsh-web-app'],
      headless: ['@deepseek-ai/dsh-base', '@deepseek-ai/dsh-headless'],
    },
  }
  atomicWrite(join(dir, 'version.json'), JSON.stringify(rec, null, 2))
  return rec
}

export function installVersion(root: LauncherRoot, version: string): VersionRecord {
  const dir = join(root.path, 'versions', version)
  mkdirSync(dir, { recursive: true })
  writeFileSync(join(dir, 'package.json'), JSON.stringify({ name: `dsh-pin-${version}`, private: true }))
  const probe = spawnSync('pnpm', ['--version'], { encoding: 'utf8', shell: process.platform === 'win32' })
  if (probe.status !== 0) {
    throw pa('PA011', 'pnpm not found on PATH', 'doctor', { help: ['install pnpm or enable corepack'] })
  }
  const add = spawnSync('pnpm', ['add', `@deepseek-ai/dsh@${version}`], {
    cwd: dir,
    encoding: 'utf8',
    shell: process.platform === 'win32',
    env: { ...process.env, ...pnpmStoreEnv(root) },
  })
  if (add.status !== 0) {
    throw pa('PA013', `pnpm add @deepseek-ai/dsh@${version} failed`, dir, {
      context: [(add.stderr || add.stdout || '').slice(0, 400)],
      help: ['check registry / network'],
    })
  }
  return verifyVersion(root, version)
}

export function listInstances(root: LauncherRoot): InstanceRecord[] {
  const dir = join(root.path, 'instances')
  if (!existsSync(dir)) return []
  return readdirSync(dir)
    .filter((id) => existsSync(join(dir, id, 'instance.json')))
    .sort()
    .map((id) => getInstance(root, id))
}

export function removeVersion(root: LauncherRoot, version: string): void {
  const holders = listInstances(root).filter((i) => i.dsh.version === version).map((i) => i.id)
  if (holders.length) {
    throw pa('PA006', `cannot remove dsh ${version}: still pinned`, `versions/${version}`, {
      context: [`instances: ${holders.join(', ')}`],
      help: ['pin those instances to another version first'],
    })
  }
  rmSync(join(root.path, 'versions', version), { recursive: true, force: true })
}

export function isValidId(id: string): boolean {
  return /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(id)
}

export function slugify(name: string): string {
  const out = name
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  if (!isValidId(out)) {
    throw new LauncherError(`cannot derive instance id from ${JSON.stringify(name)}; use [a-z0-9]+(?:-[a-z0-9]+)*`)
  }
  return out
}

export function sameHomePath(a: string, b: string): boolean {
  const norm = (p: string): string => {
    try {
      if (existsSync(p)) return realpathSync(p)
    } catch {
      /* fall through */
    }
    return resolve(p)
  }
  const left = norm(a)
  const right = norm(b)
  if (process.platform === 'win32') return left.toLowerCase() === right.toLowerCase()
  return left === right
}

export function instanceHomePath(root: LauncherRoot, inst: InstanceRecord): string {
  if (inst.home) return resolve(inst.home)
  return join(root.path, 'instances', inst.id, 'home')
}

export function assertUniqueHome(root: LauncherRoot, home: string, exceptId?: string): void {
  const target = resolve(home)
  for (const inst of listInstances(root)) {
    if (exceptId && inst.id === exceptId) continue
    if (sameHomePath(instanceHomePath(root, inst), target)) {
      throw pa('PA020', 'two instances share the same home path', `instance \`${inst.id}\``, {
        context: [`home: ${target}`, `holder: ${inst.id}`],
        help: ['each instance needs its own DSH_HOME'],
      })
    }
  }
}

export function createInstance(
  root: LauncherRoot,
  opts: {
    name: string
    version: string
    profile?: string
    port?: number | 'auto'
    workspace?: string
    credentialsKind?: 'global' | 'instance' | 'named'
    credentialsSet?: string
    id?: string
    home?: string
    adopted?: boolean
  },
): InstanceRecord {
  const profile = opts.profile || 'web'
  verifyVersion(root, opts.version)
  const id = opts.id || slugify(opts.name)
  if (!isValidId(id)) {
    throw new LauncherError(`cannot derive instance id from ${JSON.stringify(id)}; use [a-z0-9]+(?:-[a-z0-9]+)*`)
  }
  const dir = join(root.path, 'instances', id)
  if (existsSync(join(dir, 'instance.json'))) {
    throw new LauncherError(`instance \`${id}\` already exists`)
  }
  const home = opts.home ? resolve(opts.home) : join(dir, 'home')
  assertUniqueHome(root, home)
  const workspace = opts.workspace ? resolve(opts.workspace) : join(dir, 'workspace')
  const logs = join(dir, 'logs')
  mkdirSync(logs, { recursive: true })
  mkdirSync(workspace, { recursive: true })
  mkdirSync(join(workspace, '.agent-pack'), { recursive: true })
  if (!opts.adopted) {
    mkdirSync(join(home, 'sessions'), { recursive: true })
    try {
      writeFileSync(join(home, '.pa-write-probe'), 'ok')
      rmSync(join(home, '.pa-write-probe'))
    } catch (e) {
      throw pa('PA005', `instance home is not writable: ${e}`, `instance \`${id}\` / home`, {
        help: ['check permissions / disk'],
      })
    }
    const sessionsRoot = join(home, 'sessions')
    writeFileSync(
      join(home, 'launcher.patch.yml'),
      `- id: session-persistence-jsonl\n  config:\n    root: '${sessionsRoot.replace(/\\/g, '\\\\')}'\n`,
    )
    if ((opts.credentialsKind || 'global') === 'named' && opts.credentialsSet) {
      copyNamedCredentialsToHome(root, home, opts.credentialsSet)
    } else if ((opts.credentialsKind || 'global') === 'global') {
      copyGlobalCredentialsToHome(root, home)
    }
  }
  const rec: InstanceRecord = {
    schema: 'pack-agent.launcher.instance/v1',
    id,
    name: opts.name,
    dsh: { version: opts.version },
    profile: { name: profile, port: opts.port ?? 'auto' },
    workspace: { kind: opts.workspace ? 'existing' : 'owned', path: workspace },
    status: 'ready',
    home,
    display: { info: '', logo: null, star: false, category: '' },
    packs: { allowSet: 'default' },
    plugins: { disabled: [] },
    adopted: opts.adopted || undefined,
    credentials:
      (opts.credentialsKind || 'global') === 'named'
        ? { kind: 'named', set: opts.credentialsSet }
        : { kind: opts.credentialsKind || 'global' },
    created: nowIso(),
    updated: nowIso(),
  }
  atomicWrite(join(dir, 'instance.json'), JSON.stringify(rec, null, 2))
  return rec
}

export function getInstance(root: LauncherRoot, id: string): InstanceRecord {
  const path = join(root.path, 'instances', id, 'instance.json')
  if (!existsSync(path)) throw new LauncherError(`instance \`${id}\` not found`)
  const rec = JSON.parse(readFileSync(path, 'utf8')) as InstanceRecord
  const migrated = migrateInstanceRecord(rec)
  if (migrated.changed) {
    writeInstance(root, migrated.rec)
    const log = join(root.path, 'instances', id, 'logs', 'migrate.log')
    mkdirSync(dirname(log), { recursive: true })
    appendFileSync(log, `${nowIso()} ${migrated.log.join('; ')}\n`)
  }
  return migrated.rec
}

export function writeInstance(root: LauncherRoot, rec: InstanceRecord): void {
  rec.updated = nowIso()
  atomicWrite(join(root.path, 'instances', rec.id, 'instance.json'), JSON.stringify(rec, null, 2))
}

export function markImportFailed(root: LauncherRoot, id: string): InstanceRecord {
  const rec = getInstance(root, id)
  rec.status = 'import-failed'
  writeInstance(root, rec)
  return rec
}

export function instanceHome(root: LauncherRoot, id: string): string {
  return instanceHomePath(root, getInstance(root, id))
}

export type DshProc = { status: number | null; stdout: string; stderr: string }

export function runDsh(root: LauncherRoot, id: string, args: string[]): DshProc {
  const inst = getInstance(root, id)
  const bin = dshBin(root, inst.dsh.version)
  const home = instanceHome(root, id)
  const proc = spawnSync(process.execPath.includes('bun') ? 'node' : process.execPath, [bin, ...args], {
    cwd: inst.workspace.path,
    env: {
      ...process.env,
      ...pnpmStoreEnv(root),
      DSH_HOME: home,
      DSH_AGENTS_HOME: join(home, 'agents'),
      DSH_TELEMETRY_DISABLED: '1',
    },
    encoding: 'utf8',
  })
  return { status: proc.status, stdout: proc.stdout || '', stderr: proc.stderr || '' }
}

export function pluginAdd(root: LauncherRoot, id: string, spec: string): DshProc {
  const inst = getInstance(root, id)
  return runDsh(root, id, ['plugin', '--profile', inst.profile.name, 'add', spec])
}

export function dumpConfig(root: LauncherRoot, id: string): string {
  const r = runDsh(root, id, ['--dump-config'])
  return `${r.stdout}${r.stderr}`
}

export function removeInstance(root: LauncherRoot, id: string): void {
  const dir = join(root.path, 'instances', id)
  if (!existsSync(dir)) throw new LauncherError(`instance \`${id}\` not found`)
  rmSync(dir, { recursive: true, force: true })
}

function pidAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

function reclaim(root: LauncherRoot): RuntimeFile {
  const rt = readRuntime(root)
  rt.entries = rt.entries.filter((e) => e.status !== 'running' || pidAlive(e.pid))
  const claimed: Record<string, number> = {}
  for (const e of rt.entries) {
    if (e.status === 'running') claimed[e.instance] = e.port
  }
  rt.ports.claimed = claimed
  writeRuntime(root, rt)
  return rt
}

async function portFree(port: number): Promise<boolean> {
  return await new Promise((resolvePort) => {
    const server = createServer()
    server.once('error', () => resolvePort(false))
    server.listen(port, '127.0.0.1', () => {
      server.close(() => resolvePort(true))
    })
  })
}

async function claimPort(root: LauncherRoot, instanceId: string, wanted: number | 'auto'): Promise<number> {
  const rt = reclaim(root)
  if (wanted !== 'auto') {
    const holder = Object.entries(rt.ports.claimed).find(([id, p]) => p === wanted && id !== instanceId)
    if (holder) {
      throw pa('PA004', `web port ${wanted} is taken`, `instance \`${instanceId}\` / port`, {
        context: [`holder: ${holder[0]}`],
        help: ['stop the other instance', 'pick another port'],
      })
    }
    if (!(await portFree(wanted))) {
      throw pa('PA004', `web port ${wanted} is taken`, `instance \`${instanceId}\` / port`, {
        note: 'external process',
        help: ['pick another port'],
      })
    }
    return wanted
  }
  for (let p = 3080; p < 3200; p++) {
    if (Object.values(rt.ports.claimed).includes(p)) continue
    if (await portFree(p)) return p
  }
  throw pa('PA004', 'no free web port in 3080-3199', `instance \`${instanceId}\` / port`)
}

function killPid(pid: number): void {
  try {
    process.kill(pid)
  } catch {
    /* already gone */
  }
  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' })
  } else {
    try {
      process.kill(pid, 'SIGKILL')
    } catch {
      /* gone */
    }
  }
}

export async function runInstance(
  root: LauncherRoot,
  id: string,
  opts: { detach?: boolean } = {},
): Promise<RuntimeEntry> {
  const inst = getInstance(root, id)
  if (inst.status === 'import-failed') {
    throw pa('PA014', `import failed: instance \`${id}\` is not runnable`, `instance \`${id}\``, {
      note: `instance \`${id}\` is still on disk with status import-failed`,
      help: [`packagent dsh launcher instance remove ${id}`],
    })
  }
  verifyVersion(root, inst.dsh.version)
  const rt = reclaim(root)
  if (rt.entries.some((e) => e.instance === id && e.status === 'running' && pidAlive(e.pid))) {
    throw new LauncherError(`instance \`${id}\` is already running`)
  }
  const port = await claimPort(root, id, inst.profile.port)
  const home = instanceHome(root, id)
  const patch = join(home, 'launcher.patch.yml')
  const bin = dshBin(root, inst.dsh.version)
  const logPath = join(root.path, 'instances', id, 'logs', `${inst.profile.name}-${Date.now()}.log`)
  mkdirSync(dirname(logPath), { recursive: true })
  const log = createWriteStream(logPath, { flags: 'a' })
  const args = ['--profile', inst.profile.name]
  if (existsSync(patch)) args.push('--patch', patch)
  if (inst.profile.name === 'web') args.push('--port', String(port))
  const child: ChildProcess = spawn(process.execPath.includes('bun') ? 'node' : process.execPath, [bin, ...args], {
    cwd: inst.workspace.path,
    env: {
      ...process.env,
      ...pnpmStoreEnv(root),
      DSH_HOME: home,
      DSH_AGENTS_HOME: join(home, 'agents'),
      DSH_TELEMETRY_DISABLED: '1',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  })
  if (!child.pid) {
    log.end()
    throw new LauncherError(`failed to spawn instance \`${id}\``)
  }
  const ready = await new Promise<void>((resolveReady, rejectReady) => {
    const timer = setTimeout(() => rejectReady(new Error('ready timeout')), 20_000)
    let buf = ''
    const onData = (chunk: Buffer) => {
      const s = chunk.toString('utf8')
      buf += s
      log.write(s)
      if (buf.includes('dsh web: http://127.0.0.1:')) {
        clearTimeout(timer)
        resolveReady()
      }
    }
    child.stdout?.on('data', onData)
    child.stderr?.on('data', (chunk: Buffer) => log.write(chunk))
    child.once('exit', (code) => {
      clearTimeout(timer)
      rejectReady(new Error(`dsh exited before ready, code ${code}`))
    })
  }).catch((e) => {
    killPid(child.pid!)
    log.end()
    throw new LauncherError(String(e))
  })
  void ready
  const entry: RuntimeEntry = {
    instance: id,
    version: inst.dsh.version,
    pid: child.pid,
    port,
    status: 'running',
    startedAt: nowIso(),
    log: logPath,
  }
  const next = readRuntime(root)
  next.entries = next.entries.filter((e) => e.instance !== id)
  next.entries.push(entry)
  next.ports.claimed[id] = port
  writeRuntime(root, next)
  if (inst.status === 'restart-required') {
    inst.status = 'ready'
    writeInstance(root, inst)
  }
  if (!opts.detach) {
    await new Promise<void>((resolveWait) => child.once('exit', () => resolveWait()))
    log.end()
    const done = readRuntime(root)
    const row = done.entries.find((e) => e.instance === id)
    if (row) row.status = 'stopped'
    delete done.ports.claimed[id]
    writeRuntime(root, done)
  } else {
    child.unref()
  }
  return entry
}

export async function stopInstance(root: LauncherRoot, id: string): Promise<void> {
  const rt = reclaim(root)
  const row = rt.entries.find((e) => e.instance === id && e.status === 'running')
  if (!row) return
  killPid(row.pid)
  row.status = 'stopped'
  delete rt.ports.claimed[id]
  writeRuntime(root, rt)
}

export async function restartInstance(root: LauncherRoot, id: string, opts: { detach?: boolean } = {}): Promise<RuntimeEntry> {
  const rt = reclaim(root)
  const prevPort = rt.entries.find((e) => e.instance === id)?.port
  await stopInstance(root, id)
  const inst = getInstance(root, id)
  const saved = inst.profile.port
  if (typeof prevPort === 'number') {
    inst.profile.port = prevPort
    writeInstance(root, inst)
  }
  try {
    return await runInstance(root, id, opts)
  } finally {
    if (typeof prevPort === 'number') {
      const cur = getInstance(root, id)
      cur.profile.port = saved
      writeInstance(root, cur)
    }
  }
}

export function instanceLogs(root: LauncherRoot, id: string): { dir: string; files: string[]; latest: string | null; text: string } {
  getInstance(root, id)
  const dir = join(root.path, 'instances', id, 'logs')
  if (!existsSync(dir)) return { dir, files: [], latest: null, text: '' }
  const files = readdirSync(dir).filter((n) => n.endsWith('.log')).sort()
  const latest = files.at(-1) || null
  return {
    dir,
    files,
    latest,
    text: latest ? readFileSync(join(dir, latest), 'utf8') : '',
  }
}

export async function runLauncherCli(argv: string[]): Promise<void> {
  const args = [...argv]
  let rootPath = process.env.PACK_LAUNCHER_ROOT?.trim() || resolve(process.cwd(), '.pack-launcher')
  let json = false
  const positional: string[] = []
  for (let i = 0; i < args.length; i++) {
    const a = args[i]
    if (a === '--root') rootPath = resolve(args[++i] || '')
    else if (a === '--json') json = true
    else if (a === '--help' || a === '-h') {
      printLauncherHelp()
      return
    } else positional.push(a)
  }
  const root = ensureRoot(rootPath)
  const [group, cmd, ...rest] = positional
  const out = (value: unknown) => {
    if (json) console.log(JSON.stringify(value, null, 2))
    else if (typeof value === 'string') console.log(value)
    else console.log(JSON.stringify(value, null, 2))
  }
  const emit = (warnings: Diagnostic[]) => {
    for (const w of warnings) process.stderr.write(renderDiag(w))
  }
  try {
    if (!group || group === 'help') {
      printLauncherHelp()
      return
    }
    if (group === 'diag' && cmd === 'render') {
      const file = rest[0]
      if (!file) throw new LauncherError('Usage: packagent dsh launcher diag render <json>')
      const d = JSON.parse(readFileSync(resolve(file), 'utf8')) as Diagnostic
      process.stderr.write(renderDiag(d))
      if (json) out(d)
      return
    }
    if (group === 'version') {
      if (cmd === 'list') {
        out(listOrEmpty('version.list', listVersions(root)))
        return
      }
      if (cmd === 'install') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher version install <ver>')
        out(installVersion(root, rest[0]))
        return
      }
      if (cmd === 'verify') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher version verify <ver>')
        out(verifyVersion(root, rest[0]))
        return
      }
      if (cmd === 'remove') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher version remove <ver>')
        removeVersion(root, rest[0])
        out({ ok: true, removed: rest[0] })
        return
      }
    }
    if (group === 'instance') {
      if (cmd === 'list') {
        out(listOrEmpty('instance.list', listInstances(root)))
        return
      }
      if (cmd === 'create') {
        const name = rest[0]
        if (!name) throw new LauncherError('Usage: packagent dsh launcher instance create <name> [--version --profile --port]')
        const flags = parseCreateFlags(rest.slice(1))
        out(createInstance(root, { name, ...flags }))
        return
      }
      if (cmd === 'info') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher instance info <id>')
        out(getInstance(root, rest[0]))
        return
      }
      if (cmd === 'remove') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher instance remove <id>')
        removeInstance(root, rest[0])
        out({ ok: true, removed: rest[0] })
        return
      }
      if (cmd === 'clone') {
        const [id, newName] = rest
        if (!id || !newName) throw new LauncherError('Usage: packagent dsh launcher instance clone <id> <new-name>')
        const { cloneInstance } = await import('./instance-ops.js')
        out(cloneInstance(root, id, newName))
        return
      }
      if (cmd === 'rename') {
        const [id, newName] = rest
        if (!id || !newName) throw new LauncherError('Usage: packagent dsh launcher instance rename <id> <new-name>')
        const { renameInstance } = await import('./instance-ops.js')
        out(renameInstance(root, id, newName))
        return
      }
      if (cmd === 'pin') {
        const [id, ver] = rest
        if (!id || !ver) throw new LauncherError('Usage: packagent dsh launcher instance pin <id> <ver>')
        const { pinInstance } = await import('./instance-ops.js')
        const r = pinInstance(root, id, ver)
        emit(r.warnings)
        out(r.value)
        return
      }
      if (cmd === 'display') {
        const id = rest[0]
        if (!id) throw new LauncherError('Usage: packagent dsh launcher instance display <id> [--info --star --category --logo]')
        const patch: { info?: string; star?: boolean; category?: string; logo?: string | null } = {}
        for (let i = 1; i < rest.length; i++) {
          if (rest[i] === '--info') patch.info = rest[++i]
          else if (rest[i] === '--category') patch.category = rest[++i]
          else if (rest[i] === '--logo') patch.logo = rest[++i] === 'null' ? null : rest[i]
          else if (rest[i] === '--star') patch.star = rest[++i] !== 'false'
        }
        const { setInstanceDisplay } = await import('./instance-ops.js')
        out(setInstanceDisplay(root, id, patch))
        return
      }
      if (cmd === 'adopt') {
        let home: string | undefined
        let name: string | undefined
        let id: string | undefined
        let version = '0.1.0-rc.7'
        for (let i = 0; i < rest.length; i++) {
          if (rest[i] === '--home') home = rest[++i]
          else if (rest[i] === '--name') name = rest[++i]
          else if (rest[i] === '--id') id = rest[++i]
          else if (rest[i] === '--version') version = rest[++i] || version
        }
        const { adoptExistingHome } = await import('./adopt.js')
        out(adoptExistingHome(root, { home, name, id, version }))
        return
      }
    }
    if (group === 'export') {
      const id = cmd
      if (!id) throw new LauncherError('Usage: packagent dsh launcher export <id> [--out x.pinst.zip]')
      let outPath: string | undefined
      for (let i = 0; i < rest.length; i++) {
        if (rest[i] === '--out') outPath = rest[++i]
      }
      const { exportInstance } = await import('./pinst.js')
      out(await exportInstance(root, id, { out: outPath }))
      return
    }
    if (group === 'import') {
      const packPath = cmd
      if (!packPath) throw new LauncherError('Usage: packagent dsh launcher import <pack.json|pack.zip|*.pinst.zip> [--name <id>]')
      let name: string | undefined
      for (let i = 0; i < rest.length; i++) {
        if (rest[i] === '--name') name = rest[++i]
      }
      const { importPack } = await import('./import-pack.js')
      out(await importPack(root, resolve(packPath), { name }))
      return
    }
    if (group === 'plugin') {
      const id = rest[0]
      if (!id) throw new LauncherError('Usage: packagent dsh launcher plugin list|add|remove|update|enable|disable …')
      const { pluginAddToInstance, pluginDisable, pluginEnable, pluginList, pluginRemove, pluginUpdate } = await import('./plugin-ops.js')
      if (cmd === 'list') {
        out(listOrEmpty('plugin.list', pluginList(root, id).bundles))
        return
      }
      if (cmd === 'add') {
        const spec = rest[1]
        if (!spec) throw new LauncherError('Usage: packagent dsh launcher plugin add <id> <spec>')
        const r = pluginAddToInstance(root, id, spec)
        emit(r.warnings)
        out(r)
        return
      }
      if (cmd === 'remove') {
        const pkg = rest[1]
        if (!pkg) throw new LauncherError('Usage: packagent dsh launcher plugin remove <id> <pkg>')
        out(pluginRemove(root, id, pkg))
        return
      }
      if (cmd === 'update') {
        const pkg = rest[1]
        if (!pkg) throw new LauncherError('Usage: packagent dsh launcher plugin update <id> <pkg>')
        out(pluginUpdate(root, id, pkg))
        return
      }
      if (cmd === 'disable') {
        const pkg = rest[1]
        if (!pkg) throw new LauncherError('Usage: packagent dsh launcher plugin disable <id> <pkg>')
        const r = pluginDisable(root, id, pkg)
        emit(r.warnings)
        out(r)
        return
      }
      if (cmd === 'enable') {
        const pkg = rest[1]
        if (!pkg) throw new LauncherError('Usage: packagent dsh launcher plugin enable <id> <pkg>')
        out(pluginEnable(root, id, pkg))
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher plugin list|add|remove|update|enable|disable …')
    }
    if (group === 'pack') {
      const id = rest[0]
      if (!id) throw new LauncherError('Usage: packagent dsh launcher pack project|allow|deny|set-save|set-load|list <id> …')
      const packOps = await import('./pack-ops.js')
      if (cmd === 'list') {
        out(listOrEmpty('pack.list', await packOps.packList(root, id)))
        return
      }
      if (cmd === 'project') {
        const packPath = rest[1]
        if (!packPath) throw new LauncherError('Usage: packagent dsh launcher pack project <id> <pack.json>')
        out(await packOps.packProject(root, id, resolve(packPath), { allow: rest.includes('--allow') }))
        return
      }
      if (cmd === 'allow') {
        if (!rest[1]) throw new LauncherError('Usage: packagent dsh launcher pack allow <id> <pack-id>')
        await packOps.packAllow(root, id, rest[1])
        out({ ok: true, allowed: rest[1] })
        return
      }
      if (cmd === 'deny') {
        if (!rest[1]) throw new LauncherError('Usage: packagent dsh launcher pack deny <id> <pack-id>')
        await packOps.packDeny(root, id, rest[1])
        out({ ok: true, denied: rest[1] })
        return
      }
      if (cmd === 'set-save') {
        if (!rest[1]) throw new LauncherError('Usage: packagent dsh launcher pack set-save <id> <name>')
        await packOps.packSetSave(root, id, rest[1])
        out({ ok: true, saved: rest[1] })
        return
      }
      if (cmd === 'set-load') {
        if (!rest[1]) throw new LauncherError('Usage: packagent dsh launcher pack set-load <id> <name>')
        await packOps.packSetLoad(root, id, rest[1])
        out({ ok: true, loaded: rest[1] })
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher pack project|allow|deny|set-save|set-load|list <id> …')
    }
    if (group === 'session') {
      const id = rest[0]
      if (!id) throw new LauncherError('Usage: packagent dsh launcher session list|delete|backup|inspect …')
      const sess = await import('./session-ops.js')
      if (cmd === 'list') {
        const r = sess.listSessions(root, id)
        emit(r.warnings)
        out(listOrEmpty('session.list', r.value))
        return
      }
      if (cmd === 'inspect') {
        const sid = rest[1]
        if (!sid) throw new LauncherError('Usage: packagent dsh launcher session inspect <id> <sid>')
        const r = sess.inspectSession(root, id, sid)
        emit(r.warnings)
        out(r.value)
        return
      }
      if (cmd === 'delete') {
        const sid = rest[1]
        if (!sid) throw new LauncherError('Usage: packagent dsh launcher session delete <id> <sid>')
        const r = sess.deleteSession(root, id, sid)
        emit(r.warnings)
        out(r.value)
        return
      }
      if (cmd === 'backup') {
        out(sess.backupSessions(root, id, rest[1]))
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher session list|delete|backup|inspect …')
    }
    if (group === 'job') {
      const jobs = await import('./jobs.js')
      if (cmd === 'list') {
        out(listOrEmpty('job.list', jobs.listJobs(root)))
        return
      }
      if (cmd === 'cancel') {
        const jobId = rest[0]
        if (!jobId) throw new LauncherError('Usage: packagent dsh launcher job cancel <job-id>')
        out(jobs.cancelJob(root, jobId))
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher job list|cancel <job-id>')
    }
    if (group === 'crash') {
      const id = cmd
      if (!id) throw new LauncherError('Usage: packagent dsh launcher crash <id>')
      const { analyzeCrash } = await import('./crash.js')
      out(analyzeCrash(root, id))
      return
    }
    if (group === 'run') {
      const id = cmd
      if (!id) throw new LauncherError('Usage: packagent dsh launcher run <id> [--detach]')
      const detach = rest.includes('--detach')
      out(await runInstance(root, id, { detach }))
      return
    }
    if (group === 'stop') {
      if (!cmd) throw new LauncherError('Usage: packagent dsh launcher stop <id>')
      await stopInstance(root, cmd)
      out({ ok: true, stopped: cmd })
      return
    }
    if (group === 'restart') {
      if (!cmd) throw new LauncherError('Usage: packagent dsh launcher restart <id> [--detach]')
      out(await restartInstance(root, cmd, { detach: rest.includes('--detach') }))
      return
    }
    if (group === 'logs') {
      if (!cmd) throw new LauncherError('Usage: packagent dsh launcher logs <id>')
      out(instanceLogs(root, cmd))
      return
    }
    if (group === 'ps') {
      out(reclaim(root))
      return
    }
    if (group === 'scan-drop') {
      const { scanDropZips } = await import('./drop-scan.js')
      out(await scanDropZips(root))
      return
    }
    if (group === 'credentials') {
      const {
        getGlobalCredentials,
        getNamedCredentials,
        listCredentialSets,
        setGlobalCredentials,
        setNamedCredentials,
      } = await import('./credentials.js')
      if (cmd === 'list') {
        out(listOrEmpty('credentials.list', listCredentialSets(root)))
        return
      }
      if (cmd === 'get') {
        const name = rest[0]
        if (!name || name === 'global') {
          out({ yaml: getGlobalCredentials(root) })
          return
        }
        out({ name, yaml: getNamedCredentials(root, name) })
        return
      }
      if (cmd === 'set') {
        if (rest.length >= 2) {
          setNamedCredentials(root, rest[0], readFileSync(resolve(rest[1]), 'utf8'))
          out({ ok: true, name: rest[0] })
          return
        }
        const file = rest[0]
        if (!file) throw new LauncherError('Usage: packagent dsh launcher credentials set [<name>] <file.yaml>')
        setGlobalCredentials(root, readFileSync(resolve(file), 'utf8'))
        out({ ok: true })
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher credentials list|get|set')
    }
    if (group === 'market') {
      const market = await import('./market.js')
      if (cmd === 'list') {
        out(listOrEmpty('market.list', market.marketList(root, { category: rest[0] })))
        return
      }
      if (cmd === 'search') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher market search <q>')
        out(market.marketSearch(root, rest[0]))
        return
      }
      if (cmd === 'install') {
        const [id, spec] = rest
        if (!id || !spec) throw new LauncherError('Usage: packagent dsh launcher market install <id> <name>')
        const r = market.marketInstall(root, id, spec)
        emit(r.warnings)
        out(r)
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher market list|search|install')
    }
    if (group === 'update') {
      const upd = await import('./update.js')
      const { getMeta } = await import('./meta-cache.js')
      if (cmd === 'check') {
        const pkg = JSON.parse(readFileSync(join(import.meta.dirname, '../../package.json'), 'utf8')) as { version: string }
        const cached = getMeta<{ versions?: string[] }>(root, 'pack-agent-versions')
        const published = Array.isArray(cached?.body?.versions) ? cached.body.versions : []
        const cfg = readJson<{ update?: { channel?: string } }>(join(root.path, 'launcher.json'), {})
        const channel = cfg.update?.channel === 'dev' ? 'dev' : 'stable'
        out(upd.checkUpdate(root, { current: pkg.version, published, channel }))
        return
      }
      if (cmd === 'apply') {
        const ver = rest[0]
        const src = rest[1]
        if (!ver || !src) throw new LauncherError('Usage: packagent dsh launcher update apply <ver> <dir>')
        out(upd.applyUpdate(root, { version: ver, sourceDir: resolve(src) }))
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher update check|apply')
    }
    if (group === 'shortcut') {
      const id = cmd
      if (!id) throw new LauncherError('Usage: packagent dsh launcher shortcut <id>')
      const { writeShortcut } = await import('./shortcut.js')
      out({ path: writeShortcut(root, id) })
      return
    }
    if (group === 'meta') {
      const meta = await import('./meta-cache.js')
      if (cmd === 'get') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher meta get <key>')
        out(meta.getMeta(root, rest[0]))
        return
      }
      if (cmd === 'refresh') {
        if (!rest[0]) throw new LauncherError('Usage: packagent dsh launcher meta refresh <key>')
        out(await meta.refreshMeta(root, rest[0], { fetch: async () => ({}) }))
        return
      }
      throw new LauncherError('Usage: packagent dsh launcher meta get|refresh <key>')
    }
    if (group === 'doctor') {
      const { doctorLauncher } = await import('./doctor.js')
      const report = doctorLauncher(root)
      if (!report.ok) process.exitCode = 1
      out(report)
      return
    }
    throw new LauncherError(`Unknown launcher command: ${group} ${cmd || ''}`.trim())
  } catch (e) {
    if (e instanceof LauncherError && e.diagnostic) {
      process.stderr.write(renderDiag(e.diagnostic))
      if (json) console.log(JSON.stringify({ ok: false, error: e.diagnostic }, null, 2))
      process.exitCode = 1
      return
    }
    throw e
  }
}

function parseCreateFlags(args: string[]): {
  version: string
  profile?: string
  port?: number | 'auto'
  workspace?: string
  credentialsKind?: 'global' | 'instance' | 'named'
  credentialsSet?: string
} {
  let version = '0.1.0-rc.7'
  let profile: string | undefined
  let port: number | 'auto' | undefined
  let workspace: string | undefined
  let credentialsKind: 'global' | 'instance' | 'named' | undefined
  let credentialsSet: string | undefined
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--version') version = args[++i] || version
    else if (args[i] === '--profile') profile = args[++i]
    else if (args[i] === '--shell') throw new LauncherError('use --profile, not --shell')
    else if (args[i] === '--port') {
      const v = args[++i]
      port = v === 'auto' ? 'auto' : Number(v)
    } else if (args[i] === '--workspace') workspace = args[++i]
    else if (args[i] === '--credentials') {
      const v = args[++i]
      if (v === 'instance' || v === 'global') credentialsKind = v
      else if (v) {
        credentialsKind = 'named'
        credentialsSet = v
      }
    }
  }
  return { version, profile, port, workspace, credentialsKind, credentialsSet }
}

function printLauncherHelp(): void {
  console.log(`Usage:
  packagent dsh launcher version  list|install <ver>|verify <ver>|remove <ver>
  packagent dsh launcher instance list|create <name> [--version --profile --port --workspace --credentials]
                          |clone <id> <new-name>|rename <id> <new-name>|pin <id> <ver>
                          |info|remove <id>|display <id>|adopt
  packagent dsh launcher plugin  list <id>|add <id> <spec>|remove <id> <pkg>
                          |update <id> <pkg>|enable <id> <pkg>|disable <id> <pkg>
  packagent dsh launcher pack    project <id> <pack>|allow <id> <pack-id>|deny <id> <pack-id>
                          |set-save <id> <name>|set-load <id> <name>|list <id>
  packagent dsh launcher session list <id>|delete <id> <sid>|backup <id> [<sid>]|inspect <id> <sid>
  packagent dsh launcher import <pack.json|pack.zip|*.pinst.zip> [--name <id>]
  packagent dsh launcher export <id> [--out x.pinst.zip]
  packagent dsh launcher scan-drop
  packagent dsh launcher credentials list|get [<name>]|set [<name>] <file.yaml>
  packagent dsh launcher market  list [<category>]|search <q>|install <id> <name>
  packagent dsh launcher update  check|apply <ver> <dir>
  packagent dsh launcher shortcut <id>
  packagent dsh launcher meta get|refresh <key>
  packagent dsh launcher run <id> [--detach]|stop <id>|restart <id>|ps|logs <id>
  packagent dsh launcher job list|cancel <job-id>
  packagent dsh launcher crash <id>
  packagent dsh launcher doctor
  packagent dsh launcher diag render <json>

Flags: --root <dir>  --json`)
}
