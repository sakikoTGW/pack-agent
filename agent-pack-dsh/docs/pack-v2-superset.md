# PAD pack v2：超集 + PackForge 双向兼容（设计稿）

- 状态：**设计稿（M1/M3 适配器已落地并验证；见 §11）**｜2026-11-02
- 目标：PAD 的 pack 变成 PackForge 规格的**严格超集**（补上 `type/dshhome`、`dshVersions`、`launchers`、`files[]`、精确坐标、`home/`、多 profile/preset/skill/指令、市场与 registry），同时**双向兼容** `.dspack`（导入 + 导出）。
- 关联：[modpack.md](modpack.md)、[launcher-architecture.md](launcher-architecture.md) §20 注册表、[launcher-top30-2026-11.md](launcher-top30-2026-11.md)（PackForge 现状与采纳率）；规范原文：`.tmp/research/packforge/{manifest-v5,pack-v3,index,launcher-registry}.md`
- 现状前提：PAD 的 pack 是「投影 + 白名单 + 工作区 overrides + 便携/单文件」；PackForge 是「profile/home 文件覆盖 + 插件栈（pnpm/reconcile）+ 市场/registry」。**两者是不同层的超集，本稿把它们合并。**

---

## 0. 一句话

**native 引擎继续做投影（D13 不变）；新增 compat 引擎执行 PackForge 安装；两种引擎共用一个 pack 模型（v2）。导入 `.dspack` 默认走 compat，导出 `.dspack` 时按规范生成；PAD 独有能力（投影/白名单/pack-runtime/便携/抓包）作为 `pad` 命名空间扩展，别家忽略即现状。**

---

## 1. 我们要补的东西（对照 PackForge 规范）

| # | 缺口 | 补法 | 优先级 |
|---|---|---|---|
| 1 | `type`：只有单 profile | pack v2 加 `type: "profile" | "dshhome"`，默认 profile | P0 |
| 2 | `dshVersions`（实测兼容枚举） | 加 `dsh.versions[]`；导入决策 `已装 ∩ 集合`，优先 `dsh.version`、否则集合最新 | P0 |
| 3 | `launchers` 兼容声明 + canonical ID | 加 `launchers` 字段；PAD 去 registry PR 认领 ID（建议 `pad`），并自报版本（`pad cli version --json`） | P0 |
| 4 | `files[]`（path+sha256+size+urls） | 加 `files[]`；下载器复用 DownloadDock，sha256/size 校验，失败整体回滚 | P1 |
| 5 | 精确依赖坐标（npm exact / git sha） | pack v2 的 `deps` 只存精确坐标；spec 字符串在**打包时解析并锁死**（从 pnpm-lock/已装版本取） | P0 |
| 6 | `home/`（home 级 skills/presets/AGENTS.md） | zip 支持 `home/` 目录；导入落 `$DSH_HOME` 根 | P1 |
| 7 | `dshhome`：多 profile + preset/skill/指令 | pack v2 加 `profiles` / `defaultProfile` / `presets` / `skills` / `instructions`；等于「工作区快照」的落地 | P2 |
| 8 | 市场 index v2 + publishing 规范 | 市场层接 `dsh-pack-market/index.json`（packs/ 懒加载），发布遵循其 Release/`dsh-pack` 规范 | P1 |
| 9 | 安全过滤清单（home 级） | 导出/导入沿用 PackForge 排除表 + PAD 现有剥凭据规则 | P0 |
| 10 | PAD 独有（投影/白名单/optional/pack-runtime/便携/工作区 overrides/抓包） | 保留，放 `pad` 命名空间；导出 `.dspack` 时降级或 sidecar | — |

---

## 2. pack v2 schema（超集，示意）

