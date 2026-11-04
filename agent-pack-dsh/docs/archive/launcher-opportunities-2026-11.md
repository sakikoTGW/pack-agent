# PAD 可做事项报告（2026-11-04）

> **ARCHIVED（2026-11-04）**：已落地/研究完成，仅存档，不再维护。活文档见 [docs/README.md](../README.md)。


> 输入：社区群聊反馈（2026-11-03）+ 四份生态研究（behavior / inventory / top30 / landscape）+ 本机实测数据 + 当前代码审计。
> 结论先行：**启动和管理已经商品化；真正还空着的是「回退链」和「会话层」。PAD 的下一步不是加功能，是把这两件事做成能演示、能验证的承诺。**

---

## 0. 口径

| 来源 | 用法 |
|---|---|
| 群聊（🥲 / 小蒋 / 好名字 / 布衣 / 高盛盛 / Hairyf） | 需求信号，不是需求本身 |
| `launcher-top30`、`launcher-inventory`、`launcher-landscape`、`launcher-behavior-2026-11` | 竞品基线（1,222 客户端 / 325 启动器；30 个重点逐个看） |
| 本机实测（`STATE.md`） | 71 工具 / 12,755 token schema / 46 无关；45 轮会话到 33.4 万 token |
| 代码审计（`pad/Core`、`pad/Views`、CLI 子命令） | 已有什么、缺什么（§2、§4） |
| `DECISIONS.md` | D6（只读验收）/ D14（只报事实、不替人裁剪）/ D15（不报反事实）/ D20（不做多 DSH 主线）是硬约束 |

**口径纪律（三条，影响下面每一格）：**

1. **BEH 覆盖 LAND。** `launcher-landscape` 是桌面壳研究，已被 `launcher-behavior-2026-11` 降级为邻接物并自我推翻两处结论（"版本库仍是唯一硬空位"已作废：RYun601 已有活动指针 + 任意升降 + 回退链）。
2. **数量不可相加。** T30 是 30 个重点、BEH 是 24 个启动器、INV 单列表 1,222 个客户端 / 325 个启动器。所有"≥N 家"是去重下界，不是普查值。
3. **星数不能筛竞品。** 完成度与星数脱钩（DSH Manager 11★、shawnyhu 2★、RYun601 4★ 都比多数高星完整）。

---

## 1. 结论（三条）

1. **"启动/管理"已经商品化，不能当卖点。** 多版本共存 ≥12 家、每实例独立 `DSH_HOME` ≥8 家、崩溃诊断 ≥10 家完整救援面。PAD 靠功能清单竞争必输。
2. **还空着的是"组合"和"会话层"。**
   - `版本库 × known-good × 自动回切 × 跨实例` 四块单点都有人做，**同时具备的 0 家**（T30 §2.3、BEH §4.1）；
   - **会话级 deep-link = 0 家**（官方只有应用级 `dsh://`）；**跨实例会话总览与迁移 = 0 家**（LAND §6 空位 4、BEH §3.3）；
   - PAD 恰好同时有版本库（`DshKernels`）和会话跳转（`session-jump` M1 + `--explain` planner），两块都已有 80% 基座。
3. **启动价签是"双空白"，不是机会，是假设。** 全生态 0 家做启动时工具/token 预算，PAD 也没露出来——但没人验证过用户要不要。**不要写进对外卖点，先用演示测反馈。**

**一句话定位（改口版，采用 BEH §4.4 口径）：** 把多版本、实例、pack、回退放进一个可验证的管理面，且 pack 不进 plugin 栈；对外副标题可以是——**"DSH 的存档点和急救箱"**。

---

## 2. 现状：已经有的（审计过代码，不是文档口径）

