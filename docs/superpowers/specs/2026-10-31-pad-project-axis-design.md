# PAD 项目轴（Project Axis）设计 · D53 候选

date: 2026-10-31
status: **视觉已定版（2026-10-31，用户「够了，定这版视觉」）；实现按 §10 收敛**
关联：DECISIONS D17（壳不可分家）/ D20（一项目 = 一份 home）/ D35（页面按视角分侧栏）/ D50（工具链自愈）
原型：`.tmp/pad-project-mockup.html`（项目模式 / 现状对比两种状态）

---

## 0. 一句话

**项目 = 绑定层**（一组工作区 + 一组智能体 preset + 默认版本与窗口形态），**实例 = 运行层**（一份 `DSH_HOME`）。
一个项目窗口只挂自己的 home，所以 DSH 右侧工作区/会话天然只有这个项目的东西 —— 不是 UI 过滤，是数据真值。

## 1. 需求原话与现状

用户原话（2026-10-31）：

> 我要开两个项目，一个是制作 qwxy 平台，一个是 DSH 做视频。我可以把相关的 dsh 的 agent 绑定在下面，
> 这样我启动 dsh 右边的工作区就只会有这个项目的东西，而不是杂糅在一块。
> 以项目为基础，以每个 dsh 窗口为原子。一个窗口就是一个项目的工作区域。
>
> 这只是表现形式的问题，理论上来说，应该和按实例表示处于并列关系。

现状（证据见 `.tmp/briefs/pad-ia.md`）：

- 官方桌面壳只有一个 home（`E:\DSH-Home`），左侧工作区 19 条全混在一起；壳不可多开、换不了 home（D17）。
- PAD 侧「项目」不是一级实体，只有三种派生形态：`Instance.Workspace{Kind,Path}`、运行期 `WorkspaceRow`、
  `Graph` 按路径 `GroupBy` 的 `WorkspaceNode`。
- PAD 管理页已有「项目视角 = 实例清单」（D35），但 `WorkbenchView` 左栏不订阅 `Picked`，点选不联动（IA §4.2）。

## 1.5 横向对照：GitHub 上跑通的项目（2026-10-31 查）

