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

# Kill a process and everything beneath it, depth-first.
# The API host is a tree (dotnet -> cmd -> func -> worker), so killing only the
# recorded PID, or only its direct children, leaves the listener alive and the
# port bound.
function Stop-ProcessTree($procId) {
    if (-not $procId) { return }
    $children = Get-CimInstance Win32_Process -Filter "ParentProcessId=$procId" -ErrorAction SilentlyContinue
    foreach ($child in $children) { Stop-ProcessTree $child.ProcessId }
    Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
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

# Docker fixes port publishing at create time, and a container can also come back
# from `docker start` with its bindings silently unestablished (seen after Docker
# Desktop upgrades): `docker inspect` then shows HostConfig.PortBindings set but
# NetworkSettings.Ports empty, and `docker ps` lists the port with no host
# mapping. Neither `docker start` nor `docker restart` can repair that — the
# container has to be recreated — so check what is really published, not what was
# requested.
function Test-ContainerPublishesPort($name, $hostPort) {
    $json = docker inspect $name --format '{{json .NetworkSettings.Ports}}' 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json) -or $json -eq 'null') { return $false }
    try { $ports = $json | ConvertFrom-Json } catch { return $false }
    foreach ($entry in $ports.PSObject.Properties) {
        foreach ($binding in @($entry.Value)) {
            if ($binding -and "$($binding.HostPort)" -eq "$hostPort") { return $true }
        }
    }
    return $false
}

# Name of the volume a container has mounted at $destination, so a container can
# be recreated without orphaning the data it was holding.
function Get-ContainerVolumeName($name, $destination) {
    $json = docker inspect $name --format '{{json .Mounts}}' 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json) -or $json -eq 'null') { return $null }
    try { $mounts = $json | ConvertFrom-Json } catch { return $null }
    return (@($mounts) |
        Where-Object { $_.Type -eq 'volume' -and $_.Destination -eq $destination } |
        Select-Object -First 1).Name
}

function Wait-Port($port, $timeoutSec) {
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $timeoutSec) {
        if (Test-Port $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}
