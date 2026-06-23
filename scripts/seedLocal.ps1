#requires -Version 7
<#
.SYNOPSIS
  Seeds Table Storage with the sample newsletter parsed from brief/.

.DESCRIPTION
  Reads Storage__ConnectionString from src/api/Slypn.Api/local.settings.json,
  then runs the Slypn.Seed console app against brief/SLYPN_Newsletter_MAY_2026.docx
  and upserts a newsletter entity into the newsletters table.

.EXAMPLE
  .\scripts\seedLocal.ps1
#>

[CmdletBinding()]
param(
    [string] $Docx = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_lib.ps1')

$SeedDir = Join-Path $RepoRoot 'src/api/Slypn.Seed'
if (-not $Docx) {
    $Docx = Join-Path $RepoRoot 'brief/SLYPN_Newsletter_MAY_2026.docx'
}
if (-not (Test-Path $Docx)) { Write-Err "docx not found: $Docx"; exit 1 }

$localSettings = Join-Path $ApiDir 'local.settings.json'
if (-not (Test-Path $localSettings)) {
    Write-Err 'local.settings.json missing. Run scripts/setupLocal.ps1 first.'
    exit 1
}

$settings         = Get-Content $localSettings -Raw | ConvertFrom-Json
$connectionString = $settings.Values.'Storage__ConnectionString'

if (-not $connectionString) {
    Write-Err 'Storage__ConnectionString not set in local.settings.json.'
    Write-Err 'Re-copy from local.settings.sample.json (setupLocal.ps1 only copies if local.settings.json is absent).'
    exit 1
}

Write-Step "Seeding newsletter (from $Docx) + demo content (events, articles, blogs, resources)"

Push-Location $SeedDir
try {
    dotnet run --configuration Release --no-launch-profile -- $Docx --connection-string $connectionString --demo
    if ($LASTEXITCODE -ne 0) { throw "seed failed (exit $LASTEXITCODE)" }
}
finally { Pop-Location }

Write-Ok 'Seed complete.'
