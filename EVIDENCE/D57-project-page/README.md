# D57 · 2026-10-31 · 项目页重做信息优先级：运行卡给 pid / 端口 / 已跑 / 内存

用户（两张截图：运行卡里只有"证据 进程现场"和"不是 PAD 起的…所以没有端口 / pid"）：

> "这里也要有。为啥没有，然后，你看看类似项目的这个页面。你这里现在有问题，就是显示的都是
> 无效信息。首先没有用，其次不是一个人最想知道，最需要知道的。其次，读起来还很困难"

## 为什么之前"没有"

「运行」卡只读两处：PAD 账本（`AppState.Running`）和节点账本（`<home>/.pad/nodes.json`）。
「本机 DSH」是外部启动的 DeepSeek Harness app（`DSH_HOME=E:\DSH-Home`），两本账都没有它，
于是卡片直接写成"不是 PAD 起的，所以这里没有端口 / pid"——**把"我没查"写成了"没有"**。
实际这些事实一直都在：进程现场能按 `DSH_HOME` 认领进程，系统 TCP 表知道它监听哪个端口。

## 同类项目怎么摆（见 `docs/superpowers/specs/2026-10-31-pad-project-axis-design.md` §1.5 横向对照）

- vibe-kanban：workspace 卡直接把 dev server URL 摆出来，一键打开；
- neeko / kild：项目行是"状态点 + 计数徽标"，先回答在不在跑；
- claude-squad / OpenHands：一屏列会话/agent 的执行状态。

共同顺序是：**在不在跑 → 跑在哪、怎么打开 → 正在干什么 → 细节**。PAD 之前是反的。

## 修法

1. **`Core/RunFacts.cs`（新）**：按 `DSH_HOME` 认领进程（排除 PAD 自己），选主进程
   （`dsh-desktop-host` / `bin.js` / `--expose-internals` 优先，其次内存最大），取
   进程名、pid、内存、启动时间 → 已跑时长；配置 profile 取 `DSH_PROFILE` / `--profile` /
   命令行里的 `profiles\<name>`；PAD 自己起的再叠账本里带 token 的 URL。
2. **`Core/Ports.cs`（新）**：`GetExtendedTcpTable` P/Invoke 直接读系统 TCP 表，
   pid → 监听端口（IPv4）。不 shell out 解析 netstat（输出随系统语言变）。
3. **`Core/ShellActivate.cs`（新）**：枚举顶层窗口，在给定 pid 集合里找"可见、有标题"的窗口
   （Electron 的主窗口不一定挂在主进程上），`AttachThreadInput` 绕前台锁后前置。
   实测：点「切到窗口」→ 前台进程 = `DeepSeek Harness`（pid 62528）。
4. **项目页重排（四张卡，不是九张）**：
   - **运行（整行 864）**：状态词 + 灯 + 来源；六个统计：进程 / pid / 端口 / 配置 / 已跑 / 内存；
     右侧动作：切到窗口（外部启动）/ 打开界面（PAD 起的有账本 URL）/ 打开工作区 / 日志。
   - **对话（576）**：在执行数、共 N 条、最近时间、4 行对话（灯/标题/工作区·最后消息）。
   - **项目（280）**：工作区 / home / 来源 / 备注 + 工作区 / 目录 / 日志。
   - **环境（整行 864）**：内核 / 组合包 / 整合包 / 会话 / 上下文 / 工具调用 + 一行
     `profile N 个 · 近 7 天 M 次落盘 · 工作区类型`。
5. 删掉"不是 PAD 起的…所以没有端口 / pid"这种话；证据降级进 tooltip。
   `RunFacts` 读不到进程时才显示 `—`，且来源写"进程现场"。

## 实测（真实窗口，最终构建）

`project-page.png`（本机 DSH）：

- 运行：`在跑` + 灯 + `外部启动 · 进程现场`；
  `进程 DeepSeek Harness` · `pid 68084` · `端口 127.0.0.1:54024` ·
  `配置 desktop` · `已跑 1 小时 4 分` · `内存 319 MB`；动作：切到窗口 / 打开工作区 / 日志。
- 对话：`1 条对话在执行 / 共 185 条 · 最近 11 秒前`，`打开PAD` 亮灯（本文这次会话）。
- 项目：`home E:\DSH-Home`、`来源 收编的 DSH home`、工作区/目录/日志按钮。
- 环境：`内核 0.2.1-rc.2` · `组合包 14` · `整合包 —` · `会话 185` · `上下文 8%` · `工具调用 3271`。

「切到窗口」实测：点完 `GetForegroundWindow` = `DeepSeek Harness`（pid 62528）。

## 验证

- `dotnet build`：0 warning / 0 error；`pad-window` / `pad-depth` / `pad-pack` 绿。
- `pad-window` 新增 D57 断言：`Core/RunFacts.cs` 取 `Ports.Listening` / `WorkingSet` / `Uptime` /
  `ConfigProfile`；`Core/Ports.cs` 有 `GetExtendedTcpTable`；`Core/ShellActivate.cs` 存在；
  页面有 `RunHeroCard` / `EnvCard` / `Stat(`；运行卡占整行；运行卡里不许再出现
  "不是 PAD 起的（PAD 账本里没有），所以这里没有端口 / pid"；有「切到窗口」。

## 边界

- 端口只列 IPv4 监听（本机 DSH 全 IPv4；IPv6 的 0.0.0.0 之外暂不显示）。
- 外部启动的壳 PAD 不停止、不托管（D6/D18）：只给 pid/端口/已跑/内存与"切到窗口"；停止按钮只对 PAD 起的 run 出现。
- 运行现场是"加载时的一次快照"（pid/端口/已跑起点）；已跑时长在绘制时按启动时间实时算，不额外轮询。
- D54/D55 的卡片清单按本条重排；「对话」卡语义不变。
