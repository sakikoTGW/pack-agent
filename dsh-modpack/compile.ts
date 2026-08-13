/**
 * 把 pack-agent PackDoc 编成投影目录（dsh.bundle 形状）。
 * 主路径：写入 `.agent-pack/modpacks/<id>/`，入 SQLite 注册表，允许集才对会话可见。
 * 不要 `dsh plugin add` 这份整合包。
 */
import { promises as fs } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { stringify as yamlStringify } from 'yaml'
import { PACK_MOD_SCHEMA, type PackDoc, type PackModManifest } from '../src/types.js'
import { isPackZipPath, readPackZip } from '../src/pack-archive.js'
import { loadRegistry } from './registry.js'
import { remapLegacyPack } from './map.js'

export type CompileResult = {
  dir: string
  npmName: string
  installCommand: string
}

type InsertItem = {
  id: string
  name: string
  config?: Record<string, unknown>
  disabled?: boolean
  inject?: string[]
}

function remapBundlePath(rel: string): string {
  if (rel.startsWith('rules/')) return `instructions/${rel.slice('rules/'.length)}`
  return rel
}

function hasFilePrefix(files: Array<{ path: string }> | undefined, prefix: string): boolean {
  return Boolean(files?.some(f => f.path.replace(/\\/g, '/').startsWith(prefix)))
}

export async function loadPackDoc(path: string): Promise<PackDoc> {
  const abs = resolve(path)
  if (isPackZipPath(abs)) {
    const { pack, extractedRoot } = await readPackZip(abs)
    await fs.rm(extractedRoot, { recursive: true, force: true }).catch(() => {})
    return pack
  }
  return JSON.parse(await fs.readFile(abs, 'utf8')) as PackDoc
}

export function npmNameForPack(name: string): string {
  return `pack-agent-modpack-${slug(name)}`
}

export function slug(name: string): string {
  return name.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'pack'
}

function pluginId(prefix: string, name: string): string {
  return `${prefix}-${slug(name)}`
}

