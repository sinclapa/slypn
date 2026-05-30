// Phase 1 skeleton — resources only, no managed identity / role assignments yet.
// Full IaC (managed identity on SWA → Cosmos Data Contributor + Blob Data Contributor,
// per-env parameter files, custom domain) lands in #38 (Phase 6).
//
// One-time RG bootstrap (not in this file; do manually for now):
//   az group create -n rg-slypn-dev -l uksouth
//
// Deploy when ready (Phase 6):
//   az deployment group create -g rg-slypn-dev -f infra/main.bicep -p env=dev

targetScope = 'resourceGroup'

@description('Short environment name, e.g. dev, prod. Drives resource naming.')
@minLength(2)
@maxLength(8)
param env string = 'dev'

@description('Azure region for Cosmos + Storage. SWA region is fixed (westeurope).')
param location string = resourceGroup().location

@description('Set to false if this subscription already has a free-tier Cosmos account (only one allowed).')
param enableCosmosFreeTier bool = true

@description('GitHub repo to associate with the Static Web App. Empty disables CI/CD wiring at deploy time.')
param repositoryUrl string = 'https://github.com/sinclapa/slypn'

@description('Branch to track for the Static Web App.')
param repositoryBranch string = 'main'

// Short prefixes derived from env. Storage account names cannot have hyphens.
var prefixDash = 'slypn-${env}'
var prefixFlat = 'slypn${env}'
var nameSuffix = uniqueString(resourceGroup().id)

// ---------------------------------------------------------------------------
// Storage account + media container
// ---------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: take('${prefixFlat}st${nameSuffix}', 24)
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
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

// ---------------------------------------------------------------------------
// Cosmos DB (free tier — 1000 RU/s + 25 GB free forever per subscription)
// Free tier and serverless are mutually exclusive; we want free tier.
// ---------------------------------------------------------------------------
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: 'cosmos-${prefixDash}-${nameSuffix}'
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: enableCosmosFreeTier
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
  }
}

// ---------------------------------------------------------------------------
// Azure Static Web Apps (Standard SKU — includes custom domain + auth)
// SWA is region-pinned to a small set; we use westeurope for UK proximity.
// ---------------------------------------------------------------------------
resource swa 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa-${prefixDash}'
  location: 'westeurope'
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
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
// Outputs — consumed by the deploy workflow in #41 (Phase 6).
// ---------------------------------------------------------------------------
output swaUrl string = 'https://${swa.properties.defaultHostname}'
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output storageAccountName string = storage.name
output mediaContainerName string = mediaContainer.name
