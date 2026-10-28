# DECISIONS

> 一条一行，只记"为什么否掉另一个"。防止同一件事被反复提。

## D1 · 2026-09-21 · 壳从 Tauri 改为 .NET 9 WPF
否掉 Tauri，因为桌面窗要跟 PCL 同构（无边框顶栏、内页返回、卡片节奏），且 `pack-agent-for DSH.exe`
必须是**唯一真值**：窗口和 CLI 同一个二进制。WebView 壳会让"窗口"和"脚本"变成两套说法。
代价：失去跨平台。可接受——目标用户是 Windows 上的 DSH 使用者。
若日后要 macOS，重开这条，不要在 WPF 上打补丁。

## D2 · 2026-10-22 · A（启动桌面端）和 B（管理桌面端）都做，先 B 后 A
桌面端是 Electron 应用，不能用 `wt.exe --profile` 拉起，进程语义和 TUI 完全不同。
先做 B（读盘 + 改配置 + 看会话），风险为零、一个迭代能出；等体检层上线后再决定 A 的进程接管方式。
否掉"只做 A"：A 要改 LaunchPolicy / 进程检测 / 关闭语义，2–3 个迭代，且没有观测就接管进程是盲开。
否掉"只做 B"：长期看 PAD 要成为唯一入口，否则项目状态仍会分裂在两个地方。

## D3 · 2026-10-22 · 三层分离：标准 / 证据 / 判定
标准人写（或人签字）、证据机器产、判定机器算但人可否决。
否掉"AI 起草标准 + AI 自证通过"：那是盖橡皮章，不是验收。

## D4 · 2026-10-22 · PAD 不做多 harness
PRODUCT.md 已把跨壳故事划为旧仓级叙事。启动器只服务 DSH。
否掉"通用 AI IDE 启动器"：那是另一个产品。

## D5 · 2026-10-22 · 实例 = 项目，profile = 工具预算
否掉"全局一份 profile，项目靠工作区区分"：这正是工具面失控的成因——默认全量，没人问过。

## D6 · 2026-10-22 · PAD 不参与 agent 层
PAD 只管入口：启动、隔离、配置、把状态摆出来。不调模型、不写 prompt、不做语义判断、不替人验收。
`accept` 只执行 shell 断言并以退出码判定，不解读结果。
否掉"PAD 内置 AI 帮你判断做得对不对"：那会把判定层和 agent 层搅在一起，三层分离直接失效。

## D7 · 2026-10-22 · 前端不承载技术说明文档与防御性论证
界面不解释架构、不陈列理由、不预设反驳。这些属于 repo 里的 md，不属于窗口。
信息优先用视觉表达：logo、动效、比例、颜色、交互反馈。
否掉"把 STATE/ACCEPTANCE/DECISIONS 渲染进 UI"：那是把工程文档当产品界面，等于让用户读你的工作笔记。
一条自检：任何界面上出现"因为……所以……"的句子，就该把它删掉，改成看得见的状态。

## D8 · 2026-10-22 · 三级模型：系统 → 层 → 模块
**层 = 实例 = 工具预算；模块 = 工作区 = 会话 = 一组断言。**
否掉"11 个模块各开一个工作区"：没有层就没有工具预算，回到默认全量，71 个工具照旧，上下文照旧膨胀。
否掉"11 个模块各开一个实例"：11 份 `node_modules`，磁盘和时间都吃不消，而它们的工具需求其实是重复的。
真正的"架构"是模块之间的**依赖图**（信息搜集 → 数据表 → 展示页），不是并列目录。
细则见 `agent-pack-dsh/docs/SYSTEM-MODEL.md`。

## D9 · 2026-10-22 · 形态 Surface 与 Profile 正交
**Profile 决定装什么，Surface 决定怎么看见。** `tui` / `web` / `app` 三种形态，各有 Recipe / Launch / Endpoint / Stop 四个契约。
桌面壳不是特例，只是 `Exclusive = true` 的一个形态——PAD 不为它写分支。
否掉"把桌面端当唯一形态去适配"：那会让 web 和 TUI 变成旁路。
细则见 `agent-pack-dsh/docs/SURFACE.md`。

## D10 · 2026-10-22 · 整合包分两层载荷：内容 与 状态（地图）
内容层 = 投影（skill/MCP/rule/command/plugins）；状态层 = workspace + sessions，可选，类比 MC 的地图整合包。
**地图绑死绝对路径，搬运必须同时改三个锚点**：`workspace.json` 的 `path`、`sessions/<转义目录名>/`、每个会话第一帧里的 `identity.cwd`。
否掉"地图只带会话文件"：路径不改，装进去就是一堆打不开的孤儿会话。
装载策略必须显式：`append` / `replace` / `require-empty`，不许静默合并。
细则见 `agent-pack-dsh/docs/MODPACK-STATE.md`。

## D11 · 2026-10-22 · 工作区路径由结构推导，不存储
`workspace = <system>/layers/<layer>/modules/<module>/workspace`，由 (system, layer, module) 三元组算出。
**`plan.yaml` 里不写路径，整合包里也不写绝对路径**，只记 `originPath` 用于提示。
这样地图搬运只剩一件事：把 `originPath` 映射到推导路径。
否掉"包里存绝对路径"：那等于把打包机器的目录结构强加给接收方，而且三个锚点都要跟着改。
副作用是好的：工作区不再是人随手建的目录（`E:\How2VC` 那种），而是系统的结构产物。
实现：`Core/Systems.cs` + `pad cli system new`。

## D12 · 2026-10-22 · 插件按官方规范读，PAD 不重造运行时判定
规范：`deepseek-ai/deepseek-harness` → `docs/user/develop/basic/publish.md`。
两个 manifest 都在 `package.json#dsh`：bundle 声明 `dsh.bundle.patch`（字符串或**有序数组**），profile 声明 `dsh.profile.bundles`。
四条据此纠正：
1. 没有 `dsh-plugin.json` 这个官方文件——那是社区 RFC 的说法，不读它
2. in-box 的判据是**不在 profile 的 `dependencies` 里**，不是名字前缀
3. 版本兼容由 DSH 在安装和 profile 启动时自己拒绝（`engines.dsh`，没有则查 `@deepseek-ai/dsh*` peer 范围）——**PAD 只呈现，不重算**
4. 缺 `dsh.bundle` 的包照样能装，但只作为普通依赖、不激活层（`plain-dependency`）
否掉"PAD 自己实现一遍兼容性检查"：两份逻辑必然会漂移，而且违反 D6。
装载前验证用规范自带的 `dsh --profile <p> --dump-config`，不自己解析 cordis 树。
实现：`Core/Plugins.cs` + `pad cli plugin audit <home> <profile> [--surface …]`。

## D13 · 2026-10-22 · 整合包投影走 bundle 层
每个整合包 = 该 profile `dsh.profile.bundles` 里的一行，投影内容随这一行走。
否掉 `$DSH_HOME/cordis.patch.yml`：它是**机器级**层，一旦写进去，同一台机器的所有 profile 都会看见这个包——整合包就不再"跟着实例"了。
代价：装一个包要在 profile 里落一行，卸载要删干净；好处是隔离是结构性保证的，不靠约定。
验证方式：装包后 `dsh --profile <p> --dump-config` 里必须出现该包的层标记，且**别的 profile 里不能出现**。

## D14 · 2026-10-22 · 插件面由人勾选，PAD 不做自动裁剪
PAD 的职责是把**代价**摆出来（这一层带多少工具、多少 token、在哪个 surface 下会不会白装），然后**忠实执行人的勾选**。
否掉"自动识别无关插件并关掉"：那是替人做决定。
两条理由：
1. 自动裁剪会让人不知道自己手上这个实例装了什么——正是这次对话开头的问题（71 个工具从哪来没人说得清）。
2. 人勾选才有所有权。让人看见代价、自己删，比 PAD 悄悄删掉更有用。
推论：层→工具的归因仍然要做，但它服务的是**价签**，不是优化器。选择必须可回滚、可对比（勾选前后工具数/token 可见）。

