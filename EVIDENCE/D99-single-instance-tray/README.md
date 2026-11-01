# D99 · 单实例锁 + 托盘化

日期：2026-11-02
分支：lock/tray

## 1) 单实例锁（同一个 launcher root 只允许一个窗口写）

- `Core/SingleInstance.cs`：
  - 按 root 规范化路径（全路径小写 + SHA256 前 8 字节）生成 `Local\PAD-launcher-<key>` 命名 Mutex；
  - 抢到锁 = 第一个实例，往 `<root>/pad.lock` 写 pid / exe / 时间（给人看的诊断，mutex 才是权威）；
  - 抢不到 = 第二个实例：通过命名管道 `PAD-launcher-<key>` 给第一个发 `show`，让它的窗口从最小化/托盘恢复，然后自己 `Shutdown(0)`；
  - 退出时释放 mutex 并删自己的 `pad.lock`。
- **CLI 不参与锁**：`pad cli …` 无窗口，自动化/脚本照旧；已验证 GUI 开着时同 root 的 `cli status` 正常。
- 不同 root 互不影响（两个实例各开一个窗口）。

## 2) 托盘化（Win32 Shell_NotifyIcon，不引 WinForms）

- `Core/Tray.cs`：托盘图标 + 右键菜单「打开 PAD / 关闭窗口时最小化到托盘（勾选）/ 退出 PAD」；
  - 双击图标 = 打开；
  - 第一次缩托盘弹一次气泡「PAD 还在运行」；
  - 图标直接从 exe 资源提取（`ExtractIconEx`），不额外带文件。
- 关窗行为：`MainWindow.RequestClose/OnClosing` → 默认 `ui.closeToTray=true` 时隐藏窗口（进程继续跑，托盘可恢复/退出）；
  - `ui.closeToTray=false` 时 X/Alt+F4 直接退出；
  - `--shot` 截图模式不受影响（ShotExit 仍正常关）。
- 设置：`设置 → 个性化` 新增「关闭窗口时缩到托盘…」开关（pad.json `ui.closeToTray`，默认 true）；托盘菜单勾选与设置同步。

## 验证（隔离 root，不动用户正在跑的 PAD）

见 `01-test-output.txt`，实测：

| 场景 | 结果 |
|---|---|
| 启动第一个实例 | 窗口可见，`pad.lock` 写入，托盘图标存在（`Shell_NotifyIconGetRect hr=0`） |
| 同 root 再启动一个 | 第二个进程自动退出；老窗口可见（被 `show` 唤出） |
| GUI 运行时 `pad cli status` 同 root | exit 0，正常读账本（CLI 不被锁） |
| 发 WM_CLOSE（=点 X） | 窗口隐藏、进程仍在、托盘图标仍在 |
| 窗口在托盘时再启动一个 | 第二个退出；窗口恢复可见 |
| 两个不同 root 各开一个 | 两个进程都存活（互不影响） |
| pad.json `closeToTray=false` + WM_CLOSE | 进程退出（真正关闭） |
| 回归 | `pad-window` / `pad-pack` / `p2-p3` 全绿 |

## 边界

- 锁的是**窗口实例**；CLI 仍可直接写同 root（保留给自动化）。若要严格到「GUI 开着时 CLI 写操作也排队/拒绝」，可下一步加。
- 退出 PAD 不会停正在跑的实例（和 PCL 一样：启动器退出 ≠ 游戏停）。

## 补：首次点 X 的说明卡（最终版）

- 第一次点 X（且 `ui.trayHintDismissed=false`）弹 PAD 自己的卡片：
  - 标题：**PAD 已收进托盘**
  - 正文：点 X 只是把窗口收进托盘，PAD 和正在跑的实例都没停。双击托盘图标恢复窗口，右键托盘图标可以退出。
  - 底部：复选框 **不再提醒** + 按钮 **好的**（Esc/关闭同样只是收托盘，不勾选就不落盘）
- 勾上不再提醒并点好的 → 写 `ui.trayHintDismissed=true`，以后点 X 直接收托盘不再弹。
- 去掉托盘气泡（避免重复打扰）；`--shot` 截图模式不弹卡。
- 验证：`02-tray-hint-test.txt`（不勾=不落盘；勾选=落盘且后续不再弹；键盘 Enter / UIA SetFocus 均可关闭）。

## 补 2：关闭行为设置 + 提示卡最终版

- 提示卡（03 图）：标题「⚠ 注意喵：」；正文说明只收托盘；**点我跳转**链接打开 设置 → 其他 → 关闭行为；
  复选框「不再提醒」；按钮「好的」「我只是手滑了.....」。
  - 好的 = 收托盘；手滑了 = 窗口留着；链接 = 窗口留着并跳到设置；不再提醒只影响后续是否弹卡。
- 设置 → 其他 → **关闭行为**（04 图）：
  - 「点关闭按钮时」：最小化到托盘（默认）/ 退出 PAD → `ui.closeToTray`
  - 「退出 PAD 时停止所有正在运行的实例」→ `other.stopInstancesOnExit`（默认关）
- 退出路径全部接线：X / Alt+F4 走 OnClosing，托盘「退出 PAD」走 ExitGracefully；勾了停实例就先停再退。
- 验证：`05-exit-stop-test.txt`（保留=实例活；停=实例被杀且 runtime.json 清空）、`06-settings-ui-test.txt`（UI 改设置落盘）、`07-hint-behavior.txt`。
