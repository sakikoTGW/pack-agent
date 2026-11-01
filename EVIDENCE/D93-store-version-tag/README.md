# D93 · 商店详情加版本选择；商店加分类筛选

## 2）商店详情窗口：版本选择（历史版本）
- 头图卡里新增「版本 [1.73.7 ▾]」下拉：打开时异步 `Market.FetchPackument` 拉版本列表，
  默认选中最新版；安装按钮按所选版本走 `InstallFrom(row, version)`。
- 版本列表加载失败时静默回退到默认版本安装；`--shot-page market-detail` 会等版本列表（≤8s）再截图。

## 3）商店分类筛选（tag）
- 头部「排序」旁新增「分类」下拉：从当前结果页的 `TagText`（marketplace / balance / …）动态生成
  「全部 + 最多 8 个分类」，选中后客户端过滤当前页（`_tag`）。
- 与排序、只看已安装、收藏星标共存，全部在客户端重绘，不额外请求 registry。

## 隔离验收（按 PARALLEL.md）
- worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`；dev 进程最小化 + `--shot --shot-page market / market-detail --shot-exit`，退出码 0
- 产物：`01-store-detail-version.png`（dshmarket 详情：版本下拉 1.73.7 + 安装 + 仓库预览图）、
  `02-store-tag-filter.png`（排序 / 分类 / 只看已安装）

## 状态
- 分支 `pad/lead-repo-images`，**未合入 main**（主仓有别的 agent 未提交的 `pad/Core/Launcher.cs`）。
