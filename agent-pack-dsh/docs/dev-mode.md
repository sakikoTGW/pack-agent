# PAD 开发模式 · 开发会话（方案 v2）

2026-11-04 · 状态：待落码 · 取代 `lab.md` 的“复制实例”方案
关联：`frontend-spec.md`、`hot-reload-integration.md`（D110）、`DECISIONS.md` D111

> **一句话**：开发模式不是“另一份实例”，而是**实例的一种启动方式**——起来后自动开一次“开发会话”，改动只落本次运行的覆盖层并热生效；会话里看得见改动、验证结论、逐项撤销；满意就固化（写实例 + 打点），不满意关掉即恢复。跑过的会话可重放，也可导出成**复现包**。

---

## 1. 为什么不是 lab（复制实例）

| 方案 | 问题 |
|---|---|
| 复制整个 home 建 lab | 要复制/重装依赖，慢；用户要管第二份“实例”；会话/凭据/工作区还得决定搬不搬 |
| **开发模式（本方案）** | 不复制、不新增实例：同一 home、同一实例；改动只落 `--patch` 覆盖层（现有 trial 机制），普通启动完全不参与 |

---

## 2. 会话模型

`instances/<id>/dev/<sessionId>/`
- `session.json`：schema `pad.dev-session/v1`（id / profile / startedAt / endedAt / state / changes[] / verdictRef / checkpointId）
- `changes.jsonl`：每行一条改动 `{ts, op, target, before, after, applied:"overlay"|"profile"}`
- `verdicts/<ts>.json`：验证结论（schema `pad.dev-verdict/v1`）
- `patches/`：本次运行用的 `--patch` 覆盖层文件

状态机：`running` → `committed` / `discarded` / `crashed`（可续跑或重放）。

## 3. 启动与改动语义

- 启动方式二选一：普通 / 开发模式（默认普通，记住上次选择）。
- 开发模式启动：`dsh --patch <session patches>`，runtime 账本记 `mode:"dev"`。
- 开发会话内：装/卸/改插件、改配置 → **只写覆盖层** + 走 D110 热重载热生效；行药丸「本次有效」。
- 固化：checkpoint → 写 profile（只写勾选项）→ 验证 → 报告；失败自动回滚。
- 丢弃：停进程、删覆盖层；实例不变。
- 开发会话内不改内核（换版本先普通启动操作），只提示不拦装。

## 4. 验证判据（会话内可手动重跑）

| id | 判据 | 证据 |
|---|---|---|
| `config-compiles` | `--profile <p> --dump-config` 退出 0 | `dump-config.yml` |
| `boots` | 启动并读到 ready | `logs/` |
| `stays-up` | 存活 N 秒（默认 45，可配） | 进程事实 |
| `plugins-resolve` | bundles 全解析、无 missing | `plugin-audit.json` |
| `tools-inventory` | 工具数/token 相对基线（warn 不阻断） | `inspect.json` |
| `source-untouched` | 实例声明层指纹前后一致 | `source-hash.txt` |

结果：任一 `fail` → fail（固化按钮降级为“仍然固化（不推荐）”并留记录）；`warn` 不阻断。

## 5. 复现包（用户命名：复现包；代码：fixture）

一个 `.zip`：`fixture.json` + `evidence/`。

```json
{
  "kind": "pad.fixture/v1",
  "name": "plugin-update-wipes-node-modules",
  "kernel": "0.2.1-rc.2",
  "profile": "web",
  "base": ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app"],
  "steps": [{"op": "plugin add", "spec": "dsh-vision-ocr@0.3.1"}],
  "expect": {"config-compiles": "pass", "plugins-resolve": "fail"}
}
```

- `pad cli dev repro export <session>`：把会话存成复现包。
- `pad cli dev repro apply <file>`：隔离 home 里跑 steps，比对 expect（命中 = 复现成功）。
- 只存声明 + 操作 + 期望，不存会话/凭据/用户数据；不碰真实例。

## 6. 命令面

```text
pad cli run <inst> --dev [--profile p] [--name <会话名>]
pad cli dev status <inst> [--json]
pad cli dev verify <inst> [--json] [--wait 60]
pad cli dev undo <inst> --change <n>          # 逐项撤销
pad cli dev commit <inst> [--only 1,2] [--dry-run]
pad cli dev discard <inst>
pad cli dev list <inst> [--json]
pad cli dev replay <inst> --session <id>
pad cli dev repro export <inst> --session <id> [--out <zip>]
pad cli dev repro apply <zip> [--json]
```

退出码：0 成功 / 2 验证不可固化 / 3 环境异常；错误码 `PA24x`。

## 7. 分期

| 期 | 内容 | 完成标志 |
|---|---|---|
| **P0（已落码 2026-11-04）** | `run --dev` + 会话 JSON/事件 + `dev start/status/apply/verify/commit/discard/list`（判据 `config-compiles/plugins-resolve/source-untouched`，其余 skip）+ 固化打点 + `pad cli mcp serve`（7 个工具）+ checkpoint 显式 prune | ✅ 真机闭环：apply → verify pass（exit 0）→ commit（打点）→ discard 清环境；证据 `EVIDENCE/dev-mode-p0/` |
| **P1** | 逐项撤销、选择性固化、boots/stays-up/tools 判据 + 证据目录、`dev replay`、`repro export/apply` | 会话可只固化部分改动；复现包能一条命令复现 |
| **P2** | 窗口：启动方式选择、开发会话抽屉、试验历史页 | 窗口可完成一次完整开发会话 |

## 7.1 P0 实际落点（2026-11-04）

- `pad/Core/DevSession.cs`：`DevSession` / `DevChange` / `DevVerdict`、`DevSessions`（会话与历史）、`DevOps`（apply/verify/commit/discard）。
- `pad/Core/Mcp.cs`：stdio MCP server，工具 `dev_start/status/apply/verify/commit/discard/list`；stdout 只走 JSON-RPC。
- `pad/Core/Cli.cs`：`dev` / `checkpoint` / `mcp` 命令组；`run --dev`；`--json` 时人类日志全部转 stderr。
- `pad/Core/Checkpoints.cs`：**不再自动删备份**；`Prune` 改为显式、返回删除列表，CLI `checkpoint prune` 默认 dry-run。
- `pad/Core/Trial.cs`：`file:` 依赖的 discard 修复（按包名摘依赖 + 删目录），`PackageName` 单测覆盖。
- 指纹口径修正：`source-untouched` 只看 `dsh.profile.bundles` + patch（trial 只加依赖，不翻指纹；commit 才翻）。

## 8. 风险

- 覆盖层与热重载的时序：以 D110 的 settle + V1–V4 为准，失败回滚，不赌。
- 覆盖层残留：`dev discard` 必删；普通启动不带任何 dev patch（已由 `Trials.PatchArgs` 只带 trial 决定，需确保只带当前会话）。
- 固化冲突：实例在会话期间被外部改过 → 拒绝固化，提示先回退打点。
