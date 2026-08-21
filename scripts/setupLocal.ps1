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

  Web dependencies are stamped with a hash of package.json + package-lock.json,
  so a re-run skips `npm ci` entirely when neither has changed. Pass -Reinstall
  to force the clean reinstall.

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

.EXAMPLE
  .\scripts\setupLocal.ps1 -Reinstall     # force a clean `npm ci` reinstall
#>

[CmdletBinding()]
param(
    [ValidateSet('on', 'off')]
    [string] $EntraLogin,

    # Force `npm ci` even when the dependency stamp says node_modules is current.
    [switch] $Reinstall
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
            Show-Warn '.env.local had no MSAL credentials — run infra/setup.ps1 to enable real local sign-in.'
        }
    }
    Show-Ok "Set VITE_DEV_SKIP_AUTH=$skip in .env.local"

    # --- local.settings.json : AzureAd__SkipAuth ---
    if (-not (Test-Path $localSettingsPath) -and (Test-Path $sampleSettingsPath)) {
        Copy-Item $sampleSettingsPath $localSettingsPath
        Show-Ok 'Created local.settings.json from sample'
    }
    if (Test-Path $localSettingsPath) {
        $ls = Get-Content $localSettingsPath -Raw | ConvertFrom-Json -AsHashtable
        $ls['Values']['AzureAd__SkipAuth'] = $skip
        $ls | ConvertTo-Json -Depth 5 | Set-Content $localSettingsPath -Encoding UTF8
        Show-Ok "Set AzureAd__SkipAuth=$skip in local.settings.json"
    } else {
        Show-Warn 'local.settings.json not found — skipped AzureAd__SkipAuth.'
    }

    if ($Mode -eq 'on') {
        Show-Ok 'Entra login ENABLED locally (real CIAM sign-in).'
    } else {
        Show-Ok 'Entra login DISABLED locally (dev persona switcher active).'
    }
    Show-Warn 'Restart the stack for changes to take effect: .\scripts\stopLocal.ps1 then .\scripts\startLocal.ps1'
}

# Toggle-only mode: flip auth and exit without running the full prereq setup.
if ($EntraLogin) {
    Show-Step "Toggling local Entra login: $EntraLogin"
    Set-LocalAuthMode -Mode $EntraLogin
    return
}

# --- are the web dependencies already current? ------------------------------
# `npm ci` always deletes node_modules and reinstalls from the lockfile, which
# costs minutes for a tree this size. Stamp each successful install with a hash
# of package.json + package-lock.json and skip the reinstall while both are
# unchanged. The stamp lives inside node_modules, so wiping that folder (or an
# interrupted install) correctly forces a fresh one.
$webPkgPath   = Join-Path $WebDir 'package.json'
$webLockPath  = Join-Path $WebDir 'package-lock.json'
$webModules   = Join-Path $WebDir 'node_modules'
$webStampPath = Join-Path $webModules '.slypn-deps-stamp'

function Get-WebDepsHash {
    $parts = foreach ($file in @($webPkgPath, $webLockPath)) {
        if (Test-Path $file) { (Get-FileHash $file -Algorithm SHA256).Hash } else { 'missing' }
    }
    return ($parts -join '-')
}

$webDepsHash = Get-WebDepsHash
$needsNpmInstall =
    $Reinstall -or
    -not (Test-Path $webModules) -or
    -not (Test-Path $webStampPath) -or
    ((Get-Content $webStampPath -Raw).Trim() -ne $webDepsHash)

# Refuse to run while the dev stack is up ONLY when we are about to reinstall —
# `npm ci` wipes node_modules and would fail (EPERM) trying to delete files the
# running Vite/func processes have open. When the stamp says the tree is already
# current there is nothing to wipe, so the script is safe against a live stack.
if ($needsNpmInstall) {
    foreach ($p in @($WebPort, $ApiPort)) {
        if (Test-Port $p) {
            Show-Err "Port $p is in use — the dev stack looks like it's running."
            Show-Err 'Web dependencies changed, so this run needs a clean npm ci.'
            Show-Err 'Stop it first:  .\scripts\stopLocal.ps1   (then re-run setupLocal.ps1)'
            exit 1
        }
    }
}

