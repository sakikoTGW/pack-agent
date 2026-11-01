# D98 · 窗口 resize 时实时重算每页个数

用户：窗口 resize 时也实时重算分页。

## 实现（防抖 220–240ms，拖动窗口不会刷屏）
- **插件页（InstanceView）**：`SizeChanged` → 重算每页个数；若变化，按"当前第一条所在的序号"重新定位页号，再 `PaintPlugins()` 重绘。
- **插件商店（DownloadView）**：`SizeChanged` → 重算每页个数；若变化，按当前页号 × 新每页个数重算 `_from`，重新向 registry 取这一页；
  上一页/下一页步长始终用当前窗口对应的每页个数。
- 每页个数规则不变：`窗口逻辑高度 / 17`（插件页 24–84，商店 10–100）。

## 尺寸相关验证（截图）
- 窗口 1000×620 → 约 36 个/页 → 商店分页 `1 / 326`
- 窗口 1200×900 → 约 52 个/页 → 商店分页 `1 / 226`，卡片自动变 4 列
- 产物：`01-window-1000x620.png`、`02-window-1200x900.png`

## 隔离验收（按 PARALLEL.md）
- worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`（临时改 `pad.json` 的 window 尺寸做两档截图，已还原）
- 后台自截 `--shot-page market`，退出码 0
