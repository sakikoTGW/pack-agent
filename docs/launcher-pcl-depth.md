# PAD 功能深化：学 PCL 的机制，不学 Minecraft 的词

date: 2026-08-24
status: 深化进行中：工作区隔离对照 PCL Indie；发行号 Node 对照 Java 自动选择；跟随全局显示实际全局值。D6 多根已落地：本机 `roots.json` + 空根切到有实例的启动器根。
audience: 实现 PAD 窗与 `pad.json` 的人
对照源码：PCL 2.13.1.1 `E:\PCL\src`（`Settings.vb`、`PageSetup*`、`PageInstance*`、`PageSelectLeft`、`PageLaunchLeft`、`ModModpack.vb`）
产品定义：[PRODUCT.md](PRODUCT.md)。装包链：[modpack.md](modpack.md)。已有启动器功能：[launcher-design.md](launcher-design.md)。

前端可以一般。缺的是：**窗口把 CLI 已有能力露出来**，以及 **设置分成全局 / 实例两层，像 PCL Setup.ini + 版本 PCL\Setup.ini**。

定义只用 DeepSeek Harness 原词：`profile`、组合包、`dsh.profile.bundles`、`$DSH_HOME`、agent-preset、session。启动器自己的词：实例、版本库、投影、白名单。不准自造「面 / 件 / shell.kind」。

空态只留按钮，不写讲座。报错弹窗，不 toast 一闪。界面卡住（布局死循环、不抛异常）走 user32 弹窗写 `pad-crash.log`，不能只冻住。

---

## 0.1 横向对照 PCL：还停在浅水区的，这一刀动了什么

对照 `E:\PCL\src` 的启动/版本/设置体制。不抄皮肤、正版、联机、音乐、Java 内存条。

| PCL 体制 | 源码 | PAD 原先 | 这一刀 |
|---|---|---|---|
| 版本隔离 Indie | 全局 `LaunchArgumentIndieV2` 只管新建；每版本 `VersionArgumentIndieV2`；改隔离警告手动迁移；`PathIndie` vs 共享 `.minecraft` | `$DSH_HOME` 永远隔离；工作区只在克隆时共用，实例设置不能切 | 全局 `launch.workspaceIndieDefault`；实例「工作区隔离」；`pad cli instance workspace isolate\|share`；不拷文件；home 仍禁止共用 |
| Java 对应该版本 | 自动选择 / 指定路径；版本独立可覆盖 | `nodePath` 空则 PATH 或「磁盘上最新一份」runtime node | 该发行号 `engines.node` → `runtime/node/<ver>/` 写进 `launch-*.cmd`；不跟旁边较新的错 Node |
| Hint「跟随全局设置」 | `PageInstanceSetup` 每个框 `HintText="跟随全局设置"` | 空=跟随，框旁不显示全局当前值 | 空时写「跟随全局：」+ 实际全局值 |
| 启动前校验再启动 | 校验 Java、隔离目录、再启动 | `EnsureRuntimeNode` 不按该实例钉的发行号 | `EnsureRuntimeNode(dshVersion)` 先对该发行号 |
| 概览 | Overall：说明、logo、打开版本文件夹 | 改名和五个打开目录 | note、logo、揭示已有 `launch-*.cmd` |

明确仍浅、下一刀再动：改隔离时自动迁文件（PCL 也不做）；profile 之间拆 `$DSH_HOME`（违反 DSH）。

---

## 0.2 使用感断层（2026-08-30）

对照 PCL「打开 → 下载游戏 → 任务管理看进度 → 启动游戏」。PAD 原先把 CLI 摊成卡片，但下一步经常看不见。

已修：

| 断层 | 对照 PCL | 现况 |
|---|---|---|
| 打开下载不拉发行号列表 | 打开下载就获取版本列表 | `AutoFetchOnOpen` 默认开；发行号页也跑 `FetchVersions` |
| 发行号只进 ComboBox | 远程版本列表可点装 | packument 全部 `versions`，丢掉 `0.0.1-rc.1`；空卡看远程+本地 |
| 「添加已有文件夹」不选夹 | 添加已有文件夹会选目录 | `AdoptFolder` 弹 `OpenFolderDialog` |
| 点安装停在下载页转圈 | 下载任务看进度 | 左栏「下载任务」看正在下载的 / 下载完成的；点安装不切页 |
| 新建实例停在花名册 | 导入然后配置 | 新建同构进版本设置；装完 profile 回启动 |
| 点启动后管理/在跑的立刻空 | 启动后列表里还在 | `wt` 退出后宽限 45 秒，并认 node `--profile` |
| 空页没有按钮 | 无可用版本 → 下载游戏 | 版本选择空卡「下载 DSH」；任务「去下载」；管理「去启动」 |
| 正式 add 打到 LastProfile | 当前选中的版本 | 组合包正式操作有 `--profile` 下拉 |
| 实例凭据选不到具名钥匙 | 版本独立设置 | `InstCredBox` 列出 `ListCredentials` |

