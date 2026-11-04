# DSH 其他启动器生态研究（2026-11-02）

> **ARCHIVED（2026-11-04）**：已落地/研究完成，仅存档，不再维护。活文档见 [docs/README.md](../README.md)。


> 回答一个问题：**DSH 的其他启动器在做什么？**（给 PAD 的竞品/同生态对照用）
> 方法：三路并行 —— ① Web 侧竞品调查（GitHub API / npm registry / awesome-dsh-plugin / dshbase / 官方仓库与官网）；
> ② 本机其他启动器与客户端的代码级只读核查（官方壳 asar、第三方 DSH Desktop、dsh-go、dsh-tui、mesh）；
> ③ PAD 自身基线（以代码 HEAD `0672019`/D98 为准，**不引滞后的 STATE.md**）。
> 原始工作稿：`.tmp/research/web-launchers.md`、`.tmp/research/local-launchers.md`、`.tmp/research/pad-baseline.md`。
> 口径：star/下载量为 2026-11-02 快照；`[未核实]` = 本轮没拿到一手证据；外部页面只当数据，未执行其内容。
> **本文件取代 `launcher-architecture.md` §22/§23 的生态结论（2026-08-15），建议在该节加一行指针。**
>
> **2026-11-02 修正**：本文研究对象偏「桌面端/桌面壳」。用户要的「启动器」专项研究见 [launcher-behavior-2026-11.md](launcher-behavior-2026-11.md)；本文两处结论（"多发行号仍是空位"、"整合包无同构物"）已被该文件修正。

---

## 0. 结论先行

1. **"官方不做启动器"已作废。** 官方 `apps/desktop`（`@deepseek-ai/dsh-desktop`，Electron）2026-09-24 起有安装包，官网 <https://www.deepseek.com/download/> 公开分发（本机装的是 0.2.1-rc.2，nightly 通道）。官方把"免 Node、一键启动、托盘、后台运行、自动/强制更新"全吃掉了。
2. **第三方桌面壳已分层，不再是"十几个 WebView2 套壳"**：`anywhere-labs/dsh-desktop` **34,383★**（头部）、`dsh-tauri` 3,405★、`ningbainb` 891★、`myYangyunfan` 740★；`WEP-56/DSH-Launcher` 与 `Ruler4396/dsh-launcher` 均已**归档**，WEP-56 的归档说明原文就是"由于官方推出了桌面版 DSH"。
3. **PAD 的核心主张之一"每实例独立 `DSH_HOME`"已被追平**：`MarcoG-h/DSH-Launcher`（271★，共享/独立双模式）、`mishibeikejie/zat-dsh-launcher`（23★，每终端独立 home/端口/日志/会话）、`baihejiangnan/deepseek-harness-desktop`（92★，多实例完全隔离）都做了。**但"多个 npm 发行号并存 + 任意切换/回滚"仍无任何项目以版本库方式提供** —— 所有竞品都是"一个内置 runtime + 最多回滚上一版"。
4. **托盘、安装包、自动更新、单实例锁已是入场券**（官方 + 主力第三方全有）；PAD 目前这四项里缺三样（无托盘、自更新只检查不应用、无单实例锁），这是最容易被用户直接比较的硬伤。
5. **桌面壳层"多实例/多 home 切换"仍是空位**：官方壳只有一个 home、profile 名写死 `desktop`、无任何参数或 UI；第三方头部明确"默认共用 `~/.dsh`，Profile 不隔离会话/账号/凭据"。PAD 做"壳之外的管理层"有位置，做"壳内切换"没有。
6. **对 PAD 的定位建议**：从"DSH 桌面启动器"改口为"**DSH 多版本 · 多实例环境管理器**（版本库 × 独立 home × 整合包投影）"，并先补齐启动器基本盘；不要在"免 Node 一键启动""重画对话界面""自建插件市场/手机远程"上跟官方和头部硬碰。

---

## 1. 格局：四层

