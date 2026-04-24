@description('Key Vault name (3–24 chars, alphanumeric + hyphens)')
param name string

@description('Azure region')
param location string

@description('ASCII-armored PGP public key to store as a secret')
@secure()
param pgpPublicKey string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    // RBAC-based access control — no vault access policies, granular role assignments instead.
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enabledForDeployment: false
    enabledForTemplateDeployment: false
    enabledForDiskEncryption: false
  }
}

resource pgpPublicKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'pgp-public-key'
  properties: {
    value: pgpPublicKey
  }
}

output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
