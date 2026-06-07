<#
.SYNOPSIS
  Full SLYPN environment bootstrap — Azure infrastructure and Entra External ID.

.DESCRIPTION
  Five phases, each independently skippable:

  BICEP   Deploy infra/main.bicep, capture outputs, store Cosmos key and
          Storage connection string.

  ENTRA   Configure Entra External ID (CIAM):
            – API app (slypn-api): roles, scope, Graph User.Invite.All, secret
            – SPA app (slypn-web): PKCE, redirect URIs, pre-authorisation
            – User flows: checked and reported
            – Company branding HTML: uploaded to the storage static site

  SWA     Wire both halves together: set all app settings on the Static Web App
          (AzureAd, Graph, Cosmos, Storage) in one call.

  GITHUB  Set repository secrets needed by GitHub Actions:
            – AZURE_STATIC_WEB_APPS_API_TOKEN  (SWA deploy)
            – VITE_MSAL_AUTHORITY / VITE_MSAL_CLIENT_ID / VITE_API_SCOPE
              (passed to npm run build by azure-static-web-apps.yml)
          Requires the GitHub CLI (gh). Skipped automatically if gh is absent.

  LOCAL   Write src/web/.env.local and merge Entra values into
          src/api/Slypn.Api/local.settings.json so the local dev environment
          can authenticate against the real CIAM tenant.

  All derived values are saved to infra/secrets.json (gitignored by the
  existing "secrets.*" rule in .gitignore). Safe to re-run at any time —
  every operation checks current state before making changes.

.PARAMETER SkipBicep
  Skip Azure infrastructure deployment (use cached outputs from secrets.json).

.PARAMETER SkipEntra
  Skip Entra External ID setup.

.PARAMETER SkipSwa
  Skip configuring SWA app settings. Implied when both -SkipBicep and
  -SkipEntra are set and there is no cached SWA name.

.PARAMETER SkipLocal
  Skip writing local dev config files.

.PARAMETER SkipBranding
  Skip uploading the company branding HTML template to blob storage.

.PARAMETER SkipGitHub
  Skip the GitHub secrets phase. Also skipped automatically when the gh CLI
  is not found on PATH.

.PARAMETER RotateSecret
  Force-rotate the Graph API client secret even if it is not near expiry.
#>
[CmdletBinding()]
param(
    [switch]$SkipBicep,
    [switch]$SkipEntra,
    [switch]$SkipSwa,
    [switch]$SkipGitHub,
    [switch]$SkipLocal,
    [switch]$SkipBranding,
    [switch]$RotateSecret
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

# ── Helpers ───────────────────────────────────────────────────────────────────

function Step([string]$m) { Write-Host "`n◆ $m" -ForegroundColor Cyan }
function Ok  ([string]$m) { Write-Host "  ✓ $m" -ForegroundColor Green }
function Info([string]$m) { Write-Host "  · $m" -ForegroundColor DarkGray }
function Warn([string]$m) { Write-Host "  ! $m" -ForegroundColor Yellow }

$repoRoot     = Split-Path -Parent $PSScriptRoot
$scriptDir    = $PSScriptRoot
$secretsPath  = Join-Path $scriptDir 'secrets.json'
$templatePath = Join-Path $scriptDir 'signin-template.html'
$bicepPath    = Join-Path $scriptDir 'main.bicep'
$paramsPath   = Join-Path $scriptDir 'main.parameters.prod.json'
$envLocalPath = Join-Path $repoRoot 'src' 'web' '.env.local'
$localSettingsPath = Join-Path $repoRoot 'src' 'api' 'Slypn.Api' 'local.settings.json'
$localSettingsSamplePath = Join-Path $repoRoot 'src' 'api' 'Slypn.Api' 'local.settings.sample.json'

function Read-Secrets {
    if (Test-Path $secretsPath) {
        # ConvertFrom-Json -AsHashtable returns OrderedDictionary on PS 7.3+
        # which lacks ContainsKey. Copy into a plain hashtable to normalise.
        $raw = Get-Content $secretsPath -Raw | ConvertFrom-Json -AsHashtable
        $ht  = @{}
        foreach ($k in $raw.Keys) { $ht[$k] = $raw[$k] }
        return $ht
    }
    return @{}
}

function Save-Secrets([hashtable]$secrets) {
    $secrets | ConvertTo-Json -Depth 5 | Set-Content $secretsPath -Encoding UTF8
}

# Returns the cached value if present; otherwise prompts.
function Ask([hashtable]$s, [string]$key, [string]$label, [string]$default = '') {
    if ($s.Contains($key) -and -not [string]::IsNullOrWhiteSpace($s[$key])) {
        Info "$label = $($s[$key])"
        return $s[$key]
    }
    $hint = if ($default) { " [$default]" } else { '' }
    $val  = Read-Host "  ? $label$hint"
    if ([string]::IsNullOrWhiteSpace($val)) { $val = $default }
    if (-not [string]::IsNullOrWhiteSpace($val)) { $s[$key] = $val }
    return $val
}

# Call Microsoft Graph API. Acquires a fresh token every call.
function Invoke-Graph([string]$Method, [string]$Path, [object]$Body = $null,
                      [string]$ApiVersion = 'v1.0') {
    $token  = (az account get-access-token --resource https://graph.microsoft.com `
                 --query accessToken -o tsv 2>$null)
    $params = @{
        Method  = $Method
        Uri     = "https://graph.microsoft.com/$ApiVersion$Path"
        Headers = @{ Authorization = "Bearer $token"; 'Content-Type' = 'application/json' }
    }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10) }
    Invoke-RestMethod @params
}

