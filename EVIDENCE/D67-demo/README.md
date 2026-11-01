# D67 · 演示包 + 「直接打开对话」+ 更多实拍/GIF

用户反馈：「图片不够多，也没有 GIF」「没看见能直接打开对话的方法」
「.tmp/pad-project-mockup.html 你的演示呢？」

## 交付

- **`demo.html`**：可交互演示页（打开方式：双击 / `start EVIDENCE\D67-demo\demo.html`）
  - 顶部 6 帧 GIF（真实操作链路，可重播）
  - 逐帧查看器（点缩略图 / ← → 切换）
  - 「直接打开对话」三步说明 + 实拍
  - 更多实拍（D62/D63/D65/D66）
  - 和原型的关系：链接 `prototype.html`（设计定版 v4 原件，从 `.tmp/pad-project-mockup.html` 拷入，路径稳定不再依赖 .tmp）
- **`demo.gif`**：6 帧、1000px 宽、带中文说明条（PIL 合成，386 KB）
- **`frames/`**：6 张真实窗口截图（PrintWindow，1843×1314）
- **`extra/`**：D62/D63/D65/D66 关键实拍

## 「直接打开对话」实现（D67 新增）

项目页对话卡：**每一行都可点**，右上还有「打开对话」按钮。点击后：

1. 先按标题找已经在跑的 DSH 窗口（`ShellActivate.TryFocusTitle`）→ 前置；
2. 找不到 → 读实例日志里最近一次 `<code>dsh web: http://…?token=…</code>`，用 `Reveal.Url` 打开；
3. 同时把会话 id 复制到剪贴板，DSH 里粘贴即可定位；
4. 都没跑 → Toast「这个项目没在跑：先点启动」。

**为什么最后一步是贴 id**：实测 DSH web 没有 URL 深链——`?session=…` 查询参数被 SPA 丢掉
（地址栏回到 `/`，页面仍是新会话）。所以「直接打开对话」= 切/开窗口 + 复制 id，两个动作都可验证，
不是只画了一个按钮。

实拍：`frames/03-after-open.png`（Toast「已切到 DSH 窗口，会话 id 已复制」）、
`frames/04-dsh-window.png`（真的 DSH 窗口）。

## 这次的截图怎么来的

- PAD 自带 `--shot` 抓静态页 + `PrintWindow(PW_RENDERFULLCONTENT)` 抓真实窗口；
- 交互帧用 **UI Automation** 真点按钮（管理 / 主页 / 侧栏项目 / 打开对话 / 实例视角），
  不是拼图；脚本 `E:\tmp\padshot.ps1`（辅助工具，不进仓库）；
- GIF 用 bundled Python + Pillow 合成，底部中文说明条用微软雅黑。

## 验证

- `dotnet build` 0 warning / 0 error；新 PAD（pid 65800）在跑，界面就是截图里的版本；
- `pad-project.test.ts`（含项目页接线契约）+ `pad-window` 回归绿。

## 已知边界

- DSH 无会话深链，最后一步是「粘贴 id」；等 DSH 提供深链或 PAD 拿到管理口后可一步直达；
- 04 帧里 DSH 窗口同时开着原型 HTML（用户当时在看），属于现场真实画面；
- `demo.html` 用相对路径引图，整个 `EVIDENCE/D67-demo/` 目录可整体拷走。