| 能力 | 状态 | 落点 |
|---|---|---|
| 多发行号版本库（list/install/remove/pin） | 有 | `Cli.cs` release 命令组、`DshKernels.cs` |
| 每实例独立 `DSH_HOME` | 有 | `Launcher` instance create / clone |
| 实例克隆、导出（`.pinst.zip`）、工作区导出 | 有 | `Cli.cs` instance clone / export-instance / export-workspace；`Packs.ImportPack` |
| 启动 / 停止 / 进程树收敛 / 节点账本 | 有 | `Runner.cs`、`runtime.json`、`Cli.cs` run/stop/ps |
| 就绪判定（web ready） | 有 | `LaunchPolicy.WaitForWebReady` |
| 托盘 / 单实例锁 / 关窗缩托盘 | **有** | D99、`Tray.cs`、`SingleInstance.cs` |
| 自更新（检查 + 下载 + 替换自身 + 重启） | **有，已真机验证** | `PadUpdate.cs`、D93/D95 |
| 便携单文件 exe + pack-runtime | 有 | D88/D89/D91 |
| 插件页（装 / 停用 / 卸载 / 图标 / 更新） | 有 | `PluginList.cs`、D81/D82/D84 |
| 插件市场（npm 搜索 + 货架 + Hero 指标 + 详情） | 有（无策展层） | `Market.cs`、`DownloadView` |
| 整合包（`.pack/.pinst/.dspack`）导入 + 账本 + diff + 停用≠卸载 | 有 | `PackLedger.cs`、D105、`PacksForge.cs` |
| `.dspack` 双向兼容 + 反向导出 + 工作区快照导出 | 有 | `pack-v2-superset.md` §11 |
| 试验（trial add/commit/drop） | 有 | `Trial.cs` |
| `--dump-config` 配置校验 | 有（内部调用） | `Launcher.DumpConfig`、`AgentPresets.cs` |
| pnpm junction 修复 | 有（工具，未编排） | `PnpmHeal.cs` |
| 崩溃分析 | 有但很薄：**4 条规则 + 4 条 FAQ** | `CrashAnalyzer.cs`、`modpack/registries/crash-rules.json` |
| 体检（doctor） | 部分：只出报告，无修复入口 | `Launcher.cs:1731-1760`、`SettingsView.xaml.cs:1262-1294` |
| 会话跳转（surface registry + planner + gateway） | 有（M1） | `session-jump.md`、D101 |
| 会话备份 | 部分：只备 sessions + attachments，仅 UI 单条入口 | `Launcher.cs:710-738`、`InstanceView.xaml.cs:278-290` |
| 只读事实（inspect / usage / plugin audit） | 有（CLI） | `Inspect.cs`、`Usage.cs` |

### 2.1 缺口（审计：9 项能力里 0 项存在、3 项部分、6 项缺失；下表按产品视角重排）

