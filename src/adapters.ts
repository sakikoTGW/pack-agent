/**
 * Harness 适配表（L1 装备口）—— 一个 pack，按表投射到各家目录。
 *
 * 路径对照参考（社区现成资源）：
 *   - agents-anywhere: github.com/alejandrobailo/agents-anywhere
 *   - AgentSync:       github.com/baranovxyz/agentsync
 *   - agsync:          github.com/yiftahb/agsync
 *   - agentctl:        github.com/iheanyi/agentctl
 *
 * 路径 token：`~/` = 用户主目录；否则相对项目根。
 * verified:true = 官方文档或上述工具交叉核实；false = 待验。
 */
import { promises as fs } from 'node:fs'
import { homedir } from 'node:os'
import { basename, join } from 'node:path'
import JSON5 from 'json5'

export type McpFormat = 'json-mcpServers' | 'json-mcp' | 'json-openclaw' | 'toml-mcp_servers' | 'yaml-mcp_servers'

export type SkillDir = { token: string; nested?: boolean } // nested: <dir>/*/skills/<name>/SKILL.md（AstrBot 插件内置）
export type RuleSrc =
  | { kind: 'file'; token: string; format: string }
  | { kind: 'dir'; token: string; exts: string[]; format: string }
export type McpSrc = { token: string; format: McpFormat }

export type RuntimeAdapter = {
  id: string
  label: string
  verified: boolean
  detect: string[] // 任一存在 → 判定该运行时在场
  skills: SkillDir[]
  rules: RuleSrc[]
  mcp: McpSrc[]
  note?: string
}

export type ScannedResource = { name: string; ref: string; scope: string }
export type ScannedMcp = { name: string; type?: string; command?: string; args?: string[]; url?: string }
export type RuntimeScan = {
  runtime: string
  skills: ScannedResource[]
  rules: (ScannedResource & { format: string })[]
  mcp: ScannedMcp[]
}