# --- Node 20+ ---------------------------------------------------------------
Show-Step 'Checking Node.js'
$nodeCmd = Get-Command node -ErrorAction SilentlyContinue
if (-not $nodeCmd) {
    Show-Err 'Node is not on PATH. Install Node 20+ from https://nodejs.org/.'
    exit 1
}
$nodeVersion = & node --version
$major = [int]($nodeVersion -replace '^v(\d+)\..*$', '$1')
if ($major -lt 20) {
    Show-Err "Node $nodeVersion is too old. Install Node 20+."
    exit 1
}
Show-Ok "Node $nodeVersion"
# Some dependencies (undici, eslint-visitor-keys) declare engines newer than
# this; npm only warns (EBADENGINE) and installs anyway, but CI runs latest 22.x.
$nodeParts = ($nodeVersion.TrimStart('v') -split '\.')
if ($major -eq 22 -and [int]$nodeParts[1] -lt 19) {
    Show-Warn "Node $nodeVersion triggers npm EBADENGINE warnings — 22.19+ (or 24) matches CI and silences them."
}

# --- .NET 8 SDK -------------------------------------------------------------
Show-Step 'Checking .NET SDK'
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Show-Err 'dotnet is not on PATH. Install the .NET 8 SDK from https://dotnet.microsoft.com/.'
    exit 1
}
$sdks = & dotnet --list-sdks
$hasNet8 = $sdks | Where-Object { $_ -match '^8\.' -or $_ -match '^9\.' }
if (-not $hasNet8) {
    Show-Err 'Need a .NET 8 SDK (or 9, which can target net8.0). Install from https://dotnet.microsoft.com/.'
    exit 1
}
Show-Ok "Found a compatible SDK ($(& dotnet --version))"

# --- Azure Functions Core Tools v4 -----------------------------------------
Show-Step 'Checking Azure Functions Core Tools'
$funcCmd  = Get-Command func -ErrorAction SilentlyContinue
$funcHome = Join-Path $env:LOCALAPPDATA 'AzureFunctionsCoreTools'

# If our standard install dir already has func.exe, just ensure it's on PATH.
if (-not $funcCmd -and (Test-Path (Join-Path $funcHome 'func.exe'))) {
    $env:PATH = "$funcHome;$env:PATH"
    $funcCmd = Get-Command func -ErrorAction SilentlyContinue
}

if (-not $funcCmd) {
    Show-Warn 'func not found. Downloading the standalone CLI from the Azure releases...'
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
            Show-Warn "Added $funcHome to User PATH (effective in new shells)."
        }
        $funcCmd = Get-Command func -ErrorAction SilentlyContinue
    } catch {
        Show-Err "Failed to download/extract Functions Core Tools: $_"
        Show-Err 'Install manually from https://learn.microsoft.com/azure/azure-functions/functions-run-local.'
        exit 1
    }
}

if ($funcCmd) {
    $funcVersion = (& func --version) 2>&1 | Select-Object -First 1
    Show-Ok "func $funcVersion"
} else {
    Show-Err 'func installation appeared to succeed but the command is still missing on PATH.'
    exit 1
}

# --- Docker (for local emulators) ------------------------------------------
Show-Step 'Checking Docker (for Azurite)'
if (Test-DockerRunning) {
    $dockerVersion = docker version --format '{{.Server.Version}}' 2>$null
    Show-Ok "Docker $dockerVersion"
    # Flag the failure mode startLocal.ps1 now repairs, so it is visible here too.
    if ((Test-ContainerExists $AzuriteContainer) -and
        -not (Test-ContainerPublishesPort $AzuriteContainer $AzuriteBlobPort)) {
        Show-Warn "$AzuriteContainer exists but publishes no host port $AzuriteBlobPort."
        Show-Warn 'startLocal.ps1 will recreate it (keeping its data volume) on the next run.'
    }
} else {
    Show-Warn 'Docker daemon is not reachable. Start Docker Desktop before running scripts/startLocal.ps1.'
    Show-Warn 'Without Docker, startLocal.ps1 must be invoked with -NoEmulators (API will skip Table/Blob storage).'
}

# --- web deps ---------------------------------------------------------------
Show-Step "Web dependencies in $WebDir"
if (-not $needsNpmInstall) {
    Show-Ok 'Already current — package.json and package-lock.json are unchanged since the last install.'
    Write-Host '    Force a clean reinstall with: .\scripts\setupLocal.ps1 -Reinstall' -ForegroundColor DarkGray
} else {
    Push-Location $WebDir
    try {
        npm ci --no-fund --no-audit
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
        # Re-hash after the install so the stamp reflects what npm actually left
        # on disk, then write it last — a failed install leaves no stamp.
        Get-WebDepsHash | Set-Content $webStampPath -Encoding UTF8
        Show-Ok 'web dependencies installed'
    }
    finally { Pop-Location }
}

# --- API deps ---------------------------------------------------------------
Show-Step "dotnet restore in $ApiDir"
Push-Location $ApiDir
try {
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
    Show-Ok 'API dependencies restored'
}
finally { Pop-Location }

