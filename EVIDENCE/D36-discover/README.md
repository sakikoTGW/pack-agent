# D36 · 本机 DSH 全量探测（自动嗅探修全）

日期：2026-10-27（二期同日补完）· 相关决策：`DECISIONS.md` D36 与 D36 补充

## 这次修的是什么

原来的自动探测只认少数硬编码位置，且**没有"内核"这一层**：

- `shells` 固定 0 个：官方桌面端 `DeepSeek Harness.exe` 与非官方 `DSH Desktop.exe` 都漏
- TUI 没有建模，设置页 `dsh-tui` 一行永远「未找到」
- 官方桌面端 home `E:\DSH-Home` 找不到
- 「在跑」只能看文件/账本，TUI 永远判"没跑"、壳的 userData 靠目录名猜

现在按 **内核（Kernel）/ 载体（Shell·TUI·CLI）/ home** 三层建模，身份 = 内核；
「在不在跑」和「壳用哪个 userData」直接读进程 PEB（命令行 + 环境变量），不再猜。

```
Kernel  @deepseek-ai/dsh 包
        Identity = 真实包目录绝对路径
                 | <asar 路径>!<归档内目录>
                 | declared:@deepseek-ai/dsh@<ver>（只有启动脚本声明）
Carrier 桌面壳（Electron + asar 里有内核）/ TUI（profile 依赖）/ CLI（bin.js 或 shim）
Home    profiles/ 或 storages/ 的目录
进程现场 ProcTable：PEB 读命令行 + DSH_HOME/DSH_PROFILE/--user-data-dir/--profile
```

**判据：同一版本、不同位置 = 同一发行版的两份内核，不是同一个 DSH。**

## 本机实测（命令可直接复跑）

```powershell
$exe='E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\pack-agent-for DSH.exe'
$env:PACK_LAUNCHER_ROOT='E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher'
& $exe cli discover --json --out E:\pack-agent\EVIDENCE\D36-discover\inventory.json
& $exe cli discover      # discover.txt
& $exe cli procs --json  # procs.json：PEB 现场（本机 40+ 条相关进程）
& $exe cli sniff         # sniff.txt：轻量单条探测（也能给出 TUI 真实包路径）
```

`discover.txt` 摘要：

```
kernels 4
  0.2.1-rc.2  desktop bundle: E:\DSH-App\DeepSeek Harness.exe
  0.2.0-rc.2  desktop bundle: E:\新建文件夹 (5)\DSH Desktop\DSH Desktop.exe
  0.1.0-rc.7  launcher script: E:\dsh-tui\start.cmd   (declared)
  0.1.0-rc.7  pad version store
shells  2
  running  0.2.1-rc.2  E:\DSH-App\DeepSeek Harness.exe
             home E:\DSH-Home  userData %APPDATA%\@deepseek-ai\dsh-desktop  pid 28032
  stopped  0.11.1      E:\新建文件夹 (5)\DSH Desktop\DSH Desktop.exe
             home %APPDATA%\dsh-desktop\harness  userData %APPDATA%\dsh-desktop
tui     4（1 个真 TUI + 3 个 PAD 实例 profile；通用扫描所得）
  @deepseek-harness-tui/dsh-tui 0.8.3  home E:\dsh-tui\dsh-home  profile dsh-tui
       kernel 0.1.0-rc.7  launcher E:\dsh-tui\start.cmd
cli     4（官方壳内 bin.js / 官方 runtime dsh.cmd shim / PAD 版本库 / pad.json#launch.dshCli）
homes   8（E:\DSH-Home · %APPDATA%\dsh-desktop\harness · ~/.dsh · 3 个 PAD 实例 · ~ · E:\dsh-tui\dsh-home）
```

### 「在跑」的证据（真进程，不是 mock）

抓证据时起了一个真进程模拟终端里的 TUI/CLI：

