/**
 * import 查 task-kinds 链执行。失败在 create 之后留下实例并英文提醒。
 */
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { spawnSync } from 'node:child_process'
import { basename, dirname, join, resolve } from 'node:path'
import type { PackDoc } from '../../src/types.js'
import { isPackZipPath, readPackZip } from '../../src/pack-archive.js'
import { catalogSetSave, projectPack } from './catalog.js'
import { DshVersionError, resolveDshVersion } from './dsh-version.js'
import { ENGINE_PRIMITIVES } from './engine-primitives.js'
import {
  LauncherError,
  createInstance,
  dumpConfig,
  ensureRoot,
  installVersion,
  instanceHome,
  listVersions,
  markImportFailed,
  pluginAdd,
  renderDiag,
  slugify,
  verifyVersion,
  type Diagnostic,
  type InstanceRecord,
  type LauncherRoot,
} from './launcher.js'
import { readPinstZip, restorePinstHome } from './pinst.js'
import { builtinRegistryDir, loadRegistryStore, RegistryError, type RegistryStore } from './registry-store.js'

function pa(code: string, message: string, location: string, extra: Partial<Diagnostic> = {}): LauncherError {
  const diagnostic: Diagnostic = { code, level: 'error', message, location, ...extra }
  return new LauncherError(renderDiag(diagnostic).trim(), diagnostic)
}

const PRIMITIVES = new Set<string>(ENGINE_PRIMITIVES)

type ImportCtx = {
  root: LauncherRoot
  store: RegistryStore
  packPath: string
  packRoot: string
  pack: PackDoc
  name?: string
  version: string
  instance?: InstanceRecord
  cleanup?: string
  kind?: string
  handler?: string
  pinstRoot?: string
  publishedVersions?: string[]
  sniffMatch?: Record<string, unknown>
}

function leftover(ctx: ImportCtx, err: unknown): never {
  const id = ctx.instance?.id
  if (id) markImportFailed(ctx.root, id)
  let thrown = err
  if (!(err instanceof LauncherError) && id) {
    thrown = pa('PA009', `import failed: ${String(err)}`, `instance \`${id}\``, {
      note: `instance \`${id}\` is still on disk with status import-failed`,
      help: [`packagent dsh launcher instance remove ${id}`],
    })
  }
  if (thrown instanceof LauncherError && thrown.diagnostic && id) {
    const d = thrown.diagnostic
    if (!/import failed/i.test(d.message)) d.message = `import failed: ${d.message}`
    const stay = `instance \`${id}\` is still on disk with status import-failed`
    if (!d.note || !/still on disk/i.test(d.note)) d.note = d.note ? `${d.note}; ${stay}` : stay
    const help = `packagent dsh launcher instance remove ${id}`
    d.help = [...(d.help ?? []).filter((h) => h !== help), help]
    thrown.message = renderDiag(d).trim()
  }
  throw thrown
}

function loadStore(root: LauncherRoot): RegistryStore {
  try {
    const store = loadRegistryStore({
      builtinDir: builtinRegistryDir(),
      userDir: join(root.path, 'library', 'registries'),
    })
    for (const w of store.warnings) {
      process.stderr.write(
        renderDiag({ code: w.code, level: 'warning', message: w.message, location: w.id }),
      )
    }
    return store
  } catch (e) {
    if (e instanceof RegistryError) throw pa(e.code, e.message, 'registry')
    throw e
  }
}

function asMatch(body: Record<string, unknown>): Record<string, unknown> {
  const m = body.match
  if (!m || typeof m !== 'object' || Array.isArray(m)) return {}
  return m as Record<string, unknown>
}

function matchSniff(body: Record<string, unknown>, abs: string, pack: PackDoc | undefined): boolean {
  const match = asMatch(body)
  if (match.file && basename(abs) !== String(match.file)) return false
  if (match.suffix) {
    if (!abs.toLowerCase().replace(/\\/g, '/').endsWith(String(match.suffix).toLowerCase())) return false
  }
  const prefixes = match.schemaPrefix
  if (Array.isArray(prefixes) && prefixes.length) {
    const schema = String(pack?.schema || '')
    if (!prefixes.some((p) => schema.startsWith(String(p)))) return false
  }
  return true
}

