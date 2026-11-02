# D110 · 插件商店 & 排行榜：别人怎么做（截图参考）

日期：2026-11-02｜用途：重做 PAD 插件商店/增长榜前的外部参照，**不自己发明层级**

## 1. 参考站点与截图

| # | 站点 | 截图 | 关键结构 |
|---|---|---|---|
| 01 | Modrinth（Minecraft 模组/插件市场） | `01-modrinth.png` | 单页扁平列表 + 顶部 `Sort by: Downloads` 下拉 + 左侧版本过滤；每行：图标、名称/作者、描述、⬇下载数、♡关注、更新时间、标签 |
| 02 | VS Code Marketplace | `02-vscode-marketplace.png` | 搜索 + `Sort By: Installs`；扁平卡片网格：图标、名称、发布者、⬇ 244M、评分、FREE |
| 03 | JetBrains Marketplace | `03-jetbrains-marketplace.png` | 搜索 + 过滤（All/Paid/Free、Plugins/Themes、Tags）+ 右上 `Sort by: Downloads`；扁平卡片/列表 |
| 04 | Obsidian 社区插件 | `04-obsidian-plugins.png` | 左侧 facet 计数（TYPE/CATEGORIES/PAYMENTS），右上 `List/Cards` 切换 + `Popular` 排序；结果扁平 |
| 05 | Apple App Store（网页 Today） | `05-appstore-today.png` | 左侧分类导航 + 大图推荐卡（Today 流） |
| 06 | Apple App Store（Charts） | `06-appstore-charts.png` | `免费/付费` 分段 + 排行榜**平铺卡片网格**：左上大号名次、图标、名称、一句副标题、查看 |
| 07 | Steam 排行榜 | `07-steam-charts.png` | 单张扁平表：`排名 / 名称 / 价格 / 变动(▲16) / 周`；左侧切换时间窗（实时/每周/月） |
| 08 | DSH 整合包市场（自家生态） | `08-dsh-pack-market.png` | 搜索 + 分类 chips + 平铺卡片；卡片带版本/包数/依赖数 |

来源 URL：

- https://modrinth.com/discover/mods
- https://marketplace.visualstudio.com/search?target=VSCode&sortBy=Installs
- https://plugins.jetbrains.com/search?orderBy=downloads
- https://community.obsidian.md/search?type=plugin
- https://apps.apple.com/cn/iphone/today
- https://apps.apple.com/cn/iphone/charts
- https://store.steampowered.com/charts/topselling/global
- https://dsh-packforge.github.io/dsh-pack-market/

## 2. 共同模式（可以照抄的）

1. **排行榜不是单独页面，是商店的「一种排序/视图」**：Modrinth `Sort by: Downloads`、VS Code `Sort By: Installs`、JetBrains `Sort by: Downloads`、Obsidian `Popular`。用户还在同一个列表里，只换排序。
2. **结果永远平铺**：列表或卡片网格，一行放不下就换行/滚动；没有「先分区、再标签、再横向轮播」的嵌套层级。
3. **单行信息密度固定**：图标 + 名称/作者 + 一句描述 + 指标（下载/关注/评分）+ 时间/标签；排行榜额外给 **名次** 和 **变化量**（Steam 的 ▲16 / ▼2）。
4. **顶级筛选是分段控件/下拉，不是新页面**：App Store `免费|付费`、Steam 时间窗、Obsidian `List|Cards` + `Popular`。
5. **Apple Charts 的做法**：排名直接印在卡片左上角，卡片本体就是普通 App 卡；没有“排行榜专区”这种二级入口。

## 3. 我们之前做错的

| 错误 | 别人怎么做 |
|---|---|
| 单独「排行榜」货架/页面 | 排行榜 = 商店里的排序模式（Modrinth/VS Code/JetBrains） |
| 页 → 排行榜分区 → 标签 → 横向轮播 四层 | 一层：商店列表；控制项在工具栏 |
| 顶部塞一大块榜单把搜索/卡片挤下去 | 榜单与普通结果同列表，靠排序切换 |
| 自造 App Store 样式图标块/手搓布局 | 复用商店现有卡片模板，只加名次/变化量字段 |

## 4. 对应到 PAD 的最短方案

1. `SortBox` 增加 `Star 增长` / `下载增长` 两个排序项（和现有 热度/最近更新/名称 同级）。
2. 选中增长排序时，**同一个 `ResultList`**（现有 WrapPanel 卡片网格）改成显示增长榜数据：卡片模板复用，只多一个「#名次 + 增长值」徽标；分页条切换成「Top N」。
3. 周期用工具栏里一个现有风格的 `ComboBox`（日/周/月），不加分区、不加轮播、不加独立入口。
4. 点卡片仍走现有 `PluginDetailWindow`。

这样：**一个商店页、一层列表、平铺换行**，和 Modrinth / VS Code / App Store Charts 一致。

## 5. 草图（方案 A：增长 = 排序模式）

`09-mockup-sort-mode.png`

- ① `排序` 下拉在原「热度 / 最近更新 / 名称」下面新增 `Star 增长` / `下载增长`
- ② `日 / 周 / 月` 用工具栏现有控件（分段 chip）
- ③ 还是同一个结果卡片网格，换排序只换数据；卡片放不下自动换行
- ④ 卡片只多「名次 + 本期增长」两处信息
- 无新增页面、无新增分区、无横向轮播；点击卡片仍走现有 `PluginDetailWindow`

草图数据：真实 `downloads-week` 榜（dshmarket +186,498 等），仅用于比例/密度参考。

## 6. 高保真稿（视觉方向）

`10-mockup-beautiful.png`

布局仍是「一层商店 + 平铺卡片」，但视觉按 Apple 质感重做：

- 字体层级：页标题 33 / 区块 17 / 卡名 14.5 / 正文 12 / 元信息 10.5，标题加粗
- 空间：内容边距 26–30、卡片间距 14、卡内 16，整体留白加大
- 颜色：暖灰底 `#F3F5F6`、纯白卡片、PAD 青 `#0E5A5F`、增长绿 `#1C6B4A`
- 卡片：17px 圆角、1px 极浅描边 + 双层柔和阴影、52px 渐变图标块、右上名次徽章、底部版本/更新时间/增长值
- 顶部：搜索胶囊 + 分段 chip（全部 / Star 增长 / 下载增长 · 日 / 周 / 月），无嵌套卡片
- Hero：本周增长最快的插件做一张渐变推荐卡（Apple Today 式），下面是平铺增长榜
- 去掉分页浮层、去掉卡片套卡片、去掉横向轮播

## 7. 已实现（PAD 下载页 · 插件商店）

- `11-store-home.png`：**全部插件（首页）**——保持卡片网格：33px 标题 + 搜索胶囊 + 分段 chip + 3 列大卡，真实插件图标
- `12-store-growth-list.png`：**增长模式**——改成榜单式平铺列表：56px 真实插件图标、名次、作者/分类、一句描述、右侧本期增长 + 本期总量；没有 Hero 横幅
- `13-store-rail-collapsed.png`：侧栏收起（左缘 `›` 重新展开）

结构决定：

1. 全部插件（首页）= 卡片网格，浏览用；不塞 Hero/榜单
2. Star 增长 / 下载增长 = 同一个商店页的榜单模式，单列平铺列表，点击行走同一个插件详情窗
3. 日 / 周 / 月 周期 chip；数据缓存 6h
4. 所有图片走 `PluginMark`（真实插件图标），没有字母占位块
5. 侧栏可收起；视觉 token 与高保真稿一致

验证：`dotnet build` 0/0，`dotnet test` 42 项通过，UI 契约测试通过。