仍浅、下一刀：启动中左栏步骤/取消（PAD 过程在外部终端）；社区资源图标仍是字母块；检查升级只显示不装 PAD；`rewrite-scripts` / 任意 gateway RPC / `script` 只写不启 窗上无按钮。

2026-08-30 实际使用路径：点启动后 `wt -w 0 nt` 立刻退出，`runtime.json` 被清空，启动页「在跑的」和管理页一起空。现况：45 秒宽限；用 node 命令行认 `--profile`；任务页选进行中的 job，不盯着列表第一条失败。

---

## 0. 怎么读 PCL

PCL 值得抄的是六组机制，不是皮肤、正版登录、联机、背景音乐、自定义主页 XAML。

| PCL 机制 | 落到 PAD |
|---|---|
| 一个大按钮启动当前选中的版本 | 启动当前选中的实例 + `--profile` |
| 版本选择左栏是文件夹，右栏是该文件夹里的版本 | 左栏实例列表（名+路径），右栏该实例的 profile 按终端/网页分组 |
| 文件夹底下三个动作：新建 / 添加已有 / 导入整合包 | 新建实例 / 收编已有 `$DSH_HOME` / 装 `.pack.zip` |
| exe 旁 zip 启动先装再删 | 已有：`CollectSidecar` + `scan-drop` |
| 整合包目录里带着启动器 | 已有：`portable-export` 打 exe + `.pack-launcher`（版本库+实例在里面；运行时 dll 打进单文件 exe）。发给别人用 `.7z` / `7z.sfx`，ZIP64 zip 会被 Explorer 报无效 |
| 设置分全局与版本独立，版本页可「跟随全局」 | `pad.json` 全局；`instance.json` 可覆盖启动相关键 |
| 实例页四块：概览 / 设置 / Mod 管理 / 导出 | 概览 / 实例设置 / 插件 / session / agent-preset |
| 下载中心：游戏版本 + 社区资源分类 | 发行号 + npm 货架 |
| 独立任务页看进度、可取消 | 下载左栏「下载任务」读 `job` 队列，只列下载 |
| 每层设置可单独初始化 | 已有：设置左栏「初始化本页」 |
| 导入/导出整份设置 | 已有：设置·其他 |
| 右键打开一串目录 | 已有：home / 工作区 / 日志 / profiles / 投影 |

Minecraft 专用、PAD 不做：Java / 内存条 / GC / 皮肤 / 正版与外置登录 / EasyTier 联机 / 资源包光影 / 背景音乐 / 自定义主页脚本 / 土豆码主题解锁。

Node 对照 Java：每个发行号声明的 Node 版本，缺了装到 `<root>/runtime/node/<ver>/`。这是启动器职责，写进设置「运行时」。

---

## 1. 现在窗口里有什么

以 `pad/` 与 `pad cli` 2026-08-24 为准。

### 1.1 窗口已接上

| 页 | 已有 |
|---|---|
| 启动 | 大按钮、在跑列表、停、日志、管理口、新建、收编 |
| 管理 | 组合包名单（含 TUI）更新/添加/移除，不依赖管理口；有管理口时 session 树 |
| 版本选择 | 实例列表、新建、加 profile、右键 home/日志/快捷方式/删除 |
| 版本设置 | 概览、插件页（对照 PCL Mod 管理）、session、agent-preset、崩溃分析 |
| 下载 | packument 发行号列表 / 安装 / 删除；四货架搜 npm 并 `plugin add` |
| 设置 | 启动 / 个性化 / 下载 / 其他；每层可初始化 |
| 整窗 | 拖 `.pack.zip`、exe 旁 sidecar 扫描 |

### 1.2 CLI 有、窗口没有

这些命令已经在 `packagent dsh launcher` 或 `pad cli` 里，窗口必须有同一条链的按钮，禁止做第二套逻辑。