# Upload raw file bytes to a Graph navigation property (e.g. branding assets).
function Invoke-GraphUpload([string]$Path, [string]$FilePath, [string]$ContentType,
                            [string]$ApiVersion = 'v1.0') {
    $token = (az account get-access-token --resource https://graph.microsoft.com `
                --query accessToken -o tsv 2>$null)
    $bytes = [IO.File]::ReadAllBytes($FilePath)
    Invoke-RestMethod `
        -Method Put `
        -Uri    "https://graph.microsoft.com/$ApiVersion$Path" `
        -Headers @{ Authorization = "Bearer $token"; 'Content-Type' = $ContentType } `
        -Body   $bytes
}

# Compare two string arrays as unordered sets.
function Compare-StringSets([string[]]$a, [string[]]$b) {
    $sa = [System.Collections.Generic.HashSet[string]]([string[]]@($a | Where-Object { $_ }))
    $sb = [System.Collections.Generic.HashSet[string]]([string[]]@($b | Where-Object { $_ }))
    return $sa.SetEquals($sb)
}

# Ensure the active az subscription context matches $subscriptionId. Re-prompts
# if we're currently in the CIAM tenant or wrong subscription.
function Switch-ToSubscription([string]$subscriptionId) {
    $current = az account show -o json 2>$null | ConvertFrom-Json
    if ($current -and $current.id -eq $subscriptionId) {
        return
    }
    # Try setting the subscription on the current login first.
    az account set --subscription $subscriptionId 2>$null
    if ($LASTEXITCODE -eq 0) { return }
    # If that failed (e.g. we're in the CIAM tenant context), re-login.
    Info 'Re-authenticating to Azure subscription...'
    az login | Out-Null
    az account set --subscription $subscriptionId | Out-Null
}

# ── Prerequisites ─────────────────────────────────────────────────────────────

Step 'Checking prerequisites'
foreach ($tool in @('az')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Required tool '$tool' is not installed or not on PATH."
    }
}
Ok 'Azure CLI found'

# ── Load secrets and gather inputs ────────────────────────────────────────────

$s = Read-Secrets

Write-Host "`nSLYPN  ·  Environment bootstrap" -ForegroundColor White
Write-Host "────────────────────────────────" -ForegroundColor DarkGray
Write-Host '  Cached values load from infra/secrets.json (press Enter to accept).' -ForegroundColor DarkGray

# Bicep inputs
if (-not $SkipBicep) {
    $subscriptionId = Ask $s 'subscriptionId' 'Azure subscription ID'
    $resourceGroup  = Ask $s 'resourceGroup'  'Resource group name' 'rg-slypn-prod'
    $location       = Ask $s 'location'       'Azure region' 'uksouth'
    $s['subscriptionId'] = $subscriptionId
    $s['resourceGroup']  = $resourceGroup
    $s['location']       = $location
}

# Entra inputs
if (-not $SkipEntra) {
    $tenantId     = Ask $s 'tenantId'     'CIAM tenant ID (GUID from Azure portal → Overview)'
    $tenantDomain = Ask $s 'tenantDomain' 'CIAM tenant domain' 'slypn.ciamlogin.com'
    $s['tenantId']     = $tenantId
    $s['tenantDomain'] = $tenantDomain
}

# Prod URL — known after Bicep, but can be entered manually if skipping Bicep.
if ($SkipBicep) {
    $prodUrl = Ask $s 'prodUrl' 'Production SWA URL (https://... — blank if not yet deployed)'
    if (-not [string]::IsNullOrWhiteSpace($prodUrl) -and -not $prodUrl.StartsWith('https://')) {
        $prodUrl = "https://$prodUrl"
    }
    if (-not [string]::IsNullOrWhiteSpace($prodUrl)) { $s['prodUrl'] = $prodUrl }
}

Save-Secrets $s

# ── Phase 1 · Azure infrastructure (Bicep) ────────────────────────────────────