```jsonc
{
  "schema": "pad-pack/v2",              // 新版本；读取兼容 v1
  "name": "my-pack", "version": "1.0.0",
  "displayName": { "zh-CN": "…", "en-US": "…" },
  "description": { "zh-CN": "…" },
  "author": "…", "icon": "assets/icon.png", "license": "MIT",

  "type": "profile",                    // profile | dshhome（默认 profile）

  "dsh": {
    "version": "0.2.1-rc.2",            // 精确
    "versions": ["0.2.1-rc.2","0.2.2-alpha.2"], // 实测兼容枚举（PackForge dshVersions）
    "profile": "web",                   // profile 形态
    "defaultProfile": "web",            // dshhome 形态
    "profiles": {                        // dshhome 形态
      "web": { "bundles": ["@deepseek-ai/dsh-base","@deepseek-ai/dsh-web-app"], "deps": {}, "patch": "patch/web.yml" }
    },
    "presets":  [{ "name": "coding", "path": ".agent-presets/coding" }],
    "skills":   [{ "path": "skills/whale-writer" }],
    "instructions": "AGENTS.md",
    "deps": {                            // 精确坐标：语义直接对接 PackForge dependencies
      "dsh-pet": "0.2.0",
      "github:DViridescent/dafy-whale-theme": "99e8c57"
    },
    "files": [ { "path": "assets/big.bin", "sha256": "…", "size": 123, "urls": ["https://…"] } ]
  },

  "launchers": ["hdsl","pad"],           // PackForge 兼容声明（简式/全式同规范）

  "pad": {                               // PAD 扩展，别家忽略
    "projection": [ { "kind": "skill|mcp|rule|command|hook", "source": "…", "target": "…" } ],
    "whitelist": { "default": "deny|allow", "entries": [] },   // 每实例白名单（安装后写 .pad/packmeta.json）
    "overrides": [ { "from": "overrides/x", "to": "AGENTS.md" } ], // 到工作区（PAD 语义）
    "portable": { "packRuntime": true },
    "capture": { "fidelity": "L1|L2|L3|L4" }
  }
}
```

容器：继续 `.pack.zip`（ZIP + `pack.json`）。导入 `.dspack` 时先转换成这个内部模型；导出 `.dspack` 时再投影成 `dspack.json` + `manifest.json`（v5）+ 目录布局。

---

## 3. 两套安装引擎（关键决策）

| 引擎 | 语义 | 用于 | 状态 |
|---|---|---|---|
| **native** | 投影到工作区 `.agent-pack/modpacks/<id>/` + 白名单 + 工作区 overrides | PAD 自己的 pack（D13 不变） | 已有 |
| **compat** | 建新实例 → profile/home 文件覆盖 → `deps` 安装（`dsh plugin add` / pnpm）→ reconcile → `files[]` 校验 | 导入 `.dspack`（profile/dshhome） | **新增** |

规则：

1. 导入 `.dspack` 默认 compat；UI/CLI 显示「以 PackForge 方式安装」。
2. 导入 PAD pack 默认 native；`.dspack` 若显式要求可转换。
3. 两引擎共享：实例创建、版本决策、凭据剥离、下载/校验、诊断码。
4. **不混用**：一个包一次安装只走一个引擎；转换需用户确认，并给出语义损失清单。

---

## 4. 双向转换规则

### 4.1 `.dspack` → PAD（导入）

| PackForge | PAD 内部 | 备注 |
|---|---|---|
| `dspack.json` v3 + `manifest.json` v5 | 嗅探前置 | ZIP 首字节 + `format/version` 校验 |
| `type:"profile"` | `type:"profile"` | 建实例 → 落 overrides → deps 安装 → home/ 落 home |
| `type:"dshhome"` | `type:"dshhome"` | 多 profile + preset/skill/指令；`web/headless` 模板排除 |
| `dshVersion` / `dshVersions` | `dsh.version/versions` | 交集决策；无交集提示装 `dshVersion` |
| `bundles`/`dependencies`/`patch` | `dsh.profiles[name]` | 顺序保留；依赖解析为精确坐标 |
| `overrides/`、`home/` | 落 profile 根 / home 根（compat） | PAD 工作区 overrides 不参与 |
| `files[]` | 下载+sha256+size 校验；失败回滚 | 复用 DownloadDock |
| `launchers` | 读 registry → 警告放行 + 日志留痕 | 不硬拒 |
| 未识别字段 | 存 `pad.imported.extra` | 不丢数据，导出时可还原 |

### 4.2 PAD → `.dspack`（导出）

- **profile 型**：`dsh.profiles[profile]` + `dsh.deps`（精确）+ `patch` + `home/`（skills/presets/AGENTS.md）。
- **投影内容**（MCP/rules/commands/hooks）：
  - 默认「文件化」到 `home/skills`、`overrides/`（能互通，语义降级，导出时警告）；
  - 可选「sidecar」：写进 `pad` 命名空间（别家忽略，但 PAD 往返无损）。
  - 由用户选；默认文件化 + 警告。
