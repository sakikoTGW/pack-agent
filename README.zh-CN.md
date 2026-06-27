# Agent Modpack

**[English](README.md)** | **中文**

> **像装 MC 整合包一样，装你的 agent。**

**Agent Modpack** — 像装 MC 整合包一样**导出 / 导入**你的 agent：定义角色、打成 `.pack.json`，用 `packagent install` 装进 Claude Code / Codex 等 harness。

[![npm version](https://img.shields.io/npm/v/@sakikotgw/pack-agent.svg)](https://www.npmjs.com/package/@sakikotgw/pack-agent)
[![license](https://img.shields.io/npm/l/@sakikotgw/pack-agent.svg)](https://github.com/sakikoTGW/pack-agent/blob/main/LICENSE)
[![bun](https://img.shields.io/badge/bun-%3E%3D1.1.0-black?logo=bun)](https://bun.sh)

将**一个 agent**（skills / rules / MCP）打成便携 `.pack.json`，在本机**检测到的 harness** 上安装（默认多家；可用 `--runtime` 只装一家）。

```bash
npm install @sakikotgw/pack-agent
packagent detect          # 先看会装到哪几家
packagent install foo.pack.json --runtime claude-code   # 只装 Claude Code
```

> CLI：`packagent` · npm：`@sakikotgw/pack-agent` · schema：v0.2 · [开发者文档](docs/DEVELOPERS.zh-CN.md)

---

## 为什么

### 三层：Harness · Agent · Prefunction

```
  用户这一轮（input₁）
       │
       ▼
  ┌─ Harness ─────────────────────────────────────┐
  │  Claude Code / OpenClaw / Codex / Hermes …     │  ← 跑 loop、执行 tool、拼请求
  │       │                                        │
  │       ├─ 加载 pack 里的 prefunction            │  ← 整合包封这层
  │       │     skills / rules / MCP / 经验罐头     │
  │       └─ harness 自带装配（各家的 inject 顺序）  │  ← 录瓶口可抓，adapter 投射
  └────────────────────────────────────────────────┘
       │
       ▼
  fixed_input（input₂）  ──►  LLM  ──►  回复
       │
       ▼
  Harness 跑 tool、下一轮…
```

**Harness** = 运行时壳。Claude Code、OpenClaw（龙虾）、Codex、Hermes 在这一层 — 像 MC 的 1.20 / Forge，管目录、注入时机、tool 权限、多轮 loop。  
**Agent** = 在某个 harness 上选定的**角色**：`agents.yaml` 圈定用哪些 skill、rule、MCP。  
**Prefunction** = agent 里**能打进 pack、搬走的那部分**：skills、rules、MCP、经验罐头。它们不动模型权重，只影响 harness 拼出来的 **fixed_input**。

OpenClaw 和 skill 不在一层：OpenClaw **跑** agent；skill **被加载进** OpenClaw，参与拼 prompt。

整合包 = 一个 agent 的 prefunction 快照。`.pack.json` + bundle，MC 整合包列 mods 那种。

发给你：`packagent install`，配 API，在同一 harness、同一模型下，逼近同一套 fixed_input。  
PCL 下整合包 → 导入 → 开玩。

### 加载方式

Prefunction 进 bundle；**怎么挂到 harness 上** 各有一套：

- 目录：`.claude/skills`、`.agents/skills`、`.cursor/rules/*.mdc`…
- 注入：SessionStart、system-reminder、第几轮才追加 skill 正文
- 权限：MCP 白名单、tool 要不要批准

一份 pack 可投射到多家 harness；adapter 负责 **往哪写、何时 inject、谁能调工具**。

两件事：

1. **统一度量衡** — `.pack.json` + bundle，prefunction 可版本、可 eject。
2. **录瓶口** — 抓 harness 最终发给模型的请求体（prompt、tool schema、装配顺序）；装回去用 experience / rules 补 fixed_input。缺录标 L1。

### `packagent` = 启动器

detect harness → 选 pack → install / eject。一份 `.pack.json` 经 adapter 写进各 harness 的装载口。

### export / install

```
  prefunction（skills/rules/MCP/罐头）   （可选）瓶口录制
              └──────── export ──────────┘
                          │
                     .pack.json
                          │
              install ────┴───► adapter → 各 harness 目录
```

---

## 常见场景

| 痛点 | Agent Modpack |
|------|-------------------|
| 换电脑 / 换工具要重配 skill、MCP | 打成**便携 bundle**，拷走 `.pack.json` 即可 install |
| 一个项目里有多个 agent 角色 | `.agent-pack/agents.yaml` 定义边界，`export --agent` 只封一个 |
| 装完不知道卸哪了 | **install-ledger** + `packagent eject --name` 按记录卸载 |
| 只想发部分 skill 给同事 | `pack --skills` / `--manifest` 选件封包 |

---

## 核心概念

```
  agents.yaml          export --agent        .pack.json          install
 ┌─────────────┐      ──────────────►   ┌──────────────┐    ──────────────►  Claude Code
 │ packer      │                        │ bundle 内嵌   │                     Codex
 │ debugger    │                        │ skills/rules  │                     OpenClaw …
 └─────────────┘                        │ mcp + hash    │
       ▲                                └──────────────┘
       │ 一个 harness 里可有多个 agent
       └── 裸 export 无 --agent → 拒绝（agent-required）
```

| 概念 | 含义 |
|------|------|
| **Harness** | 运行时壳：Claude Code、OpenClaw、Codex… 跑 loop、拼请求、管 tool |
| **Agent** | 角色：在某个 harness 上圈定的 skills + rules + MCP（`agents.yaml`） |
| **Prefunction** | agent 里可打包的部分：skills、rules、MCP、经验罐头 |
| **Pack** | 某一个 agent 的 prefunction 快照；`export --agent` 只封一个角色 |
| **Bundle** | pack 内嵌的文件内容；跨机器 install 需要 bundle |

---

## 安装

**需要 [Bun](https://bun.sh) ≥ 1.1.0**（CLI/MCP 为 TypeScript，由 Bun 执行）。

```bash
# 项目内
npm install @sakikotgw/pack-agent

# 全局
npm install -g @sakikotgw/pack-agent

packagent --help
packagent detect
```

---

## 快速开始

### 1. 定义 agent

```bash
packagent agents init    # 生成 .agent-pack/agents.yaml
packagent agents list
```

```yaml
# .agent-pack/agents.yaml
schema: agent-pack/agents/v1

agents:
  my-agent:
    author: you
    description: 展示在 pack 上的简介
    runtime: codex                    # 扫描 skills 的主 harness
    skills: [agent-pack, my-skill]
    rules: [AGENTS.md]
    mcp: [agent-pack]
    captureAs: experience           # skill | experience（默认 experience）
```

### 2. 打包

```bash
packagent export --agent my-agent
# → .agent-pack/exports/my-agent.pack.json
```

其他方式：

```bash
packagent pack --skills brainstorming,verification-before-completion
packagent pack --manifest .agent-pack/select.json
packagent export --all              # legacy：全项目扫描
```

### 3. 安装

```bash
packagent detect
# 输出 Detected / Will install to —— 只装本机 detect 到的 harness

packagent install .agent-pack/exports/my-agent.pack.json

# 只装一家（推荐：明确目标时）
packagent install .agent-pack/exports/my-agent.pack.json --runtime claude-code
packagent install .agent-pack/exports/my-agent.pack.json --runtime codex

# 一条命令：export + install（同样可用 --runtime）
packagent sync --agent my-agent --runtime codex
```

**默认行为**：对本机 **detect 到的** 每个 harness 各投射一份（跳过 `cursor`、`generic-agents`）。  
例如同时有 Claude Code + Codex 配置 → skill 会进 `.claude/skills` **和** `.agents/skills`。  
只想装一家 → **必须加 `--runtime`**。

### 4. 卸载

```bash
packagent eject --name my-agent
```

---

## Claude Code 自举

把下面贴给 Claude Code，或手动配置 MCP：

<details>
<summary><strong>给 Claude 的 setup prompt（点击展开）</strong></summary>

```text
1. npm install @sakikotgw/pack-agent（需要本机已装 Bun）
2. 在 .mcp.json 加入 agent-pack MCP（见下方 JSON）
3. packagent agents init → 扫描项目 skills，帮我填写 agents.yaml
4. packagent sync --agent <id> 完成自举
之后我说「打包 agent X」时，请用 pack_export(agent=X) 或 packagent export --agent X
```

</details>

```json
{
  "mcpServers": {
    "agent-pack": {
      "command": "bun",
      "args": ["node_modules/@sakikotgw/pack-agent/mcp/server.ts"],
      "env": { "AGENT_PACK_CWD": "." }
    }
  }
}
```

完整示例：[mcp/config.example.json](mcp/config.example.json)

### MCP 工具

| 工具 | 作用 |
|------|------|
| `pack_detect` | 检测在场 harness |
| `pack_scan` | 扫描 skills / rules / MCP |
| `pack_export` | 导出 `.pack.json`（支持 `agent` 参数） |
| `pack_install` | 安装已有 pack |
| `pack_sync` | export + install |
| `pack_select` | 选件封包 |
| `pack_eject` | 按 ledger 卸载 |
| `pack_status` | lock / ledger / experiences |

---

## CLI 命令

| 命令 | 说明 |
|------|------|
| `packagent agents list \| init` | 查看 / 初始化 agent 定义 |
| `packagent export --agent <id>` | 导出单个 agent（**推荐**） |
| `packagent pack --skills …` | 选件封包 |
| `packagent install <file>` | 安装 pack |
| `packagent sync --agent <id>` | export + install |
| `packagent detect` | 列出检测到的 harness |
| `packagent eject --name <pack>` | 卸载 |
| `packagent status` | lock / ledger 状态 |
| `packagent diff` | 对比 pack 或 lock |

---

## 支持的 Harness

| id | skills | rules | MCP 配置 |
|----|--------|-------|----------|
| `claude-code` | `.claude/skills` | `CLAUDE.md` | `.mcp.json` |
| `codex` | `.agents/skills` | `AGENTS.md` | `.codex/config.toml` |
| `opencode` | `.opencode/skills` | `AGENTS.md` | `opencode.json` |
| `openclaw` | `.agents/skills` | `AGENTS.md` | `openclaw.json` |
| `hermes` | external_dirs | `AGENTS.md` | `~/.hermes/config.yaml` |
| `gemini-cli` | `.gemini/skills` | `GEMINI.md` | `.gemini/settings.json` |
| `windsurf` | `.windsurf/skills` | — | `.windsurf/mcp_config.json` |
| `github-copilot` | — | `.github/copilot-instructions.md` | `.vscode/mcp.json` |

install 时默认投射到 **`packagent detect` 列出的 Will install to**（本机在场 harness）；可用 **`--runtime <id>`** 只装一家。

---

## 整合包格式（`.pack.json`）

Schema：**v0.2**（`schema` 字段见 [PACK_SPEC.md](docs/PACK_SPEC.md)） · 完整规范 → [docs/PACK_SPEC.md](docs/PACK_SPEC.md)

<details>
<summary><strong>顶层字段一览（点击展开）</strong></summary>

| 字段 | 说明 |
|------|------|
| `schema` | pack schema v0.2 |
| `name` / `version` / `author` / `description` | 包身份 |
| `agent` | `{ id, harness? }` — 对应 agents.yaml 里的 agent |
| `runtime` | 导出时主 harness |
| `knowledge.skills[]` / `rules[]` | L1 清单 + version + contentHash |
| `tools.mcp[]` | MCP server 定义 |
| `experiences[]` | L2 经验罐头（SessionStart 注入） |
| `harness` / `assembly` / `model` | L2–L3 抓包蒸馏（可选） |
| `bundle.files[]` | **便携核心** — 内嵌 skill/rule 全文 |
| `resolution` | packContentHash、agentPackCli 版本锁 |
| `meta.fidelity` | `L1` \| `L2` \| … — 保真度标注 |

</details>

<details>
<summary><strong>保真度分层（点击展开）</strong></summary>

| 层 | 内容 | install 后 |
|----|------|------------|
| **L1** | skills / rules / MCP | 各 harness 原生目录 |
| **L2** | prompt / tool schema / reminders | `captureAs=skill` → rules；`experience` → 经验罐头 + hook |
| **L3+** | 装配顺序 / loop | 写入 pack，尽力投射 |

换模型行为会漂移。pack 标 L1–L4，配置可搬。

</details>

<details>
<summary><strong>项目侧目录（点击展开）</strong></summary>

```
.agent-pack/
  agents.yaml          # agent 定义
  exports/*.pack.json  # 导出产物
  applied/<pack>.json  # 安装清单
  applied/<pack>-ledger.json
  lock.json
  experiences/
  pack.ignore
  project.yaml
```

</details>

<details>
<summary><strong>JSON 骨架示例（点击展开）</strong></summary>

```jsonc
{
  "schema": "ccui-pack/v0.2",
  "name": "my-agent",
  "version": "0.2.0",
  "author": "you",
  "agent": { "id": "my-agent", "harness": "codex" },
  "knowledge": {
    "skills": [{ "name": "agent-pack", "version": "0.2.0", "contentHash": "sha256:…" }],
    "rules": [{ "name": "AGENTS.md", "format": "agents-md" }]
  },
  "tools": { "mcp": [{ "name": "agent-pack", "type": "stdio", "command": "bun" }] },
  "bundle": {
    "portable": true,
    "files": [{ "path": "skills/agent-pack/SKILL.md", "content": "…" }]
  },
  "resolution": { "packContentHash": "sha256:…", "agentPackCli": "0.2.0" },
  "meta": { "fidelity": "L1" }
}
```

</details>

---

## 开发

```bash
git clone https://github.com/sakikoTGW/pack-agent.git
cd pack-agent
bun install
bun test
```

---

## 链接

| | |
|---|---|
| **npm** | https://www.npmjs.com/package/@sakikotgw/pack-agent |
| **Issues** | https://github.com/sakikoTGW/pack-agent/issues |
| **规范** | [docs/PACK_SPEC.md](docs/PACK_SPEC.md) |
| **开发者** | [docs/DEVELOPERS.zh-CN.md](docs/DEVELOPERS.zh-CN.md) · [English](docs/DEVELOPERS.md) |

## License

[MIT](LICENSE)
