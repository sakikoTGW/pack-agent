# D88 · 插件详情改成软件商店式页面（大图标居中 / 预览图 / 简介）

用户：「这个页面还有点难看，正常来说有图片的应该像软件商店一样中间渲染插件」。

## 版面（PluginDetailWindow 重写）
- 顶部：`PLUGIN` 标签 + 右上「关闭」；
- **头图区居中**：96px 插件图标 → 名字（26px）→ spec（等宽）→ 版本 + 可更新徽标 → 动作按钮（更新/打开目录/卸载）全部居中；
- 分隔线；
- **预览图**：插件包内若有 `screenshot* / preview* / banner* / cover* / showcase*` 图（最多 3 张，
  排除 icon/logo），像商店一样在中间铺开（`PluginRow.PreviewImages`，数据驱动、无包名硬编码）；
- 简介段落、分类标签、`--profile / npm latest` 元信息；
- 保留 `--shot-page plugin-detail` 离屏渲染钩子。

## 验证（隔离环境，按 PARALLEL.md）
- worktree `E:\worktrees\pad-lead`，输出目录改用 **`E:\tmp\pad-build\lead-ui`**（原 `lead` 被其他 agent 的 PAD 进程锁住）
- 数据根 `E:\tmp\pad-roots\lead`；dev 进程 pid=17824，最小化 + `--shot --shot-page plugin-detail --shot-exit`，退出码 0；
- 产物：主窗插件页 + `-plugin-detail.png`（01/02）。
- `pad-window` ✓（worktree 自己 bin）
