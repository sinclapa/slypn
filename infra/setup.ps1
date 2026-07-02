<#
.SYNOPSIS
  Full SLYPN environment bootstrap — Azure infrastructure and Entra External ID.

.DESCRIPTION
  Five phases, each independently skippable:

  BICEP   Deploy infra/main.bicep, capture outputs, store the Storage
          connection string (Table + Blob).

  ENTRA   Configure Entra External ID (CIAM):
            – API app (slypn-api): roles, scope
            – SPA app (slypn-web): PKCE, redirect URIs, pre-authorisation
            – User flows: checked and reported (must be Sign up and sign in type)
            – Custom auth extension: sign-up gate (created via Graph; 2 manual portal clicks remain)

  SWA     Wire both halves together: set all app settings on the Static Web App
          (AzureAd, Graph, Storage) in one call.

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

# Grafana Cloud inputs (optional — skip by pressing Enter).
$grafanaOtlpUrl    = Ask $s 'grafanaOtlpUrl'    'Grafana OTLP endpoint (Enter to skip)' ''
$grafanaInstanceId = Ask $s 'grafanaInstanceId'  'Grafana instance ID   (Enter to skip)' ''
$grafanaApiToken   = Ask $s 'grafanaApiToken'    'Grafana API token     (Enter to skip)' ''
$faroUrl              = Ask $s 'faroUrl'              'Grafana Faro collector URL (Enter to skip)' ''
$faroSourcemapEndpoint = Ask $s 'faroSourcemapEndpoint' 'Faro source map endpoint   (Enter to skip)' ''
$faroSourcemapAppId    = Ask $s 'faroSourcemapAppId'    'Faro source map app ID     (Enter to skip)' ''
$faroSourcemapStackId  = Ask $s 'faroSourcemapStackId'  'Faro source map stack ID   (Enter to skip)' ''
$faroSourcemapApiKey   = Ask $s 'faroSourcemapApiKey'   'Faro source map API key    (Enter to skip)' ''
if (-not [string]::IsNullOrWhiteSpace($grafanaOtlpUrl))        { $s['grafanaOtlpUrl']        = $grafanaOtlpUrl }
if (-not [string]::IsNullOrWhiteSpace($grafanaInstanceId))     { $s['grafanaInstanceId']     = $grafanaInstanceId }
if (-not [string]::IsNullOrWhiteSpace($grafanaApiToken))       { $s['grafanaApiToken']       = $grafanaApiToken }
if (-not [string]::IsNullOrWhiteSpace($faroUrl))               { $s['faroUrl']               = $faroUrl }
if (-not [string]::IsNullOrWhiteSpace($faroSourcemapEndpoint)) { $s['faroSourcemapEndpoint'] = $faroSourcemapEndpoint }
if (-not [string]::IsNullOrWhiteSpace($faroSourcemapAppId))    { $s['faroSourcemapAppId']    = $faroSourcemapAppId }
if (-not [string]::IsNullOrWhiteSpace($faroSourcemapStackId))  { $s['faroSourcemapStackId']  = $faroSourcemapStackId }
if (-not [string]::IsNullOrWhiteSpace($faroSourcemapApiKey))   { $s['faroSourcemapApiKey']   = $faroSourcemapApiKey }

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

    $s['swaName']              = $deployResult.swaName.value
    $s['prodUrl']              = $deployResult.swaUrl.value.TrimEnd('/')
    $s['storageAccountName']   = $deployResult.storageAccountName.value
    $s['mediaContainerName']   = $deployResult.mediaContainerName.value
    $s['contentContainerName'] = $deployResult.contentContainerName.value
    $s['swaPrincipalId']       = $deployResult.swaPrincipalId.value
    Save-Secrets $s
    Ok "SWA deployed: $($s['prodUrl'])"

    # Storage connection string (Table + Blob; used until managed-identity auth is wired up)
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

    $currentTenant = az account show --query 'tenantId' -o tsv 2>$null
    if ($currentTenant -eq $tenantId) {
        # Already in CIAM context — check the token is still valid.
        az account get-access-token --resource https://graph.microsoft.com -o none 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Ok "Signed in to $tenantDomain (cached — skipping browser login)"
        } else {
            Info 'Token expired — re-authenticating...'
            az login --tenant $tenantId --allow-no-subscriptions | Out-Null
            Ok "Signed in to $tenantDomain"
        }
    } else {
        Info "Switching to CIAM tenant $tenantDomain..."
        az login --tenant $tenantId --allow-no-subscriptions | Out-Null
        Ok "Signed in to $tenantDomain"
    }

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

    # App ID URI — base form api://<clientId> plus the SWA-hostname form
    # required by Custom Auth Extensions: api://<swa-host>/<clientId>.
    $appIdUri    = "api://$apiClientId"
    $currentUris = [System.Collections.Generic.List[string]](
        @(az ad app show --id $apiObjectId --query 'identifierUris' -o json | ConvertFrom-Json))
    $urisChanged = $false
    if (-not $currentUris.Contains($appIdUri)) { $currentUris.Add($appIdUri); $urisChanged = $true }
    $pUrl = if ($s.Contains('prodUrl') -and -not [string]::IsNullOrWhiteSpace($s['prodUrl'])) { $s['prodUrl'] } else { '' }
    $extAppIdUri = ''
    if (-not [string]::IsNullOrWhiteSpace($pUrl)) {
        $swaHost     = ([Uri]$pUrl).Host
        $extAppIdUri = "api://$swaHost/$apiClientId"
        if (-not $currentUris.Contains($extAppIdUri)) { $currentUris.Add($extAppIdUri); $urisChanged = $true }
    }
    if ($urisChanged) {
        az ad app update --id $apiObjectId --identifier-uris @($currentUris) | Out-Null
        Ok "Identifier URIs: $($currentUris -join '  ')"
    } else {
        Info "Identifier URIs already set"
    }
    if ([string]::IsNullOrWhiteSpace($extAppIdUri)) {
        Warn 'Extension App ID URI (api://<swa-host>/...) not set — prodUrl not yet known. Re-run with -SkipBicep after first deploy.'
    }

    # requestedAccessTokenVersion = 2 — CIAM must issue v2 access tokens so
    # they include a `kid` header. Without this the API validator fails with
    # "kid is missing" because v1 tokens use a different signing key format.
    $apiApp = Invoke-Graph GET "/applications/$apiObjectId"
    if ($apiApp.api.requestedAccessTokenVersion -ne 2) {
        Invoke-Graph PATCH "/applications/$apiObjectId" @{
            api = @{ requestedAccessTokenVersion = 2 }
        } | Out-Null
        Ok 'requestedAccessTokenVersion set to 2'
    } else {
        Info 'requestedAccessTokenVersion already 2'
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

    # ── Graph User.ReadWrite.All permission (member deletion) ────────────────

    Step 'Entra · Graph User.ReadWrite.All (Entra account deletion)'
    $graphAppId         = '00000003-0000-0000-c000-000000000000'
    $userReadWriteAllId = '741f803b-c850-494e-b5df-cde7c675a1ca'
    $graphSp            = az ad sp show --id $graphAppId -o json | ConvertFrom-Json
    $graphSpId          = $graphSp.id

    $assignments    = Invoke-Graph GET "/servicePrincipals/$apiSpId/appRoleAssignments"
    $alreadyGranted = $assignments.value |
        Where-Object { $_.resourceId -eq $graphSpId -and $_.appRoleId -eq $userReadWriteAllId }

    if ($alreadyGranted) {
        Info 'User.ReadWrite.All already granted'
    } else {
        Invoke-Graph POST "/servicePrincipals/$graphSpId/appRoleAssignedTo" @{
            principalId = $apiSpId
            resourceId  = $graphSpId
            appRoleId   = $userReadWriteAllId
        } | Out-Null
        Ok 'User.ReadWrite.All granted with admin consent'
    }

    # ── Graph CustomAuthenticationExtensions.Receive.Payload ─────────────────
    # The CIAM tenant does not expose this appRole via the Graph SP, so the GUID
    # cannot be looked up programmatically. Instead, read the role ID from the
    # app's own requiredResourceAccess (populated when the permission is added in
    # the portal), then verify the appRoleAssignment exists.

    Step 'Entra · Graph CustomAuthenticationExtensions.Receive.Payload'
    $apiApp          = Invoke-Graph GET "/applications/$apiObjectId"
    $graphRRAEntry   = @($apiApp.requiredResourceAccess) |
                           Where-Object { $_.resourceAppId -eq $graphAppId }
    $customAuthRoles = if ($graphRRAEntry) {
        @($graphRRAEntry.resourceAccess) |
            Where-Object { $_.type -eq 'Role' -and $_.id -ne $userReadWriteAllId }
    } else { @() }

    if ($customAuthRoles.Count -eq 0) {
        Warn 'Not yet added — add it manually in the slypn-api app registration:'
        Warn '  App registrations → slypn-api → API permissions'
        Warn '  → Add a permission → Microsoft Graph → Application permissions'
        Warn '  → Search "CustomAuthentication" → tick CustomAuthenticationExtensions.Receive.Payload'
        Warn '  → Add permissions → Grant admin consent for SLYPN'
    } else {
        $spAssignments = Invoke-Graph GET "/servicePrincipals/$apiSpId/appRoleAssignments"
        $granted = $customAuthRoles | Where-Object {
            $id = $_.id
            $spAssignments.value | Where-Object { $_.appRoleId -eq $id -and $_.resourceId -eq $graphSpId }
        }
        if ($granted) {
            Ok 'CustomAuthenticationExtensions.Receive.Payload granted'
        } else {
            Warn 'Permission added but admin consent not yet granted — click "Grant admin consent for SLYPN":'
            Warn '  App registrations → slypn-api → API permissions'
        }
    }

    # ── Graph client secret (used for Entra account deletion) ─────────────────

    Step 'Entra · Graph client secret'
    $apiApp      = Invoke-Graph GET "/applications/$apiObjectId"
    $managed     = @(@($apiApp.passwordCredentials) |
                   Where-Object { $_.displayName -eq 'slypn-graph-mgmt' })
    $needsSecret = $RotateSecret.IsPresent

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
                displayName = 'slypn-graph-mgmt'
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
            $signUpFlows = $flows.value | Where-Object {
                $_.displayName -match 'sign.?up' -or
                $_.'@odata.type' -match 'externalUsersSelfServiceSignUp'
            }
            Ok "$($flows.value.Count) user flow(s) found:"
            $flows.value | ForEach-Object { Info "  $($_.displayName)" }
            if (-not $signUpFlows) {
                Warn ''
                Warn 'WARNING: No "Sign up and sign in" flow detected.'
                Warn 'A "Sign in" only flow will block new users with "account not found".'
                Warn 'Create a new flow in the CIAM portal (see instructions below).'
            }
        } else {
            Warn 'No user flows found. Create one in the CIAM portal:'
        }
        if (-not $flows.value -or $flows.value.Count -eq 0) {
            Warn ''
            Warn '  1. External Identities → User flows → New user flow'
            Warn '  2. Flow type:          Sign up and sign in  ← IMPORTANT: not "Sign in" only'
            Warn '  3. Identity providers: Email with password'
            Warn '  4. User attributes:    tick Display Name and Email Address'
            Warn "  5. Associated app:     slypn-web ($spaClientId)"
            Warn ''
            Warn '  Self-service sign-up must be ON so new invited users can register.'
            Warn '  After creating the flow, run "Run user flow" in the portal to smoke-test.'
        }
    } catch {
        # The authenticationEventsFlows beta API requires the
        # EnableMsGraphAuthenticationEventListener feature, which is not
        # enabled on all CIAM tenants. This is a read-only check — safe to
        # ignore. Verify user flows manually in the CIAM portal.
        Info 'User flows API not available on this tenant — check manually:'
        Info '  Entra admin centre → External Identities → User flows'
    }

    # ── Custom authentication extension — sign-up gate ───────────────────────
    # The Graph beta API for custom extensions requires the delegated permission
    # CustomAuthenticationExtension.ReadWrite.All, which az CLI tokens cannot
    # include in a no-subscription CIAM tenant context. Create it manually once
    # using the steps below; the extension ID is then saved to secrets.json.

    Step 'Entra · Custom auth extension — sign-up gate'

    # Shared secret that authenticates the CIAM → allow-signup callout. SWA strips
    # the OAuth Authorization header from managed-Functions calls, so we can't
    # validate the CIAM token; instead the secret travels in the Target URL (?k=)
    # and the API checks it (plus the callout's tenant + extension id).
    if (-not $s.Contains('signupGateSecret') -or [string]::IsNullOrWhiteSpace($s['signupGateSecret'])) {
        $s['signupGateSecret'] = [Convert]::ToBase64String(
            [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        ).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        Save-Secrets $s
        Ok 'Generated sign-up gate shared secret'
    }

    $extApiUrl = if ($s.Contains('prodUrl') -and -not [string]::IsNullOrWhiteSpace($s['prodUrl'])) {
        "$($s['prodUrl'])/api/auth/allow-signup?k=$($s['signupGateSecret'])"
    } else { '<prodUrl>/api/auth/allow-signup?k=<signupGateSecret> (re-run after first deploy)' }

    if (-not $s.Contains('signupExtensionId') -or [string]::IsNullOrWhiteSpace($s['signupExtensionId'])) {
        Warn 'Sign-up gate extension not yet created. Follow these steps in the CIAM portal:'
        Warn ''
        Warn '  Step 1 — Create the extension'
        Warn '    External Identities → Custom authentication extensions → + Create'
        Warn '    Name:       SLYPN sign-up gate'
        Warn '    Event type: AttributeCollectionStart'
        Warn "    Target URL: $extApiUrl"
        Warn '    Auth tab:   Select an existing app registration → slypn-api'
        Warn '                (do NOT create a new registration — the token audience must match)'
        Warn '    Timeout:    2 000 ms   Retries: 1'
        Warn '    → Save'
        Warn ''
        Warn '  Step 2 — Associate with your user flow  (THIS is what enforces the gate)'
        Warn '    External Identities → User flows → slypn-signin-signup'
        Warn '    Left menu (Settings) → Custom authentication extensions'
        Warn "    Click the pencil icon next to 'Before collecting information from the user'"
        Warn "    Select 'SLYPN sign-up gate' → Save (top toolbar)"
        Warn '    Without this association CIAM never calls /api/auth/allow-signup'
        Warn '    and ANY email can register — creating the extension is not enough.'
        Warn ''
        $extId = Ask $s 'signupExtensionId' 'Paste the Extension ID from the portal (or Enter to skip)'
        if (-not [string]::IsNullOrWhiteSpace($extId)) {
            $s['signupExtensionId'] = $extId
            Save-Secrets $s
            Ok "Extension ID saved: $extId"
        }
    } else {
        Ok "Extension created: $($s['signupExtensionId'])"
        # The extension ID being saved only proves the extension exists — NOT that
        # it's wired to the user flow. That association is a manual portal step the
        # CLI/Graph cannot read back in a CIAM tenant, so we can't verify it here.
        Warn 'Reminder: confirm the extension is ASSOCIATED with the slypn-signin-signup'
        Warn "  user flow ('Before collecting information from the user'). If it isn't,"
        Warn '  uninvited emails can still register.'
        Warn '  Verify in Grafana — uninvited sign-ups should log an AllowSignup line:'
        Warn '    {service_name="slypn-api"} |= "AllowSignup"'
        Warn '  No AllowSignup lines while sign-ups happen ⇒ the gate is not wired.'
        Warn '  See docs/auth-setup.md §6.4 "Verifying the gate is actually enforced".'
    }


    # ── CI service principal — PR preview redirect URI management ─────────────
    # slypn-ci lives in the CIAM tenant and holds Application.ReadWrite.OwnedBy
    # on Microsoft Graph. It is added as an owner of slypn-web so it can patch
    # the SPA redirect URI list when a PR preview is opened or closed.

    Step 'Entra · CI service principal (slypn-ci)'

    $ciAppName = 'slypn-ci'
    $ciApps    = (Invoke-Graph GET "/applications?`$filter=displayName eq '$ciAppName'").value
    $ciApp     = $ciApps | Select-Object -First 1
    if ($ciApp) {
        $ciClientId = $ciApp.appId
        $ciObjectId = $ciApp.id
        Info "Found: appId=$ciClientId"
    } else {
        $ciApp      = Invoke-Graph POST '/applications' @{
            displayName    = $ciAppName
            signInAudience = 'AzureADMyOrg'
        }
        $ciClientId = $ciApp.appId
        $ciObjectId = $ciApp.id
        Ok "Created: appId=$ciClientId"
    }
    $s['ciClientId'] = $ciClientId
    $s['ciObjectId'] = $ciObjectId
    Save-Secrets $s

    # Ensure the service principal exists in the tenant.
    $ciSp = (Invoke-Graph GET "/servicePrincipals?`$filter=appId eq '$ciClientId'").value |
                Select-Object -First 1
    if (-not $ciSp) {
        $ciSp = Invoke-Graph POST '/servicePrincipals' @{ appId = $ciClientId }
        Ok 'Created service principal'
    } else {
        Info "Service principal: $($ciSp.id)"
    }
    $ciSpId = $ciSp.id

    # Grant Application.ReadWrite.OwnedBy (admin consent via appRoleAssignment).
    $appReadWriteOwnedById = '18a4783c-866b-4cc7-a460-3d5e5662c884'
    $ciAssignments = (Invoke-Graph GET "/servicePrincipals/$ciSpId/appRoleAssignments").value
    $alreadyGranted = $ciAssignments | Where-Object {
        $_.resourceId -eq $graphSpId -and $_.appRoleId -eq $appReadWriteOwnedById
    }
    if ($alreadyGranted) {
        Info 'Application.ReadWrite.OwnedBy already granted'
    } else {
        Invoke-Graph POST "/servicePrincipals/$graphSpId/appRoleAssignedTo" @{
            principalId = $ciSpId
            resourceId  = $graphSpId
            appRoleId   = $appReadWriteOwnedById
        } | Out-Null
        Ok 'Granted Application.ReadWrite.OwnedBy'
    }

    # Add CI SP as owner of slypn-web (required for OwnedBy permission to apply).
    $owners      = (Invoke-Graph GET "/applications/$spaObjectId/owners").value
    $alreadyOwner = $owners | Where-Object { $_.id -eq $ciSpId }
    if ($alreadyOwner) {
        Info 'CI SP already owner of slypn-web'
    } else {
        Invoke-Graph POST "/applications/$spaObjectId/owners/`$ref" @{
            '@odata.id' = "https://graph.microsoft.com/v1.0/directoryObjects/$ciSpId"
        } | Out-Null
        Ok 'Added CI SP as owner of slypn-web'
    }

    # Create / rotate client secret.
    $ciSecretName  = 'slypn-ci-gh-actions'
    $ciCredentials = (Invoke-Graph GET "/applications/$ciObjectId").passwordCredentials
    $managed       = @($ciCredentials | Where-Object { $_.displayName -eq $ciSecretName })
    $needsSecret   = $RotateSecret.IsPresent
    if (-not $s.Contains('ciClientSecret') -or [string]::IsNullOrWhiteSpace($s['ciClientSecret'])) {
        $needsSecret = $true
    } elseif ($managed.Count -gt 0) {
        $expiry = [datetime]$managed[0].endDateTime
        if ($expiry -gt (Get-Date).AddDays(30)) {
            Info "Secret exists, expires $($expiry.ToString('yyyy-MM-dd'))"
        } else {
            Warn "Expires $($expiry.ToString('yyyy-MM-dd')) — rotating"
            $needsSecret = $true
        }
    } else {
        $needsSecret = $true
    }
    if ($needsSecret) {
        foreach ($cred in $managed) {
            try { Invoke-Graph POST "/applications/$ciObjectId/removePassword" @{ keyId = $cred.keyId } | Out-Null } catch {}
        }
        $result = Invoke-Graph POST "/applications/$ciObjectId/addPassword" @{
            passwordCredential = @{
                displayName = $ciSecretName
                endDateTime = (Get-Date).AddYears(2).ToString('o')
            }
        }
        $s['ciClientSecret'] = $result.secretText
        Save-Secrets $s
        Ok "Secret created, expires $($result.endDateTime.ToString('yyyy-MM-dd'))"
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
$graphSecret   = if ($s.Contains('graphClientSecret')) { $s['graphClientSecret'] } else { '' }
$authority     = if ($tenantId -and $tenantDomain) { "https://$tenantDomain/$tenantId/v2.0" } else { '' }
$apiScopeStr   = if ($apiClientId) { "api://$apiClientId/access_as_user" } else { '' }
$grafanaOtlpUrl = if ($s.Contains('grafanaOtlpUrl'))  { $s['grafanaOtlpUrl']  } else { '' }
$grafanaHeaders = ''
if (-not [string]::IsNullOrWhiteSpace($s['grafanaInstanceId']) -and
    -not [string]::IsNullOrWhiteSpace($s['grafanaApiToken'])) {
    $b64 = [Convert]::ToBase64String(
               [Text.Encoding]::UTF8.GetBytes("$($s['grafanaInstanceId']):$($s['grafanaApiToken'])"))
    $grafanaHeaders = "Authorization=Basic $b64"
}

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
    if ($s['storageConnectionString'])           { $settings['Storage__ConnectionString'] = $s['storageConnectionString'] }
                                                   $settings['Storage__MediaContainer']   = 'media'
                                                   $settings['Storage__ContentContainer'] = 'content'
    if ($s['signupGateSecret'])                  { $settings['SignupGate__Secret']        = $s['signupGateSecret'] }
    if ($tenantId)                               { $settings['SignupGate__TenantId']      = $tenantId }
    if ($s['signupExtensionId'])                 { $settings['SignupGate__ExtensionId']   = $s['signupExtensionId'] }
                                                   $settings['Otel__ServiceName']         = 'slypn-api'
    if ($grafanaOtlpUrl)                          { $settings['Otel__Endpoint']           = $grafanaOtlpUrl }
    if ($grafanaHeaders)                          { $settings['Otel__Headers']            = $grafanaHeaders }

    $settingArgs = $settings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }
    az staticwebapp appsettings set `
        --name $swaName `
        --resource-group $rg `
        --setting-names @settingArgs | Out-Null

    Ok "Applied $($settings.Count) setting(s) to $swaName"

    # Otel__Env is baked into appsettings.json at CI time (prod for main, dev for PR
    # previews). Delete it from ALL environments so the baked value is never overridden.
    # The old CI workflow set it per-PR-environment; those stale overrides must be cleared.
    $allEnvNames = @('default') + @(
        az staticwebapp environment list --name $swaName --resource-group $rg `
            --query '[].name' -o json | ConvertFrom-Json |
            Where-Object { $_ -ne 'default' }
    )
    foreach ($envName in $allEnvNames) {
        $envArg = if ($envName -eq 'default') { @() } else { @('--environment-name', $envName) }
        az staticwebapp appsettings delete `
            --name $swaName `
            --resource-group $rg `
            @envArg `
            --setting-names Otel__Env 2>$null | Out-Null
    }
    Ok "Removed Otel__Env from all SWA environments ($($allEnvNames -join ', '))"
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

        # ── OIDC federation for GitHub Actions ───────────────────────────────
        Step 'Phase 4 · GitHub Actions OIDC federation'

        if ($s.Contains('subscriptionId')) { Switch-ToSubscription $s['subscriptionId'] }

        # Subscription's Entra tenant (separate from the CIAM tenant used for sign-in).
        $subscriptionTenantId = az account show --query tenantId -o tsv

        $cicdAppName = 'slypn-github-actions'
        $cicdApp = az ad app list --filter "displayName eq '$cicdAppName'" --query '[0]' -o json 2>$null |
                   ConvertFrom-Json
        if ($cicdApp) {
            $cicdAppId = $cicdApp.appId
            $cicdObjId = $cicdApp.id
            Info "Found  $cicdAppName  appId=$cicdAppId"
        } else {
            $cicdApp   = az ad app create --display-name $cicdAppName -o json | ConvertFrom-Json
            $cicdAppId = $cicdApp.appId
            $cicdObjId = $cicdApp.id
            Ok "Created  $cicdAppName  appId=$cicdAppId"
        }
        $s['cicdAppId']            = $cicdAppId
        $s['subscriptionTenantId'] = $subscriptionTenantId
        Save-Secrets $s

        $cicdSpList = az ad sp list --filter "appId eq '$cicdAppId'" -o json 2>$null | ConvertFrom-Json
        if (-not $cicdSpList -or $cicdSpList.Count -eq 0) {
            az ad sp create --id $cicdAppId | Out-Null
            Ok 'Created service principal'
        } else {
            Info 'Service principal exists'
        }

        $scope = "/subscriptions/$($s['subscriptionId'])/resourceGroups/$($s['resourceGroup'])"
        $roleAssigned = az role assignment list --assignee $cicdAppId --role Contributor --scope $scope -o json 2>$null |
                        ConvertFrom-Json
        if (-not $roleAssigned -or $roleAssigned.Count -eq 0) {
            az role assignment create --assignee $cicdAppId --role Contributor --scope $scope | Out-Null
            Ok "Contributor granted on $($s['resourceGroup'])"
        } else {
            Info 'Contributor already assigned'
        }

        foreach ($fc in @(
            @{ name = 'github-main'; subject = "repo:$gitHubRepo`:ref:refs/heads/main" }
            @{ name = 'github-prs';  subject = "repo:$gitHubRepo`:pull_request" }
        )) {
            $exists = az ad app federated-credential list --id $cicdObjId -o json 2>$null |
                      ConvertFrom-Json | Where-Object { $_.name -eq $fc.name }
            if (-not $exists) {
                $tmpJson = New-TemporaryFile
                @{
                    name      = $fc.name
                    issuer    = 'https://token.actions.githubusercontent.com'
                    subject   = $fc.subject
                    audiences = @('api://AzureADTokenExchange')
                } | ConvertTo-Json | Set-Content $tmpJson -Encoding UTF8
                az ad app federated-credential create --id $cicdObjId --parameters "@$($tmpJson.FullName)" | Out-Null
                Remove-Item $tmpJson -ErrorAction SilentlyContinue
                Ok "Federated credential: $($fc.name)"
            } else {
                Info "Federated credential exists: $($fc.name)"
            }
        }

        Set-GhSecret 'AZURE_CLIENT_ID'       $cicdAppId
        Set-GhSecret 'AZURE_TENANT_ID'       $subscriptionTenantId
        Set-GhSecret 'AZURE_SUBSCRIPTION_ID' $s['subscriptionId']

        # VITE_ build-time env vars consumed by azure-static-web-apps.yml.
        Set-GhSecret 'VITE_MSAL_AUTHORITY' $authority
        Set-GhSecret 'VITE_MSAL_CLIENT_ID' $spaClientId
        Set-GhSecret 'VITE_API_SCOPE'      $apiScopeStr
        if ($s.Contains('faroUrl'))               { Set-GhSecret 'VITE_FARO_URL'           $s['faroUrl'] }
        if ($s.Contains('faroSourcemapEndpoint')) { Set-GhSecret 'FARO_SOURCEMAP_ENDPOINT' $s['faroSourcemapEndpoint'] }
        if ($s.Contains('faroSourcemapAppId'))    { Set-GhSecret 'FARO_SOURCEMAP_APP_ID'   $s['faroSourcemapAppId'] }
        if ($s.Contains('faroSourcemapStackId'))  { Set-GhSecret 'FARO_SOURCEMAP_STACK_ID' $s['faroSourcemapStackId'] }
        if ($s.Contains('faroSourcemapApiKey'))   { Set-GhSecret 'FARO_SOURCEMAP_API_KEY'  $s['faroSourcemapApiKey'] }
        if ($grafanaOtlpUrl)                      { Set-GhSecret 'OTEL_ENDPOINT'            $grafanaOtlpUrl }
        if ($grafanaHeaders)                      { Set-GhSecret 'OTEL_HEADERS'             $grafanaHeaders }

        # CIAM CI credentials — PR preview redirect URI management.
        if ($s.Contains('tenantId'))        { Set-GhSecret 'CIAM_TENANT_ID'     $s['tenantId'] }
        if ($s.Contains('spaObjectId'))     { Set-GhSecret 'SPA_OBJECT_ID'      $s['spaObjectId'] }
        if ($s.Contains('ciClientId'))      { Set-GhSecret 'CIAM_CLIENT_ID'     $s['ciClientId'] }
        if ($s.Contains('ciClientSecret'))  { Set-GhSecret 'CIAM_CLIENT_SECRET' $s['ciClientSecret'] }
    }
}

