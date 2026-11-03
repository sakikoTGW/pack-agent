# D107 安装概念 · 后端 + CLI 真实 E2E（含卡死根因与修复）

date: 2026-11-04 · owner: p2-core（task-11）· 状态：**PASS（修复后全部数字达标）**
设计/原型：`agent-pack-dsh/docs/proposals/install-concept/`（Lead 已批准）· 决策：D107。
约束：只跑 scratch root `.tmp/d107-install-e2e/.pack-launcher`（`versions` junction 到 live versions 只读）；
**不重启 live PAD（pid 75696）、不写 live root、不 commit**。

---

## 1. 结论与根因（Lead 观察到的“忙循环/卡死”）

Lead 观察到 pid 40608 的 `pad cli pack install x --pack Test-0.1.0 --accept-risk --skip-missing …`：
CPU 840s、无子进程、`--out` 一直没产出。**不是死锁/死循环，是 PAD 自身在反复做 `PnpmHeal.Relink` 的
CPU 重活**：

- `Launcher.AddBundles` / `AllowVersion` 在每次执行 `dsh plugin add` / `allow-version` 前都调用
  `PnpmHeal.Relink(versions/<ver>/node_modules)` + `Relink(profile/node_modules)`。
- 0.2.1-rc.2 的 release `node_modules` 有 **11810 个目录 / 569 个 .pnpm 包目录**；实测一次
  `Relink` = **70.0s / 64.6s**（见 `relink-benchmark.txt`，基准源码同目录）。
- 安装 Test 包要 add 9 个插件 + 1 次 allow-version → 旧代码 ≈ 10 × 65–70s ≈ **650–700s**，
  全部记在 PAD 进程 CPU 上、没有子进程、也没有阶段性输出 —— 与 840s CPU / 无输出完全吻合。
  （日志里能走到 dsh-element-picker，是因为每个插件确实都装成功了，只是每次都先磨 60–70s relink。）
- 之前 `pad cli pack preflight` 正常，是因为预检不碰 relink。

### 修复（`Core/Launcher.cs` + `Core/Packs.cs`）

1. `Launcher.AddBundles(..., bool heal = true)`、`Launcher.AllowVersion(..., bool heal = true)`：
   新增可选 `heal` 参数（默认 true，单插件旧行为不变）；批量路径只在**第一次**做 relink。
2. `Launcher.AllowVersions`：批量豁免里只有第一个 `AllowVersion` 带 `heal:true`。
3. `Packs.ApplyPackToInstance`：内部记录 `healed`，管理器 / 第一个插件 add 之后所有 `AddBundles`
   都走 `heal:false`；`InstallPackToInstance` 在 allow-version 已经 relink 过时，应用阶段传
   `heal:false`（不重复 60–70s）。
4. 可见性：relink 前后与“跳过 relink”都写日志（`检查 pnpm junction…` / `relink 完成（127s）` /
   `跳过 relink（本次批量安装已经检查过 node_modules）`），以后卡住能一眼定位阶段。
5. `Packs.InstallPackToInstance` 增加阶段日志：`[install] 预检完成…` / `[install] 开始应用：N 个插件…`。

修复后：**一次 relink（127s 冷缓存）+ 9 个插件 pnpm add ≈ 221s 总耗时，正常结束并写出报告**。
并发进程/文件锁也用 `cmd /c ... & echo EXITCODE=%ERRORLEVEL%` 复核过，退出码可靠。

---

## 2. CLI E2E 决定性数字（真 DSH 0.2.1-rc.2，scratch root）

`library/packs` 放入：`Test-0.1.0.pack.zip`（10 个 `dsh.plugins[]`）与 `Probe-Compat-0.1.0.pack.json`
（只声明 `dsh-context`，兼容）。实例 x/y/z/w = 手工写 `instance.json` 的 web profile 空实例。