## D15 · 2026-10-22 · 启动器只报事实，不报"省多少"
允许：**计算、追踪**已经发生的事——token 消耗、调用次数、会话轮数。
禁止：标注**反事实**——"关掉这一层能省多少 token"、"建议移除"、"可优化空间"。
理由：
1. "省多少"不可测量，只能估算；启动器一旦报估算，就等于给出承诺。
2. 报出来人就会依赖，PAD 就从启动器变成了优化器（与 D14 冲突）。
3. 事实自己会说话：一个工具被调用 0 次，人看见就知道该怎么处理，不需要 PAD 替他算账。
推论：`usage` 只输出两张表——消耗（token）与使用（次数），不带任何建议字段。

## D16 · 2026-10-22 · 形态启动按官方 CLI 契约，不自造
依据 `deepseek-ai/deepseek-harness` → `apps/cli/reference`。
`dsh --profile web --port <n> --no-open` 是官方支持的，默认端口 3080。
**推翻了先前"端口固定 43129、多开必撞"的判断**——43129 是 Electron 桌面壳自己挑的，不是 `dsh web` 的限制。
因此 `Endpoint` 契约取最简形式：PAD 分配端口、传 `--port`、`--no-open` 关掉 DSH 自己的浏览器交给，窗口归 PAD。
官方模板只有五个（`web` / `headless` / `sdk` / `sdk-minimal` / `acp`），`--from-default-profile` 可重置为新副本。
否掉"PAD 自己拼 bundle 列表建 profile"：官方模板已经覆盖，手搓只会和上游漂移。
注意 `desktop` 是保留 profile 名，dump 模式会拒绝——桌面壳走的是另一条路，不要混进来。
dump 有两个层次：`--dump-default-config` 只有 bundle 层，`--dump-config` 再加 profile/home/`--patch`。

## D17 · 2026-10-23 · `app` 形态不可分家：隔离只能用 `web` / `tui`
桌面壳用 Electron 标准的 `requestSingleInstanceLock()`；抢不到就 focus 既有窗口然后 quit（asar 里的原话）。
实测：`--user-data-dir <新目录>` → **退出码 9，目标目录根本没被创建**；而 `--version` 正常退出 0。
说明不是"任何第二次启动都被拦"，是那个开关本身不生效。
asar 全文扫描：`--user-data-dir` **一次都没出现**，`requestSingleInstanceLock` 有 → 壳把 userData 定死了。
**因此 PAD 无法让壳按项目分家**：共享壳 = 一份 home（`%APPDATA%\dsh-desktop\harness`），换 home 只能换壳，而壳不可多开。
→ 一个项目一个 DSH 只能用 `web` / `tui`（web 已实测：`--port` + 每项目一份 home + 独立窗口）。
`app` 形态就此降级定位：日常桌面端入口 + 壳在跑时明确拒绝（PA086），**不承担隔离**。
判据用独占打开 lockfile，不数同名进程——Electron 会起一堆 `DSH Desktop.exe`。

## D18 · 2026-10-23 · PAD 不做验收
PAD 是启动器。它把实例拉起来、把代价摆出来、把账本记下来，**到此为止**。
否掉 `pad accept <system>` 和"验收台"的定位：那是把测试运行器塞进启动器，属于 D6 禁止的判定层。
验收是**人的动作**，在项目自己的文件里发生（人看证据、人跑断言、人签字）；PAD 既不执行也不判读。
推论：
1. `modules/<m>/ACCEPTANCE.md` 只是建骨架时留的空文件，PAD 永远不读它。
2. 卡片/列表只显示**事实**（在跑 / 已停 / 端口 / 启动时间），不显示"待验收 / 通过 / 失败"。
3. 撤销此前在 `ACCEPTANCE.md`、`ATTENTION.md`、`SYSTEM-MODEL.md` 里把验收写成 PAD 职能的表述。
一句话边界：**PAD 管"起没起来"，不管"做得对不对"。**

## D19 · 2026-10-23 · 层 profile 用官方模板物化，不手搓、不启动
DSH 只对五个官方模板自动初始化 profile（`web` / `headless` / `sdk` / `sdk-minimal` / `acp`）。
层的名字是自定义的（`collect` / `frontend` / `backend`），所以 PAD 调：
```
dsh --profile <layer> --from-default-profile web --dump-config
```
`--from-default-profile` 负责复制模板，`--dump-config` 负责**只 compose 不 boot**。
实测：调用前 `profiles\<layer>\package.json` 不存在，调用后存在，全程没有启动任何服务。
否掉"PAD 自己拼 bundle 列表写 manifest"（D16 已否一次）——这条把它落成了可执行路径。
闭环：`system new` → `layers/<layer>/dsh-home` → `layer open` → 干活 → `node close`。
实现：`Nodes.MaterializeProfile` + `pad cli layer open|list`。

## D20 · 2026-10-23 · 系统 / 层 = 实验性功能，默认形态是"一项目一份 home"
`system` / `layer` 这套（系统 → 层 → 模块）降级为**实验性**：
- 不进主界面，不改 `ManageView`，不参与默认启动路径
- CLI 保留可用，帮助里标 `(experimental)`
- 默认形态回到最简：**一个项目 = 一份 `DSH_HOME` = 一个工作区**

保留它的唯一理由是"同一项目里不同职能要不同工具面"。这个需求没被验证过之前，它不该占据主路径。
退出的条件写死在这里，否则"实验性"会变成永久：
- **转正**：连续两周真实用到两层以上，且能说出省掉了哪些插件
- **删除**：一个月内没再用过 `system new` / `layer open`，连同 `Systems.cs`、`Nodes.MaterializeProfile`、`layer` 子命令一起删
判据是使用记录，不是感觉。

### D20 补充：像 DSH 的实验性 bundle 一样，默认关，手动开
开关在 `pad.json#other.experiments`（字符串数组，空 = 全关），与 DSH 对 experimental bundle 的姿态一致：
**随包发布，但不被任何人默认打开。**
```
pad cli experimental list            # 看有哪些、开没开
pad cli experimental enable layers   # 手动开
pad cli experimental disable layers  # 手动关
```
未开启时 `system new` / `layer list` / `layer open` 一律 `PA096` 拒绝，并直接给出开启命令。
实现：`OtherPrefs.Experiments` + `Cli.KnownExperiments` / `RequireExperiment`。
实测：关 → PA096；开 → pad.json 出现 `"experiments": ["layers"]` → 层列表正常；再关 → 又 PA096。

## D21 · 2026-10-23 · dsh CLI 路径：PCL 式解析，但嗅探不进启动路径
`launch.dshCli` 是 `launch.nodePath` 的兄弟（PCL 问 Java 在哪，PAD 问 node 和 dsh 在哪）。
解析顺序：**`--bin` 参数 → `pad.json#launch.dshCli` → release 记录里的 bin**。三者都没有就报 `PA094` 并点名要改哪个开关，不静默退化。
两个关键实现细节：
1. **asar 感知的存在性判断**（`LaunchPolicy.Exists`）：`…/app.asar/…` 下的路径对 `File.Exists` 永远不存在，
   但 asar 文件本身是真的。不处理这条，桌面端自带的 dsh CLI 永远填不进去。
2. **嗅探从启动路径里拿掉了**。`DshSniff.Bin()` 会 shell out（`which` / `npm root -g`），实测让 `node open`
   卡住 60 秒以上。PCL 嗅探 Java 是**一次性 + 有缓存 + 有进度**的，不在每次启动里跑；PAD 若要做嗅探，
   应当是设置页里的一次动作，而不是 `open` 的每一步。
否掉"嗅探作为最后一层兜底"：它把"找不到"变成"卡住"，比直接报错更糟。

## D22 · 2026-10-23 · 写设置不得触发依赖修复
`SaveSettings` 只做一件事：写 `pad.json`。它原先还调 `RewriteLaunchScripts()`，
那条链是 `Runner.WriteScript → PnpmHeal.Relink`，会遍历 pnpm store 重建 junction。
实测后果：`experimental enable` 慢几秒，并且 `PnpmHeal` 抛的**非 PadError 异常**一路冒到 CLI，
用户在"设置已生效"的同时看到一坨栈。
三处修正：
1. `SaveSettings` 去掉 `RewriteLaunchScripts()`；脚本在下次启动或 `pad cli rewrite-scripts` 时再生。
2. `RewriteLaunchScripts` 的 `catch` 从只捕 `PadError` 放宽到所有异常——重写双击脚本是尽力而为。
3. `WriteScript(..., heal: false)`：**重写文本不该顺带修依赖图**。`heal` 只在真正启动前保留。
实测：`rewrite-scripts` 从 >90 秒（跑不完）降到 **1.38 秒**；`experimental enable/disable` 从"慢+报错"降到 **~1 秒且无栈**。
判据（沿用 D21）：**低频的修复动作不能挂在高频路径上**。launch 是低频，save settings 是高频。

