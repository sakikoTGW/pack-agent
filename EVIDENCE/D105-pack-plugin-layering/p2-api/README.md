# P2 API scratch E2E：账本 + 停用联动 + 卸载回滚 + 装包到已有实例

date: 2026-11-04 · owner: p2-core（task-5 C 段验收）· 状态：**28/28 PASS**
设计依据：`agent-pack-dsh/docs/pack-plugin-layering.md` §3.3 动作矩阵 / §6 P2。
约束：**只跑 scratch root**（`.tmp/p2-ledger-e2e/.pack-launcher`，`versions` junction 到 live versions 只读）；
live launcher root 与 live PAD（pid 78308）**全程未碰、未重启**。

## 测了什么

真 DSH 0.2.1-rc.2 + 真 import 链（`pad cli import <pack.zip>`，包 = Probe-Specs-0.1.0，声明
`dsh.plugins[] = [dsh-find-skill(optional, 与内核不兼容), dsh-context(optional)]`）。import 链自己装上了
`dsh-context` 并写账本，然后 harness 依次调用 task-5 C 段的四个 API 并每步读 profile 真相断言：

| 步骤 | API | 断言（全部 PASS） |
|---|---|---|
| baseline | — | import 后 dep+bundle 都在、账本 `EffectiveState=installed`、投影在 |
| 停用 | `Packs.SetPackPluginsActive(false)` | bundle 行摘掉、dependency 保留、投影保留、账本/内容 `allow=false`、`EffectiveState=disabled`、`BundlesRemoved=1` |
| 启用 | `Packs.SetPackPluginsActive(true)` | bundle 行加回（0 下载）、`allow=true`、`EffectiveState=installed`、`Missing=0` |
| 修复 | `Packs.RepairPack` | 无缺件 → no-op，仍 installed，`Added=0 / StillMissing=0` |
| 卸载 | `Packs.UninstallPack` | 回滚独占插件 `dsh-context`（pnpm remove 真跑）、共享跳过=0、删投影、dependency 回滚、账本条目删除 |
| 装到已有实例 | `Packs.ApplyPackToInstance(inst, path, "web", …)` | 投影+allow+管理器+diff add 全链路；`Ok`、`ProjectionId=pack-agent-modpack-probe-specs`、dep+bundle 恢复、账本 installed、title/version、owners 计数=1 |
| 再装一次 | `Packs.ApplyPackToInstance` | **幂等 diff**：`Added=0`、`Kept=[dsh-context]`，不重复 pnpm add |

`dsh-find-skill` 在 0.2.1-rc.2 上被 DSH 判 incompatible：作为**可选**插件失败只进
`PackApplyResult.Failures/Warnings`，不影响 `Ok` 与包状态 —— 与 import 链 `add-plugins` 的
`required:false` 语义一致（必选失败才标 incomplete，P3 的 `allow-version` 通道负责“接受风险”）。

本轮 harness 抓到一个真 bug 并已修：`ApplyPackToInstance` / `SetPackPluginsActive` 一开始拿包文件
stem（如 `Probe-Specs-0.1.0`）去 `Packs.Allow/Deny`，但 pack list / allow 的行 id 是**投影目录名**
（`pack-agent-modpack-probe-specs`），`ResolvePackId` 匹配不上 → 内容启用静默失败（allow 记录成 false）。
修法：`ApplyPackToInstance` 从 `pack project` 输出取投影 id；`SetPackPluginsActive` 从账本
`content.dir` 取投影 id；`PackApplyResult.ProjectionId` 也把这个 id 交给 UI。

## 复跑方式（不写 live root）

```powershell
# 1) scratch root：versions junction 到 live versions；放一个 probe 包
$root = 'E:\pack-agent\.tmp\p2-ledger-e2e\.pack-launcher'
#    （scratch 结构：library/packs/Probe-Specs-0.1.0.pack.zip + instances/ + versions junction）
$env:PACK_LAUNCHER_ROOT = $root; $env:PACK_AGENT_REPO = 'E:\pack-agent'
# 2) 走真 import 链，同时验证 import 写账本
& 'E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\pack-agent-for DSH.exe' cli import "$root\library\packs\Probe-Specs-0.1.0.pack.zip"
# 3) 四个 API 的 E2E harness（源码见 api-e2e-harness.cs / .csproj.txt，临时工程在 .tmp/p2-ledger-harness）
cd E:\pack-agent\.tmp\p2-ledger-harness
dotnet run -c Debug -- $root probe-specs
# 期望末行：HARNESS PASS
```

harness 直接引用 `agent-pack-dsh/pad.tests/pad-bin/pack-agent-for DSH.dll`（即 `dotnet test` 构建出的
PAD 程序集），所以它验的是仓库当前代码，不是某个旧 exe。

## 文件

| 文件 | 内容 |
|---|---|
| `api-e2e-run.log` | 本次完整 stdout/stderr（28 条 PASS + 每步 dsh/pnpm 日志） |
| `api-e2e-harness.cs` | harness 源码（断言列表） |
| `api-e2e-harness.csproj.txt` | harness 工程文件（引用 PAD 程序集的方式） |
| `pack-ledger.after-apply.json` | 最终账本（schema v1，packs+owners，profile installed） |
| `profile-web.after-apply.json` | 最终 profile package.json（dependencies + dsh.profile.bundles） |
| `pad-cli.log` | `pad cli import` 的 CLI 日志（含“账本：已登记 … 1 个插件”） |