async function writeShippedMod(
  dir: string,
  manifest: PackModManifest,
  files: Record<string, string>,
): Promise<void> {
  const id = String(manifest.id || '').trim()
  if (!id || id.includes('/') || id.includes('\\') || id.startsWith('.') || id.includes('.agent-pack-origin.json')) {
    throw new Error(`invalid mod id: ${manifest.id}`)
  }
  const modDir = join(dir, 'mods', id)
  await fs.mkdir(modDir, { recursive: true })
  await fs.writeFile(join(modDir, 'mod.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8')
  for (const [fileName, content] of Object.entries(files)) {
    await fs.writeFile(join(modDir, fileName), content, 'utf8')
  }
}

type CatalogUnit = { kind: string; name: string; path: string }

function unitFromRel(srcRel: string, destRel: string): CatalogUnit | undefined {
  const p = srcRel.replace(/\\/g, '/')
  const segs = p.split('/')
  if (segs.some(seg => seg.startsWith('.') || seg === '.agent-pack-origin.json')) return undefined
  if (p.startsWith('skills/')) {
    const rest = p.slice('skills/'.length)
    if (!rest.toLowerCase().endsWith('.md')) return undefined
    const name = rest.endsWith('/SKILL.md')
      ? rest.slice(0, -'/SKILL.md'.length).split('/').pop() || rest
      : rest.replace(/\.md$/i, '')
    if (!name || name.includes('/') || name.startsWith('.')) return undefined
    return { kind: 'skill', name, path: destRel }
  }
  if (p.startsWith('commands/')) {
    return { kind: 'command', name: p.slice('commands/'.length).replace(/\.(md|txt)$/i, ''), path: destRel }
  }
  if (p.startsWith('rules/') || p.startsWith('instructions/')) {
    return { kind: 'rule', name: destRel.split('/').pop() || destRel, path: destRel }
  }
  if (p.startsWith('agents/')) {
    return { kind: 'subagent', name: p.slice('agents/'.length).replace(/\.md$/i, ''), path: destRel }
  }
  if (p.startsWith('memory/')) {
    return { kind: 'memory', name: p.slice('memory/'.length), path: destRel }
  }
  if (p.startsWith('settings/')) {
    return { kind: 'settings', name: p.slice('settings/'.length), path: destRel }
  }
  if (p.startsWith('automation/') || p === 'hooks.json') {
    return { kind: 'hook', name: destRel.split('/').pop() || 'hooks', path: destRel }
  }
  if (p.startsWith('experiences/')) {
    return { kind: 'experience', name: p.slice('experiences/'.length), path: destRel }
  }
  return undefined
}

export async function compilePackToDshBundle(pack: PackDoc, outDir: string): Promise<CompileResult> {
  pack = remapLegacyPack(pack)
  const registry = loadRegistry()
  const packName = pack.name?.trim() || 'pack'
  const npmName = npmNameForPack(packName)
  const dir = outDir
  await fs.mkdir(dir, { recursive: true })

  let skillCount = 0
  let hasAutomation = false
  const units: CatalogUnit[] = []
  const skillBodies = new Map<string, string>()
  const extraFileMods: Array<{ kind: 'rule' | 'command' | 'hook'; name: string; fileName: string; content: string }> = []
  for (const f of pack.bundle?.files ?? []) {
    const rel = f.path.replace(/\\/g, '/')
    if (rel.split('/').some(seg => seg.startsWith('.'))) continue
    const destRel = remapBundlePath(rel)
    const dest = join(dir, ...destRel.split('/'))
    await fs.mkdir(dirname(dest), { recursive: true })
    await fs.writeFile(dest, f.content, 'utf8')
    if (rel.startsWith('skills/')) skillCount++
    if (rel.startsWith('automation/') || rel === 'hooks.json') hasAutomation = true
    const unit = unitFromRel(rel, destRel)
    if (unit) {
      units.push(unit)
      if (unit.kind === 'skill') skillBodies.set(unit.name, f.content)
      if (unit.kind === 'rule' || unit.kind === 'command' || unit.kind === 'hook') {
        extraFileMods.push({
          kind: unit.kind,
          name: unit.name,
          fileName: destRel.split('/').pop() || unit.name,
          content: f.content,
        })
      }
    }
  }

  const inserts: InsertItem[] = []
  const skillUnit = registry.units.skill
  if (skillCount > 0 && skillUnit?.plugin && skillUnit.insertIdPrefix) {
    inserts.push({
      id: pluginId(skillUnit.insertIdPrefix, packName),
      name: skillUnit.plugin,
      config: {
        providerName: `pack-agent-${slug(packName)}`,
        includeDefaultRoots: false,
        customSkillDirs: ['./skills'],
      },
    })
  }

  const mcpUnit = registry.units.mcp
  for (const m of pack.tools?.mcp ?? []) {
    const serverName = String(m.name || '').trim()
    if (!serverName || !mcpUnit?.plugin || !mcpUnit.insertIdPrefix) continue
    const config: Record<string, unknown> = { serverName }
    if (m.url) {
      config.transport = 'streamable-http'
      config.url = m.url
    } else {
      config.transport = 'stdio'
      config.command = m.command
      if (m.args?.length) config.args = m.args
      if (m.env && Object.keys(m.env).length) config.env = m.env
    }
    inserts.push({
      id: pluginId(mcpUnit.insertIdPrefix, serverName),
      name: mcpUnit.plugin,
      config,
    })
  }

  const personaText = pack.dsh?.persona?.trim() || pack.harness?.base_system_prompt?.trim()
  const personaUnit = registry.units.persona
  if (personaText && personaUnit?.plugin && personaUnit.insertIdPrefix) {
    inserts.push({
      id: pluginId(personaUnit.insertIdPrefix, packName),
      name: personaUnit.plugin,
      config: { text: personaText },
    })
  }

  const hooksUnit = registry.units.hooks
  const wantHooks = hasAutomation || Boolean(pack.automation?.hooks?.length) || Boolean(pack.experiences?.length)
  if (wantHooks && hooksUnit?.plugin && hooksUnit.insertIdPrefix) {
    const hooksPath = hasFilePrefix(pack.bundle?.files, 'automation/hooks.json')
      ? './automation/hooks.json'
      : './hooks.json'
    inserts.push({
      id: pluginId(hooksUnit.insertIdPrefix, packName),
      name: hooksUnit.plugin,
      config: { configPath: hooksPath },
    })
  }

  const seenIds = new Set(inserts.map(it => it.id))
  for (const p of pack.dsh?.plugins ?? []) {
    const id = String(p.id || '').trim()
    const pluginName = String(p.name || '').trim()
    if (!id || !pluginName || seenIds.has(id)) continue
    seenIds.add(id)
    const row: InsertItem = { id, name: pluginName }
    if (p.config && Object.keys(p.config).length) row.config = p.config
    if (p.disabled) row.disabled = true
    if (p.inject?.length) row.inject = p.inject
    inserts.push(row)
  }

  const presetMeta = {
    name: pack.dsh?.preset?.name || pack.name || packName,
    description: pack.dsh?.preset?.description || pack.description || '',
  }
  await fs.writeFile(join(dir, 'preset.yml'), yamlStringify(presetMeta), 'utf8')

  for (const m of pack.tools?.mcp ?? []) {
    const serverName = String(m.name || '').trim()
    if (serverName) units.push({ kind: 'mcp', name: serverName, path: '' })
  }
  if (personaText) units.push({ kind: 'persona', name: packName, path: '' })
  for (const row of inserts) {
    units.push({ kind: 'plugin', name: row.id, path: '' })
  }

  const mods: PackModManifest[] = []
  for (const [skillName, body] of skillBodies) {
    const entry = pack.knowledge?.skills?.find(s => String(s.name || '') === skillName)
    const id = String(entry?.id || skillName).trim() || skillName
    const manifest: PackModManifest = {
      schema: PACK_MOD_SCHEMA,
      id,
      kind: 'skill',
      name: skillName,
      publisher: String(entry?.publisher || pack.author || ''),
      version: String(entry?.version || pack.version || ''),
      spec: String(entry?.spec || ''),
      path: `mods/${id}/SKILL.md`,
    }
    await writeShippedMod(dir, manifest, { 'SKILL.md': body })
    mods.push(manifest)
    units.push({ kind: 'mod', name: id, path: manifest.path })
  }
  for (const m of pack.tools?.mcp ?? []) {
    const serverName = String(m.name || '').trim()
    if (!serverName) continue
    const id = String(m.id || serverName).trim() || serverName
    const payload = JSON.stringify({
      name: serverName,
      command: m.command,
      args: m.args,
      url: m.url,
      env: m.env,
    }, null, 2) + '\n'
    const manifest: PackModManifest = {
      schema: PACK_MOD_SCHEMA,
      id,
      kind: 'mcp',
      name: serverName,
      publisher: String(m.publisher || pack.author || ''),
      version: String(m.version || pack.version || ''),
      spec: String(m.spec || ''),
      path: `mods/${id}/mcp.json`,
    }
    await writeShippedMod(dir, manifest, { 'mcp.json': payload })
    mods.push(manifest)
    units.push({ kind: 'mod', name: id, path: manifest.path })
  }
  for (const p of pack.dsh?.plugins ?? []) {
    const id = String(p.id || '').trim()
    const pluginName = String(p.name || '').trim()
    if (!id || !pluginName) continue
    const payload = JSON.stringify({ id, name: pluginName, config: p.config ?? {}, inject: p.inject ?? [] }, null, 2) + '\n'
    const manifest: PackModManifest = {
      schema: PACK_MOD_SCHEMA,
      id,
      kind: 'plugin',
      name: pluginName,
      publisher: String(pack.author || ''),
      version: String(pack.version || ''),
      spec: '',
      path: `mods/${id}/plugin.json`,
    }
    await writeShippedMod(dir, manifest, { 'plugin.json': payload })
    mods.push(manifest)
    units.push({ kind: 'mod', name: id, path: manifest.path })
  }
  for (const extra of extraFileMods) {
    const base = slug(extra.name.replace(/\.(mdc|md|json|yml|yaml)$/i, ''))
    const id = `${extra.kind}-${base}`
    const manifest: PackModManifest = {
      schema: PACK_MOD_SCHEMA,
      id,
      kind: extra.kind,
      name: extra.name,
      publisher: String(pack.author || ''),
      version: String(pack.version || ''),
      spec: '',
      path: `mods/${id}/${extra.fileName}`,
    }
    await writeShippedMod(dir, manifest, { [extra.fileName]: extra.content })
    mods.push(manifest)
    units.push({ kind: 'mod', name: id, path: manifest.path })
  }
  if (mods.length === 0) {
    throw new Error(`pack ${packName} has no mods; a modpack must ship at least one skill, MCP, plugin, rule, command, or hook`)
  }

  const patch = inserts.length ? [{ insert: inserts }] : []
  const patchHeader = `# pack-agent dsh-modpack
# 投影目录；不要 dsh plugin add 本包。放行：packagent dsh allow ${npmName}
`
  await fs.writeFile(join(dir, 'cordis.patch.yml'), patchHeader + yamlStringify(patch), 'utf8')

  const pkg = {
    name: npmName,
    version: pack.version || '0.0.0',
    description: pack.description || `pack-agent modpack ${packName} for DeepSeek Harness`,
    private: true,
    type: 'module',
    files: [
      'cordis.patch.yml',
      'preset.yml',
      'skills',
      'instructions',
      'commands',
      'automation',
      'agents',
      'memory',
      'settings',
      'experiences',
      'mods',
      'INSTALL.md',
      'catalog.json',
    ],
    dsh: { bundle: { patch: './cordis.patch.yml' } },
  }
  await fs.writeFile(join(dir, 'package.json'), JSON.stringify(pkg, null, 2) + '\n', 'utf8')

  await fs.writeFile(
    join(dir, 'catalog.json'),
    JSON.stringify({
      id: npmName,
      name: packName,
      version: pack.version || '0.0.0',
      description: pack.description || '',
      units,
      mods,
    }, null, 2) + '\n',
    'utf8',
  )

  const installCommand = `packagent dsh allow ${npmName}`
  const installMd = `# 投影 · 不要把本包 add 进 DSH 插件栈

本目录是整合包投影（\`dsh.bundle\` 形状），多包并存于 \`.agent-pack/modpacks/\`。
工作区只加载活白名单；没允许的包留在磁盘上，模型看不见。
命名预设：\`packagent dsh set-save <name>\` / \`set-load <name>\`。

宿主只装一次 pack-agent 管理器：

\`\`\`sh
dsh plugin --profile web add <pack-agent 仓库根>
\`\`\`

本包入索引并放行：

\`\`\`sh
packagent dsh index
${installCommand}
\`\`\`

停用（文件不删）：

\`\`\`sh
packagent dsh deny ${npmName}
\`\`\`
`
  await fs.writeFile(join(dir, 'INSTALL.md'), installMd, 'utf8')

  return { dir, npmName, installCommand }
}

export async function compileOursFromRepo(repoRoot: string, outDir: string): Promise<CompileResult> {
  const skillPath = join(repoRoot, 'skills', 'agent-pack', 'SKILL.md')
  const content = await fs.readFile(skillPath, 'utf8')
  return compilePackToDshBundle(
    {
      name: 'packer',
      version: '0.4.0',
      description: 'Agent Modpack packer - export/install portable modpacks',
      knowledge: { skills: [{ name: 'agent-pack', source: 'bundled' }] },
      bundle: { portable: true, files: [{ path: 'skills/agent-pack/SKILL.md', content }] },
    },
    outDir,
  )
}