if (-not $SkipBicep) {
    Step 'Phase 1 · Azure infrastructure'

    Switch-ToSubscription $subscriptionId

    # Resource group
    $rgExists = az group show --name $resourceGroup -o none 2>$null
    if ($LASTEXITCODE -ne 0) {
        Info "Creating resource group $resourceGroup in $location..."
        az group create --name $resourceGroup --location $location | Out-Null
        Ok "Created $resourceGroup"
    } else {
        Info "Resource group $resourceGroup exists"
    }

    # Bicep deployment
    Info 'Deploying main.bicep (this may take a few minutes)...'
    $deployResult = az deployment group create `
        --resource-group $resourceGroup `
        --template-file $bicepPath `
        --parameters @$paramsPath `
        --query 'properties.outputs' `
        -o json | ConvertFrom-Json

    $s['swaName']            = $deployResult.swaName.value
    $s['prodUrl']            = $deployResult.swaUrl.value.TrimEnd('/')
    $s['cosmosEndpoint']     = $deployResult.cosmosEndpoint.value
    $s['cosmosAccountName']  = $deployResult.cosmosAccountName.value
    $s['storageAccountName'] = $deployResult.storageAccountName.value
    $s['mediaContainerName'] = $deployResult.mediaContainerName.value
    $s['swaPrincipalId']     = $deployResult.swaPrincipalId.value
    Save-Secrets $s
    Ok "SWA deployed: $($s['prodUrl'])"

    # Cosmos primary key (used until managed-identity auth is wired up)
    Info 'Fetching Cosmos primary key...'
    $cosmosKey = az cosmosdb keys list `
        --name $s['cosmosAccountName'] `
        --resource-group $resourceGroup `
        --query 'primaryMasterKey' -o tsv
    $s['cosmosKey'] = $cosmosKey
    Save-Secrets $s
    Ok 'Cosmos key stored'

    # Storage connection string (used for BlobService until managed-identity)
    Info 'Fetching Storage connection string...'
    $storageConn = az storage account show-connection-string `
        --name $s['storageAccountName'] `
        --resource-group $resourceGroup `
        --query 'connectionString' -o tsv
    $s['storageConnectionString'] = $storageConn
    Save-Secrets $s
    Ok 'Storage connection string stored'

    # Derive prodUrl if it wasn't set yet (e.g. SWA default hostname)
    $prodUrl = $s['prodUrl']
}

# ── Phase 2 · Entra External ID ───────────────────────────────────────────────

if (-not $SkipEntra) {
    Step 'Phase 2 · Entra External ID'

    # ── Login to CIAM tenant ──────────────────────────────────────────────────

    # az login --tenant switches the active az context to the CIAM tenant,
    # which is required so that subsequent az ad / az rest calls target the
    # right directory. If the token is already cached (e.g. re-run) the CLI
    # uses it silently without opening a browser.
    $currentTenant = az account show --query 'tenantId' -o tsv 2>$null
    if ($currentTenant -ne $tenantId) {
        Info 'Switching to CIAM tenant...'
        az login --tenant $tenantId --allow-no-subscriptions | Out-Null
    }
    Ok "Signed in to $tenantDomain"

    # ── API app registration ──────────────────────────────────────────────────

    Step 'Entra · API app — slypn-api'
    $apiAppName = 'slypn-api'
    $existing   = az ad app list --filter "displayName eq '$apiAppName'" --query '[0]' -o json 2>$null |
                  ConvertFrom-Json
    if ($existing) {
        $apiClientId = $existing.appId
        $apiObjectId = $existing.id
        Info "Found  appId=$apiClientId"
    } else {
        $created     = az ad app create --display-name $apiAppName --sign-in-audience AzureADMyOrg -o json |
                       ConvertFrom-Json
        $apiClientId = $created.appId
        $apiObjectId = $created.id
        Ok "Created  appId=$apiClientId"
    }
    $s['apiClientId'] = $apiClientId
    $s['apiObjectId'] = $apiObjectId
    Save-Secrets $s

    # App ID URI
    $appIdUri    = "api://$apiClientId"
    $currentUris = @(az ad app show --id $apiObjectId --query 'identifierUris' -o json | ConvertFrom-Json)
    if ($currentUris -contains $appIdUri) {
        Info "App ID URI already set"
    } else {
        az ad app update --id $apiObjectId --identifier-uris $appIdUri | Out-Null
        Ok $appIdUri
    }

    # ── access_as_user scope ──────────────────────────────────────────────────

    Step 'Entra · OAuth scope — access_as_user'
    $apiApp         = Invoke-Graph GET "/applications/$apiObjectId"
    $existingScopes = @($apiApp.api.oauth2PermissionScopes)
    $hit            = $existingScopes | Where-Object { $_.value -eq 'access_as_user' }

    if ($hit) {
        $scopeId = $hit.id
        if (-not $hit.isEnabled) {
            $enabledCopy = @{
                id                      = $hit.id
                value                   = $hit.value
                type                    = $hit.type
                adminConsentDisplayName = $hit.adminConsentDisplayName
                adminConsentDescription = $hit.adminConsentDescription
                userConsentDisplayName  = $hit.userConsentDisplayName
                userConsentDescription  = $hit.userConsentDescription
                isEnabled               = $true
            }
            $others = @($existingScopes) | Where-Object { $_.id -ne $scopeId }
            Invoke-Graph PATCH "/applications/$apiObjectId" @{
                api = @{ oauth2PermissionScopes = @($others) + @($enabledCopy) }
            } | Out-Null
            Ok "Re-enabled  id=$scopeId"
        } else {
            Info "Already enabled  id=$scopeId"
        }
    } else {
        $scopeId  = [Guid]::NewGuid().ToString()
        $newScope = @{
            id                      = $scopeId
            value                   = 'access_as_user'
            type                    = 'User'
            adminConsentDisplayName = 'Access SLYPN API as a user'
            adminConsentDescription = 'Allows the app to access the SLYPN API on behalf of the signed-in user.'
            userConsentDisplayName  = 'Access SLYPN on your behalf'
            userConsentDescription  = 'Allows this app to access SLYPN on your behalf.'
            isEnabled               = $true
        }
        Invoke-Graph PATCH "/applications/$apiObjectId" @{
            api = @{ oauth2PermissionScopes = @($existingScopes) + @($newScope) }
        } | Out-Null
        Ok "Created  id=$scopeId"
    }
    $s['apiScopeId'] = $scopeId
    Save-Secrets $s

    # ── App roles ─────────────────────────────────────────────────────────────

    Step 'Entra · App roles — Admin, Contributor, Member'
    $apiApp        = Invoke-Graph GET "/applications/$apiObjectId"
    $existingRoles = @($apiApp.appRoles)
    $rolesToAdd    = @()
    $rolesToEnable = @()

    foreach ($roleName in @('Admin', 'Contributor', 'Member')) {
        $hit = $existingRoles | Where-Object { $_.value -eq $roleName }
        if ($hit) {
            $s["role${roleName}Id"] = $hit.id
            if (-not $hit.isEnabled) { $rolesToEnable += $hit.id; Info "$roleName disabled — will re-enable" }
            else { Info "$roleName  id=$($hit.id)" }
        } else {
            $roleId = if ($s.Contains("role${roleName}Id") -and $s["role${roleName}Id"]) `
                         { $s["role${roleName}Id"] } else { [Guid]::NewGuid().ToString() }
            $s["role${roleName}Id"] = $roleId
            $rolesToAdd += @{
                id                 = $roleId
                displayName        = $roleName
                description        = "SLYPN $roleName role"
                value              = $roleName
                allowedMemberTypes = @('User', 'Application')
                isEnabled          = $true
            }
        }
    }

    if ($rolesToAdd.Count -gt 0 -or $rolesToEnable.Count -gt 0) {
        $updatedRoles = $existingRoles | ForEach-Object {
            if ($rolesToEnable -contains $_.id) {
                $copy = $_ | Select-Object -Property *; $copy.isEnabled = $true; $copy
            } else { $_ }
        }
        Invoke-Graph PATCH "/applications/$apiObjectId" @{
            appRoles = @($updatedRoles) + $rolesToAdd
        } | Out-Null
        if ($rolesToAdd.Count -gt 0) {
            Ok "Created: $(($rolesToAdd | ForEach-Object { $_.displayName }) -join ', ')"
        }
        if ($rolesToEnable.Count -gt 0) { Ok "Re-enabled $($rolesToEnable.Count) role(s)" }
    }
    Save-Secrets $s

    # ── API service principal ─────────────────────────────────────────────────

    Step 'Entra · API service principal'
    $apiSpList = az ad sp list --filter "appId eq '$apiClientId'" -o json 2>$null | ConvertFrom-Json
    if (-not $apiSpList -or $apiSpList.Count -eq 0) {
        az ad sp create --id $apiClientId | Out-Null
        $apiSpList = az ad sp list --filter "appId eq '$apiClientId'" -o json | ConvertFrom-Json
    }
    $apiSpId      = $apiSpList[0].id
    $s['apiSpId'] = $apiSpId
    Save-Secrets $s
    Ok "objectId=$apiSpId"

    # ── Graph User.Invite.All permission ─────────────────────────────────────

    Step 'Entra · Graph User.Invite.All (invitation emails)'
    $graphAppId       = '00000003-0000-0000-c000-000000000000'
    $userInviteRoleId = '09850681-111b-4a89-9bed-3f2cae46d706'
    $graphSp          = az ad sp show --id $graphAppId -o json | ConvertFrom-Json
    $graphSpId        = $graphSp.id

    $assignments    = Invoke-Graph GET "/servicePrincipals/$apiSpId/appRoleAssignments"
    $alreadyGranted = $assignments.value |
        Where-Object { $_.resourceId -eq $graphSpId -and $_.appRoleId -eq $userInviteRoleId }

    if ($alreadyGranted) {
        Info 'User.Invite.All already granted'
    } else {
        Invoke-Graph POST "/servicePrincipals/$graphSpId/appRoleAssignedTo" @{
            principalId = $apiSpId
            resourceId  = $graphSpId
            appRoleId   = $userInviteRoleId
        } | Out-Null
        Ok 'User.Invite.All granted with admin consent'
    }

    # ── Graph client secret ───────────────────────────────────────────────────

    Step 'Entra · Graph client secret'

    # Look up by display name, not stored key ID — the ID in secrets.json can
    # go stale (e.g. after a failed run or a tenant switch bug). This also
    # catches and cleans up any accidental duplicates.
    $apiApp        = Invoke-Graph GET "/applications/$apiObjectId"
    $managed       = @($apiApp.passwordCredentials) |
                     Where-Object { $_.displayName -eq 'slypn-graph-invite' }
    $needsSecret   = $RotateSecret.IsPresent

    if (-not $needsSecret) {
        if ($managed.Count -gt 1) {
            Warn "$($managed.Count) duplicate secrets found — will clean up and rotate"
            $needsSecret = $true
        } elseif ($managed.Count -eq 1) {
            $expiry = [datetime]$managed[0].endDateTime
            if ($expiry -gt (Get-Date).AddDays(30)) {
                Info "Exists, expires $($expiry.ToString('yyyy-MM-dd'))"
                $s['graphClientSecretId'] = $managed[0].keyId
            } else {
                Warn "Expires $($expiry.ToString('yyyy-MM-dd')) — rotating"
                $needsSecret = $true
            }
        } else {
            $needsSecret = $true
        }
    }

    if ($needsSecret) {
        # Remove every slypn-graph-invite credential on the app before creating
        # a fresh one — prevents accumulation regardless of what secrets.json holds.
        foreach ($cred in $managed) {
            try {
                Invoke-Graph POST "/applications/$apiObjectId/removePassword" `
                    @{ keyId = $cred.keyId } | Out-Null
                Info "Removed old secret $($cred.keyId)"
            } catch {
                Warn "Could not remove secret $($cred.keyId): $_"
            }
        }
        $s.Remove('graphClientSecretId')
        $s.Remove('graphClientSecretExpiry')
        $s.Remove('graphClientSecret')

        $result = Invoke-Graph POST "/applications/$apiObjectId/addPassword" @{
            passwordCredential = @{
                displayName = 'slypn-graph-invite'
                endDateTime = (Get-Date).AddYears(2).ToString('o')
            }
        }
        $s['graphClientSecretId']     = $result.keyId
        $s['graphClientSecretExpiry'] = $result.endDateTime.ToString('yyyy-MM-dd')
        $s['graphClientSecret']       = $result.secretText
        Save-Secrets $s
        Ok "Created, expires $($result.endDateTime.ToString('yyyy-MM-dd'))"
        Warn 'Secret saved to infra/secrets.json. Remove it once it is in SWA app settings.'
    }

    # ── SPA app registration ──────────────────────────────────────────────────

    Step 'Entra · SPA app — slypn-web'
    $spaAppName  = 'slypn-web'
    $existingSpa = az ad app list --filter "displayName eq '$spaAppName'" --query '[0]' -o json 2>$null |
                   ConvertFrom-Json

    if ($existingSpa) {
        $spaClientId = $existingSpa.appId
        $spaObjectId = $existingSpa.id
        Info "Found  appId=$spaClientId"
    } else {
        $created     = az ad app create --display-name $spaAppName --sign-in-audience AzureADMyOrg -o json |
                       ConvertFrom-Json
        $spaClientId = $created.appId
        $spaObjectId = $created.id
        Ok "Created  appId=$spaClientId"
    }
    $s['spaClientId'] = $spaClientId
    $s['spaObjectId'] = $spaObjectId
    Save-Secrets $s

    # Redirect URIs — prod omitted if URL not yet known.
    $redirectUris = @('http://localhost:5173/auth/callback')
    $pUrl = if ($s.Contains('prodUrl')) { $s['prodUrl'] } else { '' }
    if (-not [string]::IsNullOrWhiteSpace($pUrl)) {
        $redirectUris = @("$pUrl/auth/callback") + $redirectUris
    }

    $spaApp           = Invoke-Graph GET "/applications/$spaObjectId"
    $currentRedirects = @($spaApp.spa.redirectUris)
    if (Compare-StringSets $redirectUris $currentRedirects) {
        Info "Redirect URIs already correct"
    } else {
        Invoke-Graph PATCH "/applications/$spaObjectId" @{
            spa = @{ redirectUris = $redirectUris }
        } | Out-Null
        Ok "Redirect URIs: $($redirectUris -join '  ')"
    }

    # SPA service principal
    $spaSpList = az ad sp list --filter "appId eq '$spaClientId'" -o json 2>$null | ConvertFrom-Json
    if (-not $spaSpList -or $spaSpList.Count -eq 0) {
        az ad sp create --id $spaClientId | Out-Null
    }

    # Required permissions
    $spaApp     = Invoke-Graph GET "/applications/$spaObjectId"
    $currentRRA = @($spaApp.requiredResourceAccess)
    $apiEntry   = $currentRRA | Where-Object { $_.resourceAppId -eq $apiClientId }
    $scopeOk    = $apiEntry -and ($apiEntry.resourceAccess |
                  Where-Object { $_.id -eq $scopeId -and $_.type -eq 'Scope' })

    if (-not $scopeOk) {
        $filtered = $currentRRA | Where-Object { $_.resourceAppId -ne $apiClientId }
        $newRRA   = @($filtered) + @(@{
            resourceAppId  = $apiClientId
            resourceAccess = @(@{ id = $scopeId; type = 'Scope' })
        })
        Invoke-Graph PATCH "/applications/$spaObjectId" @{
            requiredResourceAccess = $newRRA
        } | Out-Null
        Ok 'API scope added to SPA required permissions'
    } else {
        Info 'SPA required permissions already correct'
    }

    # Pre-authorise SPA
    $apiApp         = Invoke-Graph GET "/applications/$apiObjectId"
    $preAuth        = @($apiApp.api.preAuthorizedApplications)
    $alreadyPreAuth = $preAuth | Where-Object {
        $_.appId -eq $spaClientId -and ($_.delegatedPermissionIds -contains $scopeId)
    }

    if (-not $alreadyPreAuth) {
        $filtered   = $preAuth | Where-Object { $_.appId -ne $spaClientId }
        $newPreAuth = @($filtered) + @(@{
            appId                  = $spaClientId
            delegatedPermissionIds = @($scopeId)
        })
        Invoke-Graph PATCH "/applications/$apiObjectId" @{
            api = @{
                oauth2PermissionScopes    = @($apiApp.api.oauth2PermissionScopes)
                preAuthorizedApplications = $newPreAuth
            }
        } | Out-Null
        Ok 'SPA pre-authorised (users skip consent prompt)'
    } else {
        Info 'SPA already pre-authorised'
    }

    # ── User flows ────────────────────────────────────────────────────────────

    Step 'Entra · User flows'
    try {
        $flows = Invoke-Graph GET '/identity/authenticationEventsFlows' -ApiVersion 'beta'
        if ($flows.value -and $flows.value.Count -gt 0) {
            Ok "$($flows.value.Count) user flow(s) found:"
            $flows.value | ForEach-Object { Info "  $($_.displayName)" }
        } else {
            Warn 'No user flows found — create one in the CIAM portal:'
            Warn '  External Identities → User flows → New user flow'
            Warn '  Type:               Sign in and sign up'
            Warn '  Identity providers: Email with password'
            Warn '  Enable:             Self-service password reset'
            Warn "  Associated app:     slypn-web ($spaClientId)"
        }
    } catch {
        # The authenticationEventsFlows beta API requires the
        # EnableMsGraphAuthenticationEventListener feature, which is not
        # enabled on all CIAM tenants. This is a read-only check — safe to
        # ignore. Verify user flows manually in the CIAM portal.
        Info 'User flows API not available on this tenant — check manually:'
        Info '  Entra admin centre → External Identities → User flows'
    }

    # ── Company branding HTML ─────────────────────────────────────────────────

    if (-not $SkipBranding -and (Test-Path $templatePath)) {
        Step 'Entra · Company branding HTML'

        # Upload the HTML file content directly to Microsoft via the Graph
        # branding API. CIAM does not allow external URIs for custom HTML
        # (see aka.ms/entra-branding), so the file must be sent as bytes here.
        $brandingPath = "/organization/$tenantId/branding/localizations/0/customHTML"
        try {
            Invoke-GraphUpload $brandingPath $templatePath 'text/html' | Out-Null
            Ok 'Custom HTML uploaded to CIAM branding'
        } catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            Warn "Graph branding upload failed (HTTP $statusCode)."
            Warn 'Custom HTML branding may require a paid Entra External ID tier.'
            Warn 'Check: Entra admin centre → External Identities → Company Branding'
        }
    }

    # Make variables available for downstream phases.
    $tenantId     = $s['tenantId']
    $tenantDomain = $s['tenantDomain']
    $apiClientId  = $s['apiClientId']
    $spaClientId  = $s['spaClientId']
    $scopeId      = $s['apiScopeId']
}

# Re-read any values that were populated during Bicep or Entra phases.
$tenantId      = $s['tenantId']
$tenantDomain  = $s['tenantDomain']
$apiClientId   = $s['apiClientId']
$spaClientId   = $s['spaClientId']
$prodUrl       = $s['prodUrl']
$swaName       = $s['swaName']
$graphSecret   = $s['graphClientSecret']
$authority     = if ($tenantId -and $tenantDomain) { "https://$tenantDomain/$tenantId/v2.0" } else { '' }
$apiScopeStr   = if ($apiClientId) { "api://$apiClientId/access_as_user" } else { '' }

# ── Phase 3 · SWA app settings ────────────────────────────────────────────────

if (-not $SkipSwa -and $swaName) {
    Step 'Phase 3 · SWA app settings'

    if ($s.Contains('subscriptionId')) { Switch-ToSubscription $s['subscriptionId'] }

    $rg = $s['resourceGroup']

    # Build the full settings map — only include keys where we have a value.
    $settings = [ordered]@{}
    if ($authority)                              { $settings['AzureAd__Authority']        = $authority }
    if ($apiClientId)                            { $settings['AzureAd__Audience']         = "api://$apiClientId" }
    if ($tenantId)                               { $settings['AzureAd__TenantId']         = $tenantId }
                                                   $settings['AzureAd__SkipAuth']         = 'false'
    if ($graphSecret)                            { $settings['Graph__ClientSecret']       = $graphSecret }
    if ($prodUrl)                                { $settings['Graph__InviteRedirectUrl']  = "$prodUrl/" }
    if ($s['cosmosEndpoint'])                    { $settings['Cosmos__Endpoint']          = $s['cosmosEndpoint'] }
    if ($s['cosmosKey'])                         { $settings['Cosmos__Key']               = $s['cosmosKey'] }
                                                   $settings['Cosmos__Database']          = 'slypn'
    if ($s['storageConnectionString'])           { $settings['Storage__ConnectionString'] = $s['storageConnectionString'] }
                                                   $settings['Storage__MediaContainer']   = 'media'
                                                   $settings['Otel__ServiceName']         = 'slypn-api'
                                                   $settings['Otel__Env']                 = 'prod'

    $settingArgs = $settings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
    az staticwebapp appsettings set `
        --name $swaName `
        --resource-group $rg `
        --setting-names @settingArgs | Out-Null

    Ok "Applied $($settings.Count) setting(s) to $swaName"
} elseif (-not $SkipSwa) {
    Warn 'SWA name not known — skipping app settings (run with -SkipBicep after first deploy to apply)'
}

# ── Phase 4 · GitHub secrets ──────────────────────────────────────────────────

if (-not $SkipGitHub) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Warn 'GitHub CLI (gh) not found — skipping GitHub secrets phase.'
        Warn 'Install from https://cli.github.com, then re-run with -SkipBicep -SkipEntra -SkipSwa -SkipLocal'
    } else {
        Step 'Phase 4 · GitHub secrets'

        # Derive default repo from git remote, falling back to stored value.
        $gitRemoteUrl = git remote get-url origin 2>$null
        $defaultRepo  = if ($gitRemoteUrl -match 'github\.com[:/](.+?)(?:\.git)?$') `
                            { $Matches[1] } else { '' }
        $gitHubRepo   = Ask $s 'gitHubRepo' 'GitHub repo (owner/repo)' $defaultRepo
        $s['gitHubRepo'] = $gitHubRepo
        Save-Secrets $s

        # Authenticate if needed.
        gh auth status 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Info 'Logging in to GitHub CLI...'
            gh auth login
        }
        Ok "Authenticated to GitHub"

        # Helper: set a secret and report idempotently.
        function Set-GhSecret([string]$name, [string]$value) {
            if ([string]::IsNullOrWhiteSpace($value)) { Warn "Skipping $name — value not available"; return }
            $value | gh secret set $name --repo $gitHubRepo
            if ($LASTEXITCODE -eq 0) { Ok $name } else { Warn "Failed to set $name" }
        }

        # SWA deployment token — fetch fresh from Azure each run.
        if ($swaName -and $s['resourceGroup']) {
            if ($s.Contains('subscriptionId')) { Switch-ToSubscription $s['subscriptionId'] }
            $swaToken = az staticwebapp secrets list `
                --name $swaName `
                --resource-group $s['resourceGroup'] `
                --query 'properties.apiKey' -o tsv
            Set-GhSecret 'AZURE_STATIC_WEB_APPS_API_TOKEN' $swaToken
        } else {
            Warn 'SWA not known — skipping AZURE_STATIC_WEB_APPS_API_TOKEN'
        }

        # VITE_ build-time env vars consumed by azure-static-web-apps.yml.
        Set-GhSecret 'VITE_MSAL_AUTHORITY' $authority
        Set-GhSecret 'VITE_MSAL_CLIENT_ID' $spaClientId
        Set-GhSecret 'VITE_API_SCOPE'      $apiScopeStr
    }
}

