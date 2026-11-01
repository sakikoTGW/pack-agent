# D53 · 项目行的"在跑"灯：读四路事实，行自己呼吸

用户（2026-10-31，附管理页截图）：

> 你要让 PAD 能读取如图哪个在启动，在运行，右边出现绿灯。并让这一栏呼吸闪烁。

## 1. 原来为什么不亮

左栏那盏灯本来是有的（`Views/RunLamp.xaml`，绿色呼吸灯），但判定只有一条：

```csharp
// 改之前的 ProjectRail.Row()
var live = Nodes.Ledger.Load(inst.Home).Any(e => Proc.Alive(e.Pid));
```

`Nodes.Ledger` 是 `<home>/.pad/nodes.json`，只有 `pad cli node open` 会写；**桌面壳的 home
从不写它，PAD 自己启动的实例也只写 `runtime.json` 不写它**。本机唯一在跑的
`本机 DSH`（`%APPDATA%\dsh-desktop\harness`）因此永远是灰的——灯装了，但没人喂它。

实测：`runtime.json` 的 `entries` 是空数组，mtime 停在 2026-10-23；而
`%APPDATA%\dsh-desktop\lockfile` 正被桌面端独占、进程现场里有
`pid 15776 DSH_HOME=…\dsh-desktop\harness`（独立核查见 §5）。

## 2. 改成了什么

### 2.1 判定收在一处：`Core/RunWatch.cs`

三态：`Stopped / Starting / Running`。证据四路 + 一个瞬态，**每一路都写进 tooltip**（`Detail`），
不写 pid、不下结论（D18）：

| 证据 | 什么时候有 | 来源 |
|---|---|---|
| PAD 账本 | PAD 点「启动」起的 run | `runtime.json` 条目，pid 还活着 |
| 节点账本 | `pad cli node open` 起的节点 | `<home>/.pad/nodes.json` |
| home 锁 | 桌面壳独占 home 父目录锁 | `LaunchPolicy.LiveHomeOwner` |
| 进程现场 | 双击 `launch-*.cmd` / TUI / 壳 | 进程的 `DSH_HOME` = 该 home（PEB） |
| 启动中登记 | 点「启动」到启动窗关闭之间 | `LaunchFloat` 调 `MarkStarting / ClearStarting`（45s 宽限兜崩） |

