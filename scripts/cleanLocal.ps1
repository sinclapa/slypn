#requires -Version 7
<#
.SYNOPSIS
  Removes the SLYPN emulator Docker container (data is lost).

.DESCRIPTION
  Use this when you want a fresh Azurite. The next `startLocal.ps1` will
  re-create it. Vite + func are not touched — run `stopLocal.ps1` first
  if they're still up.

.EXAMPLE
  .\scripts\cleanLocal.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

if (-not (Test-DockerRunning)) {
    Write-Err 'Docker daemon is not running; nothing to clean.'
    exit 1
}

foreach ($name in @($AzuriteContainer)) {
    if (Test-ContainerExists $name) {
        if (Test-ContainerRunning $name) {
            docker stop $name | Out-Null
        }
        docker rm $name | Out-Null
        Write-Ok "removed $name"
    } else {
        Write-Warn "$name does not exist"
    }
}
