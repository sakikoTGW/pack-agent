# D104 · PAD 整合包「装不上 + 灯乱亮」修复验证（verifier 证据）

状态：**修复后验证 PASS（§6）；已按 Lead deploy-go 部署上线（§7：新 pid 78308，2026-11-03 23:40），task-3 完成。**
Owner：verifier（task-3）。仓库：`E:\pack-agent`。证据目录：`EVIDENCE/D104-pad-pack-lamp-import/`。
机器可核对摘要：`fixed/SUMMARY.txt`；基线：本文 §0–§5；修复后：§6；部署：§7。

## 0. 基线对象（修复前二进制，勿覆盖前先记录）

| 项 | 值 |
|---|---|
| 二进制 | `E:\pack-agent\.tmp\pad-build-z\pack-agent-for DSH.exe` |
| SHA256 | `F1ACF2FB0DB92C255BA108795A7067C2FD8462AAE6707C42028760AC712B9BC2` |
| mtime / size | 2026-11-03 17:15:01 / 336384 |
| 运行中 PAD | pid **19264**（同一 exe，env 由 `.tmp/run-pad-new.cmd` 设置；基线期间**未杀、未重启**） |
| live launcher root | `agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher` |
| 公共 env | `PACK_LAUNCHER_ROOT=<root>`、`PACK_AGENT_REPO=E:\pack-agent` |

> 所有 `cli` / `--shot` 调用都是 `WinExe` GUI 子系统程序：PowerShell 里必须
> `Start-Process -Wait -PassThru -NoNewWindow` 才会等它结束并拿到退出码；直接 `& exe` 不等待。

## 1. Bug A 基线：容器列表呼吸灯

`cli discover --json --out baseline/discover-baseline.json`（exit 0）：

| 行 | Exe | Running | Pid | Found |
|---|---|---|---|---|
| 官方壳 | `E:\DSH-App\DeepSeek Harness.exe` | **true** | 71052 | running process |
| 第三方壳 | `E:\新建文件夹 (5)\DSH Desktop\DSH Desktop.exe` | **true** | 32280 | running process |

`Tuis`(4) 全部 `Running=false`。PAD 账本 `runtime.json` 只有一条：
`instance=sisyphus profile=web pid=67008`（≠ 71052/32280，即两个桌面端都**不是** PAD 启动的）。

离屏截图（`--shot <png> --shot-page versions --shot-exit`，跳过单实例锁，不碰运行窗口）：

| 截图 | root | 容器行 | 绿灯数（像素 blob 实测） |
|---|---|---|---|
| `baseline/00-baseline-versions.png` | live root（3.2s 截图延迟，探测未完成） | 仅实例 123 profile（dsh-tui） | 0 |
| `baseline/01-baseline-versions-lamps.png` | live 状态镜像 root（首行实例 123 被选中） | dsh-tui + 官方 + 第三方 | **2**（官方+第三方亮） |
| `baseline/02-baseline-lamp-web-plus-shells.png` | 聚焦镜像 root（sisyphus 被选中） | web + 官方 + 第三方 | **3**（web+官方+第三方全亮） |
| `baseline/03-baseline-ui-mirror.png` | **验证镜像 root**（`.tmp\verify-d104-ui`，见下） | web + 官方 + 第三方 | **3**；同图整合包行无已装/未装文字 |

结论：`02`/`03` 复现 Bug A —— 三行容器（只有 sisyphus/web 是 PAD 启动）全部亮呼吸绿灯。
`03` 同时是修复后的**同镜对比图**（同一个 root 修复后再截一次，预期只剩 1 盏 web 灯 +
整合包行出现「已装到 pack-Test」）。