| 命令 | 窗口落点 |
|---|---|
| `instance clone` | 版本选择右键 |
| `instance rename` | 概览「改名」 |
| `instance pin` | 概览改发行号 |
| `instance display`（info / star / category / logo） | 概览展示字段 |
| `export` → `*.pinst.zip` | 实例页「导出」 |
| `portable-init` / `portable-export` | 设置·其他「打一份带启动器的整合包目录」 |
| `job list` / `cancel` | 下载页左栏「下载任务」 |
| `crash` | 已有崩溃分析，任务失败也要能点 |
| `doctor` | 设置·其他 |
| `credentials list/get/set` | 设置·启动「凭据」卡 + 实例覆盖 |
| `agent-preset list/copy/remove` | 实例页新分段 |
| `plugin list/add/remove/update` | 顶栏管理页；版本设置正式操作仍可用 |
| `session delete` / `backup` | session 分段 |
| `--dump-config` 落到实例目录 | 概览按钮 |
| `shortcut` | 已有，概览也要有 |

### 1.3 设置已有对照

`pad.json` schema v3，四层。

| 层 | 已有键 | PCL 近亲 |
|---|---|---|
| `launch` | 终端、标题、启动后 PAD、WT 复用、默认发行号、extraEnv、pause、遥测、`dshHome`、`tuiPackage` | `LaunchArgumentTitle`、`LaunchArgumentVisible`、自定义信息的弱对应 |
| `ui` | 主题、强调色、动效、toast、字标、logo、壁纸与透明度、窗宽高边距、顶栏高 | `UiLauncherTheme`、壁纸、logo |
| `download` | registry、超时、货架、快捷按钮 | `ToolDownloadSource` |
| `other` | 确认停止、出错开日志、刷新秒、registries 路径、损坏备份、截图延时、收编已拒绝 | 系统页的一小角 |

缺的不是「再写一段说明」，是键和按钮。

---

## 2. 设置深化：两层，跟随全局

PCL：`Setup.ini` 全局，`versions\<名>\PCL\Setup.ini` 版本独立。版本页每个框 Hint「跟随全局设置」，空 = 用全局。

PAD：

- 全局：`<launcher-root>/pad.json`，schema 升到 **v4**。坏 JSON 仍备份再回默认。每层仍可单独初始化。
- 实例覆盖：`instances/<id>/instance.json` 增加 `launch` 对象。字段与全局 `launch` 同名。`null` 或缺省 = 跟随全局。禁止在实例里再写一份完整 `pad.json`。

实例可覆盖的键：

| 键 | 含义 |
|---|---|
| `title` | 终端标题。空则用全局 `{instance} · {profile}` |
| `afterLaunch` | 启动后 PAD：keep / minimize / hide |
| `extraEnv` | 追加在全局 extraEnv 后面，同名覆盖 |
| `pauseOnError` | 该实例的 launch 脚本是否 pause |
| `telemetryDisabled` | 该实例是否写 `DSH_TELEMETRY_DISABLED` |
| `nodePath` | 空则用全局 / 该发行号 `runtime/node/<engines.node>` |
| `preCommand` | 启动前执行的一行。空则无 |
| `preCommandWait` | 是否等 preCommand 结束再跑 dsh |
| `credentialsSet` | 空 = 跟随全局默认钥匙；`instance` = 只用这份 home 的 `.credentials.yaml`；其它字符串 = `library/credentials/<name>.yaml` |

窗口：实例页加「设置」分段，每个框旁边写「全局」或当前覆盖值。按钮「本实例恢复跟随全局」只清 `instance.json` 的 `launch`，不动 `pad.json`。

---

## 3. `pad.json` v4 要加的全局键

不加讲座文案。设置页用短标签。

### 3.1 `launch`（对照 PageSetupLaunch 里能翻译过来的）

