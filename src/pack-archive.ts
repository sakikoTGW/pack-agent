/**
 * 整合包 zip：pack.json + 实体 skills/ + rules/ + mcp.json
 * 与单文件 .pack.json 并存；export 默认主产物为 .pack.zip。
 */
import { promises as fs } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import { packTmpRoot } from './tmp-root.js'
import { spawnSync } from 'node:child_process'
import type { PackDoc, PackMcpEntry } from './types.js'
import type { BundleFile } from './portable.js'

export type PackArchiveLayout = {
  root: string
  packJsonPath: string
  mcpJsonPath: string
  skillCount: number
  ruleCount: number
  mcpCount: number
}

function packMcpServersDoc(pack: PackDoc): { mcpServers: Record<string, Record<string, unknown>> } {
  const mcpServers: Record<string, Record<string, unknown>> = {}
  for (const m of pack.tools?.mcp ?? []) {
    const n = String(m.name || '').trim()
    if (!n) continue
    const cfg: Record<string, unknown> = {}
    if (m.url) {
      cfg.type = m.type || 'http'
      cfg.url = m.url
    } else if (m.command) {
      cfg.type = 'stdio'
      cfg.command = m.command
      if (m.args?.length) cfg.args = m.args
    } else continue
    if (m.env && Object.keys(m.env).length) cfg.env = m.env
    mcpServers[n] = cfg
  }
  return { mcpServers }
}

function mcpEntriesFromServers(doc: { mcpServers?: Record<string, Record<string, unknown>> }): PackMcpEntry[] {
  const out: PackMcpEntry[] = []
  for (const [name, cfg] of Object.entries(doc.mcpServers ?? {})) {
    if (!cfg || typeof cfg !== 'object') continue
    const entry: PackMcpEntry = { name }
    if (typeof cfg.url === 'string') {
      entry.url = cfg.url
      entry.type = String(cfg.type || 'http')
    } else if (typeof cfg.command === 'string') {
      entry.command = cfg.command
      entry.type = String(cfg.type || 'stdio')
      if (Array.isArray(cfg.args)) entry.args = cfg.args.map(String)
    } else continue
    if (cfg.env && typeof cfg.env === 'object') entry.env = cfg.env as Record<string, string>
    out.push(entry)
  }
  return out
}