function peekPack(abs: string): PackDoc | undefined {
  if (!existsSync(abs)) return undefined
  if (abs.toLowerCase().endsWith('.json')) {
    try {
      return JSON.parse(readFileSync(abs, 'utf8')) as PackDoc
    } catch {
      return undefined
    }
  }
  return undefined
}

async function sniff(ctx: ImportCtx): Promise<void> {
  const abs = resolve(ctx.packPath)
  if (!existsSync(abs)) throw pa('PA009', 'pack path does not exist', abs)
  const peeked = peekPack(abs)
  let hit: { kind: string; handler: string } | undefined
  for (const ent of ctx.store.entries('format-sniff')) {
    if (ent.disabled || ent.body.disabled === true) continue
    if (!matchSniff(ent.body, abs, peeked)) continue
    hit = { kind: String(ent.body.kind || ''), handler: String(ent.body.handler || '') }
    break
  }
  if (!hit) throw pa('PA009', 'no format-sniff match', abs)
  ctx.kind = hit.kind
  ctx.handler = hit.handler
  const sniffEnt = ctx.store.entries('format-sniff').find((ent) => {
    if (ent.disabled || ent.body.disabled === true) return false
    return matchSniff(ent.body, abs, peeked)
  })
  ctx.sniffMatch = sniffEnt ? asMatch(sniffEnt.body) : {}
  if (hit.kind === 'pinst' || hit.handler === 'import-pinst') return
  if (isPackZipPath(abs) && abs.toLowerCase().endsWith('.pack.zip')) {
    const { pack, extractedRoot } = await readPackZip(abs)
    ctx.pack = pack
    ctx.packRoot = extractedRoot
    ctx.cleanup = extractedRoot
    return
  }
  ctx.pack = peeked || (JSON.parse(readFileSync(abs, 'utf8')) as PackDoc)
  ctx.packRoot = dirname(abs)
}

function fetchPublished(): string[] {
  const r = spawnSync('npm', ['view', '@deepseek-ai/dsh', 'versions', '--json'], {
    encoding: 'utf8',
    shell: process.platform === 'win32',
  })
  if (r.status !== 0) {
    throw pa('PA013', 'registry unreachable', 'npm view @deepseek-ai/dsh versions', {
      context: [(r.stderr || r.stdout || '').slice(0, 400)],
    })
  }
  try {
    const parsed = JSON.parse(r.stdout || '[]') as unknown
    return Array.isArray(parsed) ? parsed.map(String) : [String(parsed)]
  } catch (e) {
    throw pa('PA013', `registry unreachable: ${e}`, 'npm view @deepseek-ai/dsh versions')
  }
}

function resolveVersion(ctx: ImportCtx): void {
  const spec = (ctx.version || ctx.pack.dsh?.version || '').trim()
  if (!spec) {
    throw pa('PA012', 'pack is missing dsh.version, or that release is not on npm', ctx.packPath, {
      help: ['set dsh.version to a published exact version'],
    })
  }
  const published = ctx.publishedVersions ?? fetchPublished()
  try {
    ctx.version = resolveDshVersion(spec, published)
  } catch (e) {
    if (e instanceof DshVersionError) {
      throw pa(e.code, e.message, ctx.packPath, { help: ['set dsh.version to a published exact version'] })
    }
    throw e
  }
}

function ensureVersion(ctx: ImportCtx): void {
  const have = listVersions(ctx.root).some((v) => v.version === ctx.version)
  if (have) verifyVersion(ctx.root, ctx.version)
  else installVersion(ctx.root, ctx.version)
}

function uniqueName(root: LauncherRoot, wanted: string): string {
  let n = 2
  let name = wanted
  while (existsSync(join(root.path, 'instances', slugify(name), 'instance.json'))) {
    name = `${wanted}-${n}`
    n++
  }
  return name
}

