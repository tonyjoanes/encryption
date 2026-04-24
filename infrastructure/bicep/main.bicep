targetScope = 'resourceGroup'

@description('Short environment name used as a suffix for all resources (e.g. dev, prod)')
param environmentName string

@description('Azure region for all resources. Defaults to resource group location.')
param location string = resourceGroup().location

@description('File type identifiers. A blob container named encrypted-{type} is created for each.')
param fileTypes array = ['members', 'addresses']

@description('ASCII-armored PGP public key. Marked @secure() so it is never logged in deployment history.')
@secure()
param pgpPublicKey string

// Resource name prefix — keep short to stay within Azure naming limits.
var prefix = 'enc${toLower(environmentName)}'

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    name: '${prefix}-ai'
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    // Storage account names: 3–24 chars, lowercase alphanumeric only, globally unique.
    name: '${replace(prefix, '-', '')}sa'
    location: location
    fileTypes: fileTypes
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    name: '${prefix}-kv'
    location: location
    pgpPublicKey: pgpPublicKey
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus'
  params: {
    namespaceName: '${prefix}-sb'
    location: location
    queueName: 'file-uploaded'
  }
}

module functionApp 'modules/functionapp.bicep' = {
  name: 'functionapp'
  params: {
    name: '${prefix}-func'
    location: location
    storageAccountName: storage.outputs.storageAccountName
    storageAccountId: storage.outputs.storageAccountId
    serviceBusFullyQualifiedNamespace: serviceBus.outputs.fullyQualifiedNamespace
    serviceBusNamespaceId: serviceBus.outputs.serviceBusNamespaceId
    serviceBusQueueName: serviceBus.outputs.queueName
    keyVaultUri: keyVault.outputs.keyVaultUri
    keyVaultId: keyVault.outputs.keyVaultId
    appInsightsConnectionString: monitoring.outputs.connectionString
  }
}

module eventGrid 'modules/eventgrid.bicep' = {
  name: 'eventgrid'
  // Event Grid subscription must be created after the Function App exists.
  dependsOn: [functionApp]
  params: {
    storageAccountName: storage.outputs.storageAccountName
    storageAccountId: storage.outputs.storageAccountId
    functionAppId: functionApp.outputs.functionAppId
    functionAppHostname: functionApp.outputs.functionAppHostname
  }
}

output functionAppHostname string = functionApp.outputs.functionAppHostname
output receiveEndpoint string = 'https://${functionApp.outputs.functionAppHostname}/api/receive'
output healthEndpoint string = 'https://${functionApp.outputs.functionAppHostname}/api/health'
