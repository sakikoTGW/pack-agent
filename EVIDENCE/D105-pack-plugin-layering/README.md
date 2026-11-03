# D105 · 整合包 × 插件分层（P1/P2/P3）独立验证

状态：**D105：PREP/M1/M2/M3 全 PASS，已部署（§12.1，pid 81852），task-8 完成。D106 M4（整包 icon / 去装态文字 / 已启用包发光）：PASS（§13），已部署上线（§13.3：新 pid 75696，2026-11-04 03:36），task-10 完成。**
Owner：verifier（task-8，已 claim；dep task-4/5/6 均完成）。仓库：`E:\pack-agent`。证据目录：`EVIDENCE/D105-pack-plugin-layering/`。
设计依据：`agent-pack-dsh/docs/pack-plugin-layering.md` + `DECISIONS.md` D105（**只读，不改**）。

## 0. 范围与硬约束

- verifier **不写产品源码**，只写 `.tmp/**` scratch 与 `EVIDENCE/D105-pack-plugin-layering/**`。
- **不写 live launcher root**（`agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher`，只读/截图/junction 引用）。
- **不重启 live PAD（pid 78308）**，直到 Lead 明确 deploy-go；`--shot` 跳过单实例锁，可安全离屏。
- 不 git commit。执行者自测不算数：每个里程碑都独立 build + 独立跑测试 + 独立截图核对。
- 长命令（npm 安装 / 构建）用后台 job；报告必须给决定性数字与文件路径，FAIL 原样报。

## 1. Scratch 环境（已就绪，可重复使用）

根目录：`E:\pack-agent\.tmp\d105-verify\.pack-launcher`

| 项 | 内容 |
|---|---|
| `versions` | junction → live root `versions`（0.1.0-rc.7 / 0.1.1-rc.2 / **0.2.1-rc.2**，只读引用） |
| `library\packs` | `Verify-Probe-0.1.0.pack.zip`（sha256 `CC287D54…`，1 个 required spec-only 插件 `dsh-context`）、`Shared-A-0.1.0.pack.zip`（`00B365AD…`）、`Shared-B-0.1.0.pack.zip`（`A67ED0A7…`）——A/B 都声明 `dsh-context`，供 M2c「共享插件」引用计数 |
| `instances\p1-full` | 由 **当前已部署 D104 二进制**（`.tmp\pad-build-z`）走 TS import 链装好：status=ready、source.packId=Verify-Probe-0.1.0、profile web 的 bundles+deps 都含 `dsh-context`（M1 截图「插件都在」的正例） |
| `instances\p1-missing` | 同上导入后**手工把 `dsh-context` 从 profile `dsh.profile.bundles` 摘掉（dependencies 保留）**，模拟「包要求但 profile 没在加载」→ M1 截图「M 个不在这个 profile」正例 |
| `pad.json` | live 复制后改：`lastInstance=p1-full`、`shotDelayMs=12000`、窗口 1040x820（离屏截图等探测完成） |
| live 只读基线 | `prep/live-root-before.txt`（pad.json/ui-state/runtime/jobs/pad.lock 哈希、instances 名单、library/versions、pid 78308 alive） |

fixture 生成脚本：`.tmp\d105-verify\make-fixtures.ts`（`src/pack-archive.ts` 的 `writePackZip`）。
预装日志：`prep/import-p1-full.*.txt`（exit 0，23.3s）、`prep/import-p1-missing.*.txt`（exit 0，15.2s）。
说明：预装用 D104 二进制只为**造数据**；所有里程碑断言都用**当次新 build** 验证，不采信 D104 二进制行为。

## 2. 构建 / 测试命令（每个里程碑都重跑）

```powershell
cd E:\pack-agent
# 构建（全新目录，不锁 live exe；记录 exit/警告/产物 sha256）
dotnet build agent-pack-dsh\pad\pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-build-d105
# 里程碑测试（M1 至少前 5 条；M2 加 dotnet test；M3 加 P3 新增）
bun run agent-pack-dsh/modpack/pad-window.test.ts
bun run agent-pack-dsh/modpack/pad-pack.test.ts
bun run agent-pack-dsh/modpack/import-pack.test.ts
bun run agent-pack-dsh/modpack/test.ts
bun run agent-pack-dsh/modpack/pack-catalog.test.ts
dotnet test agent-pack-dsh/pad.tests/Pad.Tests.csproj
```

