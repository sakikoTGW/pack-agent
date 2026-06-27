# Agent Modpack — 开发者文档

**[English](DEVELOPERS.md)** | **中文**

npm：`@sakikotgw/pack-agent` · CLI：`packagent` · Schema：`ccui-pack/v0.2`  
完整 pack 规范 → [PACK_SPEC.md](PACK_SPEC.md)

---

## 5 分钟接入

```bash
npm install @sakikotgw/pack-agent   # 需要 Bun ≥ 1.1.0
```

**CLI（人 / CI）**

```bash
packagent agents init
packagent export --agent my-agent
packagent install .agent-pack/exports/my-agent.pack.json --runtime claude-code
```

**Node / Bun 程序化**

```ts
import {
  exportPackFromProject,
  installPackFile,
  detectRuntimes,
  type PackDoc,
} from '@sakikotgw/pack-agent'

const cwd = process.cwd()

const { pack, outPath } = await exportPackFromProject(cwd, { agent: 'my-agent' })
const report = await installPackFile(cwd, outPath, { runtime: 'codex' })
```

**MCP（agent 内调用）**

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

工具名：`pack_detect` · `pack_scan` · `pack_export` · `pack_install` · `pack_sync` · `pack_select` · `pack_eject` · `pack_status` · `pack_diff` · `pack_experience_offset`

---

## 包入口

| 路径 | 用途 |
|------|------|
| `@sakikotgw/pack-agent` | 程序化 API（`src/index.ts`） |
| `packagent` | CLI（`bin/packagent.js` → `src/cli.ts`） |
| `packagent-mcp` | MCP stdio 服务 |
| `mcp/server.ts` | MCP 实现；`createAgentPackMcpServer()` 可嵌入 |

别名 bin：`agent-pack` / `agent-pack-mcp`（同实现）。

---

## CLI 命令

| 命令 | 作用 |
|------|------|
| `packagent detect` | 列出本机 detect 到的 harness + 支持的 adapter |
| `packagent agents init \| list` | 初始化 / 列出 `.agent-pack/agents.yaml` |
| `packagent export --agent <id>` | 导出 pack（只写文件，不 install） |
| `packagent pack …` | 选件封包；加 `--install` 顺带装 |
| `packagent install <file>` | 安装已有 `.pack.json` |
| `packagent sync …` | export + install 一条链 |
| `packagent eject [--name]` | 按 install-ledger 卸载 |
| `packagent status` | lock / ledger / experiences |
| `packagent diff <a> <b>` | 对比两个 pack 或 lock |

### 常用 flags

| Flag | 适用命令 | 说明 |
|------|----------|------|
| `--agent`, `-a <id>` | export / pack / sync | **推荐**：只封 `agents.yaml` 里一个 agent |
| `--all` | export / sync | 全项目扫描（legacy） |
| `--runtime`, `-t <id>` | install / sync / export | 只投射到一家 harness |
| `--from`, `-f <path>` | sync | 跳过 export，直接 install |
| `--name`, `-n` | export / pack / sync | pack 文件名 |
| `--skills`, `--rules`, `--mcp` | pack | 逗号分隔选件 |
| `--manifest`, `-m` | pack | `select.json` 路径 |
| `--install`, `-i` | pack | 封完即装 |
| `--capture-as skill\|experience` | export / install | L2 交付方式（默认 `experience`） |
| `--harness` | pack | 等同 `--capture-as skill` + 合并抓包 |
| `--on-conflict stop\|skip\|replace` | install / sync | 文件冲突策略（默认 `stop`） |
| `--no-bootstrap` | export / install | 不把 `agent-pack` skill 打进包 |
| `--modules hooks,memory,…` | export / install | 扩展模块开关 |
| `--force` | eject | 强制删冲突 skill 目录 |

### 示例

```bash
# 只装 Claude Code
packagent install team.pack.json --runtime claude-code --on-conflict replace

# 选两个 skill 封包并安装
packagent pack --skills brainstorming,verification-before-completion --install

# 已有 pack，sync 只装
packagent sync --from .agent-pack/exports/packer.pack.json --runtime codex
```

---

## MCP 工具

所有工具返回 JSON（`structuredContent` + text）。`cwd` 默认 `AGENT_PACK_CWD` 或进程 cwd。

### 公共参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `cwd` | string? | 项目根 |
| `runtime` | string? | 目标 harness id |
| `agent` | string? | `agents.yaml` 里的 agent id |
| `all` | boolean? | 全项目扫描 |
| `capture_as` | `skill` \| `experience`? | L2 交付 |
| `no_bootstrap` | boolean? | 跳过 agent-pack 自举 skill |
| `modules` | string[]? | 模块列表 |
| `state_dir` | string? | 默认 `.agent-pack` |
| `on_conflict` | `stop` \| `skip` \| `replace`? | 冲突策略 |

