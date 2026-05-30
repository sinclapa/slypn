#requires -Version 7
<#
.SYNOPSIS
  Boots the Vue dev server and the .NET Functions host side-by-side.

.DESCRIPTION
  Starts:
    - vite (web)        on http://localhost:5173/
    - func (API)        on http://localhost:7071/
  Both are launched as background jobs whose logs stream to
  scripts/.runtime/<web|api>.log. PIDs are written to scripts/.runtime/pids.json
  so stop.ps1 can shut them down cleanly. The script waits until both ports
  respond, then prints URLs and returns control. Ctrl+C does NOT stop the
  servers — use stop.ps1.

.EXAMPLE
  .\scripts\start.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

$runtimeDir = Join-Path $PSScriptRoot '.runtime'
if (-not (Test-Path $runtimeDir)) { New-Item -ItemType Directory -Path $runtimeDir | Out-Null }
$webLog  = Join-Path $runtimeDir 'web.log'
$apiLog  = Join-Path $runtimeDir 'api.log'
$pidFile = Join-Path $runtimeDir 'pids.json'

# Refuse to start if anything is already on the ports.
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

# Resolve a real Win32 executable (Start-Process can't launch .ps1 shims).
function Resolve-WinExe($name) {
    # Prefer .cmd / .exe forms over PowerShell .ps1 shims that npm/Functions install.
    foreach ($suffix in @('.exe', '.cmd', '.bat', '')) {
        $cmd = Get-Command "$name$suffix" -ErrorAction SilentlyContinue |
               Where-Object { $_.Source -match '\.(exe|cmd|bat)$' } |
               Select-Object -First 1
        if ($cmd) { return $cmd.Source }
    }
    return $null
}

# --- web --------------------------------------------------------------------
Write-Step 'Starting Vite (web)'
$npmExe = Resolve-WinExe 'npm'
if (-not $npmExe) { Write-Err 'npm.cmd not on PATH'; exit 1 }
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
    # Fall back to our standard install location used by setup.ps1.
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

# Persist PIDs so stop.ps1 has something to kill.
@{ web = $webProc.Id; api = $apiProc.Id } | ConvertTo-Json | Out-File -FilePath $pidFile -Encoding UTF8

# --- wait until both ports respond -----------------------------------------
function Wait-Port($port, $timeoutSec) {
    $start = Get-Date
    while (((Get-Date) - $start).TotalSeconds -lt $timeoutSec) {
        if (Test-Port $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

Write-Step 'Waiting for ports to come up'
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
Write-Host 'Both servers are up.' -ForegroundColor Green
Write-Host "  Web:  http://localhost:$WebPort/" -ForegroundColor Green
Write-Host "  API:  http://localhost:$ApiPort/api/articles" -ForegroundColor Green
Write-Host ''
Write-Host "Logs:  $runtimeDir" -ForegroundColor DarkGray
Write-Host "Stop:  .\scripts\stop.ps1" -ForegroundColor DarkGray