export const RUNTIME_ADAPTERS: RuntimeAdapter[] = [
  {
    id: 'cursor',
    label: 'Cursor',
    verified: true,
    detect: ['.cursor', '.cursor/mcp.json'],
    skills: [{ token: '.cursor/skills' }, { token: '~/.cursor/skills' }],
    rules: [
      { kind: 'dir', token: '.cursor/rules', exts: ['.mdc', '.md'], format: 'mdc' },
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
    ],
    mcp: [
      { token: '.cursor/mcp.json', format: 'json-mcpServers' },
      { token: '~/.cursor/mcp.json', format: 'json-mcpServers' },
    ],
    note: 'Cursor IDE：skills/rules/MCP + `.cursor/hooks.json` sessionStart 经验注入（非 rules）',
  },
  {
    id: 'claude-code',
    label: 'Claude Code',
    verified: true,
    detect: ['.claude', 'CLAUDE.md'],
    skills: [{ token: '.claude/skills' }, { token: '~/.claude/skills' }],
    rules: [
      { kind: 'file', token: 'CLAUDE.md', format: 'claude-md' },
      { kind: 'dir', token: '.claude/rules', exts: ['.md'], format: 'claude-md' },
    ],
    mcp: [
      { token: '.mcp.json', format: 'json-mcpServers' },
      { token: '~/.claude.json', format: 'json-mcpServers' },
    ],
  },
  {
    id: 'codex',
    label: 'OpenAI Codex',
    verified: true,
    detect: ['.codex', 'AGENTS.md', '~/.codex/config.toml'],
    skills: [
      { token: '.agents/skills' },
      { token: '.codex/skills' },
      { token: '~/.codex/skills' },
    ],
    rules: [
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
      { kind: 'file', token: '.codex/AGENTS.md', format: 'agents-md' },
      { kind: 'file', token: '~/.codex/AGENTS.md', format: 'agents-md' },
    ],
    mcp: [
      { token: '.codex/config.toml', format: 'toml-mcp_servers' },
      { token: '~/.codex/config.toml', format: 'toml-mcp_servers' },
    ],
    note: 'Codex + AgentSync：项目 .agents/skills 只读；MCP 写 config.toml [mcp_servers.*]',
  },
  {
    id: 'opencode',
    label: 'OpenCode',
    verified: true,
    detect: ['opencode.json', '.opencode', '~/.config/opencode/opencode.json'],
    skills: [
      { token: '.agents/skills' },
      { token: '.opencode/skills' },
      { token: '~/.config/opencode/skills' },
    ],
    rules: [
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
      { kind: 'file', token: '~/.config/opencode/AGENTS.md', format: 'agents-md' },
    ],
    mcp: [
      { token: 'opencode.json', format: 'json-mcp' },
      { token: '~/.config/opencode/opencode.json', format: 'json-mcp' },
    ],
  },
  {
    // 见 docs/HARNESS_RESEARCH.md §3：OpenClaw 配置是 JSON5 ~/.openclaw/openclaw.json，
    // MCP 在 mcp.servers（嵌套），不是顶层 mcpServers —— 现有 json-mcpServers 解析会漏读。
    id: 'openclaw',
    label: 'OpenClaw',
    verified: false,
    detect: ['~/.openclaw/openclaw.json', '~/.openclaw', 'openclaw.json'],
    skills: [
      { token: '.agents/skills' },
      { token: '~/.agents/skills' },
    ],
    rules: [
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
    ],
    mcp: [
      { token: 'openclaw.json', format: 'json-openclaw' },
      { token: '~/.openclaw/openclaw.json', format: 'json-openclaw' },
    ],
    note: 'OpenClaw：skills 从 {workspace}/.agents/skills、~/.agents/skills 加载（已核实，✅）；MCP 在 ~/.openclaw/openclaw.json 的 mcp.servers（JSON5）；瓶口 models.providers.*.baseUrl。见 HARNESS_RESEARCH.md §3。',
  },
  {
    // 据本机源码 E:\hermes-agent-main 核实（HARNESS_RESEARCH.md §4）：
    // skills 分类嵌套 ~/.hermes/skills/<cat>/<name>/SKILL.md，且原生支持 skills.external_dirs；
    // MCP=config.yaml mcp_servers(yaml)；瓶口=model.base_url + model.provider: custom。
    id: 'hermes',
    label: 'Hermes (Nous)',
    verified: true,
    detect: ['~/.hermes/config.yaml', '~/.hermes'],
    skills: [{ token: '~/.hermes/skills' }, { token: '~/.agents/skills' }, { token: '.agents/skills' }],
    rules: [{ kind: 'file', token: 'AGENTS.md', format: 'agents-md' }],
    mcp: [{ token: '~/.hermes/config.yaml', format: 'yaml-mcp_servers' }],
    note: 'Hermes：skills ~/.hermes/skills/<分类>/<名>/SKILL.md（亦支持 config skills.external_dirs 非侵入挂载）；MCP yaml mcp_servers；瓶口 model.base_url + provider: custom。见 HARNESS_RESEARCH.md §4。',
  },
  {
    // 据本机 clone 核实（HARNESS_RESEARCH.md §5）：AstrBot v4 同时有 plugins(star) + skills。
    // skills=data/skills/<名>/SKILL.md（与 Codex/Claude 同 frontmatter）；插件自带 skills 在 <plugin>/skills/。
    // CCui 特殊处理：把整合包包装成插件 data/plugins/ccui-<pack>/（见 §5）。瓶口 provider.api_base。
    id: 'astrbot',
    label: 'AstrBot',
    verified: true,
    detect: ['data/cmd_config.json', 'data/plugins'],
    skills: [{ token: 'data/skills' }, { token: 'data/plugins', nested: true }],
    rules: [],
    mcp: [],
    note: 'AstrBot：L1 插件 skills + MCP 清单；experience 仅 sidecar（无 PersonaManager）',
  },
  {
    id: 'gemini-cli',
    label: 'Gemini CLI',
    verified: false,
    detect: ['.gemini', 'GEMINI.md', '~/.gemini'],
    skills: [{ token: '.gemini/skills' }, { token: '~/.gemini/skills' }, { token: '.agents/skills' }],
    rules: [
      { kind: 'file', token: 'GEMINI.md', format: 'gemini-md' },
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
    ],
    mcp: [
      { token: '.gemini/settings.json', format: 'json-mcpServers' },
      { token: '~/.gemini/settings.json', format: 'json-mcpServers' },
    ],
    note: 'agents-anywhere / agsync：GEMINI.md + .gemini/skills + settings.json mcpServers',
  },
  {
    id: 'windsurf',
    label: 'Windsurf',
    verified: false,
    detect: ['.windsurf', '.windsurf/mcp_config.json'],
    skills: [{ token: '.windsurf/skills' }, { token: '.agents/skills' }],
    rules: [
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
      { kind: 'dir', token: '.windsurf/rules', exts: ['.md'], format: 'agents-md' },
    ],
    mcp: [
      { token: '.windsurf/mcp_config.json', format: 'json-mcpServers' },
    ],
    note: 'agsync：.windsurf/skills + mcp_config.json',
  },
  {
    id: 'github-copilot',
    label: 'GitHub Copilot',
    verified: false,
    detect: ['.github/copilot-instructions.md', '.vscode/mcp.json'],
    skills: [{ token: '.github/skills' }, { token: '.agents/skills' }],
    rules: [
      { kind: 'file', token: '.github/copilot-instructions.md', format: 'copilot-md' },
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
    ],
    mcp: [
      { token: '.vscode/mcp.json', format: 'json-mcpServers' },
      { token: '.mcp.json', format: 'json-mcpServers' },
    ],
    note: 'agents-anywhere / cyncia：copilot-instructions.md + .vscode/mcp.json',
  },
  {
    id: 'generic-agents',
    label: '通用（AGENTS.md）',
    verified: false,
    detect: ['AGENTS.md', '.mcp.json'],
    skills: [{ token: '.agents/skills' }, { token: '.claude/skills' }],
    rules: [
      { kind: 'file', token: 'AGENTS.md', format: 'agents-md' },
      { kind: 'file', token: 'CLAUDE.md', format: 'claude-md' },
    ],
    mcp: [{ token: '.mcp.json', format: 'json-mcpServers' }],
    note: 'openclaw / hermes 等未核实运行时的兜底——只抓 AGENTS.md + 标准 .mcp.json。给出真实安装路径即可升级为专用适配。',
  },
]

