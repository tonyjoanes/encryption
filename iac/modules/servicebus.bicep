@description('Service Bus namespace name')
param namespaceName string

@description('Azure region')
param location string

@description('Queue name for file-uploaded events')
param queueName string = 'file-uploaded'

resource namespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    // Standard tier: required for queues with advanced features (DLQ, sessions, etc.)
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {}
}

resource queue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: namespace
  name: queueName
  properties: {
    // Retry up to 10 times before moving to DLQ.
    maxDeliveryCount: 10
    // Dead-letter queue enabled for messages that expire or exceed maxDeliveryCount.
    deadLetteringOnMessageExpiration: true
    // Messages live for 14 days if not consumed.
    defaultMessageTimeToLive: 'P14D'
    // Consumers have 5 minutes to process and complete a message before it reappears.
    lockDuration: 'PT5M'
    enablePartitioning: false
  }
}

output serviceBusNamespaceId string = namespace.id
output fullyQualifiedNamespace string = '${namespaceName}.servicebus.windows.net'
output queueName string = queue.name
