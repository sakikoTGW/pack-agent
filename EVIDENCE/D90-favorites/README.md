# D90 · 插件收藏（本地歌单式）+ 详情页细节清理

用户说：详情页细节太丑，「打开目录/卸载」不必要，顶部「插件/关闭」也难看；另外想加收藏功能——
像歌单一样收藏插件，在本地收纳整理自己的插件库。

## 收藏
- `Core/Favorites.cs`：`<launcher>/library/favorites.json` 存 `["spec", …]`（字符串数组，按收藏顺序）；
  `IsFavorite / Toggle / Load`。跨实例共用，纯本地，不联网。
- 插件列表行尾加**星标按钮**：收藏=Accent 实心星，未收藏=灰色星（点击切换）。
- 过滤栏新增 **「收藏 (N)」** tab，和 全部 / 可更新 / 试验中 并列；空态提示「还没有收藏的插件：点行尾的星标收藏。」
- 详情页头图卡里加「☆ 收藏 / ★ 已收藏」按钮，收藏状态与列表行双向同步（`PluginRow.Favorite` 通知）。
- 收藏数据是"歌单"的最小版：先做扁平收藏夹；以后可以扩展成多个命名歌单/分组排序。

## 详情页清理（按反馈）
- 删掉顶部「插件 / 关闭」一行（窗口自带的标题栏足够）；
- 删掉头图卡下方的「打开目录 / 卸载」两个按钮（不必要且难看）；
- 保留：头图卡（图标/名字/spec/版本/更新/收藏）、预览大图横滑、描述、信息卡。

## 隔离验收（按 PARALLEL.md）
- claim `lead-repo-images`；worktree `E:\worktrees\lead-repo-images`；构建 `E:\tmp\pad-build\lead-repo-images`（0/0）
- 数据根 `E:\tmp\pad-roots\lead-repo-images`，预置 `library/favorites.json = ["dsh-better-sidebar"]`
- 后台自截：`--shot-page instance-plugins`（pid 记录见命令）→ 收藏 (1) tab、行内星标；
  `--shot-page plugin-detail` → 新详情版面 + ★ 已收藏
- 产物：`01-plugin-list-fav.png`、`02-detail-appstore.png`、`03-star-closeup.png`

## 状态
- 分支 `pad/lead-repo-images`（`60f7072` 仓库图片 / `b2c4cf7` App Store 版面 / 本次收藏+清理），**未合入 main**：
  主仓有别的 agent 未提交的 `pad/Core/Launcher.cs`，与本分支同文件不同位置。
