# D82 · 插件真图标（学插件市场：每个插件都有它自己的图标）

用户：「他们真的有插件自己的图标，你看看」。之前 PAD 的插件行要么空白、要么字母/胶囊占位，不像插件市场。

## 图标从哪来（真实来源，不是画占位）
1. **官方 deepseek-ai 包**（dsh-base / dsh-web-app…）：DSH 仓库不公开、包里也没有 logo，
   但 DSH 桌面端自带官方图标 `resources/icon.png`；PAD 把它作为内置资产
   `Assets/brand/dsh.png`，`BundleIcon.Resolve` 遇到 `@deepseek-ai/*` 时写进该插件的图标缓存
   `library/plugins-meta/<spec>/icon.png` → 列表里显示真图标。
2. **装到磁盘的插件**（dsh-tui 等）：沿用包内 logo / jsDelivr raw 抓取（机制原本就有，dsh-tui 的 logo 早已缓存）。
3. **插件市场（下载页货架）**：`MarketRow` 新增 `Repository` + `IconPath`（INPC）；
   拿到 npm 搜索结果里的 repository 后，异步用 `BundleIcon.EnsureFromRepo` 抓仓库里的
   `docs/assets/logo.svg|png`，仍没有就退到 GitHub 作者头像；官方包直接播种内置品牌图标。
   `PluginMark` 支持尺寸（管理页 28 / 货架 44），有图用图，没图才用类型字形兜底。

## 实拍
`01-manage-real-icons.png`：`@deepseek-ai/dsh-base` = DSH 官方图标；
`@deepseek-harness-tui/dsh-tui` = TUI 自己的图标；行尾保留 插件 / TUI 角色标签。

## 验证
build 0 warning / 0 error；pad-window / pad-pack / pad-project 全绿。
说明：本机进程网络被拦，货架异步抓图在这里无法在线验证（官方包走内置图标不受影响）；
用户机器上网络可用时按同一路径抓仓库 logo。
