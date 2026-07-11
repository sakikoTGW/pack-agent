# Pack 规范（`.pack.json` 格式）

> **Schema 字段值**：`ccui-pack/v0.2`（export 默认，读取兼容 v0.1）  
> **工具**：Agent Modpack / `packagent`

把一个 agent 的 prefunction（skills、rules、MCP，及可选瓶口录制）序列化成 `.pack.json`，经 adapter 投射到各 harness。

---

## 概述

| 概念 | 说明 |
|------|------|
| **Harness** | Claude Code、OpenClaw、Codex 等 — 跑 loop、拼请求、管 tool |
| **Agent** | `agents.yaml` 里圈定的角色（skill / rule / MCP 清单） |
| **Pack** | 某一 agent 的快照文件（`.pack.json` + 内嵌 `bundle`） |
| **packagent** | detect → export / install → ledger / eject |

Prefunction 进 pack；harness 决定怎么加载。详见 [README](../README.zh-CN.md#为什么)。

---

## 0. 心智模型

| Minecraft | Agent Modpack |
|-----------|---------------|
| 游戏版本（1.20 / Forge） | Harness（Claude Code、Codex、OpenClaw…） |
| 整合包（mods） | `.pack.json`：skills + rules + MCP + 可选瓶口段 |
| 启动器（PCL） | `packagent` |

**L1** — 磁盘上的 skills / rules / MCP，export 进 `knowledge` + `bundle.files`。  
**L2–L4** — prompt、tool schema、装配顺序、loop；多在 harness 内部，来源是发给模型的请求体（瓶口 capture）。

---

## 1. 保真度阶梯

分层标注，写入 `meta.fidelity`。

| 层 | 内容 | 来源 | 能否 100% |
|----|------|------|-----------|
| **L1** 配置 | skills / rules / MCP | 文件 / bundle | ✅ |
| **L2** 脚手架 | base prompt + tool schema + reminders | 瓶口 capture | ✅（同模型时） |
| **L3** 装配 | 上下文顺序 / 包裹格式 / 注入时机 | 多轮 capture | 接近 |
| **L4** 循环 | loop / hooks / subagent | capture + 重建 | 部分 |
| **L5** 模型 | 权重 | — | ❌ 仅记参数 |

换模型时行为会漂移。同 harness、同模型下 L1–L4 尽量逼近；缺 capture 标 L1。

---

## 2. Schema

```jsonc
{
  "schema": "ccui-pack/v0.1",
  "name": "string",                  // 整合包名
  "version": "string",

  // L1 — skills / rules / MCP
  "knowledge": {
    "skills": [ { "name": "string", "source": "path|git|registry", "ref": "string" } ],
    "rules":  [ { "name": "string", "format": "mdc|claude-md|agents-md", "ref": "string" } ]
  },
  "tools": {
    "mcp": [ { "name": "string", "type": "stdio|sse|http", "command?": "string", "args?": [], "url?": "string", "env?": {} } ],
    "builtin_map": [ { "name": "string", "mapTo": "string" } ]   // harness 内置 tool → MCP tool 映射
  },

  // L2–L4 — 瓶口 capture（可选）
  "harness": {
    "base_system_prompt": "string",                 // 引擎自带系统提示（含其注入的一切文本）
    "tool_schemas": [ { "name": "string", "description": "string", "input_schema": {} } ],
    "system_reminders": [ "string" ]                // 工具旁/中途注入的提醒块
  },
  "assembly": {
    "wire_format": "anthropic|openai",
    "system_is_array": false,                       // 是否用数组 system（缓存断点）
    "cache_breakpoints": 0,
    "file_wrapper": "string|null",                  // 文件包裹标签，如 <file path=...>
    "message_count": 0,
    "order_hint": [ "system", "rules", "files", "history" ]
  },
  "model": {
    "name": "string",
    "params": { "max_tokens": 0, "temperature": 0, "top_p": 0, "top_k": 0, "stop_sequences": [] }
  },
  "loop": {
    "maxTurns": null,                               // 单次抓包推不出，需多轮
    "planning": null,
    "subagents": null,
    "hooks": []
  },

  "meta": {
    "capturedAt": "ISO8601",
    "source": "wire|filesystem|manual",
    "capturedFrom": "string|null",                  // 抓自哪个 agent/版本
    "sameModel": null,                              // 回放是否同模型
    "fidelity": "L1|L2|L3|L4"
  }
}
```

---

## 3. 三个动词

```
抓(Capture)  瓶口录请求体 → harness / assembly / model
封(Package)  归一化进 schema，写入 .pack.json
装(Install)  packagent → adapter 写各 harness 目录；experience 走 hook 注入
```

---

## 4. 边界与约定

- **隔离**：install 改 harness 共享目录时写 ledger（`.agent-pack/applied/*-ledger.json`），`eject` 按记录卸载。
- **合法性**：pack 含抓包 prompt 时注意来源方 IP / ToS；对外分享前自行确认。
- **保真度**：`meta.fidelity` 与 pack 内容一致；换模型预期行为漂移。

---

## 5. v0.2 扩展（版本 · lock · 项目结构）

v0.1 只有 `name` + 扁平列表；v0.2 为**可复现、可比对**的 modpack：

### 5.1 Pack 级

| 字段 | 含义 |
|------|------|
| `schema` | `ccui-pack/v0.2` |
| `version` | 整合包 semver（来自 `.agent-pack/project.yaml`） |
| `channel` | `dev` / `stable` / `snapshot` |
| `resolution` | 封包时刻快照：`lockedAt`、`packContentHash`、`agentPackCli` |

### 5.2 组件级（每个 skill / rule / MCP）

**Skill**（`knowledge.skills[]`）：

| 字段 | 含义 |
|------|------|
| `version` | SKILL.md frontmatter 的 `version:`；无则 `0.0.0+<hash12>` |
| `contentHash` | 整个 skill 目录 `sha256:…` |
| `fileCount` | 文件数 |
| `license` / `description` | 来自 frontmatter（可选） |

**Rule**：`version` + `contentHash`（文件内容 hash）

**MCP**（`tools.mcp[]`）：

| 字段 | 含义 |
|------|------|
| `version` | 已安装包版本或 `0.0.0` |
| `package` | 从 `npx @scope/pkg@1.2.3` 解析 |
| `packageVersion` | 约束或 `node_modules` 实测 |
| `configHash` | command+args+env 规范 hash |

### 5.3 项目目录（`.agent-pack/`）

```
.agent-pack/
  project.yaml    # 包名/版本/channel/自举 skill 列表
  lock.json       # 解析后的组件版本锁（类似 package-lock）
  exports/        # *.pack.json
  applied/        # 安装清单 + L2 sidecar
  experiences/    # 经验罐头（capture-as experience）
  capture/        # L2 抓包草稿
```

`project.yaml` 示例见仓库 `.agent-pack/project.yaml`。

### 5.5 经验罐头 vs Skill 约束（交付模式）

| | Skill 约束 | 经验罐头 |
|---|------------|----------|
| 适用 | L1 SKILL.md、用户要持久规则 | 抓包蒸馏 L2–L4 |
| pack 字段 | `knowledge.skills` + `harness` | `experiences[]` |
| 安装路径 | `.claude/skills`、rules/AGENTS.md | `.agent-pack/experiences/*.exp.json` |
| CLI | `--capture-as skill` | `--capture-as experience`（默认） |

经验罐头带 `offset` 字段，使用中可微调，**不回写 skill 树**。安装时 `projectExperienceToHarnesses` 按 harness 适配表接 SessionStart / pre_llm hook（Claude / Codex / Gemini / Hermes / OpenClaw 等）。

### 5.7 可选模块 + pack.ignore

**模块**（`project.yaml` → `modules:`，CLI `--modules hooks,memory` 或 `--no-memory`）：

| 模块 | 默认 | 含义 |
|------|------|------|
| `skills` / `rules` / `mcp` / `experiences` | 开 | L1 + 经验罐头 |
| `hooks` / `subagents` / `memory` / `settings` / `transcripts` | 关 | 养久了的环境沉积 |

**pack.ignore**（`.agent-pack/pack.ignore`，gitignore 语法）：

- `#` 注释；`**` / `*` 通配；`!pattern` 反选
- 默认排除 `.env`、`node_modules/**`、`.agent-pack/capture/**`、`MEMORY.md` 等
- 要打包 memory：在 `modules.memory: true` 且从 ignore 中删掉对应行

### 5.8 卸载（eject）与 install-ledger

每次 **install 成功** 会写 `.agent-pack/applied/<pack>-ledger.json`，记录：

- 投射的 skill 目录（含 `.agent-pack-origin.json` 归属标记）
- MCP 合并项、harness L2、experience hook、AstrBot 插件等

**`agent-pack eject`** / MCP **`pack_eject`**：

| 状态 | 含义 |
|------|------|
| `removed` | 已删 |
| `missing` | ledger 有记录但磁盘已不存在（手动删过）→ **不算失败**，继续卸其余项 |
| `conflict` | 无 origin 标记或归属别的包 → 默认跳过，``--force`` 强删 |
| `partial` | 权限/占用导致删不干净 |

少件时看 `report.remediation`。

### 5.9 便携 bundle（跨机器 install）

跨机器 install 需要 `bundle.files` 内嵌 skill/rule 全文（export 时 `embedPortableFiles`）。

1. export / sync 默认 embed → `bundle.files`
2. 或把依赖 skill 一并选进 pack（`requires[]` 认：包内 bundled / 本机已有 / 目标机已装）
3. 仅 `ref`、无 embed → 换机器 install 会失败，需 re-export

`requires[]` 安装前校验；`--force-requires` / MCP `force_requires` 可跳过。

### 5.10 MCP server（agent 主入口）

| 工具 | 作用 |
|------|------|
| `pack_detect` / `pack_scan` / `pack_export` / `pack_install` / `pack_sync` / `pack_select` / `pack_diff` | 打包安装 |
| `pack_eject` | 按 ledger 卸载 |
| `pack_status` | lock + ledger + experiences |
| `pack_experience_offset` | 微调经验罐头 offset |

install 默认 bootstrap `agent-pack` 进 `.mcp.json` + `.cursor/mcp.json`（``--no-bootstrap-mcp`` 可关，待 CLI flag）。

### 5.6 向后兼容

- 读取仍接受 `ccui-pack/v0.1`；export/sync 默认写 v0.2
- 旧 pack 安装时会补全 `resolution` 并写 `lock.json`
