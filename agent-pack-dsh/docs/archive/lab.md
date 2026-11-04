# PAD Lab：DSH 试验场（功能方案）

> **ARCHIVED（2026-11-04）**：已落地/研究完成，仅存档，不再维护。活文档见 [docs/README.md](../README.md)。


> **已被取代（2026-11-04，D111）**：用户定调“试验应该是一种启动方式”，改为**开发模式 / 开发会话**方案，见 [`dev-mode.md`](dev-mode.md)。本文保留作历史，不再实施“复制实例”的 lab。

2026-11-04 · 状态：方案待拍板 · 关联：`launcher-opportunities-2026-11.md`、`pack-plugin-layering.md`、`launcher-pcl-depth.md`

> **一句话**：装/升级/换版本这类会伤环境的操作，先在可丢弃的隔离副本（lab）里做；跑一套判据验证；通过就"晋升"回真实实例，不通过就丢弃。真实实例全程不被触碰。

---

## 0. 为什么是"试验场"而不是"启动器/救援器"

| 判断 | 依据 |
|---|---|
| 启动器/管理器没有用户 | 六个管理器合计 <250 次下载；桌面壳单版 5,000+ |
| "回滚/恢复"已被壳内建 | dataelement 0.12.1→0.13.2 连续三版做 Safe Mode/启动恢复 |
| 真实痛点全是"改环境" | 插件 124 / 重装 85 / 无法启动 64 / 恢复 62（issue 词频）；#843 五天不可用、#250 丢插件 |
| 用户已在手工搭测试环境 | 自救方式 = 降级换版本、重装、手改 profile、自己拍快照 |

**核心差异不是"我也有回滚"，而是"变更发生在副本里，且有一份可复现的验证结论"。**

---

## 1. 设计原则（硬约束）

1. **单元是"实验"，不是"实例"**。用户不需要管理 N 个 DSH，只需要"这一次变更有个副本"。
2. **源实例只在 promote 时被修改**，且 promote 前自动打点；其余时间源实例只读。
3. **CLI 先行、退出码可判**（承 D6）；窗口只呈现事实。
4. **判据是资产**：`verify` 输出稳定 JSON + 证据目录，可进 EVIDENCE、可回归。
5. **不碰 DSH 内部实现**：只用官方 `dsh plugin`、`--dump-config`、版本库、进程/端口契约（承 D12/D16）。
6. **默认不重新下载**：依赖复用源实例的 pnpm store / 版本库。
7. **新功能一律新增文件**：当前工作树有 D104/D105 未提交改动，`Cli.cs` 只做单点修改。

---

## 2. 领域模型

```
labs/<id>/
  lab.json            # Lab 模型（唯一真值）
  home/               # 独立 DSH_HOME
  logs/               # 进程日志
  checkpoints/<ts>/   # 轻量打点（manifest 文件集）
  verdicts/<ts>/      # 每次 verify 的证据
  events.jsonl        # 追加式实验历史
```

### 2.1 Lab

| 字段 | 说明 |
|---|---|
| `id` / `name` | slug / 显示名 |
| `sourceInstanceId` | 从哪个真实实例开出来 |
| `dshVersion` / `profile` | 内核钉与 profile |
| `state` | `created` → `trying` → `verified` / `failed` → `promoted` / `dropped` |
| `home` / `port` / `pid` | 隔离与运行事实 |
| `verify` | 判据配置（阈值，默认见 §4） |
| `createdAt` / `updatedAt` | — |

### 2.2 Experiment（一次试验）

`events.jsonl` 一行一条：`{ts, kind, target, before, after, checkpointId, result}`。
`kind` ∈ `plugin-add | plugin-remove | plugin-update | version-pin | pack-import | config-patch`。

### 2.3 Verdict（验证结论）

`schema: pad.lab-verdict/v1`

```json
{
  "lab": "try-upload", "source": "ev-project", "ts": "2026-11-04T03:5x:00Z",
  "result": "pass|fail|inconclusive",
  "checks": [
    {"id":"config-compiles","status":"pass","ms":812,"evidence":"dump-config.yml"},
    {"id":"boots","status":"pass","ms":4310,"evidence":"logs/"},
    {"id":"stays-up","status":"pass","ms":45000},
    {"id":"tools-inventory","status":"warn","detail":"tools 71→78 / 12755→13102 tok"},
    {"id":"plugins-resolve","status":"fail","evidence":"plugin-audit.json","detail":"missing @x/y"}
  ],
  "exitCode": 2
}
```

`status` ∈ `pass | warn | fail | skip`。**warn 不阻断晋升，fail 阻断。**

---

## 3. 命令面

```text
pad cli lab new <name> --from <instance> [--version <ver>] [--profile <p>] [--copy-sessions]
pad cli lab list [--json]
pad cli lab show <name> [--json]
pad cli lab try <name> plugin add <pkg@ver>          # 或 --pack <file> / --version <ver>
pad cli lab try <name> plugin remove|update <pkg>
pad cli lab verify <name> [--json] [--wait 60] [--keep-running]
pad cli lab diff <name> [--json]                     # 相对源实例的变更集
pad cli lab promote <name> [--dry-run]               # 先打点再应用
pad cli lab rollback <name> --to <ckpt|last>
pad cli lab open <name>                              # web/tui，独立端口
pad cli lab drop <name> [--force]
```

