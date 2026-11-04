# PAD × 官方 HMR 集成方案（驱动 DSH 自带热重载）

2026-11-04 · 状态：方案待拍板 · 依据：`EVIDENCE/hot-reload-spike/`（两轮实测）、`@deepseek-ai/dsh-hmr@0.2.1-rc.2` README、本轮调研

> **一句话**：PAD 不实现热重载。默认走冷路径（写盘 + 重启）；只在需要速度的场景（lab / `--hot` / 更新）驱动官方 `dsh-hmr`，并且每次变更后用**读盘事实**验证、失败由 PAD 自己回滚。

---

## 0. 三条硬规则

| # | 规则 | 理由 |
|---|---|---|
| R1 | **默认冷**：`plugin add/remove/update`、pack 变更默认"写盘 → 重启 → 验证" | 行为可预测；和任何第三方管理器零冲突；不赌外部壳的 HMR 状态 |
| R2 | **场景化热**：只有 lab（试验场）与显式 `--hot` 才尝试热路径 | 热只在"沙盒测试 / 开发 / 更新"里有价值（实测热挂 ≈2.9s，冷启动 5–15s） |
| R3 | **单一 writer**：读到 mygo 或其它管理器 → 热关闭，默认只读；不双写账本 | PAD 的 pack-ledger 与 mygo 的 `dsh.lock/v1` 不同源，同写必漂移 |

**为什么不做全局热**：用户装没装什么、实例谁起的、HMR 是否被禁——这些差异不该让主路径分叉。冷路径对所有人都成立；热是加速器，不是必需品。

---

## 1. 能力判定：读盘纯函数，不用运行时探针

### 1.1 输入（全部已有能力）

| 输入 | 来源 | 用途 |
|---|---|---|
| 启动来源 | `instances/<id>/runtime.json` 账本：pid/port/profile/surface | PAD 起的实例才谈热 |
| profile 文件 | `profiles/<p>/package.json`（bundles/deps）、`cordis.patch.yml` | 是否禁用 hmr、是否 config-only（`root: []` 覆盖） |
| 插件清单 | `pad cli plugin list <inst> [profile]` → `Launcher.ListBundles` | 识别 mygo 包 / 第三方管理器 / 已有 bundles |
| 组合合法性 | `dsh --profile <p> --dump-config` 退出码 | 当前树是否健康（热/冷都需要） |

（实测：普通 `dsh` CLI 会 `provide('profileContext')`，HMR 条目激活；`--dump-config` 里的 `disabled: !!js` 是未求值表达式，**不能**用它判能力。）

### 1.2 输出 schema

`instances/<id>/hot-capability.json`（`pad.hot-capability/v1`）：

```json
{
  "schema": "pad.hot-capability/v1",
  "instance": "ev-project",
  "profile": "web",
  "launchSource": "pad | external | none",
  "hmr": "on | config-only | off | absent",
  "manager": "none | mygo | other",
  "hotAllowed": false,
  "reason": "launchSource=external"
}
```

### 1.3 判定表

| launchSource | profile 情况 | manager | hmr | hotAllowed |
|---|---|---|---|---|
| pad | 未禁用 hmr、未限制 root | none | on | ✅ |
| pad | 只有 `root: []`（base 默认） | none | config-only | 仅配置级热（插件新增 → 冷） |
| pad | patch 显式 `{id: hmr, disabled: true}` / headless 类组合 | none | off | ❌ |
| external（桌面壳等） | 任意 | 任意 | 不判 | ❌（不赌外部壳） |
| 任意 | 任意 | mygo/other | — | ❌（只读/委托） |
| 任意 | 旧版本无 `dsh-hmr` | — | absent | ❌ |

缓存 TTL 10 分钟，或 profile manifest / patch 的 mtime 变化时失效；`pad cli plugin status --refresh` 强制重算。

---

## 2. 三条路径

### 2.1 冷路径（默认）

```
checkpoint
  → pnpm 安装/卸载（Launcher.PluginOp 现有链路）
  → 原子写 profiles/<p>/package.json 或 cordis.patch.yml（WriteAtomic）
  → 快速重启（Runner.Stop → Runner.Launch，同 profile/端口）
  → 验证 V1–V3
  → commit ｜ 失败 → restore checkpoint → 再验证 → 失败则 quarantine
```

- 重启代价：实测冷启动 5–15 秒（含 runtime 加载）；会话在磁盘上不丢，**进行中的 turn 会断**，提示写清楚。
- 用户可见：`已安装，重启后生效（约 Ns）`。

### 2.2 热路径（lab / `--hot`，仅 `hotAllowed=true`）