# --- local.settings.json ----------------------------------------------------
Show-Step 'Local Functions settings'
$localSettings  = Join-Path $ApiDir 'local.settings.json'
$sampleSettings = Join-Path $ApiDir 'local.settings.sample.json'
if (Test-Path $localSettings) {
    Show-Ok 'local.settings.json already exists'
} else {
    Copy-Item $sampleSettings $localSettings
    Show-Ok 'Created local.settings.json from local.settings.sample.json'
}

# --- local auth mode (report only; toggle with -EntraLogin) -----------------
Show-Step 'Local auth mode'
$mode = Get-LocalAuthMode
switch ($mode) {
    'on'  { Show-Ok  'Entra login is ENABLED (real CIAM sign-in).' }
    'off' { Show-Ok  'Entra login is DISABLED (dev persona switcher active).' }
    default {
        Show-Warn 'Entra login mode unknown — run infra/setup.ps1 (capability) then toggle below.'
    }
}
Write-Host "    Toggle: .\scripts\setupLocal.ps1 -EntraLogin on|off" -ForegroundColor DarkGray

# --- Grafana Faro (optional) -------------------------------------------------
Show-Step 'Grafana Faro observability'

function Get-EnvLocalVar([string]$name) {
    if (-not (Test-Path $envLocalPath)) { return '' }
    $line = Get-Content $envLocalPath |
        Where-Object { $_ -match "^\s*${name}\s*=" } |
        Select-Object -Last 1
    if ($line -match '=\s*(.+)$') { return $Matches[1].Trim() }
    return ''
}

# infra/setup.ps1 collects the Faro settings into infra/secrets.json but only
# mirrors VITE_FARO_URL/VITE_FARO_ENV into .env.local — the FARO_SOURCEMAP_*
# set goes to GitHub secrets instead. Fall back to secrets.json so re-running
# shows what is already configured rather than an empty prompt.
$secretsPath = Join-Path $RepoRoot 'infra/secrets.json'
$secrets = @{}
if (Test-Path $secretsPath) {
    try { $secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json -AsHashtable }
    catch { Show-Warn "Could not read $secretsPath — prompt defaults may be blank." }
}

function Get-FaroCurrent([string] $envName, [string] $secretName) {
    $value = Get-EnvLocalVar $envName
    if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    if ($secretName -and $secrets.Contains($secretName)) { return [string] $secrets[$secretName] }
    return ''
}

$currentUrl     = Get-FaroCurrent 'VITE_FARO_URL'      'faroUrl'
$currentAppName = Get-FaroCurrent 'VITE_FARO_APP_NAME' ''

$hintUrl     = if ($currentUrl)     { " [$currentUrl]" }     else { '' }
$hintAppName = if ($currentAppName) { " [$currentAppName]" } else { ' [slypn-web]' }

$inputUrl     = Read-Host "  ? Faro collector URL (Enter to skip/keep)$hintUrl"
$inputAppName = Read-Host "  ? Faro app name$hintAppName"

$faroUrl     = if (-not [string]::IsNullOrWhiteSpace($inputUrl))     { $inputUrl.Trim() }     else { $currentUrl }
$faroAppName = if (-not [string]::IsNullOrWhiteSpace($inputAppName)) { $inputAppName.Trim() } else { if ($currentAppName) { $currentAppName } else { 'slypn-web' } }

$currentSmEndpoint = Get-FaroCurrent 'FARO_SOURCEMAP_ENDPOINT' 'faroSourcemapEndpoint'
$currentSmAppId    = Get-FaroCurrent 'FARO_SOURCEMAP_APP_ID'   'faroSourcemapAppId'
$currentSmStackId  = Get-FaroCurrent 'FARO_SOURCEMAP_STACK_ID' 'faroSourcemapStackId'
$currentSmApiKey   = Get-FaroCurrent 'FARO_SOURCEMAP_API_KEY'  'faroSourcemapApiKey'

$hintSmEndpoint = if ($currentSmEndpoint) { " [$currentSmEndpoint]" } else { '' }
$hintSmAppId    = if ($currentSmAppId)    { " [$currentSmAppId]" }    else { '' }
$hintSmStackId  = if ($currentSmStackId)  { " [$currentSmStackId]" }  else { '' }
$hintSmApiKey   = if ($currentSmApiKey)   { " [****]" }               else { '' }

$inputSmEndpoint = Read-Host "  ? Faro source map endpoint (Enter to skip/keep)$hintSmEndpoint"
$inputSmAppId    = Read-Host "  ? Faro source map app ID   (Enter to skip/keep)$hintSmAppId"
$inputSmStackId  = Read-Host "  ? Faro source map stack ID (Enter to skip/keep)$hintSmStackId"
$inputSmApiKey   = Read-Host "  ? Faro source map API key  (Enter to skip/keep)$hintSmApiKey"

