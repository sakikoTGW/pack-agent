# D54 · 2026-10-31 · 管理页：主页只放本机概览，选了项目就换成那份项目自己的页

用户（截图里的管理页，右侧一直是同一张「本机」仪表）：

> "现在有个问题就是，我切到哪个地方，这里右边显示的都是一样的画面。我的意思是，只有主页
> （对，管理有主页）显示这个窗口。我切到123或pack-test就显示有关那个的相关信息，而不是做
> 成一个固定的画面"

## 根因（现场）

- 管理页右栏 = `WorkbenchView` 的卡片区，所有卡片读的是**本机级**数据
  （`Discover.Run` 的 homes / 会话分布 / 工具调用）。
- 左栏 = 舞台 `ProjectRail`（容器在 `MainWindow`）。`WorkbenchView.AttachRail` 只喂侧栏、
  **不订阅 `Picked`**：点项目只改侧栏自己的 `_selected` 并重画自己，右栏和数据源都不动。
- 于是"切到哪个项目"只发生在左栏，右栏永远是本机仪表。这就是"切到哪都一样"的直接原因。

## 做法

管理页不新开 tab，按桌面端的常规分法换右栏：

1. **侧栏第一行固定「主页 / 本机概览」**（`ProjectRail.HomeRow`）：选中判据是 `_selected == ""`，
   点它发 `HomePicked`。主页不属于任何实例，但必须能选中、能回来。
2. **管理页订阅 `Picked` / `HomePicked`**：`Picked` → `ShowScope(inst)`，`HomePicked` → `ShowHome()`。
   同一处写入 `AppState.SelectedInstance`，"实例与组合包 / 版本设置"跟着同一份选中走。
3. **页头一分为二**：主页 = 「本机」+ 卡片编排（+ 卡片 / 编辑）；项目 = 项目名 +
   `版本 · 在跑/未启动 · 工作区` + 主页返回 / 版本设置 / 启动·停止。卡片编排只属于主页。
4. **项目页的卡片** = 同一套柔光卡（280 / 576 / 864 三段宽，一行 888 装满），每一项只统计这份实例：
   - 运行：账本里的活进程 / profile / 端口 / pid / 已跑 / agent 文案
   - DSH 内核：版本 / engines.node / 在不在 PAD 版本库 / profile 数 / 工作区类型
   - 组合包：按 profile 数（组合包挂在 profile 上，不在实例上）
   - 会话：该 home 的 session 数 + 近 7 天落盘柱 + 最近时间
   - 上下文 / 工具调用：`Inspect.Measure(home)` / `Usage.Measure(home)`
   - 整合包：该 workspace 的 `pack list`（走 pack-agent CLI；不可用就照实说"读不出来"）
   - 项目：工作区 / home / 来源 / 备注 + 打开工作区 / 实例目录 / 日志
5. **切换瞬间先摆对画面**（`PaintTransition`）：回主页用上一次的主页数据立即重画；进项目先给
   「读取中」。否则扫描没完成时会出现"标题已经换成新 scope、卡片还是上一个 scope 的"——那是在撒谎。
6. `--shot-page manage-project`：没有指针也能进项目页，截图可复现。

## 验证

- `dotnet build`：**0 warning / 0 error**（构建到临时输出目录，不压正在跑的 PAD）。
- `pad-window` / `pad-depth` / `pad-pack` 绿；`pad-window` 新增本条的源码断言
  （侧栏主页行、订阅 Picked/HomePicked、项目页取数、写 SelectedInstance、`manage-project`）。
- 真实 PAD 窗口（UIA 点击，非只跑测试）：

| 截图 | 说的是什么 |
|---|---|
| `home-live.png` | 管理主页：侧栏第一行「主页 / 本机概览」选中，右栏是本机仪表（在运行 / 本机 DSH / 上下文 / 近 7 天 / 工具调用 / 便签） |
| `project-live.png` | 点 `123`：标题变 123，卡片换成 123 的内核 0.1.0-rc.7、1 个 profile、2 个组合包、0 session |
| `project-packtest.png` | 点 `pack-Test`：同一位置换成 0.2.1-rc.2、0 profile、组合包空、这个工作区没有整合包 |
| `project-bottom.png` | 123 项目页滚到底：项目卡（工作区 / home / 来源 / 备注 + 三个打开按钮） |
| `final-home.png` | 点侧栏「主页」：右栏立刻回到本机仪表（用缓存画，不等扫描） |
| `project-merged.png` | 与并行会话的 D53 运行灯合流后，项目页仍照常（侧栏「本机 DSH」行亮绿灯） |

## 边界

- 项目页**不新开数据源**：卡片全部来自 `Launcher` / `Inspect` / `Usage` / `Packs` 已有入口，
  没有为了这一页发明新的判定（D6 / D18：PAD 只呈现，不判读）。
- 不改 `pad.json#ui.cards` 的语义：可自排卡片仍是**主页**的编排；项目页是固定版面。
- 与并行会话的 D52（项目状态快照）/ D53（运行灯）不冲突：本条的编号让位到 D54。