| # | 命令 | 退出码 | 结果（JSON 实测） |
|---|---|---|---|
| a | `pad cli pack preflight x --pack Test-0.1.0 --json` | **0** | `OkCount=8 · ExemptionCount=1 · MissingCount=1 · ConflictCount=0 · UnknownCount=0 · NeedsDecision=true · CanInstall=false`；`Summary=内核匹配 · 插件 10 个：8 可装 · 1 需授权 · 1 已下架/缺包` |
| b | `pad cli pack install x --pack Test-0.1.0 --accept-risk --skip-missing --json`（x 全新） | **0** | `State=incomplete · Ok=true · Added=9 · Kept=0 · Exempted=["dsh-find-skill@0.2.1"] · Skipped=["@yuxianglin/dsh-bridge-browser"] · Failures=0`；`pack-ledger.json` 已写（10 条插件记录 + owners，state=incomplete） |
| b' | 同 b 再跑一次（幂等） | **0** | `State=incomplete · Added=0 · Kept=9 · Exempted=1 · Skipped=1`（不重复下载、不重复 add） |
| c | `pad cli pack install z --pack Probe-Compat-0.1.0 --json` | **0** | `State=installed · Ok=true · Added=1`；账本 + 投影都在 |
| d | `pad cli pack install y --pack Test-0.1.0 --json`（不带 flag） | **2** | `State=needs-decision · Ok=false`；**没写账本、没建投影**（不落盘），Warnings 列出缺的两个决定 |
| e | `pad cli pack install w --pack Test-0.1.0 --accept-risk --skip-missing --dry-run --json` | **0** | `State=dry-run · Ok=true`；**没写账本、没建投影** |

关键证据：
- b 的 `Added=9` = 8 个 ok 插件 + 1 个 accept-risk 豁免后的 `dsh-find-skill`；
- `Skipped` 只有 `@yuxianglin/dsh-bridge-browser`（npm 404）；`Exempted` 只有 `dsh-find-skill@0.2.1`；
- a 中 `dsh-find-skill` 的 `needs-exemption` 依据 = 它的 4 个 `@deepseek-ai/dsh-*` peers
  要求 `^0.1.0-rc.6`，与内核 0.2.1-rc.2 不匹配（DSH `evaluatePluginCompatibility` 同规则）；
- `install-test.json` 后 `instances/x/pack-ledger.json`：`packs["Test-0.1.0"].profiles.web` 10 条插件
  （含被跳过的 bridge-browser，便于「按包修复」）、state=`incomplete`；owners 10 个 key。

人类可读输出（同样走 `--out` 之外的 `Say`，见 `pad-cli.log`）示例：
```
内核匹配 · 插件 10 个：8 可装 · 1 需授权 · 1 已下架/缺包
[install] 开始应用：9 个插件，跳过 1 个
包 Test → profile web：incomplete（不完整，可修复） · 新增 9 · 保留 0 · 豁免 1 · 跳过 1 · 失败 0
```

---

## 3. 复跑方式（不写 live root）

```powershell
# 1) scratch root：versions junction → live versions；library/packs 放 Test-0.1.0.pack.zip 与 Probe-Compat 包
$root = 'E:\pack-agent\.tmp\d107-install-e2e\.pack-launcher'
$env:PACK_LAUNCHER_ROOT = $root; $env:PACK_AGENT_REPO = 'E:\pack-agent'
$exe = 'E:\pack-agent\agent-pack-dsh\pad.tests\pad-bin\pack-agent-for DSH.exe'   # 或 pad/bin 的最新构建
# 2) 预检
& $exe cli pack preflight x --pack Test-0.1.0 --json --out preflight-test.json
# 3) 一次授权安装（Test 10 插件：accept-risk + skip-missing）
& $exe cli pack install x --pack Test-0.1.0 --accept-risk --skip-missing --json --out install-test.json
# 4) 兼容包（installed）
& $exe cli pack install z --pack Probe-Compat-0.1.0 --json --out install-compat-z.json
# 5) 决策门 / dry-run
cmd /c "`"$exe`" cli pack install y --pack Test-0.1.0 --json & echo EXITCODE=%ERRORLEVEL%"   # 期望 2，不落盘
cmd /c "`"$exe`" cli pack install w --pack Test-0.1.0 --accept-risk --skip-missing --dry-run --json & echo EXITCODE=%ERRORLEVEL%"
```
说明：Test 包安装首次要一次 relink（冷缓存实测 127s）+ 9 次 pnpm add + allow-version，总耗时实测
**221.2s**；再次安装走幂等 diff（Kept 9）约 40–60s。`pad-pack.test.ts` 里另有一个**带 90s 硬超时**的
“install 入口不挂起”回归（假内核 + example 包，实测 26.9s 完成、State=installed、账本已写）。

