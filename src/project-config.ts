/**
 * agent-pack 项目级配置 — `.agent-pack/project.yaml` 或 `.ccui/project.yaml` 的 pack 段。
 */
import { promises as fs } from 'node:fs'
import { join } from 'node:path'
import YAML from 'yaml'
import { ensureDefaultPackIgnore } from './pack-ignore.js'
import { PackConflictError, buildFileErrorDetail } from './errors.js'

function isEnoent(e: unknown): boolean {
  return Boolean(e && typeof e === 'object' && (e as NodeJS.ErrnoException).code === 'ENOENT')
}

export type PackProjectConfig = {
  schema?: string
  name?: string
  version?: string
  channel?: 'dev' | 'stable' | 'snapshot'
  packSchema?: string
  /** pack.ignore 相对路径，默认 .agent-pack/pack.ignore */
  ignore?: string
  /** 默认模块开关（打包 & 安装基准） */
  modules?: import('./modules.js').PackModules
  bootstrap?: { skills?: string[] }
  policy?: { captureAs?: 'skill' | 'experience'; knowledgeAs?: 'skill' }
  defaults?: { withHarness?: boolean; noBootstrap?: boolean; captureAs?: 'skill' | 'experience' }
  constraints?: { minAgentPackCli?: string }
}

const DEFAULT: PackProjectConfig = {
  schema: 'agent-pack/project/v1',
  version: '0.3.0',
  channel: 'dev',
  packSchema: 'ccui-pack/v0.2',
  bootstrap: { skills: ['agent-pack'] },
  policy: { captureAs: 'experience', knowledgeAs: 'skill' },
  modules: {
    skills: true,
    rules: true,
    mcp: true,
    experiences: true,
    hooks: false,
    subagents: false,
    memory: false,
    settings: false,
    transcripts: false,
  },
}

/**
 * 文件不存在（ENOENT）→ null，让调用方回退下一个来源 / 内置默认值，这是正常路径。
 * 文件存在但 YAML 解析失败 → 直接抛错，绝不能悄悄回退成默认配置——
 * 那样用户精心配置的 modules/constraints/policy 全部静默失效，且没有任何提示。
 */
async function readYamlFile(path: string): Promise<Record<string, unknown> | null> {
  let text: string
  try {
    text = await fs.readFile(path, 'utf8')
  } catch (e) {
    if (isEnoent(e)) return null
    throw new PackConflictError(
      buildFileErrorDetail({
        kind: 'file-invalid',
        what: 'project.yaml',
        path,
        cause: e as Error,
        help: [`fix the read error at \`${path}\` (permissions? disk?)`],
      }),
    )
  }
  try {
    return YAML.parse(text) as Record<string, unknown>
  } catch (e) {
    throw new PackConflictError(
      buildFileErrorDetail({
        kind: 'file-invalid',
        what: 'project.yaml',
        path,
        cause: e as Error,
        help: [`fix the YAML syntax at \`${path}\``],
      }),
    )
  }
}

export async function loadPackProjectConfig(cwd: string, stateDir = '.agent-pack'): Promise<PackProjectConfig> {
  const agentPackYaml = join(cwd, stateDir, 'project.yaml')
  const ccuiYaml = join(cwd, '.ccui', 'project.yaml')

  const direct = await readYamlFile(agentPackYaml)
  if (direct?.name || direct?.version) {
    return { ...DEFAULT, ...direct } as PackProjectConfig
  }

  const ccui = await readYamlFile(ccuiYaml)
  const packSection = ccui?.pack as PackProjectConfig | undefined
  if (packSection && typeof packSection === 'object') {
    return {
      ...DEFAULT,
      ...packSection,
      name: packSection.name ?? (ccui?.name as string | undefined),
    }
  }

  return { ...DEFAULT, name: basenameSafe(cwd) }
}

function basenameSafe(cwd: string): string {
  const parts = cwd.replace(/\\/g, '/').split('/').filter(Boolean)
  return parts[parts.length - 1] || 'agent-pack'
}

export async function ensureAgentPackProjectYaml(cwd: string, stateDir = '.agent-pack'): Promise<string> {
  const path = join(cwd, stateDir, 'project.yaml')
  try {
    await fs.access(path)
    return path
  } catch {
    /* create */
  }

  const name = basenameSafe(cwd)
  const doc: PackProjectConfig = {
    schema: 'agent-pack/project/v1',
    name,
    version: '0.3.0',
    channel: 'dev',
    packSchema: 'ccui-pack/v0.2',
    bootstrap: { skills: ['agent-pack'] },
    defaults: { withHarness: false, noBootstrap: false },
    constraints: { minAgentPackCli: '0.2.0' },
  }

  await fs.mkdir(join(cwd, stateDir), { recursive: true })
  await ensureDefaultPackIgnore(cwd, stateDir)
  await fs.writeFile(path, YAML.stringify(doc), 'utf8')
  return path
}
