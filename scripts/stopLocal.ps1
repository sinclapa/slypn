#requires -Version 7
<#
.SYNOPSIS
  Stops the local dev stack: vite + func + Azurite.

.DESCRIPTION
  - Kills the vite + func process trees recorded in scripts/.runtime/pids.json
    and sweeps ports 5173 + 7071 as a safety net.
  - Stops the slypn-azurite Docker container (it remains so data persists
    across start/stop cycles — use scripts/cleanLocal.ps1 to remove it).
  - Pass -KeepEmulators to leave the Docker containers running (useful when
    you want to seed or run the API on its own).

.EXAMPLE
  .\scripts\stopLocal.ps1
  .\scripts\stopLocal.ps1 -KeepEmulators
#>

[CmdletBinding()]
param(
    [switch] $KeepEmulators
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

$runtimeDir = Join-Path $PSScriptRoot '.runtime'
$pidFile    = Join-Path $runtimeDir 'pids.json'

if (Test-Path $pidFile) {
    Write-Step 'Stopping vite + func by recorded PIDs'
    $procIds = Get-Content $pidFile -Raw | ConvertFrom-Json
    foreach ($key in @('web', 'api')) {
        $procId = $procIds.$key
        if ($procId) {
            $name = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
            if ($name) {
                Write-Host "    killing PID $procId ($name) [$key]" -ForegroundColor DarkGray
                Get-CimInstance Win32_Process |
                    Where-Object { $_.ParentProcessId -eq $procId } |
                    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

Write-Step "Sweeping app ports $WebPort, $ApiPort"
Stop-Port $WebPort
Stop-Port $ApiPort

if (-not $KeepEmulators) {
    if (Test-DockerRunning) {
        Write-Step 'Stopping emulator containers'
        foreach ($name in @($AzuriteContainer)) {
            if (Test-ContainerRunning $name) {
                Write-Host "    stopping $name" -ForegroundColor DarkGray
                docker stop $name | Out-Null
            }
        }
    } else {
        Write-Warn 'Docker not reachable; skipping emulator stop.'
    }
}

Start-Sleep -Milliseconds 500
foreach ($p in @($WebPort, $ApiPort)) {
    if (Test-Port $p) { Write-Warn "Port $p still in use." } else { Write-Ok "Port $p free" }
}