## D23 · 2026-10-23 · 缺陷扫描第一轮：三条通用判据
第一轮扫的是"输入不对 / 操作重复"，不是数 grep 命中。修掉的四个里有两个属于**静默错误**（看起来成功，实际是错的）。
由此沉淀三条判据，后续所有 CLI 都按这个自检：
1. **不是它该有的东西，要报错，不要输出零值。** `inspect <非 home>` 曾打印一排 0，和"合法但空"无法区分；
   `layer list <不存在>` 曾只打印路径并退出 0。现在分别是 `PA098` / `PA100`。
2. **重复操作不能覆盖痕迹。** 同一 home+profile 二次 `open` 曾把账本行覆盖掉，
   第一个进程变成"还在跑、没被记录、也关不掉"的孤儿。现在 `PA099` 直接拒绝。
3. **能在动作前判断的，不要等超时。** 显式 `--port` 被占用时，原先要等满 90 秒的 announce 超时；
   现在启动前 bind 探测，`PA004` 立即失败并指出占用者 pid。
另：`PadError` 只渲染自己的诊断，**不打栈**；只有非预期异常才带栈。栈是给开发者看的，不是给使用者看的。

## D24 · 2026-10-23 · 启动链三修：编码、早退、别人的 home
一次真实的启动失败（点「启动 本机 DSH · web」卡在 86%）暴露三件事：
1. **生成的 `launch-*.cmd` 按 cmd 的本命编码写。** 原先写 UTF-8 **带 BOM**，于是首行 `@echo off` 被解析成
   `锘緻echo off` 并报错；不带 BOM 又会让中文路径变乱码。改为按控制台 OEM 代码页（本机 936）写，
   无 BOM、中文与原样一致。`chcp 65001` 那行不再需要，也不再是救命稻草。
2. **子进程已经退出了，就别再等它宣告端点。** 脚本在非零退出时会 echo `[PAD] dsh exited with N`；
   启动等待原先只看 harness 是否活着，于是明知 CLI 已死还要耗满整个启动超时。现在一发现该标记，
   立刻抛 `PA120` 并附日志尾巴。
3. **拒绝在别人家的 home 上启动。** 采用（adopt）的实例若指向正在运行的客户端 home，脚本会
   `rmdir %DSH_HOME%\profiles\node_modules` 并覆盖 `.credentials.yaml`——这次没造成损坏，纯属运气。
   现在 `Runner.Launch` 先查该 home 及其父目录的 lockfile 是否被独占，是则 `PA121` 拒绝，什么都不碰。
判据：**脚本交给谁的解析器，就用谁的编码写；进程死了就别等；不属于自己的目录不要写。**

## D25 · 2026-10-23 · 收录：只认已知位置会漏，必须问进程
`pad cli discover [--deep <root>]` 出一份本机 DSH 清单：**shells / cli / homes**，每条带"是谁找到它的"。
三个刻意的取舍：
1. **桌面端的安装位置不可枚举，所以问运行中的进程。** 这台机器的客户端装在自定义目录
   （`E:\新建文件夹 (5)\DSH Desktop`），按已知位置扫是 0 命中；改为读 `Process.GetProcessesByName("DSH Desktop")`
   的 `MainModule.FileName`，立刻得到真实路径。
2. **home 的"运行中"不能只看 PAD 账本。** 桌面端的 home 从不写 PAD 的账本，看账本永远是 idle。
   改为：账本活着的 pid **或** 该 home 的父目录 lockfile 被独占。
3. **深度扫描必须显式给根。** 全盘遍历放进入口就等于重演 D21。`--deep` 有深度上限与跳过名单
   （node_modules/.git/obj/bin/Cache/drafts），并设 20000 目录的硬上限。
已知缺口：asar 内的 CLI 读不到 `package.json`，版本号为空；可退化为"跟随所属 shell 的版本"，尚未做。

## D26 · 2026-10-23 · 窗口尺寸由页面决定，用户不可手调
PAD 回到 PRODUCT 的原原则：**固定尺寸、只留最小化与关闭**，不提供拖拽改尺寸。
但"固定"不等于"小"——管理页承载网格，必须够大。规则：
```
启动 / 下载 / 设置     1000 × 620
管理                   工作区的 72% × 86%
                       本机实测工作区 1707×1019 -> 约 1229 × 876
                       下限 1200 × 760，上限不越出工作区
```
切页时做尺寸过渡（动效），而不是瞬间跳变。
推论：**管理页内容区约 850 宽，一行只能放三张 268 的卡片，不是四张。**
之前几张 1500×930 的原型画宽了，那是 4 列，本机放不下。
网格列数按内容宽度自适应：≥1100 四列、850–1100 三列、<850 两列。
否掉"让用户手动调尺寸"：一个页面对应一种信息密度，交给用户调只会让每个页面都有两种坏尺寸。

### D26 补充：尺寸切换不做动画（实测）
第一版用 `BeginAnimation` 过渡 Width/Height/Left/Top。实测结果：**宽度跟着动画走了，高度停在 620**。
`--shot` 旁注留下了证据：`want 1229x876 from 1000x620 animate=True fx=True`，而 `logical 1228.7x620`。
改成直接赋值后立刻正确：`logical 1228.7x876`。
结论：窗口尺寸切换用直接赋值，不做动画。理由有两条——WPF 下动画 Window.Height 不可靠；而且逐帧重排本来就会抖。
柔化留给内容层（页面淡入已经在做）。`--shot` 旁注保留 `<page>: want … from … animate=…`，以后尺寸不对可以直接证明。

## D27 · 2026-10-24 · 管理 = 工作台主页，旧管理降级为内页
把 `manage` 指向新的工作台（`WorkbenchView`），原来的「实例 + 组合包」控制台**不是删掉，是降级成内页**
`bundles`，入口是工作台「项目」标题右侧的「实例与组合包」，返回走顶部返回箭头。
两个管理族页面（`manage`、`bundles`）都吃 D26 的大尺寸；内页不再回到 1000×620 被挤扁。
判据：**新东西上线不能吞掉旧入口。** 换壳可以，悬空不行——一个页面没有入口，等于被删除。

## D28 · 2026-10-24 · 管理首页 = 可收起侧栏 + 可自排卡片
左栏是项目列表（PAD 面向项目），可收起到 64 宽只剩首字与状态点；右栏是卡片区，卡片刻意做成**可自排**：
```
卡片 = type × view
  type  running | inventory | context | project | note
  view  ring | count | bar | dots
存放  pad.json#ui.cards（空数组 = 默认三张：在运行 / 已收录 / 上下文）
```
编辑模式（「编辑」按钮切换）里每张卡右上角出 `×`，末尾出一张虚线 `+` 卡；「+ 卡片」按钮随时可加。
**卡片数量与顺序就是数组的顺序**，没有隐藏状态，删掉卡片就是删掉数组项。
刻意没做的：拖拽排序、单卡独立绑定面板。project 卡当前跟随侧栏选中的项目（`source` 为空时）。
判据：**可自排的前提是"编排存在磁盘上"**，而不是存在某个控件的 Children 里。

## D29 · 2026-10-24 · 管理首页的视觉语言：柔光卡片
参考的是 soft card / claymorphism 轻量那一派（白卡浮在浅灰画布上、大圆角、弥散阴影、克制用色），
只学**表达方式**，不学它的功能布局。落到具体数值：
```
画布  #EAEFF3（C.Ink.050）      卡片  #FAFCFD（C.Surface）     —— 靠明度差分开，不靠描边
圆角  22                        内边距  24 / 20
阴影  blur 28 · depth 7 · opacity 0.10（hover 抬到 0.16）
字阶  标题 22 Bold / 数字 34 Bold / 标签 11 灰      —— 三级就够了，再多会乱
侧栏  26×26 浅色 chip（首字）+ 名称 + 状态点；选中项用 AccentWash 圆角底
按钮  「+ 卡片」实心 pill，其余 ghost
```
一个必须记住的坑：`UserControl.Resources` 里定义的东西**不要用 `Application.Current.Resources[...]` 取**。
索引器对缺失的键返回 null 而不抛异常，于是样式静默失效——第一版就因此整页没有卡片面。
取局部资源用 `Resources[...]` 或 `FindResource`。