| 缺口 | 判定 | 证据（文件:行） | 说明 |
|---|---|---|---|
| 存档点（变更前自动存一份） | **缺失** | `Launcher.cs:62-68`、`243-248`、`Runner.cs:78-90`；唯一"变更前备份"在 `SettingsView.xaml.cs:1325-1329` | 所有写入走 `WriteAtomic`（tmp + 覆盖，旧内容直接丢）；只有"设置页导入 pad.json"会先复制 `.backup-<ts>`，那是事后兜底不是打点 |
| 原地回滚（restore / rollback） | **缺失** | `Cli.cs:1293-1334` 全命令无 rollback/restore；`Launcher.cs:467` 删实例无撤销 | 最接近的只是**单步撤销**：`Trial.Discard` 回写 `dsh.profile.bundles`、`UninstallPack` 只回滚独占插件；`clone` / `export-instance` / `import` 都只造新实例，不回灌原实例 |
| known-good 判定 | **缺失** | `LaunchFloat.xaml.cs:143-169`、`Runner.cs:271-273`、`Models.cs:221-240` | 45 秒存活就直接 `return`，不落盘；`LastProfile` 在确认存活**之前**就写，所以是"最后尝试"不是"最后成功" |
| 变更时间线 / 归因 | **缺失** | `Launcher.cs:843-854`、`PackLedger.cs:404-424`、`Models.cs:238-239` | `jobs.json` 可被用户清空，`pack-ledger` 只有当前态，`instance.json` 只有 created/updated；三者都答不了"上周三改过什么" |
| 崩溃归因到变更 | **缺失** | `CrashAnalyzer.cs:47-58`、`65-81` | 只做日志正则、第一条命中即停；不读账本/版本/时间，无法回答"哪个包/哪次变更" |
| 一键抢救 / 安全模式 | **部分** | `Launcher.cs:1731-1760`（doctor 只出报告）、`Runner.cs:78-90`（启动前静默修 junction）、`Packs.cs:1631`（按包补缺件） | 构件散落，无编排、无 safe-mode、无"体检 → 修复"流水线 |
| 启动到 ready 耗时 | **缺失** | `LaunchFloat.xaml.cs:145-147`、`RunFacts.cs:33-44` | `started` 只用于超时判断，成功分支不算不显示；界面只有反向的"已跑多久" |
| 启动价签（工具数 / token） | **部分** | `Inspect.cs:18`、`178-184`；`LaunchView.xaml:77-93`、`WorkbenchView.xaml.cs:896-903` | 数据层有 per-tool token，两个启动入口都不显示（唯一相近的是实例页环境卡的"插件 N 个 / 调用 N 次"，不是 token 也不是"本次") |
| 会话备份 | **部分** | `Launcher.cs:710-738`、`InstanceView.xaml.cs:278-290` | 只备 `sessions/` + `home/attachments/`；不覆盖 instance.json / 账本 / profile bundles / 投影 / 凭据；无 CLI |
| 跨实例会话总览 / 会话级深链 | **缺失** | 仅有 `session-jump` M1 单 surface 跳转 | 没有全局视图，没有外部深链 |
| 市场策展层 | **缺失** | `Market.cs` | 市场 = npm 搜索的 UI |
| 坏包前置拦截 | **部分** | `Launcher.cs:1637` 有 `dump-config` | 能跑校验，但不在 import 前后当门禁 |

### 2.2 审计顺带发现的两个真 bug（便宜、该修）

1. **退出码解析器对不上自己写的行。** `Runner.cs:356` 追加 `[PAD] dsh exited {code}`（无 `with`），而 `LaunchPolicy.cs:224` 的 marker 是 `[PAD] dsh exited with`（对应 `Runner.cs:139` 那条脚本行）→ **PAD 自己写的退出码永远匹配不到**。不修，known-good 判定拿不到退出码。
2. **`LastProfile` 在确认存活前落盘。** `Runner.cs:271-273` / `375-376`，语义是"最后尝试"却被当"最后成功"用；救援 UI 若按它回切会回到一个没起来过的 profile。

### 2.3 研究文档里关于 PAD 自己的过期描述（别照抄）

`launcher-landscape §5` 曾经给 PAD 打 ❌：无托盘、自更新不应用、无单实例锁。**三条都已落地并真机验证**（D99 / D93 / D95），代码在 `Tray.cs`、`SingleInstance.cs`、`PadUpdate.cs`。
仍为真的是：**没有安装器**（只有单文件 exe）、**没有开机自启**。引用研究文档前先拿代码核一遍。

---

## 3. 生态基线：什么不能当卖点、什么是稀缺

### 3.1 已被追平（不能再当卖点）

| 能力 | 覆盖 | 结论 |
|---|---|---|
| 多版本共存 | **≥12 家**（dsh-plugins / RYun601 / hpyer / DSH-X / youridol / DreamRift / wishesl / Gru / oh-dsh / DSH Manager / HDSL / shawnyhu） | ❌ 商品化；只剩"任意升降 + 多级回退链体验"有 2 家（RYun601、youridol） |
| 每实例独立 `DSH_HOME` | **≥8 家**（dsh-plugins / dsh-helper / DreamRift / baihejiangnan / zat / MarcoG-h / shawnyhu / DSH Manager） | ❌ 已追平 |
| 崩溃诊断 / 救援面 | **≥10 家完整救援面**（dsh-tauri safe profile / fufankeji 插件事务回滚 / vibeinging doctor / DeepSeekGUI 诊断中心 / Bigfish / oh-dsh / cocode / DSHGuard / fnOS / dsh-chamber） | ❌ 已商品化 |
| 单点真回滚 | **≥10 家**（DSHGuard / fnOS / dsh-chamber / oh-dsh / DSHA；BEH 另有 RYun601 / zat / refyon / Gru / starline） | ⚠️ 单点已商品化 |
| 托盘 / 自更新 / 便携 | 全员入场券（托盘 ≥8、自更新 ≥9、便携 ≥7） | ❌ 缺了不像启动器，有了不加分 |
| 一键桌面端 / 套壳 | 457 个 desktop-shell、325 个 launcher | ❌ 红海，官方亲自下场 |
| 整合包（非标准形态） | ≥7 家自造格式 | ❌ "整合包"这个词已不稀缺 |

