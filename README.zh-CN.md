# pack-agent

**[English](README.md)** | **中文**

**Agent 整合包（modpack）— 定义一个 agent，打成便携包，装到任意 harness。**

[![npm version](https://img.shields.io/npm/v/@sakikotgw/pack-agent.svg)](https://www.npmjs.com/package/@sakikotgw/pack-agent)
[![license](https://img.shields.io/npm/l/@sakikotgw/pack-agent.svg)](https://github.com/sakikoTGW/pack-agent/blob/main/LICENSE)
[![bun](https://img.shields.io/badge/bun-%3E%3D1.1.0-black?logo=bun)](https://bun.sh)

将**一个 agent**（skills / rules / MCP）打成便携 `.pack.json`，在本机**检测到的 harness** 上安装（默认多家；可用 `--runtime` 只装一家）。

```bash
npm install @sakikotgw/pack-agent
packagent detect          # 先看会装到哪几家
packagent install foo.pack.json --runtime claude-code   # 只装 Claude Code
```

> CLI：`packagent` · 别名：`agent-pack` · schema：`ccui-pack/v0.2`

---

## 为什么需要这个

| 痛点 | pack-agent 怎么做 |
|------|-------------------|
| 换电脑 / 换工具要重配 skill、MCP | 打成**便携 bundle**，拷走 `.pack.json` 即可 install |
| 一个项目里有多个 agent 角色 | `.agent-pack/agents.yaml` 定义边界，`export --agent` 只封一个 |
| 装完不知道卸哪了 | **install-ledger** + `packagent eject --name` 按记录卸载 |
| 只想发部分 skill 给同事 | `pack --skills` / `--manifest` 选件封包 |

类比 Minecraft：**harness = 游戏版本**，**pack = 整合包**，**packagent = 启动器**。

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
| **Harness** | 容器：Claude Code、Codex、Cursor… 决定 skill/MCP 放哪 |
| **Agent** | 角色：一组 skills + rules + MCP（在 `agents.yaml` 里定义） |
| **Pack** | 某一个 agent 的快照（**不是**整仓 skill dump） |
| **Bundle** | pack 内嵌的文件内容 — 没 bundle 不能跨机器装 |

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
# 输出 Detected / Will install to —— 只有「在场」的 harness 才会装（不是全世界所有工具）

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

Schema：**`ccui-pack/v0.2`** · 完整规范 → [docs/PACK_SPEC.md](docs/PACK_SPEC.md)

<details>
<summary><strong>顶层字段一览（点击展开）</strong></summary>

| 字段 | 说明 |
|------|------|
| `schema` | `ccui-pack/v0.2` |
| `name` / `version` / `author` / `description` | 包身份 |
| `agent` | `{ id, harness? }` — 对应 agents.yaml 里的 agent |
| `runtime` | 导出时主 harness |
| `knowledge.skills[]` / `rules[]` | L1 清单 + version + contentHash |
| `tools.mcp[]` | MCP server 定义 |
| `experiences[]` | L2 经验罐头（SessionStart 注入） |
| `harness` / `assembly` / `model` | L2–L3 抓包蒸馏（可选） |
| `bundle.files[]` | **便携核心** — 内嵌 skill/rule 全文 |
| `resolution` | packContentHash、agentPackCli 版本锁 |
| `meta.fidelity` | `L1` \| `L2` \| … — 诚实标注保真度 |

</details>

<details>
<summary><strong>保真度分层（点击展开）</strong></summary>

| 层 | 内容 | install 后 |
|----|------|------------|
| **L1** | skills / rules / MCP | 各 harness 原生目录 |
| **L2** | prompt / tool schema / reminders | `captureAs=skill` → rules；`experience` → 经验罐头 + hook |
| **L3+** | 装配顺序 / loop | 写入 pack，尽力投射 |

换模型行为会漂移 — pack 保证**配置可搬**，不承诺完美克隆闭源 harness。

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

Monorepo 上游：[sakikoTGW/CCui](https://github.com/sakikoTGW/CCui) `packages/pack-cli`

---

## 链接

| | |
|---|---|
| **npm** | https://www.npmjs.com/package/@sakikotgw/pack-agent |
| **Issues** | https://github.com/sakikoTGW/pack-agent/issues |
| **规范** | [docs/PACK_SPEC.md](docs/PACK_SPEC.md) |

## License

[MIT](LICENSE)