function createInst(ctx: ImportCtx): void {
  let name = ctx.name || ctx.pack.name || slugFromPath(ctx.packPath)
  if (ctx.kind === 'pinst') name = uniqueName(ctx.root, name)
  ctx.instance = createInstance(ctx.root, {
    name,
    version: ctx.version,
    profile: ctx.pack.dsh?.profile || 'web',
    port: ctx.kind === 'pinst' ? 'auto' : undefined,
  })
}

function slugFromPath(p: string): string {
  return basename(p).replace(/\.(pack\.)?json$/i, '').replace(/\.pack\.zip$/i, '') || 'pack'
}

function copyOverrides(ctx: ImportCtx): void {
  const inst = ctx.instance!
  const rows = ctx.pack.dsh?.overrides || []
  for (const ov of rows) {
    const fromName = basename(ov.from)
    const toName = basename(ov.to)
    if (fromName === '.credentials.yaml' || toName === '.credentials.yaml') {
      throw pa('PA009', 'overrides must not copy .credentials.yaml', ov.from)
    }
    const src = resolve(ctx.packRoot, ov.from)
    if (!existsSync(src)) throw pa('PA009', `override from is not in the pack: ${ov.from}`, ov.from)
    const dest = resolve(inst.workspace.path, ov.to)
    const ws = resolve(inst.workspace.path)
    if (!dest.startsWith(ws)) throw pa('PA009', `override to escapes workspace: ${ov.to}`, ov.to)
    mkdirSync(dirname(dest), { recursive: true })
    cpSync(src, dest)
  }
}

async function projectAllow(ctx: ImportCtx): Promise<void> {
  const inst = ctx.instance!
  await projectPack(ctx.pack, inst.workspace.path, { allow: true })
  await catalogSetSave(inst.workspace.path, inst.id)
}

function installManager(ctx: ImportCtx): void {
  const inst = ctx.instance!
  const row = ctx.store.entry('task-kinds', 'install-manager')
  if (!row || row.disabled) throw pa('PA018', 'runtime referenced an id that is not in the merged registry', 'task-kinds/install-manager')
  const spec = String(row.body.spec || '')
  if (!spec) throw pa('PA018', 'install-manager is missing spec', 'task-kinds/install-manager')
  const added = pluginAdd(ctx.root, inst.id, spec)
  if (added.status !== 0) {
    throw pa('PA019', 'import failed: pack-agent manager is missing from --dump-config', `instance \`${inst.id}\``, {
      context: [(added.stderr || added.stdout).slice(0, 400)],
      help: ['retry plugin add for the manager'],
    })
  }
  const dump = dumpConfig(ctx.root, inst.id)
  if (!dump.includes('- id: pack-agent')) {
    throw pa('PA019', 'import failed: pack-agent manager is missing from --dump-config', `instance \`${inst.id}\``, {
      context: [dump.slice(0, 400)],
      help: ['retry plugin add for the manager'],
    })
  }
}

function addPlugins(ctx: ImportCtx): void {
  const inst = ctx.instance!
  for (const plug of ctx.pack.dsh?.plugins || []) {
    const spec = String(plug.spec || plug.name || '').trim()
    if (!spec) continue
    const required = plug.required !== false
    const r = pluginAdd(ctx.root, inst.id, spec)
    if (r.status === 0) continue
    if (!required) {
      process.stderr.write(`optional plugin skipped: ${spec}\n`)
      continue
    }
    throw pa(
      'PA014',
      `import failed: required dsh plugin add failed for ${spec}`,
      `instance \`${inst.id}\` / dsh.plugins`,
      {
        context: [(r.stderr || r.stdout).slice(0, 400)],
        note: `instance \`${inst.id}\` is still on disk with status import-failed; already-added plugins were not rolled back`,
        help: [`packagent dsh launcher instance remove ${inst.id}`],
      },
    )
  }
}

