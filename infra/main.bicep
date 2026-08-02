// SLYPN infra — full resource-group deployment.
// SWA is on the Free plan — no managed identity available, so the API
// authenticates to Storage via connection string permanently (see
// Storage__ConnectionString in setup.ps1). Content persists in Table
// Storage (metadata) + Blob Storage (article/draft bodies + media) on a
// single storage account.
//
// We run a single production environment; PR previews are handled by the
// SWA action, not by a second resource group.
//
// One-time RG bootstrap (manual):
//   az group create -n rg-slypn-prod -l uksouth
//
// Deploy:
//   az deployment group create -g rg-slypn-prod -f infra/main.bicep -p @infra/main.parameters.prod.json

targetScope = 'resourceGroup'

@description('Short environment name, e.g. dev, prod. Drives resource naming.')
@minLength(2)
@maxLength(8)
param env string = 'dev'

@description('Azure region for Storage. SWA region is fixed (westeurope).')
param location string = resourceGroup().location

@description('Azure region for the Static Web App (SWA is only available in a limited set of regions).')
param swaLocation string = 'westeurope'

@description('GitHub repo to associate with the Static Web App.')
param repositoryUrl string = 'https://github.com/sinclapa/slypn'

@description('Branch to track for the Static Web App.')
param repositoryBranch string = 'main'

@description('Tags applied to every resource.')
param tags object = {
  app: 'slypn'
  env: env
  managedBy: 'bicep'
}

// Short prefixes derived from env. Storage account names cannot have hyphens.
var prefixDash = 'slypn-${env}'
var prefixFlat = 'slypn${env}'
var nameSuffix = uniqueString(resourceGroup().id)

// ---------------------------------------------------------------------------
// Storage account + media container
// ---------------------------------------------------------------------------
// S6378 (disabling managed identities): intentional — see the Free-tier SWA
// note in the file header. There's no identity to grant this account access
// to; connection-string auth is the permanent path.
//azureresourcemanager:S6378
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: take('${prefixFlat}st${nameSuffix}', 24)
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  identity: {
    type: 'None' // Free-tier SWA has no managed identity to grant — the storage account doesn't need its own either.
  }
  tags: tags
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true        // SWA / dev use connection strings permanently — Free-tier SWA has no managed identity to grant Data Contributor roles to.
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    encryption: {
      keySource: 'Microsoft.Storage'
      services: {
        blob: { enabled: true }
        table: { enabled: true }
      }
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

resource mediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'media'
  properties: {
    publicAccess: 'None'
  }
}

// Holds large article/draft HTML bodies (one blob per content id).
resource contentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'content'
  properties: {
    publicAccess: 'None'
  }
}

// Table service — the six content tables (articles, drafts, events, resources,
// newsletters, members) are created at runtime by TableBootstrapper.
resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

// ---------------------------------------------------------------------------
// Azure Static Web Apps (Free SKU). SWA is region-pinned to a small set;
// we use westeurope for UK proximity. Preview-environment quota (3
// concurrent) is enforced in the deploy workflow, not here.
// ---------------------------------------------------------------------------
// S6378 (disabling managed identities): intentional — the Free plan doesn't
// support managed identity at all, so there's no identity block to add here.
//azureresourcemanager:S6378
resource swa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa-${prefixDash}'
  location: swaLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  tags: tags
  properties: {
    repositoryUrl: repositoryUrl
    branch: repositoryBranch
    buildProperties: {
      appLocation: 'src/web'
      apiLocation: 'src/api/Slypn.Api'
      outputLocation: 'dist'
      appBuildCommand: 'npm run build'
    }
  }
}

// ---------------------------------------------------------------------------
// Outputs — consumed by the deploy workflow in #41.
// ---------------------------------------------------------------------------
output swaUrl              string = 'https://${swa.properties.defaultHostname}'
output swaName             string = swa.name
output storageAccountName  string = storage.name
output mediaContainerName  string = mediaContainer.name
output contentContainerName string = contentContainer.name