## D30 · 2026-10-24 · 削复杂度 ≠ 削信息密度
第一版柔光卡片被否："这展示的信息太少了。我是叫你削减复杂度，怎么给信息密度一起削减了。"
教训：**去掉的是视觉噪音（描边、杂色、多余文字），不是数据本身。**
现在每张卡至少承载三层，全部用形状表达：
```
在运行   环(比例) + 每个 home 一根柱(高度=会话，实心=在跑)
已收录   大数字 + 热力方块(深浅=会话量，实心色=在跑)
上下文   竖水位(占比) + 数值 + 横条(工具定义占比)
近 7 天  柱状 + 逐日标签
工具调用 每行两层柱：上=调用次数，下=该工具吃掉的 token
```
两条硬性做法：
1. **缺席也要画出来**：空闲 home 给最小高度的柱（+1 归一），否则版面看起来是空的。
2. **同一条柱可以承载两个维度**（次数 / token 量），这比多一张卡更省地方。
判据：**密度不够时，先问"这张卡还能编码哪个已有字段"，而不是加一句说明。**

## D31 · 2026-10-24 · 关闭态给形状，展开态给明细
卡片分两面：
```
关闭  一张形状摘要（环/柱/热力/双柱），一屏扫完，不读字
展开  点击后原地变高，把同一个数据源逐行列出（名称 · 条 · 数值）
```
规则：**展开不引入新数据源**，只是把聚合拆成行。这样"加交互"不会变成"加页面"。
实测（`--shot-page manage-open`）：已收录从 6 个方块展开成 6 行 ——
`dsh-desktop/home 4 / .dsh 1 / Brs 0 / 123/home 1 / pad-op-clone/home 1 / pad-op-tmp/home 1`。
两个已知代价：
1. `WrapPanel` 的行高由最高卡决定，一张展开会让同行出现空白。要填平需换成自适应面板（未做）。
2. 展开态不持久化，切页即收。
附带修的正名问题：PAD 每个实例的 home 末级目录都叫 `home`，展开后三行都显示 "home"。
改为末级是 home/dsh-home/harness 时取上一级（`123/home`）。

## D32 · 2026-10-24 · 项目自己的图：有 logo 用 logo，有界面图用界面图
`Core/Logos.cs` 按常见名去常见位置找图，**不做遍历**（每次重绘都会调用）：
```
标记  logo / icon / avatar / brand / mark        -> 侧栏 26×26 chip
界面  ui / screenshot / preview / cover / banner / hero  -> 项目卡封面
位置  项目根 · .pad · assets · public · static · docs · images
格式  png jpg jpeg webp ico（也接受 logo-light.png 这类前缀变体）
```
找不到才退回"首字 chip"。查过的结果按 workspace 路径缓存，避免反复读盘。
封面出现时，卡片下半部改为紧凑排版（数字 + 状态），因为封面已经占掉一半高度。
**顺带查出一个数据问题**：`pad-op-clone` 的 workspace 指向 `pad-op-tmp` 的 workspace
（`instances\pad-op-tmp\workspace`），所以两个实例共用目录、共用 logo。这不是本功能的 bug，
是那份实例数据本身如此 —— 克隆实例时 workspace 路径没有重写。要修的是克隆流程，不是这里。

## D33 · 2026-10-24 · 载体图标：插件自带优先，其次数据库
载体是插件，而且**可能有多个 TUI**，所以不能按"tui / 桌面端"写死一张图。顺序：
```
1 用户设的 logo            instance.json display.logo
2 项目自己的 logo          workspace 里 logo/icon/avatar/brand/mark
3 载体插件自己的图标       package.json 的 icon/logo 字段，或包内同名文件
4 载体数据库               Assets/carriers.json（随包）+ <root>/icons/carriers.json（用户）
                           条目 { match, icon }，match 是包名片段；图标按声明文件所在目录解析
5 DSH 鲸鱼                 桌面端安装目录 resources\icon.png
```
**字母永远不画。** 本机 11 个插件目前没有一个自带图标，所以现在等于全部落到鲸鱼；
数据库留了 `carriers.json` 和 `<root>/icons/*.png` 两个投放点，补图不需要改代码。
`Carriers.Bundles(home)` 直接读各 profile 清单里的 `dsh.profile.bundles`，不猜。

## D34 · 2026-10-24 · 指纹：先分清，再谈好看
启动器必须能判断"这两个是不是同一个东西"，这是底线，不是装饰。`Core/Fingerprint.cs`
把 home · workspace · version · 声明的 bundles 排序后做 SHA-256，取前 8 位十六进制。
侧栏每行显示这 8 位；**两种"相同"会被标红**：
```
同一个 home      -> 就是同一个 DSH，换了名字
同一个 workspace -> 不同实例、同一个工作目录（本机的 pad-op-clone / pad-op-tmp 正是这种）
```
实测：`123 a26e12d1`、`本机 DSH 29fc4b86` 为灰；`pad-op-clone 65613b1e`、`pad-op-tmp dba49290` 标红
—— 指纹不同（home 不同）但 workspace 相同，PAD 直接指出来。
判据：**"分不清"要在界面上被看见，而不是靠人去记得。**

### D34 补充：共用 workspace 不是错误，是要分组
一个项目上同时开 web 和 tui 是正常用法，所以不能禁止共用目录。侧栏改为**按 workspace 分组**：
```
[图标] 123            a26e12d1
       pad-op-tmp/workspace  2          <- 分组头（末级叫 workspace 时取上级）
  [P]  pad-op-clone   65613b1e
  [P]  pad-op-tmp     dba49290           <- 组内缩进，各自指纹照常显示
[图标] 本机 DSH       29fc4b86
```
同组两条指纹不同（`65613b1e` / `dba49290`）——这就是"不是同一个 DSH，只是共用一个目录"的证据。
行 tooltip 给完整 home 路径。
判据修正：**同一个 home 是缺陷（要报），同一个 workspace 是事实（要分组）。**

## D35 · 2026-10-27 · 页面按视角分侧栏，不强行合成一个
管理=项目视角（实例清单），关系=工作区视角（层级树），实例与组合包=整合包视角，三个页面各说各的话。
否掉"所有页面共用一个侧栏、只是换内容"的提法：内容形态差别太大，硬说"同一个"是偷换概念。
保留的是**行语言统一**（logo 芯片 + 名称 + 状态点 + 展开箭头）和 **FLIP 连续性**——
视角切换时同 key 元素（工作区、实例）从旧位置滑到新位置，容器在它们周围变化。
舞台动画的原则不变：一个舞台通过变化承载场景，不做整页切换。

## D36 · 2026-10-27 · 自动探测：身份是 DSH 内核，不是外壳
问题：本机同时装了官方桌面端、非官方 DSH Desktop、一个 TUI、一份纯 dsh，
`pad cli discover` 只报出 0 个桌面壳、1 个 home、Tui=未找到。嗅探不全不是因为位置没找够，
而是**模型里没有"内核"这一层**：两个桌面端各捆一份 `@deepseek-ai/dsh`，TUI 与纯 dsh 用的是另一份。
所以先定身份：
```
内核 Kernel   @deepseek-ai/dsh 包（version + 位置）。Identity 三选一：
              真实包目录的绝对路径
              <asar 路径>!<归档内目录>     （桌面端捆的）
              declared:@deepseek-ai/dsh@<ver>（只有启动脚本声明、没有实体包）
载体           桌面壳（Electron + asar 里有内核）/ TUI（home 的 profile 依赖）/ CLI（bin.js 或 shim）
home          profiles/ 或 storages/ 的目录；壳的 userData 只是找 home 的线索
```
判据：**同一版本、不同位置 = 同一个发行版的两份内核，不是同一个 DSH。**
本机实测：`E:\DSH-App` 内 0.2.1-rc.2、`E:\新建文件夹 (5)\DSH Desktop` 内 0.2.0-rc.2、
TUI 启动脚本声明 0.1.0-rc.7、PAD 版本库 0.1.0-rc.7 —— 四个载体、三个版本、四份内核。

