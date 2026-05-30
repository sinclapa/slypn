#requires -Version 7
<#
.SYNOPSIS
  Verifies prereqs and prepares the SLYPN dev environment.

.DESCRIPTION
  Checks for Node 20+, the .NET 8 SDK, and Azure Functions Core Tools v4.
  Installs Functions Core Tools globally via npm if missing. Restores web
  (npm) and API (dotnet) dependencies, and copies local.settings.sample.json
  to local.settings.json if not already present.

.EXAMPLE
  .\scripts\setup.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

# --- Node 20+ ---------------------------------------------------------------
Write-Step 'Checking Node.js'
$nodeCmd = Get-Command node -ErrorAction SilentlyContinue
if (-not $nodeCmd) {
    Write-Err 'Node is not on PATH. Install Node 20+ from https://nodejs.org/.'
    exit 1
}
$nodeVersion = & node --version
$major = [int]($nodeVersion -replace '^v(\d+)\..*$', '$1')
if ($major -lt 20) {
    Write-Err "Node $nodeVersion is too old. Install Node 20+."
    exit 1
}
Write-Ok "Node $nodeVersion"

# --- .NET 8 SDK -------------------------------------------------------------
Write-Step 'Checking .NET SDK'
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Write-Err 'dotnet is not on PATH. Install the .NET 8 SDK from https://dotnet.microsoft.com/.'
    exit 1
}
$sdks = & dotnet --list-sdks
$hasNet8 = $sdks | Where-Object { $_ -match '^8\.' -or $_ -match '^9\.' }
if (-not $hasNet8) {
    Write-Err 'Need a .NET 8 SDK (or 9, which can target net8.0). Install from https://dotnet.microsoft.com/.'
    exit 1
}
Write-Ok "Found a compatible SDK ($(& dotnet --version))"

# --- Azure Functions Core Tools v4 -----------------------------------------
Write-Step 'Checking Azure Functions Core Tools'
$funcCmd  = Get-Command func -ErrorAction SilentlyContinue
$funcHome = Join-Path $env:LOCALAPPDATA 'AzureFunctionsCoreTools'

# If our standard install dir already has func.exe, just ensure it's on PATH.
if (-not $funcCmd -and (Test-Path (Join-Path $funcHome 'func.exe'))) {
    $env:PATH = "$funcHome;$env:PATH"
    $funcCmd = Get-Command func -ErrorAction SilentlyContinue
}

if (-not $funcCmd) {
    Write-Warn 'func not found. Downloading the standalone CLI from the Azure releases...'
    # Pin to a known-good min build (smaller, ~65 MB; uses system .NET). Override
    # via $env:SLYPN_FUNC_VERSION if you want a specific release.
    $funcVersionPin = if ($env:SLYPN_FUNC_VERSION) { $env:SLYPN_FUNC_VERSION } else { '4.12.0' }
    $zipUrl = "https://github.com/Azure/azure-functions-core-tools/releases/download/$funcVersionPin/Azure.Functions.Cli.min.win-x64.$funcVersionPin.zip"
    $zipPath = Join-Path $env:TEMP "func-cli-$funcVersionPin.zip"
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath
        if (Test-Path $funcHome) { Remove-Item $funcHome -Recurse -Force }
        Expand-Archive -Path $zipPath -DestinationPath $funcHome -Force
        Remove-Item $zipPath -ErrorAction SilentlyContinue
        $env:PATH = "$funcHome;$env:PATH"
        # Persist for future shells.
        $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
        if ($userPath -notmatch [regex]::Escape($funcHome)) {
            $userPathNew = if ($userPath) { "$funcHome;$userPath" } else { $funcHome }
            [Environment]::SetEnvironmentVariable('PATH', $userPathNew, 'User')
            Write-Warn "Added $funcHome to User PATH (effective in new shells)."
        }
        $funcCmd = Get-Command func -ErrorAction SilentlyContinue
    } catch {
        Write-Err "Failed to download/extract Functions Core Tools: $_"
        Write-Err 'Install manually from https://learn.microsoft.com/azure/azure-functions/functions-run-local.'
        exit 1
    }
}

if ($funcCmd) {
    $funcVersion = (& func --version) 2>&1 | Select-Object -First 1
    Write-Ok "func $funcVersion"
} else {
    Write-Err 'func installation appeared to succeed but the command is still missing on PATH.'
    exit 1
}

# --- web deps ---------------------------------------------------------------
Write-Step "npm ci in $WebDir"
Push-Location $WebDir
try {
    npm ci --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    Write-Ok 'web dependencies installed'
}
finally { Pop-Location }

# --- API deps ---------------------------------------------------------------
Write-Step "dotnet restore in $ApiDir"
Push-Location $ApiDir
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
    Write-Ok 'API dependencies restored'
}
finally { Pop-Location }

# --- local.settings.json ----------------------------------------------------
Write-Step 'Local Functions settings'
$localSettings  = Join-Path $ApiDir 'local.settings.json'
$sampleSettings = Join-Path $ApiDir 'local.settings.sample.json'
if (Test-Path $localSettings) {
    Write-Ok 'local.settings.json already exists'
} else {
    Copy-Item $sampleSettings $localSettings
    Write-Ok 'Created local.settings.json from local.settings.sample.json'
}

Write-Host ''
Write-Host 'Setup complete.' -ForegroundColor Green
Write-Host "Next: .\scripts\start.ps1" -ForegroundColor Green
