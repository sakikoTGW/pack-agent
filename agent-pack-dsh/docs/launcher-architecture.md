# DSH 启动器（PCL）架构设计

date: 2026-08-15
status: draft
depends_on: [PRODUCT.md](PRODUCT.md) + [launcher-design.md](launcher-design.md)（本文是实现设计）
scope: DeepSeek Harness only
ui: Tauri 调同一组 `launcher-api` 函数，与 CLI `--json` 同构。

## 0. 定位：pack-agent 的第三维度，同一项目

pack-agent 不是"打包工具 + 旁边一个新启动器项目"。**启动器是 pack-agent 自身的维度提升**，同一仓库、同一 CLI 家族、同一版本线：

```
维度① 打包（已有）      pack/export/install/sync —— 把 agent 封成 .pack.json，装进各 harness
维度② 投影（已有）      agent-pack-dsh/modpack / pack-index / plugin —— 整合包进 DSH 目录 + 白名单 + 实例内触角
维度③ 启动器（本文）    agent-pack-dsh/modpack/launcher.ts —— 编排前两维 + 版本库/实例/会话/插件/进程
        └─ ③ 是 ①② 的编排层：import = ② 的投影+放行 + ③ 的实例+运行，一条命令
```

因此：

- 本文所有"启动器"字样 = pack-agent 启动器层，不是独立产品；
- 复用不是"借别人的东西"，是**同一产品内部的层间调用**（§1 原则 4 的措辞按此理解）；
- 版本/发布：同一 monorepo、同一版本线，`packagent` 一个入口贯穿三维（§21.5）。

> 原语与行号以本机 `E:\tmp\pack-agent\dsh-src\deepseek-harness-master`（源码树标 `0.1.0-rc.5`）为证据，见 §2。**版本轴与可装清单以 npm 实测为准（E1），禁止按源码仓库假设存在 `0.1.0-rc.5` 发行包。**

---

## 1. 总览

```
pack-agent（同一项目 · 一个 CLI 家族 · 一条版本线）
┌─ Tauri 壳（agent-pack-dsh/tauri，invoke launcher_api）─┐
│  调同一组 launcher 函数 / 同一条 CLI         │
└──────────────┬───────────────────────────────┘
               │
┌──────────────▼───────────────────────────────┐
│ 维度③ agent-pack-dsh/modpack/launcher.ts（编排层）      │
│  version / instance / plugin / session /     │
│  run / job / export / diag                   │
│  同一 packagent 进程，不是第二套后端          │
└──────┬─────────────────────────┬─────────────┘
       │ 层内复用（维度①②）        │ 包壳（不改 DSH 源码）
┌──────▼──────────────────┐  ┌───▼──────────────────────────────┐
│ 维度① 打包层 src/ + mcp │  │ node + versions/<ver>/…/bin.js   │
│ 维度② 投影层 agent-pack-dsh/modpack │  │   plugin … / --port … /          │
│  / pack-index / plugin │ │   --dump-config（DSH_HOME=实例home）│
└─────────────────────────┘  └──────────────────────────────────┘
入口：`packagent dsh launcher <cmd>` → 同进程调用 launcher.ts
```

原则：

1. **引擎走现有 packagent 进程**（Bun/TS，`packagent dsh launcher …`，与 `project/allow/set-*` 同一条 CLI）。`pack-index` 继续 Rust，只因为 SQLite FTS。启动器是 pnpm + 进程 + JSON，不另起语言岛，不另起后端服务。Tauri 调同一组函数。设计见 [launcher-design.md](launcher-design.md) §13。壳在 `agent-pack-dsh/tauri/`。
2. **不改 DSH 源码**。一切通过 env / profile 目录 / `--patch` / `dsh plugin` 包壳实现。
3. **状态文件用 JSON，索引才用 SQLite**。`launcher.json` / `instance.json` / `runtime.json` 人类可读、可备份、可 diff。
4. **整合包轴不复刻**：投影/检索/白名单继续走 `packagent dsh` + `pack-index`；启动器只做"每实例一份工作区 + 命名白名单"的接线。
5. **诊断 rustc 风格**（§14）：同一编号一份人读文本 + 一份 JSON，所有 `error[PAxxx]` 拦动作、`warning[PAxxx]` 必须可见。
6. **行为种类走注册表**（§20）。引擎不写死种类名。`web` / `headless` 只出现在内建 `profiles` 与测试。

---

## 2. DSH 事实基线（设计所依据，已逐条验证）

| # | 事实 | 证据 |
|---|---|---|
| F1 | `dsh` 只有 `web` / `plugin` 两个子命令；`web` 是 `--profile web` 的硬编码别名；**出厂 profile 名另有 `headless`（见 F19），`tui` 只是文档示例的自定义 profile 名** | `apps/cli/src/bin.ts`、`apps/cli/src/args.ts:156`、审计 |
| F2 | 全局 flag：`--profile` `--patch` `--dump-config` `--dump-default-config` `-V/--version`；其后参数原样交给 app | `apps/cli/src/args.ts` |
| F3 | `--version` 只打印该安装的版本字符串（无前缀无多余输出）→ 可机检校验版本。源码树打印 `0.1.0-rc.5`；npm `@deepseek-ai/dsh@<ver>` 打印 `<ver>` | 审计 + E1 |
| F4 | home 解析实时读 env：`DSH_HOME` > `~/.dsh`；**每进程独立** → 实例隔离天然成立 | `packages/util/home-paths/src/index.ts:79-88` |
| F5 | profile = `$DSH_HOME/profiles/<name>/`：`package.json`（`dsh.profile.bundles` 有序列表 + 依赖）+ `cordis.patch.yml`（用户 patch 层，可 id 定向 override/disable/insert）+ pnpm 管理的 `node_modules` | `packages/boot/app-boot/src/profile.ts:1-21` |
| F6 | bundle = 声明 `dsh.bundle.patch` 的 npm 包；解析双锚点：先 dsh 安装（INSTALL_ANCHOR），后 profile 目录；`$DSH_HOME/profiles/node_modules` 是 boot 维护的 in-box 平铺软链 | `profile.ts:15-22,205`、`apps/cli/src/plugin.ts:36-45` |
| F7 | `dsh plugin` = 对 profile 目录跑 pnpm（相对路径锚到调用 cwd）+ 按"已装状态"回写 `dsh.profile.bundles`；依赖 pnpm 在 PATH（Windows .cmd shim） | `apps/cli/src/plugin.ts:104-157` |
| F8 | 会话 jsonl 根 = bundle 内 `id: session-persistence-jsonl` 的 `config.root: !!js dshHomePath('sessions')`；**root 必填无默认** → profile patch 可 id 定向覆盖 | `packages/bundle/base/cordis.patch.yml:98-101`、`session-persistence-jsonl/src/index.ts:59-68` |
| F9 | 会话布局 `<root>/<--cwd转义-->/<sid>/session.jsonl(.zstd)`；转义：`/ \ :`→`-`、其余非安全字符→`~XXXX`、首尾包 `--`、截 251 | `session-persistence-jsonl/src/format.ts:147-167` |
| F10 | 凭据 `$DSH_HOME/.credentials.yaml`，严格 `REF: 字符串` 映射（0600）；分层：继承 env > 文件 > `<cwd>/.env` > `$DSH_HOME/.env` | `packages/credentials/credentials-local/src/index.ts:1-9` |
| F11 | web 默认 `http://127.0.0.1:3080`；只能 `dsh web --port <n>`（0=系统分配）改；**无 PORT/DSH_PORT env**；`DSH_WEB_URL` 是输出变量 | `apps/web` cordis、审计 |
| F12 | Node 要求 `^22.19.0 || >=24.0.0` | 根 `package.json` engines |
| F13 | **DSH 无插件版本兼容声明机制**（无 `engines.dsh`）；只解析 `dsh.bundle.patch` / `dsh.profile.bundles` | 审计 |
| F14 | `DSH_AGENTS_HOME` 仅 skill-filesystem 用，缺省 `~/.agents` | `packages/skill/skill-filesystem/src/index.ts:164` |
| F15 | profile 名拒绝 `/ \ . .. node_modules`；`resolveProfileDir` = `$DSH_HOME/profiles/<name>` | `profile.ts:36,104-111` |
| F16 | patch 组合顺序：bundle 层 → profile `cordis.patch.yml` → `$DSH_HOME/cordis.patch.yml` → `--patch`（可重复）→ 遥测开关；**id 定向 patch = 整段替换该行 `config`（"replaces the targeted row's whole config"，所以每行必须重述它拥有的全部键）**；profile patch 模板原文："id-targeted config overrides, disables, and insert lists; `!!js` expressions allowed" | `apps/cli/src/profile-boot.ts:49-51,122-129`、`apps/cli/src/args.ts:132`、`packages/bundle/web-app/cordis.patch.yml:4-6`、`packages/boot/app-boot/src/profile.ts:127-131` |
| F17 | `.env` **禁止写 `DSH_*` 键**（BOOTSTRAP_PREFIXES）；shell 导出的 env 可用 → 启动器配置只走 spawn env，绝不写 .env | `packages/boot/app-boot/src/index.ts:117` |
| F18 | jsonl 物化用 `link()` 防覆盖、目录 0700；协调器是**单写者 + torn-tail 崩溃恢复**——同一 sid 双活进程 append 不受支持 | `session-persistence-jsonl/src/index.ts:536-556` |
| F19 | **出厂 profile 模板只有两个**：`web=[dsh-base, dsh-web-app]`、`headless=[dsh-base, dsh-headless]`；**没有 tui 模板**——`tui` 是文档里自定义 profile 名（`--profile tui` 落到 `DEFAULT_PROFILE_BUNDLES=[dsh-base]`，再 `dsh plugin add` 第三方组合包，如测试里的 `turtle-ui`） | `profile.ts:113-125`、`apps/cli/src/args.ts:68-71`、`apps/cli/tests/args.spec.ts:49-50` |
| F20 | web 就绪信号 = stdout 打印 **`dsh web: http://127.0.0.1:<port>`**（`console.log`，格式稳定）；`dsh --profile web --help` 不绑服务器（不产生该行） | `packages/bundle/web-app/src/index.ts:168`、`web-app/cordis.patch.yml:12` |
| F21 | profile 的 pnpm 配置 = `pnpm-workspace.yaml`（`nodeLinker: hoisted`、`autoInstallPeers: false`）；pnpm ≥10 从该文件读设置而非 .npmrc；首次 `dsh plugin` 自动 initProfile（package.json + cordis.patch.yml + pnpm-workspace.yaml） | `profile.ts:133-143`、`apps/cli/src/plugin.ts:120-125` |
| F22 | 遥测开关 `DSH_TELEMETRY_DISABLED`：**任何非空值**（含 `'0'`/`'false'`）禁遥测——privacy 开关宁错关不错开；实现为给 `session-telemetry-otel` 行生成的 disable patch | `apps/cli/src/profile-boot.ts:56-82,168` |
| F23 | 信号语义：**SIGTERM = 监管者普通停止请求，各界面都 exit 0**；SIGINT = 用户中断，exit 130 → 启动器判 stopped vs crashed 不能只看退出码 | `apps/cli/src/profile-boot.ts:218-222` |
| F24 | shipped agent-presets 在**安装内** `config/agent-presets/`（boot 时注入 presets 行的 roots）；用户 presets 在 `dsh-agent-presets` 自有可写根（实例 home）→ 版本 pin 自带 shipped presets，克隆拷贝 home 带走用户 presets | `apps/cli/src/profile-boot.ts:35,159-166` |
| F25 | `--resume <session>` 只是**透传 app 参数**（help 示例里的自定义 profile 用法）；**web bundle 不解析它**（web-app src 无 resume 匹配）→ web 续会话靠 UI 选会话，不靠 CLI | `apps/cli/src/args.ts:69`、web-app grep 无命中 |
| F26 | base bundle 共 75 行 in-box（session/attachment-local/session-query-sqlite/session-projection/settings/credentials/agent-instructions/skill-filesystem/commands/goal/subagent 家族/sandbox 家族/tool-fs/web/llm-deepseek/agent-loop…）——实例开箱能力全集；其中 `attachment-local` 根 = `$DSH_HOME/attachments/v1`（会话图片资产，克隆/备份必须带上） | `packages/bundle/base/cordis.patch.yml` 全表、`packages/attachment/attachment-local/src/store.ts:131` |
| F27 | 会话标题 = 日志内 `session/title` 事件 **last-wins fold**（log-backed，不进 header）；自动/用户重命名都 append 事件 → 启动器读标题必须扫 jsonl | `packages/session/session-title/src/index.ts:304-313,363-375` |
| F28 | jsonl 默认 **zstd 压缩**（`.jsonl.zstd`，torn-tail 帧结构）；boot 环境 = `loadLayeredEnv('dsh')` 冻结快照（`DSH_LAUNCH_ENVIRONMENT_KEY`，boot 前冻结）→ 读会话头（PA015 比对）与标题折叠都**必须** zstd 解码；启动器 env 改动只能靠重启实例 | `session-persistence-jsonl/src/format.ts:207`、`apps/cli/src/bin.ts:33`、`profile-boot.ts:250-252` |
| F29 | 宣传页「标准 / PTC / 极简 / 创造」是 **agent-preset**（安装内 `config/agent-presets/{standard,code,minimal,cordis}`），不是 launcher 的 shell/profile。出厂 profile 仍只有 `web`/`headless`。禁止把 preset 名做成启动器版本或出厂 profile | `apps/cli/config/agent-presets/*/preset.yml`、https://deepseek.com/harness |
| F30 | Web 工作区注册表落在 **`$DSH_HOME/storages/workspace.json`**（`storage-json` 的 unit 名 + `.json`；root = `dshHomePath('storages')`）。只拷 `sessions/` **不够**让 B 的侧栏分组与 A 一致 | `web-app/cordis.patch.yml:54-57`、`storage-json/src/index.ts:65`、`workspace/src/spec.ts:67-68` |
| F31 | `attachSession`：`sessionIds` 已含该 id 则**跳过** cwd 校验（两边不可变）。否则：无 cwd / realpath 失败 / 非目录 / `cwd !== workspace.path` 分别抛错。比较两边都是 `fs.realpath` 后的字符串 | `workspace/src/entity.ts:109-148` |
| F32 | 空 `DSH_HOME` 第一次 `dsh web`：`loadProfile` 对 `web`/`headless` 调 `initProfile`（写 package.json / 空 `cordis.patch.yml` / `pnpm-workspace.yaml`）+ `healProfilesModuleFallback`（`profiles/node_modules` junction）。**不为 in-box 跑 `pnpm install`**；in-box 从 INSTALL_ANCHOR 解析。`pnpm` 只出现在 `dsh plugin …` | `profile.ts:152-167,375-383`、`profile-boot.ts:98-102` |
| F33 | jsonl **没有** OS 文件锁。进程内按 sid 串行；进程间「每会话一个 live writer」是约定。`link()` / `MoveFileExW` 只防首次 materialize 覆盖。双进程 append 同一 sid 会交错字节。PA003 是启动器必须自建的唯一跨进程防线 | `session-persistence-jsonl/src/index.ts:544-590`、`coordinator.ts:581-583` |
| F34 | `persistence.list()` 只读每个制品的**第一帧/首行**（zstd 用 `readFirstZstdLine`），拿得到 header，拿不到标题。标题 = 日志内 `session/title` 或 web 的 projection cache。Web 续会话 = `session.create` 带已有 `sessionId`，不是 CLI `--resume` | `session-persistence-jsonl/src/index.ts:446-509,736-771`、`api-proxy.ts:1634-1661` |