```powershell
$bin='...\.pack-launcher\versions\0.1.0-rc.7\node_modules\@deepseek-ai\dsh\lib\bin.js'
$env:DSH_HOME='E:\dsh-tui\dsh-home'
node E:\tmp\demo-tui.js $bin --profile dsh-tui      # 进程 pid 记为 57716
```

`discover.txt` 随之出现：

```
running  @deepseek-harness-tui/dsh-tui 0.8.3  home E:\dsh-tui\dsh-home  profile dsh-tui  pid 57716
running  0.1.0-rc.7  ...versions\0.1.0-rc.7\...\lib\bin.js  pid 57716
running  [desktop] 15 sessions E:\DSH-Home
running  [dsh-tui] 0 sessions  E:\dsh-tui\dsh-home
```

kill 掉该进程后重跑同一个 discover：TUI/CLI 的 running 都回 0。
`procs.json` 里该 pid 的 `DshHome` / `ProfileArg` 与 discover 的判定一致。
工作台卡片 `workbench-dsh.png`（`--shot-page manage-open` 抓的）能看到
「本机 DSH」4 个载体 + TUI/CLI 的「在跑」；设置页 `settings-scan.png` 有同样的 Running/pid。

## 探测线索（通用规则，不写死本机路径）

```
桌面壳  运行中进程（Electron 签名 = exe 旁 resources\app.asar 里有内核或 dsh-desktop-host）
        开始菜单 .lnk（反射 COM WScript.Shell；跟 cmd→ps1 链取 DSH_HOME / dsh CLI）
        卸载注册表（HKLM/HKCU Uninstall* + HKCU\Software\<guid> 的 NSIS InstallLocation）
        已知 userData / pad.json#launch.dshCli 反推安装目录
        壳的 userData/home 优先取同 exe 进程的 PEB 现场（--user-data-dir / DSH_HOME）
TUI     home 的 profiles\*\package.json 依赖 + dsh.profile.bundles（不写死包名）
        启动脚本里的 DSH_HOME / --profile / @deepseek-ai/dsh@ver
        ~/.dsh-tui 状态目录兜底；在跑 = PEB 环境 DSH_HOME + profile 对上
CLI     pad.json、PAD 版本库、所有内核 Bin、PATH、桌面壳 shim、asar 内 bin.js
        在跑 = PEB 命令行里「参数 token」等于 bin（不是子串——脚本文本里出现路径不算在跑）
Home    已知位置 + 壳 home + 环境/注册表 DSH_HOME + PAD 实例
        固定盘根一层 *dsh*/*harness* 命名目录（深度≤2、有上限）
        --deep <root> 显式深度扫描
```

性能硬约束（承 D21/D22）：**不 shell out、不做无界遍历**；进程≤900/候选≤240、快捷方式≤800、
注册表≤2000、每类目录≤4000，扫描只在 worker 线程跑，设置页先画再填。
本机冷跑 discover ~3s（PEB 快照约 1.2s）、sniff ~0.6s。

## 测试（本机，最后构建上全绿）

```
bun run agent-pack-dsh/modpack/pad-discover.test.ts   ✓（含真子进程：TUI Running+Pid、procs DshHome/ProfileArg）
bun run agent-pack-dsh/modpack/pad-window.test.ts     ✓（顺带修了两条过期断言：D24 的 OEM 编码、Swap() 的 same 判定）
bun run agent-pack-dsh/modpack/pad-depth.test.ts      ✓
bun run agent-pack-dsh/modpack/launch-script.test.ts  ✓
```

`pad-discover.test.ts` 的「在跑」链路是真的：spawn `bun -e setTimeout --profile dsh-tui`
带 fixture `DSH_HOME` → discover 的 TUI 条目 `Running=true` 且 `Pid===child.pid`，
`pad cli procs` 里该 pid 的 `DshHome`/`ProfileArg` 与预期一致；kill 后归 false。

## 剩余的真边界（物理限制，不是没研究）

