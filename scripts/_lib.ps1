# Shared helpers for setup/start/stop. Dot-source from each script:
#   . (Join-Path $PSScriptRoot '_lib.ps1')

# Resolve repo root one level up from scripts/.
$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$script:WebDir   = Join-Path $RepoRoot 'src/web'
$script:ApiDir   = Join-Path $RepoRoot 'src/api/Slypn.Api'

# Ports we standardise on (Vite default, Functions Core Tools default).
$script:WebPort = 5173
$script:ApiPort = 7071

# Emulators (Docker).
$script:AzuriteContainer = 'slypn-azurite'
$script:AzuriteImage     = 'mcr.microsoft.com/azure-storage/azurite:latest'
$script:AzuriteBlobPort  = 10000
$script:AzuriteQueuePort = 10001
$script:AzuriteTablePort = 10002

function Show-Step($msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}
function Show-Ok($msg) {
    Write-Host "    $msg" -ForegroundColor Green
}
function Show-Warn($msg) {
    Write-Host "    $msg" -ForegroundColor Yellow
}
function Show-Err($msg) {
    Write-Host "    $msg" -ForegroundColor Red
}

function Test-Port($port) {
    return [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function Stop-Port($port) {
    $procIds = (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue).OwningProcess
    foreach ($procId in ($procIds | Sort-Object -Unique)) {
        if ($procId) {
            $name = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
            Write-Output "    killing PID $procId ($name) on port $port"
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
    }
}

# -- Docker helpers ---------------------------------------------------------
function Test-DockerRunning {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }
    docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Test-ContainerRunning($name) {
    $found = docker ps --filter "name=^/$name$" --format '{{.Names}}' 2>$null
    return [bool]($found -and $found.Trim() -eq $name)
}

function Test-ContainerExists($name) {
    $found = docker ps -a --filter "name=^/$name$" --format '{{.Names}}' 2>$null
    return [bool]($found -and $found.Trim() -eq $name)
}

function Wait-Port($port, $timeoutSec) {
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $timeoutSec) {
        if (Test-Port $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}
