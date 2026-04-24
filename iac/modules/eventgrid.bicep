@description('Storage account name to subscribe to blob-created events')
param storageAccountName string

@description('Storage account resource ID')
param storageAccountId string

@description('Function App resource ID (event delivery target)')
param functionAppId string

@description('Function App default hostname')
param functionAppHostname string

// Event Grid system topic scoped to the storage account.
// Routes BlobCreated events from the incoming/ container to the encrypt function.
resource systemTopic 'Microsoft.EventGrid/systemTopics@2023-12-15-preview' = {
  name: '${storageAccountName}-topic'
  location: resourceGroup().location
  properties: {
    source: storageAccountId
    topicType: 'Microsoft.Storage.StorageAccounts'
  }
}

resource eventSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2023-12-15-preview' = {
  parent: systemTopic
  name: 'incoming-blob-created'
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: '${functionAppId}/functions/FileEncrypt'
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      // Only trigger on blobs created in the incoming/ container.
      subjectBeginsWith: '/blobServices/default/containers/incoming/'
      includedEventTypes: [
        'Microsoft.Storage.BlobCreated'
      ]
    }
    eventDeliverySchema: 'EventGridSchema'
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
  }
}
