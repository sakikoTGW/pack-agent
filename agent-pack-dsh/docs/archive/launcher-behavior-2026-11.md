# 启动器在干什么 —— 24 个 DSH 启动器的行为链（2026-11-02）

> **ARCHIVED（2026-11-04）**：已落地/研究完成，仅存档，不再维护。活文档见 [docs/README.md](../README.md)。


> 上一份 [launcher-landscape-2026-11.md](launcher-landscape-2026-11.md) 把「桌面端/桌面壳」和「启动器」混在一张表里，被用户指出口径错误。
> 本文件**只研究对象是启动器的情况**：以「装 DSH / 管 DSH / 启停 DSH」为主业的程序或插件；不把桌面客户端算进来。
> 原始工作稿（24 项目 × 7 段行为链 + 证据）：`.tmp/research/launchers-only.md`。证据为 README/代码/GitHub API 快照（2026-11-02），标注【事实】/【推断】/未核实。

---

## 0. 结论（先看这 5 条，其中 3 条修正了我上一版的判断）

1. **「会启动 DSH」已经没有任何门槛。** 24 个启动器里的事实标准是 5 步：找/装 Node+pnpm → 把 dsh 取到某个位置（npm 装 / 受管 runtime / 便携内嵌 / npx 临时）→ 组 env（至少 `DSH_HOME`，常带 profile/端口/代理）起 `dsh web` → **解析启动输出里的 URL/一次性 token** 做就绪判定 → 交给浏览器或内嵌 WebView（新版流行 token→cookie 桥）。差异全部落在后半段。
2. **修正：PAD 的「多发行号并存」不是空位了。** `dsh-plugins/dsh-launcher`（76★，2026-10-31）明确写 "running multiple DSH versions and instances side by side"：**多版本隔离安装到 `versions/<ver>/`、每版本多实例、三档 `DSH_HOME`（复用/`~/.dsh`/每实例新建）**；`RYun601/dsh-launcher` 有**版本化运行时 + 活动指针 + 任意版本升降（`--upgrade <精确版本>`）+ 回退链**；`hpyer/dsh-for-mac` 每版本一个运行时目录、保留 5 个、健康检查通过才切；`yyh-001/DSH-X`（**770★**）有版本管理面板并保留旧版；`DreamRift`/`wishesl` 按实例指定版本。
3. **修正：「每实例独立 DSH_HOME」已不止 3 家。** dsh-plugins（三档模式最完整）、`x102201/dsh-helper`、DreamRift、baihejiangnan、zat、MarcoG-h 都做了；头部大项目 `DSH-X` 的多实例是否独立 home 仍**未核实**（README 只写"启动项选用户目录/profile/版本/端口"）。
4. **修正：整合包已有事实标准与市场。** `DSH-PackForge` 定义 `.dspack`（manifest v5 / pack v3 / index v2）、launcher registry 和徽章；`DSH-X` 内置 **整合包市场**；`x102201/dsh-helper` 的 `.dshpack` 已做成**可授权交付包**（机器码/密码绑定、禁再导出、每机导入次数、凭据永不入包）。PAD 自造格式会成孤岛。
5. **这一层现在卷的不是"能不能启动"，是"运行环境治理"**：版本保留/回退链、多实例隔离、兼容性预检与崩溃归因、整合包/交付授权、内嵌 token→cookie 桥、以及 CLI/无头化。PAD 的三件套里前两件被分头实现、第三件被规范+市场填掉大半，**剩余差异收窄为「pack 的投影语义 + 统一管理视图 + 回退链体验」的组合**。

---

## 1. 启动器清单（24 个，按能力密度）