function resolveToken(token: string, cwd: string): string {
  if (token.startsWith('~/')) return join(homedir(), token.slice(2))
  return join(cwd, token)
}

async function pathExists(p: string): Promise<boolean> {
  try {
    await fs.access(p)
    return true
  } catch {
    return false
  }
}

async function readSafe(p: string): Promise<string> {
  try {
    return await fs.readFile(p, 'utf8')
  } catch {
    return ''
  }
}

/** 检测当前目录命中哪些运行时（detect 任一路径存在即算在场）。 */
export async function detectRuntimes(cwd: string): Promise<string[]> {
  const hits: string[] = []
  for (const a of RUNTIME_ADAPTERS) {
    for (const token of a.detect) {
      if (await pathExists(resolveToken(token, cwd))) {
        hits.push(a.id)
        break
      }
    }
  }
  return hits
}

export function getAdapter(id: string): RuntimeAdapter | undefined {
  return RUNTIME_ADAPTERS.find(a => a.id === id)
}

/**
 * 某 runtime 在**项目内**的投射目标（第一把刀：把整合包装进该引擎认的目录）。
 * 只取项目内相对 token（不写 ~/ 全局目录，避免污染本机）；缺省回退 .claude/skills。
 * - skillsDir：含 SKILL.md 的目录根（各引擎都认这个形态）
 * - ruleDir：放置 rule 文件的目录（仅目录型 rules；单文件型如 AGENTS.md 不在此投射）
 */
export function runtimeProjectionDirs(runtimeId: string): { skillsDir: string; ruleDir: string } {
  const a = getAdapter(runtimeId)
  const firstLocal = (tokens: string[]): string | undefined => tokens.find(t => !t.startsWith('~/'))
  const skillsDir = firstLocal((a?.skills ?? []).map(s => s.token)) || '.claude/skills'
  const ruleDir =
    firstLocal(
      (a?.rules ?? []).filter((r): r is { kind: 'dir'; token: string; exts: string[]; format: string } => r.kind === 'dir').map(r => r.token),
    ) || '.claude/rules'
  return { skillsDir, ruleDir }
}