$faroSmEndpoint = if (-not [string]::IsNullOrWhiteSpace($inputSmEndpoint)) { $inputSmEndpoint.Trim() } else { $currentSmEndpoint }
$faroSmAppId    = if (-not [string]::IsNullOrWhiteSpace($inputSmAppId))    { $inputSmAppId.Trim() }    else { $currentSmAppId }
$faroSmStackId  = if (-not [string]::IsNullOrWhiteSpace($inputSmStackId))  { $inputSmStackId.Trim() }  else { $currentSmStackId }
$faroSmApiKey   = if (-not [string]::IsNullOrWhiteSpace($inputSmApiKey))   { $inputSmApiKey.Trim() }   else { $currentSmApiKey }

# Build the full set of Faro vars to patch into .env.local
$faroKvPairs = [System.Collections.Generic.List[pscustomobject]]::new()
if (-not [string]::IsNullOrWhiteSpace($faroUrl)) {
    $faroKvPairs.Add([pscustomobject]@{ Key = 'VITE_FARO_URL';      Value = $faroUrl })
    $faroKvPairs.Add([pscustomobject]@{ Key = 'VITE_FARO_APP_NAME'; Value = $faroAppName })
    $faroKvPairs.Add([pscustomobject]@{ Key = 'VITE_FARO_ENV';      Value = 'local' })
}
if (-not [string]::IsNullOrWhiteSpace($faroSmEndpoint)) { $faroKvPairs.Add([pscustomobject]@{ Key = 'FARO_SOURCEMAP_ENDPOINT'; Value = $faroSmEndpoint }) }
if (-not [string]::IsNullOrWhiteSpace($faroSmAppId))    { $faroKvPairs.Add([pscustomobject]@{ Key = 'FARO_SOURCEMAP_APP_ID';   Value = $faroSmAppId }) }
if (-not [string]::IsNullOrWhiteSpace($faroSmStackId))  { $faroKvPairs.Add([pscustomobject]@{ Key = 'FARO_SOURCEMAP_STACK_ID'; Value = $faroSmStackId }) }
if (-not [string]::IsNullOrWhiteSpace($faroSmApiKey))   { $faroKvPairs.Add([pscustomobject]@{ Key = 'FARO_SOURCEMAP_API_KEY';  Value = $faroSmApiKey }) }

if ($faroKvPairs.Count -gt 0) {
    $lines = if (Test-Path $envLocalPath) { @(Get-Content $envLocalPath) } else { @('# Created by scripts/setupLocal.ps1') }
    foreach ($kv in $faroKvPairs) {
        $pattern = "^\s*$([regex]::Escape($kv.Key))\s*="
        if ($lines -match $pattern) {
            # Rewrite the first occurrence and drop the rest, so a file that
            # earlier runs left with duplicate keys is repaired rather than
            # having every copy rewritten to the same value.
            $written = $false
            $lines = @(foreach ($line in $lines) {
                if ($line -notmatch $pattern) { $line; continue }
                if ($written) { continue }
                $written = $true
                "$($kv.Key)=$($kv.Value)"
            })
        } else {
            $lines += "$($kv.Key)=$($kv.Value)"
        }
    }
    $lines | Set-Content $envLocalPath -Encoding UTF8
    if (-not [string]::IsNullOrWhiteSpace($faroUrl)) {
        Show-Ok "VITE_FARO_URL set in .env.local"
        Show-Ok "VITE_FARO_APP_NAME=$faroAppName"
        Show-Ok "VITE_FARO_ENV=local (fixed for local dev)"
    }
    if (-not [string]::IsNullOrWhiteSpace($faroSmEndpoint)) { Show-Ok "FARO_SOURCEMAP_ENDPOINT set in .env.local" }
    if (-not [string]::IsNullOrWhiteSpace($faroSmAppId))    { Show-Ok "FARO_SOURCEMAP_APP_ID=$faroSmAppId" }
    if (-not [string]::IsNullOrWhiteSpace($faroSmStackId))  { Show-Ok "FARO_SOURCEMAP_STACK_ID=$faroSmStackId" }
    if (-not [string]::IsNullOrWhiteSpace($faroSmApiKey))   { Show-Ok 'FARO_SOURCEMAP_API_KEY set (masked)' }
} else {
    Show-Warn 'Faro URL not set — observability disabled locally. Re-run to configure.'
}

Write-Host ''
Write-Host 'Setup complete.' -ForegroundColor Green
Write-Host "Next: .\scripts\startLocal.ps1" -ForegroundColor Green
