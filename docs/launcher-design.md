# pack-agent 启动器功能设计

date: 2026-08-19
status: draft
scope: DeepSeek Harness only

对照：PCL 2.13.1.1（`E:\PCL\src`，`E:\PCL\NOTES.md`）。DSH 原语来自本机 `E:\tmp\pack-agent\dsh-src\deepseek-harness-master`。版本轴以 npm 实测为准。

产品定义见 [PRODUCT.md](PRODUCT.md)。装包链见 [modpack.md](modpack.md)。

## 1. 一句话

pack-agent 是启动器、整合包装卸器、管理器。当前只管理 DSH。`.pack.zip` 丢进去 → 新建一份 `DSH_HOME` → `pad cli run`。停用白名单或删除实例 = 卸。给别人的是整合包：解压根目录是 `pack-agent-for DSH.exe` + `.pack-launcher/`（对照 PCL.exe + `.minecraft`），运行时 dll 打进单文件 exe，可把已装好的实例拷进去。打这份目录：`packagent dsh launcher portable-init` 或 `pad cli portable-export <dir> <instance>`。发给别人用 `.7z` 或 `7z.sfx` 自解压，不要用 Explorer 打开 ZIP64 zip。双击 exe 开 PAD 窗（WPF 原生，顶栏启动 / 管理 / 下载 / 设置），在窗里管理整合包、agent-preset、正在跑的实例、session。DSH 发行号是唯一版本轴。`--profile`、组合包、投影白名单、工作区是实例设置。`DEEPSEEK_API_KEY` 在设置 → API Key（`library/credentials.yaml`），不属于某一实例。

定义只用 DeepSeek Harness 原词。启动器自己的词：实例、版本库、投影、白名单。

原则见 [PRODUCT.md](PRODUCT.md) §2.1。好用细则 §2.2。不把启动器改写成 Rust。skill 走投影，不 `dsh plugin add`。

## 2. 对照

