param(
    [string]$Profile = "E:\DSH-Home\profiles\desktop",
    [string]$Repo = "E:\pack-agent\agent-pack-dsh",
    [switch]$Rollback
)

$ErrorActionPreference = "Stop"
function Fail($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

$pkg = Join-Path $Profile "package.json"
if (-not (Test-Path $pkg)) { Fail "no profile at $Profile" }

if ($Rollback) {
    $backup = Get-ChildItem $Profile -File -Filter "package.json.bak-pad-jump*" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $backup) { Fail "no package.json.bak-pad-jump* backup found" }
    Copy-Item $backup.FullName $pkg -Force
    $nm = Join-Path $Profile "node_modules\@sakikotgw"
    if (Test-Path $nm) { Remove-Item $nm -Recurse -Force }
    Write-Host "rolled back: restored $($backup.Name), removed @sakikotgw packages"
    Write-Host "restart the official desktop shell to apply."
    exit 0
}

# Backup once per staging session
$backupPath = "$pkg.bak-pad-jump"
if (-not (Test-Path $backupPath)) {
    Copy-Item $pkg $backupPath -Force
    Write-Host "backup: $backupPath"
}

# File-level stage (no pnpm: do not touch the running host's dependency graph)
$pkgs = Join-Path $Profile "node_modules\@sakikotgw"
New-Item -ItemType Directory -Force $pkgs | Out-Null
foreach ($pair in @(
    @{ Name = "pad-session-jump"; Src = Join-Path $Repo "session-jump" },
    @{ Name = "pad-gateway-jump"; Src = Join-Path $Repo "gateway-jump" }
)) {
    if (-not (Test-Path $pair.Src)) { Fail "source package missing: $($pair.Src)" }
    $dst = Join-Path $pkgs $pair.Name
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item (Join-Path $pair.Src "package.json") $dst
    Copy-Item (Join-Path $pair.Src "cordis.patch.yml") $dst
    Copy-Item (Join-Path $pair.Src "README.md") $dst
    Copy-Item (Join-Path $pair.Src "lib") $dst -Recurse
    Write-Host "staged $($pair.Name)"
}

# Patch manifest as JSON, preserving validity (no BOM)
$json = Get-Content $pkg -Raw | ConvertFrom-Json
$deps = $json.dependencies
foreach ($d in @(
    @{ n = "@sakikotgw/pad-session-jump"; v = "0.2.1" },
    @{ n = "@sakikotgw/pad-gateway-jump"; v = "0.1.1" }
)) {
    $deps | Add-Member -NotePropertyName $d.n -NotePropertyValue $d.v -Force
}
$bundles = @($json.dsh.profile.bundles)
foreach ($b in @("@sakikotgw/pad-session-jump", "@sakikotgw/pad-gateway-jump")) {
    if ($bundles -notcontains $b) { $bundles += $b }
}
$json.dsh.profile.bundles = $bundles
$out = $json | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($pkg, $out, (New-Object System.Text.UTF8Encoding($false)))

# Validating re-parse proves we did not break the manifest.
$null = Get-Content $pkg -Raw | ConvertFrom-Json
Write-Host "profile manifest patched and re-parsed OK."

Write-Host ""
Write-Host "next steps:" -ForegroundColor Cyan
Write-Host "  1) restart the official DeepSeek Harness shell (this is what loads the plugins)"
Write-Host "  2) powershell -File $PSScriptRoot\run-after-restart.ps1 -List"
Write-Host "  3) powershell -File $PSScriptRoot\run-after-restart.ps1 -SessionId <id>"