/** 把 PackDoc 展开成整合包目录（实体文件，可再打 zip） */
export async function materializePackArchiveLayout(pack: PackDoc, root: string): Promise<PackArchiveLayout> {
  await fs.mkdir(root, { recursive: true })
  const packJsonPath = join(root, 'pack.json')
  await fs.writeFile(packJsonPath, JSON.stringify(pack, null, 2), 'utf8')

  let skillCount = 0
  let ruleCount = 0
  for (const f of pack.bundle?.files ?? []) {
    const dest = join(root, f.path.replace(/\//g, '\\'))
    await fs.mkdir(dirname(dest), { recursive: true })
    await fs.writeFile(dest, f.content, 'utf8')
    if (f.path.startsWith('skills/')) skillCount++
    if (f.path.startsWith('rules/')) ruleCount++
  }

  const mcpDoc = packMcpServersDoc(pack)
  const mcpJsonPath = join(root, 'mcp.json')
  await fs.writeFile(mcpJsonPath, JSON.stringify(mcpDoc, null, 2), 'utf8')
  const mcpCount = Object.keys(mcpDoc.mcpServers).length

  const name = pack.name || 'pack'
  const install = [
    `# ${name} — Agent Modpack`,
    '',
    '本 zip = 整合包：pack.json + skills/ + rules/ + mcp.json',
    '',
    '安装：',
    '  npx --yes -p @sakikotgw/pack-agent packagent install <本文件.zip>',
    '或：',
    '  packagent install <本文件.zip>',
    '',
    `skills 文件数: ${skillCount}`,
    `rules 文件数: ${ruleCount}`,
    `mcp servers: ${mcpCount}`,
    '',
  ].join('\n')
  await fs.writeFile(join(root, 'INSTALL.md'), install, 'utf8')

  return { root, packJsonPath, mcpJsonPath, skillCount, ruleCount, mcpCount }
}

function runTar(args: string[], cwd?: string): void {
  const r = spawnSync('tar', args, { cwd, encoding: 'utf8', shell: false })
  if (r.status !== 0) {
    const err = (r.stderr || r.stdout || '').trim() || `tar exit ${r.status}`
    throw new Error(`tar failed: ${err}`)
  }
}

export async function writeZipFromDir(root: string, zipPath: string): Promise<void> {
  await fs.mkdir(dirname(zipPath), { recursive: true })

  const lower = zipPath.toLowerCase()
  const tempZipPath = lower.endsWith('.zip') ? zipPath : `${zipPath}.zip`

  await fs.rm(zipPath, { force: true }).catch(() => {})
  if (tempZipPath !== zipPath) {
    await fs.rm(tempZipPath, { force: true }).catch(() => {})
  }

  runTar(['-a', '-cf', tempZipPath, '-C', root, '.'])

  if (tempZipPath !== zipPath) {
    await fs.rm(zipPath, { force: true }).catch(() => {})
    await fs.rename(tempZipPath, zipPath)
  }
}

/** 写出 .pack.zip（内含 pack.json + 实体 skills/rules + mcp.json） */
export async function writePackZip(pack: PackDoc, zipPath: string): Promise<PackArchiveLayout> {
  const stage = join(packTmpRoot(), `pack-agent-zip-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`)
  try {
    const layout = await materializePackArchiveLayout(pack, stage)
    await writeZipFromDir(stage, zipPath)
    return layout
  } finally {
    await fs.rm(stage, { recursive: true, force: true }).catch(() => {})
  }
}

async function walkFiles(dir: string, base = dir): Promise<BundleFile[]> {
  const out: BundleFile[] = []
  let entries: import('node:fs').Dirent[] = []
  try {
    entries = await fs.readdir(dir, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    const abs = join(dir, e.name)
    if (e.isDirectory()) {
      out.push(...(await walkFiles(abs, base)))
      continue
    }
    if (!e.isFile()) continue
    const rel = abs.slice(base.length).replace(/^[\\/]/, '').replace(/\\/g, '/')
    out.push({ path: rel, content: await fs.readFile(abs, 'utf8') })
  }
  return out
}

/** 从已解压的整合包目录恢复 PackDoc（优先 pack.json；缺 bundle 则从 skills/rules 重建） */
export async function loadPackFromArchiveDir(root: string): Promise<PackDoc> {
  const packPath = join(root, 'pack.json')
  let raw: string
  try {
    raw = await fs.readFile(packPath, 'utf8')
  } catch {
    const names = await fs.readdir(root)
    const hit = names.find(n => n.endsWith('.pack.json'))
    if (!hit) throw new Error(`pack archive missing pack.json under ${root}`)
    raw = await fs.readFile(join(root, hit), 'utf8')
  }
  let pack = JSON.parse(raw) as PackDoc

  const files: BundleFile[] = [...(pack.bundle?.files ?? [])]
  const have = new Set(files.map(f => f.path))

  for (const sub of ['skills', 'rules'] as const) {
    const dir = join(root, sub)
    try {
      await fs.access(dir)
    } catch {
      continue
    }
    for (const f of await walkFiles(dir)) {
      const path = `${sub}/${f.path}`
      if (have.has(path)) continue
      have.add(path)
      files.push({ path, content: f.content })
    }
  }

  if (files.length) {
    pack = { ...pack, bundle: { portable: true, files }, meta: { ...pack.meta, portable: true } }
  }

  try {
    const mcpRaw = await fs.readFile(join(root, 'mcp.json'), 'utf8')
    const mcpDoc = JSON.parse(mcpRaw) as { mcpServers?: Record<string, Record<string, unknown>> }
    const fromFile = mcpEntriesFromServers(mcpDoc)
    if (fromFile.length && !(pack.tools?.mcp?.length)) {
      pack = { ...pack, tools: { ...pack.tools, mcp: fromFile } }
    }
  } catch {
    /* optional */
  }

  return pack
}

export function isPackZipPath(path: string): boolean {
  const n = path.toLowerCase()
  return n.endsWith('.pack.zip') || (n.endsWith('.zip') && !n.endsWith('.pack.json'))
}

/** 解压 .pack.zip 到临时目录并读出 PackDoc */
export async function readPackZip(zipPath: string): Promise<{ pack: PackDoc; extractedRoot: string }> {
  const root = join(packTmpRoot(), `pack-agent-unz-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`)
  await fs.mkdir(root, { recursive: true })
  runTar(['-xf', zipPath, '-C', root])
  const pack = await loadPackFromArchiveDir(root)
  return { pack, extractedRoot: root }
}