探测按载体分线，每条都带「谁找到的」：
```
桌面壳  运行中进程 / 开始菜单 .lnk（COM 解析 + 跟 .cmd/.ps1 链）/ 卸载注册表
        （HKLM+HKCU 的 Uninstall*，以及 HKCU\Software\<guid> 的 NSIS InstallLocation）/
        已知 userData 与 pad.json#launch.dshCli 反推；Electron 签名 = resources\app.asar
        且 asar 里有内核或 dsh-desktop-host
TUI     home 的 profiles\*\package.json 依赖（不写死 @deepseek-harness-tui/dsh-tui）+ 
        启动脚本里的 DSH_HOME/--profile/@deepseek-ai/dsh@ver + ~/.dsh-tui 状态目录
home    已知位置 + 壳 home + 环境 DSH_HOME + 固定盘根一层 *dsh*/*harness* 命名目录 + --deep
```
两条实现硬约束（承 D21/D22）：**不许 shell out、不许无界遍历**；进程/快捷方式/注册表/目录全部有 cap，
扫描只在 worker 上跑，UI 线程只收结果（设置页进来先画，探完再填）。
另修掉两个真 bug：`Profiles()` 把 `.generations` 当 profile；asar 的 dataBase 不能假设 = JSON 长度 +16
（0.2.0 那份归档多 2 字节对齐，必须按 u32@4 计算）。
已知缺口：TUI 的「在跑」没有可靠判定（终端里的 node 进程不暴露 home），先只报装没装。
判据：**"是不是同一个 DSH"要在数据里能回答，不能靠安装目录名字猜。**

### D36 补充 · 2026-10-27 · 判不了的事去问进程（PEB），别只看文件
上面那句"已知缺口"是**只看了文件系统**才得出的错误结论。Windows 上进程自己就是证据：
`Core/ProcTable.cs` 直接读目标进程 PEB（`OpenProcess` + `NtQueryInformationProcess` +
`ReadProcessMemory`），拿命令行与环境变量。x64 布局：`PEB.ProcessParameters@0x20`，
`RTL_USER_PROCESS_PARAMETERS.CommandLine@0x70 / Environment@0x80`（UNICODE_STRING）；
WOW64 目标偏移不同，直接跳过；不可读进程 catch 后丢。`pad cli procs [--json] [--pid N]`
把现场打出来，既是调试口也是测试断言口。
由此补掉的三件事（本机实测）：
```
壳 userData   官方壳 renderer 命令行 --user-data-dir=C:\...\@deepseek-ai\dsh-desktop
壳 home       同 exe 子进程环境 DSH_HOME=E:\DSH-Home（比启动脚本/目录猜更硬）
TUI/CLI 在跑  环境 DSH_HOME 对上 home + profile 对上 --profile  → Running=true + pid
              CLI 用「参数 token 等于 bin」而不是命令行子串：PowerShell 命令文本里
              出现 bin 路径只是"提到"，不是"在跑"（实测会把 dsh 自己的 subprocess 误判）
```
证据：起一个真进程（env `DSH_HOME=E:\dsh-tui\dsh-home`、argv `--profile dsh-tui`、
token 带版本库 bin.js）→ discover 里 TUI 0.8.3 与对应 CLI 同时 `running pid=<该进程>`；
kill 后重跑同一个 discover，两者都回 false。测试 `pad-discover.test.ts` 用同构真子进程断言
（Running+Pid 与 `pad cli procs` 的 DshHome/ProfileArg 都对上）。
代价与边界（都实测过）：discover 冷跑从 ~2.2s 涨到 ~3s（快照一次约 1.2s，上限 900 进程/240 候选）；
偏移是 x64 专用（x86 构建会安全跳过而不是读错）；提权/其他用户进程读不到（OpenProcess 失败）；
32 位目标进程跳过；`SplitArgs` 是简化引号解析，不实现 `\"` 转义。
判据修正：**"在不在跑"是进程现场，不是文件事实；能问 OS 的不要靠猜。**

### D36 补充二 · 2026-10-27 · 探测结果要落在"用户正在做选择的地方"
版本选择页也需要探测，但需要的是**两个已有的选择控件**吃探测结果，不是再贴一块"本机 DSH 清单"：
```
「DSH 版本」下拉   PAD 版本库（可钉） + 本机探测到的内核（只读，标来源）
                  本机：0.1.0-rc.7（版本库）、0.2.0-rc.2（DSH Desktop 捆绑）、0.2.1-rc.2（DeepSeek Harness 捆绑）
                  选中探测项点「改钉」→ 明确说"不在版本库，不能改钉，要钉先装同版本"，不静默失败
「终端 (N)」列表   当前实例的 profile + 探测到、还没收编的 TUI（行显示 home，行尾「收编」）
                  实现：DetectedTuiRow : ProfileVm（合成 Instance/DshProfile，复用同一套行模板与 InstMark）
```
第一版我在左栏加过一块独立的「本机 DSH（自动探测）」清单，被否："为什么给我塞一坨狗屎进去"。
**教训：同一个数据源在不同页面有各自的既有控件（下拉框/列表），探测应该"补 entries"，不是"加面板"。**
判据：**在哪个页面，就用那个页面已有的选择控件承接探测结果；不要在每个页面重复一张总清单。**

## D37 · 2026-10-27 · 版本选择按 PCL 语义：上面插件包，下面 DSH 版本
对着 PCL 截图定下来的模型（PCL 上「可安装 Mod = 带 mods 的版本」、下「常规版本」）：
```
DSH 版本      纯内核 @deepseek-ai/dsh@x.y.z
插件包        组件包 + 对应内核 = pack.json 的 skills/mcp/plugin/rule/command/hook
              + dsh.version（缺了 PA012）+ dsh.profile（默认模板）
实例          启动方案（裸内核 或 插件包）+ 一份 DSH_HOME + 一个工作区
```
`instance.json` 新增 `source:{kind:"pack", packId, packTitle, packVersion, packPath}`；
**无 source 的旧实例 = 内核来源**（不迁移、不覆盖）。用户拍板：**一个实例只挂一个包**
（入口按单包，底层 allow/deny 保留但不再是主入口）。
实现分工：包发现/解析走 TS（`packagent dsh launcher packs --json`，见新文件
`modpack/pack-catalog.ts`：默认根 + 盘根命名启发 + `--roots`/`--deep`，≤200 文件/5s，
坏包进 `errors` 不抛；`.pack.zip/.pinst.zip` 只读 zip 中央目录取根 manifest，缺内核照报）；
C# 只持有事实并建实例（`Packs.Catalog` 全量捕获 stdout + `FirstJson`；
`Launcher.CreateFromPack` → `CreateInstance` → 写 Source → `Packs.Project`）。命令：
```
pad cli pack list [--json] [--out]
pad cli instance create --pack <id|path> [--name N] [--profile P]
```
三个流程决定：
1. **缺内核跳窗问**（用户拍板）：`CreateFromPack` 复用 `CreateInstance` 的 PA001，
   UI 捕获后 MessageBox Yes/No「现在下载安装再继续？」→ `InstallRelease(kernel)` → 重试；
   不静默下载、也不是只提示。
2. **一个实例一个包**：UI 只暴露「用这个包新建实例」；单包来源写进 `Source`。
3. **zip 可投影、pinst 走导入**：TS `packProject` 原先只吃解开的 `.pack.json`，
   这次让 `pack-ops.packProject` 对 `.pack.zip` 走 `readPackZip → projectPack(from: extractedRoot)`，
   所以 `.pack.json` 与 `.pack.zip` 都能「用这个包建实例」。C# 在
   `CreateFromPack` 建实例前只挡 `.pinst.zip`（PA012 `must be imported`，help
   `pad cli import "<path>"`，实例目录不建）；UI 对 pinst 行路由到 `Packs.Import`。
