using './main.bicep'

param environmentName = 'dev'
param location = 'eastus'

// Add new file types here — a blob container is created automatically on next deployment.
param fileTypes = ['members', 'addresses']

// pgpPublicKey is @secure() and must NOT be stored here.
// Pass it at deploy time:
//
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file main.bicep \
//     --parameters main.bicepparam \
//     --parameters pgpPublicKey="$(cat public.asc)"