- **白名单/pack-runtime/portable/capture**：不导出（或 sidecar），`.dspack` 里不存在。
- 导出前必须先「解析锁死」依赖：从 pnpm-lock / 已装版本生成精确坐标；解析失败 → 阻断导出并报 PA 码。

---

## 5. 版本决策与 launcher 语义

1. **版本**：`dsh.versions ∩ 本机已装` → 命中多个优先 `dsh.version`、其次集合内最新；无交集 → 提示安装 `dsh.version`；两者缺省 → 本机最新已装。（与 PackForge §9.1 一致）
2. **launcher 身份**：PAD 认领 canonical ID（建议 `pad`），版本自报（`pad cli version --json`）；读 `launchers.json` 失败时回落内置清单，**绝不影响安装**。
3. **警告放行**：`supported:false` / 版本不足 → 重/轻提示 + 安装日志留痕，不硬拒。

---

## 6. 市场与发布

1. 市场数据源双轨：PAD 自有货架 + `dsh-pack-market/index.json`（懒加载 `packs/<owner>.<repo>/`）。
2. 下载/安装：`sha256 + size` 校验；失败回滚；来源展示来源标签。
3. 发布：遵循 PackForge publishing v1（仓库 + Release + `dsh-pack` topic）；PAD 自己的包也按此发，避免孤岛。

---

## 7. 安全与排除

沿用 PackForge 安全过滤 + PAD 现有规则：

- 必排除：`.credentials.yaml`、`.anonymous-user-id`、`attachments/`、`settings.yaml`、`skills/.system/`、`profiles/web|headless/`、`node_modules`、日志、`runtime.json`；
- dshhome 导出**默认排除**：workspace、`sessions/`、`storages/`（会话/运行数据不随包；需要时显式勾选并警告）；
- 描述符/文件落盘 0600；路径穿越校验（`..`、绝对路径、符号链接）。

---

## 8. 里程碑与验收

| 里程碑 | 内容 | 验收 | 估算 |
|---|---|---|---|
| **M1** | pack v2 模型 + `type` + `dshVersions` + 精确 `deps` + `.dspack` 嗅探/解析 + **profile 型导入（compat）** | 用 PackForge 参考包 `all-about-whales` 导入成功；版本决策有日志；白名单/投影不参与 | 2–3 天 |
| **M2** | `files[]` 下载/校验/回滚 + `launchers` registry 读取 + 警告放行 + 市场 index 只读 | 坏 hash 回滚；registry 不可达不阻断；市场列表可浏览 | 1–2 天 |
| **M3** | **导出 profile 型 `.dspack`** + 依赖锁死 + `pad` sidecar/文件化二选一 | 导出的包过 PackForge 校验器/能被 dspack CLI 打开；往返测试 import→export→import 一致 | 2–3 天 |
| **M4** | `dshhome` / `home/` / 多 profile / preset/skill/指令；排除策略 | 多 profile 导入后各自可启动；凭据/会话不泄露 | 4–6 天 |
| **M5** | registry PR + 市场发布 + 文档/证据 | 身份登记成功；发布走通 | 1–2 天 |

合计 **10–16 天**；M1 结束即「能装别人的」，M3 结束即「别人能装我们的」。

### 验收固定夹具

- `all-about-whales`（PackForge 参考实现，profile 形态）
- 自造 dshhome 夹具（2 profile + preset + skill + AGENTS.md）
- 坏包夹具：sha256 错、`dspack.json` 版本错、缺 `manifest.json`、路径穿越、凭据文件
- 语言/平台：Windows 必测；兼容矩阵 `0.1.0-rc.7 / 0.2.1-rc.2`

---

## 9. 代码落点