### 3.2 仍然稀缺（可以下注）

| 空位 | 覆盖 | 保质期风险 | PAD 底子 |
|---|---|---|---|
| **版本库 × known-good × 自动回切 × 跨实例（四者同时）** | **0 家** | 低（没人同时做） | 版本库 + 账本 + dump-config 都有 |
| **跨实例会话总览与迁移** | **0 家**（refyon 只导 zip、DreamRift 只有档案页） | 低 | session-jump M1 planner + surface registry |
| **会话级 deep-link** | **0 家**（官方只有应用级 `dsh://`） | 低 | `pad cli session ref/open --explain` 已可生成稳定 plan |
| pack 投影语义（不进 plugin 栈） | 0 家 | **高**：BEH 自注 PackForge manifest v5"被追平最快的一条" | `PackLedger` + 停用≠卸载已落地 |
| 跨实例/跨版本统一运营视图 | 0 家（DSH-X / dsh-plugins 只有多页） | 中：anywhere-labs 可能补 | 41 个 CLI 命令 + 无头 + 队列 |
| 整合包标准接入（`.dspack` 双向） | 仅 3/30 接入（DSH Manager / HDSL / DSH-X） | — | 已双向兼容，registry PR **未合并** |
| 启动价签（工具/token） | **0 家（双空白）** | — | `inspect` 有数据；**需求未验证，别当卖点** |

**整合包标准的现实数字：** 市场索引 8 个包（7 个 v5 / 1 个 v4），`dshVersions` 0、`launchers` 0；PAD 的 registry PR（`DSH-PackForge#4`、`dsh-pack-market#2`）**仍 open / 未合并**。合并前对外不得声称"已在 registry 中"；接入标准证明的是"不孤岛"，不是"被采纳"。

---

## 4. 故障注入矩阵：现在到底能救什么

| 坏法 | 怎么造 | 现在 | 缺什么 |
|---|---|---|---|
| 端口被占 | 先起一个占 3080 | ✅ 能（换端口/提示） | — |
| 凭据缺失 / 写坏 | 删 `library/credentials.yaml` | ✅ 能（提示 + 设置页） | — |
| 插件不兼容当前内核 | 装 `engines.dsh` 不匹配的包 | ⚠️ 部分（`allow-version --accept-risk` 正门） | 装前门禁 + 自动回滚 |
| profile 依赖坏（junction 断） | 删 `profiles/<p>/node_modules` 里的 symlink | ⚠️ 工具能修（`PnpmHeal`），没有入口 | 编排成"抢救" |
| bundles 行写坏 | 手改 `dsh.profile.bundles` 塞不存在的包 | ⚠️ 能查出（dump-config 失败），不能自动回正 | 打点 + 回滚 |
| 配置写坏（pad.json / instance.json） | 改坏 JSON | ⚠️ 部分（坏 JSON 备份后回默认） | 变更历史 + 回滚 |
| 内核版本坏（钉到坏发行号） | 钉一个坏版本 | ✅ 能（换版本） | known-good 自动回切 |
| 运行中 agent 把 home 改乱 | 让 agent 改自己的配置 / 插件 | ❌ 完全不能 | 运行前后指纹对比 + 打点 |
| 会话文件坏 | 截断 `sessions/*.jsonl.zstd` | ⚠️ 有备份但手动 | 自动打点 |