async function scanSkillDir(dir: string, scope: string): Promise<ScannedResource[]> {
  const out: ScannedResource[] = []
  let entries: import('node:fs').Dirent[] = []
  try {
    entries = await fs.readdir(dir, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    if (!e.isDirectory()) continue
    const skillMd = join(dir, e.name, 'SKILL.md')
    if (await pathExists(skillMd)) out.push({ name: e.name, ref: skillMd, scope })
  }
  return out
}

/** AstrBot 插件内置：<dir>/<plugin>/skills/<name>/SKILL.md */
async function scanNestedSkills(dir: string, scope: string): Promise<ScannedResource[]> {
  const out: ScannedResource[] = []
  let plugins: import('node:fs').Dirent[] = []
  try {
    plugins = await fs.readdir(dir, { withFileTypes: true })
  } catch {
    return out
  }
  for (const p of plugins) {
    if (!p.isDirectory()) continue
    out.push(...(await scanSkillDir(join(dir, p.name, 'skills'), `${scope}:${p.name}`)))
  }
  return out
}

async function scanRuleSrc(src: RuleSrc, cwd: string): Promise<(ScannedResource & { format: string })[]> {
  if (src.kind === 'file') {
    const p = resolveToken(src.token, cwd)
    if (await pathExists(p)) return [{ name: src.token.split('/').pop() || src.token, ref: p, scope: 'project', format: src.format }]
    return []
  }
  const dir = resolveToken(src.token, cwd)
  const out: (ScannedResource & { format: string })[] = []
  const walk = async (d: string): Promise<void> => {
    let entries: import('node:fs').Dirent[] = []
    try {
      entries = await fs.readdir(d, { withFileTypes: true })
    } catch {
      return
    }
    for (const e of entries) {
      const p = join(d, e.name)
      if (e.isDirectory()) await walk(p)
      else if (src.exts.some(x => e.name.endsWith(x))) out.push({ name: e.name, ref: p, scope: 'project', format: src.format })
    }
  }
  await walk(dir)
  return out
}

/** 极简 TOML 提取：只取 [mcp_servers.NAME] 表的 command/args/url（够列出服务）。 */
function parseTomlMcpServers(text: string): ScannedMcp[] {
  const out: ScannedMcp[] = []
  const lines = text.split(/\r?\n/)
  let cur: ScannedMcp | null = null
  const header = /^\s*\[mcp_servers\.([A-Za-z0-9_-]+)\]\s*$/
  const subHeader = /^\s*\[mcp_servers\.[A-Za-z0-9_-]+\..+\]\s*$/
  const anyHeader = /^\s*\[/
  for (const line of lines) {
    const h = header.exec(line)
    if (h) {
      if (cur) out.push(cur)
      cur = { name: h[1], type: 'stdio' }
      continue
    }
    if (subHeader.test(line)) continue // env 等子表，跳过但不结束当前 server
    if (anyHeader.test(line)) {
      if (cur) { out.push(cur); cur = null }
      continue
    }
    if (!cur) continue
    const cmd = /^\s*command\s*=\s*"([^"]*)"/.exec(line)
    if (cmd) { cur.command = cmd[1]; continue }
    const url = /^\s*url\s*=\s*"([^"]*)"/.exec(line)
    if (url) { cur.url = url[1]; cur.type = 'http'; continue }
    const args = /^\s*args\s*=\s*\[(.*)\]/.exec(line)
    if (args) {
      cur.args = args[1]
        .split(',')
        .map(s => s.trim().replace(/^["']|["']$/g, ''))
        .filter(Boolean)
    }
  }
  if (cur) out.push(cur)
  return out
}

function splitInlineList(s: string): string[] {
  return s
    .split(',')
    .map(x => x.trim().replace(/^["']|["']$/g, ''))
    .filter(Boolean)
}

/** 极简 YAML 提取：只取 mcp_servers: 块下每个 server 的 command/args/url（Hermes 用）。 */
function parseYamlMcpServers(text: string): ScannedMcp[] {
  const lines = text.split(/\r?\n/)
  let i = 0
  let baseIndent = -1
  for (; i < lines.length; i++) {
    const m = /^(\s*)mcp_servers\s*:\s*$/.exec(lines[i])
    if (m) {
      baseIndent = m[1].length
      i++
      break
    }
  }
  if (baseIndent < 0) return []
  const out: ScannedMcp[] = []
  let cur: ScannedMcp | null = null
  let nameIndent = -1
  let inArgs = false
  for (; i < lines.length; i++) {
    const raw = lines[i]
    if (!raw.trim()) continue
    const indent = raw.length - raw.trimStart().length
    if (indent <= baseIndent) break
    const line = raw.trim()
    if (nameIndent < 0) nameIndent = indent
    if (indent === nameIndent && /^[\w-]+\s*:\s*$/.test(line)) {
      if (cur) out.push(cur)
      cur = { name: line.replace(/:\s*$/, ''), type: 'stdio' }
      inArgs = false
      continue
    }
    if (!cur) continue
    const cmd = /^command\s*:\s*"?([^"]*?)"?\s*$/.exec(line)
    if (cmd) { cur.command = cmd[1]; inArgs = false; continue }
    const url = /^url\s*:\s*"?([^"]*?)"?\s*$/.exec(line)
    if (url) { cur.url = url[1]; cur.type = 'http'; inArgs = false; continue }
    const argsInline = /^args\s*:\s*\[(.*)\]\s*$/.exec(line)
    if (argsInline) { cur.args = splitInlineList(argsInline[1]); inArgs = false; continue }
    if (/^args\s*:\s*$/.test(line)) { cur.args = []; inArgs = true; continue }
    if (inArgs) {
      const item = /^-\s*"?([^"]*?)"?\s*$/.exec(line)
      if (item) { cur.args = [...(cur.args || []), item[1]]; continue }
      inArgs = false
    }
  }
  if (cur) out.push(cur)
  return out
}

