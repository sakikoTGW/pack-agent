# 启动器生态清单（第二轮，2026-11-02）

> 第一轮（`launcher-behavior-2026-11.md`）覆盖了 24 个头部启动器。这一轮换方法：直接从社区大全的「Clients (Desktop & TUI)」章节做全量抽取，回答「还有多少、都是什么」。
> 结论：**上一轮只是头部样本。单一个 awesome 列表里就有 1,222 个去重客户端项目，其中启动器类 325 个；相对上轮 24 个，新增 1,207 个（其中「启动器」标签 310 个）。**

---

## 1. 方法与口径

| 来源 | 用途 |
|---|---|
| `fendouai/awesome-deepseek-harness`（6.1MB） | 主数据：`Clients (Desktop & TUI)` 章节 2,444 条 → 去重 1,222 个客户端项目 |
| `Harris1121/awesome-deepseek-harness` | 交叉：自述追踪 7,789 个项目 / 13 类，数据日更（2026-10-20） |
| npm registry search（4 组查询） | 发现 npm 侧启动器：`deepseek-harness-launcher`、`dsh-hq`、`dsh-whale-desktop-launcher`、`@kuaizhongqiang/dsh-desktop` 等 |
| GitHub API 抽查 | 校验星数时效（见 §2 注） |
| DSH-PackForge / dsh-pack-market / DSH Manager / shawnyhu 等 README | 生态标准与新形态样本 |

**口径警告**：awesome 列表的星数是**列表快照**，与当前 API 有偏差：`hairyf/deepseek-harness-desktop` 列表 814★ / API 3,407★（2026-11-01 push）；`fufankeji/deepseek-harness-studio` 426 / 706；`DSH-X` 591 / 770。**星数只做相对参考，不当作准确值。**

分类是关键词启发式（launcher / desktop-shell / TUI / mobile / remote / other），会有误分；原始数据留在 `.tmp/research/wave2/` 可复核。

---

## 2. 总量（单列表口径，去重后 1,222）

| 类别 | 数量 | 说明 |
|---|---|---|
| **launcher（启动器/管理器/托盘/一键部署）** | **325** | 与上轮 24 个重叠 15 个 → **新增 310 个** |
| desktop-shell（桌面端/客户端壳） | 457 | Electron/Tauri/WebView2/WPF/Rust/Swift 等 |
| TUI / CLI | 243 | 终端界面、命令行启动器、TUI 插件 |
| mobile | 95 | Android/Termios/APK/iOS/鸿蒙 |
| remote / gateway | 15 | 远程访问、SSH、Tailscale、飞书/微信桥 |
| other-client | 87 | 其余客户端类 |

新增启动器中：**46 个 ≥10★，14 个 ≥50★**。另有大量 0–9★ 长尾（名字多为 `dsh-launcher` / `deepseek-harness-desktop` 的重名仓库）。

---

## 3. 新增里最值得看的（按列表星数 + API 抽查）

### 3.1 高星新面孔

