# agent-pack for DSH：整合包研究

date: 2026-08-19
status: research
product: **agent-pack for DSH**
对照：PCL 2.13.1.1 `ModModpack.vb`（`E:\PCL\src\Plain Craft Launcher 2\Modules\Minecraft\ModModpack.vb`）

产品定义见 [PRODUCT.md](PRODUCT.md)。本文件只钉装包链。不谈社区套壳，不谈多 harness 语言优先。

## 0. 产品

人拿到一份 `.pack.zip`，丢进启动器，得到一个隔离实例，点一下就能在 DSH 里用这包的 skill / MCP / 规则 / 指令。

| MC | 本产品 |
|---|---|
| 游戏版本 1.20.1 | DSH 发行号，如 `0.1.0-rc.7` |
| Forge / Fabric | DeepSeek Harness |
| 整合包 zip | `.pack.zip` / `.pack.json` |
| `mods/*.jar` | 投影目录 `mods/<id>/`（skill、MCP、rule、command、hook） |
| Forge 模组（进加载器） | `pack.dsh.plugins[]` → 该实例 `dsh plugin add` |
| `overrides/` | 拷进该实例工作区的文件 |
| `versions\<名>\` + 版本隔离 | 启动器实例：独立 `DSH_HOME` + 工作区 |
| PCL 大按钮「启动游戏」 | `packagent dsh launcher run <id>` |

pack-agent 管理器 = 内容加载器。每个整合包本身禁止 `dsh plugin add`。

`docs/PACK_SPEC.md` §0 把「游戏版本」写成 Claude/Codex 等 harness。那是旧跨壳故事。本产品里游戏版本 = DSH 发行号。

## 1. PCL 装包链（源码，不是比喻）

`ModpackInstall`（`ModModpack.vb:27-106`）再 `InstallPackCurseForge`（`:157`）：

1. 嗅探 zip 根文件：`manifest.json` / `mmc-pack.json` / `modrinth.index.json` / `modpack.json` / `mcbbs.packmeta`。
2. **硬门**：`minecraft.version` 必须有（`:167`）。没有就不装。
3. 读 `minecraft.modLoaders[]`（`forge-` / `fabric-` / `neoforge-`）（`:181-198`）。
4. 用包名建隔离目录 `versions\<实例名>\`。
5. 解压，把 `overrides`（及 `client-overrides`）拷进实例（`CopyOverrideDirectory`，`:121-144`）。
6. 按 `files[].projectID/fileID` 下载 jar 进 `mods/`。`required: false` 的会问（`:217-273`）。
7. 写 PCL ini，打开版本隔离（`VersionArgumentIndie`）。
8. 列表出现新实例，首页按钮变成「启动游戏」。

旁路：把 `modpack.zip` 丢到启动器旁边，启动时先装再删包。

## 2. 本仓已经有的

| 块 | 在哪 | 相当于 MC 的哪一步 |
|---|---|---|
| Pack IR | `src/types.ts` PackDoc | 整合包正文 |
| zip | `.pack.zip` 内 `pack.json` + `bundle.files` | 自带文件的包，像把 jar 打进 zip |
| 投影 | `agent-pack-dsh/modpack/compile.ts` → `.agent-pack/modpacks/<id>/mods/` | 把内容写成实例能看见的 mods |
| 白名单 | `project` / `allow` / `set-save` | 这个实例启用哪些包 |
| 管理器 | `agent-pack-dsh/plugin/` / `@sakikotgw/pack-agent-dsh` | 内容加载器，每实例装一次 |
| 版本库 + 隔离实例 + 双开 | `agent-pack-dsh/modpack/launcher.ts` P0 | 游戏版本目录 + 版本隔离 + 同时开两份 |

## 3. 装包链（已定）

对照 PCL 第 2–8 步。命令：`packagent dsh launcher import`。细则见 [launcher-design.md](launcher-design.md) §6–§7。

### 3.1 `dsh.version`

`PackDshLayer.version` 必填。精确号：npm 没有这号 → PA012。range：已发布列表里取满足 range 的最高号；一个都没有 → PA012。用 node-semver。

```jsonc
"dsh": {
  "version": "0.1.0-rc.7",
  "profile": "web",
  "plugins": [{ "spec": "@scope/pkg@1.2.0", "required": true }],
  "overrides": [{ "from": "overrides/AGENTS.md", "to": "AGENTS.md" }]
}
```

### 3.2 包 → 新实例

1. 嗅探 `.pack.zip` / `.pack.json` / `*.pinst.zip`
2. 读 `dsh.version`；没有 → PA012
3. `version install` 若未装
4. `instance create`：新 home、新工作区
5. 拷 overrides 进工作区
6. 对该工作区 `project` + `allow` + `set-save`
7. 该实例 `dsh plugin add` 管理器一次；PA019
8. 必选 `dsh.plugins[]` `add`；失败 PA014，实例留下，状态不是可 run。可选的列出，不拦。
9. 可 `run`

禁止接到已有 home。

### 3.3 overrides

拷进该实例工作区。不进 `DSH_HOME`，不进投影 `mods/`。禁止 `.credentials.yaml`。`from` 不在包里 → PA009。目标已存在：覆盖。

### 3.4 投影和组合包

| 种类 | 字段 | 怎么装 |
|---|---|---|
| 投影 | skill / MCP / rule / command / hook | 投影 + 白名单。包目录禁止 `dsh plugin add` |
| 组合包 | `pack.dsh.plugins[]` | 该实例 `dsh plugin --profile <name> add` |

`compile.ts` 仍可把组合包记进投影供检索。`import` 必须对该实例 profile 真 `add`。

### 3.5 人路径

```
foo.pack.zip  →  实例 foo  →  run
```

## 4. 嗅探

真值在 `format-sniff`。内建：`pack.json`（`ccui-pack/*` 或 `agent-pack-ir/*`）、`.pack.zip`、`*.pinst.zip`。匹配不上 PA009。不实现 CurseForge / MMC / mrpack。

## 5. 盘上现状

设计已钉，口径只认 [PRODUCT.md](PRODUCT.md) 与 [launcher-design.md](launcher-design.md)。本文件不再另写一套「要补」。

代码还没有：无。P2 后台元数据缓存、多套命名钥匙、pnpm store 目录、P3 通道整包替换、货架、进度条、快捷方式、Tauri 大按钮已落地。

P0 + import 已有：两版本目录、两空实例、双 `run`、rustc 诊断、`profile.name`、注册表加载器、`launcher import` 查表（嗅探 → 版本 → 实例 → overrides → project+allow → 管理器 → plugins[]）。必选 `add` 失败留下 `import-failed`，stderr 英文提醒还在盘上以及怎么删。

## 6. 已核对的硬约束（装包时必须守）

- 投影目录不 `dsh plugin add`。
- 每实例独立 `DSH_HOME`。两实例禁止共用。人手拷 session 不拦；打开失败只报 error。
- `DSH_*` 只走 spawn env。
- 管理器每实例装一次；PA019 认 `--dump-config` 的 `- id: pack-agent`。
- 包声明的 DSH 版本必须能在 npm 装到；禁止按源码树假设 `0.1.0-rc.5`。
- in-box 三件套禁止裸装（`latest` 仍是 `0.0.1-rc.1`）。
- `import` 只建新 home，不接到已有实例。失败留下 `import-failed`，stderr 英文提醒。
