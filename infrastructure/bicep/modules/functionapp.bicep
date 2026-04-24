@description('Function App name')
param name string

@description('Azure region')
param location string

@description('Storage account name (used for AzureWebJobsStorage and blob operations)')
param storageAccountName string

@description('Storage account resource ID (for role assignments)')
param storageAccountId string

@description('Service Bus namespace fully qualified hostname')
param serviceBusFullyQualifiedNamespace string

@description('Service Bus namespace resource ID (for role assignments)')
param serviceBusNamespaceId string

@description('Service Bus queue name')
param serviceBusQueueName string

@description('Key Vault URI')
param keyVaultUri string

@description('Key Vault resource ID (for role assignments)')
param keyVaultId string

@description('Application Insights connection string')
param appInsightsConnectionString string

// Consumption plan — scales to zero when idle, cost-efficient for event-driven workloads.
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${name}-plan'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  kind: 'functionapp'
  identity: {
    // System-assigned Managed Identity — used for all Azure resource access.
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          // Managed Identity connection for AzureWebJobsStorage (host lease, etc.)
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          // Managed Identity connection for blob trigger and BlobServiceClient.
          name: 'StorageConnection__serviceUri'
          value: 'https://${storageAccountName}.blob.core.windows.net'
        }
        {
          // Managed Identity connection for Service Bus.
          name: 'ServiceBusConnection__fullyQualifiedNamespace'
          value: serviceBusFullyQualifiedNamespace
        }
        {
          name: 'ServiceBusQueueName'
          value: serviceBusQueueName
        }
        {
          name: 'KeyVaultUri'
          value: keyVaultUri
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
      ]
    }
  }
}

// --- Role assignments via Managed Identity ---

// Storage Blob Data Contributor: read/write/delete blobs (incoming + encrypted containers)
resource storageBlobDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccountId, functionApp.id, 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    )
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Azure Service Bus Data Sender: send messages to queues/topics
resource serviceBusDataSenderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(serviceBusNamespaceId, functionApp.id, '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
    )
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Key Vault Secrets User: read secrets (PGP public key)
resource keyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVaultId, functionApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '4633458b-17de-408a-b874-0445c86b69e6'
    )
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output functionAppId string = functionApp.id
output functionAppHostname string = functionApp.properties.defaultHostName
output principalId string = functionApp.identity.principalId