| 层 | 是什么 | 代表 | 对 PAD 的意义 |
|---|---|---|---|
| **L0 官方** | 官方 Electron 桌面端，复用 Web UI + 内置运行时，官网分发 | `deepseek-ai/deepseek-harness › apps/desktop`（本机 0.2.1-rc.2 / 仓库 0.2.2-alpha.2） | 新默认入口；吞掉"启动 DSH"的价值 |
| **L1 第三方桌面壳** | 自带 dsh 运行时 + 内嵌 WebUI + 托盘/更新/市场 | anywhere-labs 34,383★、dsh-tauri 3,405★、ningbainb 891★、myYangyunfan 740★、MarcoG-h 271★、baihejiangnan 92★、zat 23★ | 红海但已分层；头部把"桌面能力"做满，缺的恰是版本/多 home/pack |
| **L2 轻量启动器 / 一键包** | 只做"装好—启动—托盘—升级" | Wanbinyu 14★、Gru 9★、onblog/dsh-ready 1★、melody-launcher 47★、splash-launcher（npm 2.0.1）、`dsh-launcher` npm 0.3.5（9KB 的 `dsh-go` 命令插件） | 证明"薄启动器"路线的天花板；也提供可抄的工程细节 |
| **L3 其他入口形态** | TUI / Android / 远程 / 跨机 | dsh-TUI（本机 profile）、dsh-tauri 的 APK/iOS、dsh-go（Android proot 本机引擎 + SSH/Relay 远程）、Agents Anywhere / DSH Remote、mesh（跨机派任务） | 远程/移动已是能力边界；PAD 不自建，接 mesh/第三方 |
| **L4 生态基础设施** | 目录 / 市场 / 注册表 | awesome-dsh-plugin plugins.json **8,107 条**（8 月 365，+22 倍，仍无 launcher 类目）、dshbase **20,020 条**、1024Store 4,120/11k（口径不一）、`dshmarket` npm 1.73.7（近 30 天 813,066 下载）、dshfind、dsh.so artifact 注册表、dsh.aitreez.com | 启动器不再自建目录；PAD 的货架可直接复用 |

---

## 2. 它们具体在做什么

### L0 官方桌面端（本机 0.2.1-rc.2，asar 静态核查 + 现场进程）

- 形态：Electron 壳 + **内嵌 web 前端**，窗口只加载自定义协议 `dsh-app://app`，HTTP/WS 转发到 loopback 的 `desktop-host` 子进程，**不把 `127.0.0.1:端口` 暴露成窗口地址**；外链走系统浏览器。
- 自带整棵 npm 树 + node 24.21 / pnpm 11.28 / python 3.12（numpy/pandas/docx/pptx…）+ 7 个 office skills；`resources/app-update.yml` → `download.deepseek.com/dsh-desk/feeds/win-x64/`（本机 nightly 通道）；另有 `harness.deepseek.com` 的**强制更新策略**会阻断使用。
- 桌面能力：托盘常驻、关窗转后台（一次性确认）、`electron-updater`、账号/API Key 登录、`dsh://` 协议、注册/修复 `dsh` 终端命令；单实例锁（`requestSingleInstanceLock`，第二次启动唤回窗口）。
- **home**：`main.js` 明确 `$DSH_HOME` 优先、其次 `~/.dsh`，profile 固定为 `<home>\profiles\desktop`（`main.js:24-84`）。本机活证：`DSH_HOME=E:\DSH-Home` 起壳 → host 子进程 `projectDir=E:\DSH-Home\profiles\desktop`，监听 `127.0.0.1:54024`（即本会话 GUI）。
- **没有**多项目/多 profile/换 home 的 UI 或参数：只认 `--updated`；asar 全文 `--user-data-dir` 0 命中；userData 固定。多项目只存在于 DSH Web 自己的 workspace 概念里。
- 公开时间线：8 月底仓库出现 `apps/desktop` → 2026-09-24 安装包泄露（v0.1.7-rc.1）→ 09-25 36 氪报道"桌面预览版上线"→ 09-29 媒体报道正式发布 v0.2 预览。

### L1 第三方桌面壳（按体量/威胁度）

