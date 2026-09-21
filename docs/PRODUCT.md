# pack-agent · 产品设计

date: 2026-08-20
status: canonical
product: **pack-agent**
代码：仓库根 + `agent-pack-dsh/`
入口：`packagent`；当前实例面 `packagent dsh`

本文件是产品定义。功能细则见 [launcher-design.md](launcher-design.md)，装包链见 [modpack.md](modpack.md)，引擎见 [launcher-architecture.md](launcher-architecture.md)。

pack-agent 是类似 PCL 的三件事叠在一个产品里：启动器、整合包装卸器、管理器。当前只管理 DeepSeek Harness。DSH 相当于 Forge，「游戏版本」= DSH 发行号。`docs/PACK_SPEC.md` §0、`docs/NORTH_STAR.md` 里的跨壳句子是旧仓级故事，本产品不按那条做多 harness 启动器。

## 1. 人拿到什么

pack-agent 给人：

1. **启动器**：版本库、隔离实例、一点启动。启动的是 DeepSeek Harness。当前只服务终端 profile（`dsh-tui`）。
2. **整合包装卸器**：`.pack.zip` / `.pack.json` / `*.pinst.zip` 装进去，白名单停用或删实例卸掉。给别人的就是一份整合包：解压根目录是 `pack-agent-for DSH.exe` 和 `.pack-launcher/`，运行时 dll 打进单文件 exe，对照 PCL 解压包里的 `PCL.exe` + `.minecraft`。exe 旁边的 `.pack.zip` 启动时旁路装进去。打这份目录：`packagent dsh launcher portable-init`。
3. **管理器**：三块一起做。
   - 这份实例的 `--profile` 层：顶栏管理页列出 `dsh.profile.bundles`（含 TUI），对每一行 `dsh plugin --profile <name> update|add|remove`。走磁盘上的 `$DSH_HOME`，不依赖管理口。`pad cli plugin list|add|remove|update` 同一条链。组合包行和左侧实例标读该包自己的图：盘上 `icon.png` / `logo.svg` / `docs/assets/logo.svg`；npm tarball 常不含 `docs/`，就按 `package.json` 的 `repository` 从 GitHub 拉 `docs/assets/logo.svg`，缓存 `library/plugins-meta/`。`dsh-plugin.json` v0.15 没有 icon 字段；TUI 仓库里的 `dsh-ecosystem-spec`（含 tui-channel）是插件准入与协议，不是启动器图标字段。
   - 内容加载器：每实例投影 skill / MCP / 规则 / 指令；PA019 认 `--dump-config` 的 `- id: pack-agent`。
   - 正在跑的那份 Harness：该 profile 有 `@sakikotgw/pad-gateway` 时，PAD 读 DSH **官方** `@deepseek-ai/dsh-host-apiproxy` 的全量 RPC（workspace / session / agentPreset / skill / command / settings / credentials / llm），数据与 DSH Web UI 同源。`session.list` 的 `running` 就是 agent 是否在跑。不在 PAD 窗发消息。无管理口时管理页仍能更新 TUI 和组合包。

## 0. 桌面端

PAD 是 **.NET 9 WPF 原生窗口**，跟 PCL 同源，不是网页套壳。骨架同构 PCL（无边框顶栏、内页返回、左栏大按钮、卡片节奏），配色自己一套偏冷低饱和。固定尺寸、只留最小化与关闭；落到小屏时按工作区 clamp，不吊在屏幕外。

`pack-agent-for DSH.exe` 是**唯一真值**：窗口和 launcher 逻辑都在 C#，同一个二进制带命令行入口（`pad cli release|instance|profile|plugin|run|ps|gateway|where|market|…`），所以脚本和窗口不会对同一操作有两套说法。双击这一个 exe 就打开启动器。npm 包只保留跑在 DSH 进程里的插件。