**读法：** 9 种坏法里"完整能救"3 种、"半救"5 种、"救不了"1 种。把 ⚠️ 和 ❌ 收干净，就是产品承诺的实体。

---

## 5. 候选工作项池

成本口径：S ≤ 1 天，M 2–4 天，L ≥ 1 周（单人 + agent）。

### A 组 · 救（承诺：坏了能救）

**A1 存档点（打点 + known-good + 原地回滚）** — **L**，最高优先
- 存最小差异集：`instance.json` 相关键、`pad.json` 相关键、profile 的 `package.json` + `dsh.profile.bundles`、pack-ledger、内核钉、插件清单。**不存 sessions、不存 node_modules。**
- 打点时机：import 包前、`plugin add/remove/update` 前、换内核前、改 profile 前；另外 run 结束对比 home 指纹，发现 agent 改了就补一个打点。
- known-good 判据（硬）：启动成功 → 首次 ready → 存活 N 秒（默认 60，可配）→ `--dump-config` 退出 0。
- 回滚：`pad cli restore <inst> --to <ckpt|last-good>`；回滚前把当前状态也打成 `pre-restore`，永不丢现场。
- 验收：故障注入矩阵里 5 种 ⚠️/❌ 坏法 → restore 后 `dump-config` 退出 0 且工具数回到基线；证据 `EVIDENCE/D11x-checkpoint/`。
- 风险：home 指纹扫描成本（先只扫 manifest 类文件）；回滚期间并发写。

> **A1 的最小构件（审计已列出，均可挂在现有类上，不重写架构）**
> 1. 变更前钩子：挂 `Launcher.SaveInstance` / `PluginOp` / `RemoveInstance`、`Packs.ApplyPackToInstance` / `UninstallPack`、`Trial.Begin`（骨架复用 `StartJob` / `AppendJobLog`）
> 2. 快照物化：`instances/<id>/checkpoints/<ts>/`，文件集 = instance.json + pack-ledger + trials.json + `profiles/*/package.json` + 投影清单（复用 `CopyTree`；不存 `node_modules`）
> 3. 快照 manifest：`InstanceCheckpoint` + `CheckpointStore`，风格照 `PackLedger.Load/Save`
> 4. known-good 写入点：`LaunchFloat.WaitAlive` 成功分支落 verdict（`{startedAt, readyAt, upMs, exitCode}`），先修退出码 marker bug
> 5. 追加式账本：`instances/<id>/events.jsonl`（照抄 `AppendJobLog` 的重试写法；`jobs.json` 不能复用——会被用户清空）
> 6. 回滚执行器：写回 bundles（`Trial.WriteBundles` 提公共方法）+ 按 diff 补/删插件（`RepairPack` 补缺件已有，缺"删多余"）+ 恢复 instance.json/账本
> 7. 入口：CLI `checkpoint list|create|restore`（`Cli.cs:1293` 帮助文本同步）；UI 最自然落点是启动失败弹窗 `MainWindow.LaunchFailed`（现在只有崩溃分析/打开日志/关闭三个按钮）
> 8. 保留策略：环形保留 N 个 + 列表 UI + 清理（现在 `backups/` 只有一个作者、没有列表）

**A2 变更时间线 + 归因** — **M**
- `instances/<id>/history.jsonl`：`{ts, kind, source(cli/ui/agent), before/after 摘要, ckptId}`。
- UI：实例内页一条时间线，最后一条 good 之后标红。
- 验收：连续 5 次操作后顺序与归因正确；`pad cli history <inst> --json`。

**A3 一键抢救** — **M**
- `pad cli rescue <inst> [--dry-run]`：只读体检（home 结构 / pnpm junction / bundles / dump-config / 端口 / 凭据 / 内核可用性）→ 最小修复（PnpmHeal、重置 bundles 到 good、换内核重试、换端口）→ 输出"改了什么"。
- 验收：4 种 ⚠️ 坏法各一次，rescue 后能启动；救不了时给明确退出码 + 归因，不许静默。
- 注意：复用 `PnpmHeal` / `Trial.WriteBundles` / `PackLedger`，不新写一套。

