# D86 · 隔离环境后台验收（按 docs/PARALLEL.md）

只做后台验证：不前置、不点击、不重启正式 PAD。

## 环境（照 PARALLEL.md §1/§2）
- worktree：`E:\worktrees\pad-lead`，分支 `pad-lead-verify`，基于 `dd9dec0`
- 构建：`dotnet build ... -o E:\tmp\pad-build\lead` → 0 warning / 0 error
- 数据根：`E:\tmp\pad-roots\lead`；`versions/instances/library` 用 junction 共享，
  `pad.json` 复制一份并把 `lastInstance` 设为 `dsh`（本机 DSH，12 个插件）
- 启动：`PACK_LAUNCHER_ROOT=... Start-Process ... -WindowStyle Minimized`
  - 进程 pid=23888，参数 `--shot ... --shot-page instance-plugins --shot-exit`
  - 自截图（进程自己渲染，不依赖屏幕/前台），退出码 0

## 验证到的东西（01-instance-plugins.png）
- 插件行第二行是**简介**（dsh-better-sidebar 的完整描述；没有简介显示「包里没写简介。」），不再是路径；
- **可更新**是实色徽标 `↻ 0.28.0 → 0.28.2` + 实心「更新」按钮；不可更新的行只有灰色刷新图标；
- **分类标签去噪**：最多 3 个（如 dsh-i-have-adhd 只剩 adhd / output-style / accessibility；
  dsh-plugin-mesh 只剩 mesh / lan / multicast），dsh / deepseek-harness / cordis 等已消失；
- 真图标仍生效（DSH 鲸鱼、各插件自己的 logo）；
- 收编实例（本机 DSH）的插件页能列出 desktop profile 的 12 个插件（profile 下拉回退到 home/profiles）。

## 测试（worktree 自己的 bin）
- `pad-window` ✓
- `pad-pack` ✓（实跑，32s）

## 未验证 / 待允许
- **插件详情独立窗口**：它是 `ShowDialog()` 出来的另一个窗口，`--shot` 只拍主页面；
  要验证需要一次真实点击（或给 PAD 加一个 `--shot-page plugin-detail` 的测试钩子）。
  按 §6，没有你的明确许可我不动前台窗口。

## 复现
```powershell
cd E:\worktrees\pad-lead
dotnet build agent-pack-dsh\pad\Pad.csproj -c Debug -o E:\tmp\pad-build\lead
$env:PACK_LAUNCHER_ROOT = "E:\tmp\pad-roots\lead"
Start-Process "E:\tmp\pad-build\lead\pack-agent-for DSH.exe" -ArgumentList @(
  "--shot","E:\tmp\pad-roots\lead\shot-plugins.png","--shot-page","instance-plugins","--shot-exit"
) -WindowStyle Minimized
```
