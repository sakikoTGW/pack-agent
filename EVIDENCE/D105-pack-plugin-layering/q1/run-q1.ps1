# Q1 gate (docs/pack-plugin-layering.md 搂6 P2 / 搂7 Q1):
# take a bundle-bearing plugin OUT of dsh.profile.bundles but leave it in
# dependencies, run an unrelated `dsh plugin add` and `dsh plugin remove`,
# and prove DSH's reconcile() never re-adds the retained dependency.
#
# Scratch root only: <repo>\.tmp\q1-pack-ledger. `versions` is a junction to the
# live version store (read-only use). The live launcher root is never written.
#
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File run-q1.ps1
$ErrorActionPreference = 'Continue'
$scratch = 'E:\pack-agent\.tmp\q1-pack-ledger'
$live = 'E:\pack-agent\agent-pack-dsh\pad\bin\Debug\net9.0-windows\.pack-launcher'
$root = Join-Path $scratch '.pack-launcher'
$dshHome = Join-Path $root 'instances\q1\home'
$ws = Join-Path $root 'instances\q1\workspace'
$pkg = Join-Path $dshHome 'profiles\web\package.json'
$bin = Join-Path $live 'versions\0.2.1-rc.2\node_modules\@deepseek-ai\dsh\lib\bin.js'
$target = '@sakikotgw/pack-agent-dsh'   # bundle-bearing plugin under test
$unrelated = 'zod'                      # plain dependency, no dsh.bundle
$evidence = $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$fail = 0

function Say($msg) { Write-Output $msg }
function Check($name, $ok) {
  if ($ok) { Say "PASS  $name" }
  else { $fail++; Say "FAIL  $name" }
}
function ReadPkg { (Get-Content $pkg -Raw) | ConvertFrom-Json }
function Bundles { (ReadPkg).dsh.profile.bundles }
function Deps { (ReadPkg).dependencies.PSObject.Properties.Name }
function DumpBundles { 'bundles: [' + ((Bundles) -join ', ') + ']' ; 'deps:    [' + ((Deps) -join ', ') + ']' }

# A profile manifest edit in the same shape PAD's Trial.WriteBundles uses:
# rewrite dsh.profile.bundles only, leave dependencies untouched.
function SetBundles([string[]]$wanted) {
  $list = ($wanted | ForEach-Object { "'$_'" }) -join ','
  $js = "const fs=require('fs');const p=process.argv[1];const j=JSON.parse(fs.readFileSync(p,'utf8').replace(/^\uFEFF/,''));j.dsh.profile.bundles=[$list];fs.writeFileSync(p,JSON.stringify(j,null,2)+'\n','utf8');"
  node -e $js $pkg
}

# ---- 0. scratch root ------------------------------------------------------
if (Test-Path $scratch) { Remove-Item -Recurse -Force $scratch }
New-Item -ItemType Directory -Force -Path (Join-Path $dshHome 'sessions'), $ws, (Join-Path $root 'library\packs') | Out-Null
cmd /c mklink /J "$root\versions" "$live\versions" | Out-Null
[IO.File]::WriteAllText((Join-Path $root 'launcher.json'), (@{
  schema = 'pack-agent.launcher/v1'; dataRoot = $root
  defaults = @{ profile = 'web'; isolation = $true; workspaceKind = 'owned'; telemetryDisabled = $true }
  update = @{ channel = 'stable' }; adoptedHome = $null
  created = '2026-11-04T00:00:00.000Z'; updated = '2026-11-04T00:00:00.000Z'
} | ConvertTo-Json -Depth 6), $utf8)
[IO.File]::WriteAllText((Join-Path $root 'instances\q1\instance.json'), (@{
  schema = 'pack-agent.launcher.instance/v1'; id = 'q1'; name = 'q1'
  dsh = @{ version = '0.2.1-rc.2' }; profile = @{ name = 'web'; port = 'auto' }
  workspace = @{ kind = 'owned'; path = $ws }; status = 'ready'; home = $dshHome
  created = '2026-11-04T00:00:00.000Z'; updated = '2026-11-04T00:00:00.000Z'
} | ConvertTo-Json -Depth 6), $utf8)

$env:DSH_HOME = $dshHome
$env:DSH_TELEMETRY_DISABLED = '1'
$env:npm_config_ignore_workspace_root_check = 'true'
$env:NPM_CONFIG_IGNORE_WORKSPACE_ROOT_CHECK = 'true'

