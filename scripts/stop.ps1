#requires -Version 7
<#
.SYNOPSIS
  Stops the local Vue + Functions dev stack started by start.ps1.

.DESCRIPTION
  First tries the PIDs persisted by start.ps1 in scripts/.runtime/pids.json.
  Falls back to killing whatever is listening on ports 5173 and 7071. Safe
  to run repeatedly.

.EXAMPLE
  .\scripts\stop.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

$runtimeDir = Join-Path $PSScriptRoot '.runtime'
$pidFile    = Join-Path $runtimeDir 'pids.json'

if (Test-Path $pidFile) {
    Write-Step 'Stopping by recorded PIDs'
    $procIds = Get-Content $pidFile -Raw | ConvertFrom-Json
    foreach ($key in @('web', 'api')) {
        $procId = $procIds.$key
        if ($procId) {
            $name = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
            if ($name) {
                Write-Host "    killing PID $procId ($name) [$key]" -ForegroundColor DarkGray
                # Kill the whole process tree (Start-Process spawns npm.cmd which spawns node).
                Get-CimInstance Win32_Process |
                    Where-Object { $_.ParentProcessId -eq $procId } |
                    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

# Sweep ports as a safety net (catches orphaned children).
Write-Step "Sweeping ports $WebPort and $ApiPort"
Stop-Port $WebPort
Stop-Port $ApiPort

Start-Sleep -Milliseconds 500
foreach ($p in @($WebPort, $ApiPort)) {
    if (Test-Port $p) {
        Write-Warn "Port $p still in use."
    } else {
        Write-Ok "Port $p free"
    }
}
