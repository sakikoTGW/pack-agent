# DSH 启动器功能设计

date: 2026-08-19
status: draft
scope: DeepSeek Harness only

对照：PCL 2.13.1.1（`E:\PCL\src`，`E:\PCL\NOTES.md`）。DSH 原语来自本机 `E:\tmp\pack-agent\dsh-src\deepseek-harness-master`。版本轴以 npm 实测为准。

产品定义见 [PRODUCT.md](PRODUCT.md)。装包链见 [modpack.md](modpack.md)。

## 1. 一句话

`.pack.zip` 丢进启动器 → 新建一份 `DSH_HOME` → `packagent dsh launcher run`。DSH 发行号是唯一版本轴。`--profile`、组合包、投影白名单、工作区、凭据都是实例设置。

定义只用 DeepSeek Harness 原词。启动器自己的词：实例、版本库、投影、白名单。

## 2. 对照

| PCL | 本启动器 | DSH |
|---|---|---|
| 游戏版本 | DSH 发行号 | 钉死的 `@deepseek-ai/dsh@<ver>` |
| Forge | DeepSeek Harness | 插件树 |
| 一个 `.minecraft` | 一份实例 | 一份 `DSH_HOME` |
| `versions\<名>\` 里的加载器+mods | 该实例的 `--profile` + `dsh.profile.bundles` | `$DSH_HOME/profiles/<name>/` |
| 版本隔离开：`saves` 在版本文件夹 | session 只写这份 `DSH_HOME`。没有「关掉隔离」 | `dshHomePath('sessions')`，再按工作区路径和 session id 分 |
| 人自己把世界文件夹拷进另一个版本 | 人自己把 session 目录拷进另一份 home | 不拦。打开失败只报 error |
| 启动游戏 | `run` | `DSH_HOME=… dsh --profile <name> --patch …` |

`--profile` 只决定叠哪些组合包。项目和 session 是这份 Harness 按工作区真实路径自己分的。

npm `@deepseek-ai/dsh`（2026-08-19）：`0.0.1-rc.1` / `0.0.1-rc.2` / `0.0.1-rc.5` / `0.1.0-rc.2` / `0.1.0-rc.3` / `0.1.0-rc.6` / `0.1.0-rc.7`。无 rc.4、无源码树上的 `0.1.0-rc.5`。`latest` 与 `next` 都是 `0.1.0-rc.7`。

随附模板只有 `web`、`headless`。其他名字第一次 `dsh plugin add` 时只有 `@deepseek-ai/dsh-base`。

## 3. 三层

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

## 4. 盘上

```
<launcher-root>/
  versions/<dsh-version>/
  runtime/node/<node-ver>/
  instances/<id>/
    instance.json
    home/                 # DSH_HOME
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

第一批表：`format-sniff`、`task-kinds`、`pa-codes`、`profiles`。

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

不按「是不是界面」拦截。同一配置行后一层整段替换。boot 非零退出记 crashed。装完给人看 `--dump-config`。改 bundles 必须重启该实例进程。

已有 session 再 `add` → PA021 warning，打印后继续。无 TTY 同样打印后继续。`--force` 只抬 warning，不改变「能继续」。

停用 pack-agent 管理器 → PA110。停用后该实例看不到投影。

介绍：`package.json` 的 `description` → README 首段 → 无。图标：`dsh.client` 包资源 → `icon` → 名字生成。缓存 `library/plugins-meta/`。

`engines.dsh` 已声明且对不上钉的发行号 → PA002。未声明 → PA101，不拦 add。

货架：读 awesome-dsh 的 `plugins.json`，安装仍是对该实例 `dsh plugin add`。没有第二套安装通道。

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

启动器根目录出现 `.pack.zip` / `.pinst.zip`，扫描后走同一条命令。Tauri 拖文件也走这条。链本身以 `task-kinds` 的 `import` 条目为准，下面是内建链。

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

- 启动器全局钥匙：`library/credentials.yaml`，0600。
- 命名钥匙：`library/credentials/<name>.yaml`。实例 `credentials.set: "work"` 时拷这一份。
- 新建实例默认拷进 `home/.credentials.yaml`。`credentials.kind=instance` 则不拷，等人填。
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

## 13. Tauri

桌面窗口调同一组 `launcher.ts` 函数，和 CLI `--json` 同构。

- 实例列表、当前钉的 `--profile`、一个启动按钮 = `run`。
- 拖 `.pack.zip` / `.pinst.zip` = `import`。
- 组合包列表、投影白名单、版本库、任务进度、诊断文本。
- 不在窗口里做 DSH 聊天。聊天在这份 Harness 里，`web` 用浏览器打开就绪 URL。
- 自定义 TUI profile：弹出系统终端，绑该 pid。
- 启动器升级和 DSH 发行号升级分开。

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
