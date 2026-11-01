# D66 · 侧栏项目视角 + 实例/项目两条表示 + 合流重启

用户选择「1，2，3」= 侧栏项目视角、管理页两条表示 + 改名/合并、合流重启验收。

## 1) 侧栏项目视角（`ProjectRail.FillProjects`）

- 分组：**在跑 / 已停**；行 = 项目名 + `N 工作区 · M preset · profile` + 运行灯；
- 顶部新增搜索框（按名字/id 过滤，只过滤不重建数据）；
- 数据由宿主算：项目 → 实例 → `StatusOf`/账本，侧栏自己不碰盘；
- 通行规则：主页/项目页喂**项目视角**，实例页喂**实例视角**（`FillRail`），点行 `ProjectPicked(id)` → 项目页。

实拍 `project-page-rail.png`：侧栏 `1 个项目 · 1 个在跑`、搜索框、「主页」、
「在跑」分组下 `ev-project · 2 工作区 · 1 preset · web ●`，右侧就是项目页。

## 2) 管理页两条并列表示 + 编辑菜单

- **项目 → 实例**：项目页启动卡「实例视角」→ 进入该实例的页（运行层）；
- **实例 → 项目**：实例页「项目」事实行 + 「项目视角」按钮 → 回到绑定层；
- 项目页「编辑」菜单：
  - **重命名…**：只改 `name`，id 与绑定不动（避免把已有窗口/账本断开）；
  - **合并到…**：把源项目的工作区（同路径去重）与 preset（并集）并进目标，删掉源项目，实例/目录/会话不动；
  - **取消分组**：只删 `project.json`，实例、目录、会话全部保留。
- 对话框是 PAD 自己画的窗口（`Views/WorkbenchProjectMenu.cs`），不引第三方控件。

## 3) 合流：新 exe 重启 PAD

- 旧 PAD 进程已不在；`dotnet build`（默认输出，0 warning / 0 error）→ 启动
  `bin/Debug/net9.0-windows/pack-agent-for DSH.exe`，新 PAD **pid 67308** 正常运行；
- `pad-live.png` 用 `PrintWindow(PW_RENDERFULLCONTENT)` 截的真实窗口（1000×620 启动页）；
- 项目页/侧栏/项目卡都来自同一个新构建（`--shot` 走同一份代码）。

## 验证

- build **0 warning / 0 error**；
- 四套测试绿：`pad-project`（含项目页/侧栏源码契约与多 preset 顺序）、`pad-window`、`pad-depth`、`pad-pack`；
- 实拍：`project-page-rail.png`（侧栏项目视角 + 项目页）、`pad-live.png`（重启后的真实 PAD）。

## 产物

| 文件 | 说明 |
|---|---|
| `project-page-rail.png` | 侧栏项目视角 + 项目页（在跑分组、搜索框、preset chips、工作区） |
| `pad-live.png` | 合流重启后的真实 PAD 窗口（新构建） |

## 已知边界

- 主页首屏是异步扫描（`LoadHomeAsync`），`--shot-page manage` 默认延迟可能截到空态，不作为证据；
- 侧栏搜索只做了源码 + 截图验证，没有指针级自动化；
- 改名/合并没有自动化 UI 测试（逻辑走 `Projects.Save/Remove`，已有 pad-project 覆盖存储层），合流后第一次真点时留意。
