# DSH 启动器 Top30 二级研究（2026-11-02）

> 第一轮 24 个头部启动器的行为链见 [launcher-behavior-2026-11.md](launcher-behavior-2026-11.md)；第二轮全量清单见 [launcher-inventory-2026-11.md](launcher-inventory-2026-11.md)。
> 本文是第三轮：从 1,222 个客户端项目里挑 **30 个重点**，每个做 7 段行为链 + README 截图。
> 完整稿（约 26 万字节，A/B/C 三组逐项目细节）：`.tmp/research/top30/top30-full.md`；截图墙：`.tmp/research/top30/gallery/index.html`（23 个项目 / 115 张图）。

---

## 0. 方法与口径

- 30 个项目分三组并行：A 管理器/工作台/特殊形态、B 桌面端/发行版、C TUI/IDE/移动/远程。
- 每个项目：基础事实 + 7 段行为链（首次运行→启动→运行中→更新回滚→修复诊断→配置打包→非功能）+ README 截图清单 + 一句话 + 未核实。
- 证据以 README 原文 + 关键脚本源码为主；星数/推送时间来自 GitHub API 或社区列表**快照**（会过期，见 §4）。
- 截图只取 README/文档里的产品图（非实机运行）；116 条清单中下载到 114 张（跳过 1 个 SVG、1 个失败）。

---

## 1. Top30 名单与一句话

### A 组：管理器 / 工作台 / 特殊形态

| # | 项目 | 形态 | 星 | 维护 | 一句话 |
|---|---|---|---|---|---|
| 1 | Liaoyuanxinghuo/DSH-Plugin-Manager（DSH Manager） | 管理器 · Tauri/Rust | 11★ | 11-01 活跃 | 多版本 DSH + Profiles/整合包 + .dspack 导入导出与市场 + 任意版本×Profile 多实例、端口自动分配；**CC BY-NC-SA（禁商用）** |
| 2 | shawnyhu/DSH-Launcher | 管理器 · C#/托盘 | 2★ | 08-22 间歇 | 安装器+托盘+多安装目录+**多 DSH_HOME**+通知+自更新；无自动回滚 |
| 3 | xswt442-cmd/dsh-instance-manager | Web 侧实例面板 | 3★ | 10-31 活跃 | 侧边栏统一列出本机实例（端口/PID/会话数/内存/版本），3080–3129 选端口 |
| 4 | yuexps/deepseek.harness.fnos | 桌面管理应用 | 66★ | — | 启停/重启/源码构建/升级 + 全量快照还原；无 LICENSE 文件 |
| 5 | FlashingChen/dsh-desktop-hub | Electron 控制台 | 58★ | 09-26 放缓 | Harness/Plugin/MCP/Skills 多 Tab；就绪超时口径冲突（120s vs 90s） |
| 6 | peiyucn/dsh-launcher-panel | VS Code 面板 | 11★ | — | VS Code 内启动 dsh + 内置浏览器；依赖本机 Node |
| 7 | lansi-ai/dsh-forge | 桌面壳 · Electron+Cordis Host | 3★ | 09-18 停滞 | 不开放 HTTP 端口；桌面能力以 host 插件注入；无 LICENSE 文件 |
| 8 | gluztm/DSHGuard | 救援式启动器 | 未核实 | — | 快照整组回滚 + 版本记忆 + 插件管理 + 诊断导出 |
| 9 | panzeyu2013/dsh-chamber | 连接管理器 · macOS Swift | 14★ | — | 本地实例 + SSH 隧道/认证 Gateway 统一导航；known-good 两阶段回滚 |
| 10 | MCXCC303/HDSL | 启动器（HMCL 思路） | 16★ | — | 受管 Node、多版本、.dspack 导出导入 + PackMarket |

### B 组：桌面端 / 发行版 / 工作台

| # | 项目 | 形态 | 星 | 维护 | 一句话 |
|---|---|---|---|---|---|
| 11 | dsh-tauri/deepseek-harness-desktop（原 hairyf） | 桌面端 · Tauri | **3407★** | 11-01 活跃 | 一键+核心自愈更新、零环境、三平台；**MIT + 非商用附加条款**；仓库已改名 |
| 12 | fufankeji/deepseek-harness-studio | 桌面端 · Electron | 706★ | 09-01 放缓 | 零代码/插件发现/热点/AI 推荐；解析 stdout 契约、90s 超时 |
| 13 | vibeinging/dsh-desktop（原 …desktop-app） | 桌面工作台 · Electron | 610★ | — | 会话/项目/文件/调研/插件/Office；**主通道 IPC 无端口**；分支 `dev` |
| 14 | turtle2209/Bigfish | 桌面端 · 内置 Node | 315★ | 08-25 放缓 | 双击即用+桌宠；更新仅提示下载页 |
| 15 | agent-earth/deepseek-harness-desktop | 极简桌面壳 | 209★ | 09-18 放缓 | 随机回环端口+就绪检查；诊断最薄 |
| 16 | See-Sol-Lab/DeepSeekGUI | 桌面端 | 169★ | — | Git/内置浏览器/记忆 + Diagnostics Center；产品层 **PolyForm Perimeter** |
| 17 | hust-open-atom-club/oh-dsh | 发行版 · TUI+桌面+Web | 256★ | 09-12 放缓 | 分层安装；`runtimes/current.json` 指针 + 冒烟后切换 + 一键回滚 |
| 18 | cocode-agency/cocode | 发行版 · GUI+TUI | 161★ | 09-03 放缓 | GUI/TUI 租约协议（scope=home+profile+Host 指纹）；doctor |
| 19 | Astro-Han/pawwork | 桌面端 | 182★ | — | **不内置模型凭据**（README 与仓库描述冲突，以 README 为准） |
| 20 | techflag/workdsh | 桌面工作台 | 143★ | — | 项目/资产/专家/技能/连接器；runProfile+token URL；诊断薄 |