### 工具一览

#### `pack_detect`

```json
{ "cwd": "." }
```

返回：`detected[]`、`installable[]`（去掉 cursor/generic-agents）、`adapters[]`。

#### `pack_scan`

```json
{ "cwd": ".", "runtime": "claude-code" }
```

`runtime` 省略 → 扫描所有 detected harness。`runtime: "universal"` → 深度递归扫描。

#### `pack_export`

```json
{
  "cwd": ".",
  "agent": "my-agent",
  "name": "my-agent",
  "dry_run": false
}
```

`dry_run: true` → 不写文件，返回 stats + skill 名列表。

#### `pack_install`

```json
{
  "cwd": ".",
  "pack_path": ".agent-pack/exports/my-agent.pack.json",
  "runtime": "codex",
  "on_conflict": "stop"
}
```

返回：`InstallReport`（见下）。

#### `pack_sync`

```json
{ "agent": "my-agent", "runtime": "claude-code" }
```

或 `{ "from": "foo.pack.json" }` 只 install。

#### `pack_select`

```json
{
  "skills": ["brainstorming"],
  "rules": ["AGENTS.md"],
  "mcp": ["github"],
  "install": true,
  "with_harness": false
}
```

#### `pack_eject`

```json
{ "pack_name": "my-agent", "force": false }
```

#### `pack_status`

返回 lock、ledger、experiences 索引、MCP bootstrap 路径。

#### `pack_diff`

```json
{ "left": ".agent-pack/lock.json", "right": "other/lock.json" }
```

#### `pack_experience_offset`

调已安装经验罐头的 `weight` / `prompt_delta` / `reminders`。

---

## 程序化 API

从 `@sakikotgw/pack-agent` 导出（TypeScript，Bun 直接跑）。

### Export

```ts
import { exportPackFromProject, buildPackFromProject } from '@sakikotgw/pack-agent'

// 写 .agent-pack/exports/<name>.pack.json
await exportPackFromProject(cwd, {
  agent: 'my-agent',
  runtime: 'codex',
  captureAs: 'experience',
  noBootstrap: false,
  modules: ['skills', 'hooks'],
})

// 只构建，不写盘
const built = await buildPackFromProject(cwd, { agent: 'my-agent' })
// built.pack: PackDoc
```

`ExportOpts` 字段：`runtime` · `name` · `out` · `stateDir` · `select` · `withHarness` · `captureAs` · `noBootstrap` · `modules` · `agent` · `allowFullScan`

### Install

```ts
import { installPackFile, installPack } from '@sakikotgw/pack-agent'

const report = await installPackFile(cwd, packPath, {
  runtime: 'claude-code',
  onConflict: 'replace',
  captureAs: 'experience',
})
// report.ok, report.projected[], report.runtimes[], report.ledgerPath
```

`InstallOpts`：`runtime` · `runtimes` · `stateDir` · `noBootstrap` · `captureAs` · `modules` · `onConflict` · `bootstrapMcp`

### Sync

```ts
import { syncPack } from '@sakikotgw/pack-agent'

await syncPack(cwd, { agent: 'my-agent', runtime: 'codex', from: undefined })
```

### Detect / Scan

```ts
import { detectRuntimes, scanRuntime, getAdapter, RUNTIME_ADAPTERS } from '@sakikotgw/pack-agent'

const ids = await detectRuntimes(cwd)           // ['claude-code', 'codex', ...]
const adapter = getAdapter('claude-code')!
const scan = await scanRuntime(cwd, adapter)    // { skills, rules, mcp }
```

### Agents 注册表

```ts
import {
  loadAgentsRegistry,
  getAgentProfile,
  resolveAgentForExport,
  ensureAgentsYamlTemplate,
} from '@sakikotgw/pack-agent'

const reg = await loadAgentsRegistry(cwd)
const { profile, select } = resolveAgentForExport(reg!, 'my-agent')
```

### Eject / Status

```ts
import { ejectPack, packStatus } from '@sakikotgw/pack-agent'

await ejectPack(cwd, { packName: 'my-agent', force: false })
await packStatus(cwd)
```

### 便携 bundle

```ts
import { embedPortableFiles, materializePortableBundle, readPackFile } from '@sakikotgw/pack-agent'
```

### MCP 嵌入

```ts
import { createAgentPackMcpServer } from '@sakikotgw/pack-agent'
// 同 mcp/server.ts 的 createServer()
```

### 类型

```ts
import type {
  PackDoc,
  PackExperience,
  InstallReport,
  InstallOpts,
  ExportOpts,        // from export.js — 若 TS 未 re-export，从源码路径引
  CaptureDeliver,
  ConflictPolicy,
  AgentProfile,
} from '@sakikotgw/pack-agent'
```

核心结构：`PackDoc`（见 [PACK_SPEC.md](PACK_SPEC.md)）、`InstallReport`、`RuntimeInstallReport`。