| # | 项目 | 形态 | star / 最近更新 | 一句话 |
|---|---|---|---|---|
| 1 | `yyh-001/DSH-X` | GUI + 内嵌视图 | **770** / 2026-11-01 | 启动项与多实例、版本管理、**整合包市场（.dspack）**、MCP/技能 |
| 2 | `dsh-plugins/dsh-launcher` | GUI + 每实例 webview | 76 / 2026-10-31 | **多版本隔离安装 + 每版本多实例 + 三档 DSH_HOME** |
| 3 | `MarcoG-h/DSH-Launcher` | GUI + 内嵌 WebUI | 271 / 2026-09-18 | 「多实例管理中枢」：共享/独立 home 双模式、推荐整合包 |
| 4 | `Ruler4396/dsh-launcher` | 轻量 GUI | 196 / 已归档 | 克制路线（不内置 dsh、1.4MB MSI）；因生态变化停更 |
| 5 | `baihejiangnan/deepseek-harness-desktop` | 多实例管理器 | 92 / 2026-09-24 | 一启动器 N 个完全隔离实例 + 协作画布 |
| 6 | `Miyazawai/DSH-Melody-Launcher-Overture` | GUI | 137 / 2026-10-26 | `rirko` 的 C 端分支（同源） |
| 7 | `rirko/dsh-melody-launcher` | GUI + 插件管理 | 47 / 2026-10-26 | 便携 Node + 资源市场 + **整合包保存/导入/导出** |
| 8 | `loudMore/dsh-launcher` | GUI | 37 / 2026-09-18 | 小白向：三维更新看板、坏插件自动隔离、8 语言 |
| 9 | `WEP-56/DSH-Launcher` | 多标签 GUI | 32 / 已归档 | 浏览器式标签承载多个 WebUI；官方出桌面端后归档 |
| 10 | `mishibeikejie/zat-dsh-launcher` | 多终端 GUI | 23 / 2026-10-17 | 每终端 100% 独立 + 救援中心 + 更新失败回滚 |
| 11 | `Wanbinyu/dsh-launcher` | GUI + 托盘 | 14 / 2026-10-14 | 安装链最完整：winget 装 Node → 受管 pnpm 装 dsh → Doctor 脱敏 |
| 12 | `Gru110110110/dsh-launcher` | GUI + 远程 | 9 / 2026-10-01 | 固定 Node/runtime、Default/Alpha 通道、保留上一版可回滚、手机远程 |
| 13 | `x102201/dsh-helper` | 实例管理器 | 7 / 2026-10-04 | 无限并行实例 + 同一工作区分屏 + **`.dshpack` 授权交付** |
| 14 | `MM071022/dsh-portable` | 便携单文件 | 7 / 2026-08-15 | 自解压 dsh.exe（内嵌完整运行时）+ 按 home 命名互斥锁 |
| 15 | `wishesl/dsh-launcher` | GUI（Wails） | 4 / 2026-10-17 | **不开 DSH 也能管插件/本体**；实例=目录+版本；实例级临时 `--patch` 屏蔽 |
| 16 | `RYun601/dsh-launcher` | **CLI/bat（无 GUI）** | 4 / 2026-10-08 | 版本化运行时 + 活动指针 + 任意版本升降 + **回退链** + 事务式自更新 |
| 17 | `DreamRift/dsh-harness-controller` | GUI 控制器 | 4 / 2026-09-08 | Win+WSL 双实例页、按实例指定 harness 版本、Markdown 诊断报告 |
| 18 | `youridol/dsh-launcher` | GUI + 内嵌 | 3 / 2026-09-22 | npm/GitHub 双通道版本管理、技能/MCP 管理、CLI 子命令 |
| 19 | `refyon/dsh-systray` | 托盘型单文件 | 3 / 2026-10-31 | 后台起 web + 托盘；导出/导入 zip 迁移；失败回退 + 自动拉起 |
| 20 | `Isilsolme/dsh-splash-launcher` | 单文件 splash | 3 / npm 2.0.1 | 开屏动画→起 web→就绪揭示；token→cookie 注入 WebView2 |
| 21 | `FreeCodeCampXYG/starline-dsh-desktop` | 宿主型（灰区） | 3 / 2026-10-01 | 固定版本/离线包；token→cookie 握手；**明确收敛为单实例**（共享 home 会争会话写锁） |
| 22 | `onblog/dsh-ready` | GUI（克制） | 1 / 2026-11-01 | 只做"装/起/停/状态/日志/升级/卸"；**升级前插件兼容预检** |
| 23 | `LvienOeria/dsh-launcher` | DSH 插件形态 | 2 / npm 0.3.5 | 9KB：装一个 `dsh-go` 命令 → 起 harness + 开浏览器 |
| 24 | `hpyer/dsh-for-mac` | macOS 原生 | 1 / 2026-10-28 | 每版本运行时目录保留 5 个、健康检查才切、会话格式修复 |
| 25 | `NevermindZZT/dsh-launcher` | GUI（SSH） | 1 / 2026-11-01 | SSH 远程 + token 只在内存、日志脱敏（其余未核实） |

