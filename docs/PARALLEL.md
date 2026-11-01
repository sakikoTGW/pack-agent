# 多 agent 并行约定（编译隔离 / 数据隔离 / 不杀别人 / 仓库协调 / 验收定义）

date: 2026-11-01
scope: 本仓库（E:\pack-agent）与 PAD（WPF）的并行开发

多个 agent 同时在同一个仓库、同一台机器上做 PAD 时，互相踩的只有三处：**编译产物、运行数据、运行中的窗口**。
下面五条让它们互不影响；最后再统一编译一次。

## 1. 编译隔离

- 每个 agent **不写共享的** `agent-pack-dsh/pad/bin` `obj`；各自用 **git worktree + 自己的输出目录**：
  ```powershell
  git worktree add E:\worktrees\pad-<agent> <branch>
  cd E:\worktrees\pad-<agent>
  dotnet build agent-pack-dsh\pad\Pad.csproj -c Debug -o E:\tmp\pad-build\<agent>
  ```
- 不要在共享检出里直接 `dotnet build`；也不要边跑 PAD 边编到它正在用的 exe（文件锁）。
- **最后一次编译只允许一个人跑**（Lead），其他 agent 只提交。并发 `dotnet build` 会抢 `obj/`，所以各自 worktree 是硬要求。

## 2. 数据隔离

- 验收窗口用 `PACK_LAUNCHER_ROOT` 指向自己的根，不写别人的 `runtime.json`/`pad.json`：
  ```powershell
  $root = "E:\tmp\pad-roots\<agent>"
  New-Item -ItemType Directory -Force $root | Out-Null
  $real = "E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher"
  foreach ($d in "versions","instances","library") {
    $to = Join-Path $root $d
    if (-not (Test-Path $to)) { New-Item -ItemType Junction -Path $to -Target (Join-Path $real $d) | Out-Null }
  }
  Copy-Item (Join-Path $real "pad.json") $root -Force -ErrorAction SilentlyContinue
  $env:PACK_LAUNCHER_ROOT = $root
  & "E:\tmp\pad-build\<agent>\pack-agent-for DSH.exe"
  ```
- 大目录（versions / instances / library）用 junction 共享；只把小的运行态文件（runtime.json、pad.json）留成各自副本。
- 会破坏数据的操作（删实例、装包、改 home）只在**自己的数据根**里做，绝不指向共享根。

## 3. 不杀别人

- 只关自己 `Start-Process` 起来的 pid，记下 pid 再关。
- 不关闭/重启不是自己起的 PAD 窗口；共享的"正式 PAD"任何 agent 都不许动。
- 不 `Stop-Process -Name`、不按窗口标题乱关；不用 `taskkill /IM`。

## 4. 仓库协调

- 每个 agent 一个 worktree（或事先认领文件范围）；**同一文件同时只允许一个 agent 改**。
- 小步提交、提交前 `git status` 看清只包含自己的改动；不 `rebase`/`force push` 共享分支。
- Lead 负责合并；合并后再做最后一次统一编译。
- 别人正在改的文件，先用 `git status` / `git diff` 确认，冲突时由 Lead 裁决。

## 5. 验收定义

- "做完了" = **自己起的窗口**（自己的 build 目录、自己的数据根）+ 截图 + pid + 一句"怎么点"。
- 不把"代码改了/编译过了"当成验收；也不拿别人的窗口冒充自己的结果。
- 最终验收：Lead 在合并后**只做一次**：关正式 PAD → 编译 → 测试 → 启动 → 截图；其他 agent 此时不编译、不重启。
## 6. 前台窗口与截图（不打扰用户）

用户在用电脑时，**任何 agent 都不得把 PAD 拉到前台、不得自动点击/重启正式 PAD**。
这一条比"拿到证据"优先；没有允许就不动屏幕。

### 默认：后台取证

- 截图用 **后台 `PrintWindow(hwnd, PW_RENDERFULLCONTENT)`**，或 PAD 自己的 `--shot` 自截图；
  **不调用** `SetForegroundWindow` / `BringWindowToTop` / `ShowWindow(SW_RESTORE)` / `AttachThreadInput` 来抢焦点。
- PAD 已有 `--shot` / `--shot-page` 通道：能自己渲染成 PNG 的页面优先用它（进程自己截图，不打扰桌面）。
- WebView2 是独立合成层：`PrintWindow` 拍不到；用 PAD 内置的 `CoreWebView2.CapturePreviewAsync`（`RelationsWebView` 里已有实现）
  做离屏截图，**不要**为了截 WebView2 去前台摆窗口。

### 什么时候允许前台（必须同时满足）

1. 用户明确说"现在验收/现在给我看"；
2. 同一轮最多一次；先告诉用户"要前置一次窗口"，得到允许再动；
3. 记录操作前的原前台窗口，验收完 **把焦点还回去**，不顺手做别的点击。

### 禁止清单

- 不允许：`Start-Process` 之后自动 `Focus-Pad` / 循环置顶 / 抢焦点点击；
- 不允许：为了截图关闭、重启或移动"正式 PAD"；
- 不允许：每轮都截图；证据只在用户要看或自己验证功能时生成，放 `EVIDENCE/`，正文给路径，不弹窗；
- 不允许：拿"前台窗口截图"当唯一证据——优先后台截图 + pid + 文件路径。

### 隔离窗口

需要自己动手点的时候，用**自己的 dev 窗口**（见第 2 节数据隔离）：

```powershell
$env:PACK_LAUNCHER_ROOT = "E:\tmp\pad-roots\<agent>"
$p = Start-Process "E:\tmp\pad-build\<agent>\pack-agent-for DSH.exe" -PassThru
# 只操作 $p.Id；验收完自己关掉
```

dev 窗口启动时尽量 `-WindowStyle Minimized` 或用 `SW_SHOWNOACTIVATE` 显示，不抢用户前台。

## 附：最后统一编译的口令

```powershell
# Lead only, once, after merges（先问用户"现在验收吗"）:
dotnet build agent-pack-dsh\pad\Pad.csproj -c Debug -v m   # 0 warning / 0 error
bun run agent-pack-dsh/modpack/pad-window.test.ts
# 然后重启正式 PAD 并截图验收（仅此一次，且经用户允许）
```
