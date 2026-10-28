# STATE

更新：2026-10-22

## 现在在哪

- 版本 **0.1.0**，最后提交 `0406f77`（2026-09-21，`feat: replace Tauri shell with PAD WPF launcher`）
- 侧重点：启动器（TUI）+ 整合包装卸 + 管理页。产品定义见 `agent-pack-dsh/docs/PRODUCT.md`
- 直方图：`dsh-tui` 硬编码 19 处；`desktop` 命中 **0**；`tools` / `toolCount` 命中 **0**
- 结论：**只服务终端 profile，且对"这次启动出来的 DSH 装了多少工具、上下文多大"完全没有观测**

### P0 进度

| 条目 | 状态 | 证据 |
|---|---|---|
| P0-1 只读体检（`tools == 71`、`toolsTokens == 12755`） | **通过** | `EVIDENCE/P0-1/` |
| P0-2 只读不污染（home 快照哈希前后相等） | **通过** | `EVIDENCE/P0-2/` |
| P0-3 建系统骨架（`system new`） | **通过** | 见下 |
| P0-4 拉起 / 收回一个节点（web / tui / app 三个面） | **通过**（app 只做拒绝） | `EVIDENCE/D16-*/` |
| P0-5 一张卡片只显示事实（在跑 / 已停 / 端口） | 未开始 | — |

**闭环已通（D19，实验性）**：`system new` → `layers/<layer>/dsh-home` → `layer open` → `node close`。
层 profile 由 `dsh --profile <layer> --from-default-profile web --dump-config` 物化（compose 不 boot）。
实测：`frontend` 层从 empty → profile → running 1 → 关闭后 running 0，端口释放。见 `EVIDENCE/D19-layer-open/`。

> **D20（2026-10-23）**：`system` / `layer` 降级为实验性功能——不进主界面、不改 `ManageView`、
> 不参与默认启动路径。默认形态是**一个项目 = 一份 `DSH_HOME` = 一个工作区**。
> 转正 / 删除的条件写在 D20 里，判据是使用记录而不是感觉。

> 2026-10-23：P0-4 / P0-5 原本写的是"跑验收 + 验收状态"，按 D18 撤掉。
> PAD 是启动器，不执行断言、不判读结果。

已落地：`Core/Inspect.cs`（新）+ `Core/Cli.cs` 的 `inspect` 子命令（`+62` 行）。
`--out` 是唯一可靠输出通道——WinExe 的 stdout 无法被重定向捕获，这是 `Cli.cs` 原有的 `AttachConsole` 设计造成的。

### 已验过的模型主张

| 主张 | 状态 | 证据 |
|---|---|---|
| 整合包投影走 bundle 层（D13）：一个包 = 一层 | **通过**（11 bundle → 11 层，一一对应） | `EVIDENCE/D13/` |
| 插件清单按官方规范读，PAD 不重算兼容性（D12） | 已实现（`plugin audit`），未做"装包后 dump-config"闭环 | — |

`dsh --profile <p> --dump-config` 是只读、可退出码判定的验收手段，符合 D6；测出来的层标记里 in-box 与 out-of-tree 无区别。

### 已落地的命令

| 命令 | 做什么 | 验证 |
|---|---|---|
| `pad cli inspect <home> [--json] [--out]` | 结构：工具数 / 工具 token / 上下文水位 / 压缩数 | 通过（P0-1/P0-2） |
| `pad cli usage <home> [--json] [--out]` | 事实：token 消耗 + 每工具调用次数（无任何"省多少"字段） | 通过（D15） |
| `pad cli plugin audit <home> <profile> [--surface]` | 清单合规 + 形态白装判定，按官方规范读 `package.json#dsh` | 通过（D12） |
| `pad cli surfaces` | 三种形态的契约 | 通过（D9） |
| `pad cli system new <name>` | 系统骨架：层 home + 模块工作区 + 断言模板 | 通过（D11） || `pad cli node open <home> --bin <bin.js>` | 起 web 实例、读启动行、开 Edge app 窗口 | 通过（D16，实例在 PAD 退出后仍存活） |

`--bin` 现在可省：解析顺序 `--bin` → `pad.json#launch.dshCli` → release 记录 bin，都没有则 `PA094` 点名开关（D21）。
asar 内的路径靠 `LaunchPolicy.Exists` 判定（`File.Exists` 对 `app.asar` 内的路径永远为假）。

`node open` 的两个坑已修：位置参数把子命令当成了 home；子进程 stdout 用管道时 PAD 一退出就会把实例写死。现在 stdout 落文件、PAD 尾随读取。

`node close` + 账本已完成：`<home>/.pad/nodes.json` 记录 `{profile, surface, port, url, pid, startedAt, logPath}`，
close 先查账本、再杀进程树、最后删记录；找不到就报 PA081，不靠端口猜。全流程实测见 `EVIDENCE/D16-close/`。

`--surface tui` 已完成：起独立控制台跑 `--profile <p>`，记录**控制台 pid**，close 杀整棵树。
实测进程树 `cmd.exe → DSH Desktop.exe (ELECTRON_RUN_AS_NODE)`，关闭后两者都不在。证据见 `EVIDENCE/D16-tui/`。
坑：`Proc.FindDshPid` 只查 `node.exe`，而桌面 shim 起的是 `DSH Desktop.exe`，按 profile 永远查不到 → 改为记控制台 pid。

**未做**：`app` 形态的 open。

`--surface app` 已完成「拒绝」这一半：壳已在运行时 PA086，exe 不存在时 PA087。判据是独占打开
`%APPDATA%\dsh-desktop\lockfile`（Electron 会起一堆同名进程，不能靠进程名数）。

**壳不可分家（D17 实测结论）**：`--user-data-dir` 实测退出码 9、目标目录未创建；asar 全文扫描里
`requestSingleInstanceLock` 存在、`--user-data-dir` **0 次**。壳把 userData 定死，home 跟着定死；
换 home 只能换壳，而壳不可多开。→ **一个项目一个 DSH 只能用 `web` / `tui`**。证据 `EVIDENCE/D17-app/README-2.txt`。

三种形态的最终定位：`web` / `tui` 能给项目隔离，`app` 不能（只管生命周期 + 明确拒绝）。

## 下一步（P0）

目标：跑通一个 demo 项目，全程不出 PAD 窗口。细则见 `ACCEPTANCE.md`。

1. `pad cli inspect <profile> --json`：只读体检（工具数 / 工具 token / 上下文水位 / 压缩次数），数据源 `%DSH_HOME%` 与 session projcache
2. `pad cli project new <name> --profile minimal`：建瘦实例
3. `pad cli accept <name>`：跑 ACCEPTANCE 里的断言，证据落 `EVIDENCE/`
4. PAD 窗口中一个"项目卡片"：状态 + 一条证据 + 一个按钮

## 卡点 / 风险

- **桌面端是 Electron**，不能用 `wt.exe --profile` 拉起。A（启动）与 B（管理）的进程语义不同，见 `DECISIONS.md` D2
- `bin\Release` 里的 exe 是 **2026-09-10** 构建的，比最后一次提交早 11 天。调试前先确认二进制对应哪个 commit
- `drafts/` 1,188 个文件 / 184 MB 未跟踪；`config/`、`docs/superpowers/` 同样未跟踪

## 上一条会话的结论（供追溯）

本机实测：71 个工具 / 12,755 token 的 schema，其中 46 个与当前任务无关；一个 45 轮会话上下文到 33.4 万 token，400 条历史被压缩成摘要，用户发言仅占 0.08%。完整数据见 `E:\修复注意力涣散\REPORT.md`。