| 键 | 默认 | PCL | 行为 |
|---|---|---|---|
| `processPriority` | `normal` | `LaunchArgumentPriority` | 生成的 `launch-*.cmd` 里用 `start /belownormal` 等；wt 标签做不到时忽略并记 warning |
| `preCommand` | `""` | `LaunchAdvanceRun` | 启动脚本开头调用 |
| `preCommandWait` | `true` | `LaunchAdvanceRunWait` | |
| `nodePath` | `""` | `LaunchArgumentJavaSelect` | 空 = 该发行号 `runtime/node/<engines.node>`，再 PATH |
| `runtimeInstall` | `true` | 自动装 Java | 发行号 `engines.node` 不满足时装到 `runtime/node/` |
| `workspaceIndieDefault` | `owned` | `LaunchArgumentIndieV2` | `owned` = 新建实例独立工作区；`shared` = 指到 `sharedWorkspace`。`$DSH_HOME` 仍每实例一份 |
| `sharedWorkspace` | `""` | 共享 `.minecraft` | 空 = `<root>/workspace-shared` |
| `defaultProfile` | `dsh-tui` | 无直接对应 | 新建实例第一次 `plugin add` 用的 `--profile` 名 |
| `credentialsDefault` | `global` | 登录方式弱对应 | 启动器凭据分发器：`global` = `library/credentials.yaml`；`none` = 不拷 |

已有键保留：终端、标题、启动后、WT 复用、默认发行号、extraEnv、pause、遥测、`dshHome`、`tuiPackage`。

### 3.2 `ui`

| 键 | 默认 | PCL | 行为 |
|---|---|---|---|
| `windowOpacity` | `100` | `UiLauncherTransparent` | 整窗 Opacity。40–100。滑条即时预览 |
| `wallpaperBlur` | `0` | `UiBackgroundBlur` | 壁纸 BitmapEffect 半径。0 关掉 |
| `brandText` | `PAD` | `UiLogoText` | 顶栏名称。空则显示 PAD |
| `animSpeed` | `100` | `SystemDebugAnim` / `AniSpeed` | 动效时长倍率百分数。越大越慢 |
| `hiddenTabs` | `[]` | `UiHiddenPage*` | 可藏：`manage` / `download`。启动和设置禁止全藏。F12 临时显示，对照 PCL |
| `hideVersionEntry` | `false` | `UiHiddenFunctionSelect` | 藏启动页「版本选择 / 版本设置」 |

壁纸文件夹随机一张：已有单路径。补 `wallpaperDir`，空 = 单文件；非空则每次启动抽一张。刷新按钮对照「刷新背景图片」。

不做：音乐、自定义主页 XAML、隐藏主题解锁。

### 3.3 `download`

| 键 | 默认 | PCL | 行为 |
|---|---|---|---|
| `pnpmNetworkConcurrency` | `0` | `ToolDownloadThread` | 0 = pnpm 自己的默认；否则写进该次 `pnpm add` 的环境 |
| `cacheDir` | `""` | `SystemSystemCache` | 空 = pnpm 全局 store；非空 = 该启动器自己的 store，已有 `pnpm-store.ts` 接线 |
| `autoFetchOnOpen` | `false` | 版本列表源 | 打开下载页是否自动查 npm |

registry 快捷按钮、货架已有。

### 3.4 `other`

| 键 | 默认 | PCL | 行为 |
|---|---|---|---|
| `debug` | `false` | `SystemDebugMode` | 诊断多打一行 stderr；设置页显示 schema 与根路径 |
| `updateChannel` | `release` | `SystemSystemUpdate` | PAD 自身：`release` / `off`。检查按钮已有 |
| `exportSettings` / 导入 | 按钮 | 导出/导入设置 | 写出 `pad-settings.v4.json`；导入前备份当前 `pad.json` |
| `identify` | 启动生成 | `Identify` | 本地随机 id，复制按钮。不上传 |

---

## 4. 各页要补的功能

### 4.1 启动页

保持大按钮。副标题继续是「实例 · profile」。配置和日志不在这一页。

点启动：跳进实例管理内页。启动过程有进度条和入场动画。缺 `DEEPSEEK_API_KEY` 切到设置 → API Key。

在跑时启动页只留「查看实例管理」。

### 4.2 版本选择（对照 PageSelect）

左栏对照 PageSelectLeft：

1. **实例列表**：名字 + `$DSH_HOME` 路径，选中左边竖条，齿轮打开右键菜单
2. **添加或导入**：新建实例（弹名）、添加已有文件夹、导入整合包

右栏对照 PageSelectRight：按终端 / 网页 / 自定义分组，卡片可折。点一行选中并回启动页；齿轮或右键进版本设置。空态「无可用版本」→ 下载 DSH。

右键菜单补齐：

- 打开 home / 工作区 / 日志 / `$DSH_HOME/profiles` / 投影目录
- 改名、克隆、收藏（`display.star`）、分类（`display.category`）
- 改钉发行号
- 写快捷方式
- 导出 `pinst.zip`
- 删除

列表行要能看见：名字、发行号、收藏星、分类。logo 有则显示，无则现有字母块。

