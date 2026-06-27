---
name: agent-pack
version: 0.2.0
description: Pack and install agent configs as portable modpacks. Use when the user wants to pack/export/sync skills, rules, MCP, or harness prompts; compose a partial pack from selection; or install a pack to all detected harnesses (Claude Code, Codex, etc.). Triggers include 打包、整合包、装包、sync、export pack、复刻 agent、把 prompt 打进包.
---
# Agent Modpack — 像装 MC 整合包一样，装你的 agent

```
  Harness（Claude Code / OpenClaw / Codex …）跑 loop
       └─ 加载 prefunction：skills / rules / MCP / 经验罐头
            └─ 拼成 fixed_input → LLM
```

**Harness** = 运行时壳。**Prefunction** = pack 里可搬运的配置（skill、rule、MCP…）。整合包封 prefunction，adapter 挂到各 harness。  
**选 agent → 封包 → install（默认本机 detect 到的 harness；`--runtime` 只装一家）。**

## 你要帮用户做的三件事

| 用户意图 | 你怎么做 |
|----------|----------|
| **全量自动**（当前项目一切打进包并安装） | 跑 `agent-pack sync` |
| **选择性打包**（只要某几个 skill / 某条 rule / 某 MCP） | 见下方「选件封包」 |
| **只安装已有 pack** | `agent-pack install path/to/foo.pack.json` 或 `sync --from` |

宿主有 **bun** 时，**优先 MCP 工具**（确定性、无 shell）；CLI 留给人与 CI。

---

## 模式 A：MCP（agent 首选）

npm 安装后，在 Cursor / Claude Code 的 `.mcp.json` 里加入：

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

Monorepo 开发时 args 可改为 `packages/pack-cli/mcp/server.ts`。

| 工具 | 作用 |
|------|------|
| `pack_detect` | 检测在场 harness + 适配表 |
| `pack_scan` | 扫描 skills / rules / MCP |
| `pack_export` | 导出便携 pack（`dry_run` 可预览） |
| `pack_install` | 安装已有 pack |
| `pack_sync` | 扫描 → 封包 → 安装（或 `from` 只装） |
| `pack_select` | 选件封包（`skills`/`rules`/`mcp` + 可选 `install`） |
| `pack_diff` | 对比两个 pack / lock |

各工具均支持 `cwd`；未传时用 `AGENT_PACK_CWD` 或进程 cwd。`capture_as`: `skill` \| `experience`（默认 experience）。

---

## 模式 B：CLI（人 / CI）

在项目根执行（Windows 可用仓库根的 `agent-pack.bat`）：

```bash
# 全自动：扫描 → 便携封包 → 检测 harness → 全部安装（自带 agent-pack skill 自举）
agent-pack sync

# 选名字
agent-pack sync --name my-team-pack

# 已有 pack，只安装
agent-pack sync --from .agent-pack/exports/my-team-pack.pack.json

# 只导出不装
agent-pack export --name my-team-pack
```

## Skill 约束 vs 经验罐头（打包者必选）

抓包/蒸馏出的 L2–L4 **不要默认当成 skill**。让用户选交付方式：

| 模式 | CLI | 装完在哪 | 特性 |
|------|-----|----------|------|
| **skill 约束** | `--capture-as skill` / `--harness` | `.claude/rules/*-harness.md` 或 `AGENTS.md` | 持久规则，像 mod 写进 harness |
| **经验罐头** | `--capture-as experience` / `--experience`（**默认**） | `.agent-pack/experiences/` + **各 harness SessionStart hook** | 内化注入，**不是 skill**；install 自动接 Claude/Codex/Hermes 等 |

L1（brainstorming 等 SKILL.md）始终是 **skill 约束**；只有抓包蒸馏内容才二选一。

## 可选模块 + pack.ignore

`.agent-pack/project.yaml` 里 `modules:` 控制打包/安装哪些层；`.agent-pack/pack.ignore` 用 **gitignore 语法**排除路径（密钥、transcripts、MEMORY 等）。

```bash
# 只额外打包 hooks + subagents
agent-pack export --modules hooks,subagents

# 安装时不要 memory
agent-pack install foo.pack.json --no-memory
```