## 4. 文件

| 文件 | 内容 |
|---|---|
| `preflight-test.json` | a：Test 包预检完整 JSON（8/1/1，10 行插件状态） |
| `install-test.json` | b：accept-risk + skip-missing 安装报告（incomplete / 9 added / 1 skipped / 1 exempted） |
| `install-test-rerun.json` + `install-test-rerun.stdout.txt` | b'：幂等重跑报告（Added=0 / Kept=9）与 `EXITCODE=0` 证据 |
| `install-compat-z.json` | c：兼容包安装报告（installed / Added=1） |
| `install-noflag-y.json` | d：不带 flag 的 needs-decision（exit 2） |
| `install-dryrun-w.json` | e：dry-run（exit 0，不落盘） |
| `pack-ledger.x-after-test-install.json` | Test 装完后的账本（10 插件记录 + owners + incomplete） |
| `profile-web.x-after-test-install.json` | Test 装完后的 profile（dependencies + dsh.profile.bundles） |
| `pack-ledger.z-after-compat-install.json` | 兼容包装完后的账本 |
| `pad-cli.log` | 上述命令的 CLI 日志（含人类可读摘要与 relink/跳过日志） |
| `relink-benchmark.txt` / `relink-benchmark.cs.txt` / `relink-benchmark.csproj.txt` | 根因量化：单次 `PnpmHeal.Relink` 70.0s / 64.6s 的基准与源码 |
| `final-gates.txt` | 最终门：`dotnet build pad` 0/0、`dotnet test` 203/203、`import-pack.test.ts` 9✓、`pad-pack.test.ts`（含 install 不挂起回归）35.4s ✓ 的原始输出 |

## 5. 残留风险 / 后续

1. **一次 relink 仍要 60–127s**（0.2.x release node_modules 1.1 万目录）：批量安装已从 N 次降到 1 次；
   若还要更快，后续可改成“一次 `dsh plugin add a b c …` 多 spec”或在 portable 场景跳过 relink
   （有风险，需单独验证）。本次不做，避免破坏 DSH 的 add/reconcile 语义。
2. **预检要打 npm registry**（`npm view …`，并发≤4、15s 超时、进程内缓存）：10 个插件约 10–15s；
   网络失败→`unknown`（不阻塞）。离线环境的预检会全部 unknown，安装仍按 DSH 实际结果处理。
3. `Test-0.1.0` 的 `dsh-find-skill@0.2.1` 豁免只对 0.2.1-rc.2 生效（DSH 官方 allow-version 语义）；
   换内核后需要重新预检/授权。
4. 被 skip 的插件仍写进账本（required、无 addedVersion），所以包一直显示 incomplete 且
   「按包修复」有据可查；等包作者重发或换包版本后修复即可。
5. `pad-pack.test.ts` 新增的 install 回归依赖最新构建产物（需含新 CLI 命令）；老 exe 会以 PA026/未知命令
   失败并给明确提示。该段实测 27–32s（主要花在 repo 的 project/list 子进程），所以测试总预算从 40s
   调到 75s（并发构建时留足余量），当前整包耗时 32–40s。
