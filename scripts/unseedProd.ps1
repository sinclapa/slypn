<#
.SYNOPSIS
  Removes the sample/demo content seeded into SLYPN production.

.DESCRIPTION
  Deletes ONLY the rows/blobs created by the demo seed (stable, prefixed ids):
    - members  : RowKeys starting "seedmember"
    - articles : RowKeys starting "seedart" / "seedblog"  (+ content blobs)
    - events   : RowKeys starting "evt-coffee-" / "evt-extra-"
    - resources: RowKeys starting "seedres"
  Real content (GUID/hex ids) is never matched, so live data is untouched.

  Requires an authenticated Azure CLI (az login) with access to the prod
  subscription. The storage connection string is fetched at runtime — no secret
  is stored in this file.

.EXAMPLE
  pwsh scripts/unseedProd.ps1            # prompts for confirmation
  pwsh scripts/unseedProd.ps1 -Force     # no prompt (use in CI/automation)
#>
[CmdletBinding()]
param(
    [string]$ResourceGroup  = "rg-slypn-prod",
    [string]$StorageAccount = "slypnprodstgfuicmindwdk4",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

Write-Host "Resolving connection string for $StorageAccount ($ResourceGroup)…"
$cs = az storage account show-connection-string -g $ResourceGroup -n $StorageAccount --query connectionString -o tsv
if (-not $cs) { throw "Could not get connection string. Are you logged in (az login) and on the right subscription?" }

if (-not $Force) {
    Write-Host "This will DELETE all demo-seeded rows from PROD ($StorageAccount)." -ForegroundColor Yellow
    $answer = Read-Host "Type the storage account name to confirm"
    if ($answer -ne $StorageAccount) { Write-Host "Aborted."; return }
}

function Remove-SeedRows {
    param([string]$Table, [string[]]$Prefixes)
    $items = az storage entity query --table-name $Table --connection-string $cs `
        --query "items[].{pk:PartitionKey, rk:RowKey}" -o json | ConvertFrom-Json
    $n = 0
    foreach ($it in $items) {
        $match = $false
        foreach ($p in $Prefixes) { if ($it.rk.StartsWith($p)) { $match = $true; break } }
        if (-not $match) { continue }
        az storage entity delete --table-name $Table --connection-string $cs `
            --partition-key $it.pk --row-key $it.rk | Out-Null
        Write-Host "  - $Table/$($it.rk)"
        $n++
    }
    Write-Host "Deleted $n row(s) from $Table."
}

Remove-SeedRows -Table "members"   -Prefixes @("seedmember")
Remove-SeedRows -Table "articles"  -Prefixes @("seedart", "seedblog")
Remove-SeedRows -Table "events"    -Prefixes @("evt-coffee-", "evt-extra-")
Remove-SeedRows -Table "resources" -Prefixes @("seedres")

# Article/blog body blobs live under content/articles/<id>.
$blobs = az storage blob list --container-name content --connection-string $cs `
    --prefix "articles/seed" --query "[].name" -o tsv
$bn = 0
foreach ($b in ($blobs -split "`n" | Where-Object { $_ })) {
    az storage blob delete --container-name content --connection-string $cs --name $b | Out-Null
    Write-Host "  - blob $b"
    $bn++
}
Write-Host "Deleted $bn content blob(s)."
Write-Host "Done. Demo seed removed from production." -ForegroundColor Green