1. PEB 偏移是 **x64 专用**：PAD 以 x86 构建时安全跳过（不读错），32 位目标进程也跳过。
2. 提权/其他用户进程 `OpenProcess` 失败 → 读不到现场；只能靠文件/注册表线索（壳仍需同名 exe 判定）。
3. 命令行上限 4096 字符；`SplitArgs` 是简化引号解析（不实现 `\"` 转义），DSH 启动行远短于此。
4. TUI 的 home 若完全不在"命名目录/已知位置"下，需要 `--deep <root>` 才会被发现（有意的：不做全盘遍历）。
5. `pad cli sniff` 是轻量探测（PATH/模块目录/盘根命名）；完整清单走 `pad cli discover` 或设置页。

## 版本选择页（同一轮的补充）

探测结果要落在**用户真正做选择的地方**，而不是到处贴一张"探测清单"：

- **「DSH 版本」下拉** = PAD 版本库（可钉）+ 本机探测到的内核（只读、带来源标签）；**同版本合并来源**，
  不能因为版本库里有 0.1.0-rc.7 就把「TUI 启动脚本声明的 0.1.0-rc.7」吞掉。
  本机实测三项：`0.1.0-rc.7（PAD 版本库 + 本机启动脚本声明）`、
  `0.2.0-rc.2（本机桌面端捆绑 DSH Desktop）`、`0.2.1-rc.2（本机桌面端捆绑 DeepSeek Harness）`；
  选中只有本机来源的版本再点「改钉」→ 明确说"不在版本库，不能改钉，要钉先装同版本"，不静默失败。
- **「终端 (N)」列表** = 当前实例的 profile + 本机探测到、还没收编成实例的 TUI。
  本机出现 `dsh-tui（本机） · 终端 · dsh-tui 0.8.3 · 内核 0.1.0-rc.7 · 本机 dsh-tui/dsh-home`
  —— TUI 自己的包版本（0.8.3）、它用的内核、它所在的 home 三样都写在同一行，而不是一条和现有行几乎一样的
  "终端 · 0.1.0-rc.7"；行尾「收编」把它所在的 home 收成 PAD 实例（收编过的不会再重复出现）。
- 证据：`versions-page.png`（终端列表两行 + 下拉当前项的来源）、`versions-dropdown.png`（下拉展开，三条版本与来源）。
- 过程中我曾在这页左侧加过一块独立的「本机 DSH（自动探测）」清单——被判定为多余的东西，已整体回退；
  这页只做上面两处"融进既有选择控件"的改动。

## 文件清单

| 文件 | 角色 |
|---|---|
| `pad/Core/Inventory.cs` | Kernel / Shell / Tui / DshCli / DshHome / Inventory（Identity + Running/Pid） |
| `pad/Core/Asar.cs` | 手写 asar 读取器（asar 内内核版本） |
| `pad/Core/DshKernels.cs` | 内核扫描（三种 Identity、六类已知根、Merge 去重） |
| `pad/Core/ProcTable.cs` | PEB 进程现场（命令行/DSH_HOME/--profile/--user-data-dir） |
| `pad/Core/Discover.cs` | 全量探测：桌面壳/TUI/CLI/home/内核 + 进程现场归属 |
| `pad/Core/DshSniff.cs` | 轻量探测：PATH/模块目录/盘根命名（sniff 与设置页共用） |
| `pad/Core/Cli.cs` | `discover` / `procs` / `sniff` 输出 |
| `pad/Core/AppState.cs` | `DetectedTuiRow`：探测到的 TUI 作为「终端」列表行（ProfileVm 子类） |
| `pad/Views/SettingsView.*` | 设置页「本机 DSH」+ 重新探测（worker 扫描） |
| `pad/Views/WorkbenchView.*` | 工作台「本机 DSH」卡：载具计数 + 展开逐行 |
| `pad/Views/VersionsView.*` | 版本下拉含探测内核；终端列表含探测 TUI（可收编） |
| `modpack/pad-discover.test.ts` | fixture + 真进程集成测试 |