# ── Phase 5 · Local dev configuration ─────────────────────────────────────────

if (-not $SkipLocal -and $authority -and $spaClientId -and $apiScopeStr) {
    Step 'Phase 5 · Local dev configuration'

    # .env.local
    $envLines = @(
        '# Auto-generated by infra/setup.ps1 — do not commit.'
        "VITE_MSAL_AUTHORITY=$authority"
        "VITE_MSAL_CLIENT_ID=$spaClientId"
        "VITE_API_SCOPE=$apiScopeStr"
        'VITE_DEV_SKIP_AUTH=false'
    )

    # Preserve any non-VITE_ vars already in the file (e.g. VITE_FARO_*).
    if (Test-Path $envLocalPath) {
        $existing = Get-Content $envLocalPath |
            Where-Object { $_ -notmatch '^#' -and $_ -match '=' } |
            Where-Object { $_ -notmatch '^VITE_MSAL_|^VITE_API_SCOPE|^VITE_DEV_SKIP_AUTH' }
        if ($existing) { $envLines += $existing }
    }

    $envLines | Set-Content $envLocalPath -Encoding UTF8
    Ok "Wrote $envLocalPath"

    # local.settings.json — create from sample if absent, then patch Entra/Graph fields.
    if (-not (Test-Path $localSettingsPath)) {
        if (Test-Path $localSettingsSamplePath) {
            Copy-Item $localSettingsSamplePath $localSettingsPath
            Info 'Created local.settings.json from sample'
        } else {
            Warn 'local.settings.sample.json not found — cannot create local.settings.json'
        }
    }

    if (Test-Path $localSettingsPath) {
        $ls = Get-Content $localSettingsPath -Raw | ConvertFrom-Json -AsHashtable

        $ls['Values']['AzureAd__Authority'] = $authority
        $ls['Values']['AzureAd__Audience']  = "api://$apiClientId"
        $ls['Values']['AzureAd__TenantId']  = $tenantId
        $ls['Values']['AzureAd__SkipAuth']  = 'false'
        # Set Graph secret for local invitation testing if available.
        if ($graphSecret) {
            $ls['Values']['Graph__ClientSecret'] = $graphSecret
        }
        $ls['Values']['Graph__InviteRedirectUrl'] = 'http://localhost:5173/'

        $ls | ConvertTo-Json -Depth 5 | Set-Content $localSettingsPath -Encoding UTF8
        Ok "Updated $localSettingsPath (Entra/Graph fields; Cosmos/Storage unchanged)"
    }
} elseif (-not $SkipLocal -and (-not $authority -or -not $spaClientId -or -not $apiScopeStr)) {
    Warn 'Entra values not available — skipping local dev config (run without -SkipEntra first)'
}