空列表：只留上面三个按钮，不写说明段。

### 4.3 实例页（对照 PageInstanceLeft）

分段改成五块，左或顶栏均可，前端一般就行：

| 分段 | 对照 | 内容 |
|---|---|---|
| 概览 | Overall | 改名、描述 `note`、收藏、分类、选 logo；打开 home / 工作区 / 日志 / profiles / 投影；导出启动脚本（已有 `launch-*.cmd` 则揭示路径）；`--dump-config` 写到 `instances/<id>/dump-config.yml`；补全发行号（`release install` 当前钉的号）；删除实例 |
| 设置 | Setup | §2 的实例覆盖。初始化本实例启动覆盖 |
| 插件 | Mod 管理 | 另外装上的 `dsh plugin` 包；过滤全部/可更新/试验中；add / remove / update；试验 / 固化 / 丢掉。随附层 / TUI / 管理口在顶栏管理页。整合包在投影，不进本页 |
| 整合包 | mods 文件夹那一层的投影 | 已有 list / 装 / 启用 / 停用。补：打开投影目录、白名单套装 `set-save` / `set-load`、从文件安装 |
| 导出 | Export | `*.pinst.zip`；`pad cli portable-export <dir> <instance>` 把发行号和实例拷进解压目录 |
| session | 存档 | 已有列表。补：删一条、备份一条、打开所在目录 |
| agent-preset | 角色组成 | 随附只读列表；copy 到用户层；删用户层。禁止删随附（PA112） |

凭据不进导出包。导出时 PA104 warning，窗口弹一次。

### 4.4 下载页（对照 PageDownload）

左栏保持：发行号 + 社区货架。

发行号补：

- npm packument 列表：每个发行号一行，未装的「安装」，已装标记；ComboBox 仍可手填
- 校验按钮（`--version` + 记 `verified`）
- 打开该 `versions/<ver>/` 目录
- 删除仍检查 PA006
- 运行时：该发行号需要的 Node，缺则装到 `runtime/node/`
- 装完进版本选择，不把人留在任务页或下载页

货架补：

- 点进行详情：对照 PCL 插件页。返回标题栏、信息卡、版本芯片（全部 / latest / 主.次如 1.45）、可下版本名单点一行再下
- 搜索卡对照 PCL 下载 Mod：版本=该实例钉的发行号，「装到」=实例名单。禁止把 `--profile` 旗标或 `实例 · profile` 画在搜索卡上
- 左栏只放分类，不把正在下 / 已下好塞在分类下面
- 点下载立刻 Toast「开始装」；装完 Toast「装进」或「没装成」

### 4.5 管理页

这页存在的理由：更新这份实例的 TUI 和组合包。

- 组合包名单：`--profile` 下拉、行上更新/移除、添加、全部更新。list 读 `dsh.profile.bundles` + `node_modules` 版本；update 是 `dsh plugin --profile <name> update`。没启动、没有 pad-gateway 也能列出。
- TUI `@deepseek-harness-tui/dsh-tui` 当作可更新组合包，不要求先装管理口。
- session 树仅在跑着且管理口 Ready 时。对一条 session：取消 agent、打开该 session 文件。
- 管理口失败：session 树空着，组合包区仍可用。缺 `DEEPSEEK_API_KEY`：启动拦 PA116；管理口 `credentials.describe` 为 false 时管理页显示 PA116

### 4.6 下载任务（对照应用商店）

下载页左栏，宿主与官方下面一项「下载任务」。点进去右边两段：正在下载的、下载完成的。进行中带进度和取消，下完在文件夹中显示 / 从列表中删除。不摊 CLI 日志。数据 `jobs.json`，只列 `install-version` 与 `plugin-add|update|remove`，不列克隆实例。点下载页的安装不自动切过来。顶栏「任务」关着。

### 4.7 设置页分层微调

左栏保持四层，把新键塞进现有卡，不要新开「讲座页」。

- **启动**：现有 DSH 命令 / `$DSH_HOME` / 终端之上，加凭据、Node 路径、启动前命令、进程优先级、默认 profile
- **个性化**：加整窗不透明、壁纸模糊、壁纸目录、藏页。F12 说明写在「其他」一行标签即可
- **下载**：加 pnpm 并发、store 目录、打开下载页是否自动查
- **其他**：debug、导入/导出设置、识别码、doctor、打整合包目录、打开启动器根

---

## 5. 多份启动器根（对照「文件夹列表」）