---

## `.agent-pack/agents.yaml`

Schema：`agent-pack/agents/v1`

```yaml
schema: agent-pack/agents/v1

agents:
  my-agent:
    author: you
    description: 展示在 pack 上的简介
    packName: optional-export-filename
    runtime: codex              # export 扫描 skills 的主 harness
    skills: [agent-pack, my-skill]   # 或 "*"
    rules: [AGENTS.md]
    mcp: [agent-pack]
    subagents: []
    captureAs: experience       # skill | experience
    modules:
      hooks: false
      memory: false
    bootstrap:
      skills: [agent-pack]
```

`export --agent` / `pack_sync(agent=…)` 必须能解析到这里的 id（除非 `--all` 或显式 `select`）。

---

## `.pack.json` 速查

Schema：`ccui-pack/v0.2`

| 字段 | 说明 |
|------|------|
| `name` / `version` / `author` / `description` | 包身份 |
| `agent` | `{ id, harness? }` |
| `runtime` | `{ id, label, verified }` |
| `knowledge.skills[]` / `rules[]` | L1 清单 + hash |
| `tools.mcp[]` | MCP 定义 |
| `experiences[]` | L2 经验罐头 |
| `harness` / `assembly` / `model` | 瓶口录制（可选） |
| `bundle.files[]` | **跨机器 install 必需** — `{ path, content }` |
| `resolution.packContentHash` | 内容锁 |
| `meta.fidelity` | `L1` \| `L2` \| … |

选件 manifest（`select.json`）：

```json
{
  "name": "dev-pack",
  "skills": ["brainstorming"],
  "rules": ["AGENTS.md"],
  "mcp": ["github"],
  "captureAs": "experience"
}
```

---

## Harness adapter id

| id | 说明 | install 默认 |
|----|------|----------------|
| `claude-code` | `.claude/skills`, `CLAUDE.md`, `.mcp.json` | ✓ |
| `codex` | `.agents/skills`, `AGENTS.md`, `.codex/config.toml` | ✓ |
| `opencode` | `.opencode/skills`, `opencode.json` | ✓ |
| `openclaw` | `.agents/skills`, `openclaw.json` | ✓ |
| `hermes` | external_dirs, `~/.hermes/config.yaml` | ✓ |
| `gemini-cli` | `.gemini/skills` | ✓ |
| `windsurf` | `.windsurf/skills` | ✓ |
| `github-copilot` | copilot-instructions | ✓ |
| `astrbot` | plugin 投射 | ✓ |
| `cursor` | 仅 detect | **跳过** |
| `generic-agents` | 泛化扫描 | **跳过** |

`packagent detect` → `Will install to` 即默认 install 目标。  
`PACK_APPLY_SKIP`：`cursor`, `generic-agents`。

各 adapter 字段见 `src/adapters.ts` 的 `RUNTIME_ADAPTERS`。

---

## 项目状态目录

默认：`.agent-pack/`（`DEFAULT_STATE_DIR`）

```
.agent-pack/
  agents.yaml
  exports/*.pack.json
  applied/<name>.json          # 安装清单
  applied/<name>-ledger.json   # eject 用
  lock.json
  experiences/
  capture/                     # 抓包草稿（合并进 export）
  pack.ignore                  # gitignore 语法
  project.yaml                 # 模块默认开关
```

---

## 冲突与错误

策略：`on_conflict` / CLI `--on-conflict`

| 值 | 行为 |
|----|------|
| `stop` | 遇冲突抛 `PackConflictError`，exit 1 |
| `skip` | 跳过冲突项，继续 |
| `replace` | 覆盖已有 skill / MCP 项 |

MCP 工具冲突返回 `isError: true` + `structuredContent`（含 `kind`、`summary`）。

常见 conflict kind：`skill-exists` · `agent-unknown` · `requires-missing`

---

## 扩展模块

`--modules` / `project.yaml`：

| id | 默认 | 说明 |
|----|------|------|
| `skills` | on | L1 skills |
| `hooks` | off | automation hooks |
| `subagents` | off | subagent 定义 |
| `memory` | off | MEMORY 等 |
| `settings` | off | 权限 / env 片段 |
| `transcripts` | off |  transcript 蒸馏进 experiences |

---

## 相关文件

| 文件 | 内容 |
|------|------|
| [PACK_SPEC.md](PACK_SPEC.md) | pack schema 全文 |
| [../src/index.ts](../src/index.ts) | 公开 export 列表 |
| [../mcp/server.ts](../mcp/server.ts) | MCP 工具注册 |
| [../src/adapters.ts](../src/adapters.ts) | harness 适配表 |
| [../mcp/config.example.json](../mcp/config.example.json) | MCP 配置示例 |

Issues：https://github.com/sakikoTGW/pack-agent/issues