配套：导入/拖包前先把包复制进 `library/packs`（导入成功会消费原文件，库副本让目录持续可见）；
`registry-store.ts` 补 `pad-pages` 注册表键（此前 import/scan-drop 全链 PA017 的既有红）。
已知缺口：左侧实例列表还没按来源分组；pack list 的 `--query` 是目录过滤、没接 pack-index FTS
（FTS 仍是实例工作区内 `.agent-pack/modpacks` 的检索真值）；同名包的 `.pack.json` 与
`.pack.zip` 会各列一行（kind 不同，未去重）。
实测（scratch launcher + junction 到真实版本库）：`instance create --pack example.pack.zip` exit 0，
`instance.json` 写 `dsh.version=0.1.0-rc.7` / `lastProfile=web` / `source.kind=pack`，
workspace 生成 `.agent-pack/modpacks/pack-agent-modpack-example`；pinst 同命令 exit 1 且零写入
（`EVIDENCE/D37-packs/pack-zip-demo.instance.json`）。
判据：**"版本"是可启动方案，"插件包"是自带内核的版本，"实例"是选定了方案的那份数据。**

## D38 · 2026-10-27 · 实例图标：整合包图 → 载体图标 → 蓝鲸，绝不画字母
用户指着启动页磁贴上的白字 **P**："这里这个 p 不能用 p。有图片就用整合包内的图片，实在不行就用
载体 surface 的图标，再不济就用 deepseek 蓝色鲸鱼图标。"
根因不是字体画的字母，而是 `InstIcon.PackImage()` 的候选里混进了 **workspace 根的项目 logo**：
`instances/pad-op-tmp/workspace/logo.png` 本身就是一张字母 P 的图，被当成"整合包图片"优先显示。
所以"禁止画字母"的测试全绿也挡不住——**字母是图片内容**。
修法（优先级按用户原话）：
```
1 整合包图片      display.logo（用户显式） > 实例 pack/ 目录 > 来源包目录（Source.PackPath 父目录）
                 > 投影 workspace/.agent-pack/modpacks/* 里的 icon/logo/pack.png 或 pack.json 的 icon/logo
2 载体图标        profile bundles 的插件图标（TUI 的鲸鱼 logo 等）；横条 wordmark 用 Uniform 不裁
3 载体 surface    tui → 终端 glyph；web → 窗口 glyph
4 DeepSeek 蓝鲸   内置 Assets/api/deepseek.png（兜底）
（永不画名字首字母；PAD 自己的 pad-logo 不再兜底）
```
关键点：**"整合包图片"必须只来自整合包自己**。`workspace` 根、实例根的 logo 是项目品牌
（D33 用于侧栏 chip / 卡片封面），可能就是一个字母，不能冒充实例图标。
另加 `PAD_ICON_DEBUG=<file>` 诊断旋钮（记录每个 tile 走了哪条分支）——这次就是靠它抓到
`pack=...\workspace\logo.png` 的；不设环境变量就完全不写盘。
实测：同样两个实例改成显示 dsh-TUI 载体图标；没有 profile/载体的实例落蓝鲸。
`pad-window.test.ts` 加了回归断言（InstMark 必须引 deepseek.png、不得用 pad-logo；
InstIcon 不得再 Push workspace 根/实例根、必须看 Source.PackPath）。
已知缺口：`.pack.zip` **压缩包内部**的 icon 暂不读（只看外置/投影出来的图）。
判据：**"有图片"限定为"这个整合包自己的图片"；品牌图和字母图不许顶上。**

## D39 · 2026-10-27 · 版本选择页：整合包不叫插件包、容器要列全、安装不填空
用户对版本选择页的三条原话与落地：
1. **"插件不叫插件包"**。顶部把本机 `.pack.json/.pack.zip` 叫「插件包」是错的：它们是**整合包**。
   标题/提示/按钮/空态/弹窗统一改「整合包」；每行副标题说清包里有什么：
   `内核 x · 插件 2 · 技能 3 · MCP 1 · profile web · source`（`ComponentsText` 中文化）。
   底部实例白名单那个同名「整合包」展开区改名「**这个实例的整合包**」，两个概念不再同名。
2. **"这里底下不该叫这个要叫容器。列出所有容器让用户自己选。"**
   「终端 (N)」→「**容器**」，不再只列当前实例 profile：把本机探测到的**桌面端 / TUI / 命令行**全部列进去
   （本机实测 2 桌面端 + 1 TUI + 4 命令行 = 7），行首标类型，行尾带「收编」；点 profile = 打开启动页，
   点探测容器 = 收编/切换到对应实例。实现是 `DetectedShellRow` / `DetectedTuiRow` / `DetectedCliRow`
   三个 `ProfileVm` 子类，复用同一套行模板与图标，不为「探测结果」再造一套控件。
3. **"'安装新版本' 我不懂咋用……不要做填空题"**。
   原来的「文本框填 profile 名 + 下拉 + 创建」改成「**新增启动版本**」点选：
   `终端 TUI / 网页 / 空白` 三张 RadioCard（写清装什么、怎么跑），名字自动生成（占用自动加序号，
   本机自动出 `dsh-tui-2`），下面实时显示将创建的名字 + 组合包列表 + 内核；只在「自定义名字（可选）」
   展开后才出现输入框。
判据：**术语要能对上 DSH 的真值（整合包 / 组合包 / 插件 / 容器），同一概念不许在页内两种叫法；
能点选的不要让人填空。**
证据：`EVIDENCE/D39-versions-ui/`（三张实拍 + README）。

## D40 · 2026-10-28 · 容器行：用壳自己的图标，并把命令行入口说清楚
用户看「容器」列表：*"这几个都有啥区别。哦对了，官方的桌面端用官方的小黑鲸，非官方用他们自己的图。"*
1. **图标取壳自己的**：`ProfileVm.IconPath`（虚拟属性），`DetectedShellRow` 取
   `<exe 目录>\resources\icon.png`（兜底 `icon.ico` / `tray.ico`）；`InstMark.Paint()` 最前面处理
   「行自带图标」，不再落兜底蓝鲸。本机结果：官方 `E:\DSH-App\resources\icon.png` = 官方小黑鲸；
   第三方 `E:\新建文件夹 (5)\DSH Desktop\resources\icon.png` = 它自己的 dashboard logo。
   命令行入口按「bin 在哪个壳的安装目录下」继承那个壳的图标；PAD 版本库那条没有所属壳，兜底蓝鲸。
2. **类型细分**：桌面端行首标签改 `官方桌面端` / `第三方桌面端`
   （`FileVersionInfo.ProductName` 含 "DeepSeek" = 官方；第三方按自家 ProductName）。
3. **命令行 4 条不再同名同图标**，标题+副标题说清是哪个入口：
   `DeepSeek Harness 自带的 dsh`（app.asar 内 bin.js）/ `DeepSeek Harness 装的 dsh 命令`
   （runtime\cli\bin\dsh.cmd）/ `PAD 版本库 dsh` / `设置指定的 dsh CLI`（pad.json#launch.dshCli，
   当前指向第三方壳的 asar，并写出来源壳名）；`在跑/未跑 + pid` 仍来自 PEB 现场。
判据：**同一种东西可以用不同图标，但"不同在哪"必须写在一行里；图标必须来自它自己，不许借。**
证据：`EVIDENCE/D39-versions-ui/ICONS.md` + `02-containers.png`。
## D41 · 2026-10-28 · bun 自愈 + 容器去收编 + 制作整合包用 Go packmaker
用户三连：1)"有bug"（整合包区 PA028：Bun was not found on PATH）；2)"这俩没存在的必要了"（删除
「这个实例的整合包」与「新增启动版本」）；3)"那就要支持制作整合包，一个整合包至少要有插件，最多就是给你哪个结构"。
修法：
1. bun 自愈：`Proc` 绝对路径解析 + `Bun` 解析器（~/.bun/bin、%APPDATA%\npm、本地 tools、BUN_INSTALL、PAD 旁），
   子进程 PATH 前置；缺 bun 自动 `npm i -g bun`；`Packs.Unavailable` 不再拿 bun 当门槛。
2. 删两块：版本选择页只留 整合包目录 / DSH 版本 / 容器；pad-window 反向断言。
3. 容器：命令行不算容器（删 DetectedCliRow）；去掉「收编」按钮；ItemsControl(Button) → ListBox(PlainList)：
   点击=选中，右侧只有设置齿轮（实例→版本设置；探测容器→只读设置卡）；修选中↔重建互递归栈溢出（_containerSync）。
