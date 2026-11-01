# D58 · 2026-10-31 · 图标 / logo / 交互反馈 / 动效：把"事实"画成形状

用户：

> "多来点图标和logo，交互反馈，动效。去学学开源的logo，图标库"

## 先去看了什么（开源图标 / logo 库与许可）

| 库 | 许可 | 学到的东西 |
|---|---|---|
| [Lucide](https://lucide.dev/license) | ISC | 24 网格、一图标一概念、圆头笔画 |
| [Tabler Icons](https://tabler.io/license) | MIT | 同一套 24 网格；状态/设备类图标的取舍 |
| [Simple Icons](https://simpleicons.org/) | CC0 | 品牌 logo 的正确用法：**取真实品牌标记，不自己画一个像的** |
| [Material Symbols](https://fonts.google.com/icons) | Apache-2.0 | 同一概念在 14–24px 都成立的最小轮廓 |

**怎么用**：取共同表达（24 网格、一个概念一个图标、笔画圆润、单色 currentColor），
按本仓既有 iconfont 的 1024 单位填充轮廓**重画**（不逐条复制任何一家，避开许可问题）；
品牌 logo 不手绘——直接读运行中 exe 的真实图标。

## 改了什么

1. **图标（9 个新轮廓，`Theme/Icons.xaml`）**：`I.Cpu` / `I.Plug` / `I.Clock` / `I.Chat` /
   `I.Layers` / `I.Activity` / `I.Memory` / `I.External` / `I.Home`。
   用在：运行卡的六个统计、对话行、项目事实行、四张卡的标题、所有 hero 按钮与打开按钮、
   主页七张卡的标题（`CardIcon`）。
2. **真实 logo**：
   - 页头（`ScopeLogoHost`）和运行卡（`LogoFor`）显示**运行中外壳自己的 exe 图标**；
     `Core/AppIcon.cs` 用 `SHGetFileInfo` 取 HICON → `CreateBitmapSourceFromHIcon` → PNG，
     缓存在 `<root>/library/icons/exe/`（同一 exe 同时间戳只取一次；不引 System.Drawing）；
   - 取不到时退回 `InstMark`（整合包图 → 载体图 → 鲸鱼）。
3. **交互反馈**：
   - 统计值**点一下复制**（`Clipboard.SetText` + toast `已复制 pid：68084`），悬停有 AccentWash 底纹、手型光标；
   - 四张卡悬停轻抬 2px（`TranslateTransform` + `Fx.Move`）；
   - 对话行悬停底纹；按钮沿用既有 hover/press 模板；
   - 页头运行灯与卡内 lamp 呼吸（`RunLamp` 既有动效）。
4. **动效**：
   - 运行现场第一次到达：logo `Fx.PulseIn(pop)`、统计区 `Fx.Fade` 淡入；
   - **只播一次**（`_heroAnimated`，切项目重置），2s 的状态轮询不会反复重播；
   - 关掉动画时（`Fx.Animated=false`）全部退化为静止值，不闪。

## 验证（真实窗口）

`project-icons.png` / `home-icons.png`（最终构建）：

- 页头：`DeepSeek Harness` 真实图标 + 绿呼吸灯 + `本机 DSH 0.2.1-rc.2 · 在跑 · dsh/workspace`；
- 运行卡：鲸鱼 logo + `在跑`；带图标的六格：进程 / pid / 端口 / 配置 / 已跑 / 内存；
- 对话：每行 灯 + 对话图标 + 标题；标题带 `对话 · 1 在执行` 图标；
- 按钮：切到窗口（窗口图标）/ 打开工作区（文件夹）/ 日志（文档）；
- 主页：七张卡标题都有图标（在运行 / 本机 DSH / 上下文 / 近 7 天 / 工具调用 / 项目卡 / 便签）。
- 点 `pid` 统计 → 剪贴板 = `68084`（实测），toast 报 `已复制 pid：68084`；
- `library/icons/exe/8e35c0d9ab4b.png`（3984 B）证明真实图标已缓存。

`dotnet build`：0 warning / 0 error；`pad-window` 新增 D58 断言（九个小图标键、AppIcon 用
`SHGetFileInfo` 且缓存、统计点击复制、卡片 hover、运行卡真实 logo + InstMark 兜底、
进场动效只播一次、主页卡片图标、页头 logo/lamp），`pad-depth` / `pad-pack` 绿。

## 边界

- 图标形状是本仓重画（1024 单位填充），不复制 Lucide/Tabler 的路径数据；品牌 logo 来自本机 exe 资源，
  不随包分发。
- 运行卡的 logo 取的是"正在跑的那个 exe"，没在跑时退回实例芯片。
- 动效只做一次；状态刷新不重播。关动画时保持可读的静止状态。