默认 **关**：`hooks` `subagents` `memory` `settings` `transcripts`。要打包 `MEMORY.md` 需 `--modules memory` 且从 `pack.ignore` 删掉 `MEMORY.md` 行。

```bash
# 蒸馏进经验罐头（默认）
agent-pack pack --skills brainstorming --capture-as experience --install

# 蒸馏当 skill 约束写 rules
agent-pack pack --skills brainstorming --capture-as skill --install
```

`select.json` 示例：

```json
{
  "name": "my-dev-pack",
  "skills": ["brainstorming"],
  "captureAs": "experience"
}
```

---

**选件封包** — 先让用户确认要选什么，再写 manifest 并执行：

```bash
# manifest 示例见 .agent-pack/select.json
agent-pack pack --manifest .agent-pack/select.json --install

# 直接指定（PowerShell 下逗号会被拆成空格，两种写法都行）
agent-pack pack --skills brainstorming verification-before-completion --harness --install
```

`select.json` 格式：

```json
{
  "name": "my-dev-pack",
  "skills": ["brainstorming", "verification-before-completion"],
  "rules": ["CLAUDE.md"],
  "mcp": ["github"],
  "harness": true
}
```

- `skills` / `rules` / `mcp`：数组，**只封列出的**；省略或 `"*"` = 全选该项  
- `captureAs`: `"skill"` | `"experience"` — 抓包蒸馏怎么交付（`harness: true` = skill）

装完清单：`.agent-pack/applied/<name>.json`  
封包输出：`.agent-pack/exports/<name>.pack.json`

**自举**：`sync` / `export` / `install` 默认会把 **agent-pack 本 skill** 打进包并装到各 harness（除非 `--no-bootstrap`）。

**版本（v0.2）**：export/sync 写 `ccui-pack/v0.2`，每个 skill/MCP 带 `version` + `contentHash`/`configHash`；`.agent-pack/lock.json` 记录解析锁。

---

## 模式 C：无 CLI — 用文件工具自打包

1. 按 `docs/PACK_SPEC.md` schema 写 `ccui-pack/v0.1` JSON  
2. **L1**：只收录用户选的 skills（整个 `<name>/` 目录含 SKILL.md）、rules、mcp  
3. **L2 harness**（可选）：
   - 有 `.ccui/packs/*.pack.json` 或 `.agent-pack/capture/*.json` → 读最新，取 `harness` + `assembly` + `model`
   - 或用户给出系统提示原文 → 填入 `harness.base_system_prompt`
   - 没有来源 → 留空，`meta.fidelity: "L1"`
4. **便携化**：把每个 skill/rule 文件内容嵌进 `bundle.files`（`skills/<名>/...`、`rules/<名>`）  
5. 给用户 pack 路径；若可执行 CLI，再跑 `agent-pack install <pack>`

---

## 保真度（L1–L4）

| 层 | 进 pack | install 后 |
|----|---------|------------|
| L1 | knowledge + tools.mcp | `.claude/skills`、`.agents/skills`、`.mcp.json` 等 |
| L2 | harness：prompt / tool_schemas / reminders | rules 块或 experience 罐头 + hook |
| L3+ | assembly / loop | 写入 pack，adapter 尽力投射 |

L1 = prefunction 文件。L2+ = 瓶口录制，缺录标 L1。换模型行为会漂移。
---

## 和用户协作：选件对话模板

1. 列出当前项目有的：`skills` 目录名、`rules`、`mcp` 名（用 list/read，或让用户 paste `agent-pack detect` 输出）  
2. 问：「要全装还是只装哪几个？」  
3. 写 `.agent-pack/select.json` → `agent-pack pack --manifest ... --install`  
4. 若用户要 L2：**问是否有 capture 草稿，或请用户 paste 系统提示**；无来源则标 L1
5. 回报：`export 路径`、`projected harness 列表`、fidelity（L1/L2）

---

## 约定

- `harness` 段只来自 capture 或用户提供的原文；缺则标 L1  
- 用户说「只要 X Y」就只封 X Y  
- 换模型行为会漂移；pack 标 L1–L4

规范：`docs/PACK_SPEC.md` · CLI：`packages/pack-cli`