| 项目 | 列表★ / API★ | 形态/栈 | 一句话 |
|---|---|---|---|
| [hairyf/deepseek-harness-desktop](https://github.com/hairyf/deepseek-harness-desktop) | 814 / **3,407** | Tauri 桌面端 | 一键桌面端：全本地、核心自愈更新、零环境，Win/mac/Linux |
| [fufankeji/deepseek-harness-studio](https://github.com/fufankeji/deepseek-harness-studio) | 426 / **706** | Electron | 零代码桌面端：插件发现/热点推送/一键管理/AI 推荐/视觉增强 |
| [myYangyunfan/dsh_desktop](https://github.com/myYangyunfan/dsh_desktop) | 521 | Electron | 内置 Node + dsh CLI 的 Windows 一键启动（上轮已收录） |
| [vibeinging/deepseek-harness-desktop-app](https://github.com/vibeinging/deepseek-harness-desktop-app) | 610 | Electron | 本地 AI 桌面工作台：会话/项目/文件/调研/插件/Office 产物 |
| [vibeinging/dsh-work](https://github.com/vibeinging/dsh-work) / [dsh-desktop](https://github.com/vibeinging/dsh-desktop) | 610 / 584 | Electron | 同作者三仓，工作台/桌面端/总入口 |
| [Minke（lencx）](https://github.com/lencx/Minke) | 637 | 桌面端 | 「DeepSeek Harness Desktop」，知名作者，API 暂 404（改名/迁移待核实） |
| [turtle2209/Bigfish](https://github.com/turtle2209/Bigfish) | 315 | 桌面端 | 内置 Node 运行时，双击即用，附桌面萌宠 |
| [agent-earth/deepseek-harness-desktop](https://github.com/agent-earth/deepseek-harness-desktop) | 209 | 桌面端 | 极简跨平台壳，免配置开箱即用 |
| [See-Sol-Lab/DeepSeekGUI](https://github.com/See-Sol-Lab/DeepSeekGUI) | 169 | 桌面端 | 集成 Git/内置浏览器/记忆的稳定桌面端 |
| [oh-dsh（hust-open-atom-club）](https://github.com/hust-open-atom-club/oh-dsh) | 256 | 发行版 | 一站式发行版：TUI + 桌面 + Web 统一体验，分层安装 |
| [cocode-agency/cocode](https://github.com/cocode-agency/cocode) | 161 | 发行版 | 「开箱即用发行版」：桌面 GUI + 终端 TUI + 集成 |
| [pawwork](https://github.com/Astro-Han/pawwork) | 182 | 桌面端 | macOS/Win 桌面 agent，内置免费模型、无需 API key |
| [Pilot Harness](https://github.com/op7418/pilot-harness) | 276 | 桌面端/插件套件 | CodePilot 风格桌面客户端，三平台 |
| [dshcode](https://github.com/whitelonng/dshcode) | 126 | Electron | 社区桌面伴侣，macOS/Win 一键 |
| [workdsh](https://github.com/techflag/workdsh) | 143 | 桌面工作台 | WorkBuddy 风格，管理项目/资产/专家/技能/连接器 |
| [deepseek.harness.fnos](https://github.com/yuexps/deepseek.harness.fnos) | 66 | 管理应用 | 启动/停止/重启、源码构建与升级 |
| [rustdsh](https://github.com/jimoto-no-llm/rustdsh) | 68 | Rust 启动器 | 快、带 Claude Code/Codex 迁移与模型设置 |
| [dsh-desktop-hub](https://github.com/FlashingChen/dsh-desktop-hub) | 58 | Electron | 桌面管理控制台：Harness/Plugin/MCP/Skills 多 Tab |
| [HDSL](https://github.com/MCXCC303/HDSL) | 16 | 启动器 | 基于 HMCL（Minecraft 启动器）思路的 DSH 启动器 |
| [dsh-chamber](https://github.com/panzeyu2013/dsh-chamber) | 14 | macOS Swift | 本地实例 + SSH 隧道/认证 Gateway 的统一连接管理器 |
| [dsh-bar-macos](https://github.com/donald-trump86/dsh-bar-macos) | 18 | macOS 菜单栏 | 极轻菜单栏伴随/快捷启动 |
| [DeepSeekHarnessGreen](https://github.com/LiuJunheng/DeepSeekHarnessGreen) | 17 | 绿色发行版 | 一键安装启动、不污染 C 盘、单文件夹管理、自动更新 |

### 3.2 星少但「完整度」很高的（本轮重点发现）

| 项目 | ★（API） | 为什么重要 |
|---|---|---|
| [Liaoyuanxinghuo/DSH-Plugin-Manager（DSH Manager）](https://github.com/Liaoyuanxinghuo/DSH-Plugin-Manager) | **11**（Rust，2026-11-01 push） | 三栏 UI：DSH 环境（多版本共存）｜Profiles（含整合包管理）｜插件与运行控制；**多版本下载/扫描/镜像**、**多实例 × 多 Profile 独立进程 + 端口自动分配**、**`.dspack`（PackForge manifest v5/容器 v3）导入导出 + 整合包市场**、插件启停/降级/版本豁免、日志提取带 token 地址。功能面几乎覆盖「多版本 × 实例 × pack」全部。 |
| [shawnyhu/DSH-Launcher](https://github.com/shawnyhu/DSH-Launcher) | 2（C#，2026-08-22） | 安装器 + 托盘 + 多 DSH 安装目录 + **多 DSH_HOME 管理** + 端口/工作目录/自启 + 通知（提问/审批/结束）+ 轻量自更新 + 平台双 Release 通道（win-v*/mac-v*）。工程完整度高于星数。 |
| [xswt442-cmd/dsh-instance-manager](https://github.com/xswt442-cmd/dsh-instance-manager) | 3（2026-10-31） | **Web 侧实例管理器**：侧边栏面板统一列出本机 dsh 实例（端口/PID/会话数/内存/版本），3080–3129 选端口启动。管理面长进了 DSH 里。 |
| [DSH-PackForge](https://github.com/DSH-PackForge/DSH-PackForge) + [dsh-pack-market](https://github.com/DSH-PackForge/dsh-pack-market) | 规范仓库 | 整合包事实标准：manifest v5 / `.dspack` v3 / index schemaVersion 2 / **launcher registry（启动器 canonical ID 认领）** / publishing 规范；配套 GUI+CLI（`dspack list/pack/view/install/market`）与市场页。 |

### 3.3 移动端 / 远程 / TUI（新增大类，上轮只覆盖个位数）

- 移动端：`dsh-pocket` 796、`DSHA` 483/370、`dsh-mobile-apk` 309、`dsh-mobile` 294、`dsh-ios` 273、`dsh-android` 133、`dsh-mobile-app` 74……
- 远程/网关：`jingyun-dsh` 369、`dsh-bridge` 145、`ax-feishu-bridge`、`dsh-remote`、`dsh-auth-gateway`、`dsh-ssh`、Tailscale 系……
- TUI：`oh-dsh` 256、`dsh-tianshu-tui` 226、`Pilot Harness` 276、`deepencode` 103、`Martty` 66、`dsh-console` 22……
- 发行版：`oh-dsh`、`cocode`、`pawwork`、`deepseek-harness-studio`、`DeepSeekHarnessGreen`、`dsh-portable` 系……

---

## 4. 这一轮看出来的规律

1. **赛道规模被严重低估**：单列表 1,222 客户端 / 325 启动器；上轮 24 个只是「有 README + 有星」的头部。长尾数量是头部的 10 倍以上。
2. **重名是常态**：`dsh-launcher` 有几十个仓库、`deepseek-harness-desktop` 有几十个；**名字不能当身份**。PackForge 的 launcher registry（canonical ID）正是在解决这件事。
3. **星数与完成度脱钩**：高星多是一键桌面端/桌面壳；真正做「多版本 + 多实例 + pack + 回退」的往往是低星（DSH Manager 11★、shawnyhu 2★、dsh-instance-manager 3★）。上一轮按星取样会漏掉这些。
4. **生态已有事实标准**：PackForge 规范 + 市场 + launcher registry；集成 `.dspack` 的启动器会越来越多，自造格式的孤立风险继续上升。
5. **形态分层稳定**：一键壳（最多）→ 桌面工作台 → 管理器 → 发行版 → TUI/CLI → 移动端 → 远程网关 → 插件形态启动器。官方桌面端出来后，第三方重心明显转向「管理/发行版/移动/远程」。
6. **平台长尾**：Windows 绝对主导；macOS（菜单栏/原生壳）、Linux（AppImage/deb）、Android Termux、鸿蒙都有专门项目；iOS 侧以远程客户端为主。

---

## 5. 对上一轮结论的修正

- 「24 个启动器」→ 应改为「第一轮 24 个头部样本；单列表口径下启动器类 325 个、客户端 1,222 个」。
- 「多版本 / 多实例只有少数几家」→ 实际在低星项目里也有完整实现（DSH Manager、shawnyhu 的 multi-DSH_HOME、dsh-instance-manager）；这条能力已经商品化，不是任何单家的差异点。
- 「整合包是差异化」→ PackForge 已把它变成**规范 + 市场 + 启动器注册表**；不兼容 `.dspack` 会变成孤岛。

---

## 6. 待核实

1. **列表星数时效**：已确认至少 3 个偏差（hairyf、fufankeji、DSH-X），不能直接用。
2. **分类噪声**：关键词分类把部分通用 agent 工作台（`wesight`、`cetus`、`opc-nexus`）算进 launcher；它们只是支持 DSH，不是 DSH 专用启动器。
3. **`Minke` / `oh-dsh` 的 GitHub API 404**：可能改名或迁移，需按 HTML/其他来源复核。
4. 约 2,400 条原始条目里仍可能有遗漏的真实启动器（分类在 other-client 的 87 条、以及 Plugins 章节里的插件形态启动器）。
5. 各项目「是否仍在维护」未逐项核实；列表状态 `✅ active` 只是列表口径。

## 7. 原始数据

- `E:\pack-agent\.tmp\research\wave2\clients-inventory.json`（1,222 条：name/cat/stars/status/url/desc）
- `clients-inventory.tsv`、`clients-report.txt`（分类 Top 40 + 描述）
- `overlap-report.txt`（与上轮 24 个的重叠：15 个；新增启动器 310 个）
- `inventory.tsv`（另一版宽口径 3,593 条，含噪声，仅作检索）
- 下载的 README：`wave2/*.md`（DSH Manager、shawnyhu、PackForge、dsh-instance-manager、dsh-forge 等）