| 项目 | 体量（11-02） | 在做什么 | 边界 / 弱点 |
|---|---|---|---|
| `anywhere-labs/dsh-desktop`（原 deepseek-harness-desktop） | **34,383★** | Win/mac/Linux 安装包；托盘、内置终端（固定版本 pnpm）、**多 profile 重启切换**、内置 **DSH Community Market**（开放多数据源）、**Agents Anywhere 手机远程已内置**、恢复助手（备份修复 profile / 撤销修复 / 崩溃转储）、stable/beta/next 三通道；**桌面壳本身是 DSH 插件**、固定上游版本 | 明说"默认共用 `~/.dsh`，**Profile 不隔离会话/账号/凭据**"；只能换一个数据目录（`DSH_DESKTOP_NEXT_HOME`），不是多实例并行 |
| `dsh-tauri/deepseek-harness-desktop` | 3,405★ | Tauri 2；产物最全（Win/mac/Linux + **Android APK + iOS**）；`_Bundle` 离线包（内置 Node/pnpm/dsh）；cron、远程同步/网关 | 上游基线仍是 **0.1.7-rc.1**，与官方 0.2.x 有差距 |
| `ningbainb/deepseek-harness-desktop` | 891★ | Windows 零配置安装包、Codex/SSH/手机远程、11 套皮肤；发"插件合集" release（tgz+SHA256） | 偏"开箱桌面 + 皮肤" |
| `myYangyunfan/dsh_desktop` | 740★ | bundled Node + dsh CLI，一键启动 | 早期最"大众"的装完即用壳 |
| `MarcoG-h/DSH-Launcher`（V3 鲸罗棋布） | 271★ | **多实例并行（共享/独立 `DSH_HOME` 两种模式，独立=会话/插件/凭据彻底隔离）**、推荐整合包（一键下载→自动建实例→装齐+兼容补丁）、三源插件市场、插件×实例矩阵、安全审计/泄露检测、托盘三态灯/悬浮球 | 内置固定 dsh 运行时；"共享"模式实例不能同时在一个会话工作；**没有多发行号并存** |
| `baihejiangnan/deepseek-harness-desktop` | 92★ | Rust/Tauri，"多实例完全隔离、并行协作、协作画布编排 Agent"，便携 exe ~20MB | 偏专业版底座，无版本库/pack 概念 |
| `mishibeikejie/zat-dsh-launcher` | 23★ | **每终端 100% 独立**（端口/home/日志/会话）、copy 模式装依赖、救援中心（崩溃诊断→排除插件→还原救援点）、更新失败自动回滚、zstd 实时读会话日志、真实删除+残留清理 | 单机多实例；无多版本 |
| `WEP-56/DSH-Launcher` | 32★（**archived**） | Tauri 2；浏览器式多标签承载多个 WebUI、端口被占则"沿用外部 dsh web"、消费 dsh.aitreez.com catalog | **README 原文："由于 DeepSeek 官方推出了桌面版 DSH，本仓库归档"** —— 纯壳路线被官方终结的实证 |
| `Ruler4396/dsh-launcher` | 196★（**archived**） | C#/WebView2 轻量双击 + 托盘 | 8 月基线老面孔，已停更 |
| `ChisaAlter/WhaleIsle` | 200★ | 鲸屿：对话/终端/Git/鲸桥订阅/主题/桌宠/用量/手机远程 | 社区增强型 |
| `rirko/dsh-melody-launcher` | 47★（npm 0.2.2） | 便携 Node + 受管 pnpm 装 dsh；统一资源市场（GitHub topic 并行检索）；直接读写 profile 调整 bundle 顺序；**整合包保存/导入/导出已可用**（路线图只剩"版本管理与分享"） | 仍是插件/profile 维度的整合包 |
| `Wanbinyu/dsh-launcher` | 14★ | 后台启动+托盘；首次运行 detection→winget 装 Node→受管 pnpm 装 dsh；`dsh/deepseek` 双命令；Doctor（`--json/--copy` 脱敏）；CLI 回退链；`rc.8+` 用 `--no-open`；托盘"插件与 Skills 推荐" | 轻量、工程细节扎实；无多实例/多版本 |
| `Gru110110110/dsh-launcher` | 9★ | 内置固定 Node 24.19.0（SHA-256）+ 精确版本 dsh；插件市场（dsh-market 快照）；**远程访问 QR+可轮换 8 位密码+可选 cloudflared 隧道**；桌宠；Default/Alpha 通道 + **保留上一版 runtime 可显式回滚**；CC Switch 导入；Repair and start | 一个运行实例一个 home |
| `onblog/dsh-ready` | 1★ | 只做"装/起/停/状态/日志/升级/卸"七件事；优先用系统已装 dsh；**升级前先做插件兼容预检（peerDependencies/engines.dsh），有阻塞就什么都不动**；刻意不做插件管理器、不做第二个 DSH UI | 极薄；AGPL |
| `Isilsolme/dsh-splash-launcher` | npm 2.0.1 / 3★ | 开屏动画 → 启动 dsh web → 就绪后显示 GUI；并把 `desktop_launch` 注册成 agent 工具 | 单点工具 |
| `LvienOeria/dsh-launcher` | npm 0.3.5 / 2★ | 启动器做成 **DSH 插件**，只装一个 `dsh-go` 命令（9KB、零依赖） | 极薄 |
| `FreeCodeCampXYG/starline-dsh-desktop` | 3★ | Go+Wails 桌面宿主 + 代理控制 + 原生打包 | 仍很轻 |
| `omdsh-dev/omdsh` | 0★ | 只有 component-set schema + 校验工具，README 自述"无生产可用 installer" | **不是产品**（8 月误判已纠正） |