### C 组：TUI / IDE / 移动 / 远程

| # | 项目 | 形态 | 星 | 维护 | 一句话 |
|---|---|---|---|---|---|
| 21 | huiliyi37/dsh-tianshu-tui | 终端 TUI 插件 | 296★ | 10-29 活跃 | 纯展示层、会话恢复/分叉/回退、一键脚本；安装浮 `@next` 不锁宿主 |
| 22 | CheChc/deepencode | TUI（opencode 风格） | 101★ | 08-24 停滞 | build/plan 模式+状态栏；仅源码、无回滚 |
| 23 | cofy-x/dsh-console | TUI + 独立 CLI | 22★ | 活跃 | 精确钉宿主 0.2.1-rc.2、旧版拒绝；唯一宿主版本门禁 |
| 24 | anoslide/dsh-vscode-layout | IDE 式布局 | 104★ | 停滞 ~78d | 三栏/文件树/多标签；无版本校验、补丁需重放 |
| 25 | shaobeichen/dsh-pocket | 移动/远程客户端 | 2655★ | 活跃 | 双二维码/独立密码/限速/隧道自恢复；**GPL-2.0**；不在手机装运行时 |
| 26 | DSH-APP/DSHA | Android · 本机运行时 | 2850★ | 活跃 | APK 内离线 rootfs（278MiB/arm64）；**隔离区试跑+失败自动回切+快照（真回滚）** |
| 27 | ZSeven-W/dsh-ios | macOS 上的 iOS 模拟器插件（纠偏） | 未核实 | 39d 放缓 | **不是 iOS 客户端**：模拟器/真机工具 + MJPEG 侧栏 |
| 28 | jingyunstudio/jingyun-dsh | 白标桌面壳+商业平台（纠偏） | 925★ | 33d 放缓 | **不是远程启动器**；会员/计费/打包；声称 Apache-2.0 但 LICENSE 文件缺失 |
| 29 | wenbin-wb/dsh-bridge | 远程门禁/隧道 | 未核实 | 活跃 | 256-bit Token+访客密码+后台防篡改、三档作用域、fail-closed、IM 矩阵 |
| 30 | FunnelCakes/deepseek-harness-android | Android/Termux 部署 | 38★ | 76d 停滞 | 10+ 兼容补丁；不锁版、无回滚、端口硬编码 3080 |

---

## 2. 横向结论（三组合并后）

### 2.1 「谁拥有 Harness 进程」已分成四代
1. **子进程 + stdout 就绪契约**：fufankeji（解析 `dsh web: <url>`，90s 超时、SIGTERM→5s SIGKILL）
2. **子进程 + 随机回环端口 + 就绪检查**：agent-earth、Bigfish（HTTP 轮询 90s）、dsh-tauri（3080 占用时避让）
3. **自管 Host + 上层租约**：cocode（scope = DSH_HOME + profile + Host 配置指纹）
4. **绕开 CLI**：pawwork（sidecar）、workdsh（runProfile + token URL）；vibeinging 更极端——**IPC 主通道无端口**

### 2.2 运行时策略两极
- **自管/内置**：DSH Manager（便携 Node+pnpm）、Desktop Hub（内置 Node 24.10+dsh 0.1.7-rc.2）、HDSL（受管 Node）、DSH Forge（全内嵌）；
- **寄生宿主/系统**：VS Code 面板（要求本机 Node）、dsh-instance-manager（寄生）、fnOS（依赖应用 nodejs_v24）、DSHGuard（npx）、dsh-chamber（随包）。
- **设备端真正装运行时只有 2 个**：DSHA（APK 内离线 rootfs）与 FunnelCakes（Termux 脚本）。