function readPinst(ctx: ImportCtx): void {
  const abs = resolve(ctx.packPath)
  let loaded: ReturnType<typeof readPinstZip>
  try {
    loaded = readPinstZip(abs)
  } catch (e) {
    throw pa('PA009', `pinst structure is invalid: ${e}`, abs)
  }
  ctx.pinstRoot = loaded.root
  ctx.cleanup = loaded.root
  ctx.packRoot = loaded.root
  if (loaded.manifest.schema !== 'pack-agent.pinst/v1') {
    throw pa('PA009', `pinst schema ${String(loaded.manifest.schema)} is not pack-agent.pinst/v1`, abs)
  }
  const expect = ctx.sniffMatch?.manifestSchema
  if (expect && loaded.manifest.schema !== String(expect)) {
    throw pa('PA009', `pinst schema ${loaded.manifest.schema} != ${expect}`, abs)
  }
  const inst = loaded.instance
  const version = inst.dsh?.version || loaded.manifest.instance?.version || ''
  const profile = inst.profile?.name || loaded.manifest.instance?.profile || 'web'
  const name = inst.name || loaded.manifest.instance?.name
  ctx.pack = { name, dsh: { version, profile } }
  ctx.version = version
}

function restorePinst(ctx: ImportCtx): void {
  const inst = ctx.instance!
  if (!ctx.pinstRoot) throw pa('PA009', 'pinst root missing', ctx.packPath)
  restorePinstHome(ctx.pinstRoot, instanceHome(ctx.root, inst.id), inst.workspace.path)
  const home = instanceHome(ctx.root, inst.id)
  const sessionsRoot = join(home, 'sessions')
  mkdirSync(sessionsRoot, { recursive: true })
  writeFileSync(
    join(home, 'launcher.patch.yml'),
    `- id: session-persistence-jsonl\n  config:\n    root: '${sessionsRoot.replace(/\\/g, '\\\\')}'\n`,
  )
}

const steps: Record<string, (ctx: ImportCtx) => void | Promise<void>> = {
  sniff,
  'resolve-version': resolveVersion,
  'ensure-version': ensureVersion,
  'create-instance': createInst,
  'copy-overrides': copyOverrides,
  'project-allow': projectAllow,
  'install-manager': installManager,
  'add-plugins': addPlugins,
  'read-pinst': readPinst,
  'restore-pinst': restorePinst,
}

export async function importPack(
  root: LauncherRoot,
  packPath: string,
  opts: { name?: string; publishedVersions?: string[] } = {},
): Promise<InstanceRecord> {
  ensureRoot(root.path)
  const store = loadStore(root)
  const ctx: ImportCtx = {
    root,
    store,
    packPath,
    packRoot: dirname(resolve(packPath)),
    pack: {},
    name: opts.name,
    version: '',
    publishedVersions: opts.publishedVersions,
  }
  try {
    await sniff(ctx)
    const chainId = ctx.handler === 'import-pinst' ? 'import-pinst' : 'import'
    const chain = store.entry('task-kinds', chainId)
    if (!chain || chain.disabled) {
      throw pa('PA018', 'runtime referenced an id that is not in the merged registry', `task-kinds/${chainId}`)
    }
    const rawSteps = chain.body.steps
    if (!Array.isArray(rawSteps) || !rawSteps.length) {
      throw pa('PA017', `${chainId} task-kinds is missing steps`, `task-kinds/${chainId}`)
    }
    for (const row of rawSteps) {
      const prim = row && typeof row === 'object' ? String((row as { primitive?: string }).primitive || '') : ''
      if (prim === 'sniff') continue
      if (!PRIMITIVES.has(prim) || !steps[prim]) {
        throw pa('PA018', `runtime referenced an id that is not in the merged registry`, `primitive ${prim}`)
      }
      await steps[prim](ctx)
    }
  } catch (e) {
    leftover(ctx, e)
  } finally {
    if (ctx.cleanup) rmSync(ctx.cleanup, { recursive: true, force: true })
  }
  return ctx.instance!
}
