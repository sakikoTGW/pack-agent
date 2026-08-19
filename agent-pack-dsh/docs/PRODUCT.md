# agent-pack for DSH · 产品设计

date: 2026-08-19
status: canonical
product: **agent-pack for DSH**
代码：`agent-pack-dsh/`
入口：`packagent dsh`

本文件是产品定义。功能细则见 [launcher-design.md](launcher-design.md)，装包链见 [modpack.md](modpack.md)，引擎见 [launcher-architecture.md](launcher-architecture.md)。

`docs/PACK_SPEC.md` §0、`docs/NORTH_STAR.md`、APL 仍是仓级跨壳故事。本产品里「游戏版本」= DSH 发行号，只做 DeepSeek Harness。

## 1. 人拿到什么

一份 `.pack.zip` 丢进启动器，得到一个隔离实例，点一下就能在 DSH 里用这包的 skill / MCP / 规则 / 指令。

```
foo.pack.zip  →  实例 foo  →  packagent dsh launcher run
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
| 大按钮「启动游戏」 | `packagent dsh launcher run <id>` |

pack-agent 管理器 = 内容加载器。每个整合包本身禁止 `dsh plugin add`。

## 3. 红线

- 只做 DSH。不做 Cursor / Claude / Codex 多 harness 启动器。
- 投影目录禁止 `dsh plugin add`。
- 管理器每实例装一次。PA019 认 `--dump-config` 的 `- id: pack-agent`。
- 诊断主句英文。用户覆盖内建条目 PA109 warning，能继续。可 `disabled: true`，不能删内建 id。
- `import` 失败：实例留下，状态 `import-failed`，stderr 提醒人失败了、还在盘上、怎么删。
- 出厂 profile 只有 `web` / `headless`。没有 tui 模板。
- 两实例禁止共用 `DSH_HOME`。人不拦拷 session 目录；打开失败只报 error。
- `DSH_*` 只走 spawn env，不写 `.env`。
- 版本清单以 **npm 实测**为准。禁止按源码树假设存在 `0.1.0-rc.5` 包。
- 禁止裸装 `@deepseek-ai/dsh-base` / `dsh-web-app` / `dsh-headless`（它们的 `latest` 仍是 `0.0.1-rc.1`）。
- 新实例一律新建目录，不默默改写 `~/.dsh`。
- 引擎在现有 `packagent dsh` 进程里。不另起 Rust 后端，不另起套壳。
- 行为种类走注册表。引擎源码不写死嗅探列表、import 步骤、`--profile` 随附模板名。`web` / `pack.json` / `install-manager` 只出现在内建 JSON 和测试里。
- **定义只用 DeepSeek Harness 原词。** 组合包、普通依赖、`dsh.profile.bundles`、`dsh.client`、`--profile`、`$DSH_HOME`。不准自造「面 / 件 / shell.kind」这类分类。启动器自己的词只保留：实例、版本库、投影、白名单。

## 4. 三层同时活着

| 层 | 盘上 | 人做什么 |
|---|---|---|
| 版本库 | `<root>/versions/<dsh-ver>/` | 装 / 校验 / 列出 / 删。有实例钉着的不能删。 |
| 实例 | `<root>/instances/<id>/` | 独立 `DSH_HOME` + 工作区。`--profile`、组合包、投影白名单、凭据都是实例设置。 |
| 会话 | 该 home 的 jsonl | 列出 / 删单条。不做跨实例挂载。人自己拷目录不拦。 |

多版本、多实例、多会话一直在盘上。进程上 A 开着可以再开 B。

禁止把 `--profile` 名、组合包、投影、白名单写成「版本」。

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
9. Tauri 窗口调同一组函数。不 `dsh plugin add` 自己。自定义 TUI profile 用系统终端。不在窗口里做 DSH 聊天。

随附模板只有 `web` / `headless`。其他名字第一次 `plugin add` 时只有 `@deepseek-ai/dsh-base`。

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
| 有 | 根目录 zip 旁路扫描、凭据库、收编 `~/.dsh`、Tauri 薄壳（调同一组函数） |

## 8. 嗅探（第一期内建）

真值在 `format-sniff` 注册表。内建三条：`pack.json`（schema `ccui-pack/*` 或 `agent-pack-ir/*`）、`.pack.zip`、`*.pinst.zip`。匹配不上 → PA009。不实现 CurseForge / MMC / mrpack。用户可加条目，`handler` 必须是已有原语。

## 9. 代码落点

| 块 | 路径 |
|---|---|
| 启动器 | `agent-pack-dsh/modpack/launcher.ts` |
| 注册表 | `agent-pack-dsh/modpack/registries/*.json`；用户覆盖 `<launcher-root>/library/registries/` |
| CLI | `packagent dsh` → `agent-pack-dsh/modpack/cli.ts` |
| 投影 | `agent-pack-dsh/modpack/{compile,catalog,map,registry}.ts` |
| 管理器 | `agent-pack-dsh/plugin/` → `@sakikotgw/pack-agent-dsh` |
| 检索 | `agent-pack-dsh/pack-index/` |
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