| 职责 | 文件 |
|---|---|
| pack v2 模型/解析/迁移（v1→v2） | `Core/Packs/PackDoc.cs`、`Core/Packs/PackV2.cs` |
| `.dspack` 嗅探/解析 | `agent-pack-dsh/modpack/packforge.ts`（已实现） |
| 市场 / registry / files[] / compat 安装 | `agent-pack-dsh/modpack/packforge-ext.ts`（已实现） |
| compat 安装引擎（PAD 侧接线） | `Core/Packs/CompatInstaller.cs`（待做，调用 pack-runtime） |
| native 引擎（保留） | `Core/Packs/ProjectionInstaller.cs` |
| 版本决策 | `Core/Packs/VersionDecision.cs`（复用到 Release 库） |
| launcher registry | `Core/Packs/LauncherRegistry.cs` |
| 市场 | `Core/Market.cs` 扩 `dsh-pack-market` 数据源 |
| 导出 | `Core/Packs/DspackWriter.cs` |
| CLI | `pad cli pack import|export --format dspack`、`pad cli launcher id|version`、`pad cli market --source packforge` |
| 诊断码 | PA140–PA149（packforge.*） |

---

## 10. 风险与开放问题

1. **D13 例外**：native 投影不变；compat 是新通道。需要一条决策记录（建议 D101）。
2. **数据模型改动**：`deps` 精确坐标、`files[]`、`type/dshhome` 都是 v2 新增；v1 包读取需迁移，**不可静默降级**（迁移失败报 PA140）。
3. **pnpm/reconcile 对齐**：PAD 用 `dsh plugin add` 驱动；PackForge 用 plugin manager + reconcile。需在 M1 用真实包验证一次完整安装，不靠文档假设。
4. **导出语义损失**：投影内容文件化会丢 PAD 语义；sidecar 则别家不认。必须显式让用户选。
5. **sessions/workspace 排除策略**：dshhome 导出默认排除，是否允许"带会话的工作区快照"另立开关。
6. **规范演进**：PackForge 仍是社区规范（v5/v3/index v2），需版本门 + 回落策略；官方若出标准则跟随。
7. **license**：PackForge 规范与示例包的许可证需核对后再引用其样例内容。

---

## 11. 实现进展（2026-11-02）

**已落地并验证：PackForge 适配器（纯逻辑层，未接安装引擎）**

- 代码：`agent-pack-dsh/modpack/packforge.ts`（701 行）+ `packforge.test.ts`（344 行，17 项断言）
- CLI：`packagent dsh packforge inspect | convert | export | decide | launchers`
- 能力：`.dspack` 读取与 manifest v5 校验、路径安全（PA143）、版本决策（pinned/set-latest/needs-install/latest-installed）、launcher 判定（警告放行）、`.dspack ⇄ pad-pack v2` 转换、导出（精确依赖守卫 PA148、敏感文件过滤、zip 生成）
- 验证（证据：`EVIDENCE/D101-packforge-adapter/`）：
  1. 单测 17 项全过；
  2. CLI 端到端通过，导出产物可被 `Expand-Archive` 正常解压（`dspack.json` + `manifest.json` v5）；
  3. `bun build src/cli.ts --target=bun` 后 216 modules / 0.85MB，bundle 内 packforge 命令可用（pack-runtime 可携带）；
  4. **真实市场包**：市场索引 8 个包（7 个 v5 / 1 个 v4；dshVersions 0、launchers 0 → 生态采纳率低）；下载 5 个真包 sha256 全对；4 个 v5 `inspect` 通过；v4 按预期拒绝 PA141；`better-deepseek-harness-codex-2.8.1` 真包往返成功（26 bundles / 24 deps / 343 home 文件全保留）。
- 尚未做：C# 侧 compat 安装引擎接线（PAD UI/CLI 调 pack-runtime；当前已能在 TS 里跑通计划与落盘）、真实 `dsh plugin add`/pnpm 依赖安装（未在验证机上执行网络安装）、`dshhome` 安装（M4，当前显式 PA151）、launcher ID 正式 PR 认领（材料已就绪，见 `packforge registry` 输出的 `padClaim`）、v4 导入已支持（历史单 profile，按 profile 处理）。

**扩展层（D102，同日落地并验证）**

- 代码：`agent-pack-dsh/modpack/packforge-ext.ts` + `packforge-ext.test.ts`（10 项断言）
- 新增 CLI：`packforge market | fetch <id> | registry [--id] | install <file> --home <dir> [--apply] [--deps]`
- 能力：
  - **市场**：读 `dsh-pack-market/index.json`（schemaVersion 2），逐 URL 下载并校验 `sha256+size`；支持 `downloadUrls` 备用源
  - **registry**：读 `launchers.json`，失败回落内置清单（不阻断）；输出 PAD 待认领记录与 PR 行
  - **files[]**：多 URL 下载 + 校验 + 临时名落盘；坏 hash/不安全路径/无 URL 分别记入 failed/skipped
  - **compat 安装**：plan（校验 home/profile、bundles/overrides/home、敏感文件警告）→ apply（profile 形态：overrides→profile 根、home/→home 根、写 `dsh.profile.bundles`；dshhome 形态：逐 profile 落盘 + home 根 preset/skill/AGENTS.md；可选 `--deps` 跑 `dsh plugin add`、files[] 下载）；默认 dry-run，`--apply` 才落盘；已存在 profile → PA150
