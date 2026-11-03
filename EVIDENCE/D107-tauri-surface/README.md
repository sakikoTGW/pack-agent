# D107 · tauri 形态：DSH 桌面端（Tauri）作为 PAD 第 4 个 surface

date: 2026-11-04
status: 已实现（未 commit）
决策：`DECISIONS.md` D107；文档：`agent-pack-dsh/docs/SURFACE.md`
上游研究对象：`github.com/dsh-tauri/deepseek-harness-desktop`（浅克隆 @ `b74b63b`，在 `.tmp/research/dsh-tauri`，未入库）

## 一句话

`dsh-tauri` 桌面端就是 PAD 的一个 surface：`pad cli node open <home> --surface tauri --exe <exe>`。
不新增 PAD 页面、不单独设计 Tauri UI——复用现有 surface / 账本 / stop 链。

## 上游事实（决定 PAD 怎么接）

| 事实 | 位置 | 对 PAD 的含义 |
|---|---|---|
| 桌面端 = Tauri 2 壳 + Electron Runtime（React 前端） | README「功能」；仓库 AGENTS.md「桌面端已采用 Electron 架构」 | PAD 不重写它，只拉起/收回 |
| release 构建下 `DSH_HOME` 环境变量优先，否则 `~/.dsh` | `apps/desktop/src-tauri/src/config/runtime.rs:558-576` | PAD 能用 `DSH_HOME` 把项目实例 home 指给它 |
| 单实例：按 app identifier 抢锁（Windows 自研 mutex；其他平台 `tauri-plugin-single-instance`） | `src/lib.rs:61`；`src/desktop/builder.rs:1610` | 同一时间只有一扇 Tauri 窗；`Exclusive=true`，第二次启动按「激活转发后退出」处理 |
| deep link 只处理 `dshtauri://open`（聚焦窗口） | `src/desktop/deep_link.rs:6-12` | 没有会话级深链；jump 只给 R3 `focus-clipboard` |
| 内核/依赖/更新/数据目录迁移由桌面端自己管 | README「运行时管理 / 数据目录」 | PAD 不接管它的更新；`--exe` 指向用户已装好的桌面端 |

## 代码改动

| 文件 | 改动 |
|---|---|
| `agent-pack-dsh/pad/Core/Surface.cs` | 新增 `Surface.Tauri`（`tauri`，独占，无端点）；`Surface.All` 加第 4 项 |
| `agent-pack-dsh/pad/Core/Nodes.cs` | 新增 `OpenTauri`：校验 exe → `DSH_HOME=<home>` 启动 → 5s 存活判定；PA147/148/149 |
| `agent-pack-dsh/pad/Core/Cli.cs` | `node open --surface tauri --exe`；`p cli surfaces` 自动输出；`window` 文案 `tauri desktop window`；缺 `--exe` → PA146 |
| `agent-pack-dsh/pad/Core/Jump/JumpModels.cs` | 内置描述符 `pad-tauri`（kind `tauri`，transport `focus-clipboard`） |
| `agent-pack-dsh/modpack/registries/pa-codes.json` | PA146–PA149 |
| `agent-pack-dsh/docs/SURFACE.md` | 「三种形态」→「四种形态」；写清 tauri 的单实例 / `DSH_HOME` / 无端点边界 |
| `agent-pack-dsh/modpack/pad-tauri.test.ts` | 新增：源码契约 + 实跑（surfaces / PA146 / PA147 / PA149） |

## 证据文件

| 文件 | 内容 |
|---|---|
| `01-surfaces.txt` | `pad cli surfaces` 输出（含 `"name": "tauri"`、`exclusive: true`、`needsEndpoint: false`） |
| `02-pa146-no-exe.txt` | 缺 `--exe` → `PA146`，exit 1 |
| `03-pa147-missing-exe.txt` | exe 不存在 → `PA147`，exit 1 |
| `04-pa149-fast-exit.txt` | 秒退 exe（`where.exe`）→ `PA149`（没窗口就报，不记假成功），exit 1 |
| `05-build.txt` | `dotnet build ... 0 警告 0 错误` |
| `06-test-tauri.txt` | `bun run modpack/pad-tauri.test.ts` 两段绿 |
| `07-test-dotnet.txt` | `dotnet test pad.tests` 203/203 |
| `pad-cli.log` | 上述命令的 PAD CLI 日志（scratch root：`.tmp/d107-tauri-surface`） |

相关套件（同一提交状态下实跑）：`pad-window.test.ts` / `pad-project.test.ts` / `pad-depth.test.ts` 全绿。

## 真机验收（2026-11-04，dsh-tauri 0.30.2）

- 安装：`E:\DSH-Tauri`（399.3 MB）；数据 `%APPDATA%\dsh-tauri`+`%LOCALAPPDATA%\dsh-tauri` 整目录搬到 `E:\DSH-Tauri-data` / `E:\DSH-Tauri-localdata` 后建 junction —— **C 盘无实体**。
- 闭环：open 6.1s → 账本 → 同 home PA099 → 另一 home PA149（单实例）→ close 杀树 → 空账本；`DSH_HOME` 确实生效（scratch home 里出现了壳自己的 `profiles/desktop` + `storages/workspace.json`）。
- 真机暴露并修掉：普通 `Process.Start` 继承 PAD stdio → `pad cli node open` 挂 120s；改 `UseShellExecute=true` + 调用期临时 `DSH_HOME` 后 6.1s 返回。`cmd > log` 独占日志导致 PAD 追加失败 → 日志只写一行启动记录。
- 明细与原始输出：`real-shell/README.md`、`real-shell/00-run.txt`、`real-shell/10–12*.txt`。

## 边界（没做，别读成做了）

- **不做 Tauri 专属 UI**：没有新页面/新窗口样式；项目页要不要暴露「Tauri 窗」是设计问题，等用户定。
- **项目 `window.kind = "shell"` 仍是 PA126**：本轮只做 surface（CLI/核心），项目轴 UI 未动。
- **单实例**：一次只能有一个 Tauri 桌面窗；同时开第二个项目之前必须关掉前一个（PA149 文案点名）。
- **没有会话级跳转**：上游只认 `dshtauri://open`；jump 走 R3 聚焦+剪贴板。
- **PAD 不管它的内核/更新**：桌面端的运行时管理与 PAD 版本库是两套，不合并。
- **单实例**：同一时间只有一扇 Tauri 窗；第二个项目要等第一扇关（PA149 文案点名）。
