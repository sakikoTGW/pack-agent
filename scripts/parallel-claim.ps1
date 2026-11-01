<#
.SYNOPSIS
  并行开发占位（worktree / branch / 输出目录 / 数据根），一个 agent 一个固定命名，避免同名撞车。

.DESCRIPTION
  执行前会检查：分支是否已存在、worktree 路径是否已被占用、是否已有同 scope 的 claim。
  通过后写 .parallel/claims/<slug>.json（提交进仓库，其他 agent 可见）。

  命名规约（PARALLEL.md §1）：
    slug     = <agent>-<task>（小写、字母数字与连字符）
    branch   = pad/<slug>
    worktree = E:\worktrees\<slug>
    build    = E:\tmp\pad-build\<slug>
    data     = E:\tmp\pad-roots\<slug>

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent lead -Task ui-detail -Scope "pad/Views,pad/Core/PluginList.cs" -Create
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -List
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/parallel-claim.ps1 -Agent lead -Task ui-detail -Release
#>
param(
  [string]$Agent,
  [string]$Task,
  [string]$Scope = "",
  [switch]$Create,
  [switch]$Release,
  [switch]$List,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function RepoRoot {
  $root = & git -C (Get-Location) rev-parse --show-toplevel 2>$null
  if (-not $root) { throw "不在 git 仓库里（先 cd 到 E:\pack-agent 或 worktree 根）" }
  return $root.Trim()
}

function Slug([string]$agent, [string]$task) {
  $s = ("$agent-$task").ToLowerInvariant()
  $s = [regex]::Replace($s, '[^a-z0-9-]+', '-')
  $s = [regex]::Replace($s, '-+', '-').Trim('-')
  if (-not $s) { throw "Agent/Task 不能为空或全是符号" }
  return $s
}

$root = RepoRoot
$claimDir = Join-Path $root '.parallel/claims'

if ($List) {
  if (-not (Test-Path $claimDir)) { "没有任何并行 claim。"; exit 0 }
  $rows = Get-ChildItem $claimDir -Filter '*.json' | ForEach-Object {
    try { Get-Content $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $null }
  } | Where-Object { $_ }
  if (-not $rows) { "没有任何并行 claim。"; exit 0 }
  $rows | Select-Object agent, task, branch, worktree, scope, base, created_at | Format-Table -AutoSize
  exit 0
}

if (-not $Agent -or -not $Task) { throw "用法：-Agent <名字> -Task <短任务名> [-Scope <文件范围>] [-Create] [-Release] [-List]" }
$slug = Slug $Agent $Task
$branch = "pad/$slug"
$worktree = "E:\worktrees\$slug"
$buildOut = "E:\tmp\pad-build\$slug"
$dataRoot = "E:\tmp\pad-roots\$slug"
$claimFile = Join-Path $claimDir "$slug.json"

if ($Release) {
  if (Test-Path $claimFile) {
    if ($DryRun) { "DRY: would remove $claimFile"; exit 0 }
    Remove-Item $claimFile -Force
    "claim 已释放：$slug（分支/worktree 是否已合并删除由 Lead 决定）"
  } else {
    "没有这个 claim：$slug"
  }
  exit 0
}

# ---- 冲突检查 ----
$problems = @()

& git -C $root rev-parse --verify --quiet "refs/heads/$branch" | Out-Null
if ($LASTEXITCODE -eq 0) { $problems += "分支已存在：$branch" }

if (Test-Path $worktree) { $problems += "worktree 已存在：$worktree" }

$wt = & git -C $root worktree list --porcelain 2>$null
if ($wt -match [regex]::Escape($worktree) -or $wt -match [regex]::Escape($branch)) {
  $problems += "git worktree list 里已占用：$branch / $worktree"
}

if (Test-Path $claimFile) { $problems += "claim 已存在：$claimFile（-List 查看）" }

if ($problems.Count -gt 0) {
  "占位失败："
  $problems | ForEach-Object { "  - $_" }
  exit 1
}

# ---- 写 claim ----
$base = (& git -C $root rev-parse --short HEAD).Trim()
$payload = [ordered]@{
  agent      = $Agent
  task       = $Task
  scope      = $Scope
  branch     = $branch
  worktree   = $worktree
  build_out  = $buildOut
  data_root  = $dataRoot
  base       = $base
  created_at = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
}

if ($DryRun) {
  "DRY: " + ($payload | ConvertTo-Json -Compress)
  exit 0
}

New-Item -ItemType Directory -Force -Path $claimDir | Out-Null
$payload | ConvertTo-Json -Depth 4 | Set-Content -Path $claimFile -Encoding UTF8

if ($Create) {
  & git -C $root worktree add $worktree -b $branch $base | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "git worktree add 失败：$worktree / $branch" }
  "worktree 已创建：$worktree（分支 $branch，基线 $base）"
}

"占位成功：$slug"
"  branch   = $branch"
"  worktree = $worktree"
"  build    = $buildOut"
"  data     = $dataRoot"
"  claim    = $claimFile"
"下一步请把你的写操作限制在 -Scope 声明的文件里；合并前跑 -List 和 PARALLEL.md 的合并清单。"