- 验证（证据：`EVIDENCE/D102-packforge-compat-market/`）：
  - ext 测试 10 项全过；核心测试 17 项全过
  - 真实市场：8 个包列出；`fetch pokemon` 下载 sha 与索引一致；真实 registry 读取成功
  - 真实包 `install --apply`：pokemon 包落盘（2 overrides + 4 bundles + 2 deps planned）；codex 包 dry-run 正确显示 bundles=26 / deps=24 / home=343，且不写盘
  - pack-runtime bundle 重建 0.87MB，bundle 内 `market` 可联网工作
- 新增诊断码：PA150（目标 home/profile 不合法或已存在）、PA151（不支持的 pack 类型，如 collection）、PA152（市场索引/查找失败）、PA153（包下载/hash 失败）、PA154（registry 回落，warning）、PA155（`dsh plugin add` 失败）、PA156（profile package.json 非法）

**PAD C# 接线（D103，同日落地并验证）**

- `Packs` 新增 `PackforgeJson/Inspect/Market/Registry/Install`（自动识别便携 pack-runtime 或仓库 node 模式）
- `PacksForge.Import`：inspect → 选发行号（`--release`/`dshVersion`/`dshVersions`）→ 校验已装 → **先建隔离实例** → compat 安装进该实例 home；dshhome 用 `defaultProfile` 作为实例默认 profile（M4 见 D105）
- CLI：`pad cli packforge inspect|market|registry|install|import`，全部支持 `--out`
- 验证（证据：`EVIDENCE/D103-pad-packforge-wiring/`）：
  - `dotnet build -o .tmp/pad-build-z` 0 error（正式 bin/Debug 被运行中的 PAD 锁住）
  - PAD CLI `inspect`/`import` 端到端：真实 pokemon 包 → 创建实例 pf-demo2 → profile/package.json bundles 与 overrides 落盘
  - **真实依赖安装**：shim `dsh` → 真实 DSH 0.2.1-rc.2（node 24 / pnpm 11.7）→ `dsh plugin add json5@2.2.3` 成功，`node_modules/json5`、`pnpm-lock.yaml`、profile dependencies 均出现
- 仍缺：launcher registry PR 提交（材料已就绪，见下）

**M4 dshhome + registry PR 材料（D105，同日落地并验证）**

- compat 引擎支持 `type:"dshhome"`：`overrides/profiles/<name>/` 逐 profile 落盘、其余 `overrides/` 进 home 根（`.agent-presets/` / `skills/` / `AGENTS.md` / `data/`）、逐 profile 写 `dsh.profile.bundles` + 依赖安装、`defaultProfile` 作为实例默认启动 profile、重技能 `skills[]`（带 urls 的条目）复用 files[] 下载
- C#：`Inspect` 不再拒绝 dshhome；`PacksForge.Import/ImportAuto` 用 `defaultProfile` 作为实例 profile；`pad cli packforge import-auto` 已跑通 dshhome 真包夹具
- 验证（证据：`EVIDENCE/D105-dshhome-m4-registry-pr/`）：
  - ext 测试 12 项（含 dshhome plan/apply：2 profile、preset/skill/AGENTS.md、profile patch、逐 profile bundle/package.json、per-profile deps runner）
  - CLI 安装 dshhome 夹具：`copiedOverrides=4`，`profiles/dev/package.json`、`profiles/writer/package.json`、`.agent-presets/coding/`、`skills/whale/`、`AGENTS.md` 全部落盘
  - PAD `import-auto` dshhome：实例 `ext-home`，`lastProfile=dev`，上述文件在实例 home 内齐全
  - **dshhome 多 profile 真实网络依赖**：`dev→json5@2.2.3`、`writer→is-number@7.0.0` 各自 `dsh plugin add` + pnpm 真装成功（两个 profile 的 node_modules/lock 独立），CLI 与 PAD `--deps` 均通过