> 验证镜像 root `.tmp\verify-d104-ui\.pack-launcher` 的构造（只读派生，live root 零写入）：
> `pad.json`（live 复制，`other.shotDelayMs=12000`、`lastInstance=sisyphus`）+ `runtime.json`
> （live 复制，含 sisyphus/web pid 67008 账本）+ `instances\sisyphus\instance.json`
> （**display name 改为 `aaa-sisyphus`**，id 仍为 sisyphus，让实例列表按 Name 排序后它排第一被选中）
> + `instances\pack-test\instance.json`（原样复制，供「installed id→实例名」映射出 `pack-Test`）
> + `library\packs\Test-0.1.0.pack.zip` + `versions` junction 到 live versions。
> 旧二进制的实例列表 `SelectedItem` 会被 CollectionView 当前项同步成第一项，所以 live root 的
> `--shot` 永远选中 `123`、看不到 web 行；镜像改名只是让截图选中 PAD 账本里的 web profile，
> 灯语义本身不变（`MarkRunning` 按 `Instance.Id` 匹配账本）。
客观计数脚本：`.tmp\lamp-analyze.py`（`#3FBF73` 呼吸灯与浅底混合后的绿色族检测），输出 `baseline/lamp-analysis.txt`。

**镜像 root 说明**：直接对 live root 截图时，`--shot-page versions` 会等 3.2s
（`pad.json other.shotDelayMs`）后渲染，而 `ScanDetectedAsync` 探测未完成 ⇒ 桌面端/TUI 行
根本没出现（`00` 只有 0 灯）。为了拿到"灯"的基线证据，用**只读派生**的镜像
`.pack-launcher`（`pad.json`（把 `shotDelayMs` 调大到 12000）+ 复制的 `runtime.json` /
`instances/*/instance.json`（home 仍指向原路径，只读）+ `library/packs` + `versions`
junction 到 live versions），live root 本身**零写入**。截图进程同样只读。
另：旧二进制的实例列表 `SelectedItem` 会被 CollectionView 当前项同步成第一项（`123`），
所以"web 灯"证据用只含 sisyphus 实例的聚焦镜像 root（`02`）；这不属于本次修复范围，记录备查。

## 2. Bug B 基线：整合包装不上 + 无已装状态

### 2a. catalog（`cli pack list --json --out baseline/packlist-baseline.json`，exit 0，9 行）

Test 包两行（`E:\pack-agent\.tmp\pack-core-inspect\Test.pack.zip` 是队友临时产物，drive 扫描带出）：

- `id=Test-0.1.0 source=library installed=[pack-test] components={plugin:10,...}`
  path=`<live root>\library\packs\Test-0.1.0.pack.zip`
- `id=Test source=drive installed=[]`

即 catalog 已带 `installed:["pack-test"]`，但修复前 UI 行只有「Test 0.1.0 / 0.2.1-rc.2 · 插件 10 / 新建实例」，
**没有任何"已装到/未安装"字样**（见 `00-baseline-versions.png` 整合包卡片）。

### 2b. 真实失败复现（scratch root，未碰 live）

`E:\pack-agent\.tmp\verify-d104\.pack-launcher`：`library\packs` 复制
`Test-0.1.0.pack.zip`；`versions` junction 到 live versions。命令：

```
PACK_LAUNCHER_ROOT=E:\pack-agent\.tmp\verify-d104\.pack-launcher
PACK_AGENT_REPO=E:\pack-agent
<exe> cli instance create --pack Test-0.1.0 --name verify-baseline
```

结果 **exit=1**，`baseline/baseline-instance-create.stdout.txt`：

```
-- 23:12:47 pad cli instance create --pack Test-0.1.0 --name verify-baseline
pack Test has no mods; a modpack must ship at least one skill, MCP, plugin, rule, command, or hook
error[PA028]: the projection command failed
 --> packagent dsh launcher pack project verify-baseline "...\library\packs\Test-0.1.0.pack.zip"
  = help: 看上面前面的输出
```

失败后落盘状态（`baseline/baseline-instance.json` + 目录枚举）：

- `instance.json` 已写入：`source.kind=pack, source.packId=Test-0.1.0, source.packVersion=0.1.0`；
- 投影目录只有 `.agent-pack\modpacks\pack-agent-modpack-test\preset.yml`（41 B），
  **没有 `mods/`、没有 plugin-*.json**（"半成品实例"）；
