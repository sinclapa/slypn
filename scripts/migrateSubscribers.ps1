<#
.SYNOPSIS
  Moves legacy newsletter subscribers out of the `members` table into `subscribers` (SEC-5).

.DESCRIPTION
  Before SEC-5, POST /api/newsletter/subscribe stored each address as a `members` row with
  Status="subscribed", so subscribers and invited members shared one table. Subscribers have their
  own table now and these rows have to follow.

  The move itself runs in the Slypn.Seed console (--migrate-subscribers), which talks to Table
  Storage through the Azure SDK; this script only resolves the connection string and confirms the
  target. See src/api/Slypn.Seed/MigrateSubscribers.cs for the selection rules — in short, a row
  moves only when its status is "subscribed" AND it holds no roles AND it has no oid, which is
  exactly the "not invited" test the API uses, so a real member is never touched.

  Idempotent and re-runnable: the destination row key is derived from the address, and rows already
  migrated no longer match the source filter.

  Requires an authenticated Azure CLI (az login) with access to the target subscription, unless
  -ConnectionString is supplied. No secret is stored in this file.

.EXAMPLE
  pwsh scripts/migrateSubscribers.ps1 -DryRun          # report what would move, change nothing
  pwsh scripts/migrateSubscribers.ps1                  # prompts for confirmation
  pwsh scripts/migrateSubscribers.ps1 -Force           # no prompt (use in CI/automation)
  pwsh scripts/migrateSubscribers.ps1 -ConnectionString "UseDevelopmentStorage=true" -Force
#>
[CmdletBinding()]
param(
    [string]$ResourceGroup  = "rg-slypn-prod",
    [string]$StorageAccount = "slypnprodstgfuicmindwdk4",
    # Skips the az lookup entirely — used to run this against local Azurite.
    [string]$ConnectionString,
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$cs = $ConnectionString
if (-not $cs) {
    Write-Host "Resolving connection string for $StorageAccount ($ResourceGroup)…"
    $cs = az storage account show-connection-string -g $ResourceGroup -n $StorageAccount --query connectionString -o tsv
    if (-not $cs) { throw "Could not get connection string. Are you logged in (az login) and on the right subscription?" }
    $target = $StorageAccount
}
else {
    $target = "the supplied connection string"
}

if (-not $DryRun -and -not $Force) {
    Write-Host "This will MOVE subscriber rows out of members in $target." -ForegroundColor Yellow
    Write-Host "Run with -DryRun first if you haven't." -ForegroundColor Yellow
    $answer = Read-Host "Type the storage account name to confirm"
    if ($answer -ne $StorageAccount) { Write-Host "Aborted."; return }
}

$seed = Join-Path $PSScriptRoot "../src/api/Slypn.Seed"
$seedArgs = @("--migrate-subscribers", "--connection-string", $cs)
if ($DryRun) { $seedArgs += "--dry-run" }

dotnet run --project $seed -- @seedArgs
if ($LASTEXITCODE -ne 0) { throw "Migration failed (exit $LASTEXITCODE)." }

Write-Host "Done." -ForegroundColor Green
