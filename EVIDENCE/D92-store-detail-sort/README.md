# D92 · 商店详情改苹果式窗口（带安装）；商店排序 / 只看已安装

## 2）商店详情 = App Store 式窗口
- 点商店卡片不再进旧的三栏详情面板，而是打开 `PluginDetailWindow` 的**商店模式**：
  图标 / 名字 / spec / 版本 / **安装**按钮（右上）/ 收藏 / 预览图（仓库 README）/ 描述 / 标签 / 信息卡；
- 安装按钮直接走现有 `InstallFrom(row, row.Version)`，装到「装到」下拉选中的实例（默认最新版本）；
- 截图钩子：`--shot-page market-detail`，产物 `<--shot>-store-detail.png`，后台离屏渲染。

## 3）商店排序 / 筛选
- 头部加 **排序**：热度（默认，npm popularity）/ 最近更新（date ISO 解析）/ 名称；
- 加 **只看已安装**：用目标实例的 bundle 列表过滤（`Launcher.ListBundles` 的 spec 集合）；
- 排序/筛选在客户端对当前页应用（`_lastPage` 重绘），不额外请求 registry；
- 收藏星标保留在卡片右上角，和插件页共用 `library/favorites.json`。

## 隔离验收（按 PARALLEL.md）
- worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`；dev 进程最小化 + `--shot --shot-page market / market-detail --shot-exit`，退出码 0
- 产物：`01-store-detail-window.png`（dshmarket 商店详情窗口）、`02-store-sort-filter.png`（排序/只看已安装）

## 状态
- 分支 `pad/lead-repo-images`，**未合入 main**（主仓有别的 agent 未提交的 `pad/Core/Launcher.cs`）。