| PCL | 本启动器 | DSH |
|---|---|---|
| 游戏版本 | DSH 发行号 | 钉死的 `@deepseek-ai/dsh@<ver>` |
| Forge | DeepSeek Harness | 插件树 |
| 一个 `.minecraft` | 一份实例 | 一份 `DSH_HOME` |
| `versions\<名>\` 里的加载器+mods | 该实例的 `--profile` + `dsh.profile.bundles` | `$DSH_HOME/profiles/<name>/` |
| 版本隔离开：`saves` 在版本文件夹 | `$DSH_HOME` 始终隔离。工作区隔离对照 PCL `VersionArgumentIndieV2`：可关，改成 `workspace.kind=existing` 与指定目录共用。session 仍只写这份 home | `dshHomePath('sessions')`，再按工作区路径和 session id 分 |
| 人自己把世界文件夹拷进另一个版本 | 人自己把 session 目录拷进另一份 home | 不拦。打开失败只报 error |
| 启动游戏 | `run` | `DSH_HOME=… dsh --profile <name> --patch …` |
| 模组列表 | 投影 + 组合包 | `pack.list` / `plugin.list` |
| 世界 / 存档 | session | `$DSH_HOME/sessions/…`，列出带 `agentPreset` |
| 角色组成 | agent-preset | 发行号随附 + `$DSH_HOME/.agent-presets` |

`--profile` 只决定叠哪些组合包。项目和 session 是这份 Harness 按工作区真实路径自己分的。

npm `@deepseek-ai/dsh`（2026-08-19）：`0.0.1-rc.1` / `0.0.1-rc.2` / `0.0.1-rc.5` / `0.1.0-rc.2` / `0.1.0-rc.3` / `0.1.0-rc.6` / `0.1.0-rc.7`。无 rc.4、无源码树上的 `0.1.0-rc.5`。`latest` 与 `next` 都是 `0.1.0-rc.7`。

随附模板只有 `web`、`headless`。其他名字第一次 `dsh plugin add` 时只有 `@deepseek-ai/dsh-base`。PAD 钉自定义 profile `dsh-tui` 的组合包 `@deepseek-harness-tui/dsh-tui`。

## 3. 四层

### 3.1 版本库

路径：`<root>/versions/<version>/`。

- 安装：`pnpm add @deepseek-ai/dsh@<ver>`，锁闭包进该目录。禁止裸装 `dsh-base` / `dsh-web-app` / `dsh-headless`。
- 校验：`--version` 等于目录名；`--dump-default-config` 里有 `id: session-persistence-jsonl`。
- 删除：有实例钉着 → PA006。
- 改钉：目标必须已校验；改完 PA103，不自动卸组合包。
- Node：每个发行号声明的版本；缺了装到 `<root>/runtime/node/<ver>/`。多份 DSH 可共用同一份合格 Node。

### 3.2 实例

一份实例 = 一份 `DSH_HOME` = `instances/<id>/home`。

- 新建 / 克隆 / 重命名 / 删除。
- `instance.json` 钉：`dsh.version`、`profile.name`、`profile.port`（仅 `web`）、`workspace`、`packs.allowSet`、`credentials.kind`。
- 两个实例的 home 路径不得相同。撞了 → PA020。
- 克隆：拷整份 home（含 `sessions/`、`attachments/`、`.credentials.yaml`）。工作区若改成新目录，旧 session 的 header.cwd 对不上新工作区，Harness 挂不上，文件留在 home 里。要同一项目：`workspace.kind=existing` 指原路径。
- 删除前列出将丢掉的 session 目录。运行中先停。
- 新实例建新目录。不写 `~/.dsh`，除非用户选收编。

### 3.3 工作区和 session

工作区是项目目录。session 是这份 Harness 里一条对话，绑一个 agent，磁盘：

```
$DSH_HOME/sessions/<工作区路径编出来的目录>/<session id>/
```

根由启动器 `--patch` 写成该实例 `home/sessions`，等于 `dshHomePath('sessions')`。禁止按 `profiles/<name>` 再拆。

- 列出 / 删单条 / 备份 home。
- 同一 `DSH_HOME` 里两个进程写同一个 session id → PA003。help：停掉另一个进程。`web` 和自定义 profile 同时 `run` 同一实例，也走这道锁。
- 不做挂载。没有 `shared/sessions`，没有 `session mount` 命令，`instance.json` 没有 mounts 表。两实例即使 `workspace.path` 相同，也各写各的 `home/sessions`。
- 人自己把 session 目录拷进另一份 home：启动器不拦、不确认、不改 jsonl、不改 header.cwd。实例照样能 `run`。打开那条 session 时，header.cwd 对不上 → `error[PA007]`；格式版本不对 → `error[PA015]`；DSH 崩了 → 转述退出码和日志。不默默修好。打不打得开是拷的人的事。

### 3.4 agent-preset

一份实例的 Harness 里，agent 的组成是 agent-preset。PAD 同时管启动和这份名册。

- 随附：钉住的发行号安装内 `config/agent-presets/`。只读。删 → PA112。
- 用户层：该实例 `home/.agent-presets/<id>/agent.cordis.yml`。id 必须匹配 `[a-z0-9][a-z0-9-]*`。
- `list`：用户层先扫，随附同名覆盖。与 DSH `dsh-agent-presets` 发现顺序一致。
- `copy <from> <to>`：整目录拷到用户层。目标已在名册或非法 id → PA113。
- `remove`：只删用户层。
- session 列表带 `agentPreset`：header 为创建时值，日志里最后一条 `agent-preset/selected` 覆盖。
- 不在 PAD 窗里 `ctx.agents.create` 代聊。新开 agent = 人在已 `run` 的 Harness 里选 preset。

### 3.5 管理侧门（官方 apiproxy）

PAD 是管理层：不只扫盘，还要和正在跑的 DSH 说话。用的是 DSH **官方**网关，不自造协议。

`@deepseek-ai/dsh-host-apiproxy` 只提供 `ctx.apiProxy`，自己不注册路由（官方 README 原文：carriers such as HTTP wrap it themselves）。Web 版由 web-app 接上 HTTP；终端 profile 没人接，所以本仓的 `@sakikotgw/pad-gateway`（源码 `agent-pack-dsh/gateway/`）接一份 loopback carrier：

- 独立 npm 包、独立插件行，`inject: [apiProxy]`。**不能**并进主插件 —— cordis 的 inject 会等服务就绪，没装网关的 profile 会连 tools / skills 一起等不到。
- 用官方 `toFetchHandler(ctx.apiProxy)`，监听 `127.0.0.1` 随机端口，广告写 `$DSH_HOME/pad-gateway.json`（token，0600），排障日志写 `$DSH_HOME/pad-gateway.log`。
- `GET /pad/ping` 是 carrier 自己的健康检查；其余原样交给官方协议：`POST /api/<method>` 带 `client-request` 信封，SSE 在 `GET /api/events.mux`。
- 它的 `cordis.patch.yml` 一次插三行：`dsh-host-apiproxy`、`dsh-host-directory-picker-native`、自己。apiproxy 的 inject 列表要 `directoryPicker`，`dsh-base` 不带。
- **必须是 `-native`，不能用 `-auto`**：`-auto` 的 inject 是 `['webServer','loader']`，终端 profile 没有 `webServer`，于是 picker 起不来 → apiproxy 起不来 → 网关永远不 `apply()`，静默挂住，一句报错都没有。
- 这些包的 npm `latest` 都是旧的 `0.0.1-rc.1`，必须钉到发行号那条线。
- `headless` 官方不挂 apiproxy，那种 profile 只能扫盘。

PAD 能读到的因此和 DSH Web UI 完全同源：`session.list` 每行带 `running`（agent 在跑）、`blank`（能否切 preset）、`agentPreset`；`workspace.list` 给出真实的 workspace 注册表。`agentPreset.select` 只在会话空白时可用，跑过一轮就 `agent-preset-locked`，这是 DSH 的硬锁。

## 4. 盘上

```
<launcher-root>/
  versions/<dsh-version>/
  runtime/node/<node-ver>/
  instances/<id>/
    instance.json
    home/                 # DSH_HOME；装了 pad-gateway 的 profile 跑起来后写出 pad-gateway.json
    workspace/            # 默认工作区；可改指已有项目
    logs/
    workspace/            # 默认工作区；可改指已有项目
    logs/
  library/
    credentials.yaml      # 0600，全局钥匙
    plugins-meta/
  launcher.json
  runtime.json