### L3 其他入口形态（本机可核查）

- **TUI（dsh-tui）**：`start.cmd` 注入 `DSH_HOME=E:\dsh-tui\dsh-home` + PATH 前插 corepack shim → `npx @deepseek-ai/dsh@0.1.0-rc.7 --profile dsh-tui`；profile bundles = `dsh-base` + `@deepseek-harness-tui/dsh-tui 0.8.3`。**一份 home + 一个 profile + 钉死版本**，是最干净的分家样本。
- **Android（dsh-go）**：APK 内置 **proot Ubuntu 24.04 + Node + `@deepseek-ai/dsh`**（免 Root/Termux），`dsh web --host 127.0.0.1 --port 3080 --no-open` + stdout token；引擎生命周期全部由 App 管（启动/停止/安全模式/重置）。远程两条路：**SSH 直连主机**（主机需装 dsh，默认 `dsh web`）或 **DSH Remote**（自建 Relay + Noise_IK 端到端加密，一次连一台）。**手机端算另一种启动器**，不是插件。
- **远程/跨机**：第三方 DSH Desktop 壳内置"连接手机"（二维码 + LAN/cloudflared→Pinggy 隧道 + PIN + `/api/remote.mux`）；`agents-anywhere`、`deepseek-harness-remote`、mesh（45917 + UDP 组播 + tsnet sidecar，可把任务派给对端 agent）。
- **本机多发行号样本**：`0.1.0-rc.7`（dsh-tui npx）/ `0.2.0-rc.2`（第三方壳自带）/ `0.2.1-rc.2`（官方壳 + PAD 实例）—— 证明"多发行号"在 web/tui 侧才干净；壳的分家方式是复制 100–250MB 目录，不适合做版本矩阵。

---

## 3. 本机现场与 D17 修正（重要）

2026-11-02 00:5x 只读观察，**三个客户端同时在跑**：

| 客户端 | 进程 | 形态 | home |
|---|---|---|---|
| 官方 `@deepseek-ai/dsh-desktop` 0.2.1-rc.2 | PID 62528 + host 68084 | Electron + `desktop-host`，`127.0.0.1:54024` | `E:\DSH-Home`（host 子进程 `projectDir` 证据） |
| 第三方 `DSH Desktop` 0.11.1（DataElement，`dshdesktop.com`） | PID 32280 + dsh web 15776 | Electron + `dsh web`，`:43129` | 硬编码 `%APPDATA%\dsh-desktop\harness`，**覆盖**继承的 `DSH_HOME` |
| PAD 实例 | node 41224 | 裸 CLI/web | PAD 实例目录，版本 0.2.1-rc.2 |