Say "== scratch root: $root"
Say "== dsh:           $bin"
Say "== DSH_HOME:      $dshHome"
Push-Location $ws
try {

# ---- 1. install the bundle-bearing plugin through DSH itself --------------
Say ''
Say '== [1] dsh plugin --profile web add @sakikotgw/pack-agent-dsh'
node $bin plugin --profile web add $target *> (Join-Path $scratch '01-add.out.txt')
Say "exit=$LASTEXITCODE"
Say ((Get-Content (Join-Path $scratch '01-add.out.txt') -Raw).Trim())
$profileCreated = Test-Path $pkg
Check 'profile web is created with dsh.profile.bundles' $profileCreated
Check "$target is in dependencies" ((Deps) -contains $target)
Check "$target is in bundles (baseline)" ((Bundles) -contains $target)
DumpBundles

# ---- 2. baseline dump-config: the bundle layer must be composed ------------
Say ''
Say '== [2] dsh --dump-config --profile web  (baseline)'
$dumpBase = Join-Path $scratch '02-dump-baseline.yml'
node $bin --dump-config --profile web *> $dumpBase
Say "exit=$LASTEXITCODE lines=$((Get-Content $dumpBase).Count)"
$baseHit = (Select-String -Path $dumpBase -Pattern ([regex]::Escape("== $target"))).Count
Check "baseline dump-config carries the '$target' layer" ($baseHit -gt 0)

# ---- 3. strip the bundle row, keep the dependency -------------------------
Say ''
Say '== [3] manual edit: remove the bundle row, keep dependencies'
SetBundles @((Bundles) | Where-Object { $_ -ne $target })
DumpBundles
Check "$target still in dependencies" ((Deps) -contains $target)
Check "$target no longer in bundles" (-not ((Bundles) -contains $target))

# ---- 4. unrelated dsh plugin add (new dependency -> reconcile runs) -------
Say ''
Say "== [4] dsh plugin --profile web add $unrelated   (unrelated add)"
node $bin plugin --profile web add $unrelated *> (Join-Path $scratch '04-add-unrelated.out.txt')
Say "exit=$LASTEXITCODE"
Say ((Get-Content (Join-Path $scratch '04-add-unrelated.out.txt') -Raw).Trim())
DumpBundles
Check "unrelated add: $target NOT re-added to bundles" (-not ((Bundles) -contains $target))
Check "unrelated add: $target kept in dependencies" ((Deps) -contains $target)
Check "unrelated add: $unrelated is a dependency now" ((Deps) -contains $unrelated)

# ---- 5. unrelated dsh plugin remove (reconcile runs again) ----------------
Say ''
Say "== [5] dsh plugin --profile web remove $unrelated   (unrelated remove)"
node $bin plugin --profile web remove $unrelated *> (Join-Path $scratch '05-remove-unrelated.out.txt')
Say "exit=$LASTEXITCODE"
Say ((Get-Content (Join-Path $scratch '05-remove-unrelated.out.txt') -Raw).Trim())
DumpBundles
Check "unrelated remove: $target NOT re-added to bundles" (-not ((Bundles) -contains $target))
Check "unrelated remove: $unrelated is gone from dependencies" (-not ((Deps) -contains $unrelated))

# ---- 6. dump-config after the strip: the layer must be absent -------------
Say ''
Say '== [6] dsh --dump-config --profile web  (bundle stripped)'
$dumpOff = Join-Path $scratch '06-dump-stripped.yml'
node $bin --dump-config --profile web *> $dumpOff
Say "exit=$LASTEXITCODE lines=$((Get-Content $dumpOff).Count)"
$offHit = (Select-String -Path $dumpOff -Pattern ([regex]::Escape("== $target"))).Count
Check "stripped dump-config has NO '$target' layer" ($offHit -eq 0)
Check "stripped dump is shorter than baseline" ((Get-Content $dumpOff).Count -lt (Get-Content $dumpBase).Count)

# ---- 7. put the row back by hand: dump-config must recover ----------------
Say ''
Say '== [7] manual edit: add the bundle row back'
$back = @((Bundles)); $back += $target
SetBundles $back
DumpBundles
$dumpOn = Join-Path $scratch '07-dump-restored.yml'
node $bin --dump-config --profile web *> $dumpOn
Say "exit=$LASTEXITCODE lines=$((Get-Content $dumpOn).Count)"
$onHit = (Select-String -Path $dumpOn -Pattern ([regex]::Escape("== $target"))).Count
Check "'$target' layer is composed again" ($onHit -gt 0)
Check "restored dump matches the baseline layer set" ((Get-Content $dumpOn).Count -eq (Get-Content $dumpBase).Count)

Say ''
Say "== RESULT: $(if ($fail -eq 0) { 'Q1 PASS - strip bundles / keep deps survives unrelated dsh plugin add + remove' } else { "$fail CHECK(S) FAILED" })"

} finally {
  Pop-Location
  Copy-Item (Join-Path $scratch '*.yml') $evidence -Force -ErrorAction SilentlyContinue
  Copy-Item (Join-Path $scratch '*.out.txt') $evidence -Force -ErrorAction SilentlyContinue
}
if ($fail -ne 0) { exit 1 }