对 spec 的修正（现实与 spec 不一致处，以现实为准）：

- spec §2/§3.2/§10/§15 已按 F19/E1 改：出厂无 TUI；版本清单跟 npm；启动命令带 `--patch`。
- spec §6/§16 已改：不做跨实例挂载。人手拷 session 目录不拦；打开失败报 PA007/PA015 或转述 DSH。
- spec §17「会话根如何接 jsonl root」：`--patch` overlay 覆盖 F8 的 `config.root` 到该实例 `home/sessions`（§8）。跨工作区 cwd 接不上，DSH 自己拒，启动器只渲染。
- spec §17「DSH 发行包从哪拉」已解决：npm 钉 `@deepseek-ai/dsh@<ver>` + 锁闭包（§4）。禁止裸装 `dsh-base` / `dsh-web-app` / `dsh-headless`（E2：三件套 `latest` 都是 `0.0.1-rc.1`）。
- 2026-08-15 夜间：官方四种「模式」= agent-preset（F29），不是 shell。§8 的 projectKey 改走 `launcher.ts`，作废 Rust 移植。换实例继续聊要同时看 `sessions/` 与 `storages/workspace.json`（F30）。

---

## 3. 盘上布局与文件 schema（细化 spec §4）

```
<launcher-root>/
  launcher.json                     # 数据根、源、默认值
  runtime.json                      # 运行中进程/端口账本
  jobs.json                         # 任务队列持久化（headless 任务）
  versions/<dsh-ver>/
    package.json                    # 占位私有包（pnpm 安装锚）
    node_modules/                   # @deepseek-ai/dsh@<ver> + 全部依赖
    version.json
    pnpm-lock.yaml
  runtime/node/<node-ver>/          # 启动器自管 Node（系统 Node 不达标时）
  instances/<id>/
    instance.json
    home/                           # 该实例 DSH_HOME（profiles/ sessions/ attachments/ .credentials.yaml settings.yaml …）
    workspace/                      # 默认工作区（owned）；existing 模式指向外部路径
    logs/                           # 每进程 stdout/stderr
  library/
    plugins-meta/<pkg>.json         # 插件介绍/图标缓存
    credentials.yaml                # 全局钥匙（0600；可选）
```

### launcher.json（v1）

```json
{
  "schema": "pack-agent.launcher/v1",
  "dataRoot": "绝对路径",
  "registry": {
    "type": "npm",
    "sources": [
      { "name": "官方", "url": "https://registry.npmjs.org", "ttlSec": 3600 }
    ],
    "strategy": "speed-prefer"
  },
  "nodeMirror": "https://nodejs.org/dist",
  "defaults": { "profile": "web", "isolation": true, "workspaceKind": "owned", "telemetryDisabled": true },
  "adoptedHome": null,
  "created": "ISO", "updated": "ISO"
}
```

- `registry.sources` 是**多源列表**（PCL 的多镜像模型，§18.1）：首次使用/定期并行测速排序，`strategy=speed-prefer`（默认）/`fixed`（钉第一个）；单个源失败自动回退下一个，全部失败 PA013。元数据（版本列表）按 `ttlSec` 后台缓存（§18.7），不阻塞主流程。
- 数据根可改：迁移 = 整体搬目录 + 重写 dataRoot（运行中实例先停，PA005 前置检查）。

### instance.json（v1）

```json
{
  "schema": "pack-agent.launcher.instance/v1",
  "id": "writing",
  "name": "写作实例",
  "display": { "info": "一句话介绍", "logo": null, "star": false, "category": "" },
  "dsh": { "version": "0.1.0-rc.7" },
  "profile": { "name": "web", "port": "auto" },
  "workspace": { "kind": "owned", "path": "<root>/instances/writing/workspace" },
  "plugins": { "disabled": ["modlens"] },
  "packs": { "allowSet": "writing" },
  "credentials": { "kind": "global" },
  "created": "ISO", "updated": "ISO", "pinnedAt": "ISO"
}
```

- `profile.name` = `dsh --profile` 的名字。随附模板只有 `web` / `headless`；其他名字是自定义 profile（F19）。`instance.json` 钉这一字段，没有 `shell.kind`。
- `port ∈ "auto"|0|正整数`：只在 `profile.name=web` 时作为 web 应用参数 `--port`；auto = 启动器预分配，0 = 交 DSH。
- `plugins.disabled` 是启动器意图；**真值是 profile 的 `package.json`**（F5/F7），每次操作后重读，不双写两份账。
- `packs.allowSet` = 该实例工作区 `.agent-pack` 的命名白名单名（`set-save/set-load` 已有），**实例创建即建工作区并写入默认 allowSet**。
- 克隆 = 深拷贝 home（含会话与 DSH 工作区注册表）+ 新 id/name、清 port。home 里的 workspace 记录与会话 header.cwd 仍指向**原工作区 realpath**。新实例若改用 owned 新目录，打开旧 session 时 DSH `attachSession` 会拒 → **PA007**。启动器不改 jsonl。要继续聊同一工作区，克隆后的 `workspace` 必须 `existing` 指向原路径。改钉 = 只改 `dsh.version` + PA103。
- 没有 `sessions.mounts`。session 根固定该实例 `home/sessions`。

### runtime.json（v1）

```json
{
  "schema": "pack-agent.launcher.runtime/v1",
  "entries": [
    { "instance": "writing", "version": "0.1.0-rc.7", "pid": 1234,
      "port": 3080, "status": "running", "startedAt": "ISO", "log": "instances/writing/logs/web-1234.log" }
  ],
  "ports": { "claimed": { "writing": 3080 } }
}
```

写入规则：原子替换（写临时文件+rename）；启动器重启时按 pid 认回存活进程（孤儿认领），pid 死则清 entry。

### version.json（v1）

```json
{
  "schema": "pack-agent.launcher.version/v1",
  "version": "0.1.0-rc.7",
  "nodeRange": "^22.19.0 || >=24.0.0",
  "installedAt": "ISO",
  "integrity": "pnpm-lock 完整性串",
  "verified": true,
  "verifyOutput": "0.1.0-rc.7",
  "sessionFormatVersion": 0,
  "patchRowIds": ["session-persistence-jsonl"],
  "profileTemplates": {
    "web": ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app"],
    "headless": ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-headless"]
  }
}
```

（后三个字段 = §19.7 的 compat 快照，verify 时从安装提取。）

---

## 4. 版本库

