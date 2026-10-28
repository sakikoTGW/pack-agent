# 实测笔记（不进公众号正文）

> 日期：2026-07-18 · 机器：Windows · Bun 1.3.14 · Node 24  
> 用途：校对事实。读者稿见 `wechat-agent-modpack.md`。

## 已跑

| 项 | 结果 |
|----|------|
| `bun run test` | 全绿 |
| 裸 `export` | 硬拒绝，要 `--agent` |
| `export --agent packer` | skills=1 bundle=1 L1 |
| 临时目录 install/eject | skill + ledger + origin；eject 清 skill/mcp-bootstrap |
| 冷 npx | `npx --yes -p @sakikotgw/pack-agent packagent …` 成功 |
| 旧 npx 写法 | `npx --package … -- pack-agent` Windows 失败 |
| Hermes E2E | 见下 |

## Hermes 端到端（本次补测）

目录：`%TEMP%\pack-hermes-e2e-*\B`

1. 带 experience 的 pack install → `experienceHooks` 写入  
   `B\.hermes\config.yaml#pre_llm_call`，command 含 `AGENT_PACK_HOOK_STYLE=hermes`
2. `notes` 提示交互 consent / `hooks_auto_accept`
3. 跑 hook 脚本两种 style，再喂给 Hermes 源码 `_parse_response("pre_llm_call", stdout)`：
   - Claude `hookSpecificOutput` → `OUT None`（假成功机制复现）
   - `{"context":"..."}` → `HAS_MARKER True`（marker：`HERMES_E2E_CONTEXT_MARKER_9f2a`）

未做：拉起 Hermes 交互式 CLI 完整对话一轮（解析层与接线已证；consent/TTY 是产品外约束）。

## 编辑备忘（为何正文删掉清单体）

读者要的是判断和路径，不是 QA 表。  
「我跑过」应变成故事里的证据（`OUT None` / 一条能用的 npx），不该变成文末成绩单。