```
checkpoint
  → pnpm 安装依赖（官方 HMR 的队列之外，必须先装完）
  → 原子写：新增走 bundles（manifest 变化触发官方 recompose）或 patch insert（用过即弃）
  → settle：3s 起轮询验证，每 500ms 一次，最多 15s
  → 验证 V1–V4
  → commit ｜ 失败 → 撤回（写回 manifest / 删 patch 行）→ 再验证
           → 撤回后仍失败 → 降级冷路径重试一次 → 仍失败 → quarantine + 提示重启
```

- 热成功判据（四条全过）：
  1. `dsh --profile <p> --dump-config` 退出 0
  2. `plugin list` 中目标行出现/消失/版本正确
  3. `inspect` 工具数符合预期（插件声明了工具时）
  4. **PID 与端口未变**（没偷偷重启）
- 官方已知边界（必须由 PAD 兜）：失败静默（stdout/stderr 无输出）、非原子（good+bad 同 patch 时 good 会挂上）、替换失败旧版本不恢复。

### 2.3 只读 / 委托（`manager ≠ none`）

- 默认只读：`plugin status` 展示；`add/remove` 报 `PA2xx`，文案明确"该实例由 mygo 管理，请在 mygo 面板操作"。
- `--delegate=mygo`（P2）：走 `/api/mygo/*`，需先核实 API 形态再开。
- 绝不做：PAD 写 bundles 而 mygo 同时维护自己的 lockfile。

---

## 3. 验证（V1–V4，全部读盘）

| # | 检查 | 命令 | 失败含义 |
|---|---|---|---|
| V1 | 组合合法 | `dsh --profile <p> --dump-config` 退出码 | 树坏了 |
| V2 | 行级事实 | `pad cli plugin list <inst> <profile> --json` | 没装上 / 没卸掉 |
| V3 | 工具事实 | `pad cli inspect <home> --json`（工具数 / token） | 插件没真正生效 |
| V4 | 运行事实 | 账本 pid/port 对比（热：必须不变；冷：新 pid 记录） | 热路径偷偷重启了 |

失败静默是官方行为（实测），**V1–V4 是唯一判定依据**；探针插件只在"四项不明"的 debug 场景用（P2，不做常驻）。

---

## 4. 事务与回滚

- checkpoint 最小集：`instance.json` 相关键、`profiles/<p>/package.json`、`cordis.patch.yml`、pack-ledger、内核钉。**不含** sessions / node_modules。
- 回滚：写回文件 + 删除新增 patch 行（+ 可选 `pnpm remove`）；等 settle；跑 V1–V3；失败 → `quarantine`，保留现场与证据，提示"重启可恢复"。
- 保留：最近 20 个 / 7 天，环形清理。
- 撤销：`pad cli plugin undo <inst>` 回到最近一次 checkpoint。

---

## 5. `restart-required` 判定

满足任一 → 必须重启，不尝试热：

- `launchSource=external` / `manager≠none` / `hmr=off|absent`
- 换内核发行号，或变更涉及框架依赖（`@deepseek-ai/cordis*`、非插件的 `@deepseek-ai/dsh-*`）
- 目标实例有 worker loader / 打包 runtime（官方 README 列的重启场景）
- 热验证失败且撤回失败

输出 `restart: true, restartReason=<枚举>`，落 job 结果与 UI。

---

## 6. CLI 面

```text
pad cli plugin status <inst> [profile] [--json] [--refresh]   # 读盘能力判定（hot/cold/manager）
pad cli plugin add <inst> <profile> <spec> [--hot|--cold] [--restart-ok]
pad cli plugin remove <inst> <profile> <spec> [--hot|--cold]
pad cli plugin verify <inst> [profile] [--json]               # V1–V4
pad cli plugin undo <inst>                                    # 回到最近 checkpoint
```

| 退出码 | 含义 |
|---|---|
| 0 | 完成且验证通过（热已生效 / 冷已写入并重启验证） |
| 2 | 已写入但需要重启（未自动重启，或判定 restart-required） |
| 3 | 验证失败，已自动回滚 |
| 4 | 回滚失败 / 需人工（quarantine） |
| PA2xx | 参数/环境/冲突错误（需登记 `pa-codes.json`） |

窗口：成功只给 Toast；常驻只有「待重启」一条（+ 行药丸），不新增页面，沿用现有插件页。

---

## 7. 代码落点

### 新增

| 文件 | 内容 |
|---|---|
| `pad/Core/HotCapability.cs` | 读盘判定 + schema + 缓存 |
| `pad/Core/Checkpoints.cs` | 最小 checkpoint / restore / undo 栈 |
| `pad/Core/HotReload.cs` | settle 等待、原子写、撤回、降级 |
| `pad/Core/PluginVerify.cs` | V1–V4 汇总（复用 `DumpConfig` / `ListBundles` / `Inspect`） |
| `pad.tests/HotReloadTests.cs` | capability 判定表、checkpoint 往返、settle 假时钟 |

