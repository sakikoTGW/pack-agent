# 多 agent 并行约定（编译隔离 / 数据隔离 / 不杀别人 / 仓库协调 / 验收定义）

date: 2026-11-01
scope: 本仓库（E:\pack-agent）与 PAD（WPF）的并行开发

多个 agent 同时在同一个仓库、同一台机器上做 PAD 时，互相踩的只有三处：**编译产物、运行数据、运行中的窗口**。
下面五条让它们互不影响；最后再统一编译一次。

## 1. 编译隔离 + 唯一命名（先占位）

- **先占位再开工**，不要手写路径/分支名：
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent <名字> -Task <短任务> -Scope "<要改的文件或目录>" -Create
  ```
  脚本生成并写 `.parallel/claims/<slug>.json`：分支 `pad/<agent>-<task>`、worktree `E:\worktrees\<agent>-<task>`、
  构建输出 `E:\tmp\pad-build\<agent>-<task>`、数据根 `E:\tmp\pad-roots\<agent>-<task>`。
- 命名规则（脚本生成，不手写）：slug=`<agent>-<task>`，其余路径都由 slug 派生。
- **禁止复用**别人的分支 / worktree / 输出目录；脚本会检查 `git worktree list`、`refs/heads/<branch>`、已有 claim 并拒绝。
- **不要共用** `E:\tmp\pad-build\lead` 这类公共输出目录：exe 会被别人的 PAD 进程锁住（2026-11-01 已发生）。
- 每个 agent **不写共享的** `agent-pack-dsh/pad/bin` `obj`；不要在共享检出里直接 `dotnet build`；不要边跑 PAD 边编它正在用的 exe。
- **最后一次编译只允许一个人跑**（Lead），其他 agent 只提交。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent <名字> -Task <短任务> -Scope "<范围>" -Create
cd E:\worktrees\<agent>-<task>
dotnet build agent-pack-dsh\pad\Pad.csproj -c Debug -o E:\tmp\pad-build\<agent>-<task>
```

## 2. 数据隔离

- 验收窗口用 `PACK_LAUNCHER_ROOT` 指向自己的根（`E:\tmp\pad-roots\<agent>-<task>`，与 claim 一致），不写别人的 `runtime.json`/`pad.json`：
  ```powershell
  $root = "E:\tmp\pad-roots\<agent>-<task>"
  New-Item -ItemType Directory -Force $root | Out-Null
  $real = "E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher"
  foreach ($d in "versions","instances","library") {
    $to = Join-Path $root $d
    if (-not (Test-Path $to)) { New-Item -ItemType Junction -Path $to -Target (Join-Path $real $d) | Out-Null }
  }
  Copy-Item (Join-Path $real "pad.json") $root -Force -ErrorAction SilentlyContinue
  $env:PACK_LAUNCHER_ROOT = $root
  & "E:\tmp\pad-build\<agent>-<task>\pack-agent-for DSH.exe"
  ```
- 大目录（versions / instances / library）用 junction 共享；只把小的运行态文件（runtime.json、pad.json）留成各自副本。
- 会破坏数据的操作（删实例、装包、改 home）只在**自己的数据根**里做，绝不指向共享根。

## 3. 不杀别人

- 只关自己 `Start-Process` 起来的 pid，记下 pid 再关。
- 不关闭/重启不是自己起的 PAD 窗口；共享的"正式 PAD"任何 agent 都不许动。
- 不 `Stop-Process -Name`、不按窗口标题乱关；不用 `taskkill /IM`。

## 4. 仓库协调（分支归属 + 合并审查）

- **一个分支只属于 claim 的 agent**；不许往别人的分支提交，也不许在别人的 worktree 里改文件。
- 同一文件同时只允许一个 agent 改；开工前把 `-Scope` 写进 claim。
- 小步提交；提交前 `git status` 看清只包含自己的改动；不 `rebase`/`force push` 共享分支。
- **Lead 合并前必须审查分支内容**（防止把别人的提交混进合并，2026-11-01 已发生）：
  ```powershell
  git log <claim.base>..<branch> --oneline    # 必须只有该 agent 的提交
  git diff --stat <claim.base>..<branch>      # 必须落在 claim 的 scope 内
  ```
  不符合就停下来问，不要 merge。
- 合并后 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent <名字> -Task <短任务> -Release` 释放占位；
  分支/worktree 由 Lead 决定是否删除。
- 别人正在改的文件，先用 `git status` / `git diff` 确认，冲突时由 Lead 裁决。
- 事故记录：`.parallel/claims/README.md`。

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
$env:PACK_LAUNCHER_ROOT = "E:\tmp\pad-roots\<agent>-<task>"
$p = Start-Process "E:\tmp\pad-build\<agent>-<task>\pack-agent-for DSH.exe" -PassThru
# 只操作 $p.Id；验收完自己关掉
```

dev 窗口启动时尽量 `-WindowStyle Minimized` 或用 `SW_SHOWNOACTIVATE` 显示，不抢用户前台。

## 附：合并清单（Lead）

```powershell
# 1) 看现在有哪些 claim / 分支
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -List
git worktree list

# 2) 审分支：只应该有该 agent 的提交，且落在 scope 内
git log <claim.base>..<branch> --oneline
git diff --stat <claim.base>..<branch>

# 3) 合并后释放占位
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent <名字> -Task <短任务> -Release
```

## 附：最后统一编译的口令

```powershell
# Lead only, once, after merges（先问用户"现在验收吗"）:
dotnet build agent-pack-dsh\pad\Pad.csproj -c Debug -v m   # 0 warning / 0 error
bun run agent-pack-dsh/modpack/pad-window.test.ts
# 然后重启正式 PAD 并截图验收（仅此一次，且经用户允许）
```