4. 制作整合包：新增 Go 工具 `agent-pack-dsh/packmaker`（stdlib archive/zip）：`make --spec --out [--icon]`；
   name/version/dsh.version 必填、`dsh.plugins[]` 至少一个（exit 2）；skills/rules/mcp/commands/hooks/subagents/settings
   勾了才写；图标进 zip 根。PAD「制作」窗口 → `Core/PackMaker.cs` 写临时 spec 调 `packmaker.exe`；
   csproj 把 packmaker.exe 拷到输出目录；`ReloadPackCatalog` 只留带 dsh.version/pinst 的真 DSH 整合包。
实测：go test 绿；`packmaker make` 产出 `tui-全家桶-0.1.0.pack.zip`（pack.json+dsh.plugins 2 个+icon）；
`pad cli discover --json`（headless，不开窗）证明探测正常：4 内核 / 2 桌面端 / TUI / CLI 全在；
pad-window、pad-pack、pad-depth 绿。证据：`EVIDENCE/D41-packmaker/`。
判据：**没有插件的不叫整合包；已经探测到的容器不需要再"收编"；PAD 不赖用户的 PATH。**
## D42 · 2026-10-28 · 降文字密度：没用的删，有信息用动画/交互，非要写就藏
用户指着容器行「2 · 在跑 · pid 28032 · DSH-Home」和容器抬头统计："这个就没用"；
"降低文字密度……尽可能用动画和前端表示……真有要写也该能用层级结构或隐藏就用"。
规则：1) 对决策没影响的字直接删；2) 有影响的先用视觉状态（在跑=绿灯，不写字）；3) 真必须写先删技术说明，再放 tooltip/设置。
改动：
- 容器抬头统计整句删除；行内只留 类型标签/图标/名字/版本号；「在跑」→ 绿色呼吸灯（blink 0.9s + 缩放呼吸 1.6s + 上下 0.8px 抖动 0.7s，AutoReverse Forever）；
  pid/home/userData/内核进 tooltip 和设置卡；类型标签「官方桌面端/第三方桌面端」→「官方/第三方」。
- 整合包行删路径行与「profile 默认/drive:/来源未记录」，副标题只剩 内核+组件；按钮「用这个整合包新建实例」→「新建实例」；
  抬头统计删除，空态只剩一句；「来源里的本机…」删除；下拉来源缩成 版本库/启动脚本/桌面端自带；制作窗口说明同步砍短。
验证：build 0/0；pad-window/pad-pack/go test 绿；PAD 只构建一次、重启一次（pid 25200），不再反复开关窗口。
判据：**列表里每个字都得影响决策；状态用眼睛看得到，说明用 tooltip/设置藏；技术文档语言不许进 UI。**
## D43 · 2026-10-28 · 侧栏/关系图实例图标：项目 logo 不是图标，空白也不许留
用户指项目侧栏："这种地方也是，图像呢？"——pad-op-* 显示字母 P（workspace 的项目 logo），本机 DSH 空白。
根因：ProjectRail.Chip 的候选是「自己设的 → 项目 workspace 的 logo.png → 水洗色底」；RelationsView /
RelationsWebView / WorkbenchView 也把 Logos.Mark(workspace) 当图标。项目 logo 可能是一张字母图（D38 同病），
没图的实例就只剩空白。
修法：统一 InstIcon.MarkFile(inst) = 整合包图 → 载体图标（BundleIcon.First）；项目 workspace logo 不再进候选。
侧栏实例行/树节点直接换 InstMark 控件（整合包图 → 载体 → DeepSeek 蓝鲸，绝不空白/字母）；关系图、关系网页、
工作台全部改用 MarkFile。同时删掉"未在跑"的灰点（没信息不画），在跑统一 RunLamp 呼吸绿灯（侧栏行、树节点、
容器列表共用）。验证：pad-window 新增反向断言（四视图不得再出现 Logos.Mark、ProjectRail 必须 InstMark+RunLamp、
RunLamp 必须有 Forever 动画）；pad-window/pad-pack 绿；build 0/0；PAD 单次重启（pid 53796）。
判据：**列表/图谱上的每一个图标都必须回答"这是哪个实例"，所以只能来自该实例的包/载体；字母图与空白都不算图。**
## D44 · 2026-10-28 · 学 PCL 的创建整合包页：自己选核心/容器/插件
用户："学 PCL 做个创建整合包的功能，可以快速把已有的插件配置和容器和核心打包。当然要有一个页面给自己选，自己组。"
落地：新增内页 `makepack`（Views/MakePackView.xaml/.cs），版本选择整合包区「制作」进入：
- 身份：名字/版本/说明/图标（点预览块选图，Fx.Reveal 淡入）；
- 核心：单选，PAD 版本库 + 本机探测内核（mono 版本 + 来源小字）；
- 容器：单选，"不指定" + 桌面端/TUI（InstMark 图标 + RunLamp 呼吸绿灯，tooltip 详情）；
- 插件：多选现有实例每个 --profile 的 bundles + 探测容器 home 的 profiles，可手加 spec；
- 快速预填：实例钉的版本=默认核心、当前 profile bundles=默认勾、实例 home 命中的容器=默认容器、换容器自动带上其插件配置；
- 底部只一句条件提示（缺核心/缺插件）+ 制作；成功后 toast 文件名并回版本选择（自动刷新整合包列表）。
设计规则（D42）：不摆说明文，只有分节标题；来源走小字/tooltip；图像用 InstMark；状态用 RunLamp；选中走 RowItem 动效；一个主动作。
导出仍走 Go packmaker（至少一个插件；dsh.version 必填）。pad-window 新增断言：页面必须有 核心/容器/插件 三块 + Discover.Run + Carriers.Bundles + MainWindow 工厂；pad-window/pad-pack 绿；build 0/0；PAD 单次重启（pid 33824）。
判据：**"制作整合包"是一个可挑选的页面，不是填空题；能自动带出来的已有配置就带出来，人只做选择。**
## D45 · 2026-10-28 · 新建整合包 = 导出：落到用户选的位置，不塞回库
用户："这个新建整合包是用来导出的"。
改动：入口文案「制作」→「导出」，内页标题「导出整合包」，页面按钮「导出」。
点导出先弹 SaveFileDialog（默认 `<名字>-<版本>.pack.zip`，起始目录 Downloads），
然后 packmaker 生成到临时目录，再 File.Copy 到用户选的位置，最后删临时目录并 toast 文件名；
不再写进 `<launcher root>/library/packs`（那是导入库，不是导出口）。
`PackMaker` 拆成 `WriteTo(outDir, req)`（导出/临时目录）与 `Write(root, req)`（导入库，保留给以后导入用）。
验证：pad-window 新增断言（必须 SaveFileDialog + File.Copy + WriteTo + 「导出」入口）；pad-window/pad-pack 绿；
build 0/0；PAD 单次重启（pid 34040 附近新进程）。
判据：**导出是"拿走一份 zip"，不是"往自己库里放一份"；落点由用户决定。**
## D46 · 2026-10-28 · 已建好的包也能导出：右键导出，不加新按钮
用户："那我们已经建立好的包，这不没办法导出了吗？你又陷入设计误区了，不要想着加东西解决。"
上一版只给"新组包"做了导出（makepack 页 + 保存位置），已经存在的整合包（library/packs 里的
.pack.zip/.pack.json，即版本选择整合包列表里的行）没有导出口——设计死角。
修法不新增可见界面：整合包列表行**右键 → 导出…**（ContextMenu 默认隐藏，PCL 同款）→ SaveFileDialog →
File.Copy 原文件到用户位置 → toast。左键行为不变；要"从已有实例配置组新包导出"仍走整合包区「导出」内页（预填）。
验证：pad-window 断言行必须有 ContextMenu+「导出…」、ExportPack_Click、File.Copy；pad-window exit 0、pad-pack 绿；
build 0/0；PAD 单次重启（pid 68796）。
判据：**导出是对"包"这个对象的能力，不是对"新建流程"的能力；已存在的对象也必须能带走。**
## D47 · 2026-10-28 · 导出两种形式：清单包 / 便携包；没上架插件塞文件夹
用户："要是有的插件他无法 npm 下载没上商店呢？我说导出的时候应该支持选导出成哪种，一种是表（但是没上架的插件搞个文件夹塞进去）。另一种是解压即玩，实例什么的都要进去，要让他们打开压缩包里的 PAD 就能玩。当然你别取名成解压即玩。"
落地：导出页底部「导出形式」二选一：
1) 清单包 .pack.zip：packmaker 生成 pack.json（dsh.version + dsh.plugins[]，至少一个）；没上架 npm 的插件用「本地插件…」选目录，
   整个目录塞进 plugins/<名字>/，清单条目带 "local":"plugins/<名字>" 指路（实测包内出现 plugins/my-local-plugin@1.0.0/...）。