PCL：若干 `.minecraft` 并列，启动器记住当前选中的那份。

PAD：

- 名册不写进 `pad.json`（那是某一根自己的偏好）。写 `%LOCALAPPDATA%\pack-agent-dsh\roots.json`：`last` + `roots[]` 绝对路径。换文件夹打开 PAD 读同一份。
- 设置 · 其他 · 多个启动器根：下拉、切换、添加已有 `.pack-launcher`、新建根。
- 启动选根：`PACK_LAUNCHER_ROOT` 已设则用它；exe 旁 `.pack-launcher` 已有实例则留着（便携包）；空根切到 `last`，再否则切到本机嗅到的、实例最多的那份。嗅探范围：名册、`AGENT_PACK_TMP\.pack-launcher`、`PACK_AGENT_REPO` 的 Debug/Release `.pack-launcher`。禁止枚举 tmp 下一层（几百个 `pad-build*` 会 PA040）。不扫整盘。
- 切根 = 换内存里的 `Launcher` 并 Reload，有实例时写入 `last`。
- 不把这条链做成静默收编 `~/.dsh`。收编仍是版本选择里人选文件夹。

---

## 6. 启动脚本要吃进去的东西

`instances/<id>/launch-<profile>.cmd` 是人能双击的真值。深化后脚本按顺序：

1. `set PAD_INST=%~dp0` 与 `PAD_ROOT`
2. 全局 extraEnv，再实例 extraEnv；跳过 `DEEPSEEK_API_KEY`
3. 自有实例 `DSH_HOME=%PAD_INST%home`，`DSH_TELEMETRY_DISABLED`
4. 运行时从 `%DSH_HOME%\.credentials.yaml` 读 `DEEPSEEK_API_KEY` 再 `set`（不把值写进文件）
5. `preCommand`（可 wait）
6. `node`：kit 内 `runtime/node/<ver>/` 写成 `%PAD_ROOT%…`，否则 PATH 上的 `node`
7. `%PAD_BIN% --profile <name> --patch …`
8. `pause` 按该实例/全局

改设置后下次启动重生脚本。正在跑的进程不热改。点启动的悬浮窗等到 Harness 的 node pid，而不是 PAD 自己写进 `runtime.json` 的条目。

---

## 7. 明确不做

与 [launcher-design.md](launcher-design.md) §15 相同，并加上：

- 窗口里做 DSH 聊天
- 投影目录 `dsh plugin add`
- 两实例共用 `$DSH_HOME`
- 皮肤、正版登录、联机、音乐、自定义主页
- Java / RAM / 显卡切换
- 把 npx 缓存路径当可分发发行号。钉本机命令必须拷进 `versions/`
- 复活已删的空态讲座句

---

## 8. 验收（深化完成后）

1. 全局设置改标题，新实例跟随；某实例覆盖标题后只影响该实例的 cmd。
2. 版本选择能克隆、改名、导出 pinst，CLI 与窗口结果同一目录。
3. 实例页能 copy agent-preset、能 `--dump-config` 落盘、能打开五个目录。
4. 设置能导出再导入，导入前有备份文件。
5. 下载任务能看到一次 `release install` 的进度并能取消。
6. `pad.json` schema 为 v4；旧 v3 打开时补默认键，不丢已有值。
7. 藏下载页后 F12 能看见；不能把启动和设置同时藏光。
8. 诊断仍是 `error[PAxxx]` 弹窗。
9. 窗口操作没有「只写了 UI、CLI 没有」的第二条链。

---

## 9. 落地顺序

前端保持现有骨架。按依赖排：

| 刀 | 内容 | 依赖 |
|---|---|---|
| D1 | 窗口露出 clone / rename / pin / 打开目录 / dump-config / 导出 pinst / 装整合包按钮 | 已有 CLI |
| D2 | `instance.json` launch 覆盖 + 实例设置分段 + 重生 cmd | D1 |
| D3 | pad.json v4 新键接到设置页：凭据、Node、preCommand、优先级、cache、debug、导入导出设置 | D2 |
| D4 | 任务页接 job | 已有 job |
| D5 | agent-preset 分段、白名单套装、组合包 add/remove/update | 已有 plugin CLI |
| D6 | 多启动器根 | D3；已落地：本机 `roots.json` + 空根共用有实例的根 |

D1 就能把「功能不全」收掉一大半。D2–D3 才是「设置不全」。D6 名册与空根共用已落地。