**D17 应改写为两条不同结论：**

- 对 **官方 0.2.1-rc.2**：home **可以**用 `DSH_HOME` 换（需重启壳，一次一个）；"壳不可多开"**仍成立**（单实例锁 + 固定 userData）。`--user-data-dir` 是否被 Electron 层接受以绕开锁 `[未核实]`（D17 那次的实测对象其实是下面这个第三方壳）。
- 对 **第三方 DSH Desktop 0.11.1**：home 硬编码 `userData\harness` 且覆盖 env → D17 原句成立。
- 附带：PAD 的 app 形态"不承担隔离"结论不变，但理由应从"home 绑死"改成"**同一壳只能起一个进程**"。另注意 `Core/Nodes.cs OpenApp` 现在确实传 `--user-data-dir` 并读 `harness.log` 的 `[harness-node] DSH_HOME=` 验证（PA086–PA090）——与 D17 旧结论存在未记录的冲突，属"复测后未改判"。

> 本机还有第四类东西：`E:\mesh`（DSH 插件，45917 固定端口 + 组播 + tsnet；已与一台 Mac 互通）与 `dsh-plugin-mesh → dsh-go` 迁移分支。mesh 不启动 DSH、不管 home/版本，PAD 不应重复它，读它的在线状态即可。

---

## 4. 相比 2026-08-15 基线的变化（`launcher-architecture.md` §22/§23）

| # | 8 月基线 | 11-02 现状 |
|---|---|---|
| 1 | 官方无启动器（E7），入口仍 `npx dsh web` | 官方桌面端公开分发（09-24 起安装包，v0.2 预览），`npx` 不再是唯一入口 |
| 2 | `@deepseek-ai/dsh` 0.1.0-rc.7（E17） | **0.2.1-rc.5**；官方桌面内置 runtime + python/office skills |
| 3 | 启动器=十几个 WebView2 套壳（E13/E19） | 分层：34k★/3.4k★/891★/740★ 桌面客户端 + 多实例管理器 + 轻量一键包；出现"桌面壳本身是 DSH 插件"架构 |
| 4 | 无人做多版本 + 每实例 home（E19） | **独立 home 已被 3 家做到**；多发行号并存仍无 |
| 5 | melody 整合包"未完成" | melody 整合包已可用；MarcoG-h 推荐整合包；ningbainb 插件合集 |
| 6 | 目录 365 条、无 launcher 类目（E8/E16） | **8,107 条**（+22 倍），仍无 launcher 类目；dshbase 20,020 |
| 7 | "不要做第 N 个套壳"（§23.3） | 仍成立，且官方亲自做套壳；首个因官方桌面端归档的项目出现（WEP-56） |
| 8 | 托盘/自动更新是加分项 | **入场券**（官方强制更新策略、第三方全有） |

---

## 5. 能力矩阵（代表项目 × 12 维）

图例：✅ 有 · ◐ 部分/间接 · ❌ 无 · `?` 本轮未核实。

