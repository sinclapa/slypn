// SLYPN infra — full resource-group deployment.
// Phase 6.1 — adds managed identity on SWA + role assignments to Storage
// (Blob + Table Data Contributor) so the API can drop the connection-string
// fallback in prod. Content persists in Table Storage (metadata) + Blob
// Storage (article/draft bodies + media) on a single storage account.
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

// Built-in role ids
var blobDataContributorRoleId  = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'                        // Storage Blob Data Contributor (Azure RBAC)
var tableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'                         // Storage Table Data Contributor (Azure RBAC)

// ---------------------------------------------------------------------------
// Storage account + media container
// ---------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: take('${prefixFlat}st${nameSuffix}', 24)
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  identity: {
    type: 'None' // Access is via the SWA's managed identity (RBAC role assignments below), not the storage account's own identity.
  }
  tags: tags
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true        // SWA / dev still uses connection strings; tightened to false post-rollout.
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
// Azure Static Web Apps (Standard SKU) — system-assigned managed identity
// for outbound calls to Cosmos + Blob without storing secrets.
// SWA is region-pinned to a small set; we use westeurope for UK proximity.
// ---------------------------------------------------------------------------
resource swa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa-${prefixDash}'
  location: swaLocation
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  identity: {
    type: 'SystemAssigned'
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
// Role assignments — let the SWA managed identity reach Blob + Table.
// ---------------------------------------------------------------------------

// Storage Blob Data Contributor at the storage account scope.
resource blobDataAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, swa.id, blobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: swa.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Storage Table Data Contributor at the storage account scope.
resource tableDataAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, swa.id, tableDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', tableDataContributorRoleId)
    principalId: swa.identity.principalId
    principalType: 'ServicePrincipal'
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
output swaPrincipalId      string = swa.identity.principalId
