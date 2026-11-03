# D107 安装概念 UI（task-12）

用户 2026-11-04 批准的原型：`agent-pack-dsh/docs/proposals/install-concept/`（01 版本页安装▾ /
02 实例页包库选择器 / 03 预检+报告）。本目录是 Electron 外的新 build 离屏截图（`--shot` 渲染，
非屏幕捕获），scratch root `E:\pack-agent\.tmp\d105-ux`（未动 live root）。

## 实现
1. 版本选择页（`Views/VersionsView.xaml(.cs)`）：整合包行右侧单按钮改「安装 ▾」→ ContextMenu 三项：
   - 装到当前实例（左栏选中的实例；无选中禁用）→ PreflightPack → 授权窗口 → InstallPackToInstance；
   - 新建实例… → 保留 D104 的 InstallPackAsync（完整 import 链）；
   - 先检查能不能装… → 只弹预检窗口（DryRun/checkOnly，不写盘）。
2. 实例页（`Views/InstanceView.xaml(.cs)`）：「装整合包…」改开 `PackPickerWindow`：
   目标实例写标题；`Packs.Catalog(AppContext.BaseDirectory, …)` 异步加载（骨架/空态）；
   每行 PackIcon 图标、名字/版本、内核+插件数、后台逐行 PreflightPack 徽标（检查中… / 可装 /
   N 个需授权 / N 个已下架/缺失 / N 个版本冲突 / 未知 / 已装到当前实例 / 缺 N 可修复）；
   已装且缺件行动作变「修复」（RepairPack）；底部「从文件…」→ 旧的 Import_Click（ApplyPackToInstance）、取消。
3. 新 `Views/PackPreflightWindow.xaml(.cs)`：两阶段同窗。阶段 1「安装前检查」= 内核/管理器/插件分类
   汇总/逐条 needs-exemption、missing、version-conflict、内核警告 + 两个复选框（接受 N 个兼容风险 /
   跳过 M 个下架；无对应项隐藏）+ 取消/开始安装；阶段 2「安装报告」= 状态药丸（已装到 / 不完整·缺 N）、
   5 个统计（新增/保留/豁免/跳过/失败）、逐条 added/exempted/kept/skipped/failures/warnings、
   知道了 / 重新导出这个包 / 打开实例 / 去修复缺少项。
   共用流程 `PackPreflightWindow.RunInstallFlowAsync`（版本页与实例页同一份，勾选 →
   `PackInstallOptions{AcceptRisk, SkipMissing}`）。
4. shot hook（`MainWindow.xaml.cs` 仅 shot 分支）：`--shot-page pack-picker`（真实包库+真实逐行预检）、
   `pack-preflight`（真实 PreflightPack 阶段 1）、`pack-preflight-report`（阶段 2，固定 fixture，
   保证可复现）。这三页不再走主窗口 ScheduleShot（避免覆盖截图）。
5. 旧路径保留：实例页 Import_Click（从文件）、版本页 InstallPackAsync（新建实例）、
   D106「版本页不显示装到哪」不变。

## 截图（本目录，新 build + scratch root `E:\pack-agent\.tmp\d105-ux\.pack-launcher`）
- `versions-install-menu.png`：版本页 3 行整合包（IconPack 自有 icon / PlainPack 回落 glyph / Test
  带 icon），每行右侧「安装 ▾」；不再显示「已装到/已建实例」。
- `instance-packs-glow.png`：「装整合包…」按钮 + 已启用包行发光、停用行无光（D105 UX 保留）。
- `pack-picker.png`：真实包库 3 个（Icon Pack / Plain Pack / Test），真实预检徽标：
  Test = 10 插件、1 个需授权(warn) + 1 个已下架/缺失(bad)、按钮「检查并安装」；另两个「可装/安装」。
- `pack-preflight-stage1.png`：真实预检（Test → ux-demo · web）：内核匹配通过、管理器未装自动补、
  插件 10 个（8 可装/1 需授权/1 已下架）、版本冲突无、警告行、逐条豁免/下架详情、两个授权复选框、
  取消/按勾选开始安装。
- `pack-preflight-stage2.png`：安装报告固定 fixture（与原型 03 右卡数字一致：9 新增/0 保留/1 豁免/
  1 跳过/0 失败，已装到 + 不完整·缺 1，跳过 dsh-bridge-browser，按钮 知道了/重新导出/打开实例/去修复缺少项）。

## 与原型差异（诚实记录）
- 原型是页面内浮层（背后有半透明暗场）；实现是独立无边框 Window（内部带暗场 + 白卡），
  尺寸 1060×680 / 1020×700，离屏渲染时暗场外是透明边。
- WPF ContextMenu 不做两行「标题+说明」；菜单项用 ToolTip 放原型副标题，菜单项文字与原型一致。
- 包库选择器过滤了「没有 dsh.version 内核声明」的包（本机开发 bin 目录里有若干这种 fixture）；
  可装包按内核分组展示。
- 阶段 2 截图用固定 fixture（避免截图流程真的写 scratch 实例）；真实安装后由同一 `ToView(report)`
  映射，失败/跳过逐条原样列出。
- 原型 03 是「左右两卡」并列示意；实现是同一窗口两阶段切换（阶段 1 点开始安装后切阶段 2）。
- 报告阶段多了「知道了」按钮（原型只有 3 个动作，无关闭按钮；无边框窗口需要显式关闭）。

## 未覆盖/风险
- 「装到当前实例」菜单在没有选中实例时禁用并提示先选实例（原型假定已有当前实例）。
- 真实安装的 UI E2E 未在离屏截图里跑（会写 scratch 实例）；API 语义由 p2-core CLI E2E 覆盖，
  UI 侧只保证接线/映射/不静默展示。首次真实点击路径建议 verifier 在 scratch root 上走一遍。