- **获取**：`pnpm add @deepseek-ai/dsh@<ver>`（cwd=`versions/<ver>/`，占位 package.json 为锚）；npm 不可达报 PA010 之外新增 PA013（registry 不可达）。registry url 来自 launcher.json（企业内部镜像可换）。**必须是完整依赖闭包安装**：`@deepseek-ai/dsh` 的依赖（`@deepseek-ai/dsh-base`、`-web-app`、`-headless` 等 in-box bundle）随之一起装进 `versions/<ver>/node_modules`——in-box bundle 按"安装锚点优先"解析（F6），闭包齐了才保证版本与 in-box 插件同 pin，否则会回退解析到当前安装。
- **禁止裸装 `@deepseek-ai/dsh-base`**（E2）：该包 `latest` 仍是 `0.0.1-rc.1`，`next` 才是 `0.1.0-rc.7`。只允许经 `@deepseek-ai/dsh@<ver>` 把闭包拉进来。
- **锁闭包是硬条件**：发布态 `dsh@0.1.0-rc.6` 对 in-box 依赖写的是 `^0.1.0-rc.6`。无 `pnpm-lock.yaml` 再解析时，后发的 `0.1.0-rc.7` 会被 caret 吸进来，版本目录名与实装闭包会漂。`verify` 必须断言 lockfile 存在，且 `--version` 输出等于目录名。
- **为什么 `versions/<ver>/` 是纯安装而不是该版本的 DSH_HOME**：spec §3.1 要求"许多实例可钉同一份安装"，§3.2 要求每实例独立 home（会话/凭据/插件归属实例，不归属版本）。实例 home 与版本安装分离正好满足两条；审计提出的"版本即 home"备选会让会话与凭据跟着版本走，违背实例隔离，故不采用。
- **Node 管理**：读该版本 package.json engines.node（F12 基线）；本机 Node 不满足 → 从 nodeMirror 下载到 `runtime/node/<node-ver>/`（解压即用，不装系统）；下载失败 PA010。多个版本可共用同一份合格 Node。
- **校验**：`<node> <versions/<ver>/node_modules/@deepseek-ai/dsh/lib/bin.js> --version`，stdout 精确等于 `<ver>`（F3）→ 写 `verified: true`；不等/非零退出 → PA001。**完整性加验**：跑一次 `--dump-default-config`，断言 bundle 层包含 `id: session-persistence-jsonl` 行——证明 in-box 闭包完整（缺该行说明安装不完整或版本漂移，仍 PA001）。
- **列表/删除**：删除前查引用（所有 instance.json 的 dsh.version）；有钉 → PA006 列出引用实例。删除 = 整目录 rm（无 job 占用时）。
- **多版本并存互不覆盖**；实例只存版本号字符串。
- **改钉**：`instance pin <id> <ver>`：目标版本必须 installed+verified（PA001），改后 PA103（插件未验证）；不自动卸插件。

> 注：`versions/<ver>/` 是"纯安装"，不是 DSH_HOME。in-box bundle 经 INSTALL_ANCHOR 从这份安装解析（F6），因此**版本与 in-box 插件天然同 pin**；用户插件装在实例 profile 里（§6）。

## 5. 实例

生命周期（§15 状态机）。创建步骤：

1. 检查钉的版本已装（PA001）；
2. `instances/<id>/`：instance.json + home/ + workspace/ + logs/；
3. 首次启动时 boot 自动物化 `home/profiles/<shell>/` 与 `profiles/node_modules` 平铺软链（F5/F6），启动器不手写这些；
4. workspace/owned 下预建 `.agent-pack/`（空目录）——整合包轴接线的锚；
5. **装管理器插件 + 冒烟**（§21.3：`dsh plugin add @sakikotgw/pack-agent-dsh@<pin>` + `packagent_list` 冒烟，失败 PA019）；
6. 凭据按 §11 落 `home/.credentials.yaml`。

- **隔离**：默认每个实例独立 home+workspace。`workspace.kind=existing` 允许指向已有项目（用户显式选择）。
- **删除**：列出将被丢掉的会话数（§7 枚举）后确认；运行中先停。
- **收编**（spec §12）：首启若 `~/.dsh` 存在，二选一：收编成 `adopted` 实例（home 指向现有目录，启动器只读其元数据、不写）；或留下不动。收编实例在 UI 上标"外部 home"。

## 6. 插件管理（实例设置，不是版本）

全部包壳 F7：`DSH_HOME=<实例home> <node> <versions/<ver>/…/bin.js> plugin --profile <shell> <pnpm args>`。启用集真值 = profile package.json 的 `dsh.profile.bundles`（F5）。

环境细节（F21）：profile 的 pnpm 走 `pnpm-workspace.yaml`（`nodeLinker: hoisted`、`autoInstallPeers: false`，pnpm ≥10 不看 .npmrc）——启动器**不代写 pnpm 配置**；profile manifest（package.json/cordis.patch.yml/pnpm-workspace.yaml）在首次 `dsh plugin` 时由 DSH 自动 initProfile，启动器不手建。

| 操作 | 实现 | 备注 |
|---|---|---|
| list | 读 profile package.json dependencies + bundles + 本地 node_modules 实际版本 | 不调 `dsh plugin list`（不存在） |
| add | `plugin … add <spec>`（spec 支持 registry/路径/git/别名，F7 锚定） | 装完读回真值 |
| remove | `plugin … remove <pkg>` | 依赖与 bundles 由 F7 回写 |
| update | `plugin … update <pkg>` | F7 按已装状态回写，含"新版获得 dsh.bundle" |
| disable | bundles 列表移除该包（依赖保留）+ 记入 instance.json.disabled | 再 enable 恢复；disabled 集合每次操作后与真值对账。**停用只用 bundles 移除，不用 patch 层的 disable 操作**——启用集真值唯一，避免双账 |
| enable | 从 disabled 移除 + 塞回 bundles 尾部 | |

**兼容检查（launcher 自定约定，DSH 尚无——F13/E6）**：

- 约定：插件 package.json `"engines": { "dsh": ">=0.1.0-rc.7 <0.2.0" }`（semver range）。DSH 自己不看。
- **生态目前全空**：npm 上几乎没有 `engines.dsh`。按「未声明 = 已验证失败」会让 **PA101 条条响、PA002 几乎不响**，这是错门。
- 未声明 → **PA101** warning（元数据缺失，不当成已验证，**不拦 add**）。
- 已声明且 range 不满足 → **PA002** error（拦 add/update/改钉；`--force` 不抬 error）。
- 真门禁：`--dump-config` 看不见该 bundle 行、启动失败、崩溃分析（§18.2）。PA002 只处理「作者写了范围且写错」这一种。
- 依赖装了但没有 `dsh.bundle` → **PA105** warning（普通依赖，F7 已有此警告，launcher 侧复述口径）。
- pack-agent 自家 `dsh-plugin` 发布时带头声明 engines.dsh，做生态示范。

**装完必须重启**：`dsh plugin add` 只改 profile `package.json` 的 dependencies/bundles。长寿命进程的 HMR 只 watch `cordis.patch.yml`（`profile-boot.ts` `watchUserPatches`），**不重读 bundles**。运行中 add 不会热进树；启动器在 add/remove/update 成功后把该实例标 `restart-required`，下次 `run` 前必须 stop+start。

**磁盘**：每个实例每个 profile 一份 `node_modules`（F21 hoisted）。多实例钉同一 DSH 版本仍会各肥一份插件树。P0 接受；P2 再评估硬链/pnpm store 复用，且不得破坏实例隔离。

**多个组合包、两个 `--profile`**（PRODUCT §5，学 PCL 隔离、不猜界面）：

- `dsh plugin add` 有 `dsh.bundle` 就进当前 `profile.name` 的 `dsh.profile.bundles`。不按界面类型拦截。
- 装完跑 `--dump-config`。boot 非零退出 → crashed。
- 已有 session 再 add 组合包 → PA021 warning，打印后继续。
- `import` 只建新 `DSH_HOME`。两实例共用一个 home → PA020 error。同一 home 的多个 `--profile` 共用 `dshHomePath('sessions')`，按工作区路径分，不按 profile 名拆 root。
- Tauri 不进 `dsh.profile.bundles`。

**安全**：`dsh plugin add` = 在该实例 home 里跑第三方 install 脚本（pnpm prepare / git allowBuilds）。最低门：只装声明了 `dsh.bundle.patch` 的包；装完 `--dump-config` 可见该行；失败进崩溃分析。不在 P0 做沙箱。

**元数据**（spec §5）：描述 `package.json description` → README 首段 → 无；图标 `dsh.icon|icon` → README 首图 → 名字生成；缓存 `library/plugins-meta/<pkg>.json`。停用 pack-agent 管理器插件前警告（spec §5）。

## 7. 整合包（每实例白名单，复用现有轴）

- 实例工作区 = 该实例的 `.agent-pack` 根；投影/检索全部落在工作区，天然实例隔离——**上一轮实验"两 agent 同目录各装各的装不了"的问题，在启动器里被"每实例一个工作区"结构性解决**。
- 白名单 = 命名 allow-set（`pack-index` 已有 `set-save/set-load/set-list`，`packagent dsh` 已暴露）：
  - 实例创建：在工作区执行 `set-save <allowSet>`（默认集 = 创建时 enabled 集快照）；
  - 允许/停用 = `packagent dsh allow/deny` 后 `set-save` 回写该实例的命名集；
  - 切换 = `set-load <allowSet>`。
- 禁止把投影目录 `dsh plugin add`（spec §5 红线，沿用）。
- 开工前顺手修两个已知瑕疵：投影里 `.agent-pack-origin.json` 被嵌套成目录（`agent-pack-dsh/modpack/compile.ts`）、`search` 不过滤 enabled 的口径（`pack-index` main.rs）——二者都影响"白名单即所见"的可信度。

## 8. 会话

- **根**：实例会话根 = `instances/<id>/home/sessions`。落地方式 = **启动器自有的 `--patch` overlay 文件**（F16：`--patch` 层晚于 profile/home 层应用；比改 profile 自带的 `cordis.patch.yml` 更干净——不碰 DSH 用户层的文件，随时可见可删）：

  ```yaml
  # instances/<id>/home/launcher.patch.yml（启动器生成，启动时 --patch 注入）
  # 写死绝对路径，不用 !!js（静态值无需求值；字符串加引号防 Windows 反斜杠歧义）
  - id: session-persistence-jsonl
    config:
      root: 'E:\\launcher-root\\instances\\writing\\home\\sessions'
  ```

  语义要点（F16 原文：patch "replaces the targeted row's whole config"，每行必须重述它拥有的全部键）：bundle 层该行**只拥有 `root` 一个键**，所以 overlay 重述 `{root}` 即完整；将来 DSH 若给该行加键，version verify 的 `--dump-default-config` 快照比对会暴露（§4/PA107）。**注意 F17：配置只走 spawn env / patch 文件，绝不写 .env。**
