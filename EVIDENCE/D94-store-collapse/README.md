# D94 · 插件商店头部可折叠（默认收掉不重要的东西）

用户：「插件商店那个框改成可以缩掉……不重要就缩掉，重要了再显示」。

## 折叠规则
- **默认（收）**：只留「插件商店」标题 + 共 N 个 · 第 x/y 页 + **筛选 ▾** + 搜索框 + 搜索按钮。
  删掉了这些不重要的文字/标签：
  - `插件 · keywords:dsh-plugin` 副标题（缩成 ToolTip 级别的信息，直接不占位）；
  - 搜索框上方「名称」标签（placeholder 已经说明）；
  - 结果区「点卡片看详情并安装」提示句。
- **展开（筛选 ▾）**：版本 / 装到 / 重置条件 / 排序 / 分类 / 只看已安装；按钮变成「收起 ▴」。
- 折叠只影响可见性，搜索、排序、分类、收藏状态都不受影响。

## 隔离验收（按 PARALLEL.md）
- worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`；dev 最小化 + 后台自截：
  - `--shot-page market` → `01-collapsed.png`
  - `--shot-page market-more`（测试钩子）→ `02-expanded.png`，退出码 0
- 顺手补了 `market` / `market-more` / `market-detail` 的 TabDownload 路由（原来 market-detail 只渲染了窗口，主窗还停在启动页）。

## 状态
- 分支 `pad/lead-repo-images`，**未合入 main**（主仓有别的 agent 未提交的 `pad/Core/Launcher.cs`）。
