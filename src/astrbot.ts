/**
 * AstrBot 插件生成器 —— 把 ccui 整合包「包装成 AstrBot 插件（star）」挂载上去。
 *
 * 为什么是插件而非散投 skills（见 docs/HARNESS_RESEARCH.md §5）：
 *   - AstrBot v4 的插件可自带 skills（`<plugin>/skills/<name>/SKILL.md` 被原生发现，只读、归属插件）；
 *   - 一个 pack = 一个可整体启停/更新的插件，而不是散落的文件。
 *
 * 生成物 data/plugins/agent_pack_<pack>/：
 *   metadata.yaml   插件元数据（name 必须是合法 Python 标识符 → importlib data.plugins.<name>）
 *   main.py         继承 Star 的类；initialize() 注册「工具端口」（register_llm_tool）
 *   skills/<n>/SKILL.md   整合包 skills（= skills 端口，AstrBot 自动发现）
 *   mcp_servers.json      整合包 MCP 清单（= MCP 端口；可合并进 data/cmd_config.json）
 *   README.md
 */
import { promises as fs } from 'node:fs'
import { basename, dirname, join } from 'node:path'
import type { PackDoc } from './types.js'
import { resolveSkillDir } from './portable.js'

/** 规范成合法 Python 标识符（AstrBot 用 name 作 importlib 模块名） */
function pyIdent(name: string): string {
  let s = String(name || 'pack').replace(/[^A-Za-z0-9_]/g, '_')
  if (!/^[A-Za-z_]/.test(s)) s = `p_${s}`
  return s
}

export type AstrbotPluginResult = {
  dirName: string
  pluginDir: string
  skills: string[]
  mcp: string[]
  files: string[]
}

function metadataYaml(pack: PackDoc, dirName: string): string {
  const desc = `CCui 整合包「${pack.name || dirName}」挂载插件：暴露 skills 与 MCP/工具端口。`
  return [
    `name: ${dirName}`,
    `desc: ${desc}`,
    `version: ${pack.version || '0.1.0'}`,
    `author: CCui`,
    `repo: `,
    '',
  ].join('\n')
}

