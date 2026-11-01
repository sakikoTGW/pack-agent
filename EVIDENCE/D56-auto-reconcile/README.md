# D56 · 2026-10-31 · 本机 DSH 自动对账：home 换了，PAD 自己跟过去

用户（指着 D55 的横幅）：

> "这个东西就是这么智能，这种东西不能手动"

## 为什么会冒出 D55 那条横幅

1. `instances/dsh/instance.json` 是 2026-10-22 收编时冻住的路径指针：
   `home=%APPDATA%\dsh-desktop\harness`、`version=0.1.0-rc.7`、`adopted=true`。
   后来本机换到新的 DeepSeek Harness app（`E:\DSH-App`，`DSH_HOME=E:\DSH-Home`），
   没有任何逻辑回头改这份实例。
2. `pad.json` 其实早就对了：`launch.dshHome = E:\DSH-Home`。设置和实例各指一处，从不碰面。
3. `Launcher.HomeAlreadyAdopted` 写错：
   ```csharp
   Instances().Any(i => i.Adopted || SamePath(i.Home, home))   // 旧
   ```
   "存在**任意一份** adopted 实例"就算"这个 home 已收编" → 收编提示永远不弹、正确的 home 永远进不来。
4. 旧 DSH Desktop 还开着（`DSH_HOME` 指向旧 home），所以「本机 DSH」的绿灯是真的——
   症状被盖住，直到有人问"目前有哪些对话在跑"。
5. D55 的横幅只是把这个错位**摆出来**让人点「收编」——那还是手动。

## 修法（不需要用户做任何事）

1. `HomeAlreadyAdopted` 只比 home 本身：
   `Instances().Any(i => LaunchPolicy.SamePath(i.Home, home))`。
2. 新增 `Launcher.ReconcileMachineHome(out from, out to, Inventory? inventory)`：
   - 目标 home 的来源：`pad.json#launch.dshHome` → 进程现场 `DSH_HOME`（`RunWatch.ProbeHomes`）→
     再回落配置；只认 `DshSniff.LooksLikeHome` 通过的目录；
   - 已经有实例指向它就什么都不做；
   - **只有恰好一份 `adopted` 实例**时才动（多份就是猜，宁可不改）；
   - 改指时顺手把钉的内核对齐到"这份 home 实际在跑的那个壳"（`Discover` 的 `Shell.KernelVersion`），
     但**只在 PAD 版本库里真的装着这个发行号时**才改——不给自己造一个装不上的版本；
   - 重算 `adoptedFingerprint`，`SaveInstance` 落盘（`RewriteLaunchScripts` 一并重生）。
3. 启动时 `MainWindow.AutoReconcileHome()`：后台 `Discover.Run` + 对账，改指了就 `Reload()` +
   toast `本机 DSH 已跟随到 …`；**先对账，再考虑手动收编兜底**（`OfferAdoptIfNeeded` 只处理对账后仍无解的情况）。
4. **删除 D55 的横幅与「收编」按钮**（WorkbenchView 的 `OtherHomesBar` / `RefreshOtherHomesAsync` /
   `AdoptOther_Click`）：根因修好后，不再需要让人替 PAD 收拾指针。

## 实测（同一次重启，无人工干预）

`instances/dsh/instance.json`：

```
before: home = C:\Users\Brs\AppData\Roaming\dsh-desktop\harness   version = 0.1.0-rc.7
after : home = E:\DSH-Home                                        version = 0.2.1-rc.2
        adoptedFingerprint 已重算，updated 已更新
```

界面（`project-live-home.png`，真实窗口，UIA 点击）：

- 页头：`本机 DSH 0.2.1-rc.2 · 在跑 · dsh/workspace`
- 运行：`在跑` + 灯 + `证据：进程现场`（不是 PAD 起的，所以没有端口/pid）
- DSH 内核：`0.2.1-rc.2`（自动对齐到在跑的壳；`版本库 已装`）
- 组合包：`desktop 14 个`
- 对话：`1 条对话在执行 / 共 185 条 · 最近 20 秒前`，
  行：`打开PAD（20 秒前，亮灯）`、`打开PAD (2)（3 分钟前）`、`联网调研任务（36 分钟前）×2`，
  脚注仍写明 `管理口没连上…`
- 横幅消失（不再有"没进 PAD 的 home"）。

验证：`dotnet build` 0 warning / 0 error；pad-window 新增 D56 断言（`HomeAlreadyAdopted` 只比 home、
`ReconcileMachineHome` 读 `ProbeHomes`/`Launch.DshHome`、恰好一份 adopted、`SaveInstance`、
内核 `KernelVersion` 对齐、启动先 `AutoReconcileHome`），pad-window / pad-depth / pad-pack 绿。
证据：`EVIDENCE/D56-auto-reconcile/`。

## 边界

- 对账只动"机器级那份 adopted 实例"（恰好一份时）；用户自己建的实例（123 / pack-Test / …）永远不动。
- 内核钉只在本机版本库有该发行号时对齐；没有就保持原钉，并在卡片上照实显示（不假装）。
- 旧 home 不再被 PAD 当作实例；旧 DSH Desktop 若还在跑，只是普通进程，不再影响「本机 DSH」的语义。
- D55 的「对话」卡保留（它回答的是"有哪些对话、哪几条在执行"）；D55 的横幅按本条删除。