设置按 PCL 分层，存在 `<launcher-root>/pad.json`（schema v4）：`launch`（终端 / 标题 / 启动后行为 / 默认发行号 / 额外环境变量 / Node / 启动前命令 / 新建实例工作区隔离）、设置左栏 **API Key**（`DEEPSEEK_API_KEY`）、`ui`（主题含自定义色 / logo / 壁纸路径与透明度滑条 / 动效速度 / 顶栏文字 / 字标 / 提示 / 窗宽高边距顶栏 / 刷新壁纸）、`download`（npm registry / 超时 / 货架 / 快捷按钮）、`other`（确认停止 / 刷新秒 / registries 路径 / 截图延时）。坏 JSON 会备份再回默认。每层可单独恢复默认。主题色是 PAD 自己的冷色板，禁止 `#1370f3`。窗几何默认 1000×620，可在设置改。壁纸与 logo 可换图。卡片标题带圆角色块小标识。实例可在 `instance.json` 的 `launch` 覆盖同名键，空 = 跟随全局。工作区隔离在实例设置切换，不跟 `$DSH_HOME` 走。启动脚本优先用该发行号 `engines.node` 对应的 `runtime/node/<ver>/`。顶栏启动 / 管理 / 下载 / 设置。点启动弹出带阴影的悬浮卡片走进度；管理页左栏实例、右边这份实例的组合包（随附层 / TUI / 管理口）。版本设置左栏：概览 / 设置 / 插件 / session / agent-preset。插件页对照 PCL Mod 管理，只列另外 `dsh plugin add` 的包；标签用 keywords。整合包走投影，不进插件名单。

PAD 不把 DSH 装进自己窗口。终端 profile 在真终端里跑（Windows Terminal 优先，可在设置里换 conhost 或自定义命令行），PAD 生成一份 `launch-<profile>.cmd` 交给它，那个脚本人能自己双击。

```
foo.pack.zip  →  实例 foo  →  pad cli run foo
```

## 2. 对照

