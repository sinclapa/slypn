#requires -Version 7
<#
.SYNOPSIS
  Boots Azurite + Cosmos emulator + Vue dev server + .NET Functions host.

.DESCRIPTION
  Starts:
    - slypn-azurite (Docker)    blob/queue/table emulator on :10000-10002
    - slypn-cosmos  (Docker)    Cosmos DB emulator (vnext-preview) on :8081
    - vite (npm)                Vue dev server on http://localhost:5173/
    - func (Functions Core)     .NET API host on http://localhost:7071/

  Emulator containers persist between start/stop cycles so seeded data
  survives. Use scripts/clean.ps1 to wipe them. Vite + func PIDs are
  written to scripts/.runtime/pids.json for stop.ps1.

  Ctrl+C does NOT stop anything — use scripts/stop.ps1.

.EXAMPLE
  .\scripts\start.ps1
#>

[CmdletBinding()]
param(
    [switch] $NoEmulators   # skip starting Docker containers (e.g. when running against a real Cosmos / Storage account)
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

$runtimeDir = Join-Path $PSScriptRoot '.runtime'
if (-not (Test-Path $runtimeDir)) { New-Item -ItemType Directory -Path $runtimeDir | Out-Null }
$webLog  = Join-Path $runtimeDir 'web.log'
$apiLog  = Join-Path $runtimeDir 'api.log'
$pidFile = Join-Path $runtimeDir 'pids.json'

# Refuse to start if the app ports are already taken (emulator port collisions
# are handled below — those map onto existing containers).
foreach ($p in @($WebPort, $ApiPort)) {
    if (Test-Port $p) {
        Write-Err "Port $p is already in use. Run .\scripts\stop.ps1 first."
        exit 1
    }
}

# Ensure local.settings.json exists (don't fail noisily — setup.ps1 covers this).
$localSettings = Join-Path $ApiDir 'local.settings.json'
if (-not (Test-Path $localSettings)) {
    $sample = Join-Path $ApiDir 'local.settings.sample.json'
    if (Test-Path $sample) {
        Copy-Item $sample $localSettings
        Write-Warn 'Created local.settings.json from sample (run setup.ps1 if anything else looks off).'
    }
}

# --- emulators --------------------------------------------------------------
if (-not $NoEmulators) {
    if (-not (Test-DockerRunning)) {
        Write-Err 'Docker daemon is not running. Start Docker Desktop (or pass -NoEmulators).'
        exit 1
    }

    Write-Step 'Ensuring Azurite container'
    if (Test-ContainerRunning $AzuriteContainer) {
        Write-Ok "$AzuriteContainer already running"
    } elseif (Test-ContainerExists $AzuriteContainer) {
        docker start $AzuriteContainer | Out-Null
        Write-Ok "$AzuriteContainer started"
    } else {
        docker run -d --name $AzuriteContainer `
            -p "${AzuriteBlobPort}:10000" `
            -p "${AzuriteQueuePort}:10001" `
            -p "${AzuriteTablePort}:10002" `
            $AzuriteImage | Out-Null
        Write-Ok "$AzuriteContainer created"
    }
    if (-not (Wait-Port $AzuriteBlobPort 30)) {
        Write-Err "Azurite blob port $AzuriteBlobPort did not come up in 30s. See: docker logs $AzuriteContainer"
        exit 1
    }
    Write-Ok "Azurite ready on http://127.0.0.1:$AzuriteBlobPort/"

    Write-Step 'Ensuring Cosmos DB emulator container (vnext-preview)'
    if (Test-ContainerRunning $CosmosContainer) {
        Write-Ok "$CosmosContainer already running"
    } elseif (Test-ContainerExists $CosmosContainer) {
        docker start $CosmosContainer | Out-Null
        Write-Ok "$CosmosContainer started"
    } else {
        docker run -d --name $CosmosContainer `
            -p "${CosmosPort}:8081" `
            $CosmosImage | Out-Null
        Write-Ok "$CosmosContainer created (first start downloads the image; can take a few minutes)"
    }
    Write-Step 'Waiting for Cosmos emulator (~30-90s)'
    if (-not (Wait-Port $CosmosPort 180)) {
        Write-Err "Cosmos emulator port $CosmosPort did not come up in 180s. See: docker logs $CosmosContainer"
        exit 1
    }
    Write-Ok "Cosmos emulator ready on https://localhost:$CosmosPort/"
}

# --- web --------------------------------------------------------------------
function Resolve-WinExe($name) {
    foreach ($suffix in @('.exe', '.cmd', '.bat', '')) {
        $cmd = Get-Command "$name$suffix" -ErrorAction SilentlyContinue |
               Where-Object { $_.Source -match '\.(exe|cmd|bat)$' } |
               Select-Object -First 1
        if ($cmd) { return $cmd.Source }
    }
    return $null
}

Write-Step 'Starting Vite (web)'
$npmExe = Resolve-WinExe 'npm'
if (-not $npmExe) { Write-Err 'npm.cmd not on PATH'; exit 1 }

$viteBinary = Join-Path $WebDir 'node_modules/.bin/vite.cmd'
if (-not (Test-Path $viteBinary)) {
    Write-Step 'Installing web dependencies (npm ci)'
    Push-Location $WebDir
    & $npmExe ci --no-audit --no-fund
    $npmExit = $LASTEXITCODE
    Pop-Location
    if ($npmExit -ne 0) {
        Write-Err 'npm ci failed. See the output above for details.'
        exit 1
    }
}

$webProc = Start-Process -FilePath $npmExe -ArgumentList 'run', 'dev' `
    -WorkingDirectory $WebDir `
    -RedirectStandardOutput $webLog `
    -RedirectStandardError  "$webLog.err" `
    -WindowStyle Hidden -PassThru
Write-Ok "vite PID $($webProc.Id), logging to $webLog"

# --- API --------------------------------------------------------------------
Write-Step 'Starting Functions host (API)'
$funcExe = Resolve-WinExe 'func'
if (-not $funcExe) {
    $funcHome = Join-Path $env:LOCALAPPDATA 'AzureFunctionsCoreTools'
    $candidate = Join-Path $funcHome 'func.exe'
    if (Test-Path $candidate) { $funcExe = $candidate }
}
if (-not $funcExe) {
    Write-Err 'func not on PATH. Run setup.ps1 first.'
    Stop-Process -Id $webProc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
$apiProc = Start-Process -FilePath $funcExe -ArgumentList 'start' `
    -WorkingDirectory $ApiDir `
    -RedirectStandardOutput $apiLog `
    -RedirectStandardError  "$apiLog.err" `
    -WindowStyle Hidden -PassThru
Write-Ok "func PID $($apiProc.Id), logging to $apiLog"

@{ web = $webProc.Id; api = $apiProc.Id } | ConvertTo-Json | Out-File -FilePath $pidFile -Encoding UTF8

# --- wait for app ports -----------------------------------------------------
Write-Step 'Waiting for vite + func to come up'
if (-not (Wait-Port $WebPort 30)) {
    Write-Err "vite did not start on $WebPort within 30s. See $webLog."
    exit 1
}
Write-Ok "vite ready on http://localhost:$WebPort/"

if (-not (Wait-Port $ApiPort 90)) {
    Write-Err "func did not start on $ApiPort within 90s. See $apiLog."
    exit 1
}
Write-Ok "func ready on http://localhost:$ApiPort/"

Write-Host ''
Write-Host 'Dev stack is up.' -ForegroundColor Green
Write-Host "  Web:    http://localhost:$WebPort/"           -ForegroundColor Green
Write-Host "  API:    http://localhost:$ApiPort/api/articles" -ForegroundColor Green
if (-not $NoEmulators) {
    Write-Host "  Cosmos: https://localhost:$CosmosPort/ (self-signed cert)" -ForegroundColor Green
    Write-Host "  Blob:   http://127.0.0.1:$AzuriteBlobPort/devstoreaccount1" -ForegroundColor Green
}
Write-Host ''
Write-Host "Logs:  $runtimeDir"           -ForegroundColor DarkGray
Write-Host "Seed:  .\scripts\seed.ps1"    -ForegroundColor DarkGray
Write-Host "Stop:  .\scripts\stop.ps1"    -ForegroundColor DarkGray