# ── Phase 5 · Local dev configuration ─────────────────────────────────────────

# Default for the summary when Phase 5 is skipped; resolved from existing config below.
$localSkipAuth = 'true'

if (-not $SkipLocal -and $authority -and $spaClientId -and $apiScopeStr) {
    Step 'Phase 5 · Local dev configuration'

    # This phase configures the *capability* to use Entra locally (it writes the
    # MSAL client id / authority / scope). The on/off toggle itself
    # (VITE_DEV_SKIP_AUTH / AzureAd__SkipAuth) is owned by scripts/setupLocal.ps1
    # — so here we PRESERVE whatever the toggle is currently set to and never
    # stomp it. Fresh machines default to skip-auth on (dev persona switcher).
    $localSkipAuth = 'true'
    if (Test-Path $envLocalPath) {
        $flagLine = Get-Content $envLocalPath |
            Where-Object { $_ -match '^\s*VITE_DEV_SKIP_AUTH\s*=' } |
            Select-Object -Last 1
        if ($flagLine -match '=\s*(true|false)\s*$') { $localSkipAuth = $Matches[1] }
    }
    Info "Local Entra login toggle preserved: skip-auth=$localSkipAuth (change with scripts/setupLocal.ps1 -EntraLogin on|off)"

    # .env.local
    $envLines = @(
        '# Auto-generated by infra/setup.ps1 — do not commit.'
        "VITE_MSAL_AUTHORITY=$authority"
        "VITE_MSAL_CLIENT_ID=$spaClientId"
        "VITE_API_SCOPE=$apiScopeStr"
        "VITE_DEV_SKIP_AUTH=$localSkipAuth"
    )
    if ($s.Contains('faroUrl') -and -not [string]::IsNullOrWhiteSpace($s['faroUrl'])) {
        $envLines += "VITE_FARO_URL=$($s['faroUrl'])"
        $envLines += 'VITE_FARO_ENV=local'
    }

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
        $ls['Values']['AzureAd__SkipAuth']  = $localSkipAuth
        if ($graphSecret) { $ls['Values']['Graph__ClientSecret'] = $graphSecret }
        $ls['Values']['Graph__InviteRedirectUrl'] = 'http://localhost:5173/'
        if ($grafanaOtlpUrl) { $ls['Values']['Otel__Endpoint'] = $grafanaOtlpUrl }
        if ($grafanaHeaders) { $ls['Values']['Otel__Headers']  = $grafanaHeaders }

        $ls | ConvertTo-Json -Depth 5 | Set-Content $localSettingsPath -Encoding UTF8
        Ok "Updated $localSettingsPath (Entra/Graph fields; Storage unchanged)"
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
    Write-Host "   VITE_DEV_SKIP_AUTH=$localSkipAuth  (toggle: .\scripts\setupLocal.ps1 -EntraLogin on|off)"
}

if ($swaName -and $authority) {
    Write-Host ''
    Write-Host " SWA app settings applied to $swaName" -ForegroundColor Yellow
    if ($graphSecret -and $s.Contains('graphClientSecret')) {
        $s.Remove('graphClientSecret')
        Save-Secrets $s
        Ok '  graphClientSecret removed from secrets.json'
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