退出码：`0` 成功 / 可晋升；`2` 验证不可晋升；`3` 判据不可判；错误码 `PA2xx`（需登记 `pa-codes.json`）。
帮助文本同步 `Cli.cs` 的 Usage。

---

## 4. 验证判据（"足够强"的核心）

默认 8 项，可在 `lab.json#verify` 覆盖阈值：

| # | id | 判据 | 证据 |
|---|---|---|---|
| 1 | `config-compiles` | `dsh --profile <p> --dump-config` 退出 0 | `dump-config.yml` |
| 2 | `boots` | 启动进程并读到 ready 行（web） | `logs/` |
| 3 | `stays-up` | 存活 N 秒（默认 45） | 进程事实 |
| 4 | `tools-inventory` | 工具数/token 相对基线 | `inspect.json` |
| 5 | `plugins-resolve` | bundles 全解析、无 missing、无 peer 冲突 | `plugin-audit.json` |
| 6 | `no-crash-rules` | `CrashAnalyzer` 无命中、退出码为 0 | `crash.json` |
| 7 | `sessions-work` | 建会话 + 一轮对话可用（需 API Key，默认 `skip`） | `session.json` |
| 8 | `source-untouched` | 源实例 home 指纹前后一致（隔离自证） | `source-hash.txt` |

判定：任一 `fail` → `fail`；`inconclusive` = skip > 2 或环境异常。

**这就是护城河**：壳能给你"启动成功"，PAD 给你"这次变更到底改变了什么、还剩下什么风险"。

---

## 5. 关键动作语义

### 5.1 new（建副本）

- 目录：`labs/<id>/`；独立端口（从 3080 起避让，复用 `Ports.cs`）；不碰 `~/.dsh`。
- home 内容：复制源实例的 **profile 声明层**（`profiles/<p>/package.json` 的 deps + `dsh.profile.bundles`、`pad.json` 相关键、内核钉、pack 账本），**不复制 sessions / attachments / node_modules**。
- 依赖：优先复用源实例的 pnpm store / 版本库；需要安装时走 `dsh plugin add`（官方通道，承 D12）。
- 隔离自证：new 结束时对源实例 home 做一次指纹，写进 `lab.json#sourceHash`。

### 5.2 try（试验）

- 复用 `Launcher.PluginOp` / `Packs.ImportPack` / 版本 pin 的既有实现，只是目标从"实例"换成"lab 的 home"。
- 每次 try 前自动打轻量 checkpoint（见 5.4），`events.jsonl` 记录 before/after。
- try 不自动 verify（显式调用，避免"装一下就跑 45 秒"）。

### 5.3 verify（验证）

按 §4 顺序执行；`--keep-running` 保留进程供人工看；默认结束后停进程。
产出 `verdicts/<ts>/` + `verdict.json`，写回 `lab.json#lastVerdict`，退出码 0/2/3。

### 5.4 checkpoint / rollback

- checkpoint = manifest 文件集：`instance.json` 相关键、`profiles/<p>/package.json`（含 bundles）、`pad.json` 相关键、pack-ledger、内核钉。
- **不存** sessions / node_modules（依赖坏了用 `PnpmHeal` 修，不用快照）。
- `lab rollback` 写回上述文件（`Trial.WriteBundles` 已有现成写法），**只用于 lab 内**，不改源实例。

### 5.5 diff / promote

- `diff` = lab 相对源实例的变更集：bundles delta、deps delta、内核版本、pack 账本 delta、profile 配置键。
- `promote`：
  1. 检查源实例无运行中写入（`runtime.json`），否则 `PA2xx`；
  2. 给源实例打 `pre-promote` checkpoint；
  3. 应用变更（写 profile、pin 内核、合并账本）；
  4. 失败自动 restore `pre-promote`；
  5. 产出 `promote-report.json`（改了什么 + 证据）。
- **边界**：只搬"环境声明"，不搬 home / sessions / 工作区。

### 5.6 drop

停进程 → 杀进程树 → 删 `labs/<id>` → 释放端口记录 → 源实例指纹复核 → 写审计。
默认保留 24h（`lab list` 可见 `expired`），`--force` 立即删。

---

## 6. 实现落点

### 6.1 新增文件

| 文件 | 内容 | 依赖 |
|---|---|---|
| `pad/Core/Lab.cs` | `Lab` 模型 + `LabStore`（读写 `lab.json`，风格照 `PackLedger`） | 无 |
| `pad/Core/LabCreate.cs` | new/list/show/drop/open；隔离与端口 | Launcher、Ports、Nodes |
| `pad/Core/LabTrial.cs` | try：plugin/pack/version/config-patch | Launcher.PluginOp、Packs、DshKernels |
| `pad/Core/LabVerify.cs` | 8 项判据 + verdict + 证据 | LaunchPolicy、Inspect、CrashAnalyzer、PluginList、Proc |
| `pad/Core/LabPromote.cs` | diff/promote/rollback | Trial.WriteBundles、PackLedger、Launcher |
| `pad.tests/LabTests.cs` | store/判定表/diff/promote dry-run | xUnit |

