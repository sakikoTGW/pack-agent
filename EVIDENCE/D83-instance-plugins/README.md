# D83 · 实例插件扫描：去掉硬编码，所有条目由数据解析

用户：「不要硬编码。为什么其他的实例不行呢」。

## 之前为什么有的实例列不出插件
1. 管理页有一道**硬编码过滤** `IsProfileLayer(spec)`：只认 dsh-base/dsh-web-app/dsh-tui/pad-gateway 等几个名字，
   第三方插件全被藏掉（本机 DSH 只显示 2 个就是这原因）→ 已移除。
2. 插件包目录只查 `profile/node_modules/<spec>`；**发行号自带的插件**不在那里
   （新版本在 `versions/<ver>/node_modules/.pnpm/<scope>+<name>@*/…`，老版本在
   `versions/<ver>/node_modules/@deepseek-ai/dsh/node_modules/…`）→ 一律显示“未装到磁盘”。
3. 图标/版本号判断里还有按 `@deepseek-ai/` 前缀硬编码的分支 → 已删，改成 `Provided`（是否由发行号解析到）数据驱动。

## 现在的通用解析（没有包名硬编码）
`Launcher.ListBundles` 对每个 `dsh.profile.bundles` 条目依次找包：
1. `home/profiles/<profile>/node_modules/<spec>`（用户装的）；
2. `versions/<ver>/node_modules/.pnpm/<scope>+<name>@*/node_modules/<spec>`（新版发行号自带）；
3. `versions/<ver>/node_modules/@deepseek-ai/dsh/node_modules/<spec>`（老版发行号自带）；
4. `versions/<ver>/node_modules/<spec>`（直接装进发行号的）。
解析到就 `Provided=true`、读真实 `package.json`（版本/描述/关键词/图标）；
图标：包内文件 → 缓存 → 发行号自带但无 logo 时用 DSH 品牌图标；第三方插件走仓库 logo/头像（网络）。
版本文案：有版本显示版本；没有且 Provided → 「由 DSH 提供」；否则「未装到磁盘」。

## 为什么各实例显示不同（数据不同，不是扫描坏了）
- 123 / ev-project：刚建，profile 里只有 base + web-app；
- pack-Test：**还没有 profile**（从未启动过）→ 「这份实例还没有 profile」；
- pad-op-clone / pad-op-tmp：自己装过插件，能列出各自的插件；
- 本机 DSH：desktop profile 14 个 bundle 全部列出（真图标 + 真版本）。

## 仍显示「未装到磁盘」的 4 个
`@deepseek-ai/dsh-experimental-*` 只存在于 **DSH 桌面端自己的包**（E:\\DSH-App/resources/app.asar），
不在 PAD 的发行号或 profile 里 → 这是真实的“找不到文件”，下一步可从桌面端 asar 里解析（不写死包名）。

## 实拍
`04-local-dsh-final.png`：本机 DSH 14 个插件（图标 + 版本 + 角色标签）
`07/08`：切换实例时的列表（自动化点击有误，以 04 为准）
