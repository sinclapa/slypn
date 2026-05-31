#requires -Version 7
<#
.SYNOPSIS
  Seeds Cosmos DB with the sample newsletter parsed from brief/.

.DESCRIPTION
  Reads Cosmos__* values from src/api/Slypn.Api/local.settings.json, then
  runs the Slypn.Seed console app against brief/SLYPN_Newsletter_MAY_2026.docx
  and upserts a newsletter document into the configured database.

.EXAMPLE
  .\scripts\seed.ps1
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
    Write-Err 'local.settings.json missing. Run scripts/setup.ps1 first.'
    exit 1
}

$settings = Get-Content $localSettings -Raw | ConvertFrom-Json
$endpoint = $settings.Values.'Cosmos__Endpoint'
$key      = $settings.Values.'Cosmos__Key'
$database = $settings.Values.'Cosmos__Database'

if (-not $endpoint -or -not $key -or -not $database) {
    Write-Err 'Cosmos__Endpoint / Cosmos__Key / Cosmos__Database not set in local.settings.json.'
    Write-Err 'Re-copy from local.settings.sample.json (setup.ps1 only copies if local.settings.json is absent).'
    exit 1
}

Write-Step "Seeding newsletter from $Docx into $database"

Push-Location $SeedDir
try {
    dotnet run --configuration Release --no-launch-profile -- $Docx --endpoint $endpoint --key $key --database $database
    if ($LASTEXITCODE -ne 0) { throw "seed failed (exit $LASTEXITCODE)" }
}
finally { Pop-Location }

Write-Ok 'Seed complete.'
