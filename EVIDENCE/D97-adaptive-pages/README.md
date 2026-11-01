# D97 · 分页大小按窗口高度自适应（插件页 + 插件商店）

用户：分页不要固定 54，按窗口高度自适应；插件市场那边也看看。

## 实现
- 规则：**每页个数 ≈ 窗口逻辑高度 / 17**（滚动一段就翻页），并夹在合理区间：
  - 版本设置 → 插件列表：`clamp(height/17, 24, 84)`；
  - 插件商店：`clamp(height/17, 10, 100)`，并通过 `Market.Search(..., pageSize)` 传给 registry（npm search `size`），
    上一页/下一页也按同一个自适应大小的步长走。
- 窗口越高一页越多，窗口矮一页越少；默认 1000×620 逻辑窗口下 → 约 36 个/页。
- 商店页脚分页器随页面数变化：11,712 个结果 → `1 / 326`（原来固定 20/页时是 1/586）。

## 隔离验收（按 PARALLEL.md）
- worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`；后台自截 `--shot-page market` → `01-market-adaptive-pages.png`，退出码 0
- 插件页本机只有 12 个插件（< 每页数），分页器不显示（符合预期）。

## 状态
- 分支 `pad/lead-repo-images`，**未合入 main**（主仓有别的 agent 未提交的 `pad/Core/Launcher.cs`）。
