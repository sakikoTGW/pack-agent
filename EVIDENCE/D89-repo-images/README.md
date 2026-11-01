# D89 · 插件详情：App Store 式版面 + 仓库图片

用户：「做成软件商店那样的嘛。实在不行就学苹果」。

## 版面（App Store 式）
- **头图卡**（白色圆角卡）：112px 插件图标 / 名字 26px / spec / 版本 + 可更新徽标 / 右上「更新」按钮（对应 App Store 的「获取」）；
- 卡片下方：打开目录 / 卸载 次级按钮；
- **预览**：仓库 README 图片横向滚动带，统一 **350×218** 圆角卡片（一样大；没有图就整块隐藏）；
- **描述**：正文段落；
- **信息**：白色圆角卡里的行式列表（版本 / 最新版本 / 作用域 / 来源），右对齐值，行间分隔线；
- 背景浅灰、卡片白底、圆角与留白都按 App Store 的节奏。

## 数据
- 图片来自插件 GitHub 仓库 README（`Core/RepoShots.cs`：解析 repository → raw README → 提图 → 过滤 shields/badge → 缓存
  `<launcher>/library/plugin-shots/<name>/shot-NN.ext`）；
- `PackageMeta.Repository` → `BundleRow.Repository` → `PluginRow.Repository` 打通。

## 隔离验收（按 PARALLEL.md）
- claim `lead-repo-images`；worktree `E:\worktrees\lead-repo-images`；分支 `pad/lead-repo-images`
- 构建 `E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根 `E:\tmp\pad-roots\lead-repo-images`；dev pid=54660，最小化 + `--shot --shot-page plugin-detail --shot-exit`，退出码 0
- 产物：`01-detail-repo-images.png`（旧版）、`02-detail-appstore.png`（App Store 式）

## 状态
- 分支 `pad/lead-repo-images` 上两次提交（`60f7072` 仓库图片、App Store 版面），**尚未合入 main**：
  主仓有别的 agent 未提交的 `pad/Core/Launcher.cs`，与本分支同文件不同位置。
