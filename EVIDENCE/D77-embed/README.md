# D77 · 打开项目 = 右边嵌入这个项目的 DSH

用户确认：「左边 PAD 管理，右边嵌 DSH」（第一版不做"点会话切过去"）。
不再让 PAD 自己画项目仪表盘——右边就是这个项目真实的 DSH 工作区。

## 实现
- 新 `Views/ProjectDshView.xaml(.cs)`：WebView2 承载器。
  - 项目在跑 → `ShowUrl()` 导航到该项目实例的 `127.0.0.1:<port>`（token URL）；
  - 项目没启动 → `ShowStopped()` 给一张引导卡（启动 / 在浏览器打开 / 重试）；
  - 导航失败（token 过期/页面打不开）→ 同样回落引导卡 + 「启动这个项目」重新拉起；
  - 只允许本机 localhost 页面，外链交给系统浏览器；`重开` 按钮 reload。
- `WorkbenchView`：项目模式不再画卡片，改为 `CardScroll` 隐藏 + `DshHost` 显示；
  `ShowProjectDsh()` 接 `StartProjectAsync` / `Reveal.Url`，URL 取 `_scopeFacts.PadUrl`，兜底扫日志。
- 其它页面（现在/实例）保持原样；DSH 一行不改。

## 实拍（真实 PAD 窗口）
- `05-reenter.png`：左 PAD 项目栏 + 右嵌 DSH（真实内容）
- `06-stopped.png`：停止后引导卡
- `11-starting.png` / `12-started.png`：点「启动这个项目」→ 重新拉起并嵌回 DSH

## 已知毛边（下一版修）
- 停止后 WebView 会先显示"打不开"再回落引导卡（应直接显示"已停"）；
- 启动耗时较长（几十秒），期间只有按钮文字变化，缺进度；
- WebView2 是独立合成层：PrintWindow 截不到，截图/录屏要用屏幕捕获；
- v1 不做"点左栏会话切过去"（README 顶部约定）。
