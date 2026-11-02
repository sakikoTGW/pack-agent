param(
    [string]$Home = "E:\DSH-Home",
    [string]$SessionId,
    [switch]$List,
    [int]$WaitSeconds = 120
)

$ErrorActionPreference = "Stop"
function Fail($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

$adPath = Join-Path $Home "pad-gateway.json"
$sessionsDir = Join-Path $Home "storages\session_projcache\sessions"

if ($List) {
    if (-not (Test-Path $sessionsDir)) { Fail "no session cache at $sessionsDir" }
    $files = Get-ChildItem $sessionsDir -File -Filter "*.json" | Sort-Object LastWriteTime -Descending
    Write-Host "可跳会话（新→旧）："
    foreach ($f in $files) {
        $id = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
        Write-Host ("  {0}   {1}" -f $id, $f.LastWriteTime.ToString("MM-dd HH:mm"))
    }
    exit 0
}

if (-not $SessionId) { Fail "pass -SessionId <id> (use -List to see candidates)" }

# 1) wait for the carrier ad written by pad-gateway-jump on host boot
$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ((Get-Date) -lt $deadline -and -not (Test-Path $adPath)) {
    Start-Sleep -Seconds 2
}
if (-not (Test-Path $adPath)) {
    Fail "pad-gateway.json not found after ${WaitSeconds}s. gateway-jump host half did not load; see README troubleshooting."
}

$ad = Get-Content $adPath -Raw | ConvertFrom-Json
$origin = ([uri]$ad.url).GetLeftPart([System.UriPartial]::Authority)
Write-Host "carrier: $origin  (ad pid $($ad.pid), capabilities.jump=$($ad.capabilities.jump))"

# 2) ping
$ping = Invoke-WebRequest -Uri "$origin/pad/ping" -Headers @{ Authorization = "Bearer $($ad.token)" } -UseBasicParsing -TimeoutSec 10
Write-Host "ping: $($ping.StatusCode) $($ping.Content)"

# 3) jump
$body = @{ sessionId = $SessionId } | ConvertTo-Json
$jump = Invoke-WebRequest -Uri "$origin/pad/jump" -Method Post -Body $body -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $($ad.token)" } -UseBasicParsing -TimeoutSec 10
Write-Host "jump: $($jump.StatusCode) $($jump.Content)"
Write-Host ""
Write-Host "现在看官方 DeepSeek Harness 窗口：应该已经切到 $SessionId。" -ForegroundColor Cyan
Write-Host "如果窗口只是被聚焦/剪贴板兜底，说明渲染器里的消费端插件没加载。"