# ── Summary ───────────────────────────────────────────────────────────────────

Save-Secrets $s

Write-Host ''
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White
Write-Host ' SLYPN · Setup complete' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor White

if ($authority) {
    Write-Host ''
    Write-Host ' src/web/.env.local' -ForegroundColor Yellow
    Write-Host "   VITE_MSAL_AUTHORITY=$authority"
    Write-Host "   VITE_MSAL_CLIENT_ID=$spaClientId"
    Write-Host "   VITE_API_SCOPE=$apiScopeStr"
    Write-Host "   VITE_DEV_SKIP_AUTH=false"
}

if ($swaName -and $authority) {
    Write-Host ''
    Write-Host " SWA app settings applied to $swaName" -ForegroundColor Yellow
    if ($graphSecret) {
        Warn "  Remove graphClientSecret from infra/secrets.json now that it is in SWA settings."
    }
}

if ($s.Contains('gitHubRepo') -and -not $SkipGitHub) {
    Write-Host ''
    Write-Host " GitHub secrets set on $($s['gitHubRepo'])" -ForegroundColor Yellow
    Write-Host '   AZURE_STATIC_WEB_APPS_API_TOKEN'
    Write-Host '   VITE_MSAL_AUTHORITY  ·  VITE_MSAL_CLIENT_ID  ·  VITE_API_SCOPE'
}


Write-Host ''
Write-Host ' infra/secrets.json holds all IDs and the Graph client secret.' -ForegroundColor DarkGray
Write-Host ' It is git-ignored. Back it up to a password manager or Key Vault.' -ForegroundColor DarkGray
Write-Host ''