### 修改（单点，Lead 独占）

| 文件 | 改动 |
|---|---|
| `pad/Core/Launcher.cs` | `PluginOp` 分叉 hot/cold + checkpoint 钩子 |
| `pad/Core/Cli.cs` | `plugin status/verify/undo`、`--hot/--cold` |
| `modpack/registries/pa-codes.json` | PA2xx 段 |
| `docs/launcher-architecture.md:282` | 改正"装完必须重启、不重读 bundles"的旧结论 |

### 复用

`Launcher.WriteAtomic` / `DumpConfig` / `ListBundles`、`Runner`、`Nodes`、`Trial.WriteBundles`、`PackLedger`、`Inspect`、`Ports`。

---

## 8. 验收

| # | 场景 | 判据 |
|---|---|---|
| E1 | 冷路径装插件 | 写盘 → 重启 → V1–V3 过；账本新 pid |
| E2 | 热路径装插件（lab/`--hot`） | 3–15s 生效；V1–V4 过；PID 不变 |
| E3 | 坏插件热装 | 自动撤回；V1–V3 回到基线；进程仍在；退出码 3 |
| E4 | 外部壳实例 | 判定 cold，不尝试热；退出码 2 |
| E5 | mygo 实例 | 判定 manager=mygo；add 拒绝 PA2xx 并给文案 |
| E6 | 换内核 | 判定 restart，不假装热 |
| E7 | 并发两次变更 | 串行锁；第二次等第一次结束；不互相踩 |

证据落 `EVIDENCE/hot-reload-integration/`，方法沿用 `hot-reload-spike`（scratch home + 真实 dsh）。

---

## 9. 风险

| 风险 | 对策 |
|---|---|
| 官方 HMR 行为随版本漂移 | 只依赖行为验证（V1–V4），不依赖日志/内部实现 |
| settle 时间不确定 | 轮询验证而非固定 sleep；超时降级冷 |
| 外部壳无法确认运行态 | 一律冷，不赌 |
| 第三方管理器 | 只读，不双写 |
| 回滚自身失败 | quarantine + 保留现场 |
| 热装期间用户手动改盘 | 变更串行锁 + 写前指纹校验，冲突就中止 |

---

## 10. 待拍板

1. 冷路径重启：默认自动重启，还是先问用户（默认自动 + 可关）。
2. 热失败后是否自动降级冷路径重试一次（建议是）。
3. `--hot` 是否允许在 external 实例上强开（建议否）。
4. `plugin status` 是否默认在插件页常驻显示"热/冷"徽标。

> 落地后记 `DECISIONS.md` 新 D 编号 + `docs/README.md` 索引；本文先作为方案。

## 12. UI（v1.1，已落码，已按「能删就删」收敛）

插件页把热重载接进窗口（按 `frontend-spec.md` v1.1；token/图标全部复用现有 Theme，不新增颜色/图标）：

- **成功不常驻**：热装成功后只给一条 3–5s Toast（`已生效 · 无需重启`）；行就是普通「已启用」。热/冷是实现细节，成功是终点，不占屏。
- **只常驻「待重启」**：插件页顶部单条琥珀条（同一时刻最多一条）「{spec} 待重启生效」+ 重启按钮（`Runner.Stop + Launch`）；对应行显示 `待重启` 药丸。这是唯一需要人做决定的状态。
- **失败**：红色 Toast + 插件日志原文，不常驻。
- **撤销**：不在 UI 常驻（成功态已收掉），走 CLI `pad cli plugin undo <instance>`；后续可放进行「更多」菜单。
- **状态来源**：`instances/<id>/hot-state.json`（`HotReload` 每次 apply 写入）+ 运行账本 `startedAt` 比较——进程在变更之后才启动则状态自然消化，不再显示。
- **动作接线**：窗口里的装/卸/更新改为 `HotReload.ApplyAsync(wantHot: true)`；PAD 启动的实例默认走热路径，失败自动回滚并提示；外部壳/只读实例自动落回冷路径与「待重启」。
- **能力放宽**：`hmr=config-only`（`root: []`，模块监听关闭）也算可热——官方 HMR 的 profile manifest/patch 重组合不依赖模块监听；只有改源码才需要 root。

真机截图：`EVIDENCE/hot-reload-integration/ui/00-before-green-bar.png`（收敛前，绿条常驻）、`02-real-pad-pending.png`（待重启）、`04-real-pad-hot-no-bar.png`（收敛后：热装成功不占屏）。