function mainPy(pack: PackDoc, dirName: string, skills: string[], mcp: string[]): string {
  const packName = (pack.name || dirName).replace(/'/g, '')
  return `import json
import logging
from pathlib import Path

from astrbot.api.star import Context, Star

logger = logging.getLogger("astrbot")

PACK_NAME = ${JSON.stringify(packName)}
SKILLS = ${JSON.stringify(skills)}
MCP_SERVERS = ${JSON.stringify(mcp)}


class CcuiPack_${dirName}(Star):
    """CCui 整合包「${packName}」挂载插件（L1 skills + MCP 清单）。

    - skills：本插件 skills/ 子目录中的 SKILL.md 会被 AstrBot 自动发现。
    - experience：agent-pack 仅写 sidecar；PersonaManager 注入不在支持范围。
    - tools：initialize() 通过 register_llm_tool 暴露一个工具端口。
    - MCP：mcp_servers.json 为本包携带的 MCP 清单；如需接入，合并进 data/cmd_config.json。
    使用 /plugin ${dirName} 查看本帮助。
    """

    def __init__(self, context: Context, config=None) -> None:
        super().__init__(context)
        self._plugin_dir = Path(__file__).resolve().parent

    async def initialize(self) -> None:
        logger.info(
            "[ccui] pack '%s' mounted: %d skills, %d mcp servers",
            PACK_NAME, len(SKILLS), len(MCP_SERVERS),
        )
        try:
            self.context.register_llm_tool(
                name="ccui_pack_info_${dirName}",
                func_args=[],
                desc=("Return info about the mounted CCui integration pack '%s' "
                      "(its skills and declared MCP servers)." % PACK_NAME),
                func_obj=self._ccui_pack_info,
            )
        except Exception as e:  # noqa: BLE001
            logger.warning("[ccui] register tool port failed: %s", e)

    async def _ccui_pack_info(self, event, context, **kwargs) -> str:
        mcp_path = self._plugin_dir / "mcp_servers.json"
        mcp_detail = ""
        try:
            if mcp_path.exists():
                mcp_detail = mcp_path.read_text(encoding="utf-8")
        except Exception:  # noqa: BLE001
            mcp_detail = ""
        return (
            "CCui pack '%s' mounted via AstrBot plugin.\\n"
            "Skills: %s\\n"
            "MCP servers (declared): %s\\n%s"
            % (PACK_NAME, ", ".join(SKILLS) or "-",
               ", ".join(MCP_SERVERS) or "-", mcp_detail)
        )

    async def terminate(self) -> None:
        logger.info("[ccui] pack '%s' unmounted", PACK_NAME)
`
}

function readmeMd(pack: PackDoc, dirName: string, skills: string[], mcp: string[]): string {
  return [
    `# ${dirName}`,
    '',
    `由 CCui 自动生成的 AstrBot 插件，挂载整合包 **${pack.name || dirName}**。`,
    '',
    '## 端口',
    `- **skills**（${skills.length}）：\`skills/<name>/SKILL.md\`，AstrBot 启动自动发现。`,
    `- **experience**：agent-pack 仅 sidecar（\`.agent-pack/harness/astrbot/\`）；Persona DB 注入不支持。`,
    `- **tools**：插件 \`initialize()\` 注册 \`ccui_pack_info_${dirName}\` 函数工具。`,
    `- **MCP**（${mcp.length}）：见 \`mcp_servers.json\`；如需接入，把其中条目合并进 \`data/cmd_config.json\`。`,
    '',
    '## 安装',
    '把本目录放进 AstrBot 的 `data/plugins/`，重启或在 WebUI 重载插件即可。',
    '',
  ].join('\n')
}

/** 收集 pack 的 skills 文件（便携包内嵌优先，否则按 ref 从本机解析） */
async function collectSkillFiles(
  cwd: string,
  pack: PackDoc,
): Promise<{ skillNames: string[]; files: Array<{ rel: string; content: string }> }> {
  const files: Array<{ rel: string; content: string }> = []
  const skillNames: string[] = []

  const bundleFiles = pack.bundle?.files
  if (bundleFiles?.length) {
    for (const f of bundleFiles) {
      const m = f.path.match(/^skills\/([^/]+)\/(.+)$/)
      if (!m) continue
      const [, name, rest] = m
      if (!skillNames.includes(name)) skillNames.push(name)
      files.push({ rel: `skills/${name}/${rest}`, content: f.content })
    }
    return { skillNames, files }
  }

  // 非便携：按 ref 从本机目录读取整棵 skill 目录
  for (const s of pack.knowledge?.skills ?? []) {
    const name = String(s.name || basename(String(s.ref || '')))
    const dir = await resolveSkillDir(cwd, name, String(s.ref || ''), null)
    if (!dir) continue
    skillNames.push(name)
    for (const rel of await walk(dir)) {
      files.push({ rel: `skills/${name}/${rel}`, content: await fs.readFile(join(dir, rel), 'utf8') })
    }
  }
  return { skillNames, files }
}

async function walk(root: string, base = root): Promise<string[]> {
  const out: string[] = []
  let entries: import('node:fs').Dirent[] = []
  try {
    entries = await fs.readdir(root, { withFileTypes: true })
  } catch {
    return out
  }
  for (const e of entries) {
    const abs = join(root, e.name)
    if (e.isDirectory()) out.push(...(await walk(abs, base)))
    else out.push(abs.slice(base.length + 1).replace(/\\/g, '/'))
  }
  return out
}

/** 生成 AstrBot 插件目录；destPluginsDir 应为 AstrBot 的 data/plugins（或暂存区）。 */
export async function writeAstrbotPlugin(
  cwd: string,
  pack: PackDoc,
  destPluginsDir: string,
): Promise<AstrbotPluginResult> {
  const dirName = `agent_pack_${pyIdent(pack.name || 'pack')}`
  const pluginDir = join(destPluginsDir, dirName)
  await fs.rm(pluginDir, { recursive: true, force: true })
  await fs.mkdir(pluginDir, { recursive: true })

  const { skillNames, files: skillFiles } = await collectSkillFiles(cwd, pack)
  const mcp = (pack.tools?.mcp ?? []).map(m => String(m.name || '')).filter(Boolean)

  const written: string[] = []
  const writeFile = async (rel: string, content: string) => {
    const abs = join(pluginDir, rel.replace(/\//g, '\\'))
    await fs.mkdir(dirname(abs), { recursive: true })
    await fs.writeFile(abs, content, 'utf8')
    written.push(rel)
  }

  await writeFile('metadata.yaml', metadataYaml(pack, dirName))
  await writeFile('main.py', mainPy(pack, dirName, skillNames, mcp))
  await writeFile('README.md', readmeMd(pack, dirName, skillNames, mcp))
  if (mcp.length) {
    const servers: Record<string, unknown> = {}
    for (const m of pack.tools?.mcp ?? []) {
      const n = String(m.name || '')
      if (!n) continue
      const cfg: Record<string, unknown> = {}
      if (m.url) cfg.url = m.url
      else if (m.command) { cfg.command = m.command; if (m.args) cfg.args = m.args }
      if (m.env) cfg.env = m.env
      servers[n] = cfg
    }
    await writeFile('mcp_servers.json', JSON.stringify({ mcpServers: servers }, null, 2))
  }
  for (const f of skillFiles) await writeFile(f.rel, f.content)

  return { dirName, pluginDir, skills: skillNames, mcp, files: written }
}