判据全部做**路径归一化**（`GetFullPath → TrimEnd('\\','/') → 忽略大小写`）：实例里存的是
`e:\`，进程里是 `E:\`；`Path.GetFullPath` 不纠正盘符大小写，靠比较器兜。不解析 junction
（DSH home 内 `profiles/node_modules/*` 全是 junction，解析会把灯算到别处）。

**进程现场要排除 PAD 自己**：PAD GUI/CLI 也会带 `DSH_HOME`（继承宿主 harness，或从某实例终端被拉起）。
现场核查发现 PAD GUI 进程的 `DSH_HOME=E:\DSH-Home`——不排除就会假绿灯。
`RunWatch.IsPadItself`（`pack-agent*` 家族）同时用在 `RunWatch.ProbeHomes` 和
`Discover.BuildHomes` 的 `procHomes` 里，界面深证据和 CLI 同一个口径。

### 2.2 界面：灯在行右端，行自己呼吸

- `ProjectRail.Row` 改成三列 Grid（logo | 名字 | 行尾灯），灯在**行最右端**；
  没在跑整盏收起（灰点没有信息，沿用 D29/D35）。
- 行底色单独一层 `AccentWash`，由 `Fx.Breathe` 做透明度呼吸（2.4s 往返；启动中 1.1s 更快）；
  关掉动画时给静止中点值（`Fx.Calm` 归零）。
- 状态快照变了**只改灯和呼吸，不重建列表**——重建会跳指针位置、丢 FLIP 连续性（D35）。
  `AppState` 只在快照真的变了才 `StatusChanged`（每 2s 一拍，便宜扫描：JSON + 活 pid + 试锁，不扫进程表）。
- 深证据（进程现场）不每 2s 扫：工作台加载时 `RunWatch.NoteHomes(Discover 的在跑 home)` 灌一次，
  2s 的轻扫描复用它。
- 项目卡（「123 / 0 / 未启动」那张）也改成同一份事实：字是 `启动中 / 在跑 / 未启动`，
  右边同一盏灯，状态变化时重画卡片区。

### 2.3 可测出口：`pad cli status`

```
pad cli status [--json] [--out <path>]
  文本   <Name>  <Phase>  <Detail>
  JSON   [{ "Id", "Name", "Phase": "Stopped|Starting|Running", "Detail" }]
```

CLI 一次性跑完，所以进程现场（PEB）当场扫一次；界面和脚本不可能各算一套。

## 3. 验证

- `dotnet build … -c Debug`：**0 警告 0 错误**（`build.txt`）。
- `bun run agent-pack-dsh/modpack/pad-window.test.ts`：**exit 0**，含新增两条
  `✓ 运行灯：三态判定 / 左栏呼吸 / cli status`、`✓ pad cli status 读账本：Running / Stopped / 纯文本一行`（`tests-pad-window.txt`）。
  实跑用例用 temp root：`runtime.json` 里放**活 pid**（bun 自己）→ `Running`；清空 → `Stopped`。
- `pad-depth` / `pad-pack` 也绿（同一次改动后复跑）。
- 真根状态表：`status-real.json` / `status-real.txt`：

| 实例 | Phase | Detail |
|---|---|---|
| 123 | Stopped | |
| pack-Test | Stopped | |
| pad-op-clone | Stopped | |
| pad-op-tmp | Stopped | |
| **本机 DSH** | **Running** | **home 锁 · 进程现场** |

- 实拍：
  - `manage-live.png` —— 真实 PAD 窗口（`--shot-page manage` 打开，pid 66340），
    `本机 DSH` 行右端绿灯 + 行底色呼吸，其余四行无灯。
  - `manage-lamp-a.png` / `manage-lamp-b.png` —— 同一页两次自渲染，`shotDelayMs` 分别 12000 / 13200
    （错开 1.2s，跑完把 `pad.json` 原样恢复）。
  - `breathing-a-vs-b.png` —— 把「本机 DSH」行带裁出来并排。
  - 像素差（1843×1314，阈值 ΔRGB>1）：

    | 区域 | 变化像素 | 平均差 | 最大差 |
    |---|---|---|---|
    | 「本机 DSH」行带 y=520–620 | **9642 / 35400** | 1.25 | 257 |
    | 同一列其余行 y=60–520 | **0 / 162840** | 0 | 0 |
    | 右侧卡片区 | **0 / 1253240** | 0 | 0 |

    只有那一行在动 → 灯 + 行呼吸；其余一像素不动。

## 4. 已知边界（不假装）

1. **「启动中」是 PAD 进程内的瞬态**：`LaunchFloat` 点下去登记、窗口关闭清掉。
   盘上没有可判定的痕迹（历史 `launch-*.cmd` / 失败日志都停在 10-23，用它们推「启动中」会误报）。
   `pad cli status` 因此看不到 Starting；界面看得到。
2. **app surface 的 home 错位**：`cli node open --surface app` 的壳 home 是 `<home>\.pad\desktop\harness`，
   不等于 `inst.Home`，只有 PAD 账本能兜住它——账本判据不能删。
3. **深证据的刷新时机**：进程现场那一路在管理页加载时刷新一次，之后靠 2s 轻扫描（账本/锁/节点账本）。
   双击脚本起的实例，最晚在下次进管理页时亮。
4. **不动别人的灯**：关系页的树形侧栏仍用 Gateway 的 `Live`（那是 agent 在跑，不是实例在跑），
   两者不混。

## 5. 独立核查（另一会话只读核对，摘录）

`E:\pack-agent\.tmp\runwatch-truth.md`（26.8KB，含完整命令与证据链）结论：

- `本机 DSH` 真值在跑：① `%APPDATA%\dsh-desktop\lockfile` 被独占（mtime 与壳启动对得上）
  ② 进程现场 `pid 15776 DSH_HOME=…\dsh-desktop\harness` ③ `discover Homes Running=true`。
- 其余四个实例未跑（账本无条目、无 nodes.json、无锁、procs 无匹配）。
- **只看 PAD 账本会漏掉的正好是唯一在跑的那一个**；另给出两条实现风险：
  进程现场排除 PAD 自身、路径归一化——都已按 §2.1 落实。

## 6. 复现

```powershell
$exe='E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\pack-agent-for DSH.exe'
$env:PACK_LAUNCHER_ROOT='E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher'

# 状态表（主真相）
& $exe cli status --json --out E:\tmp\status.json

# 自渲染（呼吸两帧：临时把 pad.json 的 shotDelayMs 改成 12000 / 13200，跑完改回 3200）
& $exe --shot E:\tmp\a.png --shot-page manage --shot-exit
& $exe --shot E:\tmp\b.png --shot-page manage --shot-exit

# 测试
cd E:\pack-agent
bun run agent-pack-dsh/modpack/pad-window.test.ts
```