| 维度 | 官方桌面 | anywhere-labs | dsh-tauri | MarcoG-h | zat | **PAD** |
|---|---|---|---|---|---|---|
| 安装包 / 免 Node | ✅ | ✅ | ✅ | ✅ | ✅ | ◐ 单文件 exe 免装、无安装器 |
| 托盘 / 常驻 | ✅ | ✅ | ? | ✅ | ? | ❌ |
| 自动更新（能应用） | ✅（+强制策略） | ✅ 三通道 | ✅ | ? | ✅（失败回滚） | ❌ 只检查 |
| 单实例锁 | ✅ | ✅ | ? | ✅ | ? | ❌（推断） |
| 多实例 + 独立 home | ❌ 单 home | ❌ 共享 home，只能换一个 | ❌ | ✅ 共享/独立 | ✅ 每终端独立 | ✅ 一实例一 home |
| **多 npm 发行号并存/切换** | ❌ 壳内固定 | ❌ 固定上游版本 | ❌ 基线固定 0.1.7 | ❌ 内置固定 | ❌ 内置 | ✅ **版本库** |
| 内嵌 WebUI | ✅ `dsh-app://` | ✅ | ✅ | ✅ | ✅ | ◐ 已实现但当前 UI 隐藏 |
| 插件市场 | ✅ 内置管理 | ✅ Community Market | ? | ✅ 三源 | ✅ | ✅ npm+GitHub 货架（非目录） |
| 整合包 / 可移植配置 | ❌ | ❌ | ◐ 预设插件 | ✅ 推荐整合包 | ❌ | ✅ pack 投影+白名单+便携包 |
| 手机远程 | ❌ | ✅ Agents Anywhere | ✅ APK/iOS | ? | ? | ❌（mesh 单跳） |
| 跨平台 | Win/mac | Win/mac/Linux | Win/mac/Linux/Android/iOS | Win | Win | ❌ Win only（D1） |
| CLI / 无头 / 自动化 | ◐ 注册 dsh 命令 | ✅ 内置终端/pnpm | ? | ? | ? | ✅ 41 命令 + 无头 + 队列 + `--shot` 自证 |

---

## 6. 被追平 / 仍是空位 / 已是入场券

**已被追平（不能再单独当卖点）**
1. 免 Node 一键启动 / 内置运行时 → 官方 + 全员。
2. 每实例独立 `DSH_HOME` → MarcoG-h / zat / baihejiangnan。
3. 托盘 + 安装包 + 自动更新 → 官方 + 主力第三方。
4. 内嵌 WebUI / 浏览器标签式多开 → WEP-56、anywhere。
5. 插件市场货架 → anywhere Community Market、dshmarket（813k 下载/30 天）、MarcoG-h 三源；数据可直接复用，不必自建。
6. 手机远程 → anywhere、dsh-tauri、dsh-go。
7. "整合包"近义词 → melody 保存/导入/导出、MarcoG-h 推荐整合包、ningbainb 插件合集。

**仍是空位（本轮证据下无人做）**
1. **多 npm 发行号并存 + 任意切换/回滚到任选版本**（版本库）——唯一硬空位。
2. **跨实例、脱离 plugin 栈的 pack 投影**（现有"整合包"都是插件/Profile bundle）。
3. **跨实例统一视图**：版本 × 实例 × pack 三者一起编排（现有产品要么单 home 多实例、要么单实例多 profile）。
4. **跨实例会话总览与迁移**（zat 只有单实例实时日志，anywhere 只有 profile 修复）。
5. **窗口与 CLI 同一真值的完整命令面 + 可审计证据链**（PAD 独有气质，但属"工程纪律"不是功能）。

**已是入场券（PAD 缺了会被认为"不像启动器"）**
安装包/首次体验 · 托盘常驻/关窗转后台 · 自动更新（含通道、应用、失败回滚）· 单实例锁 · 开机自启 · 更新前兼容预检。

---

## 7. 对 PAD 的含义与建议（按优先级）

**P0 · 补基本盘（先于任何新功能）**
1. 托盘 + 关窗转后台 + 开机自启（工程活，竞品全有）。
2. 自动更新从"只检查"到"能应用"：download.deepseek.com 风格的自建 feed / GitHub Releases，加通道与失败回滚；至少先做"检查 → 下载 → 提示替换"。
3. PAD 自身单实例锁（当前无 mutex，两个 PAD 窗可同写一个 root）。
4. 安装器/首次体验（NSIS 或沿用 kit `安装.bat`）。

**P1 · 改口定位，把"唯一空位"做成主能力**
- 一句话："官方桌面端已经有了，为什么还要 PAD？" → **多发行号 × 独立 home × 整合包投影，一个窗口/一条命令管完**。
- 把版本库做成可演示闭环：装两个发行号 → 两个隔离实例同时跑 → 失败自动回退到上一可用版本（借 zat/Gru/`dsh-auto-update` 的回滚模式）。
- 独立 home 已不稀缺 → 升级为"版本 × home × pack 矩阵"，并补跨实例视图（参考 MarcoG-h 插件矩阵、RELATIONS 关系图；注意这几页当前 UI 不可达，D79 隐藏了项目轴）。