/** JSON5 兜底：OpenClaw 配置是 JSON5（注释/尾逗号/无引号键），用 json5 解析 */
function parseJsonLoose(text: string): Record<string, unknown> | null {
  try {
    return JSON.parse(text)
  } catch {
    try {
      return JSON5.parse(text)
    } catch {
      return null
    }
  }
}

function parseJsonMcp(text: string, format: McpFormat): ScannedMcp[] {
  const json = parseJsonLoose(text)
  if (!json) return []
  // OpenClaw：mcp.servers（嵌套）；标准：顶层 mcpServers；json-mcp：顶层 mcp
  let servers: Record<string, Record<string, unknown>>
  if (format === 'json-openclaw') {
    servers = ((json.mcp as Record<string, unknown> | undefined)?.servers || {}) as Record<string, Record<string, unknown>>
  } else {
    const key = format === 'json-mcp' ? 'mcp' : 'mcpServers'
    servers = (json[key] || {}) as Record<string, Record<string, unknown>>
  }
  return Object.entries(servers).map(([name, cfg]) => {
    const command = cfg.command
    const cmdStr = Array.isArray(command) ? String(command[0]) : typeof command === 'string' ? command : undefined
    const cmdArgs = Array.isArray(command)
      ? command.slice(1).map(String)
      : Array.isArray(cfg.args)
        ? (cfg.args as unknown[]).map(String)
        : undefined
    return {
      name,
      type: (cfg.type as string) || (cfg.url ? 'http' : 'stdio'),
      command: cmdStr,
      args: cmdArgs,
      url: cfg.url as string | undefined,
    }
  })
}

async function scanMcpSrc(src: McpSrc, cwd: string): Promise<ScannedMcp[]> {
  const p = resolveToken(src.token, cwd)
  const text = await readSafe(p)
  if (!text) return []
  if (src.format === 'toml-mcp_servers') return parseTomlMcpServers(text)
  if (src.format === 'yaml-mcp_servers') return parseYamlMcpServers(text)
  return parseJsonMcp(text, src.format)
}

