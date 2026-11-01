# D55 · 2026-10-31 · 项目页看得到对话：哪几条在执行，以及"在跑的 home 没进 PAD"

用户（`本机 DSH` 项目页截图）：

> "这一页，根本看不到目前有哪些对话在运行。。。。或者不明显"

## 现场两件事

1. **项目页没有对话列表。** 只有一个「会话 16」+ 近 7 天柱状图：这个 home 里有哪些对话、跑到哪一步，
   界面上不回答。「运行」卡还只看 PAD 账本（`AppState.Running` / `Nodes.Ledger`），而左栏绿灯看的是
   `AppState.StatusOf`（D52/D53）——左边绿、右边写"未启动"，同一页两份事实。
2. **正在聊的那份 home 根本不在 PAD 里。** 本会话跑在 `E:\DSH-Home`（DeepSeek Harness app，profile `desktop`），
   而 PAD 的「本机 DSH」指向旧的 `%APPDATA%\dsh-desktop\harness`。所以不管项目页怎么画
   "这份实例的对话"，都看不到当前真正在跑的那 19 条。

## 做法

1. **`Core/Dialogs.cs`（新）**：只读 `<home>/storages/session_projcache/sessions/*.json`，取
   标题 / cwd / preset / 模型 / 最后消息时间 / 上下文水位 / 回合数 / `sessionStats.openStep` /
   `pendingCalls` / 子 agent 标签。
   - **"在执行"= DSH 自己记着 `openStep` 还没收尾**；有管理口（`@sakikotgw/pad-gateway`）时以
     `session.list` 的 `running` 为准。PAD 不另做判定（D18）。
   - 只解析按落盘时间最近的 N 份（默认 16），半写坏的缓存跳过，不挡整张卡。
2. **项目页「对话」宽卡**（第二行第一位）：`N 条对话在执行` + `共 N 条 · 最近 …` + 4 行
   （呼吸灯 / 标题 / 工作区 · 最后消息），有子 agent 的写进 tooltip（cwd / preset / 模型 / 上下文）。
   脚注写明依据：
   - 管理口已连 → `在跑来自 DSH 的 session.list（可见 N 条 session）`；
   - 没连上 → 原样给出 `Gateway.Probe` 的话（"跑起来了但没有写出管理口。这个 profile 大概没装 apiproxy
     —— 到版本选择里给它补上"）。
   - 卡标题带上执行数：`对话 · 2 在执行`。
3. **「运行」卡改用 `AppState.StatusOf`**：`在跑 / 启动中 / 未启动` + 灯 + `证据`（`home 锁 · 进程现场`）；
   PAD 自己起的再列 profile / 端口 / 已跑 / agent 文案。和左栏是同一份事实。
4. **页头横幅：在跑但没进 PAD 的 home**。`Discover.Run` 扫出 `Running` 且不属于任何实例的 home，
   写 `19 条对话 · 2 个在执行 · 最后写入 9 秒前` + `在执行：打开PAD · 打开PAD (2)`，右侧「收编」
   走 `MainWindow.AdoptHomePath`（`Launcher.AdoptHome`，只读收编；确认后切到新项目页）。
   不收编也能看见那边在跑什么；收编后整份对话列表进项目页。
5. **会话卡**收窄成 1 列 mini 柱（近 7 天落盘），把宽度让给对话卡。
6. 清单 60s 内复用，切项目不重复全量扫 `Discover.Run`；横幅 / 对话卡都先画首帧、后台补数据。

## 验证

- `dotnet build`：**0 warning / 0 error**。
- `pad-window` 新增 D55 断言（`Core/Dialogs.cs`、`session_projcache`、`openStep`、`pendingCalls`、
  `RunCard` 用 `StatusOf`、`DialogCard`、`Gateway.Tree`、`OtherHomesBar`、`AdoptHomePath`）；
  `pad-window` / `pad-depth` / `pad-pack` 绿。
- 真实 PAD 窗口（UIA 查找 + TOPMOST 后鼠标点击），同一次运行两次截图：

| 截图 | 说的是什么 |
|---|---|
| `home-with-banner.png` | 管理主页：横幅 `E:\DSH-Home 19 条对话 · 1 个在执行 · 最后写入 21 秒前`；本机仪表不变 |
| `project-dialogs.png` | `本机 DSH` 项目页：页头 `0.1.0-rc.7 · 在跑 · dsh/workspace`（不再和左栏绿灯矛盾）；「运行」卡 `在跑 + 证据 home 锁 · 进程现场`；「对话」卡 `没有对话在执行 / 共 16 条 · 最近 43 分钟前` + 4 条对话（标题 / 工作区 / 最后消息）+ 管理口脚注；横幅 `19 条对话 · 2 个在执行` + `在执行：打开PAD · 打开PAD (2)` + 收编 |

UIA 文本核对（同一时刻）：见 `markers.txt`。

## 边界

- **D56 起横幅已删除**：本机 home 换了由 PAD 启动时自动对账（`ReconcileMachineHome`），不再让人点「收编」；
  本条保留「对话」卡与当时的证据（横幅只是 D55 那一版的过渡形态）。
- 未收编的 home 只给摘要 + 执行中标题，**不列全部对话**——要列就收编（4）；这是有意的：
  一个不在 PAD 里的 home，PAD 不假装自己是它的管理器。
- `openStep` 是"DSH 还没收尾"的事实，不是 PAD 的结论；管理口没连时界面必须把那句话写出来（2）。
- 与并行会话的 D52（状态快照）/ D53（运行灯）/ D54（项目页）同一套事实，不新增第二份判定。
