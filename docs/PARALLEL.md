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

## 附：最后统一编译的口令

```powershell
# Lead only, once, after merges:
dotnet build agent-pack-dsh\pad\Pad.csproj -c Debug -v m   # 0 warning / 0 error
bun run agent-pack-dsh/modpack/pad-window.test.ts
# 然后重启正式 PAD 并截图验收
```