2) 便携包 .zip（不叫解压即玩）：复用 Launcher.ExportPortable(dest, instanceId)（PAD 单文件 exe + .pack-launcher：版本库/实例/
   runtime node/registries，凭据日志已剥离），再 ZipFile.CreateFromDirectory 成 zip；解压后根目录有 PAD exe，打开就能跑。前提先选中实例。
不是新页面：挂在已有导出页底部；本地插件多一个按钮。验证：pad-window 断言两种形式/禁"解压即玩"/AddLocal_Click/LocalPlugins/
ExportPortable/ZipFile.CreateFromDirectory + packmaker main.go 支持 LocalPlugins；pad-window exit 0、pad-pack exit 0；build 0/0；PAD 单次重启。
判据：**导出不是只有一种形态——分享依赖时用清单包，分享"开箱能跑"时用便携包；不能联网装的插件必须随包带走。**
## D48 · 2026-10-28 · 实测右键导出：能用，卡在"目标文件被占用"；顺手修重试与重复行
用户："打开看看。右键的适配页做了，哪能用啊，测试"
实测（真实 PAD 窗口，UIA 驱动）：
1. 整合包行右键 → 菜单出现「导出…」 ✓
2. 调用菜单 → 弹出「导出整合包」SaveFileDialog，默认文件名 `Test-0.1.0.pack.zip`、默认目录（上次用过的桌面/下载）✓
3. 选择覆盖桌面上已存在的 `Test-0.1.0.pack.zip` → 弹出覆盖确认 → 确认后报
   "The process cannot access the file ... because it is being used by another process"（目标被资源管理器/压缩软件占着）。
结论：右键导出链路本身通；失败是外部占用 + 错误信息不友好。
修法（不新增 UI）：
- `File.Copy` 加 5 次×200ms 重试（杀软/资源管理器短暂占用能过）；
- 仍失败时给中文可执行提示："目标文件被占用：xxx。换个位置，或先关掉打开它的程序再试"；
- catalog 去重：同名+同版本只留一行（优先 library 那份），解决桌面副本+库副本并排出现两行 "Test" 的问题。
验证：实测页面现在只显示一行 Test；用例 pad-window/pad-pack 绿；build 0/0。
判据：**验收要走真实链路；外部占用不是功能坏，但必须给可执行的人话，而且列表不许同一个包两行。**
## D49 · 2026-10-28 · 导出页打开就崩（UpdateHint 早于控件创建）+ 右键便携包遇到没装的内核
用户截图 1："PAD 出错了 Object reference not set..."；截图 2："that release is not installed"（右键 导出便携包）。
崩溃根因（pad-crash.log）：
`MakePackView.InitializeComponent()` 里 `FormatList IsChecked="True"` 触发 `Checked=Format_Changed`
→ `UpdateHint()` → `Hint` 还没被 XAML 创建 → NullReference，导出页一打开就崩。
修法：`UpdateHint` 开头 `if (Hint is null) return;`，FormatPortable 访问也加空判断。（实测导出页已能打开。）
右键只有清单包 → 右键菜单改成两项：`导出清单包…`（拷原文件）/`导出便携包…`（带 PAD+实例，压 zip）。
便携包报 PA001 的原因：包钉的内核（Test 是 0.2.1-rc.2）不在 PAD 版本库；而便携包要先建临时实例。
修法：导出便携包前若 `releases` 里没有该内核，弹窗问"现在下载安装，装完继续导出？"，装完再建临时实例、
ExportPortable、压 zip、删临时实例；PA001 失败则给中文提示（不再甩英文 "that release is not installed"）。
验证：pad-window（新增：便携包必须先装缺的内核、UpdateHint 空判断、右键两种形式）、pad-pack 绿；build 0/0；PAD 单次重启（pid 70180）。
判据：**打开就崩的页面等于没有；导出缺依赖时要么自己补（问用户后安装），要么说人话指路。**
## D50 · 2026-10-28 · 本机没有 pnpm：PAD 自己解析/安装，不再抛 "`pnpm` was not found on PATH"
用户："他说我本机没有，6666.我本机真没有的话哪里来的包"
实测本机：`where pnpm` 为空，npm 全局也没有 pnpm；但 `%LOCALAPPDATA%\pnpm` 里留着 `.tools` 和 `store`
（以前装过、PATH 里没有了）。corepack 在（0.34.5）。所以"包"能存在（packmaker/投影不需要 pnpm），
但 PAD 的发行号安装/装插件（dsh plugin add 内部也走 pnpm）确实需要 pnpm。
修法（和 bun 同一套思路，不让用户自己配环境）：
- 新增 `Core/Pnpm.cs`：先在 PATH 找，再找 `PNPM_HOME`、`%LOCALAPPDATA%\pnpm`、`%APPDATA%\npm`、
  `%ProgramFiles%\nodejs`（corepack shim）、`~/.local/share/pnpm`；真的没有就 `npm i -g pnpm`（中文错误兜底）。
- `Proc.PrependToolPath`：子进程 PATH 前置 bun 和 pnpm 的目录（嵌套 `dsh plugin add` 也能找到 pnpm）。
- `Launcher.InstallRelease` / `AddBundles`：在起 pnpm / node 之前 `await Pnpm.EnsureAsync(...)`。
- 下载页的 pnpm 状态改用 `Pnpm.Locate()`，不再用 `Proc.Which("pnpm")` 误报"不在 PATH"。
验证：headless `pad cli release install 0.2.1-rc.2` 走的就是这条链（自动装 pnpm → pnpm add → 装好内核），
之后右键便携包不再卡在 pnpm；pad-window 新增断言（Pnpm.cs/EnsureAsync/Locate、Launcher 先 Ensure、Proc 前置、
下载页用 Pnpm.Locate）；build 0/0。
判据：**能不能跑不该取决于用户的 PATH——PAD 要自己把工具链补齐；缺工具时说人话，别甩英文 PA011。**
## D51 · 2026-10-28 · pnpm >=10 的 ignored-builds 把安装判失败；安装要有进度和速度
用户："我让他安装了，现在有个问题，就是安装的时候看不见速度和效率，最后如图（pnpm add failed for this release）。
别他妈跳你那个powershell了"
根因（jobs/install-version-*.log）：pnpm 11.7.0 也把依赖 build scripts 默认拦掉，
最后一行 `[ERR_PNPM_IGNORED_BUILDS] Ignored build scripts: @google/genai, koffi, node-pty, protobufjs`
→ exit 非 0 → PAD 抛 PA010 "pnpm add failed for this release"。包其实已经下好了。
修法：
1. `Launcher.InstallRelease`：`pnpm add ... --config.strictDepBuilds=false --config.dangerouslyAllowAllBuilds=true`，
   version 目录 `.npmrc` 写入 `strict-dep-builds=false` / `dangerously-allow-all-builds=true`；
   profile `.npmrc` 同样补齐（嵌套 dsh plugin 的 pnpm 也放行）。实测手动同参数 `pnpm add` → `Done in 6.9s` exit 0。
2. PA010 不再只写小写的原因：把 pnpm 输出里的 `ERR_...` 行带进错误文案，用户能看出是哪条规则拦的。
3. 进度/速度：MainWindow 新增可原地更新的进度卡 `Progress(text)/ProgressDone()`；
   装内核时把 pnpm 输出翻译成 `安装内核 X · 下载 24.6/71.4 MB · 3.2 MB/s`（按两次 MB 采样算速度）、
   resolved/reused/added、以及"建临时实例/打便携包/压缩"阶段；失败前也能看到卡在哪一步。
4. 不再由我起 powershell 跑 pnpm：PAD 内的 Proc 用 CreateNoWindow，所有安装都在窗口内完成并显示进度。
验证：pad-window/pad-pack 绿；build 0/0；PAD 单次重启（pid 62272）。
判据：**安装不是黑盒——要能看到阶段、进度、速度；失败要能看见是哪条规则拦的，而不是一句小写英文。**