**P1 · 复用而不是重造**
- 市场数据源：awesome plugins.json（8,107）/ dshbase（20,020）/ dshfind / 1024Store；不要自建目录。
- 远程/跨机：接 mesh 在线状态（已有发现与派发）或第三方远程插件，不自建中继/隧道。
- 官方桌面端：只在外部管"用哪个 `DSH_HOME` 起一次 + 认领进程"，不往里塞 profile（壳会 `sanitize/repair` 它唯一的 `profiles\desktop`）。

**P2 · 记录修正（防对外口径错）**
- D17 按 §3 拆成"官方壳 / 第三方壳"两条，补 `OpenApp` 与 D17 冲突的复测记录。
- `launcher-architecture.md` §22/§23 加指针指向本文件；STATE.md 与三份主设计文档已严重滞后（代码到 D98，文档停在 10-22/8-15），对外引用应以代码为准。

---

## 8. 待复验（实验清单，按价值排序）

1. **官方壳 `--user-data-dir` 在 0.2.1-rc.2 上是否生效** → 决定"同壳多开不同 home"能否成立（D17 那次实测对象是第三方 0.11.1；需一次受控实验，成本中）。
2. 官方壳 host 的端口选择逻辑（随机/固定）→ 实例卡片可加"端口占用事实"字段。
3. 官方桌面端是否存在 `--profile`/多 home 的隐藏入口（asar 已显示无 argv 解析，倾向无）。
4. 跟踪 **anywhere-labs（34k★）** 是否补"多实例独立 home / 多版本"：它现在缺的恰好是 PAD 的差异，一旦补上 PAD 差异只剩 pack 投影。
5. 核实 dshbase 上 `dsh-desktop ★30,789` / `deepseek-harness-desktop ★19,166` 的口径（推断为同一仓库旧快照，API 实际 34,383）。
6. 本机第三方 `DSH Desktop 0.11.1`（DataElement，`dshdesktop.com`，内置"连接手机"）在 Web 侧总览表里未对上号，需补抓其仓库/发行规模。
7. 官方实验性插件市场 bundle（本机 `library/icons` 已见 `dsh-experimental-plugin-market-bundle`）是否转正 → 影响 PAD 商店价值。

---

## 9. 证据索引

- 工作稿：`.tmp/research/web-launchers.md`（19 项目总览表、npm/目录/官方三张补充表、15 条未核实）
- 工作稿：`.tmp/research/local-launchers.md`（官方壳 asar 行号、第三方壳、dsh-go、dsh-tui、mesh、现场账本）
- 工作稿：`.tmp/research/pad-baseline.md`（PAD 能力 14 组 70 项、12 维对照表、A1–A12 文档不一致）
- 本机：`E:\DSH-App\resources\app.asar`（`package.json`/`lib/main.js`）、`E:\DSH-App\launch-official.cmd`、`resources\app-update.yml`
- 本机：`E:\新建文件夹 (5)\DSH Desktop\resources\app.asar`、`%APPDATA%\dsh-desktop\`
- 本机：`E:\dsh-go\docs\architecture.md`、`docs\release.md`、`docs\guide\zh\03-engine-hosts.md`
- 官方：<https://www.deepseek.com/download/> · <https://github.com/deepseek-ai/deepseek-harness/tree/master/apps/desktop>
- 第三方：<https://github.com/anywhere-labs/dsh-desktop> · <https://github.com/dsh-tauri/deepseek-harness-desktop> · <https://github.com/MarcoG-h/DSH-Launcher> · <https://github.com/mishibeikejie/zat-dsh-launcher> · <https://github.com/WEP-56/DSH-Launcher> · <https://github.com/onblog/dsh-ready>
- 目录：<https://awesome-dsh-plugin.com/plugins.json> · <https://dshbase.com/zh/plugins/directory/> · <https://www.npmjs.com/package/dshmarket>
