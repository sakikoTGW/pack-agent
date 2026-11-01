# D64 · P2：web 启动 detach 修复 + 多 agent preset

用户原话「1，3」→ 对应的第 3 项本轮做完（detach + 多 preset）；第 1 项（UI 接线）放在
下一轮单独做，原因和计划见文末。

## 1) detach：`project run` 不再让终端一直挂着

**问题**（D62 遗留）：`Runner.LaunchWeb` 用管道读子进程输出，web 进程和调用方共用 stdout，
所以从终端跑 `project run` 会一直占着终端/管道，直到窗口关闭。

**修法**
1. `LaunchWeb(..., detachOutput: true)`：`cmd /d /c start "" /b cmd /d /c "…launch-web.cmd >> log 2>&1"`
   —— 子进程拿自己的控制台句柄，输出直接落到实例日志，父进程立刻退；
2. `LaunchResult` 增加 `LogPath`：`ProjectRunner` 只轮询**本次启动打开的那个日志**，
   不再按 mtime 猜（上一版会读到死进程的旧 token，HTTP 401）；
3. 读日志用 `FileShare.ReadWrite | Delete`：detached cmd 还持有该文件时也能读。

**验证**（真实 root，`ev-project`）：

```
Start-Process … cli project run ev-project --json --out run-detach.json
exe ended=True in 20.3s     ← 之前要等窗口关闭
```

- `run-detach.json`：`status: started`、`preset: minimal`、**新 token**；
- CLI 退出后 node 仍存活：pid 58876 监听 `127.0.0.1:3080`；
- 日志里是本次启动的真实就绪行（`cli-detach.out`）。

**已知边界**：交互终端里命令会立刻返回；但用 `| tee` 之类**管道捕获输出**时，shell 仍会
等到 web 进程退出——因为 `start /b` 子进程还持有那根管道。这是 Windows 句柄继承的边界，
不是 PAD 的等待。GUI 启动不受影响。

## 2) 多 preset（P2）

语义：`project.agents[0]` 是默认；`add` 只是把选项挂到项目上，`use` 才换默认。

```
pad cli project preset add    <id> <preset>   # 挂一个选项（校验发行号）
pad cli project preset use    <id> <preset>   # 换默认（数组前移）
pad cli project preset remove <id> <preset>   # 摘掉（离线可用，不需要发行号）
pad cli project preset clear  <id>            # 全清，patch 层恢复合法
pad cli project preset        <id> <preset>   # 单绑（原有语义）
```

实验室实测（真实 0.2.1-rc.2，临时 root + junction）：

| 步骤 | agents | patch 默认 |
|---|---|---|
| `add minimal` | `["minimal"]` | minimal |
| `add ptc` | `["minimal","ptc"]` | minimal（add 不换默认） |
| `use ptc` | `["ptc","minimal"]` | **ptc** |
| `remove minimal` | `["ptc"]` | ptc |
| `remove ptc` | `[]` | 管理块移除 |
| `clear` | `[]` | patch 只剩注释 + `[]`（合法） |

`remove` 后仍保留用户的 patch 行；清空后 patch 文件回到 `[]`，DSH 下次 dump-config 正常。

## 验证

- `dotnet build` **0 warning / 0 error**；
- `pad-project.test.ts` 全绿，新增：preset 合并（`[]`/用户行/幂等/clear/PA125）+ 多 preset
  增删换（F 段，`project.json` 的 agents 顺序逐步断言）；
- 回归 `pad-window` / `pad-depth` / `pad-pack` 全绿。

## 产物

| 文件 | 说明 |
|---|---|
| `run-detach.json` | detach 后的 run 结果（新 token、preset minimal） |
| `cli-detach.out` | CLI 生命周期：launched → url → wrote，进程 20.3s 退出 |
| `presets-before-p2.json` | 多 preset 前的项目状态（bound=[minimal]） |
| `preset-add.json` / `preset-use.json` / `preset-remove.json` / `preset-clear.json` | 四个动作的输出 |

## 下一步：UI 接线（本轮未做）

UI 在并行实现线的域里（`WorkbenchView.xaml.cs` 2757 行、`ProjectRail`、`MainWindow` 舞台），
一次改动会横跨主页「在跑」卡、项目页、管理页两条表示，需要单独一轮盯完；计划：

1. `Core/ProjectHub.cs`：读 `Projects.List()`，映射 project → instance → `RunPhase`/`RunFacts`；
2. `ProjectRail`：新增项目视角（在跑/已停分组 + 搜索），行 = logo + 名称 + 工作区数 + preset；
3. `WorkbenchView`：主页「在跑」卡与项目页改读 ProjectHub；项目页加「启动/停止、preset chips、
   工作区列表、跳转会话」；
4. 管理页保留两条并列表示：实例（运行层）/ 项目（绑定层），改名/合并/拆开走对话框；
5. 用 PAD 自带 `--shot-page manage-project` 截图当验收证据。
