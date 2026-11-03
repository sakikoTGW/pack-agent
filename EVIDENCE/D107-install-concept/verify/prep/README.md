# D107「安装整合包」概念 · verifier PREP

状态：**PREP 完成（2026-11-04 04:05），等 Lead "M5 GO" 后 claim/跑门；部署等 deploy-go。**
Owner：verifier（task-13，依赖 task-11/12）。证据根：`EVIDENCE/D107-install-concept/`。
约束：只写 `.tmp` 与 `EVIDENCE/D107-install-concept/`；不改产品源码；不写 live root；不提前重启；不 commit。

## 1. Live / 部署基线（`verify/prep/live-before-m5.txt`）

- live PAD **pid 75696** ALIVE（title PAD，exe `.tmp\pad-build-z\pack-agent-for DSH.exe`，start 03:36:47），`pad.lock`=75696
- 当前部署 dll sha `A765CF9C…`（D106）、exe apphost `C47F9830…`
- `pad-crash.log` 基线：size 21267 / mtime 2026-11-02T16:42:25 / sha `721F9951…`（deploy 后必须无 delta）
- `pad.json` 30AD621B… / `ui-state.json` C7D5BEE4… / `runtime.json` 6FABFA48… / `jobs.json` 50D0726E…（deploy 后哈希不变）
- sisyphus web：runtime 账本 `sisyphus/web pid=61272` ALIVE（部署后仍须 running）
- 部署目标 `.tmp\pad-build-z\`（保留 `.pack-launcher`/`pad-crash.log`/`packmaker.exe`）；
  新 build：`dotnet build agent-pack-dsh\pad\pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-build-d107`；
  重启：`cmd /c E:\pack-agent\.tmp\run-pad-new.cmd`（**绝不能用管道捕获**，D105/D106 教训）。

## 2. 独立 scratch 环境（已建好）

`E:\pack-agent\.tmp\d107-verify\.pack-launcher`：
- `versions` junction → live versions（0.2.1-rc.2）
- `library\packs`：`Test-0.1.0.pack.zip`（真实包：10 插件里 8 可装 / dsh-find-skill@0.2.1 需授权 /
  @yuxianglin/dsh-bridge-browser 已下架）、`Verify-Probe-0.1.0.pack.zip`（兼容 probe：required dsh-context）
- 实例（用当前 D106 build 标准 PAD CLI 造，形状同 PAD 新建）：
  - `inst-empty`：`cli instance create inst-empty 0.2.1-rc.2 --profile web` → status ready、lastProfile=web、
    **profiles 目录还没有**（首次安装需自行 ensure profile —— M5 要验证能否装进空实例）
  - `verify-probe`：`cli import Verify-Probe-0.1.0.pack.zip` → status ready、profile web、账本+投影已就绪
    （用于「已装到当前实例 → 修复」UI 场景和回归）
- `pad.json`：`lastInstance=inst-empty`、`shotDelayMs=12000`、窗口 1040x820（拍 instance-packs 时切 verify-probe）

## 3. Before 基线（当前 D106 build，`verify/baseline/`）

| 截图 | 说明 |
|---|---|
| `01-current-versions.png` | 整合包行有 D106 图标，但右侧只有「新建实例」；**无「安装▾」下拉**、无安装态文字 |
| `02-current-instance-packs.png` | verify-probe 实例页；有已装包行+发光（D106）；「装整合包…」按钮走文件对话框，**无包库选择器/预检** |
| `03-current-pack-picker-unsupported.png` | 当前 build 无 `--shot-page pack-picker` 钩子（落到启动页） |
| `04-current-pack-preflight-unsupported.png` | 当前 build 无 `--shot-page pack-preflight` 钩子（落到 manage 尺寸页） |
| `preflight-probe.out.txt` | 当前 CLI **没有 `pack preflight`**（exit 1 + usage dump）→ 明确 before |

当前 build 连拍同页为静止（D106 时已验证）；D107 新增页面/下拉/预检对话框由新 build 截图验证。

## 4. M5 门（Lead 说 "M5 GO" 后执行）

1. 冻结 `diff_sha`（full + product 子集 .cs/.ts/.xaml/.json），确认稳定；
2. `dotnet build pad -c Debug -o .tmp\pad-build-d107` 0/0；`dotnet test pad.tests`（含新 PackSemver/PackPreflight 测试）；
   bun：pad-window / pad-pack / import-pack / test / pack-catalog / pinst 全绿；
3. 记录新 build dll sha（必须不同于 D106 的 A765CF9C…）。

## 5. CLI 独立 E2E 断言清单（`E:\pack-agent\.tmp\pad-build-d107\pack-agent-for DSH.exe`，
env `PACK_LAUNCHER_ROOT=.tmp\d107-verify\.pack-launcher`、`PACK_AGENT_REPO=E:\pack-agent`）

所有命令在**全新/可重置的 scratch**上跑；每条都记录 exit code + stdout/stderr + 落盘前后哈希快照。
以 task-13 与原型（03-preflight-report）为准的期望：

| # | 命令 | 期望 |
|---|---|---|
| a | `cli pack preflight inst-empty --pack Test-0.1.0 --json --out <f>` | exit 0；`OK=8`、`NeedsExemption=1`（`dsh-find-skill@0.2.1`）、`Missing=1`（`@yuxianglin/dsh-bridge-browser`）、`KernelMatches=true`、`NeedsDecision=true` |
| b | `cli pack install inst-empty --pack Test-0.1.0 --dry-run --json --out <f>` | exit 0；dry-run **无落盘**（ledger/投影/profile/allow 前后哈希一致；实例目录快照对比） |
| c | `cli pack install inst-empty --pack Test-0.1.0 --json --out <f>`（无 flag） | exit **2**（or 契约明示的决策拒绝），**不落盘/不部分装**；人类可读错误提示说明需 accept-risk/skip-missing |
| d | `cli pack install inst-empty --pack Test-0.1.0 --accept-risk --skip-missing --json --out <f>` | exit 0；`State=incomplete`、`Added=9`、`Skipped` 含 `@yuxianglin/dsh-bridge-browser`、`Exempted` 含 `dsh-find-skill@0.2.1`；`pack-ledger.json` 存在且 packs/owners/profiles state 正确；`dsh --dump-config --profile web` 能看到已装插件层；被跳过包标「不完整」 |
| e | `cli pack install verify-probe --pack Verify-Probe-0.1.0 --accept-risk --skip-missing --json`（或兼容包路径） | exit 0；`State=installed`、失败=0 |
| f | 人类可读输出（不带 --json）：`cli pack install ... --accept-risk --skip-missing` | 一行摘要可读（如「已安装/不完整 N 个…」），不是 JSON |
| g | 回归：`cli pack list --json --out`、`cli instance list --json` | 结构未被破坏（避免 D104-D106 回归） |

补充断言：preflight `NeedsDecision=false` 的兼容包（Verify-Probe）直接 `install --json`（无 flag）应 exit 0/installed；
两次 dry-run 幂等；失败路径不得写 ledger/投影。

## 6. UI 截图核对清单（新 build + scratch root；对照 `agent-pack-dsh\docs\proposals\install-concept\`）

用 `--shot-page versions` / `instance-packs` / `pack-picker` / `pack-preflight`（**实现若用别的页名，
先读 MainWindow/InstanceView ShotPage 分支再适配**；当前 build 无这些钩子已记录基线）。

| 页面 | 对照原型 | 核对点 |
|---|---|---|
| versions | `01-versions.png` | 每个包行有「**安装 ▾**」主按钮（不再是只有新建实例）；下拉三项：**装到当前实例（<lastInstance>）/ 新建实例… / 先检查能不能装…**；默认目标=当前选中实例；仍显示包图标（D106）且**不显示装到哪文字**（D106 约定） |
| instance-packs | `02-instance-picker.png` | 「装整合包…」打开**本机包库选择器**（搜索/计数/来自文件）；每行预检徽标：`N 个可装` / `N 个需授权` / `N 个已下架` / 已装到当前实例「缺 N 个·修复」；按钮文案：检查并安装 / 安装 / 修复；包行发光保留（D106） |
| pack-picker | `02-instance-picker.png` | 同上的独立离屏截图；目标实例写在标题（「目标实例：verify-probe」等） |
| pack-preflight | `03-preflight-report.png` | **两阶段**：阶段1「安装前检查」：内核匹配通过、插件 10 个=8 可装/1 需授权/1 已下架、dsh-find-skill@0.2.1 需授权行、@yuxianglin/dsh-bridge-browser 已下架行、版本冲突「无」、两个勾选（接受风险写 allow-version / 跳过下架标不完整）、取消/开始安装；阶段2「安装报告」：徽标已装/不完整·缺1、计数器 **9 新增 / 0 保留 / 1 风险豁免 / 1 跳过 / 0 失败**、跳过原因行、「实例里其它内容没有被动过」、按钮 重新导出这个包/打开实例/去修复缺少项 |
| 徽标一致性 | CLI a/d 输出 | UI 预检徽标数字必须与 CLI preflight/install 结果一致（8/1/1；报告 9/0/1/1/0） |
| 回归 D104 | 之前证据 | versions 容器行：只有 PAD 启动的 web 行有呼吸灯；官方/第三方/探测 TUI 不得有灯（源断言 + 视觉） |
| 回归 D106 | 之前证据 | 包行自定义 icon/回落「包」；版本页无装到哪文字；实例页 enabled 发光（像素/对照） |
| 布局 | 原型 | 无控件重叠/溢出/截断；下拉/对话框离屏截图完整（必要时调窗口高度） |

## 7. 部署（Lead 明确 deploy-go 后；沿用 D105/D106 模板）

产物 `.tmp\pad-build-d107\*`；当前 live pid **75696**（lock 75696）。
记录 before 哈希 → Stop-Process → 复制覆盖 `.tmp\pad-build-z\`（保留 `.pack-launcher`/`pad-crash.log`/`packmaker.exe`）
→ 校验部署后 dll sha → `cmd /c E:\pack-agent\.tmp\run-pad-new.cmd`（**不捕获管道**）→ 新 pid；
post-check：新 pid≠75696 & path build-z；pad-crash.log 无 delta；pad.json/ui-state/runtime/jobs 哈希不变；
sisyphus web pid 61272 alive + `cli status` Running；live `--shot-page versions --shot-exit` EXIT 0 且渲染正常；
pad.lock=新 pid；证据 `EVIDENCE/D107-install-concept/verify/deploy/`。

## 8. 风险 / 注意

- `inst-empty` 没有 profiles 目录（PAD 新建实例的真实形状）：安装/预检必须能自行 ensure profile，否则按契约
  明确报错；若 M5 实现要求先有 profile，这是契约差异，如实报 FAIL/风险而不是自己改产品。
- 截断/下架/授权数字依赖 Test 包真实内容与 npm registry 状态（`@yuxianglin/dsh-bridge-browser` 已下架 404），
  若网络/registry 行为变化会在证据里注明。
- `--shot-page pack-*` 页名/交互由实现决定；先读代码适配（当前 build 无钩子已在 §3 记录）。
- 部署命令不要管道捕获；不要 kill 后台 job 时连带误杀 PAD（D105 曾发生）。
- 原型允许实现差异（不要求像素一致），但不能出现坏体验（按钮点不动、文案矛盾、布局崩坏）。