> 另有 14 个更小的启动器/托盘插件（`DSH-Folk` Android 45★、`dsh-web-startup-auth` 65★、`dsh-tray-launcher`、`dsh-instance-manager` 等）见工作稿 §7.1；`dsh-instance-manager` 做的是"Web 侧实例管理（端口/PID/会话数/内存/版本）"，形态很接近 PAD 的"管理视图"。

---

## 2. 启动那一刻到底执行什么（事实层）

【事实】差异几乎全在这一层：用哪条命令、给什么环境、占哪个端口、怎么认就绪、token 怎么交给界面。

| 项目 | 实际命令/方式 | home / env | 端口策略 | token / 就绪 |
|---|---|---|---|---|
| DSH-X | npm 装的版本 + 管理页/Electron 壳 | 启动项选目录/profile/版本 | 默认 3780，占用顺延 | 未核实 |
| dsh-plugins | 选实例+profile → spawn | **三档 DSH_HOME，启动器注入** | 每实例独立 | 解析 `dsh web` 输出 URL |
| MarcoG-h | `node dsh/lib/bin.js <profile>` | `DSH_HOME=共享或独立` | 每实例独立 | HTTP 200 → 内嵌 WebContentsView |
| baihejiangnan | `$env:DSH_HOME=$fresh; dsh web` | 只注入子进程 | 自动探测 | 端口就绪 → 内嵌 |
| zat | 隐藏控制台启动 | 每终端独立 home | 每终端独立 | 端口探测 |
| Wanbinyu | CLI 回退链（源码→bin→本地→全局→PATH→npx） | 受管 harness root + `DSH_*` env | 3080（可改） | HTTP 就绪 + token 地址提取（内存、脱敏） |
| Gru | 私有 runtime 的 `dsh web` 进程树 | `DSH_DESKTOP_HOME`/`DSH_HOME` | 默认 | 未核实（推测 HTTP） |
| rirko | `npx @deepseek-ai/dsh web`（本地后切 `dsh web`） | 界面配 home/profile/工作目录 | 日志识别地址 | 日志识别服务地址 |
| wishesl | **`npx -y @deepseek-ai/dsh@<version> web`**（cwd=实例目录） | 实例目录；启动时注入临时 `--patch` | 每实例端口 | 解析带 token 地址（界面打码） |
| RYun601 | 版本化 runtime 的 Node 入口 | 活动指针 `runtime-current.json` | 3080；FOREIGN_PORT 不误杀 | HTTP 轮询 900s；`--open` 复用认证入口 |
| youridol | `dsh web`（端口可配） | 全局单版本，不动 `~/.dsh` | 3080 可配 | 内嵌打开自动带 token |
| DreamRift | `dsh web`（指定版本时 `npx @deepseek-ai/dsh@<v>`） | 每实例 home/workspace | 每实例独立 | 解析 `dsh web: http://...` 公告 |
| x102201 | 每实例独立进程 + 物化运行时 | **每实例独立 home** | 每实例独立 | 等端口就绪 |
| starline | `dsh --profile web --host 127.0.0.1 --port <动态> --no-open` | 默认 `~/.dsh` | 动态 loopback | Go 代理做 **token→cookie 握手** |
| Isilsolme | 后台 `dsh web` | 工作目录/端口可配 | 已有服务只开窗 | **token→cookie 注入 WebView2** |
| WEP-56 | PATH `dsh`（或 npx 模式） | Tauri 配置目录 | 3080；被占则**沿用外部服务** | Web 服务可用 |
| MM071022 | `dsh.exe web`（内嵌完整 runtime） | `~/.dsh`；`DSH_EXE_HOME` | 3080 | 端口/浏览器 |
| hpyer | 受管 runtime 的 `dsh` | 受管 runtimes 目录 | 3080 可配 | 健康检查通过才切版本 |