| MC / PCL | 本产品 |
|---|---|
| 游戏版本 1.20.1 | DSH 发行号，如 `0.1.0-rc.7` |
| Forge | DeepSeek Harness |
| 整合包 zip | `.pack.zip` / `.pack.json` |
| `mods/*.jar` | 投影 `mods/<id>/`（skill / MCP / rule / command / hook） |
| Forge 模组 | `pack.dsh.plugins[]` → 该实例 `dsh plugin add` |
| `overrides/` | 拷进该实例工作区 |
| `versions\<名>\` + 隔离 | 独立 `DSH_HOME` + 工作区 |
| `.minecraft` 文件夹 | 一份实例（一份隔离 `DSH_HOME`） |
| `versions/<名>/` 一个可启动版本 | **一个 profile**（`$DSH_HOME/profiles/<name>/`） |
| 那个版本的 loader + `mods/` | 该 profile 的 `dsh.profile.bundles` |
| 大按钮「启动游戏」 | 启动选中的 profile |
| 存档 / 世界 | session，每条绑一个 agent；同一实例的多个 profile 共用（DSH 定的） |
| 角色组成 | agent-preset（`standard` / `code` / `minimal` / `cordis` + 用户层 copy） |

pack-agent 管理器 = 该实例 `--profile` 层的组合包更新（含 TUI，不依赖管理口）**加上** 内容加载器 **加上** 该实例 Harness 里的 agent-preset 名册、session，以及经官方 apiproxy 对正在跑的实例发的管理 rpc。每个整合包本身禁止 `dsh plugin add`。

## 2.1 原则

PCL 是产品形状。下面六条同时成立，后一条不能推翻前一条的装包路径。

| 原则 | 落到本产品 |
|---|---|
| 好用 | 见 §2.2。整窗拖入再进版本设置。禁止首页单独做一块大拖拽框。 |
| 高性能 | 引擎留在现有 `packagent dsh` 进程。检索走 `pack-index` 的 SQLite FTS。注册表 `id → 路径` O(1)。不另起 Rust 后端，不另起套壳。 |
| 准确严谨 | 学 rustc：`error[PAxxx]` 拦动作，`warning[PAxxx]` 必须印出来，主句英文。版本号以 npm 实测为准。失败实例留下，状态 `import-failed`。不默默改 `~/.dsh`，不补默认值。 |
| everything is a plugin | 沿袭 DSH：有 `dsh.bundle` 的进该 `--profile` 的 `dsh.profile.bundles`；管理器本身是组合包。启动器自己的页也是注册表：内建 `modpack/registries/pad-pages.json`，用户覆盖 `<launcher-root>/library/registries/pad-pages.json`，可 `disabled: true`，不能删内建 id。skill / MCP / rule / command / hook 走投影，包目录禁止 `dsh plugin add`。 |
| 高度自定义 | 用户覆盖 `<launcher-root>/library/registries/`；自定义 `--profile`；`overrides/`；命名白名单。内建 id 可 `disabled: true`，不能从映射里删掉。 |
| 安全 | 每实例新建 `DSH_HOME`。凭据库 0600，导出剥 `.credentials.yaml`。`DSH_*` 只走 spawn env。`overrides` 禁止带凭据文件。`dsh plugin add` 在该实例 home 跑第三方 install 脚本，隔离是边界；P0 不做沙箱。 |

禁止把「学 rustc」读成「启动器改写成 Rust」。禁止把 everything is a plugin 读成「每个 skill 都 `dsh plugin add`」。

## 2.2 好用

对照 PCL 2.13.1.1 源码，不是对照「有一个导入区」。

PCL 窗口 `FormMain.xaml` 设 `AllowDrop="True"`。`FormMain.xaml.vb` 的 `FileDrag` 在整窗接文件，按后缀分流：`.xaml` 当主页、`.jar` 当 Mod、`.zip`/`.mrpack` 走 `ModpackInstall` 再切任务页。左栏 `PageSelectLeft` 的「导入整合包」只是一个列表项，弹选文件，走同一条 `ModpackInstall`。exe 旁边的 `modpack.zip` 由 `PageLaunchLeft` 启动时自动装，成功后删包。首页永远是大按钮「启动游戏」加当前实例名。

落到本产品：

1. **首页主角是启动。** 左栏 54px `run` + 当前实例名。导入是次要动作。
2. **整窗拖入。** 文件落到窗口任意处，走 `format-sniff` 再 `import`。拖入时窗口可以换光标或描边，禁止常驻一块虚线大框。
3. **导入然后配置。** `import` 成功：列表立刻出现新实例，选中它，打开版本设置。新建实例同一条：建完就进版本设置。`--profile`、白名单、展示字段在那里改。`DEEPSEEK_API_KEY` 在设置 → API Key，写入 `library/credentials.yaml`，不属于某一实例。不另起导入向导。给别人的解压包：`pad cli portable-export <dir> <instance>` 把该发行号和实例拷进目录，对照 PCL 文件夹里已经有 `.minecraft`。解压后 `home` / 工作区改写到这份拷贝；`launch-*.cmd` 用 `%~dp0` 相对路径并运行时读 yaml；剥 `.credentials.yaml`、`pad-cli.log`、`runtime.json`。对方在设置 → API Key 填一次。拷进目录的 PAD 必须是单文件 exe，开发宿主报 PA117。发给别人的压缩包用 `.7z` 或 `7z.sfx` 自解压 exe：pnpm 文件数会超出 ZIP64，Windows 压缩文件夹会把 zip 报成无效。
4. **选文件和旁路同一条链。** 「导入整合包」按钮弹选文件。exe / 启动器根旁的 `.pack.zip` / `.pinst.zip` 启动时扫描，成功后删包。三条入口都进 `import`。
5. **空态有行动。** list 空结果带 `emptyState{title, hint, action}`。前端不自己编。
6. **进度不挡启动页。** 装发行号的进度条留在下载页；看队列去下载页左栏「下载任务」，右边分正在下载的 / 下载完成的。只列装发行号和 `dsh plugin add|update|remove`，不列克隆实例。点启动跳进实例管理页，启动过程有进度条和动画。成功后停 / 日志 / 工作区也在实例管理页。
7. **拖入先分流。** 文件夹拒。一次一个整合包。已在版本设置的投影页再拖 `pack.json`，走该实例 `project`，不新建 home。
8. **装完换层。** 创建并安装 profile 成功回启动。市场搜索卡对照 PCL 下载 Mod：版本=该实例钉的 DSH 发行号，「装到」=哪份实例。点进插件：信息卡、版本芯片（全部 / latest / 主.次）、可下版本名单，点一行再装。点下载立刻提示开始装，装完再提示结果。没有实例就进版本选择，没有 profile 进版本设置。禁止用「请先到某页」把人撵走。禁止把 `--profile` 旗标画在搜索卡上。左栏对照 PCL 只放分类，不把正在下塞在分类下面。分类底下「下载任务」点进去，右边显示正在下载的 / 下载完成的。
9. **发行号列表来自 npm packument。** 下载页列出 `@deepseek-ai/dsh` 的全部 `versions`（丢掉 `0.0.1-rc.1`），对照 PCL 选版本。ComboBox 可手填。空卡仅当远程列表和本地版本库都空。

## 3. 红线

- 只做 DSH。不做 Cursor / Claude / Codex 多 harness 启动器。
- 投影目录禁止 `dsh plugin add`。
- 管理器每实例装一次。PA019 认 `--dump-config` 的 `- id: pack-agent`。
- 诊断主句英文。用户覆盖内建条目 PA109 warning，能继续。可 `disabled: true`，不能删内建 id。
- 窗口出错必须弹窗。界面线程卡住不抛异常时，用系统弹窗写 `pad-crash.log` 后结束进程，禁止只冻住。切页若本页 Content 指向自己，PA040 拦住并弹窗。
- `import` 失败：实例留下，状态 `import-failed`，stderr 提醒人失败了、还在盘上、怎么删。
- 出厂 profile 只有 `web` / `headless`。没有 DSH 官方 tui 模板。PAD 钉推荐自定义 profile `dsh-tui`，组合包 `@deepseek-harness-tui/dsh-tui`。
- 两实例禁止共用 `DSH_HOME`。人不拦拷 session 目录；打开失败只报 error。
- `DSH_*` 只走 spawn env，不写 `.env`。
- 版本清单以 **npm 实测**为准。禁止按源码树假设存在 `0.1.0-rc.5` 包。
- 禁止裸装 `@deepseek-ai/dsh-base` / `dsh-web-app` / `dsh-headless`（它们的 `latest` 仍是 `0.0.1-rc.1`）。
- 新实例一律新建目录，不默默改写 `~/.dsh`。
- 引擎在现有 `packagent dsh` 进程里。不另起 Rust 后端，不另起套壳。
- 行为种类走注册表。引擎源码不写死嗅探列表、import 步骤、`--profile` 随附模板名。`web` / `pack.json` / `install-manager` 只出现在内建 JSON 和测试里。
- **定义只用 DeepSeek Harness 原词。** 组合包、普通依赖、`dsh.profile.bundles`、`dsh.client`、`--profile`、`$DSH_HOME`、agent-preset、session。不准自造「面 / 件 / shell.kind」这类分类。启动器自己的词只保留：实例、版本库、投影、白名单。

## 4. 四层同时活着

| 层 | 盘上 | 人做什么 |
|---|---|---|
| 版本库 | `<root>/versions/<dsh-ver>/` | 装 / 校验 / 列出 / 删。有实例钉着的不能删。 |
| 实例 | `<root>/instances/<id>/` | 独立 `DSH_HOME` + 工作区。相当于一个 `.minecraft` 文件夹。 |
| profile | `$DSH_HOME/profiles/<name>/` | **可启动条目**，相当于一个版本文件夹。名册直接扫盘得出，不在 `instance.json` 里另存一份，所以 PAD 和 DSH 不会各说各话。 |
| agent-preset | 发行号随附 `config/agent-presets/` + 该 home `.agent-presets/` | 列出 / copy 到用户层 / 删用户层。随附只读。 |
| 会话 | 该 home 的 jsonl | 列出时带 `agentPreset`（header，后被 `agent-preset/selected` 覆盖）。删单条。不做跨实例挂载。人自己拷目录不拦。 |

多版本、多实例、多名册、多会话一直在盘上。进程上 A 开着可以再开 B。

禁止把 `--profile` 名、组合包、投影、白名单、agent-preset 写成「版本」。

## 5. `dsh plugin add` 与 `--profile`

学 PCL：启动器不猜 jar 是不是界面。PCL 只做版本隔离、磁盘文件冲突、更新时警告可能不兼容。

落到 DSH：

1. 一个实例 = 一份 `DSH_HOME`。`run` 只执行 `dsh --profile <name>`，`<name>` 写在 `instance.json` 的 `profile.name`。
2. `dsh plugin --profile <name> add`：有 `dsh.bundle` 的包进入该 profile 的 `dsh.profile.bundles`；没有的按 DSH 原样当普通依赖并转述那句 warning。不按「界面」拦截第二个组合包。
3. 同一配置行：后一层 patch 整段替换 `config`。启动失败 = DSH 非零退出。装完给人看 `--dump-config` 的层栈。
4. 一份实例 = 一份 `DSH_HOME`。`launcher import` 新建实例，不接到已有 home 上。启动器没有「关掉隔离」。
5. 同一份 `DSH_HOME` 里可以有多个 `--profile`。`web` / `headless` / 自定义名叠的组合包不同，工作区和 session 仍是这份 Harness 的：按工作区真实路径分项目，再按 session id 分。禁止按 `profiles/<name>` 再拆 `session-persistence-jsonl.root`。
6. 两个实例禁止同一个 `DSH_HOME`。PA020。
7. 人自己把 session 目录拷进另一份 home：启动器不拦、不确认、不收拾。打开时 DSH 拒或崩，只把 error 渲染出来。打不打得开是拷的人的事。
8. 往已经有 session 的实例里 `dsh plugin add` 组合包：PA021 warning，打印后继续。无 TTY 同样打印后继续。
9. 双击 `pack-agent-for DSH.exe` 开 PAD 窗（WPF 原生，顶栏启动 / 管理 / 下载 / 设置，点启动弹出进度悬浮卡片），在窗里管理整合包、agent-preset、正在跑的实例、session。窗默认逻辑尺寸 1000×620（`ui.windowWidth/Height`），只有最小化与关闭。PAD 不 `dsh plugin add` 自己。自定义 TUI 走 profile `dsh-tui` + 组合包 `@deepseek-harness-tui/dsh-tui`。不在 PAD 窗里做 DSH 聊天。要管正在跑的实例，就给该 profile 加 `@sakikotgw/pad-gateway` 这一层，PAD 读 `$DSH_HOME/pad-gateway.json` 后调官方 apiproxy 的 rpc。

随附模板只有 `web` / `headless`。其他名字第一次 `plugin add` 时只有 `@deepseek-ai/dsh-base`。PAD 对自定义 profile `dsh-tui` 钉组合包 `@deepseek-harness-tui/dsh-tui`。

## 6. 投影和组合包

| 种类 | 字段 | 怎么装 |
|---|---|---|
| 投影 | skill / MCP / rule / command / hook | 投影 + 白名单。包目录禁止 `dsh plugin add`。 |
| 组合包 | `pack.dsh.plugins[]` | 该实例 `dsh plugin --profile <name> add`，进 `dsh.profile.bundles`。 |

## 7. 现在有 / 没有

| 状态 | 项 |
|---|---|
| 有 | 投影、白名单、管理器、SQLite 检索 |
| 有 | 两版本目录、两实例 home 隔离、双进程 `run`、rustc 形诊断 |
| 有 | 注册表、`launcher import` / `export`、overrides、`dsh.plugins[]` 真 `add` |
| 有 | 根目录 zip 旁路扫描、凭据库、收编 `~/.dsh`、PAD 窗（WPF）、`pack-agent-for DSH.exe` 双击入口与 `pad cli` |
| 有 | 该实例 agent-preset 名册 list/copy/remove；session 列表带 `agentPreset` |
| 有 | `@sakikotgw/pad-gateway`：包住官方 apiproxy，广告在 `$DSH_HOME/pad-gateway.json`；PAD 调 `workspace.list` / `session.list` / `session.cancel` |
| 有 | 设置 → API Key：`library/credentials.yaml` 填 `DEEPSEEK_API_KEY`，拷进各实例 home；启动脚本运行时从 `%DSH_HOME%\.credentials.yaml` 读进环境，不把密钥写进 `launch-*.cmd`；缺则 PA116 |
| 有 | 本机共用启动器根名册 `%LOCALAPPDATA%\pack-agent-dsh\roots.json`（对照 PCL 文件夹列表）。换文件夹打开 PAD，空的 exe 旁根切到上次有实例的那份；不默默收编 `~/.dsh` |

## 8. 嗅探（第一期内建）

真值在 `format-sniff` 注册表。内建三条：`pack.json`（schema `ccui-pack/*` 或 `agent-pack-ir/*`）、`.pack.zip`、`*.pinst.zip`。匹配不上 → PA009。不实现 CurseForge / MMC / mrpack。用户可加条目，`handler` 必须是已有原语。

## 9. 代码落点

| 块 | 路径 |
|---|---|
| 启动器（窗 + `pad cli`） | `agent-pack-dsh/pad/`（Views/ + Core/），产物 `pack-agent-for DSH.exe` |
| 注册表 | `agent-pack-dsh/modpack/registries/*.json`；用户覆盖 `<launcher-root>/library/registries/` |
| CLI | 启动器 `pad cli …`；打包/投影 `packagent dsh` → `agent-pack-dsh/modpack/cli.ts` |
| 投影 | `agent-pack-dsh/modpack/{compile,catalog,map,registry}.ts` |
| 管理器 | `agent-pack-dsh/plugin/` → `@sakikotgw/pack-agent-dsh` |
| 检索 | `agent-pack-dsh/pack-index/` |
| agent-preset | `agent-pack-dsh/modpack/agent-preset-ops.ts` |
| 管理口 | `agent-pack-dsh/gateway/` → `@sakikotgw/pad-gateway`；PAD 侧 `pad/Core/Gateway.cs` |
| overlay | `agent-pack-dsh/cordis.ts` |
| Pack 类型 | `src/types.ts` 的 `PackDshLayer` |

## 10. 稿怎么读

1. 本文件 — 产品是什么
2. [modpack.md](modpack.md) — 对照 PCL 的装包链
3. [launcher-design.md](launcher-design.md) — 启动器功能
4. [launcher-architecture.md](launcher-architecture.md) — 引擎、数据、命令、硬事实
5. [launcher-p0.md](launcher-p0.md) — P0 计划（已落地）

看板指针：`.collab/board/architecture/01-dsh-launcher.md`。

**改口径必须同步这五处。** 禁止只改一份、留下旧「缺口 / 要补 / --shell / 挂载」。产品对用户的句子以本文件为准；功能步骤以 launcher-design 为准；引擎细节以 architecture 为准。architecture 不得发明产品层没有的命令和词。
