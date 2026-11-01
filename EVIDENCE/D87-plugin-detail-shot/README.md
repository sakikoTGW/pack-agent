# D87 · `--shot-page plugin-detail` 测试钩子（后台自截插件详情窗口）

用户要求：给 PAD 加 `--shot-page plugin-detail` 测试钩子，后台自截插件详情窗口，不前置窗口。

## 实现（分支 `pad-lead-verify`，已合并到 main `43c1fab`）
- `MainWindow`：`--shot-page plugin-detail` 路由到「版本设置」内页，并把截图延迟抬到 market 档（8s）；
- `InstanceView`：
  - ShotPage = `plugin-detail` 时进插件页；
  - 插件列表加载完 → `QueueDetailShot()`：取第一条有简介的插件，创建 `PluginDetailWindow`（不开任务栏、放到屏幕外 -20000），
    在 `Loaded → ApplicationIdle` 时 `RenderTo()` 离屏渲染，然后自己关闭；
- `PluginDetailWindow.RenderTo(path)`：`RenderTargetBitmap` 渲染窗口自身，写 PNG + `.txt`（logical/dpi/pixels）。
- 输出文件：`<--shot 路径去掉 .png>-plugin-detail.png`（主窗口截图仍照旧）。

## 后台验收（按 PARALLEL.md，不碰前台）
```powershell
$env:PACK_LAUNCHER_ROOT = "E:\tmp\pad-roots\lead"
Start-Process "E:\tmp\pad-build\lead\pack-agent-for DSH.exe" -ArgumentList @(
  "--shot","E:\tmp\pad-roots\lead\shot-detail.png","--shot-page","plugin-detail","--shot-exit"
) -WindowStyle Minimized
# 退出码 0；产物：
#   shot-detail.png               （主窗：版本设置 → 插件，12 个插件）
#   shot-detail-plugin-detail.png （插件详情窗口离屏渲染）
```
- 构建：worktree `E:\worktrees\pad-lead` → `E:\tmp\pad-build\lead`，0 warning / 0 error
- 数据根：`E:\tmp\pad-roots\lead`（junction 共享 versions/instances/library，独立 pad.json，lastInstance=dsh）
- 本仓库共享 `bin` 未编译、正式 PAD 未触碰

## 产物
- `01-plugin-detail.png`：插件详情窗口（图标 + 名字 + spec + 版本/可更新徽标 + 简介 + `--profile`/npm latest + 更新/打开目录/卸载/关闭）
- `02-plugin-page.png`：主窗插件页（标签去噪、可更新徽标、简介第二行）
