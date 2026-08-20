#requires -Version 7
<#
.SYNOPSIS
  Boots Azurite + Vue dev server + .NET Functions host.

.DESCRIPTION
  Starts:
    - slypn-azurite (Docker)    blob/queue/table emulator on :10000-10002
    - vite (npm)                Vue dev server on http://localhost:5173/
    - dotnet run (Functions)    .NET API host on http://localhost:7071/

  Emulator containers persist between start/stop cycles so seeded data
  survives. Use scripts/cleanLocal.ps1 to wipe them. Vite + func PIDs are
  written to scripts/.runtime/pids.json for stopLocal.ps1.

  Ctrl+C does NOT stop anything — use scripts/stopLocal.ps1.

.EXAMPLE
  .\scripts\startLocal.ps1
#>

[CmdletBinding()]
param(
    [switch] $NoEmulators   # skip starting Docker containers (e.g. when running against a real Storage account)
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
        Show-Err "Port $p is already in use. Run .\scripts\stopLocal.ps1 first."
        exit 1
    }
}

# Ensure local.settings.json exists (don't fail noisily — setupLocal.ps1 covers this).
$localSettings = Join-Path $ApiDir 'local.settings.json'
if (-not (Test-Path $localSettings)) {
    $sample = Join-Path $ApiDir 'local.settings.sample.json'
    if (Test-Path $sample) {
        Copy-Item $sample $localSettings
        Show-Warn 'Created local.settings.json from sample (run setupLocal.ps1 if anything else looks off).'
    }
}

# --- emulators --------------------------------------------------------------
if (-not $NoEmulators) {
    if (-not (Test-DockerRunning)) {
        Show-Err 'Docker daemon is not running. Start Docker Desktop (or pass -NoEmulators).'
        exit 1
    }

    Show-Step 'Ensuring Azurite container'
    if (Test-ContainerRunning $AzuriteContainer) {
        Show-Ok "$AzuriteContainer already running"
    } elseif (Test-ContainerExists $AzuriteContainer) {
        docker start $AzuriteContainer | Out-Null
        Show-Ok "$AzuriteContainer started"
    } else {
        docker run -d --name $AzuriteContainer `
            -p "${AzuriteBlobPort}:10000" `
            -p "${AzuriteQueuePort}:10001" `
            -p "${AzuriteTablePort}:10002" `
            $AzuriteImage | Out-Null
        Show-Ok "$AzuriteContainer created"
    }
    if (-not (Wait-Port $AzuriteBlobPort 30)) {
        Show-Err "Azurite blob port $AzuriteBlobPort did not come up in 30s. See: docker logs $AzuriteContainer"
        exit 1
    }
    if (-not (Wait-Port $AzuriteTablePort 30)) {
        Show-Err "Azurite table port $AzuriteTablePort did not come up in 30s. See: docker logs $AzuriteContainer"
        exit 1
    }
    Show-Ok "Azurite ready on http://127.0.0.1:$AzuriteBlobPort/ (blob) + :$AzuriteTablePort (table)"
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

Show-Step 'Starting Vite (web)'
$npmExe = Resolve-WinExe 'npm'
if (-not $npmExe) { Show-Err 'npm.cmd not on PATH'; exit 1 }

$viteBinary = Join-Path $WebDir 'node_modules/.bin/vite.cmd'
if (-not (Test-Path $viteBinary)) {
    Show-Step 'Installing web dependencies (npm ci)'
    Push-Location $WebDir
    & $npmExe ci --no-audit --no-fund
    $npmExit = $LASTEXITCODE
    Pop-Location
    if ($npmExit -ne 0) {
        Show-Err 'npm ci failed. See the output above for details.'
        exit 1
    }
}

$webProc = Start-Process -FilePath $npmExe -ArgumentList 'run', 'dev' `
    -WorkingDirectory $WebDir `
    -RedirectStandardOutput $webLog `
    -RedirectStandardError  "$webLog.err" `
    -WindowStyle Hidden -PassThru
Show-Ok "vite PID $($webProc.Id), logging to $webLog"

# --- API --------------------------------------------------------------------
Show-Step 'Starting Functions host (API)'
# Started via `dotnet run`, not `func start`: Core Tools warns that running it
# directly against a .NET isolated project "may not correctly load function
# extensions". The Worker SDK maps `dotnet run` onto `func start` with
# RunWorkingDirectory set to the build output, which is the supported path.
# `func` must still be on PATH — the SDK shells out to it.
$funcExe = Resolve-WinExe 'func'
if (-not $funcExe) {
    $funcHome = Join-Path $env:LOCALAPPDATA 'AzureFunctionsCoreTools'
    $candidate = Join-Path $funcHome 'func.exe'
    if (Test-Path $candidate) { $funcExe = $candidate }
}
if (-not $funcExe) {
    Show-Err 'func not on PATH. The Functions SDK shells out to Core Tools, so it is still required. Run setupLocal.ps1 first.'
    Stop-Process -Id $webProc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}
# Debug (the dotnet run default) rather than Release: this is the interactive
# dev stack, so fast incremental builds and debugger attach matter more than
# matching the deployed configuration. The e2e suite uses Release for that.
$apiProc = Start-Process -FilePath 'dotnet' `
    -ArgumentList 'run', '--no-launch-profile', '--', '--port', "$ApiPort" `
    -WorkingDirectory $ApiDir `
    -RedirectStandardOutput $apiLog `
    -RedirectStandardError  "$apiLog.err" `
    -WindowStyle Hidden -PassThru
Show-Ok "dotnet run PID $($apiProc.Id), logging to $apiLog"

@{ web = $webProc.Id; api = $apiProc.Id } | ConvertTo-Json | Out-File -FilePath $pidFile -Encoding UTF8

# --- wait for app ports -----------------------------------------------------
Show-Step 'Waiting for vite + func to come up'
if (-not (Wait-Port $WebPort 30)) {
    Show-Err "vite did not start on $WebPort within 30s. See $webLog."
    exit 1
}
Show-Ok "vite ready on http://localhost:$WebPort/"

if (-not (Wait-Port $ApiPort 90)) {
    Show-Err "func did not start on $ApiPort within 90s. See $apiLog."
    exit 1
}
Show-Ok "func ready on http://localhost:$ApiPort/"

Write-Host ''
Write-Host 'Dev stack is up.' -ForegroundColor Green
Write-Host "  Web:    http://localhost:$WebPort/"           -ForegroundColor Green
Write-Host "  API:    http://localhost:$ApiPort/api/articles" -ForegroundColor Green
if (-not $NoEmulators) {
    Write-Host "  Blob:   http://127.0.0.1:$AzuriteBlobPort/devstoreaccount1"  -ForegroundColor Green
    Write-Host "  Table:  http://127.0.0.1:$AzuriteTablePort/devstoreaccount1" -ForegroundColor Green
}
Write-Host ''
Write-Host "Logs:  $runtimeDir"           -ForegroundColor DarkGray
Write-Host "Seed:  .\scripts\seedLocal.ps1"    -ForegroundColor DarkGray
Write-Host "Stop:  .\scripts\stopLocal.ps1"    -ForegroundColor DarkGray