**技术分水岭**：token 处理已经三代——① 直接开浏览器带 token URL（Wanbinyu/DreamRift）；② 换 cookie 再注入 WebView（starline/Isilsolme）；③ 内嵌还需内置插件放宽 `SameSite=Strict`（wishesl）。谁做内嵌视图，谁必须解决这一层。

---

## 3. 这一层在卷什么

### 3.1 已成标配（≥8 家都有，不做就是 bug）
一键装/定位 Node、装/定位 dsh、启停/重启、就绪后自动开浏览器、托盘、开机自启、日志落盘、启动失败提示、安装包或便携单文件、更新检查。

### 3.2 正在卷的中层（新的战场）
1. **版本化运行时 / 回退链**：RYun601（活动指针 + 任意版本升降 + 当前→上一版→旧版回退链，只有通过就绪校验的版本才可用于回退）、hpyer（每版本目录、保留 5 个、健康检查才切）、DSH-X（版本面板 + 保留旧版 + 启动项额外保留当前/上次版本）、youridol（npm/GitHub 双通道任意升降）、Gru（保留上一版显式回滚）、dsh-plugins（多版本隔离安装）。
2. **多实例 + 每实例独立 home**：dsh-plugins（三档）、dsh-helper（+运行时物化）、DreamRift（Win+WSL）、baihejiangnan（硬隔离、共享 home 禁并行）、zat（每终端）、MarcoG-h（双模式）。
3. **整合包 / 交付包**：DSH-X（`.dspack` + 整合包市场）、x102201（`.dshpack` 机器码/密码/禁再导出/导入次数）、dsh-plugins（modpack）、rirko（Modpack 保存导入导出）、MarcoG-h（推荐包+兼容补丁）、refyon（zip 导出导入）、baihejiangnan（Profile/Home 备份）。
4. **兼容性预检 / 崩溃归因 / 救援**：onblog（peerDeps/engines 计划，阻塞不动）、wishesl（按能力探测而不是版本号、三态证据面板）、zat（救援中心：诊断+救援点+重装）、RYun601（不兼容插件列举并询问移除）、youridol（崩溃归因到插件并可逆禁用）、loudMore（坏插件隔离 `.disabled`）。
5. **内嵌视图 + token/cookie 桥**：≥11 家；官方桌面端把"开箱即用"吃满后，第三方靠这个差异活。
6. **插件市场（消费目录，不自造）**：DSH-X、dsh-plugins（dsh-plug.in）、loudMore、wishesl、DreamRift、rirko、MarcoG-h、Gru、WEP-56（aitreez catalog）。
7. **迁移 / 交付 / 授权**：refyon（zip + 账号同步）、x102201（授权交付）、DSH-X（市场）、rirko、baihejiangnan。
8. **CLI / 无头**：RYun601（纯 CLI）、youridol（`dsh-launcher plugin|skill|mcp --json`）、DreamRift（`--instance ... start|stop|status`）、LvienOeria（`dsh-go` 命令）、Wanbinyu（doctor）。启动器开始可被脚本/CI 驱动。

### 3.3 仍然稀薄 / 空白（本轮证据内）
- **pack 的"投影"语义**（配置/技能/规则以可逆方式注入、不进 plugin 栈）：现有 pack 全部落 profile（`.dspack` overrides）或整 home（`.dshpack`），没有"不进栈"的产品。**但 PackForge manifest v5 已到多 profile + preset/skill/指令级别，语义离 PAD 很近——这是被追平最快的一条。**
- **统一管理中枢**：DSH-X / dsh-plugins 已有多页（版本/实例/pack/插件/MCP/技能），但没有产品把 **发行号库 × 实例矩阵 × pack 生命周期 × 跨实例会话** 放进同一张运营视图。
- **跨实例会话浏览/迁移**：只有局部（refyon 导出 zip、DreamRift 档案页、dsh-helper pack 可选会话）。
- **多启动器互通**：PackForge 已建 launcher registry / canonical ID / 支持徽章，说明生态希望启动器统一认 pack。

---

## 4. 对 PAD 的结论