**A4 崩溃规则 / FAQ 数据层** — **S（工程）+ 持续（内容）**
- 现状 4 条规则 + 4 条 FAQ，太薄（对手 ≥10 家有完整救援面）。目标 20–30 条，规则带 `fix` 字段供 A3 消费。
- 开放社区贡献：registry JSON 已经是数据格式，给一个 `contrib/` 提交路径，把群里的热情变成资产。
- 验收：本机历史日志 20 条样本命中率 ≥ 60%；每条规则有一个可复现的最小日志夹具。

**A5 坏包拦截（装前预检 + 失败自动回滚）** — **S/M**
- import → 打点 → 装 → `--dump-config` + 冒烟启动 → 失败自动回到打点前并报原因。
- `dump-config` 已有，缺"门禁"和"自动回滚"。

### F 组 · 会话（承诺：跨实例找得到、喊得动）

**F1 跨实例会话总览与迁移** — **M/L**（生态 0 家）
- 全局会话视图：所有实例/所有工作区的会话，带项目、最近一轮、状态；点一条 = 切到对应 surface 的对应会话。
- 迁移 = 把一个会话连同 workspace 记录搬到另一个实例（`storages/workspace.json` 三个锚点都要改，`MODPACK-STATE.md` 已写清）。
- 底子：`session-jump` M1 的 `SessionRef` + `SurfaceRegistry` + `JumpPlanner` 可直接复用。
- 验收：跨 2 个实例跳转成功；迁移后原实例不丢会话、新实例能打开；`--explain` 可解释。

**F2 会话级 deep-link（`pad://session/<id>`）** — **M**（生态 0 家）
- 注册协议 → 单实例转发 → 查询 planner 候选 → 打开对应 surface；找不到就明确报"没这个会话"。
- 与 `packagent://`（装包入口）共用一套协议注册与转发。
- 验收：从浏览器 / 群聊点链接能落到正确会话；未知 id 有明确退出码。

### B 组 · 装（承诺：装包不折腾）

**B1 市场策展层** — **M/L**（有就行，不当卖点）
读 PackForge market index（PR #2 已提）+ awesome-dsh 收录，做「包 / 插件」两个 Tab，一键装到某实例。**不自己维护 curation 列表。**

**B2 装前 diff 视图** — **S**
D105 已经算 diff 了，缺"装之前先给人看会动哪些插件 / bundle 行"的 UI。

**B3 `.dspack` 收尾** — **S**
PR 跟进 + 双向覆盖矩阵补全（§11 已有记录，缺回归用例固化）。

### C 组 · 省（承诺：每次启动的代价可见）— **先验证，再开发**

**C1 启动价签** — **S/M，但需求未验证**
启动卡显示：本次工具数 / token / 插件数；与上次对比。**严格守 D14/D15**——只报事实，不报"省多少"。
先做 A4/D3 的演示时顺带问 10 个用户："你在不在乎这个数字？" 再决定要不要进主界面。

**C2 冷启动耗时** — **S**（顺手做，不是卖点）
`runtime.json` 加 `readyMs`；卡片显示；`pad cli status --json` 带出来。

**C3 预设（profile + 工具白名单 + 备注）** — **M**（依赖 C1 是否验证通过）

### D 组 · 叙事与分发

**D1 30 秒演示 + 定位文案** — **S**（非代码，性价比最高）
群里没人知道 PAD 已经能装插件、有账本、能导包。**先补认知，再补功能。** 演示只演"坏了 → 救回来"，不要演装包。

**D2 分享一条链接** — **S/M**
导出实例 / 包 → 生成可分发的链接或文件，对方点开进 PAD（可与 F2 共用协议）。

**D3 便携 kit 自带规则与存档** — **S**
便携包解压即带 crash-rules / faq / 默认预设，收件人第一次启动就能救。

### E 组 · 验收基建（先做，否则 A 组无法证明）

**E1 故障注入套件** — **S/M**
一个脚本造 6 种坏法（端口 / 凭据 / 不兼容插件 / 断 junction / 坏 bundles / 坏内核钉），逐个断言"救得回来"或"救不回来有明确报错"。这是 A1/A3 的证据来源。

