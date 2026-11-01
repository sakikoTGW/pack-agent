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

## 补：首次点 X 的说明卡（不再提醒）

按需求补上「第一次点 X 时解释一次，并给不再提醒的选择」：

- `MainWindow.HideToTray`：若 `ui.trayHintDismissed == false`，先弹 PAD 自己的卡片：
  标题「PAD 会留在托盘」，正文说明「点 X 只是收进托盘，PAD 与正在跑的实例都不停；双击托盘恢复，右键菜单退出」，
  按钮：**不再提醒**（持久化 `trayHintDismissed=true`）/ **每次提醒**（不写，保持下次还弹）。
- 托盘气泡改为不再使用（只在卡片里说清楚，避免重复打扰）。
- 已验证（`02-tray-hint-test.txt`）：首次弹卡→选不再提醒→窗口收托盘且 pad.json 记住；再次点 X 直接收托盘不弹；
  选每次提醒则不落盘，下次还会提示。
- `--shot` 截图模式不弹（`App.ShotPath is null` 条件）。