### 6.2 修改文件（Lead 独占，单点）

| 文件 | 改动 |
|---|---|
| `pad/Core/Cli.cs` | `case "lab"` 分发 + Usage 一条 |
| `modpack/registries/pa-codes.json` | 新增 `PA2xx` 段 |
| `docs/README.md` | 索引一行 |

### 6.3 复用清单（已验证存在）

`Launcher.CloneInstance/CopyTree/WriteAtomic/StartJob/AppendJobLog`、`LaunchPolicy.WaitForWebReady`、`Ports.cs`、`Nodes.cs`（起停）、`Trial.WriteBundles`、`PackLedger`、`Inspect.Measure`、`CrashAnalyzer`、`PluginList`、`PnpmHeal`、`DshKernels`、`Runner`。

---

## 7. 要拍板的决策（6 条）

| # | 决策 | 推荐 | 代价 |
|---|---|---|---|
| D1 | lab 的 home 怎么建 | 空 home + 复制 profile 声明层 + 依赖复用 store（默认）；`--copy-sessions` 可带会话 | 最干净；新建需要一次依赖安装 |
| D2 | 依赖是否共享 | 默认独立，允许 `--shared-deps` 换速度 | 共享写有污染风险 |
| D3 | promote 粒度 | 只搬环境声明（bundles/deps/内核/账本/配置键） | 不搬会话与工作区 |
| D4 | promote 时源实例是否必须停 | 不必停，但要求 `runtime.json` 无活跃写入 | 否则拒绝 |
| D5 | 是否允许直接对源实例做实验 | 不允许（保持语义干净） | 用户必须先建 lab |
| D6 | verify 默认存活阈值 | 45 秒（`LaunchPolicy.RunStartGraceSec` 同源） | 可配 |

---

## 8. 验收（"足够强"的判据）

| 编号 | 场景 | 判据 |
|---|---|---|
| E2E-1 | 隔离 | 建 lab 后源实例 home 指纹不变；lab 运行不影响源实例进程/端口 |
| E2E-2 | 坏插件 | 装 #843 型坏包 → `verify` 退出 2（`plugins-resolve` 或 `config-compiles` fail）→ `drop` → 源实例仍可启动、工具数不变 |
| E2E-3 | 好插件 | 装正常插件 → `verify` pass → `promote` → 源实例出现该插件、dump-config 0 |
| E2E-4 | 回滚 | 装坏 → `lab rollback` → `verify` pass（证明 checkpoint 有效） |
| E2E-5 | 换内核 | lab 里 pin 另一发行号 → verify → promote 改源实例内核钉 |
| E2E-6 | 证据 | 每次 verify 有 `verdicts/<ts>/`；`--json` 结构稳定可脚本判读 |
| 单测 | — | store 读写、verdict 判定表、diff、promote dry-run 不写盘 |

**故障夹具**：#843（插件更新清空 node_modules 不回滚）、#250（迁移回滚不彻底 + 备份被清）两种真实坏法，做成可离线复现的 fixtures。

---

## 9. 分期

| 期 | 内容 | 完成标志 |
|---|---|---|
| **P0** | `lab new/list/show/drop` + `try plugin add` + `verify` 前 6 项 + 证据 + 单测 | E2E-1/2/3 通过 |
| **P1** | checkpoint/rollback + promote/diff + 版本 pin 试验 | E2E-4/5 通过 |
| **P2** | 窗口「试验场」页 + `sessions-work` 判据 + 夹具库 + 重启后 lab 状态恢复 | 窗口可完成一次完整试验 |
| **P3** | lab 模板/分享（把一次坏法存成包，别人可复现） | 能用 lab 复现别人报的 bug |

---

## 10. 风险与对策

| 风险 | 对策 |
|---|---|
| pnpm 依赖复用/隔离的性能与正确性（最大风险） | P0 先走"官方 `dsh plugin add`"最慢但最正确的路；P1 再做 store 复用 |
| 端口/进程泄漏 | 复用 `Nodes` 的账本 + 进程树收敛；`lab drop` 必须杀干净并复核 |
| 与桌面壳抢 home | lab 一律不用 `~/.dsh`；只用 `labs/<id>/home` |
| verify 误报（工具数阈值） | 阈值可配；`warn` 不阻断；判据命中率进 EVIDENCE 回归 |
| 已有未提交改动（D104/D105） | 新功能只加新文件；`Cli.cs` 单点修改；不碰 `Packs.cs`/`Launcher.cs` 现有逻辑 |
| 用户不会主动建 lab | P2 窗口做"改环境前自动建议建 lab"；CLI 在 `plugin add` 前提示 |

---

## 11. 产出物

- `docs/lab.md`（本方案）
- `pad cli lab` 命令面 + `pad/Core/Lab*.cs` + `pad.tests/LabTests.cs`
- `EVIDENCE/D1xx-lab/`：E2E-1…6 的真实证据
- `pa-codes.json` 新增 PA2xx 段
