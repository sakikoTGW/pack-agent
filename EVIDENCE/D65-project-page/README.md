# D65 · 项目页（启动/停止、preset chips、工作区、对话）

用户选择「2：先做项目页（启动/停止/preset chips），侧栏随后」。

## 做了什么

`WorkbenchView.xaml.cs` 里新增项目轴视图，**逻辑全部复用已有实现**，不在视图里复制一套：

| 能力 | 接的是 |
|---|---|
| `ShowProject(proj)` / 页头 `ProjectHeader()` | `Projects`（project.json 主数据）+ `RunWatch`/`RunFacts`（运行现场） |
| 启动/停止 | `ProjectRunner.RunAsync`（D62：ensure 实例 → seed → 起窗口/聚焦）与 `Runner.Stop` |
| preset chips + 「添加」菜单 | `AgentPresets.ProbeAsync`（发行号可选列表）+ `AgentPresets.Apply`（写 profile patch 层） |
| 工作区 / 对话 / 环境卡 | 现有 `DialogCard` / `EnvCard`，scope = 项目实例 |

- **页面入口**：主页卡片区新增「项目」卡（`ProjectAxisBody`，点行 `ShowProject`）；`--shot-page
  manage-project` 也改为优先打开项目页（D57 截图口径不变）。
- **页头事实**：`2 工作区 · preset minimal · 实例 ev-project` + 运行灯 + 停止/启动按钮。
- **启动卡**：状态词、pid、端口、已跑、工作区数、默认 preset；动作 = 启动/停止、打开界面
  （账本里的带 token URL）、打开工作区。
- **preset 卡**：已绑 chip（主色 = 默认，点其它 chip = 设成默认）、「+ 添加」弹菜单挂发行号里
  还没绑的 preset；下方写明发行号可选与「chip 改默认、添加只挂选项」。
- **工作区卡**：项目绑定目录（点名字打开）。

## 实拍

`project-page.png`（PAD 自带 `--shot` 口径，真实本机 root，ev-project 正在跑）：

- 页头：`ev-project · 2 工作区 · preset minimal · 实例 ev-project`，右上「停止」；
- 启动卡：`在跑`、pid 58876、端口 `127.0.0.1:3080`、已跑 10 小时 35 分、工作区 2、默认 preset minimal；
- agent preset 卡：`minimal`（默认，主色 chip）、`+ 添加`、发行号可选 `standard / ptc / minimal / cordis`；
- 工作区：`ws-frontend` / `ws-backend`；对话卡显示这个项目 home 的会话；
- 环境卡：内核 0.2.1-rc.2、组合包 2、会话 1、profile 1 个。

## 验证

- `dotnet build` **0 warning / 0 error**；
- `pad-project.test.ts` 新增「项目页接线」源码契约（`ShowProject`/`StartProjectAsync`/
  `ProjectPresetCard`/`ProjectAxisBody`/`PaintProjectCards`/`ProjectRunner.RunAsync`/`AgentPresets.Apply`）全绿；
- 回归 `pad-window` / `pad-depth` 绿（UI 只是加视图，测试口径没动）。

## 还没做（下一轮）

1. **侧栏项目视角**（用户已排在「随后」）：在跑/已停分组 + 搜索，行 = 项目 logo + 名称 +
   工作区数 + preset；侧栏目前仍是实例视角（截图里 6 个实例）。
2. 主页「现在流」的项目行：卡片已实现（`ProjectAxisBody`），但 home 截图要等
   `LoadHomeAsync` 扫完（`--shot-page manage` 默认延迟不够，这次没拿它当证据）。
3. 管理页「实例 / 项目」两条并列表示的切换与改名/合并/拆开对话框。
4. 会话跳转的可点击行（现在对话卡里已有行，但还没接「点一行切窗口」）。

> 新构建在 `E:\tmp\pad-proj-build`（没覆盖正在跑的 PAD）。要在真实界面看这一页，需要用新 exe
> 重启一次 PAD；合流时统一做「单次重启 + EVIDENCE 截图」。