```

数据根可改，版本库和实例跟着搬。

## 4.1 注册表

行为种类不写死在 `launcher.ts`。

内建：`agent-pack-dsh/modpack/registries/*.json`。用户覆盖：`<launcher-root>/library/registries/`。实例只引用条目 id。

合成结果是 `id → 绝对路径`。查表 O(1)。读字段看 mtime，变了只重读该文件。坏文件 PA017，映射不切到坏路径。`fs.watch` 更新映射。正在跑的任务用开工时的映射快照。

第一批表：`format-sniff`、`task-kinds`、`pa-codes`、`profiles`、`pad-pages`。PAD 顶栏页走 `pad-pages`，`handler` 只能是已有 `page.*`。

- 多余字段、缺必填 → PA017。代码不补默认值。默认写在内建条目正文。
- 自建条目允许。跑起来引用不到 → PA018。自定义 `--profile` 名可以不在 `profiles` 表里。
- 条目可写 `requires`。闭包失败 PA018。有环 PA017，新表不用。
- `import` 步骤只引用引擎已有原语。表里写了没有的原语 → PA018。
- 同 id 覆盖 → PA109 warning，按新正文，能继续。
- 用户层可标 `disabled: true`，不能从映射里删掉内建 id。禁用后嗅探匹配不到 → PA009。
- 诊断主句英文。`error[PAxxx]` 码不变。
- `launcher doctor` 全表再跑一遍 schema / 闭包 / 环 / 原语能否解析。JSON 里不准写检查脚本。

`import` 查 `task-kinds` 的 `import` 链。管理器 spec 在 `install-manager` 条目，默认 `@sakikotgw/pack-agent-dsh`。PA019 认 `- id: pack-agent`。

## 5. 组合包

对该实例某个 `--profile` 做 `dsh plugin --profile <name> add|remove|update`。有 `dsh.bundle` 进 `dsh.profile.bundles`；没有则普通依赖，转述 DSH 那句 warning。

入口是顶栏管理页：列出该 `--profile` 随附层、TUI、管理口（`dsh-base` / `dsh-web-app` / `dsh-headless` / `dsh-tui` / `pad-gateway`），行上更新 / 移除，可添加，可全部更新。走 `$DSH_HOME` 磁盘，不依赖管理口，没启动也能做。版本设置**插件**页对照 PCL Mod 管理，只列另外 `dsh plugin add` 的包：搜索名称/描述/标签，打开文件夹，从文件安装，下载新插件，全选，过滤全部/可更新/试验中，行上详情/打开目录/更新/移除，试验行固化/丢掉。标签用 package.json keywords，不打「组合包」。整合包是投影，不进这张名单。`pad cli plugin list|add|remove|update` 同一条 `PluginOp`。行上的图是该包自己发布的：盘上 `icon.png` / `logo.svg` / `docs/assets/logo.svg`；没有就按 `package.json` `repository` 拉 GitHub 上的 `docs/assets/logo.svg`，缓存 `library/plugins-meta/`。`dsh-plugin.json` v0.15 没有 icon 字段。TUI 仓库里的 `dsh-ecosystem-spec`（含 tui-channel）是插件准入与协议，不是启动器图标字段。

不按「是不是界面」拦截。同一配置行后一层整段替换。boot 非零退出记 crashed。装完给人看 `--dump-config`。改 bundles 必须重启该实例进程。

已有 session 再 `add` → PA021 warning，打印后继续。无 TTY 同样打印后继续。`--force` 只抬 warning，不改变「能继续」。

停用 pack-agent 管理器 → PA110。停用后该实例看不到投影。

介绍：`package.json` 的 `description` → README 首段 → 无。图标链见上。缓存 `library/plugins-meta/`。

`engines.dsh` 已声明且对不上钉的发行号 → PA002。未声明 → PA101，不拦 add。

货架：读 awesome-dsh 的 `plugins.json`，安装仍是对该实例某个 `--profile` 做 `dsh plugin add`。下载页搜索卡对照 PCL：版本=发行号，装到=实例。没有第二套安装通道。

投影继续 `packagent dsh project|allow|deny|set-*`。投影目录禁止 `dsh plugin add`。

## 6. 包字段

`src/types.ts` 的 `PackDshLayer`：

```jsonc
"dsh": {
  "version": "0.1.0-rc.7",   // import 必填；精确号或 npm range，解析到已发布发行号
  "profile": "web",          // 默认 web；headless 或自定义名
  "persona": "coding",
  "preset": { "id": "standard" },
  "plugins": [
    { "spec": "@scope/pkg@1.2.0", "required": true }
  ],
  "overrides": [
    { "from": "overrides/AGENTS.md", "to": "AGENTS.md" }
  ]
}
```

- 无 `dsh.version` → PA012，不装。精确号：npm 没有这号 → PA012。range：已发布列表里取满足 range 的最高号；一个都没有 → PA012。用 node-semver。
- `plugins[].spec`：npm / `github:` / 路径。`required` 默认 true。`false` 的列出，不拦 import。无 TTY 同样列出并继续。
- `overrides` 拷进该实例工作区，不进 `DSH_HOME`，不进投影 `mods/`。禁止拷 `.credentials.yaml`。`from` 不在包里 → PA009。目标已存在：覆盖。
- `persona` / `preset` 是这份 Harness 跑起来之后的 agent-preset，不是版本，不是 `--profile`。

## 7. `import`

```
packagent dsh launcher import <pack.json|pack.zip|*.pinst.zip> [--name <id>]
```

启动器根目录出现 `.pack.zip` / `.pinst.zip`，扫描后走同一条命令。PAD 窗里拖文件也走这条。链本身以 `task-kinds` 的 `import` 条目为准，下面是内建链。

### 7.1 整合包

1. 嗅探：根文件 `pack.json` 且 schema 为 `ccui-pack/*` 或 `agent-pack-ir/*`。
2. 读 `dsh.version`；没有或 npm 没有这号 → PA012。
3. `version install` 若未装。
4. `instance create`：新 id、新 home、新工作区。`profile.name` = 包里的 `dsh.profile` 或 `web`。
5. 拷 `overrides` 进工作区。
6. 对该工作区 `project` + `allow` + `set-save`。
7. `dsh plugin --profile <name> add` 管理器一次；`--dump-config` 看不到 `- id: pack-agent` → PA019。
8. 必选 `dsh.plugins[]` 逐个 `add`；失败 → PA014，实例留下，状态 `import-failed`。stderr 必须写明 import 失败、实例还在、怎么删。可选的列出。
9. 可 `run`。

禁止把这条链接到已有实例的 home 上。要往已有实例装内容：对该实例工作区 `project`，或对该 `--profile` `plugin add`，走已有 session 的 PA021。

### 7.2 实例导出包 `*.pinst.zip`

manifest `pack-agent.pinst/v1`。解开 → 新 id → 钉的版本没有则先装 → 新 home。剥掉的凭据不还原。端口清空。

### 7.3 导出

`packagent dsh launcher export <id> [--out x.pinst.zip]`

打包 `instance.json` + home（去掉 `.credentials.yaml`）+ 工作区 `.agent-pack` 白名单名。PA104。钥匙不进 `instance.json`。

## 8. 凭据

DSH 读该 home 的 `.credentials.yaml`（`REF: 字符串`，0600）。

PAD 设置 → API Key 有 `DEEPSEEK_API_KEY` 的 PasswordBox，写入启动器 `library/credentials.yaml`（或具名 `library/credentials/<name>.yaml`）。空白 YAML 编辑器不算配置口。保存时拷进所有使用这份 API Key 的非收编实例 home。`launch-*.cmd` 在运行时从 `%DSH_HOME%\.credentials.yaml` 读出再 `set DEEPSEEK_API_KEY`（DSH 分层 env > 文件；wt/cmd 不继承 PAD 进程）。密钥不写进脚本文件。自有实例的 `DSH_HOME` / 工作区 / `bin.js` / 发行号 Node 跟 `%~dp0` 走，解压换目录仍能双击。缺这个值时启动报 `error[PA116]`，位置指 library，并切到设置 → API Key。管理口连上后 `credentials.describe` 为 false 也显示 PA116。不把密钥写进 `instance.json`、不写进 pack。

- 启动器凭据分发器：`library/credentials.yaml`，0600。
- 命名钥匙：`library/credentials/<name>.yaml`。实例 `credentialsSet: "work"` 时拷这一份。
- 新建实例默认从分发器拷进 `home/.credentials.yaml`。`credentialsSet=instance` 则不拷。收编的 home 不覆盖。
- 启动器不代聊，不把密钥写进 `instance.json`、不写进 pack。

## 9. 收编 `~/.dsh`

第一次打开启动器：

- 默认：不动 `~/.dsh`。
- 收编：建实例「本机原有」，`home` 指到现有目录，标 `adopted`。启动器不改这份 home 里的文件，除非用户在该实例上点了装包或 `plugin add`。
- 新实例永远新建目录。

两实例不得都指向 `~/.dsh`。PA020。

## 10. 进程

```
DSH_HOME=<instance/home>
DSH_AGENTS_HOME=<instance/home/agents>
cd <workspace>
<node> <versions/<ver>/…/bin.js> --profile <name> --patch <home>/launcher.patch.yml
```

`web` 另加应用参数 `--port`。就绪：stdout 出现 `dsh web: http://127.0.0.1:<port>`。自定义 profile 含第三方 TUI 组合包：系统终端里跑该进程。

- 不同实例可同时跑。
- 同一实例同一时刻一个 `run`。再 `run` → 先停或拒绝。
- `runtime.json`：实例 id、版本、`--profile`、pid、端口、状态。
- 停 / 重启只动其中一个。启动器挂了按 pid 认回。
- `DSH_TELEMETRY_DISABLED=1` 默认开。不写 `.env`。

## 11. 任务队列

装版本、校验、装组合包、投影、备份、克隆、import、export 进队列。可并行，互不抢同一实例目录。每条有状态、日志、可取消。

## 12. 日志

stdout/stderr → `instances/<id>/logs/`。可打开 home、工作区、日志。可跑 `dsh --dump-config` 落到该实例目录。版本损坏：已钉它的运行中实例先停，再补全或重装。

## 13. 桌面端（WPF）

`agent-pack-dsh/pad/`：.NET 9 WPF 原生窗口，跟 PCL 同源，不是网页套壳。UI 与 launcher 逻辑同在 C#，`pack-agent-for DSH.exe` 一个二进制既开窗口也当命令行（`pad cli …`），一份真值。

窗口：无边框 44px 顶栏、固定尺寸、只留最小化与关闭（对照 PCL 的 `ResizeMode="CanMinimize"`）。落到小屏按工作区 clamp。骨架同构 PCL，配色自己一套偏冷低饱和。PCL 只作视觉与手感参照，不复制它的 XAML 或 `.vb`。

顶栏四页，没有占位死按钮：

- **启动**：选中实例和大按钮。点启动弹出进度悬浮卡片。
- **管理**：这份实例的组合包（含 TUI）更新、添加、移除。有管理口时再显示正在聊的 session。
- **下载**：列出 npm packument 里 `@deepseek-ai/dsh` 的全部发行号（丢掉 `0.0.1-rc.1`），点装进度条留在下载页，成功进版本选择；社区资源搜索卡对照 PCL 下载 Mod：版本=DSH 发行号，「装到」选实例；点进插件看信息卡、版本芯片和可下版本名单再下。左栏只放分类，分类底下「下载任务」：右边正在下载的 / 下载完成的。只列装发行号和 `dsh plugin add|update|remove`，不列克隆实例。点装不自动切到这一栏。
- **设置**：左栏 API Key / 启动 / 个性化 / 下载 / 其他。API Key 层填 `DEEPSEEK_API_KEY`，并列出哪份 key 匹配哪些实例。`pad.json` schema v4；坏配置备份后回默认；每层可单独恢复。窗默认 1000×620。卡片标题带圆角色块小标识。其他层「多个启动器根」对照 PCL 文件夹列表：名册在 `%LOCALAPPDATA%\pack-agent-dsh\roots.json`。exe 旁 `.pack-launcher` 没有实例时，切到上次有实例的根，或本机嗅到的实例最多的根。便携包自己已有实例则不切。`PACK_LAUNCHER_ROOT` 已设则不切。不把 `~/.dsh` 收编进这条链。

内建 `pad-pages` 仍有 `tasks` 条目，`disabled: true`，不占顶栏。

实例内页（从版本选择进，顶栏换成回退键）：概览、设置、插件、session、agent-preset。版本选择对照 PCL PageSelect：左栏实例列表（名 + `$DSH_HOME` 路径）和「添加或导入」，右栏该实例的 profile 按终端 / 网页分组。点启动进实例管理内页：进度条、停止、日志、工作区。功能补全见 [launcher-pcl-depth.md](launcher-pcl-depth.md)。

启动终端 profile 时 PAD 生成 `instances/<id>/launch-<profile>.cmd` 交给终端执行，人能自己双击。`web` 交给浏览器。不在窗口里做 DSH 聊天。启动器升级和 DSH 发行号升级分开。

### 13.1 整合包接进窗口

投影轴不重写：投影编译器（TS）、unit 注册表、`pack-index`（Rust SQLite）是和 `packagent dsh` 共用的一份资产，C# 里再写第二份就等于对「这个实例启用了哪些包」给出两个答案。PAD 把它当子进程调 —— `node <repo>/bin/packagent.js dsh launcher --root <root> --json …` —— 拿 JSON 当真值。仓库路径来自 `PACK_AGENT_REPO`，或启动器根目录 / exe 旁边的 `.pack-agent-repo`。缺 bun 或找不到仓库时，实例页直接说这句，而不是显示一个像「这个实例没有整合包」的空列表。

窗口能做：投影一个 `.pack.zip` / `.pack.json` / `.pinst.zip`、扫根目录旁路 zip、列出已投影的包、启用/停用。停用只是从白名单摘掉，文件还在盘上。同一组操作在 `pad cli pack list|project|allow|deny|scan`。

### 13.2 热重载：试验 → 固化

DSH 只在启动时读一次 `dsh.profile.bundles`，改磁盘要重启才生效。官方 apiproxy 的 rpc 表里**没有** plugin 或 loader 方法（`session.*`、`workspace.*`、`agentPreset.*`、`skill.list`、`settings.*`、`credentials.*`、`llm.*`、`goal.*`、`host.*`、`subagent.*`），所以对正在跑的进程热挂插件这条路不存在。别假装有。

能做的是试验 → 固化，每一步都是 DSH 自己的操作：

1. **试验**：`dsh plugin --profile <name> add <spec>` 装进 profile，然后把 DSH 刚追加的那几行**从 `dsh.profile.bundles` 摘回来**（留下 DSH 原话里的那个状态：`installed as a plain dependency, not a profile layer`），改写成 `instances/<id>/trial-<profile>.patch.yml`，靠官方 `--patch` 只挂本次启动。所以平常启动不带它，「仅本次运行」是真的。
2. **固化**：把那几行写回 `dsh.profile.bundles`，删掉 overlay。下次启动生效。
3. **丢掉**：`dsh plugin remove` 卸掉，删 overlay。profile 回到试验前。

两条防线：已经在 `bundles` 里的包不许再试验（PA032）—— 否则「装完没有新增行」和「这包没声明 dsh.bundle」长得一模一样，一次误点就会把好用的层卸掉；候选包没声明 `dsh.bundle` 时报 PA105，并且只在这次 add 才带进来的情况下才卸，不动 profile 本来就有的依赖。

`--patch` 写进 `launch-<profile>.cmd`，不藏在 PAD 里：同一次双击要能重现同一次运行。CLI 同一组：`pad cli trial list|add|commit|drop`。

## 14. 诊断

学 rustc。`error[PAxxx]` 非零退出，拦住。`warning[PAxxx]` 打印后继续，必须看见。无 TTY 不交互问。`--force` 只抬 warning。

| 码 | 级 | 何时 |
|---|---|---|
| PA001 | error | 钉的发行号没装好 / 校验失败 |
| PA002 | error | 组合包声明了 `engines.dsh` 且对不上 |
| PA003 | error | 同一 `DSH_HOME` 里该 session id 已有可写进程 |
| PA004 | error | web 端口被占 |
| PA005 | error | 实例 home 不可写 |
| PA006 | error | 删除仍被钉着的发行号 |
| PA007 | error | 打开某条 session 时 DSH `attachSession` 拒了：cwd 对不上 / 非目录 |
| PA009 | error | pinst / pack 结构非法 |
| PA011 | error | 没有 pnpm |
| PA012 | error | 包没有 `dsh.version`，或 npm 没有这号 / range 一个都匹配不上 |
| PA013 | error | registry 不可达 |
| PA014 | error | 包里必选组合包 `dsh plugin add` 失败。实例留下，状态 `import-failed`。stderr 提醒人 import 失败了 |
| PA015 | error | 打开某条 session 时格式版本与钉的发行号不符 |
| PA017 | error | 注册表 schema / 多余字段 / 缺字段 / 环 |
| PA018 | error | 跑起来引用了合成表里没有的 id |
| PA019 | error | 管理器没进 `--dump-config` |
| PA020 | error | 两个实例 home 路径相同 |
| PA021 | warning | 已有 session 还 `add` 组合包 |
| PA101 | warning | 未声明 `engines.dsh` |
| PA102 | warning | 该 session 上次由另一发行号写入 |
| PA103 | warning | 改钉后组合包未验证 |
| PA104 | warning | 导出去掉了凭据 |
| PA105 | warning | 普通依赖，无 `dsh.bundle` |
| PA109 | warning | 用户层覆盖了内建注册表条目 |
| PA110 | warning | 停用管理器 |
| PA111 | warning | 删 session 后全文索引可能残留 |
| PA112 | error | 删除发行号随附的 agent-preset |
| PA113 | error | agent-preset id 非法、找不到、或已存在 |
| PA114 | error | `pad-gateway.json` 广告缺失或 Harness pid 已死 |
| PA115 | error | pad-gateway 拒或 apiproxy rpc 失败 |
| PA116 | error | `DEEPSEEK_API_KEY` 未配置。启动拦并切到设置 → API Key；管理口 `credentials.describe` 为 false 时写在实例管理页 |
| PA117 | error | `portable-export` 当前 PAD 不是单文件 exe。先 `dotnet publish -p:PublishSingleFile=true`，或设 `PACK_PAD_HOST_EXE` |
| PA032 | error | 这个组合包已经在该 profile 的层里，没有可试验的东西 |
| PA040 | error | 界面线程卡住（无异常），或某页把自己塞进自己的 Content。前者系统弹窗后结束进程；后者切页拦住 |

新失败模式出现再加码，写进本表。

## 15. 产品不做

- Cursor / Claude / Codex 多 harness 启动器
- 启动器窗口里做 DSH 聊天
- 投影目录 `dsh plugin add`
- 两实例共用一份 `DSH_HOME`
- 跨实例挂载、junction、`session mount|unmount`、`shared/sessions`
- 按 `profiles/<name>` 拆 `session-persistence-jsonl.root`
- 启动前扫描并拦掉人手拷进来的 session
- 出厂 tui 模板（上游没有；自定义 `--profile` + 第三方组合包）

## 16. 验收

1. 两个发行号目录同时在 `versions/`。
2. 两个实例 home 互不包含对方 `sessions/`。
3. A 跑着再开 B，`runtime.json` 两个 pid。停 A 不影响 B。
4. `import` 无 `dsh.version` → PA012，不建实例。
5. `import` 成功：新 home、工作区有投影且已 allow、`--dump-config` 有 `pack-agent`、必选组合包在该 `--profile` 的 `dsh.profile.bundles`。
6. 两实例 `workspace.path` 相同，A 的 session 文件不出现在 B 的 `home/sessions`。
7. 同一实例两个进程写同一 session id → PA003。
8. 投影目录不在 `dsh plugin` 依赖里；白名单允许后 SkillProvider 能列出 skill。
9. 导出 zip 无 `.credentials.yaml`。
10. 诊断含 `error[PA` 或 `warning[PA`、`-->`、`= help:`。
11. `instance.json` 无 `shell.kind`，有 `profile.name`。无 `sessions.mounts`。
12. 把 A 的一条 session 目录拷进 B 的 `home/sessions`：B 仍能 `run`。打开该条若 DSH 拒，stderr 有 `error[PA007]` 或 `error[PA015]` 或 DSH 原文。启动器不改那份 jsonl。
13. 注册表多余字段 / 缺字段 → PA017。未知 id 在运行引用时 → PA018。内建 `import` 链查表执行，不在 `launcher.ts` 写死步骤名。