| 项目 | star | 它怎么组织的 | 抄什么 | 不抄什么 |
|---|---|---|---|---|
| [PrismLauncher](https://github.com/PrismLauncher/PrismLauncher) | 11k | 每实例独立目录；全局/实例两级设置；实例列表 | 设置分层（空=跟随全局）、实例列表分组/搜索 | Minecraft 资产共享那一套 |
| [vibe-kanban](https://github.com/BloopAI/vibe-kanban) | 29k（已宣布 sunset） | 项目 → issue → workspace（分支+终端+dev server），每 workspace 一个 agent | 项目/工作区/会话三级、每工作区独立工作副本 | kanban 任务流（PAD 是启动器，不是看板） |
| [OpenHands](https://github.com/All-Hands-AI/OpenHands) | 92k | 会话 = 独立 runtime/容器；项目级 microagents | 一会话一隔间、agent 配置跟着项目 | 容器沙箱（P0 不做沙箱） |
| [claude-squad](https://github.com/smtg-ai/claude-squad) | 8.7k | 一 agent 一目录一 tmux 会话 | 一 agent 一窗口、运行状态可见 | tmux 进程托管 |
| [crystal](https://github.com/stravu/crystal) | 3.1k | 多 Claude 会话的桌面容器 | 会话列表/恢复 | — |
| [neeko](https://github.com/tincopper/neeko) | 12（活跃，2026-10） | 多项目 agent 管理器：**一项目一常驻 PTY**，切项目不杀；每项目可覆盖设置；重启恢复列表/窗口 | **一项目一窗口常活 + 每项目覆盖 + 重启恢复**、项目色/重命名 | 内置编辑器/IDE（PAD 不把 DSH 装进自己窗口） |
| [kild](https://github.com/Wirasm/kild) | 65 | 项目注册表 `project add/list/info/default/remove` + 用户级/项目级 TOML 分层 | **项目注册表 CRUD + 配置两层** | git worktree 强依赖 |
| [opentree](https://github.com/axelgar/opentree) | 5 | 工作区 = worktree + 分支 + tmux 窗口；状态放 `~/.opentree`，不写工作树；每工作区端口持久 | **状态不进项目目录 + 端口持久** | git/tmux 强依赖 |
| [sunChargedd/dsh-plugins](https://github.com/sunChargedd/dsh-plugins) | — | DSH 官方 Web 插件（`index.js` host + `client.js` 浏览器半），已上架市场，能给侧栏加面板（SSH / skills） | 第二期「项目面板/项目切换」走 `dsh.client` | — |

**共同模式**（这是「跑通」项目的共识，不是我们发明的）：

1. **项目 = 一个目录**（多数还要求是 git repo）；agent 配置跟着项目目录走（`.claude/`、`.opentree.toml`、`.kild/config.toml`），不放在启动器数据库里。
2. **窗口/会话按项目开**，切换项目不杀进程；状态在侧栏可见。
3. **设置两层**：全局 + 项目覆盖（空 = 跟随全局）。
4. **列表按 在跑 / 已停 分组 + 计数徽标 + 搜索**。
5. **状态与会话元数据放在工作目录之外**，不污染项目。
6. 它们全都绑 git/worktree，因为它们的 agent 在改代码；**DSH 的 workspace 是任意目录**，我们不该把 git 当门槛（差异点，写清楚）。

> 出处说明：neeko / kild / opentree / dsh-plugins / vibe-kanban 为 README 实读；PrismLauncher / OpenHands / claude-squad / crystal 只核了 star 与产品定位，隔离细节由调研子代理补。

### 1.6 据此对设计的修订

1. **项目配置允许放在项目目录**：`<launcher-root>/projects/<id>/project.json` 之外，允许项目目录内 `.pad/project.json`（可进 git、跟着目录走），root 里只做索引；两者冲突时项目内优先（并进 PA122 判定）。
2. **不要求 git**：项目 = 目录集合；P1 不引入 worktree。
3. **项目注册表是 CRUD 列表**：`pad cli project add|list|info|default|remove`，对应 kild 的命令面；P1 至少 list/add/remove。
4. **列表语言**：在跑 / 已停 分组 + 计数徽标 + 搜索框（PAD 现有列表都没有搜索，IA §4.3 已指出的缺口）。
5. **端口持久化**：每项目固定端口写进 project.json，窗口重启后 URL 不变（opentree 的做法）。
6. **重启恢复**：PAD 重开 → 项目列表 + 上次选中项目 + 窗口映射恢复（neeko 的做法；复用 `AppState` 与 `runtime.json`）。
7. **状态不写项目工作区**：只写 `<launcher-root>` 与 `home/`（opentree 的做法；与既有 `.agent-pack` 投影例外保持一致）。
8. **一期仍是「一项目一窗口」**；将来要同项目多窗口，照 vibe-kanban 的「每窗口独立工作副本」做，而不是共享 home。

## 2. 为什么不能「同一个窗口里过滤」（DSH 侧硬事实）

全部来自对已装 DSH 包（`versions/0.2.1-rc.2/node_modules/.pnpm/.../@deepseek-ai/*`）的只读调查：

| # | 事实 | 证据 |
|---|---|---|
| F30 | 工作区列表 = `<DSH_HOME>/storages/workspace.json` 的**全量投影**（`storage-json` single 布局，`unit.name=workspace`、`version=2`） | `dsh-base/cordis.patch.yml:182-190`、`dsh-workspace/lib/index.js:255-268,454-460` |
| F-new | **没有任何** workspace 的 allowlist / roots / hidden 配置；`dsh-workspace` 连 `Config` 都没有 | `dsh-workspace/lib/index.js`、`package.json` |
| F-new | 唯一过滤钩子 `remoteAccess.allows()` 对 owner（本地窗口）恒为 `ALLOW` | `dsh-api-remote-access/lib/index.js:224,252-255` |
| — | 所以「同 home 白名单」只能改 `dsh-api-workspace-controller.follow()` 或 `dsh-workspace.list()` 源码 | `dsh-api-workspace-controller/lib/types/index.js:209-226` |
| F2 | `dsh`/`dsh web` 没有 `--workspace` / `--session` / `--preset`；home 只能靠 `DSH_HOME` | `@deepseek-ai/dsh/lib/bin.js:105`、`dsh-web-app/lib/startup.js:25` |
| F-new | `--profile` 只隔离组合与 node_modules；sessions / storages / credentials / attachments / skills **全部按 home 共享** | `dsh-app-boot/lib/index.js:1128-1136` |
| F-new | 手写 `workspace.json` 不合 schema 会让 DSH **启动失败**（fail-loud，不备份跳过） | `dsh-storage-domain/lib/index.js:368-381`、`dsh-workspace/lib/index.js:833-857` |
| F2/F29 | agent preset：0.2.x = 组合里的一行（`preset-standard/ptc/minimal/cordis`，进程内 `definitions` 全量 list）；0.1.x = `<home>/.agent-presets/<id>/` 目录 roots | `dsh-web-app/presets/*.patch.yml`、`dsh-agent-preset-registry/lib/index.js:441-444,570-581`、`dsh-agent-presets/lib/index.js:808-815,887-889` |
| — | 0.2.x 没有 apiproxy（`pad-gateway` 只服务 `>=0.1.0-rc.7 <0.2.0`） | `.pnpm` 全树 + `gateway/package.json` |

**结论**：工作区可见性只能靠「换一份 home」；preset 可见性可以按 home/进程裁剪（patch 或 roots）。

## 3. 数据模型：项目是本地主数据，实例是运行体

```
<launcher-root>/projects/<id>/project.json     ← 新增，项目（绑定层，唯一真源）
<launcher-root>/instances/<id>/{instance.json,home/,workspace/,launch-*.cmd}   ← 已有，实例（运行层）
```

`project.json`（schema `pack-agent.launcher.project/v1`）：

```json
{
  "schema": "pack-agent.launcher.project/v1",
  "id": "qwxy",
  "name": "qwxy平台",
  "display": { "logo": null, "note": null },
  "workspaces": [
    "E:\\projects\\qwxy-platform",
    "E:\\projects\\qwxy-docs"
  ],
  "agents": ["standard", "code"],
  "dsh": { "version": "0.2.1-rc.2", "profile": "web" },
  "window": { "kind": "web", "shellExe": null },
  "instance": "qwxy",
  "lastRun": "2026-10-31T20:00:00Z",
  "created": "2026-10-31T19:00:00Z",
  "updated": "2026-10-31T20:00:00Z"
}
```

规则：

1. **项目持有 `instance` id**；PAD 启动时自动建/复用该实例（复用 `Launcher.CreateInstance` 的原子写入与 `RewriteLaunchScripts` 总闸）。
2. **工作区是绝对路径数组**（realpath 校验必须存在且是目录）；第一个工作区 = 窗口默认工作区。
3. **智能体是 preset id 数组**，来源 = 该实例该 profile 的名册（`Launcher.ListAgentPresets` / 0.2.x 的内置四行）。
4. **实例可缺省**：`project.json` 先于实例存在也合法（绑定先配、启动再建）。
5. 项目与实例是**并列表示**：管理页可切换「按项目 / 按实例」，两套行不互相改写。

## 4. 启动链（`pad cli project run <id>` 与启动按钮同一条）

```
project.json
  → 1 校验（PA122/PA123）
  → 2 确保实例（instances/<id>/，home+profile）
  → 3 写 home/storages/workspace.json（PA124，失败保留 .bak）
  → 4 写 preset 绑定（PA125；0.1.x 用 .agent-presets + launcher.patch.yml，0.2.x 用 profiles/<p>/cordis.patch.yml）
  → 5 起窗口（web: LaunchWeb + Edge --app=；shell: Nodes.OpenApp + --user-data-dir）
  → 6 TrackRun 写 runtime.json（instance = project id）
```

### 4.1 `workspace.json` 写入规则（唯一高风险点）

- 结构：`unit{name:"workspace",version:2}` + `global{initialized:true,workspaceIds[],archivedSessionIds[],pinnedSessionIds[],defaultWorkspaceId}` + `tables.workspaces{uuid → {path,title,sessionIds,createdAt,updatedAt}}`。
- **合并而非重写**：已存在的同 path 记录保留原 uuid 与 `sessionIds`（否则第二次启动会丢会话归属）；新增目录才生成 uuid 并 append 到 `workspaceIds`；删除只从 `workspaceIds` 与表里移走该项目自己的条目。
- `path` 必须与 DSH 的 realpath 一致（`fs.realpath` 语义：Windows 下统一盘符大小写、解析 junction）。
- `initialized:true` 会短路 DSH 的「自动建默认工作区」。
- 写前：`workspace.json` → `workspace.json.bak`；写用 `Launcher.WriteAtomic`（临时文件 + 替换）。
- 校验：`workspaceIds` 无重复、全部能在表里找到、`initialized` 时表与顺序等长、同 session 不挂两个 workspace。**不满足就不写，报 PA124 并把旧文件原样留着。**

### 4.2 preset 绑定

| DSH 线 | 做法 | 风险 |
|---|---|---|
| 0.1.x（`dsh-tui` 线，apiproxy 可用） | 把选中 preset 拷进 `home/.agent-presets/<id>/`（复用 `Launcher.CopyAgentPreset`）；在 `home/launcher.patch.yml` 覆写 `dsh-tui-agent-presets.config`：`roots` 指向项目目录 + `includeUserRoot:false` + `default` = 第一个绑定项 | 需确认目标 profile 用了该包（tui 组合包自带） |
| 0.2.x（web 线） | 在 `home/profiles/<profile>/cordis.patch.yml` 写：`- id: preset-ptc/preset-minimal/preset-cordis` + `disabled: true`；`agent-preset-registry.config.default` = 第一个绑定项；项目自建 preset 用 insert 行 | `selectedDefault` 也会落在同一个共享 patch，同一 home 多窗口会互相覆盖 → 一期规定「一项目一 home 一窗口」 |

## 5. 界面（第一期）

- **不新造 tab**：管理页沿用「左栏 + 右栏」，左栏顶部加分段「项目 / 实例」。
  - 项目模式：行 = 项目（名称 + 工作区数 + 智能体数 + 在跑点），分组头 = 无（项目本身就是顶层）。
  - 实例模式：保持现有 `ProjectRail.Fill` 的行为不动。
- 右栏 = 项目详情：工作区（路径 + 增删 + 「设为默认」）、智能体（勾选 preset）、窗口（形态 / 版本 / profile）、启动/停止。
- **补齐 IA §4.2 的断链**：`WorkbenchView` 订阅 `ProjectRail.Picked` → 统一 `AppState.SelectInstance/SelectProject`。
- 启动后 DSH 窗口不改（它就是普通 web/壳窗口），PAD 只负责 home 与 registry 正确。

## 6. 错误码（接 PA117 之后）

| 码 | level | 主句 | 触发 |
|---|---|---|---|
| PA122 | error | project definition is invalid | `project.json` 坏 schema / id 冲突 / 字段缺失 |
| PA123 | error | project workspace path is not a directory | 绑定的工作区不存在或不是目录 |
| PA124 | error | could not write the workspace registry | `workspace.json` 校验/写入失败（保留 `.bak`，不猜） |
| PA125 | error | could not bind agent presets for this project | preset 拷贝/patch 写入失败 |

## 7. 验收 / 测试

- **单测**（`bun run agent-pack-dsh/modpack/pad-project.test.ts`，模板抄 `pad-depth.test.ts`：临时 root + 真跑 `pad cli project ...`）：
  1. 建项目 → `pad cli project run` 前先 `pad cli project registry qwxy`（dry-run 输出）→ `workspace.json` 的 `initialized=true`、`workspaceIds` 与绑定目录一一对应、无 default workspace 自动生成；
  2. 第二次 run：同 path 记录 uuid 不变、`sessionIds` 保留、不产生重复 uuid；
  3. 绑定不存在的目录 → PA123，`workspace.json` 未被修改（hash 前后相等）；
  4. 0.1.x 线：`launcher.patch.yml` 出现 `dsh-tui-agent-presets` 覆写；0.2.x 线：`profiles/web/cordis.patch.yml` 出现 `disabled: true` 的三个 preset 行；
  5. 项目与实例并列：`pad cli project list --json` 与 `pad cli instance list --json` 互不影响。
- **构建门**：`dotnet build` 0 warning / 0 error；`pad-window.test.ts`、`pad-pack.test.ts`、`pad-depth.test.ts` 全绿。
- **实机**：EVIDENCE/D53-project-axis/：建 `qwxy`、`dsh-video` 两个项目 → 各起一个窗口 → 截图证明侧栏只有本项目工作区、另一项目不受影响；PAD 只构建一次、重启一次（pid 记录）。
- **反向验收**：手工把 `workspace.json` 改坏 → 启动必须报 PA124 且不启动窗口（而不是让 DSH 崩）。

## 8. 分期

| 期 | 内容 | 判据 |
|---|---|---|
| P1 | `project.json` 主数据 + 管理页项目/实例分段 + `workspace.json` 合并写入 + web 窗口 + PA122-124 + 测试 | 两个项目窗口各看各的 |
| P2 | preset 绑定（先 0.1.x，再 0.2.x）+ 第三方壳窗口（`--user-data-dir`，`Nodes.OpenApp` 接进启动链） | 窗口里智能体列表只有绑定的 |
| P3 | 在线校准（gateway `workspace.list` 对齐项目表）+ 项目改名/归档/多实例 | 离线可用、在线一致 |

## 9. 风险（写进 DECISIONS 的那几条）

1. **registry fail-loud**：DSH 对 `workspace.json` 的任何 schema 偏差都是启动失败，不是降级。所以 PAD 必须「校验→备份→原子写」，并把失败留在 PAD 侧（PA124）。
2. **一项目一 home 的成本**：sessions/凭据/技能/插件各一份；`launch-*.cmd` 已有凭据复制与 profile 清理补丁，继续沿用。
3. **0.2.x 无 apiproxy**：管理口（workspace.list/session.list）对 0.2 实例不可用 → 项目页不能依赖在线数据，离线主数据是唯一真源。
4. **官方壳不能多开**（D17）：项目窗口只能是 Edge 网页窗口或第三方 `DSH Desktop.exe --user-data-dir`；要在文档里说清，不让用户以为能开两个官方壳。
5. **preset 漂移**：0.2.x 的 `selectedDefault` 写回共享 profile patch；一项目一窗口一期硬约束，后续若要多窗口需换 home。

## 10. 视觉定版（2026-10-31）

状态：**定版**。原型、屏幕、交互、动效以本节为准；实现按本节收敛，不再改方向。
前置约束（用户 2026-10-31 明确）：**DSH 界面一行不改**（不装 client 插件、不动 asar）；隔离靠 PAD 起的独立 home；状态 / 预览 / 跳转 / 反馈全部在 PAD 侧。

### 10.1 屏幕

- **第一屏「现在」**：按「需要你」排序（等你输入 > 在跑 > 刚完成）。顶部三格计数（可点筛选、可悬停看成员），下面是流；每条 = 状态图标 + 会话名 + `项目 · agent · 正在做什么` + 状态 pill + 时间。点一条 = PAD 把窗口切到该项目/会话 + toast + 窗口聚焦脉冲（**DSH 窗口本身不改**）。
- **第二屏「管理」**：项目详情 = 运行卡 / 智能体团队 / 工作区 / 任务 / 能力 / 最近会话 / 配置 bar（配置只留一条，点「编辑」进设置）。
- **「自动收编」页**：19 个工作区 → 6 个项目，0 次手动操作；规则卡（只读 DSH、本地保存、删项目 = 取消分组、可逆）。
- **左栏**：项目（在跑 / 已停分组 + 搜索）或 实例（分段切换）。

### 10.2 视觉 token

- 色板：冷、低饱和。accent `#1d5d78`；背景 `#eef1f4`；面板 `#f7f9fa`；卡 `#fff`；线 `#e3e9ee`；
  状态色：在跑 `#b4762a`、等你输入 `#7c55b8`、已完成 `#22855a`、空闲 `#a9b6bf`。
- 项目 logo：30px 圆角 9 渐变 tile（每项目一组主/辅色）+ 线性图标（代码 / 胶片 / 柱状 / 网络 / 分支 / 终端）；hover 放大 6% + 轻微旋转。
- 字号：大标题 19、卡片标题 12、正文 11.5–12.5、元信息 9.5–10.5；圆角 10–12；间距 4 / 6 / 8 / 10 / 12。

### 10.3 图标

- 24×24 线性规范（Lucide / Feather 风格：2px 描边、圆角端点），内联 SVG symbol，无外部依赖；WPF 实现用 `Theme/LineIcons.xaml` 同一规范。
- 禁止：emoji、字母/首字母当 logo、线性与填充图标混用。

### 10.4 动效预算

- 缓动 `cubic-bezier(.22,.7,.3,1)`；交互 150–320ms，不炫技。
- 列表进场 30ms 错峰（fade + 上移 4px）；tab 滑块 320ms；在跑条目底部 shimmer 1.7s；状态点呼吸 1.1s；toast 2.4s 自动收；切窗口时窗口描边脉冲 1.4s；按钮 hover 抬 1–2px、press 微缩。
- 实现时必须支持 `prefers-reduced-motion`。

### 10.5 悬停 ＝ 预览（唯一允许的悬停）

保留（都是「点进去会看到什么」的预览）：
1. **项目行** → 项目卡（预览「管理」页内容：工作区清单 / 智能体数 / 端口 / 会话数）；
2. **「现在」条目** → **对话预览**：最近两轮，每轮 user/agent 气泡 + 工具调用行（`✓ 编辑 … +18 −2` / `⟳ 运行 pnpm test 2.1s`），底部 `时间 · 共 N 轮 · 上下文`；
3. **计数格、任务/能力/会话标题** → 成员预览（数字背后具体是哪几条；能跳的点一行跳）；
4. **运行卡 chip「N 在跑 / M 等你输入」** → 同成员预览。

禁止（评审中已否掉）：解释性 tooltip、重复行内已可见信息的 tooltip、纯元数据卡（pid / 内存 / 启动命令不进悬停，归运行卡或日志 / 设置）。已删除：工作区标题悬停、能力 chip tooltip、任务行 tooltip、智能体团队 / 运行卡的悬停明细。

### 10.6 交互规则

- 键盘：`↑` `↓`（或 j/k）选择、`Enter` 打开、`/` 搜索、`Esc` 取消筛选；
- 聚合数字：点 = 筛选，悬停 = 看成员；
- 点 / 回车 = 切窗口 + toast + 焦点脉冲；启动 / 停止 = toast + 状态迁移；
- 左栏项目 = 进第二屏；左栏「实例」分段 = 运行体轴。

### 10.7 定版产物

`docs/superpowers/specs/attachments/2026-10-31-project-axis/`：
`prototype.html`（可交互，`#adopt` `#conv` `#wait` `#manage` `#instance` `#hover` `#hover2` `#hover-wait` `#hover3`–`#hover7` 直达各态）+ 11 张定版截图（`01-now` … `11-sessions`）。

### 10.8 与并行实现线的对齐

仓库工作树里已有一条并行实现（DECISIONS **D53–D60** + `EVIDENCE/D53…D60`，截至 2026-10-31 23:31 仍未提交，`WorkbenchView` 等文件正在被修改）。**本节是视觉/交互的唯一验收基准**：那条实现线按 §10 收敛，冲突时以本节为准；DECISIONS 新条目顺延（D61+），不重复占用 D53–D60。