- 包内只有 `pack.json`+`icon.png`；`pack.json` = `ccui-pack/v0.2`，只有 `dsh.plugins[10]` specs
  （无 skill/mcp/rule/command/hook）→ 旧 TS 编译器不把 spec-only `dsh.plugins` 当 mod，直接 `has no mods`。
  证据：`baseline/Test-0.1.0.pack.json`、job 日志 `...verify-d104...\jobs\project-*.log`。

## 3. 预存在红：pad-window.test.ts 在 HEAD 就是红的（与本次修复无关）

Lead 指明 HEAD 的 `pad-window.test.ts` 有两条过期 DownloadView 断言（DownloadView.xaml 在
fa8b6a2/d845499/466b7ee 重构过），pad-ui 只被授权 rebase 这两条。独立证明（**不改主工作树**）：

```
git worktree add E:\pack-agent\.tmp\pad-head-worktree HEAD   # HEAD 54cf34ca419e918e109a9b0f926011d8570d24aa
cd E:\pack-agent\.tmp\pad-head-worktree
bun run agent-pack-dsh/modpack/pad-window.test.ts             # exit=1
```

失败断言（HEAD 文件第 524 行）：
`if (read('Views/DownloadView.xaml').includes('关键词')) fail('搜索标签还在用关键词')`
→ 旧断言要求占位符还叫「关键词」，而现 DownloadView 已改成新搜索卡片。

对照：**当前工作树**（pad-ui 已 rebase 后）同一命令 `exit=0`，新增用例
`✓ 运行灯：三态判定 / 左栏呼吸 / cli status`。日志：
`baseline/pad-window-test-HEAD.log`、`baseline/pad-window-test-HEAD.stdout.txt`、
`baseline/pad-window-test-HEAD.stderr.txt`、`baseline/pad-window-test-WORKTREE.*`。
结论：HEAD 红是预存在的过期断言，不算产品回归；修复后应只剩 pad-ui 授权的 rebase 差异。

## 4. 验证 harness（修复落地后执行的确切命令）

从 `E:\pack-agent` 执行：

1. **定向 TS 测试**（逐个跑，记录 exit/log）：
   `bun run agent-pack-dsh/modpack/import-pack.test.ts`
   `bun run agent-pack-dsh/modpack/pack-catalog.test.ts`
   `bun run agent-pack-dsh/modpack/test.ts`
   `bun run agent-pack-dsh/modpack/pad-window.test.ts`
   `bun run agent-pack-dsh/modpack/pad-pack.test.ts`
2. **.NET 测试**：`dotnet test agent-pack-dsh/pad.tests/Pad.Tests.csproj`
3. **构建到全新目录**（不锁运行中 exe）：
   `dotnet build agent-pack-dsh/pad/pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-build-d104`
   产物：`E:\pack-agent\.tmp\pad-build-d104\pack-agent-for DSH.exe`（AssemblyName 已确认）。
4. **scratch E2E（NEW build）**：新建 `E:\pack-agent\.tmp\verify-d104-e2e\.pack-launcher`
   （`library\packs` 复制 Test 包 + `versions` junction 到 live versions），
   `PACK_LAUNCHER_ROOT=<scratch> PACK_AGENT_REPO=E:\pack-agent` 下：
   `cli instance create --pack Test-0.1.0 --name verify-pack`（exit 0、无 "has no mods"）；
   断言 `instance.json` `source.packId=Test-0.1.0`、投影 `mods/plugin-*.json` 存在（目标 10 个）、
   实例 profile 真装上了包声明的插件；再 `cli pack list --json --out` 断言 Test 行
   `installed` 含新实例 id。（真 npm 安装可能数分钟 → 后台 job + 日志。）