**E2 单命令场景复现** — **S**
所有 A/B/C/F 能力都能 `pad cli ...` 一条命令复现，窗口只用来看，不作为唯一证据（沿用 D6）。

---

## 6. 排序与路线

| 时间 | 做什么 | 为什么 |
|---|---|---|
| **本周** | **先修 2 个真 bug**（退出码 marker / `LastProfile` 语义）；D1 文案 + 30 秒演示；A4 规则扩到 20 条（纯数据）；C2 耗时；E1 脚本骨架；F2 用现成 `session ref` 做一个可点的 demo | 认知缺口最大、成本最低；两个 bug 是 A1 的前置；规则库和会话深链都是社区可感知的资产 |
| **两周（0.5.x）** | A1 最小版（手动打点 + `restore` + CLI/UI）；A5 接进 import | 让"救"第一次可演示、可验证 |
| **一月（0.6）** | A2 时间线 + A3 rescue + F1 跨实例会话 + B2 diff UI | 把"救"和"会话"变成可解释的日常能力 |
| **一季（0.7）** | B1 市场策展、C1/C3 按验证结果决定、D2 协议分享 | 分发入口与运营面 |

排序依据：**稀缺度 × 已投入基座 × 可验证性**。A1 三项全高；F1/F2 稀缺度最高但基座偏设计；B1 三项都中；C1 稀缺但需求未验证。

---

## 7. 不做清单

| 不做 | 理由 |
|---|---|
| 内置第二份"稳定 DSH"（PE 内核） | 双倍维护；救不了最常见的 profile 依赖坏；多发行号 + 文件级修复已覆盖 |
| 市场策展运营 | 需要人力；npm 搜索 + PackForge market 已够用 |
| WebView2 / Electron 套壳 | 已有 457 个桌面壳 |
| "多 DSH 并行管理"当主线 | D20 已否；需求密度不够 |
| 自动裁剪工具 / 插件（替人做决定） | 违反 D14；只报价签，不给建议 |
| 报"省了多少 token" | 违反 D15；反事实不可测 |
| 对外说"只有 PAD 做多版本 / 独立 home" | 已被 ≥12 家 / ≥8 家实现；说出去就掉 credibility |

---

## 8. 需要拍板的决策

1. **存档粒度**：最小差异集（推荐）vs 整 home 复制（贵、实现简单）。
2. **打点策略**：只在风险动作前打（推荐）+ run 结束指纹补齐，还是每个动作都打。
3. **known-good 阈值**：存活 60 秒是否够？要不要加"首次成功对话"作为更强判据。
4. **回滚语义**：原地回滚（推荐，保持实例 id/home）+ pre-restore 保底；还是新建实例。
5. **主攻方向**：A 组「回退链」还是 F 组「会话层」先做？（两者都 0 家，A 更可验证、F 更易演示）
6. **C1 价签**：先当假设验证，还是直接进主界面。
7. **对外话术**：是否采用"存档点 / 急救箱"作主标题，"启动器"退为副标题。
8. **PR 状态**：PackForge registry PR 未合并前，任何对外材料不得声称已注册。

---

## 9. 证据与命令（写进验收时用）

```text
pad cli checkpoint <inst> [--label <name>]      # 新增
pad cli restore <inst> --to <ckpt|last-good>    # 新增
pad cli history <inst> [--json]                 # 新增
pad cli rescue <inst> [--dry-run]               # 新增
pad cli session ref <id> --explain --json       # 已有：跳转 plan
pad cli session open <id>                       # 已有
pad cli inspect <home> --json                   # 已有：工具数 / token
pad cli status --json                           # 已有：运行事实
pad cli crash <inst>                            # 已有：崩溃报告
pad cli pack list / import / allow / deny       # 已有
dsh --profile <p> --dump-config                 # 外部校验，退出码可判（D6）
```

新增命令一律先有 CLI + 退出码，再有窗口；窗口只呈现，不作为证据。
