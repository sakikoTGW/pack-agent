# DSH 启动器 P0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 同一仓里能装两个 DSH 发行号、建两个隔离实例、同时 `run web`，诊断长得像 rustc。

**Architecture:** 引擎是现有 `packagent dsh` 进程里的 `agent-pack-dsh/modpack/launcher.ts`。不另起 Rust crate，不另起后端。`pack-index` 继续 Rust，只服务 SQLite 检索。P0 写死 `web`/`headless`。真安装走 `pnpm add @deepseek-ai/dsh@<ver>`；单测用假 `bin.js`。

**Tech Stack:** TypeScript · Bun · 已有 `agent-pack-dsh/modpack/cli.ts` · Node/pnpm 在 PATH · 临时目录 `E:\tmp\pack-agent`

---

## 文件

- Create: `agent-pack-dsh/modpack/launcher.ts`
- Create: `agent-pack-dsh/modpack/launcher.test.ts`
- Modify: `agent-pack-dsh/modpack/cli.ts`（`launcher` 子命令）
- Modify: `package.json`（test 脚本加上 launcher.test.ts）

P0 不做：注册表解释器、跨实例挂载、插件管理、导入导出、Tauri、凭据、收编 `~/.dsh`。这些除挂载外设计已钉，见 PRODUCT / launcher-design。

## 验收

1. 两个版本目录同时在 `versions/`
2. 两实例 home 互不包含对方会话
3. A 跑着再开 B，`runtime.json` 两个 pid
4. 停 A 不影响 B
10. 诊断含 `error[PA`、`-->`、`= help:`