- **枚举**：按 F9 规则解析 `<root>/*/*/`；会话"属于哪个工作区" = 目录名反解 projectKey（lossy，配合 session 头里的 cwd 精确化）。`projectKey` / `encodeSegment` 在 **`agent-pack-dsh/modpack/launcher.ts` 移植**（与 DSH `format.ts:121-167` 同算法），golden 钉死：`E:\pack-agent → --E-pack-agent--`、含非安全字符 → `~XXXX`。P1 加测试，禁止再写一份 Rust 实现。**显示信息**：`list()` 只解第一帧拿 header（F34）；标题要扫 `session/title` 或读 web projection，P1 先显示 sid + cwd + createdAt，标题可后补。
- **删除/备份**：删除 = rm 单个 `<sid>` 目录；备份 = 打包 home/sessions + **home/attachments/**（F26 会话图片资产，克隆/备份必须带上）。会话删除与 `session-query-sqlite`：默认 `openAt: never`（不建索引）→ rm 安全；若实例启用了全文搜索，删除后提示重建索引（**PA111** warning）。
- **写锁**：同一 `DSH_HOME` 里两个进程写同一个 sid。锁文件放该 sid 目录 `owner.json`，`create_new` 原子占位：`{instance, pid, dshVersion, acquiredAt}`。第二个进程 → **PA003**。stale 锁（pid 已死）可回收。DSH jsonl 是**单写者 + torn-tail 崩溃恢复**，双活进程 append 同一 sid 不受支持（F18/F33），PA003 是唯一跨进程防线。两实例各有 home 时各锁各的文件；人若自己 junction 成同一 inode，打开时文件系统或 DSH 怎么炸我们就怎么报，不维护共享锁表。
- **不做挂载**。没有 `shared/sessions`，没有 `session mount|unmount`，`instance.json` 没有 mounts。两实例即使 `workspace.path` 相同，也各写各的 `home/sessions`。
- **人手拷**：把 A 的 `<sid>` 目录拷进 B 的 `home/sessions`，启动器不拦、不确认、不改 jsonl。B 仍能 `run`。打开那条时：
  - header.cwd 的 realpath ≠ B 的工作区 → DSH `attachSession` 拒 → **PA007**
  - header.version ≠ 钉版本 `SESSION_FORMAT_VERSION` → DSH `SessionFormatUnsupportedError` → **PA015**
  - 格式相同但发行号不同 → **PA102** warning，仍打开
  - 组合包把内容写坏、进程崩 → 转述退出码和日志
  不默默改 cwd、不 migrate 格式、不在 `run` 前扫描清场。后果由拷的人承担。
- **续会话**：web 下靠 UI 选会话（`--resume` 只是透传 app 参数、web 不解析，F25）。侧栏分组在 `home/storages/workspace.json`（F30），拷 jsonl 不会自动带上分组。


## 9. 进程监管

启动（spawn 环境）：

```
DSH_HOME=<root>/instances/<id>/home
DSH_AGENTS_HOME=<root>/instances/<id>/home/agents   # 可选，缺省 ~/.agents（F14），隔离时显式设
cwd=<workspace>
cmd: <node> <root>/versions/<ver>/node_modules/@deepseek-ai/dsh/lib/bin.js \
       --profile <shell> --patch <home>/launcher.patch.yml \
       [ --port <p> ]                              # 仅 web kind；launcher flag 后的参数原样进 app（F2）；headless/profile 无端口
```

配置只走 spawn env 与 `--patch`；**不写任何 .env / DSH_* 键**（F17）。

- **就绪检测（web）**：扫描 stdout 等待 **`dsh web: http://127.0.0.1:<port>`**（F20，`console.log` 格式稳定）；拿到即把 URL/端口写入 runtime.json entry、状态 running。超时（默认 30s，launcher.json 可配）且进程未退 = 卡启动 → crashed 态 + 提示看日志（§18.5）。`--help` 类调用不会打印该行，所以**不用 `--help` 探活**。
- **端口**：launcher 端口账本 = runtime.json.ports；`auto` 时先占位（bind 探测 `127.0.0.1:<n>` 递增），与账本冲突 → PA004（note 指向占用实例）；外部进程占用 → 探测失败 → PA004（note 外部占用，help 换端口）。不用 `--port 0` 因 DSH_WEB_URL 只是输出、记账复杂（F11）。
- **日志**：stdout/stderr 双流重定向 `instances/<id>/logs/<shell>-<pid>.log`。
- **账本**：启动成功（进程存活 + 就绪信号）写 runtime.json entry；退出/崩溃清 entry、状态改 crashed（保留最近一次退出码 + 日志路径）。**crashed 是中间态**（PCL ModWatcher 同款语义）：进程已死但崩溃现场未清理，UI 可一键「查看崩溃分析」（§18.5）后再清。
- **启动器退出策略**：启动器退出时对运行中实例三选一（launcher.json 默认「询问」，无 UI 时「留下」）：留下（只清账本，pid 认回）、自动停、跟随退出。下次启动按 pid 认回。
- **孤儿认领**：启动器重启后按 pid 认回（Windows：OpenProcess 探测），活着 → 恢复 entry；死了 → 清。
- **stop** = 温和终止（taskkill / SIGTERM）→ 超时强杀；只动目标实例。**restart** = stop+start（保留端口占位）。**crashed 判定**（F23）：启动器发过 SIGTERM/SIGINT 且进程按信号退出（exit 0/130）= stopped，不判 crashed；只有未请求的退出（非零且非信号路径）才判 crashed。
- **遥测**（F22）：实例启动 env 默认置 `DSH_TELEMETRY_DISABLED=1`（privacy-first，launcher.json 可关）；env 在 boot 前冻结（F28），改动只能重启实例。
- `dsh --dump-config` 输出到 `instances/<id>/logs/dump-config.yaml`（诊断用，F2）。

## 10. 任务队列

- 任务：install-version / verify-version / install-plugin / project-pack / backup / clone / export / import / launch-headless。
- **任务树 + 加权进度**（PCL LoaderTask/LoaderDownload/LoaderCombo 模型，§18.1）：任务是组合体——install-version = [解析元数据(5%) → 下载文件(80%，可多文件分片) → 校验(10%) → 记账(5%)]，每叶子有 ProgressWeight，整体进度 = 加权和。事件流按 `queued → running → progress{taskId, weight, done, total} → done|failed|cancelled` 推给前端（进度页是事件流，不是轮询 JSON）。
- 互斥：**以实例目录为锁粒度**（内存互斥表 + 目录锁文件）；同一实例同一时刻一个写任务；不同实例可并行（spec §9）。
- 状态机：queued → running → done|failed|cancelled；cancel 只对 queued 立即生效，running 打标、任务在检查点退让。
- jobs.json 持久化 headless 类任务（退出码+日志路径），其余内存即可。
- 装版本期间已装好的实例照常可跑（互斥只锁实例）。

## 11. 凭据

- 全局钥匙：`library/credentials.yaml`（0600，与 DSH 同构 `REF: 字符串`，F10）。
- 新建实例：默认复制全局钥匙 → `home/.credentials.yaml`（0600）；`credentials.kind=instance` 时留空由用户手填（UI 引导）。
- 导出默认剥 `home/.credentials.yaml` → PA104；**钥匙永不进 instance.json**（spec §8 红线）。
- 启动器不代聊、不代写密钥内容（只搬运文件）。

## 12. 导入导出

- 导出 `*.pinst.zip`：instance.json + home/（剥 `.credentials.yaml` 默认）+ 工作区 `.agent-pack` 白名单集名 + logs 不含；加 manifest `{schema:"pack-agent.pinst/v1", exportedAt, stripped:["credentials"]}`。
- 导入整合包：走 [launcher-design.md](launcher-design.md) §7.1。导入 `*.pinst.zip`：解包 → 校验 schema（坏包 PA009）→ 新 id/name → 钉的版本没有则先装 → 新 home。剥掉的凭据不还原。端口清空。
- 启动器根目录出现 `.pack.zip` / `.pinst.zip`，扫描后走同一条 `import`。Tauri 拖文件也走这条。

## 13. 命令面（`packagent dsh launcher`）

```
packagent dsh launcher version  list|install <ver>|verify <ver>|remove <ver>
packagent dsh launcher instance list|create <name> [--version --profile --port --workspace --allow-set]
                          |clone <id> <new-name>|rename|remove|pin <id> <ver>|info <id>
packagent dsh launcher plugin  list <id>|add <id> <spec>|remove <id> <pkg>
                          |update <id> <pkg>|enable <id> <pkg>|disable <id> <pkg>
packagent dsh launcher pack    project <id> <pack>|allow <id> <pack-id>|deny <id> <pack-id>
                          |set-save <id> <name>|set-load <id> <name>|list <id>
packagent dsh launcher session list <id>|delete <id> <sid>|backup <id> [<sid>]
packagent dsh launcher run     <id> [--detach]|stop <id>|restart <id>|ps|logs <id>
                          # run 默认等待就绪：web 等到 F20 信号，headless 等到退出码；--detach 立即返回
packagent dsh launcher job     list|cancel <job-id>
packagent dsh launcher export  <id> [--out x.pinst.zip]|import <pack.json|pack.zip|*.pinst.zip> [--name n]
packagent dsh launcher diag    render <json-file>
packagent dsh launcher doctor
```

- 全局 `--json`：所有命令输出机器 JSON；默认人类可读文本，诊断走 rustc 风格渲染（stderr）。
- Tauri 面 = 同一组函数，参数/返回值与 `--json` 同构（tauri command 薄转发）。
- TS 薄壳：`packagent dsh launcher <cmd> …` 透传 bin（保持"人用 packagent 一个入口"）。

## 14. 诊断体系（PA 表 v1）

渲染规则：主句 + `-->` 位置 + 上下文 + `note/help`。`error` 非零退出，拦住，无 TTY 不交互问。`warning` 打印后继续，必须看见。`--force` 只抬 warning。每条一对双输出：文本 + JSON `{code, level, message, location, context, note, help}`。

| 码 | 级 | 触发 | note / help 要点 |
|---|---|---|---|
| PA001 | error | 钉的版本未装 / 校验不符 | help: install / verify 该版本 |
| PA002 | error | 插件**已声明** engines.dsh 且与钉的版本不兼容 | note: 声明范围与钉版本；help: 改钉 / disable / 升级插件。未声明走 PA101，不走本码 |
| PA003 | error | 同一 `DSH_HOME` 里该 session id 已有可写进程 | note: holder pid/版本；help: 停那个进程 |
| PA004 | error | web 端口被占 | note: 占用方（本启动器实例 / 外部进程）；help: 换端口 / 停占用实例 |
| PA005 | error | 实例 home 不可写 / 数据根不可写 | help: 权限 / 磁盘 |
| PA006 | error | 删除仍被实例钉着的版本 | note: 引用实例清单；help: 先改钉 |
| PA007 | error | 打开某条 session 时 DSH `attachSession` 拒了：cwd 对不上 / 非目录 | help: 工作区改回 header.cwd，或别打开这条。启动器不改 jsonl |
| PA008 | error | 实例目录锁被任务持有 | note: holding job；help: 取消任务 |
| PA009 | error | 导入包结构非法（schema 不符） | note: manifest 内容 |
| PA010 | error | Node 不达标且下载失败 | help: nodeMirror / 手动放 runtime/node |
| PA011 | error | pnpm 不在 PATH（插件操作需要，F7） | help: 装 pnpm 或 corepack |
| PA012 | error | 包没有 `dsh.version`，或 npm 没有这号 / range 一个都匹配不上 | help: 写精确已发布号 |
| PA013 | error | registry 不可达 | help: launcher.json 换镜像 |
| PA014 | error | 包里必选组合包 `dsh plugin add` 失败 | 实例留下，状态不是可 run |
| PA015 | error | 打开某条 session 时格式版本 ≠ 钉版本 `SESSION_FORMAT_VERSION`（DSH 读侧硬拒，§19.1） | note: 两侧格式版本号；help: 改钉到同格式版本 / 别打开这条。不拦 `run` |
| PA016 | error | 钉版本随附模板快照缺少 `web` 或 `headless` | help: 换发行号 / 重新 verify。自定义 `profile.name` 不走本码 |
| PA017 | error | 注册表加载/schema 校验/条目引用闭包校验失败（§20） | note: 注册表名 + 失败条目；help: 修复或还原内建 |
| PA018 | error | 跑起来引用了合成表里没有的 id | note: 引用处 + 未知 id；help: 补条目或改引用 |
| PA019 | error | 实例内 pack-agent 管理器未生效（安装或工具冒烟失败，§21.3） | note: 冒烟输出；help: 重装插件 / 查 pnpm 日志 |
| PA020 | error | 两个实例 home 路径相同 | help: 各用各的目录 |
| PA021 | warning | 已有 session 还 `add` 组合包 | 打印后继续。无 TTY 同样打印后继续 |
| PA101 | warning | 插件未声明 engines.dsh（元数据缺失） | 不当成已验证；不拦安装。真门禁看 dump-config / 启动 / 崩溃分析 |
| PA102 | warning | 会话最后写入版本 ≠ 当前实例版本 | 打开人手拷来的 session 时提示；不拦 |
| PA103 | warning | 改钉后插件未验证 | help: 跑 plugin list 复核 |
| PA104 | warning | 导出已剥离凭据 | |
| PA105 | warning | 依赖无 dsh.bundle，装为普通依赖 | |
| PA106 | warning | 收编的 ~/.dsh 被外部改动 | help: 重新收编 |
| PA107 | warning | overlay patch 的目标 id（如 `session-persistence-jsonl`）在钉的版本 bundle 中不存在 | 版本漂移，override 静默失效；help: 重新 verify 该版本 / 改钉 |
| PA108 | warning | 同会话根下出现 realpath 相同的两个 slug 目录（projectKey 截断/大小写碰撞，§19.2） | help: 规范化工作区路径 |
| PA109 | warning | 用户层覆盖了内建注册表条目（行为偏离内建，§20.1） | note: 被覆盖条目 id |
| PA110 | warning | 停用 pack-agent 管理器插件（spec §5 警告的落实，§21.3） | help: 停用后实例内看不到投影整合包 |
| PA111 | warning | 删除会话但实例启用了全文搜索（`session-query-sqlite` 索引残留，F26） | help: 重建索引 |

实现：`agent-pack-dsh/modpack/launcher.ts` 的 diag：码表 + 渲染器 + `--force` 语义。码表一份 JSON 供以后 Tauri 直接消费。

## 15. 状态机

实例：`created → ready(版本+profile 就绪) → running → stopped/crashed`；`adopted`（外部 home）只读元数据。任务：`queued → running → done|failed|cancelled`。同一 home 同一 sid 写锁：`free → owned(pid,ver) → stale(pid 死) → free`；冲突即 PA003。

## 16. 分期与验收映射（spec §16 十条机检）

| 期 | 交付 | 覆盖验收 |
|---|---|---|
| P0 能跑 | 版本库 + 实例 CRUD + 双 `run` + rustc 诊断。`profile.name` 已落地。已写死 `web`/`headless` 的判断，下一刀删掉改查 `profiles`。 | 1、2、3、4、10 |
| 下一刀 | 注册表解释器（路径映射、热更新、PA017/018/109）+ `launcher import` 查 `task-kinds.import` | 5、8、13 |
| 随后 | 同 home 写锁（PA003）+ 打开失败报错（PA007/PA015/PA102）+ 导出（PA104）+ 凭据 + 收编 ~/.dsh（PA106）+ 任务队列（PA008）。**已落地。** | 6、7、9、11、12 |
| 壳 | Tauri。`agent-pack-dsh/tauri/`，invoke `launcher_api` | |

P0 计划：[launcher-p0.md](launcher-p0.md)。

## 17. 风险与开放项

1. **projectKey 移植漂移**：Rust 重实现必须 golden 测试钉死（含中文路径 `~XXXX` 样例）；DSH 改转义规则时启动器要跟版。
2. **registry 可达性**：`@deepseek-ai/dsh` 是否在 npmjs 公开可装需开工即验证（`npm view @deepseek-ai/dsh`）；不可达则 registry 配内网源，PA013 已备。
3. **patch 目标 id 漂移**：`session-persistence-jsonl` 的 id 若在新版本改名，overlay 的 override 静默失效（会话落回 home 根）。防线：version verify 时用 `--dump-default-config` 断言该 id 存在（§4）；每次启动前对 overlay 再验一次 → 缺失 PA107。
4. **pnpm 依赖**（F7）：launcher doctor 检查；不捆绑 pnpm（一期），缺失 PA011。
5. **自定义 profile**：DSH 无出厂 tui 模板（F19）。文档示例 `tui` = `--profile tui` + `dsh plugin add` 第三方组合包。同一 `$DSH_HOME` 里可以和 `web` 并存；`run` 启动当前 `profile.name`。自定义 TUI 用系统终端，见 launcher-design §13。
6. **人手拷 session**：启动器不拦 Explorer / `cp`。打开失败只报 PA007/PA015 或转述 DSH。人不自己做 junction 就不需要 junction 权限。
7. **DSH 同 sid 双活写**：DSH 本身不支持，启动器锁即全部防线；锁崩（启动器被强杀）时靠 stale 回收，存在极小窗口，一期接受、文档写明。
8. **engines.dsh 约定**：pack-agent 单方引入，DSH 上游未来若定义同名字段需对齐。P0/P1 把它当可选元数据，不当生态门。
9. **会话格式版本门**（§19.1）：DSH 读侧对格式版本不符是硬拒。人手拷进来的 session 打开时 PA015，不拦实例 `run`。若 DSH 未来改 `SESSION_FORMAT_VERSION`，launcher 的 version.json 快照要随 verify 重取。
10. **projectKey 编码漂移与碰撞**（§19.2）：UTF-16 code unit 迭代 + 251 截断是碰撞源；Rust 移植靠 golden 测试钉死，运行时靠 sid 扫描 + PA108 兜底。DSH 一旦改转义规则，需引入 `projectKeyVersion` 快照字段。

## 18. PCL 对照优化清单（研究来源：`E:\PCL\src` 2.13.1.1 + `E:\PCL\NOTES.md`）

研究结论：PCL 值得抄的不是"下载中心"的表象，而是五组机制。以下每条标注 [落地位置] 与 [期]。

### 18.1 多源测速 + 加权进度管线（下载层）

PCL 的 `ModDownload.vb`：官方源/BMCLAPI 镜像**并行探测并记录耗时**（"官方源加载耗时"）、失败自动回退、列表与文件分层下载、逐文件 hash 校验（MD5/SHA1）；`LoaderTask/LoaderDownload/LoaderCombo` 把下载组合成**带权重的任务树**，进度是整棵树的加权和。

- 多源：launcher.json `registry.sources[]` + 测速排序 + 回退（已写入 §3）。
- 任务树 + 加权进度 + 事件流（已写入 §10）。
- 逐文件完整性：版本安装靠 pnpm lockfile integrity；插件安装同理；`verify` 命令可单文件重验。[§4][P0]

### 18.2 崩溃分析器（PCL 口碑最好的功能）

`ModCrash.vb CrashAnalyzer`：自动收集多位置日志（crash-reports/、logs/latest.log、启动器日志）、支持导入崩溃包、按文件类型分类、给出分析。

DSH 版 = **实例非零退出/crashed 时自动聚合**：`logs/*.log` + 最近一次 `--dump-config` + 会话 jsonl 尾段（可选）→ 分类（进程退出码 / 插件加载错 / 端口冲突 / 凭据缺失）→ 对照 PA 码表 + **常见问题库**（`library/faq/`，纯 markdown 条目，可外置更新）输出建议。[§9 crashed 态 + §11][P1]
与 PA 体系的关系：PA 码 = 机检结论，CrashAnalyzer = 现场聚合 + 解法库，二者互补。

### 18.3 导入即启用

NOTES 的对照结论："PCL 装完就能玩，DSH 停在 project 默认不放行"。PCL `ModModpack.vb`：选 zip → 判断种类 → 解包 → 写配置 → **列表立刻出现新实例、首页按钮变「启动游戏」**。

DSH 版 = 一体命令：

```
packagent dsh launcher import <pack|dir> [--name <n>]
  = 嗅探 → version install → instance create → overrides → project+allow → 管理器 → plugins[] → 可 run
```

一条命令完成「丢包 → 隔离实例 → 能 run」。拖 zip 和根目录扫描走同一函数。`--profile` 来自包里的 `dsh.profile`，默认 `web`。

### 18.4 格式嗅探导入

PCL 读压缩包根文件判断 CurseForge/HMCL/MMC/MCBBS/Modrinth 六种。DSH 第一期只认：`pack.json`（schema `ccui-pack/*` 或 `agent-pack-ir/*`）/ `.pack.zip` / `.pinst.zip`。不实现 CurseForge / MMC / mrpack。用户不指定类型，按根文件嗅探。

### 18.5 进程状态机细节

`ModWatcher.vb`：每进程一个 Watcher，状态含 **Crashed 且进程未退** 的中间态；启动器退出时对游戏进程的处理策略。

- crashed 中间态 + 启动器退出策略（留下/自动停/跟随）已写入 §9。[P0]
- 额外一项：**启动耗时超时判定**（web 实例 N 秒内未打印 `dsh web: http://…` 且进程未退 = 卡启动，提示看日志）。[§9][P0]

### 18.6 配置版本迁移（ConfigVersion）

PCL 的 `Configs.JavaConfigVersion` + 迁移日志：配置带版本号，升级走显式迁移路径。DSH 版：所有 schema 文件都有 `schema` 字段，配套**迁移器注册表**（vN→vN+1 显式函数 + 迁移日志写入 logs/）——坏包 PA009，旧版包走迁移，二者分开。[§3 所有 schema][P0]

### 18.7 后台元数据缓存

PCL 对资源索引做后台刷新 + 上次更新时间记录，主流程不等待。DSH 版：registry 版本列表/插件元数据按 TTL 缓存 `library/meta/`，列表页秒开，刷新走后台任务树。[§4 + §10][P2]

### 18.8 实例展示元数据

PCL 实例卡片：Name/Info/Logo/IsStar/分类。instance.json 已加 `display{info, logo, star, category}`（§3）；Logo 生成规则沿用插件图标规则（§6）。[P1]

### 18.9 空态文案

PCL 空态 = 标题 + 说明 + 行动按钮（"无可用版本"+ 下载按钮）。DSH 版：所有 list 类命令在空结果时输出 `emptyState{title, hint, action}` 字段，前端不自己编文案。[§13][P1]

### 18.10 多钥匙凭据

PCL 多账户管理（PageLink）。DSH 版：`library/credentials/` 支持**多套命名钥匙**，实例 `credentials.kind` 从"global|instance"扩为"命名字段"（`credentials.set: "work"`），导出剥除规则不变。[§11][P2]

### 18.11 启动器自更新

PCL 的 `patches/` 是一串增量 patch（2.12.x → 2.13.x），带更新通道。DSH 版：P3 前先做**通道 + 整包替换**（dev/stable，spec 已有 channel 概念）；增量补丁观察 PCL 收益后再定。[§13][P3]

### 18.12 不抄清单（与理由）

- 皮肤/主题工坊：spec §15 已排。
- 市场逛店/分类货架：一期不做；但下载中心的分类/详情/进度三件套是**二期插件市场的直接蓝图**（届时 format-sniff 表 + 元数据缓存 + 进度事件流全部复用）。
- 游戏内 UI、多 harness：spec 红线。

### 18.13 优化落期总览

| 期 | 新增优化 |
|---|---|
| P0 | 18.1 多源/任务树/加权进度 · 18.3 import · 18.5 crashed 态+退出策略+启动超时 · 18.6 配置迁移 |
| P1 | 18.2 崩溃分析器 · 18.4 格式嗅探 · 18.8 实例元数据 · 18.9 空态文案 |
| P2 | 18.7 后台缓存 · 18.10 多钥匙。**已落地** `library/meta/`、`library/credentials/<name>.yaml` |
| P3 | 18.11 自更新 · 进度页/大按钮/直启快捷方式。**已落地** 通道+整包替换到 `library/updates/`、货架读缓存 plugins.json、Tauri 进度条与启动按钮 |

## 19. 兼容性与编码矩阵（DSH 侧硬约束）

### 19.1 会话格式版本门（硬，不是 warning）

- jsonl **第一行** = 会话头 `{version, id, createdAt}`（`packages/core/session/src/index.ts:152`），`version` 是构建期常量 **`SESSION_FORMAT_VERSION = 0`**（`packages/core/session/src/types.ts:56`）。
- 读侧双门：jsonl 后端在解析任何事件行**之前** `refuseForeignFormatVersion`——版本不符直接 `SessionFormatUnsupportedError`，用户看到的是"upgrade the harness"而非"corrupt session log"（`format.ts:234-247`）；core 层再校验一次（`index.ts:101-102`）。
- **对启动器**：人手拷进来的 session，只有 `SESSION_FORMAT_VERSION` 相同才可能打开，这是 DSH 硬约束，不是启动器拦拷。
  - `version.json` 增 `sessionFormatVersion` 字段：verify 时从 `versions/<ver>/node_modules/@deepseek-ai/dsh-session/…` 提取该常量（构建常量，随版本 pin）。
  - 列出或打开该条时：读 jsonl 第一行（**文件默认 zstd 压缩，读头需 zstd 解码，F28**）比对钉版本常量 → 不符 **PA015**。不拦该实例 `run`。不改 jsonl。
  - 格式版本相同但 DSH 版本不同 → **PA102**（warning，"格式兼容、行为可能漂移"）。

### 19.2 projectKey 编码移植规范（最易翻车的函数）

DSH 实现 = **UTF-16 code unit 迭代**（`charCodeAt`，`format.ts:147-167`）。移植要点：

- **按 UTF-16 code unit 迭代**：非 BMP 字符（emoji 等）在 JS 里是代理对 → 转义成**两个** `~XXXX`（`😀` → `~D83D~DE00`）。Rust 用 `s.encode_utf16()` 逐单元，**禁止按 Unicode scalar 迭代**，否则所有 emoji 路径全部漂移。
- 规则原文：`/ \ :` → `-`（连续折叠成一个）；非 `~` 且 `[A-Za-z0-9._-]` 直写；其余（含 `~` 自身、空格、中文、代理对）→ `~XXXX`（大写十六进制补 4 位）；去前导 `-`；空 → `root`；包 `--slug--`；**`slice(0,251)` 按 code unit 截断**。
- **截断碰撞是真实的**：超长路径前缀相同 → slug 撞车 → 会话目录混淆。因此启动器**枚举一律按 session id 全根扫描定位**（`encodeSegment` 后的目录名），绝不假设 slug 唯一。发现同根下两个 slug 目录 realpath 相同 → **PA108**（warning，提示规范化工作区）。
- **cwd 不规范化**：core session 只校验 `isAbsolute`（`index.ts:114`），projectKey 对 header 里的**原样字符串**转义。Windows 大小写/尾分隔符差异 → 同一目录多 slug。打开时 DSH 自己用 `realpath` 比对工作区（`entity.ts:122-144`），不符 PA007。启动器不改 header.cwd。
- golden 测试集（Rust 侧钉死）：`E:\pack-agent → --E-pack-agent--`；中文 `项目` → `~9879~76EE`；emoji 代理对；全分隔符 → `--root--`；无 cwd → `_no-cwd`；251 截断样例。

### 19.3 语义化版本

- `versions/` 排序、`pin` 比较、范围解析一律 **semver**（`0.1.0-rc.6 < 0.1.0-rc.10`），禁止字符串比较。
- `engines.dsh` range 解析对齐 **node-semver**（DSH 生态是 npm）：prerelease 匹配规则必须与 node-semver 一致（range 含 prerelease 比较器才匹配 prerelease 版本）。Rust 侧选 `nodejs-semver` 并写对照测试（与 `semver` npm 包行为逐例比对）。
- `--version` 输出原样即版本字符串（F3），除 trim 外不做变换。

### 19.4 路径与文件系统

- 实例 id 消毒：`[a-z0-9]+(?:-[a-z0-9]+)*`（与 DSH profile 名限制同风格 F15）；显示名自由；引擎生成的文件名永不含 `<>:"|?*`、尾点、尾空格。
- Windows：路径比较一律 canonicalize 后比（大小写不敏感）。
- 长路径：launcher-root 建议浅路径 + 引擎 manifest `longPathAware`；projectKey 的 251 截断已把会话层压短，但实例路径仍可能超 MAX_PATH → doctor 检查并提示。

### 19.5 进程 I/O 编码

- **就绪信号按字节流扫描**（`dsh web: ` 是 ASCII 前缀），不受 Windows 控制台代码页影响；stdout/stderr 用管道字节捕获，不做"解码→重编码"。
- 日志文件 = 字节透传落盘（UTF-8 原样）；崩溃分析器读日志按 UTF-8 宽容解码（坏字节替换）。
- taskkill / SIGTERM 只传 pid，不涉及路径编码。

### 19.6 文本文件约定

- 一切 JSON/YAML/patch 文件 **UTF-8 无 BOM**；YAML 里 Windows 路径一律**单引号字符串**（反斜杠、盘符冒号）；JSON 序列化禁止 NaN/Infinity。
- 时间戳一律 ISO 8601 带偏移（存 UTC `Z`）；会话 jsonl 的 `createdAt` 是 epoch 毫秒（DSH 原生，读取时原样，不换算）。

### 19.7 DSH 版本兼容登记（compat 快照）

`version.json` 增加三个 verify 时从安装提取的快照字段：

```json
{
  "sessionFormatVersion": 0,
  "patchRowIds": ["session-persistence-jsonl", "session-telemetry-otel", "attachment-local", "credentials", "settings"],
  "profileTemplates": { "web": ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-web-app"], "headless": ["@deepseek-ai/dsh-base", "@deepseek-ai/dsh-headless"] }
}
```

用途：overlay 注入前用 `patchRowIds` 校验目标 id 存在（PA107 升级为用快照判，不再临时跑 `--dump-config`）；列出/打开 session 时用 `sessionFormatVersion` 判 PA015；实例创建/改钉用 `profileTemplates` 判断 `profile.name` 是否为随附模板。将来 DSH 改 projectKey 规则则加 `projectKeyVersion` 字段（现在恒 0，独立于 SESSION_FORMAT_VERSION）。

### 19.8 打包格式兼容

- pack 文档 schema（ccui-pack v0.2）与 pinst v1 走 §18.6 迁移器注册表；旧 Cursor 老包走 `dsh map`（已有，不变）。
- 导入嗅探（§18.4）只认**声明 schema 的头部**，无 schema 的未知包 → PA009 并提示"可能需 `map --from`"。

## 20. 注册表体系（引擎解释注册表，不内置种类）

### 20.1 契约

每个注册表一个文件、一个 schema、一个版本。统一信封：

```json
{
  "schema": "pack-agent.registry/v1",
  "registry": "task-kinds",
  "version": 1,
  "entries": [ { "id": "install-version", "…": "…" } ]
}
```

加载顺序：

```
内建 agent-pack-dsh/modpack/registries/<name>.json
  → <launcher-root>/library/registries/<name>.json（按条目 id 合并）
  → 实例只引用 id，不带表正文
```

合成结果 = `Map<id, 绝对路径>`。查表 O(1)。读正文时比对 mtime，变了只重读该文件。`fs.watch` 更新映射。坏文件 PA017，映射不切到坏路径。正在执行的任务用开工时的映射快照；新任务用新映射。已经在跑的 DSH 进程不读这套表，要吃新 `profiles` 就绪规则就停再 `run`。

规则：

- **条目必须有稳定 id**。跑起来引用合成表没有的 id → **PA018**。自建条目允许。自定义 `--profile` 名可以不在 `profiles` 表里。
- **多余字段、缺必填 → PA017**。引擎不补默认值。默认只写在内建条目正文。
- **`requires`**：加载和热更新做闭包校验，缺 PA018；有环 PA017，新表不用。`import` 链顺序以条目正文为准，`requires` 不重排。
- **覆盖**：同 id 用户层覆盖 → **PA109** warning，按新正文，能继续。新 id 追加。用户层可标 `disabled: true`，不能从映射里删掉内建 id。
- 诊断主句英文。
- **原语**：`task-kinds` 每步只能引用引擎已有原语。写了没有的 → PA018。引擎源码不出现具体种类名；`"web"` / `"install-version"` 只出现在内建 JSON 和测试里。
- **自己检查**：加载/热更新跑 schema、闭包、环。`launcher doctor` 再全跑，并检查原语能否解析、`import` 链引用是否都在。条目里不准写检查脚本。
- **第一批表**：`format-sniff`、`task-kinds`、`pa-codes`、`profiles`、`crash-rules`、`faq`、`migrate`、`compat-snapshot-spec`。
- **测试**：每张表拒多余字段、缺字段、未知 id 运行时 PA018、覆盖 PA109。假 `bin.js` 演 `plugin add` / `--dump-config`。真 npm 另开，默认不跑。

### 20.2 注册表清单（v1）

| 注册表 | 文件 | 驱动 | 条目示例（内建） | 可扩展 |
|---|---|---|---|---|
| `pa-codes` | `modpack/registries/pa-codes.json` | §14 诊断码表 | PA001…、PA017/PA018、PA109 | 是 |
| `profiles` | `library/registries/profiles.json` | §3/§9：`--profile` 名 → 随附模板或自定义、就绪信号（F20 `dsh web:` / 退出码）、`--port` 是否适用 | `web`、`headless`、自定义名 | 是 |
| `task-kinds` | `library/registries/task-kinds.json` | §10 任务类型：子任务树、权重计划、锁粒度（全局/实例/版本）、取消检查点、持久化要求；`import` 是组合序列（§18.3） | install-version、verify-version、install-plugin、project-pack、backup、clone、export、import、launch-headless | 是 |
| `format-sniff` | `modpack/registries/format-sniff.json` | 导入嗅探：`id` + `match` + `kind`（`pack`\|`pinst`）+ `handler` 原语 | ccui-pack、pack-zip、pinst-v1 | 是 |
| `crash-rules` | `library/registries/crash-rules.json` | §18.2 崩溃分析：日志行模式 → 分类 → 引用 FAQ 条目 | 非零退出码、端口占用、插件加载失败、凭据缺失 | 是 |
| `faq` | `library/faq/*.md` + `faq-index.json` | 崩溃分析/诊断的解法库；外置可更新 | — | 是 |
| `migrate` | `crates/pack-launcher/src/migrate/`（函数注册表 + 声明表） | §18.6 schema 迁移器：`{fromSchema, toSchema, 迁移函数 id}`；声明表数据驱动，函数按 id 注册 | launcher/v1、instance/v1、version/v1 | 引擎扩展点（函数需编译进 lib；声明可用户补） |
| `compat-snapshot-spec` | `library/registries/compat-snapshot-spec.json` | §19.7 每个 DSH 版本要快照哪些事实（字段名 + 从安装的提取方式），`version.json` 只存快照值，**快什么由注册表决定** | sessionFormatVersion、patchRowIds、profileTemplates | 是（DSH 新事实不重编译） |
| `projectkey-spec` | 注册表 + 引擎函数 | §19.2 编码规则：规则版本号、golden 向量集（引擎函数按规则版本实现，向量由注册表供给测试） | v0（UTF-16 code unit 规则）+ 向量 | 版本化（新规则加 v1，旧版本仍可解释旧目录） |
| `id-rules` | `library/registries/id-rules.json` | §19.4 命名/消毒规则 per 实体（instance/version/profile/pack/session） | 各实体正则 + 生成器说明 | 是 |
| `unit-map` | `agent-pack-dsh/modpack/registry.yaml`（已有，引用不复制） | 整合包单元 → DSH 插件映射（已有资产，启动器不重造） | skill/mcp/hooks/… 11 种 | 是 |
| 覆盖层 | `library/registries/` 用户层文件 | 用户/发行版对上述表的覆盖 | — | 是 |

### 20.3 与 DSH 分层的对称性

启动器的三层（内建 → 用户注册表层 → 实例引用）与 DSH 的 patch 组合顺序（bundle → profile patch → home patch → `--patch`）同构；实例的"引用注册表条目 id"对应 DSH 的"profile 引用 bundle 包名"。这保证：**换 DSH 版本 = 换快照数据；加行为种类 = 加注册表条目；两者都不需要发新版启动器**。

### 20.4 本设计各章的硬编码点回改索引

| 章 | 原硬编码 | 改为 |
|---|---|---|
| §3 instance.json | `profile.name` = `--profile` | 引用 `profiles` 注册表或任意自定义名（F15 限制） |
| §9 就绪检测 | 固定等 `dsh web:` 行、固定 30s | 就绪模式由 `profiles` 条目声明（字节前缀模式/退出码模式），超时值进 launcher.json 默认配置 |
| §10 任务树 | 固定任务清单与权重 | `task-kinds` 注册表（权重计划/锁粒度/检查点） |
| §13 import | 固定命令序列 | `task-kinds` 的 `import` 条目 |
| §14 诊断 | 表写在文档里 | `pa-codes` 注册表为唯一真值，文档表是导出视图 |
| §18.2 崩溃分析 | 固定分类 | `crash-rules` + `faq` 注册表 |
| §18.4 嗅探 | 固定格式清单 | `format-sniff` 注册表 |
| §18.6 迁移 | 固定 v1 迁移 | `migrate` 声明表 + 函数注册表 |
| §19.2 编码 | 硬编码移植规范 | 规范保留（函数必须有实现），但规则版本 + golden 向量进 `projectkey-spec` |
| §19.4 命名 | 固定正则 | `id-rules` 注册表 |
| §19.7 快照 | 固定三字段 | `compat-snapshot-spec` 注册表决定快什么 |

### 20.5 对应诊断码

| 码 | 级 | 触发 |
|---|---|---|
| PA017 | error | 注册表加载/schema 校验/条目引用闭包校验失败 |
| PA018 | error | 跑起来引用了合成表里没有的 id |
| PA109 | warning | 用户层覆盖了内建注册表条目（行为偏离内建） |

## 21. 与现有 pack-agent DSH 插件的整合

启动器不是替代品：**插件 = 实例内管理器，启动器 = 实例外编排**，二者咬合在实例工作区的 `.agent-pack` 上。

### 21.1 现状盘点（已验证）

`agent-pack-dsh/plugin/`（npm 包 `@sakikotgw/pack-agent-dsh` 0.4.3）作为 DSH bundle 挂载（`dsh.bundle.patch` → `cordis.patch.yml`），两条 insert：

1. 管理器本体 `@sakikotgw/pack-agent-dsh`（inject: tools, skills, commands）→ 7 个工具（detect/compile/project/map/search/allow/deny/list）、6 个斜杠命令、`pack-agent-catalog` SkillProvider（rank 150，只回放行集技能）；
2. 专属 skill-filesystem 实例（providerName: pack-agent-dsh，只服务 `./skills`）→ 会话内可见 `pack-agent-dsh` 技能（操作手册）。

两种安装源：npm 包（发布态）；仓库根 `cordis.root.yml`（开发态，insert 根包 `exports["./dsh"]`）。底层资产：`agent-pack-dsh/modpack/` 投影编译器 + `registry.yaml` unit-map + `agent-pack-dsh/pack-index` SQLite（含命名白名单 set-save/set-load/set-list）——**这些全部被启动器复用，不复刻**（§1 原则 4）。

### 21.2 分工与唯一共享状态

| 轴 | 实例内（插件） | 实例外（启动器） |
|---|---|---|
| 投影/检索 | 会话内 `packagent_project/search` | `import` 任务调用同一 `agent-pack-dsh/modpack` 函数 |
| 白名单 | 会话内 `allow/deny/set-load` | 实例创建/`import` 时 `set-save` 落命名集 |
| 目录 | 会话内 `list` | `instance info` 同读 |

**唯一共享状态 = 实例工作区的 `.agent-pack/catalog.sqlite` + 命名白名单**；两端都不缓存镜像，catalog 是唯一真值（与 §6"启用集真值唯一"同原则）。`instance.json` 的 `packs.allowSet` 只是"该实例默认加载哪个命名集"的指针，不复制白名单内容。

### 21.3 实例 bootstrap 增补（§5 步骤更新）

创建实例时追加：

4. **装管理器**：`dsh plugin --profile <name> add`，spec 来自 `task-kinds` 的 `install-manager`。冒烟 = `--dump-config` 出现 `- id: pack-agent`，失败 PA019。

spec §5 的"停用 pack-agent 管理器前警告"落实为：`plugin disable <id> pack-agent` 时引擎检查该插件是管理器 → PA110（warning，提示"停用后实例内看不到投影整合包"）。

### 21.4 既有插件的待办清单（随本设计立项，独立任务）

| # | 项 | 说明 |
|---|---|---|
| 1 | 声明 `engines.dsh` | 生态示范（§6 约定），同时消除自家插件的 PA101 |
| 2 | 修 `compile.ts` origin.json 嵌套 bug | **已修**（`compile.ts:65-66,81` + `agent-pack-dsh/modpack/test.ts:199-223`）：含 `.agent-pack-origin.json` 的路径不再当 skill |
| 3 | `search` 口径 | 明确"search = 全索引检索，snapshot = 会话可见集"，`--enabled` 过滤选项补齐 |
| 4 | `pack-agent-dsh` 技能增补 launcher 段 | 加入 `packagent dsh launcher …`、`import` 入口与「启动器装实例」的用法 |
| 5 | 会话不按 profile 拆根 | catalog 属工作区。隔离 = 每实例一份 `DSH_HOME` + 工作区。人手拷 session 自负。 |

### 21.5 发布与版本关系

- 启动器代码进 `agent-pack-dsh/modpack/launcher.ts`，跟投影层同一包、同一版本线，不另发 bin。
- **一个入口**：`packagent` 贯穿三维——`packagent pack|export|install …`（①）、`packagent dsh project|allow|set-* …`（②）、`packagent dsh launcher …`（③）。
- 根包 `exports["./dsh"]`、`dsh.bundle.patch`、`agent-pack-dsh/plugin/lib` 关系不变。
- 开放项（P3）：会话内是否要一个 `packagent_launcher` 状态工具（ps/logs 只读）——按"插件不扩职责"原则暂不做，等实例内真实需求出现再以注册表条目形式加。

## 22. DSH 插件生态基线（2026-08-15 实测）

### 22.1 生态事实（npm registry + 源码验证）

| # | 事实 | 证据 |
|---|---|---|
| E1 | `@deepseek-ai/dsh` 已在 npm 发布，版本序列：`0.0.1-rc.1/2/5`、`0.1.0-rc.2/3/6/7`（**无 rc.4/rc.5，最新 0.1.0-rc.7**）——版本库"从 registry 拉"已验证可行；**版本清单必须来自 registry 实测，不能按源码仓库假设** | `npm view @deepseek-ai/dsh versions` |
| E2 | in-box 三件套都独立发布，且 **latest 全是旧的 0.0.1-rc.1，next 才是 0.1.0-rc.7**：`dsh-base` / `dsh-web-app` / `dsh-headless`。禁止裸装其中任何一个 | `npm view … dist-tags`（2026-08-15 夜间复测） |
| E3 | registry.yaml 映射的 10 个核心插件包全部独立发布、版本线各自独立：skill-filesystem 0.0.1-rc.3、mcp-client 0.0.1-rc.1、hooks-claude-code 0.0.1-rc.5、persona/presets/subagent/session/settings/commands 0.0.1-rc.1、**agent-instructions 已到 0.1.0-rc.7** | `npm view` ×10 |
| E4 | 插件作者约定 = 声明 `"dsh": {"bundle": {"patch": "./cordis.patch.yml"}}`，patch 里 insert 行（id+name+config+inject）；注入点：tools/skills/commands + cordis 服务；自家 `@sakikotgw/pack-agent-dsh` 0.4.3 / `@sakikotgw/pack-agent` 0.4.2 已按此发布 | `agent-pack-dsh/plugin/package.json`、monorepo `examples/mcp-memory/*.cordis.yml` |
| E5 | 分发通道全集：npm registry / 本地路径 / git / tarball（`dsh plugin` 锚定 + pnpm 转发；git 依赖受 pnpm ≥10 prepare/allowBuilds 约束） | `apps/cli/src/plugin.ts:104-155` |
| E6 | 官方**无** `engines.dsh`、无图标约定、无官方市场索引 | 源码 grep + 官方 publish 教程 |
| E7 | 官方发现通道 = GitHub topic [`dsh-plugin`](https://github.com/topics/dsh-plugin) + 文档站；安装入口是 `npx @deepseek-ai/dsh web`，没有官方启动器 | README + https://deepseek.com/harness |
| E8 | 社区目录已是事实市场：[`awesome-dsh-plugin.com/plugins.json`](https://awesome-dsh-plugin.com/plugins.json) **365** 条（2026-08-14），分类 ui/theme/session/memory/tools/skill/workflow/notify/model/dev/fun；字段 `name/owner/url/npm/stars/install/description{en,zh}` | 当场 GET，`count: 365` |
| E9 | 站内市场 [`dshmarket`](https://www.npmjs.com/package/dshmarket)：`dsh plugin --profile web add dshmarket`，读 E8 的 JSON；只装目录里的源；主题可热切；自称多数插件刷新页面即可，与核心「改 bundles 要重启」不是同一条路 | https://github.com/dsh-market/dsh-market |
| E10 | Web 半另有官方字段 `dsh.client`（`platform: web`、`inject`、`immediately`）+ `exports["./client"]`。官方原文：**plugin-set changes take effect on restart** | `docs/subsystems/client-modules.md` |
| E11 | 抽查第三方 npm：`@sakikotgw/pack-agent-dsh` / `@liustack/modsearch` / `@liustack/pptfast` 只有 `dsh.bundle.patch`；`@liustack/modlens@3.16.4` 另有 `dsh.client`。全部无 `engines.dsh`、无 `dsh.icon` | `npm view` |
| E12 | 目录把本仓写成 `dsh plugin --profile web add @sakikotgw/pack-agent`（根包 0.4.2），不是 `@sakikotgw/pack-agent-dsh`（0.4.3） | plugins.json `pack-agent` 条 |
| E13 | 启动器赛道 48 小时内挤满 WebView2/WKWebView 套壳（Ruler4396、melody-launcher、dsh_desktop…）。做的是「一键开 web + 托盘」，不是版本库/隔离实例 | GitHub search `dsh launcher` |
| E14 | 出厂无 TUI 已被社区补：`ccch1mneyyy/dsh-TUI`、`huiliyi37/dsh-tianshu-tui`、`openma-ai/deepseek-harness-tui` | awesome 列表 |
| E15 | 脚手架 `create-dsh-plugin@0.1.1`：模板 + **next-tag 钉版本** + `--verify`。官方 publish 教程只教 `dsh.bundle`，不教市场元数据 | npm + 官方 publish 教程（文档站 2026-08-15 夜间 503，见 E21） |
| E16 | 2026-08-15 夜间复测：`plugins.json` 仍 **365** 条，`updated: 2026-08-14`，分类 tools 94 / ui 84 / dev 50 / workflow 27 / session 26 / notify 24 / memory 20 / fun 20 / model 8 / theme 6 / skill 6。目录 **没有** launcher 条目 | 当场 GET |
| E17 | `@deepseek-ai/dsh` 版本轴未动：仍无 rc.4/rc.5，`latest=next=0.1.0-rc.7`（发布 2026-08-13）。`dsh@0.1.0-rc.6` 对 in-box 依赖全是 `^0.1.0-rc.6`，锁闭包仍是硬条件 | `npm view` |
| E18 | `dshmarket@1.2.2`（2026-08-14） | `npm view dshmarket version` |
| E19 | 套壳继续堆，仍无人做「多 npm 版本 + 每实例 DSH_HOME」。最接近：`rirko/dsh-melody-launcher`（插件/本体管理，整合包未完成）；`omdsh-dev/omdsh` 只有 component-set schema。`starline-dsh-desktop` 支持多开端口，共享用户配置 | GitHub README 抽查 ≥8 个 |
| E20 | 目录安装名仍是根包 `@sakikotgw/pack-agent`（0.4.2）。`-dsh` 包 0.4.3 的 row `id` 同为 `pack-agent`，`name` 不同。PA019 匹配 **`- id: pack-agent`**，不要只 grep 包名 | plugins.json + `agent-pack-dsh/plugin/cordis.patch.yml:3-5` + `cordis.root.yml:3-5` |
| E21 | 官方文档站 `deepseek-harness.github.io` 2026-08-15 夜间 **503**。宣传页 https://deepseek.com/harness 可达，入口仍是 `npx @deepseek-ai/dsh web` | WebFetch |

### 22.2 对设计的直接推论

1. **版本库**（§4）：版本清单 = registry `versions` + dist-tag（`latest`/`next` → launcher 的 stable/dev 通道映射）；安装 = `pnpm add @deepseek-ai/dsh@<ver>`，闭包自动带 in-box bundle 的对应版本（E2）。
2. **插件兼容是双版本轴**（§6/§19.3）：插件自己的版本线独立于 DSH（E3）。未声明 → PA101 warning，不拦。已声明且不满足 → PA002。`update` 后按 F7 的"已装状态"重算 bundles，E3 的独立版本线意味着**同一 profile 内各插件版本无联动约束**（除 peer 依赖，pnpm 管）。
3. **元数据/图标约定由 launcher 定义**（§6）：生态暂无标准（E6），`engines.dsh` + `dsh.icon|icon` 就是我们引入的约定，自家插件带头（§21.4 #1）。
4. **市场二期蓝图**（§18.12）：E1–E5 说明"按 npm 检索 + dsh.bundle 判据 + 元数据缓存"足以做插件市场第一期——不需要上游配合；分类/详情/进度三件套直接复用 §18 资产。
5. **smoke 口径**（PA019 落实）：`--dump-config` 出现 **`- id: pack-agent`**（F2 免 boot）。根包 insert `name: '@sakikotgw/pack-agent/dsh'`，`-dsh` 包 insert `name: '@sakikotgw/pack-agent-dsh'`，id 相同。真 boot 冒烟留给 P1。
6. **dist-tag（2026-08-15 夜间复测）**：`@deepseek-ai/dsh` 的 `latest` 与 `next` 都是 `0.1.0-rc.7`。`dsh-base` / `dsh-web-app` / `dsh-headless` 的 `latest` 全是 `0.0.1-rc.1`。通道映射必须按包名分别读 tag，禁止假设「latest = 新」，禁止裸装 in-box。
7. **市场不要自造目录**：浏览/搜索直接消费 E8 的 `plugins.json`。安装仍是对该实例 `dsh plugin add`。货架读 `library/meta/plugins.json` 缓存。
8. **插件身份有两半**：Host = `dsh.bundle.patch`（进 profile 要重启进程）；Web = `dsh.client`（进树之后刷新页面能看到 client 半）。启动器 list 必须两栏都读，图标优先看 client 包资源，不要发明 `dsh.icon` 当生态已有标准。
9. **本仓在目录里的安装名是根包** `@sakikotgw/pack-agent`。启动器默认装管理器时应对齐目录，或同时接受 `-dsh` 包，避免和 dshmarket 各装各的。

## 23. 生态续研（2026-08-15）

### 23.1 官方在卖什么

- 产品：[DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)，2026-08-13 开源，developer preview，README 写明会破兼容。
- 用户入口：`npx @deepseek-ai/dsh web` → `127.0.0.1:3080`，home 默认 `~/.dsh`。
- 文档：[https://deepseek-harness.github.io/deepseek-harness/](https://deepseek-harness.github.io/deepseek-harness/)；宣传页 [https://deepseek.com/harness](https://deepseek.com/harness)。
- 扩展教程只定义 bundle/profile 两层，发现靠 topic，不靠 npm 搜索（`npm search dsh` 噪声极大，会搜到 2016 年的同名 shell）。

### 23.2 社区已经长出的层

| 层 | 谁 | 对我们的含义 |
|---|---|---|
| 目录 | awesome-dsh-plugin 365 条 + `plugins.json` | 启动器浏览的数据源，不要再爬 GitHub topic |
| 站内市场 | `dshmarket`、dsh-plugin-hub、plugin-workshop、plugin-center | 实例内逛店已有人做；我们做实例外编排 |
| 检索 | `dsh-find-plugin`、`dsh-recommend` | agent 内找插件，不是启动器职责 |
| 脚手架 | `create-dsh-plugin` | 作者工具；钉的是 `next` tag，和 dsh-base latest 陷阱同构 |
| TUI | 至少 3 个第三方 | 启动器 `kind=profile` 即可，不必出厂 TUI |
| 套壳启动器 | 十余个 WebView2/托盘 | 和 PCL 式版本库不是一类产品；不要做成第 N 个套壳 |
| 视觉/工具 | modlens、modsearch、pptfast、better-sidebar、皮肤站 | 实例插件清单的真实货 |

`dsh-external/*` 在另一份 awesome 里大量出现，部分仓可能要 org 权限，启动器不要把这组 URL 当公开可装源。

### 23.3 和本启动器的边界

别人已经做完：开窗口、托盘、主题热切、站内货架。

还没人按 PCL 做、且我们要做的：多发行号并存、每实例独立 `DSH_HOME`、投影整合包不进 plugin 栈。人手拷 session 自负。

P0 仍是：装两个 npm 版本 + 两个隔离实例 + 同时 `run web`。生态货架留给实例内的 `dshmarket` 或 P3 读 `plugins.json`。

### 23.4 2026-08-15 夜间续研（源码 + 复测）

会改实现的新事实：

1. **官方「四种模式」不是启动器轴**（F29）。标准 / PTC / 极简 / 创造 = 实例跑起来之后选的 agent-preset。启动器 shell 仍是 `web` / `headless` / 自建 profile。
2. **换实例继续聊有两层数据**（F30）：jsonl 在 `sessions/`；侧栏分组在 `storages/workspace.json`。人手拷 jsonl 不会带上分组。克隆 home 会带走旧注册表。打开失败只报错，不收拾。
3. **PA003 必须自建**（F33）。DSH 没有跨进程文件锁。
4. **实例创建不要手写 profile**（F32）。第一次 `run` / `dsh plugin add` 会 `initProfile`。P0 `createInstance` 已遵守。
5. **projectKey 在 TS 移植**（§8 已改）。架构里「Rust 侧移植 + ruzstd」是上一轮错类残留，作废。
6. **套壳红海未变**（E16–E19）。365 插件、rc.6 未动。melody-launcher / omdsh 不是版本库产品。
7. **文档站 503**（E21）。实现期以本机源码 + npm + https://deepseek.com/harness 为准。
8. **管理器两包同一 row id**（E20）。发布默认仍建议 `@sakikotgw/pack-agent-dsh@<pin>`（体积小）；目录/dshmarket 写的是根包。PA019 认 id，两包都能过。

P1 已补：`projectKey` golden、读 header 第一帧、`owner.json` 写锁、`--dump-config` 认 `- id: pack-agent`、命令面 clone/pin/plugin/session/job/crash/emptyState。P0 范围不动。
