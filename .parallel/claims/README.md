# 并行占位（claims）

多 agent 同时改 PAD 时，用 `scripts/parallel-claim.ps1` 占位，避免同名 worktree / 分支 / 输出目录撞车。

一个 claim 一个 JSON：`agent / task / scope / branch / worktree / build_out / data_root / base / created_at`。

## 用法

```powershell
# 占位（只写 claim）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent lead -Task ui-detail -Scope "agent-pack-dsh/pad/Views,agent-pack-dsh/pad/Core/PluginList.cs"

# 占位 + 直接建 worktree（推荐）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent lead -Task ui-detail -Scope "agent-pack-dsh/pad/Views" -Create

# 看谁占了什么
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -List

# 合并完释放
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent lead -Task ui-detail -Release
```

## 命名（由脚本生成，不要手写）

| 东西 | 规则 |
|---|---|
| slug | `<agent>-<task>`（小写、连字符） |
| 分支 | `pad/<slug>` |
| worktree | `E:\worktrees\<slug>` |
| 构建输出 | `E:\tmp\pad-build\<slug>` |
| 数据根 | `E:\tmp\pad-roots\<slug>` |

## 禁止

- 复用别人的 worktree / 分支（脚本会检测并拒绝）；
- 往别人的分支提交（合并前用 `git log <base>..<branch>` 复核）；
- 共用 `E:\tmp\pad-build\lead` 这类公共输出目录（会锁 exe）。

## 2026-11-01 事故记录

两个 agent 同时用了 `E:\worktrees\pad-lead` / 分支 `pad-lead-verify`，
另一个 agent 的 `454c568`（插件市场美化）被混进同一次合并（`4586c55`）。
教训：唯一命名 + claim 文件 + 合并前审分支提交。
