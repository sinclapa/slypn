#requires -Version 7
<#
.SYNOPSIS
  Verifies prereqs and prepares the SLYPN dev environment, and toggles local
  Entra sign-in on/off.

.DESCRIPTION
  Checks for Node 20+, the .NET 8 SDK, and Azure Functions Core Tools v4.
  Installs Functions Core Tools globally via npm if missing. Restores web
  (npm) and API (dotnet) dependencies, and copies local.settings.sample.json
  to local.settings.json if not already present.

  Local auth mode: infra/setup.ps1 configures the *capability* to sign in with
  Entra locally (it writes the MSAL credentials); this script owns the on/off
  toggle. Pass -EntraLogin to flip it:
    - off : dev persona switcher (VITE_DEV_SKIP_AUTH=true / AzureAd__SkipAuth=true)
    - on  : real CIAM sign-in     (both flags false; requires infra/setup.ps1 to
            have written the MSAL credentials into .env.local)
  When -EntraLogin is supplied the script ONLY toggles auth and exits (it skips
  the prereq/restore steps). With no arguments it runs the full prereq setup and
  reports the current auth mode without changing it.

.EXAMPLE
  .\scripts\setupLocal.ps1                 # full prereq setup; report auth mode

.EXAMPLE
  .\scripts\setupLocal.ps1 -EntraLogin off # switch to dev personas (no sign-in)

.EXAMPLE
  .\scripts\setupLocal.ps1 -EntraLogin on  # switch to real Entra sign-in
#>

[CmdletBinding()]
param(
    [ValidateSet('on', 'off')]
    [string] $EntraLogin
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

$envLocalPath     = Join-Path $WebDir '.env.local'
$localSettingsPath = Join-Path $ApiDir 'local.settings.json'
$sampleSettingsPath = Join-Path $ApiDir 'local.settings.sample.json'

# Reads the current local auth mode ('on' = Entra sign-in, 'off' = dev personas,
# '?' = unknown) from .env.local's VITE_DEV_SKIP_AUTH flag.
function Get-LocalAuthMode {
    if (-not (Test-Path $envLocalPath)) { return '?' }
    $line = Get-Content $envLocalPath |
        Where-Object { $_ -match '^\s*VITE_DEV_SKIP_AUTH\s*=' } |
        Select-Object -Last 1
    if ($line -match '=\s*true\s*$')  { return 'off' }
    if ($line -match '=\s*false\s*$') { return 'on' }
    return '?'
}

# Toggles local Entra sign-in by setting VITE_DEV_SKIP_AUTH (.env.local) and
# AzureAd__SkipAuth (local.settings.json) together so the web + API agree.
function Set-LocalAuthMode {
    param([Parameter(Mandatory)][ValidateSet('on', 'off')][string] $Mode)

    # Entra ON  -> skip-auth false (real CIAM sign-in)
    # Entra OFF -> skip-auth true  (dev persona switcher)
    $skip = if ($Mode -eq 'on') { 'false' } else { 'true' }

    # --- .env.local : VITE_DEV_SKIP_AUTH ---
    if (Test-Path $envLocalPath) {
        $lines = @(Get-Content $envLocalPath)
        if ($lines -match '^\s*VITE_DEV_SKIP_AUTH\s*=') {
            $lines = $lines -replace '^\s*VITE_DEV_SKIP_AUTH\s*=.*', "VITE_DEV_SKIP_AUTH=$skip"
        } else {
            $lines += "VITE_DEV_SKIP_AUTH=$skip"
        }
        $lines | Set-Content $envLocalPath -Encoding UTF8
    } else {
        @('# Created by scripts/setupLocal.ps1', "VITE_DEV_SKIP_AUTH=$skip") |
            Set-Content $envLocalPath -Encoding UTF8
        if ($Mode -eq 'on') {
            Write-Warn '.env.local had no MSAL credentials — run infra/setup.ps1 to enable real local sign-in.'
        }
    }
    Write-Ok "Set VITE_DEV_SKIP_AUTH=$skip in .env.local"

    # --- local.settings.json : AzureAd__SkipAuth ---
    if (-not (Test-Path $localSettingsPath) -and (Test-Path $sampleSettingsPath)) {
        Copy-Item $sampleSettingsPath $localSettingsPath
        Write-Ok 'Created local.settings.json from sample'
    }
    if (Test-Path $localSettingsPath) {
        $ls = Get-Content $localSettingsPath -Raw | ConvertFrom-Json -AsHashtable
        $ls['Values']['AzureAd__SkipAuth'] = $skip
        $ls | ConvertTo-Json -Depth 5 | Set-Content $localSettingsPath -Encoding UTF8
        Write-Ok "Set AzureAd__SkipAuth=$skip in local.settings.json"
    } else {
        Write-Warn 'local.settings.json not found — skipped AzureAd__SkipAuth.'
    }

    if ($Mode -eq 'on') {
        Write-Ok 'Entra login ENABLED locally (real CIAM sign-in).'
    } else {
        Write-Ok 'Entra login DISABLED locally (dev persona switcher active).'
    }
    Write-Warn 'Restart the stack for changes to take effect: .\scripts\stopLocal.ps1 then .\scripts\startLocal.ps1'
}

# Toggle-only mode: flip auth and exit without running the full prereq setup.
if ($EntraLogin) {
    Write-Step "Toggling local Entra login: $EntraLogin"
    Set-LocalAuthMode -Mode $EntraLogin
    return
}

# Refuse to run while the dev stack is up — `npm ci` wipes node_modules and would
# fail (EPERM) trying to delete files the running Vite/func processes have open.
foreach ($p in @($WebPort, $ApiPort)) {
    if (Test-Port $p) {
        Write-Err "Port $p is in use — the dev stack looks like it's running."
        Write-Err 'Stop it first:  .\scripts\stopLocal.ps1   (then re-run setupLocal.ps1)'
        exit 1
    }
}

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

# --- Docker (for local emulators) ------------------------------------------
Write-Step 'Checking Docker (for Azurite)'
if (Test-DockerRunning) {
    $dockerVersion = docker version --format '{{.Server.Version}}' 2>$null
    Write-Ok "Docker $dockerVersion"
} else {
    Write-Warn 'Docker daemon is not reachable. Start Docker Desktop before running scripts/startLocal.ps1.'
    Write-Warn 'Without Docker, startLocal.ps1 must be invoked with -NoEmulators (API will skip Table/Blob storage).'
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

# --- local auth mode (report only; toggle with -EntraLogin) -----------------
Write-Step 'Local auth mode'
$mode = Get-LocalAuthMode
switch ($mode) {
    'on'  { Write-Ok  'Entra login is ENABLED (real CIAM sign-in).' }
    'off' { Write-Ok  'Entra login is DISABLED (dev persona switcher active).' }
    default {
        Write-Warn 'Entra login mode unknown — run infra/setup.ps1 (capability) then toggle below.'
    }
}
Write-Host "    Toggle: .\scripts\setupLocal.ps1 -EntraLogin on|off" -ForegroundColor DarkGray

Write-Host ''
Write-Host 'Setup complete.' -ForegroundColor Green
Write-Host "Next: .\scripts\startLocal.ps1" -ForegroundColor Green
