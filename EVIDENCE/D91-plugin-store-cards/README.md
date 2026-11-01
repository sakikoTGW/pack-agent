# D91 · 插件商店：卡片网格（App Store 式）

用户：「插件商店呢」——把「下载 → 插件」从一行行列表改成商店卡片。

## 版面
- 标题区：「插件商店」大标题 + 当前 shelf 摘要（`插件 · keywords:dsh-plugin`）；
- 搜索表单保留（名称 / 版本 / 装到 / 搜索 / 重置条件 / 共 N 个）；
- 结果区改 **白卡片网格**（灰底 + 3 列，宽度自适应换行）：
  - 44px 插件图标、名字、作者；
  - 两行描述；
  - 底部：版本、更新时间、分类 chip；
  - 右上角 **收藏星标**（与插件页共用 `library/favorites.json`）；
- 点卡片进详情（复用现有市场详情面板）。

## 隔离验收（按 PARALLEL.md）
- claim `lead-repo-images`；worktree `E:\worktrees\lead-repo-images`
- 构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`；dev pid=51892（首版）/后台自截后退出，退出码 0
- `--shot-page market` 后台自截：`01-store-cards.png`

## 状态
- 分支 `pad/lead-repo-images`（本次提交在 `8ec920f` 之后），**未合入 main**：主仓有别的 agent
  未提交的 `pad/Core/Launcher.cs`，与本分支同文件不同位置。