CLI / 截图统一 env：`PACK_LAUNCHER_ROOT=E:\pack-agent\.tmp\d105-verify\.pack-launcher`、
`PACK_AGENT_REPO=E:\pack-agent`。GUI 子系统 exe 必须 `Start-Process -Wait -PassThru`（直接 `&` 不等待）。
新 build 产物：`.tmp\pad-build-d105\pack-agent-for DSH.exe`（AssemblyName=pack-agent-for DSH，net9.0-windows WinExe，已确认）。
**不要**覆盖 `.tmp\pad-build-z\`（live 部署目标）直到 deploy-go。

## 3. 截图命令与「当前二进制」基线（已记录）

```powershell
$exe='E:\pack-agent\.tmp\pad-build-d105\pack-agent-for DSH.exe'   # 里程碑时用新 build
$env:PACK_LAUNCHER_ROOT='E:\pack-agent\.tmp\d105-verify\.pack-launcher'
$env:PACK_AGENT_REPO='E:\pack-agent'
Start-Process -FilePath $exe -ArgumentList @('--shot',$png,'--shot-page','instance-packs','--shot-exit') -Wait -PassThru -NoNewWindow
```

**基线（当前部署的 D104 二进制，`.tmp\pad-build-z`，sha256 `C47F9830…`）**：

| 截图 | 结果 |
|---|---|
| `baseline/01-current-instance-packs-unsupported.png` | `--shot-page instance-packs` **尚无此页钩子**：落到启动页（只证明当前二进制没有该截图页；P1 需新增，若 P1 用了别的页名，按实际代码适配并记录） |
| `baseline/02-current-instance-overview.png` | 实例页（Overview）现状 |
| `baseline/03-current-instance-plugins.png` | 插件页现状：`pack-agent-dsh` + `dsh-context` 两行，**没有「来自整合包 X」**来源标注 |
| `baseline/04-current-instance-bundles.png` | 与 03 同页（bundles 别名，文件字节一致 66979） |

实例页现有导航 tag：`overview/settings/plugins/bundles/**packs**/sessions/presets`；整合包面板是
`PanePacks`（xaml line ~451，`PackList`），插件面板 `PanePlugins`（`PluginList`）。P1 任务的
`--shot-page instance-packs` 预期对应 `NavPacks`/tag=`packs`（**以 P1 实际落地代码为准**）。
灯光/呼吸灯不在 D105 验收范围（D104 已验），D105 只看文案/归属/动作接线。

## 4. M1 验收清单（P1 = task-4 完成时）

1. `dotnet build pad -c Debug` → 0 warnings / 0 errors；新产物 sha256 记录。
2. 5 个 bun 套件全绿（见 §2）。
3. **四类提示/文案**（截图 `p1/` + 源码断言双证）：
   - (a) 实例页整合包行副标题「**带 N 个插件（M 个不在这个 profile）**」：
     `p1-full` 应为 1 个插件、0 不在；`p1-missing` 应为 1 个插件、**1 个不在**；
   - (b) 实例页插件行「**来自整合包 X**」小标：`dsh-context` 行应标 `Verify Probe`（或包名）；
     管理插件 `pack-agent-dsh` 若未被包声明则无来源标（以代码事实为准）；
   - (c) 插件页 remove/update 对「被已装包声明」的 spec 有**二次确认**文案（包会残缺）；源码断言；
   - (d) `pack-agent` 管理器 remove 有 **PA110** 警告拦截（确认后继续）；源码断言；
   - (e) 版本页装态药丸文案过渡为「**已建实例：X**」（Q5，为 P2 账本语义让位）。
4. 截图人工核对 + 报告 PASS/FAIL；注意 P1 不改数据（不出现 pack-ledger.json 才算对）。

## 5. M2 验收清单（P2 core+UI = task-5/6 完成时，按 task-8 描述 a–e）

环境同 §1（新 build）。所有写操作只落 scratch root。

- a. 装兼容包装进新实例 → `instances/<id>/pack-ledger.json` 存在，字段对（`packs/owners/profiles/state`）。
- b. 停用包 → profile `package.json` 的 `dsh.profile.bundles` 中该插件行消失、`dependencies` 保留；
  `dsh --dump-config --profile web` 不含该层；启用 → 恢复；**不重新下载**（node_modules 时间戳/无 install 日志）。
  命令基线（node 入口，与 PAD `Packs` 一致）：
  `node E:\pack-agent\bin\packagent.js dsh --profile web --dump-config`（在 home 下，按实际 CLI 形态核对）。
- c. 卸载包 → 投影目录删除、独占插件被 remove、共享插件保留：用 `Shared-A`/`Shared-B`
  都声明 `dsh-context` 构造两包共享；断言 owners 计数与保留清单。
- d. 实例页「装整合包…」走完整链（`ApplyPackToInstance`）→ 插件真装、账本写入、版本页「已装到」来自账本。
- e. 手工 remove 账本登记的插件 → 包标 `incomplete`，实例页出现「修复」，`RepairPack` 可补齐。
- 截图落 `p2/`，JSON/命令输出落 `p2/`；`dotnet test pad.tests` 含新增 `PackLedgerTests` 全绿。

## 6. M3 验收清单（P3 = task-7 完成时）

- `allow-version` 通道：scratch home 里 `dsh plugin allow-version … --accept-risk` 后 `plugin add` 成功（E2E）。
- 冲突三选一 UI：升到包版本 / 保留现有（包 incomplete）/ 取消；三分支行为核对（源码断言 + 可得截图）。
- P3 新增 parser/AllowVersion 单测在 `dotnet test` 里全绿。

## 7. 部署（仅 Lead deploy-go 后）

把 `.tmp\pad-build-d105\*` 覆盖 `.tmp\pad-build-z\`（保留 `.pack-launcher`），停当前 PAD（先查 pid，
D105 时预计仍是 78308），`run-pad-new.cmd` 重启；断言新 pid、`pad-crash.log` 无新增、sisyphus web 未停、
live root `--shot` 渲染正常；写本 README 的 M1/M2/M3 + 部署结论并完成任务。

## 8. 风险 / 注意

- P1/P2 进行中（源码时刻在变）：只有 Lead 或执行者宣布里程碑完成后，才对新 build 跑正式验收；
  PREP 期间的截图/数据明确标注为基线。
- `instance-packs` 截图页是 P1 新增钩子，命名/导航 tag 可能与预期不同 → 先读
  `MainWindow.xaml.cs` / `InstanceView.xaml.cs` 的 ShotPage 分支再跑。
- npm 安装需网络且可能几分钟 → 后台 job + 超时放宽；fixture 只用 `dsh-context`（0.2.1-rc.2 可装，D104 已验）。
- `p1-missing` 是**人工构造**的缺失态（bundle 行摘除、deps 保留），仅用于展示；不代表真实安装。
- live PAD pid 78308 / live root 全程只读；`--shot` 不会弹窗、不抢前台。
- D104 的验证证据在 `EVIDENCE/D104-pad-pack-lamp-import/`，勿混用。

## 9. M1 结果（P1 / task-4 完成后的独立验收）

**结论：PASS**（2026-11-04 01:41）。机器摘要：`m1/SUMMARY.txt`；命令/日志/截图全部在 `m1/`。

- 源码快照：HEAD `54cf34ca…`，diff_sha `3739cb722efc738b835e2d566b270234353b863b`（M1 全程前后一致）。
- 构建：`dotnet build agent-pack-dsh\pad\pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-build-d105`
  → **exit 0，0 warnings / 0 errors**；新 DLL sha256 `1EFAEAF3…`（与 D104 的 `E478C313…` 不同，证 P1 代码进包；
  exe apphost sha 与 D104 相同属正常）。`m1/build-identity.txt`、`m1/logs/build-pad-d105.log`。
- 测试：5/5 bun 套件 exit 0（pad-window / pad-pack / import-pack / test / pack-catalog），
  `m1/tests-summary.txt` + `m1/logs/`。
- 截图（我的 scratch root + 新 build；pad.json lastInstance 切换后分区截图）：
  - `m1/shots/01-instance-packs-full.png`：p1-full 包行 **「带 1 个插件（都在这个 profile）」**
  - `m1/shots/04-instance-packs-missing.png`：p1-missing 包行 **「带 1 个插件（1 个不在这个 profile）」**
  - `m1/shots/02-instance-plugins-full.png`：dsh-context 行 **「来自整合包 Verify Probe」**；
    管理器 `pack-agent-dsh` 无来源标（它不被包声明，符合事实）
  - `m1/shots/03-versions-pills.png`：Verify Probe 药丸 **「已建实例：p1-full、p1-missing」**；Shared A/B **「未安装」**
  - 全部 exit 0；`--shot-page instance-packs` 钩子生效（基线 01 曾落到启动页）。
- 源码断言 14/14 PASS（`m1/source-assertions.txt`）：PackPluginFacts / PackFacts / PluginOrigin、
  「带 N 个插件」文案、OriginText、PackListRow、PA110 + IsManagerSpec、
  PluginRemove/PluginUpdate 的包归属确认（`_win.Ask(warn:true)`，取消不执行）、「已建实例：」、shot 钩子。
  modal 确认框无法离屏截图 → 以源码 + pad-window.test.ts 断言作证（诚实说明）。
- P1 只提示不改数据：scratch 实例无 `pack-ledger.json`（P2 才有），符合 P1 设计。
- Live 完整性：pid 78308 存活；live root 各文件哈希与 `prep/live-root-before.txt` 完全一致（无写入）；
  未重启 PAD、未改产品源码、未 commit。
- 交叉核对：pad-ui 自证截图在 `p1/`，文案与我的独立截图一致。

## 10. M2 结果（P2 core+UI 完成后的独立验收）

**结论：PASS**（2026-11-04 02:30）。机器摘要：`m2/SUMMARY.txt`；全部命令/日志/JSON/截图在 `m2/`。
冻结源码：HEAD `54cf34ca…`，diff_sha `d933536385a5146e3f1a29b70f641f700db4193e`
（M2 全程 before/after 一致；Lead 确认 pad-ui 已停手）。

- 构建：`dotnet build agent-pack-dsh\pad\pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-build-d105`
  → **EXIT 0，0 warnings / 0 errors**；dll sha256 `87CF41CB…`（`m2/build-identity.txt`）。
- 测试：5/5 bun 套件 EXIT 0；`dotnet test pad.tests` → **103 passed / 0 failed**（+36，含 PackLedgerTests）
  （`m2/tests-summary.txt`、`m2/logs/`）。
- E2E 驱动：`E:\pack-agent\.tmp\d105-m2\probe\d105-m2-probe.exe` 调**真实 P2 公共 API**；
  干净 scratch root `E:\pack-agent\.tmp\d105-m2\.pack-launcher`（versions junction；3 个 fixture 包）。

| 项 | 决定性结果 | PASS |
|---|---|---|
| a. 账本字段 | `pack-ledger.json` schema=`pack-agent.pack-ledger/v1`；pack `Verify-Probe-0.1.0` title/version/path/installedAt/content.dir/allow=True；`profiles.web.state=installed`、plugins[0]=`dsh-context` origin=pack addedVersion=0.80.0；owners.web["dsh-context"]=["Verify-Probe-0.1.0"] | ✅ |
| b. 停用/启用 | 停用：state=disabled、allow=False、bundles 摘掉 dsh-context、**deps 保留**、`--dump-config` 层 hits=0（baseline=3）；启用：恢复、hits=3；node_modules mtime 前后完全一致（**无重新下载**） | ✅ |
| c. 共享插件卸载 | Shared-A+Shared-B 同装一实例：owners dsh-context=[A,B]；卸 A → removed=[] skipped_shared=[dsh-context] projection_deleted=True，A 投影消失、B 投影保留、profile deps/bundles 仍含 dsh-context | ✅ |
| d. ApplyPackToInstance + 版本页账本态 | 对已有实例 Apply Shared-B：ok=True、kept=[dsh-context]（幂等 diff）；版本页药丸：Shared B「已装到 Shared A」、Verify Probe「已装到 Verify Probe」（账本），Shared A 卸载后「已建实例：Shared A」（来源回退） | ✅ |
| e. incomplete + 修复 | 手工 `dsh plugin remove dsh-context` 后读时对账 state=incomplete、missing=[dsh-context]（原始账本文件仍 installed，R1 正确）；截图显示「不完整」+ 修复按钮；RepairPack 105.5s 后 state=installed、added=[dsh-context]、still_missing=[]，截图不再显示不完整 | ✅ |

- 截图：`m2/shots/02-instance-packs-installed.png`、`01-instance-packs-incomplete.png`、
  `03-versions-ledger-pills.png`、`04-instance-packs-after-repair.png`（全部 EXIT 0）。
- P2 UI 源码断言 10/10 PASS（`m2/source-assertions.txt`）：Toggle→SetPackPluginsActive、
  UninstallPack/RepairPack 接线、Import_Click→ApplyPackToInstance（不再直调 Project）、
  RecordPluginRemoved、VersionsView 账本「已装到」、未登记/不完整 badge + 修复 gating；
  pad-window.test.ts 含整组 P2 断言。
- 完整性：live PAD pid 78308 存活；live root 各文件哈希与 `prep/live-root-before.txt` 一致（无写入）；
  源码 diff_sha 全程不变；未重启 PAD、未改产品源码。
- 观察（非验收失败）：旧 `Packs.Import(path)` 不吃 --name（实例 id 取包标题 slug；UI 的 ImportPack 传 --name）；
  卸载后包仍可能以「未登记」行出现在实例页（allow 行未清，P2 范围外）；真实 pnpm 操作慢（修复 105.5s）。

## 11. M3 结果（P3 / task-7 完成后的独立验收）

**结论：PASS**（2026-11-04 02:54）。机器摘要：`m3/SUMMARY.txt`；命令/日志/JSON 在 `m3/`。

- 源码（**re-frozen**）：HEAD `54cf34ca…`；`diff_sha_full=4ee5c643…`（含 Lead 的 DECISIONS/docs 文档改动）、
  `diff_sha_product=65751b98…`（只看 .cs/.ts/.xaml/.json）。
  说明：第一次快照 `fb62bb52` 期间恰好有 Lead 的 DECISIONS.md / docs README 落盘（产品代码未变）；
  重冻结后**整个 gate 重跑**，两次 build 的 dll sha 完全一致 → 结论不受影响。
- 构建：`dotnet build pad -c Debug -o .tmp\pad-build-d105` → EXIT 0，**0 warnings / 0 errors**；
  dll sha256 `A0AEA165…`（`m3/build-identity.txt`）。
- 测试：**6/6 bun 套件** EXIT 0（pad-window / pad-pack / import-pack / test / pack-catalog / pinst）；
  `dotnet test pad.tests` → **114 passed / 0 failed**（103→114，P3 +11 PackCompatTests）；
  run 后 full/product diff_sha 均不变（`m3/tests-summary.txt`）。

### 11.1 P3 E2E（**全新 DSH_HOME** scratch root，非 copy-home）
`E:\pack-agent\.tmp\d105-m3\.pack-launcher` 从零重建（versions junction；实例由 import 链自建）：

1. `cli import Compat-Probe-0.1.0`（声明 required `dsh-find-skill`，与 0.2.1-rc.2 不兼容）→ EXIT 1、
   error[PA014]、instance `compat-probe` status=import-failed、source.packId 记录。
2. **负控**（未 allow）：`dsh plugin --profile web add dsh-find-skill` → EXIT 1，stderr 含**逐字提示行**：
   `dsh: to accept the risk, run: dsh plugin --profile web allow-version dsh-find-skill@0.2.1 --dsh-version 0.2.1-rc.2 --accept-risk`
3. P3 API `Launcher.AllowVersion` → EXIT 0（args 与 DSH 契约逐字一致；本次 102.8s，含 PAD 侧 bun/pnpm 准备）；
   写 `profiles\web\compatibility.json` = `{"dsh-find-skill@0.2.1":["0.2.1-rc.2"]}`；
   `dsh plugin --profile web version-exemptions` EXIT 0 → 同一份 JSON。
4. allow 后 `dsh plugin --profile web add dsh-find-skill` → **EXIT 0（4.1s）**；deps+bundles 增加；
   `dsh --dump-config --profile web` EXIT 0，**dsh-find-skill 层 hits=3**。
5. PAD 重试路径：allow 后 `Packs.ApplyPackToInstance` → ok=True（已装则幂等 added=[]）；
   `Launcher.MarkInstanceStatus(ready)` → 实例状态 ready。
6. **真实版本冲突**（R6 不静默覆盖）：实例 verify-probe 已装 dsh-context 0.80.1，
   apply Conflict-Probe（钉 `dsh-context@0.79.0`）→ `ok=False state=incomplete conflicts=[dsh-context@0.79.0]`。

Parser（`m3/parser-checks.txt`、`m3/prep/parser-smoke.txt`）：精确提示行 → profile=web + pkg/dsh 全解析；
完整 stderr（warning 行在前）→ 解析成功但 profile 空（文档约定"空=用当前 profile"）；
截断变体 → parsed=null 且不崩；`--dsh-version=` 等号形式 / CRLF+`| ` PAD 渲染 / scoped 包名 均解析正确。

### 11.2 P3 UI / 单测断言
`m3/source-assertions.txt`（+ corrections）：PackCompat.FromError 接入、兼容三选一（接受风险/换内核/取消）、
`Launcher.AllowVersion` 调用（InstanceView.xaml.cs:1721）、版本冲突三选一、VersionsView
`RecoverIncompatibleImportAsync`+`MarkInstanceStatus` 续装、**stale-row 过滤**（InstanceView.xaml.cs:1845
`packKey is null && r.Dir 不存在 → 隐藏`，按实际实现核对；我最初猜 `ProjectionExists` 是错的，已纠正）、
`AllowVersionArgs` 契约、`PackCompat` 类、pad-window.test.ts 完整 P3 block。
`PackCompatTests` 11 facts（dotnet test 114/114 内含）。
- 交叉核对：pad-ui `p3/` 证据（fresh-home E2E、version-exemptions、stale-row 截图、copy-home
  ERR_PNPM_UNEXPECTED_VIRTUAL_STORE 如实记录）与我的独立结果一致。

### 11.3 完整性
live PAD pid 78308 存活；live root 哈希与 `prep/live-root-before.txt` 完全一致（无写入）；
源码 diff_sha 全程稳定（全量 4ee5c643 / 产品 65751b98）；未重启 PAD、verifier 未改产品源码、未 commit。
观察（非失败）：PAD 侧 AllowVersion 102.8s（raw dsh 1.1s，差在 bun/pnpm 准备，UI 有 busy/progress）；
`dsh plugin version-exemptions` 需要 `--profile`；多行 stderr 的 profile 空回退属设计。

## 12. Deploy-go packet（等 Lead 明确 deploy-go 后才执行）

| 项 | 值 |
|---|---|
| 构建产物 | `E:\pack-agent\.tmp\pad-build-d105\*`（exe/dll/pdb/deps/runtimeconfig/Assets/runtimes/webview dlls/packmaker.exe） |
| exe（apphost） | `pack-agent-for DSH.exe` sha256 `C47F9830866D1EC418132CC2FE4124CA6CB9EC14A8821AEF2AA13B1CAAA77E55`（apphost 恒定） |
| dll（真实代码） | `pack-agent-for DSH.dll` sha256 `A0AEA16526EF6FA83499B2934345A6E681DD7490729F47507549D393DB46059A` size=4832768 |
| 目标 | `E:\pack-agent\.tmp\pad-build-z\`（保留 `.pack-launcher`、`pad-crash.log`、`packmaker.exe`） |
| 步骤 | ①停当前 PAD（先查 pid，D105 时预计 78308，`Stop-Process -Id <pid> -Force`）→ ②复制 `.tmp\pad-build-d105\*` 覆盖 `.tmp\pad-build-z\` → ③校验部署后 dll sha=`A0AEA165…` → ④`cmd /c E:\pack-agent\.tmp\run-pad-new.cmd` 重启 → ⑤等窗口、记录新 pid |
| 部署后必检 | 新 pid ≠ 旧 pid 且 exe 路径=`pad-build-z`；`pad-crash.log` 前后 size/mtime/sha 不变（无新增）；`pad.json`/`ui-state.json`/`runtime.json` 哈希不变；sisyphus web（runtime pid 67008）仍存活、`cli status` Phase=Running；live root `--shot --shot-page versions --shot-exit` EXIT 0 + 渲染正常（账本/药丸/无崩溃）；`pad.lock` 内容=新 pid |
| 证据落盘 | `EVIDENCE/D105-pack-plugin-layering/m3/deploy/`（before/after 状态、部署后 live shot、sha 对照） |

### 12.1 部署执行结果（POST-DEPLOY，deploy-go 后执行）

时间：2026-11-04 03:04–03:05。证据：`m3/deploy/`（before-state / copy-result / final-state / status-after-deploy.json /
`16-deployed-live-versions.png` / lamp-analysis-deployed.txt）。

| 项 | 结果 |
|---|---|
| 覆盖产物 | `.tmp\pad-build-d105\*` → `.tmp\pad-build-z\`；`.pack-launcher` / `pad-crash.log` / `packmaker.exe` 保留 |
| 部署后 exe | sha256 `C47F9830866D1EC418132CC2FE4124CA6CB9EC14A8821AEF2AA13B1CAAA77E55`（=预期 apphost） |
| 部署后 dll | sha256 `A0AEA16526EF6FA83499B2934345A6E681DD7490729F47507549D393DB46059A`（=预期真实代码） |
| 旧进程 | pid **78308** 已停止 |
| 第一次重启 | pid 53632（03:04:24 启动）；**被 verifier 的 hung capture job 清理时连带杀掉**（该 GUI 进程继承了被终止 job 的进程树）——如实记录，非产品问题；随即干净重启 |
| **最终部署进程** | **pid 81852**，start 2026-11-04 03:04:49，exe=`E:\pack-agent\.tmp\pad-build-z\pack-agent-for DSH.exe`，窗口标题 `PAD`，`pad.lock` 内容=81852；截图后仍 ALIVE |
| pad-crash.log | 部署前后完全一致：size 21267 / mtime 2026-11-02T16:42:25 / sha256 `721F9951…` → **无新增**；live shot 后再查仍一致 |
| 设置文件 | `pad.json` / `ui-state.json` / `runtime.json` / `jobs.json` sha256 与部署前完全一致（未被改写） |
| sisyphus web | runtime 账本 `sisyphus/web pid=**61272**` 存活（注：D104 时的 67008 已不在——期间 sisyphus 被重启过，非本次部署所致）；`cli status` → `sisyphus: Running`；**未停止** |
| 部署后 live shot | `m3/deploy/16-deployed-live-versions.png` EXIT 0（82865B，1000x620@1.5dpi）；渲染正常无崩溃；lamp 计数 **0**（3.2s 延迟下只赶上所选实例 123 的 dsh-tui 行，检测行未出现——1 盏灯的严谨证明仍是 M1 的镜像图 `m1/shots/11…`） |
| 部署后药丸 | live root 的 Test 行显示 **「已建实例：pack-Test」**：`pack-test` 是 D105 之前建的老实例、没有 `pack-ledger.json` → 账本无记录，按来源回退显示「已建实例」；D105 之后新建/重装的实例才会显示「已装到」 |

**残留注意（给最终用户消息）**：
- live root 老实例（pack-test 等）没有 pack-ledger，实例页整合包行会显示「未登记」、版本页显示「已建实例」；
  这是设计内的老实例回退（P2 空态/边界），不是故障；对老实例执行一次「装整合包」后账本会补写。
- `run-pad-new.cmd` 中文注释是 GBK，重定向 stderr 会看到一条无意义 cmd 报错；脚本本身启动正常。
- 已退出僵尸进程对象 pid 49748 仍在（0 句柄），不持有 live pad.lock（=81852），无害。
- 部署前后 live launcher root 只读：未写 live root；sisyphus web 未动。

## 13. D106 M4 结果（整包 icon / 版本页去装态文字 / 实例页已启用包发光）

**结论：PASS**（2026-11-04 03:36；task-10）。机器摘要：`ux-icon-glow/SUMMARY.txt`；证据 `ux-icon-glow/`。
源码冻结：`diff_sha_full=5c46b79b…`、`diff_sha_product=710e23da…`（全程稳定）。
构建：`dotnet build pad -c Debug -o .tmp\pad-build-d106` → EXIT 0，**0 warnings / 0 errors**；
dll sha `A765CF9C…`（与 D105 部署的 `A0AEA165…` 不同，证 D106 代码进包）。
测试：6/6 bun 套件 EXIT 0；`dotnet test pad.tests` → **126 passed / 0 failed**（+12 PackIconTests）。

### 13.1 三项 UX 验收（独立 fixtures + 新 build 截图）
Fixture（`.tmp/d106-ux/.pack-launcher/library/packs/`）：
`Icon-Probe-0.1.0.pack.zip`（zip 根带 64x64 `icon.png`，橙底白环白 I）、`NoIcon-Probe-0.1.0.pack.zip`（无 icon）；
scratch 实例 `icon-probe`：Icon-Probe 已启用、NoIcon-Probe 已停用（发光对比）。
截图（`ux-icon-glow/fixed/`，全部 EXIT 0）：
1. **版本页** `01-fixed-versions-icons.png`：Icon-Probe 行显示**自定义图标**；NoIcon 行回落**「包」pill**；
   **没有任何「已装到/已建实例」文字**（源码里 InstallStateText/PackStatePill/IsInstalled/已装到/已建实例 全部移除）。
2. **实例页发光** `02/03-fixed-instance-packs-glow*.png`（两次不同呼吸相位）：
   enabled 行（Icon Probe）出现 accent 呼吸光；disabled 行（NoIcon Probe）无发光（略调暗）；
   账本「不完整」药丸、修复/卸载按钮保留。
3. 图标渲染证据：fixed instance-packs 行图标区橙像素 1251（bbox 317,256-357,296 = 41x41px ≈ 27x27 逻辑）；
   baseline（D105）同一页橙像素 0（通用「包」）→ 自定义图标确实生效。
4. 发光定量证据（`ux-icon-glow/fixed/`）：
   - 对照：旧 D105 build 连拍两张 → changed_pixels=**0**（页面静止，无动画）；
   - D106 连拍两张 → changed_pixels=**40800**，集中 enabled 卡片上缘 y=210..234（25878px）与下缘
     y=358..375（14871px）；disabled 行区域 0 → 呼吸动画只在 enabled 行；
   - 颜色采样：enabled 上缘 baseline (225.1,230.7,235.3) → fixed shot02 (212.5,220.4,225.9) →
     shot03 (188.2,200.8,208.6)（呼吸相位差）；disabled 下缘三张均 (234.0,239.0,243.0) → 不发光。
5. 交叉核对：pad-ui 自证截图 `ux-icon-glow/versions-pack-icons.png` / `instance-packs-glow.png` 与我的独立结果一致。
6. 源码断言（`ux-icon-glow/source-assertions.txt`）：VersionsView 无可见装态绑定；IconPath/HasIcon 两页都有；
   InstanceView DropShadowEffect + DoubleAnimation + Forever + `Enabled=false → Effect=null` 联动；
   PackIcon 缓存 `<root>/library/icons/packs`、sha16(path+mtime+size) key、`MaxBytes=2*1024*1024`、
   svg 光栅化、坏 zip catch；pad-window.test.ts 有完整 D106 断言块；PackIconTests 12 facts。

### 13.2 完整性 / 部署
live PAD **pid 81852 未被触碰**（`pad.lock`=81852）；live root 未写（`ux-icon-glow/prep/live-before-m4.txt` 的
pad.json/ui-state/runtime/jobs/pad-crash.log 哈希仍为基线）；未重启 PAD；verifier 未改产品源码、未 commit。
**部署状态：等 Lead deploy-go**（packet 见 `ux-icon-glow/SUMMARY.txt` 末尾：产物 `.tmp\pad-build-d106\*`，
dll sha `A765CF9C…`，覆盖 `.tmp\pad-build-z\` 保留 `.pack-launcher`，停 81852 → 复制 → `run-pad-new.cmd`
（**不要管道捕获**）→ 新 pid/crash-log 无 delta/设置哈希不变/sisyphus 61272 alive/live `--shot-page versions`）。

### 13.3 M4 部署执行结果（POST-DEPLOY）

时间 2026-11-04 03:36:38–03:37。证据 `ux-icon-glow/deploy/`。

| 项 | 结果 |
|---|---|
| 覆盖产物 | `.tmp\pad-build-d106\*` → `.tmp\pad-build-z\`；`.pack-launcher` / `pad-crash.log` / `packmaker.exe` 保留 |
| 部署后 exe | sha256 `C47F9830866D1EC418132CC2FE4124CA6CB9EC14A8821AEF2AA13B1CAAA77E55`（=预期 apphost） |
| 部署后 dll | sha256 **`A765CF9C373EC6D1D6121128AD4CEB2265680B25FAB43545C8F00F271198E88A`**（=预期 D106 代码） |
| 旧进程 | pid **81852** 已停止 |
| **新进程** | **pid 75696**，start 2026-11-04 03:36:47，exe=`.tmp\pad-build-z\pack-agent-for DSH.exe`，窗口标题 `PAD`，`pad.lock`=75696；截图后仍 ALIVE |
| pad-crash.log | 部署前后 & 截图后完全一致：size 21267 / mtime 2026-11-02T16:42:25 / sha256 `721F9951…` → **无新增** |
| 设置文件 | `pad.json` / `ui-state.json` / `runtime.json` / `jobs.json` sha256 与部署前完全一致 |
| sisyphus web | runtime 账本 `sisyphus/web pid=61272` ALIVE；`cli status` → `sisyphus: Running`（未停） |
| 部署后 live shot | `ux-icon-glow/deploy/01-deployed-live-versions.png` EXIT 0（97488B）；live root 的 Test 整合包行显示 **Test 包自己的 icon.png**（不再是通用「包」pill），且**没有任何装到哪文字** → D106 UX 已在 live root 生效 |
| 启动方式 | `cmd /c E:\pack-agent\.tmp\run-pad-new.cmd`（不捕获管道，D105 教训）；脚本 GBK 注释仍会打一条无害 cmd 警告 |

**残留注意**：live root 老实例（pack-test 等）无 pack-ledger，实例页整合包行显示「未登记」、账本/修复语义按老实例回退（P2 设计），
D106 只在视觉上换 icon/去装到哪/已启用发光，不改老实例数据；旧僵尸进程对象 pid 49748（0 句柄）无害。