/** 按某运行时适配器扫描当前目录的 L1 资源。 */
export async function scanRuntime(cwd: string, adapter: RuntimeAdapter): Promise<RuntimeScan> {
  const skills: ScannedResource[] = []
  for (const sd of adapter.skills) {
    const dir = resolveToken(sd.token, cwd)
    skills.push(...(sd.nested ? await scanNestedSkills(dir, adapter.id) : await scanSkillDir(dir, adapter.id)))
  }
  const rules: (ScannedResource & { format: string })[] = []
  for (const r of adapter.rules) rules.push(...(await scanRuleSrc(r, cwd)))
  const mcpAll: ScannedMcp[] = []
  for (const m of adapter.mcp) mcpAll.push(...(await scanMcpSrc(m, cwd)))
  // 去重（同名 MCP 多源合并，先到先得）
  const seen = new Set<string>()
  const mcp = mcpAll.filter(s => (seen.has(s.name) ? false : (seen.add(s.name), true)))
  const dedupeRes = (arr: ScannedResource[]) => {
    const s = new Set<string>()
    return arr.filter(x => (s.has(x.ref) ? false : (s.add(x.ref), true)))
  }
  return {
    runtime: adapter.id,
    skills: dedupeRes(skills),
    rules: rules.filter((x, i, a) => a.findIndex(y => y.ref === x.ref) === i),
    mcp,
  }
}

const UNIVERSAL_IGNORE = new Set([
  'node_modules', '.git', 'dist', 'build', '.next', 'out', 'target', '.cache',
  'coverage', '.turbo', 'site-packages', '.venv', 'venv', '__pycache__', '.ccui', '.agent-pack',
])
const RULE_FILES = new Map<string, string>([
  ['AGENTS.md', 'agents-md'],
  ['AGENTS.override.md', 'agents-md'],
  ['CLAUDE.md', 'claude-md'],
])

/**
 * 通用深度扫描（万能 L1）：不靠预知目录，递归发现任意 harness 的资源面。
 * skills = 含 SKILL.md 的目录；rules = AGENTS.md/CLAUDE.md/*.mdc；
 * MCP = 任意带 mcpServers/mcp 的 json、带 [mcp_servers] 的 toml、带 mcp_servers: 的 yaml。
 */
export async function scanUniversal(cwd: string, maxDepth = 5): Promise<RuntimeScan> {
  const skills: ScannedResource[] = []
  const rules: (ScannedResource & { format: string })[] = []
  const mcp: ScannedMcp[] = []
  const seenMcp = new Set<string>()
  const seenSkill = new Set<string>()

  const addMcp = (servers: ScannedMcp[]) => {
    for (const s of servers) {
      if (!s.name || seenMcp.has(s.name)) continue
      seenMcp.add(s.name)
      mcp.push(s)
    }
  }

  const walk = async (dir: string, depth: number): Promise<void> => {
    if (depth > maxDepth) return
    let entries: import('node:fs').Dirent[] = []
    try {
      entries = await fs.readdir(dir, { withFileTypes: true })
    } catch {
      return
    }
    if (entries.some(e => e.isFile() && e.name === 'SKILL.md')) {
      const ref = join(dir, 'SKILL.md')
      if (!seenSkill.has(ref)) {
        seenSkill.add(ref)
        skills.push({ name: basename(dir), ref, scope: 'universal' })
      }
    }
    for (const e of entries) {
      const p = join(dir, e.name)
      if (e.isDirectory()) {
        if (UNIVERSAL_IGNORE.has(e.name) || e.name.startsWith('.git')) continue
        await walk(p, depth + 1)
        continue
      }
      if (RULE_FILES.has(e.name)) {
        rules.push({ name: e.name, ref: p, scope: 'universal', format: RULE_FILES.get(e.name)! })
      } else if (e.name.endsWith('.mdc')) {
        rules.push({ name: e.name, ref: p, scope: 'universal', format: 'mdc' })
      } else if (e.name.endsWith('.json')) {
        const t = await readSafe(p)
        if (/"mcp(Servers)?"\s*:/.test(t)) {
          addMcp(parseJsonMcp(t, /"mcpServers"\s*:/.test(t) ? 'json-mcpServers' : 'json-mcp'))
        }
      } else if (e.name.endsWith('.toml')) {
        const t = await readSafe(p)
        if (/\[mcp_servers\./.test(t)) addMcp(parseTomlMcpServers(t))
      } else if (e.name.endsWith('.yaml') || e.name.endsWith('.yml')) {
        const t = await readSafe(p)
        if (/^\s*mcp_servers\s*:/m.test(t)) addMcp(parseYamlMcpServers(t))
      }
    }
  }

  await walk(cwd, 0)
  return {
    runtime: 'universal',
    skills,
    rules: rules.filter((x, i, a) => a.findIndex(y => y.ref === x.ref) === i),
    mcp,
  }
}