### 2.3 版本哲学三派，回滚能力极稀缺
- 多版本共存（DSH Manager / DSH-Launcher / HDSL）｜单版本钉死随包（Desktop Hub / DSH Forge / dsh-chamber）｜通道/源码 tag 跟随（VS Code 面板 / fnOS）。
- **30 个里真有回滚的只有约 5 个**：DSHGuard（快照整组回滚）、fnOS（全量快照还原）、dsh-chamber（known-good 两阶段）、oh-dsh（runtime 指针+冒烟）、DSHA（隔离试跑+自动回切）；DeepSeekGUI 是软回滚。其余最多「另装旧版」。

### 2.4 整合包标准化渗透率低
- A 组 10 个里只有 **2 个**接 `.dspack`/PackForge（DSH Manager、HDSL）；B/C 组更少。
- 生态已有规范 + 市场 + launcher registry，但真正接入的启动器还是少数 → 格式孤岛风险真实存在。

### 2.5 诊断/救援是分水岭
- 完整救援面：dsh-tauri（safe profile + `.broken-<ts>` + 单插件快照）、fufankeji（插件事务回滚+插件安全模式）、vibeinging（恢复页 + doctor）、DeepSeekGUI（Diagnostics Center）、Bigfish（启动失败三选一重置）、oh-dsh（候选插件隔离预览）、cocode（doctor + host status）、DSHGuard、fnOS、dsh-chamber。
- 最薄：agent-earth、pawwork、workdsh、Bigfish（仅重置入口）、vscode-layout。

### 2.6 许可证必须读 LICENSE 本体
5 个反例：DSH Manager（CC BY-NC-SA，禁商用）、DeepSeekGUI 产品层（PolyForm Perimeter）、dsh-tauri（MIT + `LICENSE.details` 非商用附加）、pawwork（Apache-2.0）、jingyun（声称 Apache-2.0 但文件缺失）；fnOS/DSH Forge 无 LICENSE 文件；shawnyhu API 报 NOASSERTION 但文本是 MIT。

### 2.7 定位纠偏（任务书 → 实际）
- `ZSeven-W/dsh-ios`：不是 iOS 客户端，是 macOS 上的 **iOS 模拟器/真机插件**；
- `jingyunstudio/jingyun-dsh`：不是远程启动器，是 **Tauri 白标桌面壳 + 商业平台**；
- `huiliyi37/dsh-tianshu-tui`：是**终端 TUI 插件**，不是 Web 端界面插件；
- `Astro-Han/pawwork`：**不内置模型凭据**，仓库描述与 README 冲突；
- 两个仓库改名：`hairyf/…` → **`dsh-tauri/…`**；`vibeinging/…-desktop-app` → **`vibeinging/dsh-desktop`**（默认分支 `dev`）。

### 2.8 截图素材分布极不均
- 最多：dsh-bridge 24 张、fufankeji 15、workdsh 13、DeepSeekGUI 10、dsh-tauri/Bigfish/Bigfish 类 4–5、oh-dsh 2；
- **零产品截图**：DSHA、jingyun-dsh、dsh-vscode-layout、FunnelCakes、shawnyhu、xswt442、lansi-ai（README 只有 badge/logo/群二维码）；
- 截图墙共 23 个项目 / 115 张图。

---

## 3. 数据与产物

| 产物 | 位置 |
|---|---|
| 完整逐项目稿（26 万字节） | `.tmp/research/top30/top30-full.md` |
| A/B/C 组原稿 | `.tmp/research/top30/group-a.md`、`group-b.md`、`group-c.md` |
| 截图清单（116 条 URL） | `.tmp/research/top30/group-*-shots.json` |
| 下载清单（114 张） | `.tmp/research/top30/shots-manifest.json` |
| **截图墙（23 项目/115 图）** | `.tmp/research/top30/gallery/index.html` |
| 拼图总览 | `.tmp/research/top30/contact/sheet-01..10.jpg` |

---

## 4. 未核实与口径风险

1. **星数/推送时间口径混杂**：GitHub API 配额中途耗尽（60/h），部分来自 API、部分来自社区列表快照；至少 3 处已知偏差（hairyf 列表 814 vs API 3407、fufankeji 426/706、DSH-X 591/770）。表中已尽量标注。
2. **4 个项目 ② 段有硬缺口**：cocode 的 GUI endpoint/端口形态、pawwork 的端口/日志/单实例、workdsh 的端口/就绪/日志、oh-dsh 的 Desktop 内部拉起方式。
3. **其他缺口**：shawnyhu 是否单实例锁；HDSL Release 资产名与 tag 疑似错位；DSH Forge 自更新保留上一版的程度；fnOS 离线包无哈希/签名证据；dsh-ios 是否存在独立 iOS 客户端仓库；FunnelCakes/dsh-bridge 对 0.2.x 的兼容。
4. **截图**：GitHub `user-attachments` 链接 HEAD 403 / GET 200；下载器已按 GET 处理，但仍跳过 1 个 SVG、1 个失败。
5. **未执行任何仓库代码**：全部结论来自 README/文档/脚本与源码静态阅读。