### 4.1 已被追平（不能再说"只有我们做"）
多版本共存（dsh-plugins / RYun601 / hpyer / DSH-X / youridol / DreamRift / wishesl）· 每实例独立 home（≥6 家）· 整合包（DSH-X 市场、dsh-helper 授权交付、rirko、MarcoG-h、refyon）· 托盘/自启/单实例锁/安装包/自动更新（全员）· 内嵌视图 + token 桥（≥11 家）· 失败回滚（RYun601/zat/refyon/Gru/starline）。

### 4.2 该抄（按性价比）
1. **RYun601 的事务式运行时治理**：活动指针 + `--upgrade <精确版本>` 可升可降 + 回退链（只有通过就绪校验的版本才能用于回退）+ 维护互斥锁 + 离线烟雾验证。PAD 版本库可直接吸收成"发行号指针 + 回退链"。
2. **dsh-helper 的 pack 授权/交付模型**：机器码/密码、禁再导出、每机导入次数、凭据永不入包、导入信任确认。
3. **DSH-X 的版本保留策略**：保留 N 个旧版 + 启动项当前/上一次版本额外保留。
4. **wishesl 的实例级插件屏蔽**：临时 `--patch` 覆盖层，只对本次启动生效、不改全局、停止自动恢复。
5. **onblog 的升级预检 + RYun601 的"不兼容插件列举并询问移除"**：写进 PAD 的升级/切版本流程。
6. **zat 救援中心 / refyon"回退上次正常状态 + 5min×3 自动拉起"**：并入 PAD 失败恢复面。
7. **至少兼容 `.dspack`（manifest v5 / pack v3）**，并挂 PackForge launcher registry 徽章；否则 PAD 的 pack 是孤岛。

### 4.3 该避
1. 不要自造 pack 格式（生态已有规范+市场+徽章；要么兼容，要么明确"我们是本地投影不是 pack 市场"）。
2. 不要把"多版本 / 独立 home / 托盘 / 免 Node"当独有卖点。
3. 不要做第 N 个内嵌 WebView 壳（≥11 家 + 官方桌面端）。
4. 不要忽略 CLI/无头（已有 5 家在做成可脚本驱动；PAD 的 `pad cli` 要显式对标）。
5. 不要再押"官方没桌面端"的时间差（WEP-56 / Ruler4396 已因此归档）。

### 4.4 PAD 现在站在哪（一句话）
**「多版本 × 独立 home × 整合包」三件套全部被人分头实现或规范化了。PAD 剩下的差异是「pack 投影语义 + 统一管理视图 + 回退链体验」的组合**；对外表述应从"只有我们能多版本/独立 home"改成"把多版本、实例、pack、回退放进一个可验证的管理面，且 pack 不进 plugin 栈"。

---

## 5. 待核实（影响结论的 3 条，优先级高）

1. **DSH-X（770★）的多实例是否各自独立 `DSH_HOME`**：README 的"用户目录"含义未明说 → 决定"独立 home 是否连头部都已做"。
2. **dsh-plugins/dsh-launcher 的安装包是否真实可用/用户量**：目前只有仓库证据（76★ / 2026-10-31 push），无 Release 资产核实。
3. **PackForge 市场当前规模与校验实现**：`.dspack` 是否真能被 DSH-X 导出/导入通过规范校验；PackForge CLI 实际用法。

其余 14 条（各项目单实例锁、下载量、dsh-plug.in 规模等）见 `.tmp/research/launchers-only.md` §8。

---

## 6. 证据索引

- 工作稿（24 项目 × 7 段行为链、能力矩阵、启动对照表、未核实 17 条）：`.tmp/research/launchers-only.md`
- PAD 基线（代码 HEAD `0672019`/D98）：`.tmp/research/pad-baseline.md`
- 关键项目：<https://github.com/yyh-001/DSH-X> · <https://github.com/dsh-plugins/dsh-launcher> · <https://github.com/RYun601/dsh-launcher> · <https://github.com/x102201/dsh-helper> · <https://github.com/wishesl/dsh-launcher> · <https://github.com/mishibeikejie/zat-dsh-launcher> · <https://github.com/MarcoG-h/DSH-Launcher>
- 规范/市场：<https://github.com/DSH-PackForge/DSH-PackForge>
- 上一份（桌面端/壳，已降级为邻接物）：[launcher-landscape-2026-11.md](launcher-landscape-2026-11.md)