- registry PR：**已提交** —— `DSH-PackForge/DSH-PackForge#4`（registry 表行）与 `DSH-PackForge/dsh-pack-market#2`（index/web launchers.json），均 open/mergeable；合并后 `packforge registry` 会显示 `padClaim.present=true`

**窗口 UI + 网络验证（D104，同日落地并验证）**

- UI：窗口拖入 `.dspack`（与原生 `.pack.zip` 分流）→ `PacksForge.ImportAuto`（manifest 取 name/profile、发行号按已装集合决策、重名自动后缀）；版本页文件框同样支持 `*.dspack`
- `pad cli version --json`（registry 认领要求的自报版本命令）已加
- 验证：源码契约测试 16 项；`dotnet build` 0 error；与 UI 同函数的 `import-auto` 用真实 pokemon 包建出实例 pokemon / pokemon-2
- 网络：`files[]` 真 CDN 下载 + sha256/size 校验通过；`github:… #commit` 依赖经 `dsh plugin add` + pnpm 真实安装成功（is-number）
- 兼容性发现：真实市场包 `pokemon@1.0.0` 的传递依赖 `@deepseek-ai/dsh-type-meta` 非公开包，公网安装失败（PA155 带出 pnpm 日志）——市场包质量问题，非 PAD 逻辑问题

**实例 → `.dspack` 反向导出（D106，同日落地并验证）**

- 新模块 `agent-pack-dsh/modpack/packforge-export.ts`：实例 home → `.dspack`（单 profile → `type:"profile"` + `home/`；多 profile → `type:"dshhome"` + `overrides/profiles/*`，`defaultProfile` 取实例 lastProfile）
- 安全排除：sessions/storages/logs/workspace/attachments/凭据/settings.yaml/launcher.patch.yml；安装产物（node_modules/package.json/lock）也不进包
- 依赖必须精确坐标，否则 PA148；写出后立刻用自家读取器自检
- 入口：CLI `packforge export-instance`；PAD `pad cli packforge export-instance <home> --file <x.dspack>`；实例页「导出 .dspack」按钮（SaveFileDialog）
- 验证（证据：`EVIDENCE/D106-instance-dspack-export/`）：
  - 单测 5 组：pinned 判定 / dshhome 导出（排除敏感+派生文件）/ 往返导入 / 单 profile 形态 / PA148
  - 真实实例端到端：`.tmp/pad-z-root/instances/ext-home` → export dshhome（2 profiles / 4 overrides / 3 homeFiles）→ 重新 import 到新 home，AGENTS.md/presets/skills/profile patch/package.json 齐全
  - PAD CLI `export-instance` 通过；`dotnet build` 0 error；UI 契约测试补导出按钮/命令
- 仍缺：workspace/sessions 的“工作区快照”（另立设计）、`files[]` 重内容导出、范围依赖的自动锁定（当前 PA148 提示）

**工作区快照导出 + 范围依赖自动锁定（D107，同日落地并验证）**

- 范围依赖：导出时先读 `<profile>/node_modules/<dep>/package.json` 的已装版本锁成精确坐标（不能锁 → PA148），D106 已补测试
- 工作区快照导出：`agent-pack-dsh/modpack/workspace-snapshot.ts` —— 实例 `storages/workspace.json` + 选中 session 文件 → `.workspace.zip`（`snapshot.json` schema `pad-workspace-snapshot/v1`）
  - `--workspace` 过滤、`--max-sessions N`（按 mtime 取最近）、裁剪 workspace.json、记录 `originPath`/`encodedDir`/`requires`
  - 不导出凭据/settings/launcher 产物
  - CLI `packforge export-workspace` + PAD `pad cli packforge export-workspace`
- 验证（证据：`EVIDENCE/D107-workspace-snapshot/`）：3 组单测全过；真实实例 123（4 sessions, 65KB）导出并在 zip 中核对 snapshot.json/裁剪 workspace.json/session 文件；PAD CLI `--workspace + --max-sessions 2` 通过；`dotnet build` 0 error
- 仍缺：工作区快照**导入**（workspace.json 写回、sessions 目录名重写、zstd 首帧 cwd 重写）、`--max-context`、`kind:both`