5. **离屏 UI 截图（NEW build）**：
   `--shot <png> --shot-page versions --shot-exit`（`PACK_LAUNCHER_ROOT=<root>`、`PACK_AGENT_REPO=E:\pack-agent`，Start-Process -Wait）：
   - **同镜对比**：对 `.tmp\verify-d104-ui\.pack-launcher` 再截一张（与 `03` 完全同 root）：
     断言**恰好 1 盏灯**（web 行；官方/第三方行无灯）、整合包行出现「已装到 pack-Test」；
   - **live root 只读截图**：断言官方/第三方/探测 TUI 行**不得**有绿灯（注意 3.2s 截图延迟可能
     赶不上探测，若行没出现则以镜像图为准并记录）；
   - 用 `.tmp\lamp-analyze.py` 客观计数（预期 `03` 修复图 `lamps=1`），并人工看图核对文字。
6. **部署（需 Lead 明确批准后才做）**：
   - 把 `pad-build-d104` 的产物覆盖到 `E:\pack-agent\.tmp\pad-build-z\`（保留其 `.pack-launcher`）；
   - `Stop-Process -Id 19264`；`E:\pack-agent\.tmp\run-pad-new.cmd` 重启；等窗口；
   - 断言新 pid ≠ 19264、`pad-build-z\pad-crash.log` 无新增、live root `--shot` 复验修复效果；
   - **不碰** sisyphus web 实例（勿 stop）。

## 5. 基线结论

- Bug A 可复现：探测到 OS 在跑的 2 个桌面端被当 PAD 运行态 → 容器行亮灯（`02` 三灯）。
- Bug B 可复现：spec-only `dsh.plugins` 包 → TS `pack Test has no mods` / PA028，实例半成品；
  UI 无已装/未装状态。
- HEAD `pad-window.test.ts` 红为预存在过期断言（已用干净 worktree 证明），不是产品回归。

## 6. 修复后验证（PASS，部署前快照）

**源码快照**：HEAD `54cf34ca419e918e109a9b0f926011d8570d24aa`，working tree diff_sha
`e9b4752c…`，10 个修改文件（pack-core 4 + pad-ui 4 + Lead 的 `MakePackView.xaml.cs` /
`pad-window.test.ts`）；`fixed/source-snapshot.txt`。

**新构建**：`dotnet build agent-pack-dsh/pad/pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-build-d104`
→ exit 0，0 warnings / 0 errors（5.7s）；
产物 `E:\pack-agent\.tmp\pad-build-d104\pack-agent-for DSH.exe` SHA256 `C47F9830866D1EC4…`。
日志 `fixed/logs/build-pad-d104.log`。运行中的 live PAD（pid 19264）全程未动。

### 6.1 测试（全绿）

| 命令 | 结果 |
|---|---|
| `bun run agent-pack-dsh/modpack/import-pack.test.ts` | exit 0（6.1s） |
| `bun run agent-pack-dsh/modpack/pack-catalog.test.ts` | exit 0（20.3s） |
| `bun run agent-pack-dsh/modpack/test.ts` | exit 0（1.1s） |
| `bun run agent-pack-dsh/modpack/pad-window.test.ts` | exit 0（5.1s，含 D104 三组新断言） |
| `bun run agent-pack-dsh/modpack/pad-pack.test.ts` | exit 0（4.1s） |
| `dotnet test agent-pack-dsh/pad.tests/Pad.Tests.csproj` | exit 0，**67 passed / 0 failed** |

证据：`fixed/tests-summary.txt`、`fixed/logs/*.out.txt`、`fixed/logs/dotnet-test-pad.tests.log`。
`pad-window.test.ts` 新增断言覆盖：探测行只写 `LocalRunning` 不点灯（含 MakePack 页）、
`PackItem.InstallStateText`、`import-failed` 不算已装、PA014 翻人话、`Packs.ImportPack` 接线。

### 6.2 Bug A：容器绿灯（PASS）

| 截图 | 内容（客观 blob 计数） |
|---|---|
| `baseline/03-baseline-ui-mirror.png`（旧二进制同镜） | **3 灯**：web + 官方 + 第三方 |
| `fixed/shots/11-fixed-ui-mirror-tall.png`（新二进制同镜，4 行全可见） | **1 灯**：web；官方/第三方/TUI 均无灯 |
| `fixed/shots/14-fixed-live-root.png`（live root 只读） | **0 灯**（终端/官方/第三方行都渲染出来了） |
| `fixed/shots/13c-new-makepack-retry.png`（导出整合包页） | **0 灯**；旧二进制同页 `13b` = **2 灯** |

计数脚本 `.tmp/lamp-analyze.py`（按呼吸灯渲染色族检测），输出见 `baseline/lamp-analysis.txt`、
`fixed/shots/lamp-analysis-*.txt`。live root 的 `--shot` 有 3.2s 延迟与异步探测竞争的已知限制，
严谨对比用同一份实例/账本数据派生的只读镜像 root（`shotDelayMs=12000`）。

### 6.3 Bug B1：整合包已装态（PASS）

- `fixed/shots/11-fixed-ui-mirror-tall.png`：Test 0.1.0 → 绿色药丸 **「已装到 pack-Test」**；
- `fixed/shots/14-fixed-live-root.png`：live root 同样 **「已装到 pack-Test」**；
- `fixed/shots/12-fixed-scratch-pills.png`（scratch E2E root，三种状态一图齐）：
  - Test 0.1.0 → **「已装到 verify-legacy；已建实例（上次没装完）Test」**（ready 与 import-failed 分开）
  - Verify Probe 0.1.0 → **「已装到 verify-importpack、Verify Probe」**
  - Verify Uninstalled 0.1.1 → 灰色 **「未安装」**

### 6.4 Bug B2：可安装性（按 Lead 调整后的口径 PASS）

scratch root `E:\pack-agent\.tmp\verify-d104-e2e\.pack-launcher`（versions junction 到 live，
未写 live root；日志 `fixed/e2e/*`）：

| 场景 | 命令 | 结果 |
|---|---|---|
| A. CLI 旧投影路径 | `cli instance create --pack Test-0.1.0 --name verify-legacy` | **exit 0**，无 "has no mods"；status=ready，`source.packId=Test-0.1.0`，投影 10 个 mod（20 文件） |
| B. Test 包完整 import 链 | `cli import Test-0.1.0.pack.zip` | **不再 "has no mods"**；投影 10 个 plugin.json、`source.packId=Test-0.1.0`；随后在 `dsh plugin add` **honest PA014**：`dsh-find-skill@0.2.1 is incompatible with dsh 0.2.1-rc.2 (requirements ^0.1.0-rc.6)`；实例 status=import-failed 留盘（包内容不兼容，非代码修复 FAIL） |
| C. 兼容探针包（verifier 自造 Verify-Probe，required `dsh-context`） | `cli import Verify-Probe-0.1.0.pack.zip` | **exit 0**（23.3s），id=verify-probe **status=ready**，`source.packId=Verify-Probe-0.1.0`；profile `dependencies dsh-context ^0.79.0`、bundles 含 dsh-context、`node_modules/dsh-context@0.79.0`（真 npm 装好）；投影 1 个 plugin mod |
| D. UI 路径 `Packs.ImportPack`（用最终源码编译的 probe exe，同 UI 按钮调用的公共 API） | probe `Verify-Probe` + `--name verify-importpack` | **status=ready**，`source.packId=Verify-Probe-0.1.0`，elapsedMs=22849，captured lines=42，profile bundles 含 dsh-context |
| E. 目录 installed 索引 | `cli pack list --json --out` | Test-0.1.0 `installed=[test,verify-legacy]`；Verify-Probe `installed=[verify-importpack,verify-probe]`；Verify-Uninstalled `installed=[]` |

补充：`VersionsView.PackCompatLine` 把 PA014/incompatible 翻成「这个整合包里的插件与它钉的内核不兼容…」
（由 `pad-window.test.ts` 断言 + 代码检查覆盖；Toast 无法离屏截图，未做像素级证据）。

### 6.5 HEAD 预存在红 + live 完整性（**部署前**快照）

- `.tmp\pad-head-worktree` @HEAD：`pad-window.test.ts` exit 1（过期断言）；
  当前工作树 rebase 后 exit 0 → 预存在、非回归（`baseline/pad-window-test-HEAD.log`）。
- 部署前：live PAD **pid 19264 仍存活**（start 17:15:11），`pad-build-z` 未改、
  `pad-crash.log` 未新增（mtime 仍 2026-11-02）。→ 随后已在 §7 部署（pid 78308）。
- 已知无害：一个已退出的僵尸进程对象 pid 49748（HandleCount=0/Threads 空）残留，不持有 live
  `pad.lock`（部署前=19264，部署后=78308），未处理。

### 6.6 部署（等 Lead deploy-go）

计划（**已执行，见 §7**）：把 `pad-build-d104` 产物覆盖到 `E:\pack-agent\.tmp\pad-build-z\`
（保留 `.pack-launcher`）→ `Stop-Process -Id 19264` → `E:\pack-agent\.tmp\run-pad-new.cmd` 重启 →
断言新 pid、`pad-crash.log` 无新增、live root `--shot` 复验 → 记录部署后证据。**不碰** sisyphus web 实例。

## 7. 部署结果（POST-DEPLOY，Lead deploy-go 后执行）

时间：2026-11-03 23:40:13 → 23:41（Asia/Shanghai）。证据：`fixed/deploy/`。

| 项 | 结果 |
|---|---|
| 覆盖产物 | `.tmp\pad-build-d104\*` → `.tmp\pad-build-z\`（`.pack-launcher`、`pad-crash.log`、`packmaker.exe` 保留） |
| 部署后 exe | `E:\pack-agent\.tmp\pad-build-z\pack-agent-for DSH.exe` SHA256 **`C47F9830866D1EC418132CC2FE4124CA6CB9EC14A8821AEF2AA13B1CAAA77E55`**（= 预期；dll 也与 d104 一致） |
| 旧进程 | pid 19264 已停止（0.3s 内退出） |
| 新进程 | **pid 78308**，start 2026-11-03 23:40:27，exe = `.tmp\pad-build-z\pack-agent-for DSH.exe`，窗口标题 `PAD`（窗口保留给用户） |
| single-instance | live root `pad.lock` 内容 = `78308` |
| pad-crash.log | 部署前后完全一致：size 21267 / mtime 2026-11-02 16:42:25 / sha256 `721F99…` → **无新增**；截图后再查仍一致 |
| 设置文件 | `pad.json` / `ui-state.json` / `runtime.json` sha256 前后一致（未被改写） |
| sisyphus web | runtime 账本 `sisyphus/web pid=67008` 仍存活；`cli status --json` 显示 `sisyphus Phase=Running`（**未停止**） |
| 部署后 live 截图 | `fixed/shots/15-deployed-live-versions.png`：exit 0，整合包卡片有内容、Test 行药丸 **「已装到 pack-Test」**、无崩溃；lamp 计数 **0**（这次 3.2s 延迟下只剩所选实例 123 的 dsh-tui 行，检测行未赶上；1 盏灯的严谨证明仍用已验收的镜像图 `11`） |
| 进程存活复验 | 截图后 pid 78308 仍 ALIVE（title=PAD） |

**残留 caveat（给最终用户消息）**：
- live root 的 `Test-0.1.0` 包内插件（`dsh-find-skill@0.2.1` 等）与它钉的 DSH `0.2.1-rc.2` 不兼容，
  点「新建实例」会在真装插件阶段以中文提示「插件与它钉的内核不兼容…」（PA014 原文）失败；
  这是**包内容/版本问题**，以前被误报成 "has no mods"。要成功安装需按当前内核重新导出该包
  （或换兼容插件版本）。已用兼容探针包端到端证明 import 链本身是好的。
- `run-pad-new.cmd` 里中文注释是 GBK 编码，重定向 stderr 会看到一条无意义 cmd 报错；脚本本身正常启动。
- 已退出的僵尸进程对象 pid 49748 仍在（HandleCount=0），不持有 live `pad.lock`（=78308），无害、未处理。
- 基线期间 live PAD pid 19264 未重启、live root 未写入（镜像 root 只读）。
