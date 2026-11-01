# D89 · 插件详情：显示仓库 README 里的图片（一样大）

用户：「就他们仓库的图片，不过要一样大」。

## 实现
- `Core/RepoShots.cs`（新增）：
  - 从 `package.json` 的 `repository`/`homepage` 解析 GitHub 仓库；
  - 抓 `raw.githubusercontent.com/<repo>/HEAD/README*.md`，正则提取 `![](...)` / `<img src>`；
  - 过滤 shields.io / badge / data: / 非图片扩展，相对路径按 raw 基准解析；
  - 下载到 `<launcher>/library/plugin-shots/<name>/shot-NN.ext`（最多 6 张，单张 ≤6MB），有缓存直接复用。
- 仓库字段打通：`PackageMeta.Repository` → `BundleRow.Repository` → `PluginRow.Repository`（profile 包与 trial 都带）。
- `PluginDetailWindow`：
  - 预览图统一 **200×126** 的方形卡片（`UniformToFill` 裁切），一排铺在中间，数量多少都一样大；
  - 没有本地预览图时异步从仓库拉，期间显示「正在从仓库加载图片…」，没图就隐藏；
  - 本地包内 `PreviewImages` 仍然优先。
- `--shot-page plugin-detail` 钩子会等图片加载（最多 10s）再离屏渲染，后台可验收。

## 隔离验收（按 PARALLEL.md）
- claim：`.parallel/claims/lead-repo-images.json`；worktree `E:\worktrees\lead-repo-images`，分支 `pad/lead-repo-images`
- 构建：`E:\tmp\pad-build\lead-repo-images` → 0 warning / 0 error
- 数据根：`E:\tmp\pad-roots\lead-repo-images`；dev 进程 pid=51384，最小化 + `--shot --shot-page plugin-detail --shot-exit`，退出码 0
- 实际下载到 3 张仓库图（缓存 `library/plugin-shots/dsh-better-sidebar/shot-01..03.png`）
- 产物：`01-detail-repo-images.png`（中间一排 3 张等大图）

## 状态
- 改动已提交到分支 `pad/lead-repo-images`（commit `60f7072`），尚未合并进 main：
  主仓当前有另一个 agent 未提交的 `pad/Core/Launcher.cs`（100 行，pack-runtime 相关），
  和本分支同文件不同位置；按 PARALLEL.md 不替别人提交/不动别人工作区。
