# D106 M4 · 整合包 icon / 去装态文字 / 已启用包发光 — verifier PREP

状态：**PREP 完成（2026-11-04 03:25），等 Lead "M4 GO"（task-9 done）后 claim/跑验收；部署等 deploy-go。**
Owner：verifier（task-10）。只写 `.tmp` 与 `EVIDENCE/D105-pack-plugin-layering/`；不改产品源码、不写 live root、不 commit。
证据目录：`EVIDENCE/D105-pack-plugin-layering/ux-icon-glow/`。

## 1. 当前 live 状态（deploy 基线，部署时沿用 D105 M3 §12 模板）

`prep/live-before-m4.txt`：
- live PAD **pid 81852** ALIVE（title PAD，exe `.tmp\pad-build-z\pack-agent-for DSH.exe`，start 2026-11-04 03:04:49），`pad.lock`=81852
- 当前部署 dll sha `A0AEA16526EF6FA83499B2934345A6E681DD7490729F47507549D393DB46059A`、exe apphost sha `C47F9830…`
- `pad-crash.log` size 21267 / mtime 2026-11-02T16:42:25 / sha `721F9951…`（无新增基线）
- `pad.json` / `ui-state.json` / `runtime.json` / `jobs.json` sha 已记录（部署后必须不变）
- sisyphus web：runtime 账本 pid **61272** ALIVE（未停）
- 部署目标：`.tmp\pad-build-z\`（保留 `.pack-launcher`/`pad-crash.log`/`packmaker.exe`）；
  新 build：`dotnet build agent-pack-dsh\pad\pad.csproj -c Debug -o E:\pack-agent\.tmp\pad-build-d106`；
  重启：`cmd /c E:\pack-agent\.tmp\run-pad-new.cmd`。

## 2. Fixture 包（已造好，`.tmp/d106-ux/.pack-launcher/library/packs/`）

| 包 | zip 内容 | 说明 |
|---|---|---|
| `Icon-Probe-0.1.0.pack.zip` (932B) | `pack.json` + **`icon.png`**（根，500B，64x64 橙底白环白 I，可肉眼区分） | D106 应显示自定义图标 |
| `NoIcon-Probe-0.1.0.pack.zip` (338B) | 仅 `pack.json` | D106 应回落「包」pill |

两包都是 `ccui-pack/v0.2`、kernel 0.2.1-rc.2、profile web、声明 required `dsh-context`（保证可安装）。
注意：pack.json 必须 **UTF-8 无 BOM**（带 BOM 会让 TS JSON parse 失败，已踩过并修正）。

## 3. Scratch 实例（发光对比用）

`E:\pack-agent\.tmp\d106-ux\.pack-launcher`（versions junction → live versions）：
- 实例 `icon-probe`（由当前 D105 build 的 `pad cli import Icon-Probe...` 创建，status=ready）
- 两个投影：`pack-agent-modpack-icon-probe`、`pack-agent-modpack-noicon-probe`
- 账本：`Icon-Probe-0.1.0 state=installed allow=True`（**已启用 → 应有发光**）；
  `NoIcon-Probe-0.1.0 state=disabled allow=False`（**已停用 → 应无发光**）
- `pad.json`：`lastInstance=icon-probe`、`shotDelayMs=12000`、窗口 1040x820
- 重建命令（如需）：见 `prep/` 下 import/apply/disable 日志与 D105 README 的 fixture 脚本思路。

## 4. Before 基线截图（当前 D105 build，pad-build-z）

`baseline/`：
- `01-baseline-versions.png`：整合包行只有通用「包」pill（无自定义 icon）；仍有旧装态药丸
  「已装到 Icon Probe」→ D106 后应**不再显示装到哪**、Icon-Probe 行显示自定义图标、NoIcon 行回落「包」
- `02-baseline-instance-packs.png`：两行 pack 无图标；**无发光**（enabled/disabled 视觉相同）；
  Icon Probe 已启用/installed，NoIcon Probe 已停用（显示不完整 badge + 修复）→ D106 后
  enabled 行应发光、disabled 行不发光
- `03-baseline-instance-overview.png`：实例概览（可选对照）
- `lamp-analysis-baseline.txt`：绿色 lamp 计数 0（D106 发光若用 accent 色，不能靠现 lamp 计数器，
  需按实现看色值；会加像素级对比 enabled vs disabled 行）

## 5. M4 验收计划（Lead 说 "M4 GO" 后执行）

1. 冻结 `diff_sha`（full + product 子集 `.cs/.ts/.xaml/.json`），确认稳定后：
   `dotnet build pad -c Debug -o .tmp\pad-build-d106` → 0/0；
   `dotnet test agent-pack-dsh\pad.tests\Pad.Tests.csproj`（含新 `PackIconTests`）；
   6 个 bun 套件（pad-window / pad-pack / import-pack / test / pack-catalog / pinst）全绿。
2. 独立源码复核（对照 task-9 实现）：
   - `VersionsView.xaml(.cs)`：**界面不再绑定/显示 InstallStateText/已装到/已建实例药丸**（tooltip 允许）；
     有 `IconPath`/`HasIcon` + 回落「包」pill；
   - `InstanceView` 包行：`Enabled` 时 `Effect`/呼吸动画，Disabled 无发光；
   - `PackIcon` 解析：zip 根 icon.png、缓存、坏包/无 icon 不抛（README/测试断言 + 源码）。
   - pad-window.test.ts 的 D106 断言存在且绿。
3. 用**新 build** 对同一 scratch root 出 `fixed/` 截图（versions + instance-packs），人工核对：
   图标显示/回落、版本页无装到哪文字、enabled 发光/disabled 不发光；对 instance-packs 做像素级
   enabled/disabled 行对比（提取行区域亮度/边缘光晕差异），结论落 `fixed/`。
4. 写 `ux-icon-glow/SUMMARY.txt` + D105 README 的 D106 段落；FAIL 原样报。
5. deploy-go 后：按 §1 模板部署（pid 81852 → 新 build 覆盖 build-z → Stop → run-pad-new → 新 pid /
   crash-log 无 delta / 设置哈希不变 / sisyphus 61272 alive / live `--shot-page versions` 渲染正常），
   证据 `ux-icon-glow/deploy/`，完成 task-10。

## 6. 已知注意

- 不要用 `2>&1 |` 捕获 `run-pad-new.cmd` 的输出：GUI 进程继承管道会挂住；D105 部署时因此误杀过一次
  detached PAD（本次重启直接 `& cmd /c ...` 不捕获）。
- 当前 scratch 的 NoIcon-Probe 因共享 dsh-context 被停用而显示「不完整」；不影响发光对比（disabled 行）。
- D106 变更进行中（PackIcon.cs/PackIconTests.cs 已出现）；PREP 的 before 截图基于当前部署 D105 build。
