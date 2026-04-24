@description('Storage account name (max 24 chars, lowercase alphanumeric)')
param name string

@description('Azure region')
param location string

@description('File type names — a blob container named encrypted-{type} is created for each')
param fileTypes array

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

// Staging container for raw uploaded files (pre-encryption)
resource incomingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'incoming'
  properties: {
    publicAccess: 'None'
  }
}

// One encrypted output container per file type.
// @batchSize(1) prevents ARM API race conditions on parallel container creation.
@batchSize(1)
resource encryptedContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for fileType in fileTypes: {
    parent: blobService
    name: 'encrypted-${toLower(fileType)}'
    properties: {
      publicAccess: 'None'
    }
  }
]

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
