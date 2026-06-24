# @sakikotgw/pack-agent

Pack **one agent** (skills / rules / MCP) into a portable `.pack.json`, then install to every harness on the machine (Claude Code, Codex, OpenClaw, Hermes, …).

```bash
npm install @sakikotgw/pack-agent
packagent detect   # alias: agent-pack
```

## Requirements

- **[Bun](https://bun.sh) ≥ 1.1.0** — CLI/MCP bins are TypeScript executed by Bun.

## Install

```bash
npm install @sakikotgw/pack-agent
# or global
npm install -g @sakikotgw/pack-agent
```

## Quick start

```bash
packagent agents init
packagent export --agent packer      # needs .agent-pack/agents.yaml
packagent install .agent-pack/exports/packer.pack.json
packagent sync --agent packer         # export + install
```

Selective: `packagent pack --skills brainstorming` · Legacy full scan: `packagent export --all`

## Claude Code self-setup

```text
1. npm install @sakikotgw/pack-agent
2. MCP in .mcp.json: bun + node_modules/@sakikotgw/pack-agent/mcp/server.ts, AGENT_PACK_CWD="."
3. packagent agents init → edit agents.yaml → packagent sync --agent <id>
```

---

## 整合包结构（`.pack.json`）

Schema 当前版本：**`ccui-pack/v0.2`**。一个 pack = **某一个 agent 的快照**（不是整仓 skill dump）。

### 顶层字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `schema` | string | 固定 `ccui-pack/v0.2` |
| `name` | string | 整合包名（默认 = agent id） |
| `version` | string | 包版本 semver |
| `author` | string? | 作者 / 团队 |
| `description` | string? | 展示用简介 |
| `agent` | `{ id, harness? }` | 本 pack 对应的 agent 定义 |
| `runtime` | `{ id, label, verified }` | 扫描/导出时的主 harness |
| `policy` | `{ captureAs, knowledgeAs }` | `captureAs`: `skill` \| `experience`（默认 experience） |
| `modules` | object | 纳入了哪些可选模块（hooks/memory/…） |
| `knowledge.skills[]` | array | L1 skill 清单（name, ref, version, contentHash） |
| `knowledge.rules[]` | array | L1 规则（AGENTS.md / CLAUDE.md / .mdc） |
| `tools.mcp[]` | array | MCP server 配置（stdio/http, command, args, url） |
| `experiences[]` | array? | L2 经验罐头（抓包/蒸馏，SessionStart 注入） |
| `harness` | object? | L2 脚手架：base_system_prompt, tool_schemas, reminders |
| `assembly` | object? | L3 装配 hint（wire_format, order_hint, …） |
| `model` | object? | 源模型名与参数快照 |
| `automation.hooks[]` | array? | 可选：hooks 模块 |
| `agents.subagents[]` | array? | 可选：subagent 定义 |
| `memory.files[]` | array? | 可选：MEMORY.md 等 |
| `bundle` | `{ portable, files[] }` | **便携核心**：内嵌文件内容，可拷到任意目录 install |
| `resolution` | object | 锁定：packContentHash, agentPackCli, skill/mcp 计数 |
| `meta` | object | exportedAt, fidelity, detectedRuntimes, bootstrapSkills |

### 保真度分层

| 层 | 内容 | 来源 | install 后去哪 |
|----|------|------|----------------|
| **L1** | skills / rules / MCP | 文件扫描 | `.claude/skills`, `.agents/skills`, `.mcp.json`, `.codex/config.toml` … |
| **L2** | system prompt / tool schema / reminders | 抓包或 capture 草稿 | `captureAs=skill` → rules；`experience` → `.agent-pack/experiences/` + SessionStart hook |
| **L3** | 上下文装配顺序 | 抓包差分 | 写入 pack + sidecar（尽力） |
| **L4** | loop / hooks / subagent | 配置快照 | 因 harness 而异 |

`meta.fidelity`: `L1` | `L2` | … — 诚实标注，不臆造 L2。

### 便携 bundle 内路径（`bundle.files[]`）

| path 前缀 | 内容 |
|-----------|------|
| `skills/<name>/SKILL.md` | skill 正文 + 同目录附属文件 |
| `skills/<name>/…` | scripts、references 等 |
| `rules/<name>` | 规则文件全文 |
| `agents/<name>.md` | subagent 定义（modules.subagents） |
| `hooks/<runtime>.json` | hook 片段 |
| `memory/<name>` | MEMORY 等 |
| `settings/<key>.json` | settings 片段 |
| `transcripts/<id>.jsonl` | 可选：transcript 蒸馏源 |

install 时从 bundle 解压到各 harness 适配路径；**没 bundle 的 pack 不能跨机器装**。

### 项目侧目录（install 后）

| 路径 | 说明 |
|------|------|
| `.agent-pack/agents.yaml` | agent 定义（export 必填 `--agent`） |
| `.agent-pack/exports/*.pack.json` | export 产物 |
| `.agent-pack/applied/<pack>.json` | 安装清单（投射到哪些 runtime） |
| `.agent-pack/applied/<pack>-ledger.json` | 卸载 ledger（eject 用） |
| `.agent-pack/lock.json` | 导出锁（contentHash / 版本） |
| `.agent-pack/experiences/*.exp.json` | 经验罐头 |
| `.agent-pack/pack.ignore` | 打包排除（gitignore 语法） |
| `.agent-pack/project.yaml` | 项目默认 modules / policy |

### Agent 定义（`agents.yaml`）

```yaml
schema: agent-pack/agents/v1
agents:
  packer:
    author: you
    description: Short intro on exported pack
    runtime: codex              # 扫描 skills 的主 harness
    skills: [agent-pack, my-skill]
    rules: [AGENTS.md]
    mcp: [agent-pack]
    captureAs: experience       # skill | experience
```

裸 `export` / `pack` 无 `--agent`、`--manifest`、`--skills`、`--all` → **拒绝**（`agent-required`）。

### 示例 JSON 骨架

```jsonc
{
  "schema": "ccui-pack/v0.2",
  "name": "packer",
  "version": "0.2.0",
  "author": "you",
  "description": "My agent snapshot",
  "agent": { "id": "packer", "harness": "codex" },
  "runtime": { "id": "codex", "label": "OpenAI Codex", "verified": true },
  "knowledge": {
    "skills": [{ "name": "agent-pack", "source": "path", "ref": ".agents/skills/agent-pack", "version": "0.2.0" }],
    "rules": [{ "name": "AGENTS.md", "format": "agents-md", "ref": "AGENTS.md" }]
  },
  "tools": { "mcp": [{ "name": "agent-pack", "type": "stdio", "command": "bun", "args": ["…/mcp/server.ts"] }] },
  "policy": { "captureAs": "experience", "knowledgeAs": "skill" },
  "bundle": {
    "portable": true,
    "files": [{ "path": "skills/agent-pack/SKILL.md", "content": "---\nname: agent-pack\n…" }]
  },
  "resolution": { "packContentHash": "sha256:…", "agentPackCli": "0.2.0" },
  "meta": { "fidelity": "L1", "detectedRuntimes": ["codex", "claude-code"] }
}
```

---

## MCP tools

`pack_detect` · `pack_scan` · `pack_export` · `pack_install` · `pack_sync` · `pack_select` · `pack_eject` · `pack_status`

Config: `mcp/config.example.json`

## Supported harnesses

| id | skills | rules | MCP |
|----|--------|-------|-----|
| claude-code | `.claude/skills` | `CLAUDE.md` | `.mcp.json` |
| codex | `.agents/skills` | `AGENTS.md` | `.codex/config.toml` |
| opencode | `.opencode/skills` | `AGENTS.md` | `opencode.json` |
| openclaw | `.agents/skills` | `AGENTS.md` | `openclaw.json` |
| hermes | external_dirs | `AGENTS.md` | `~/.hermes/config.yaml` |

## Uninstall

```bash
packagent eject --name packer
```

## Links

- npm: https://www.npmjs.com/package/@sakikotgw/pack-agent
- Spec detail: [docs/PACK_SPEC.md](docs/PACK_SPEC.md)
