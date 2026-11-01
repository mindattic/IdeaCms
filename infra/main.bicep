// IdeaCms — Azure infrastructure. Two deployments of the same CMS on one App Service plan:
//
//   mindattic             the company site (MindAttic's own content, on Ideas)
//   mindattic-ideas-demo  a public, vanilla demo of Ideas that the demo-reset workflow wipes hourly
//
// They share the plan, the SQL server and the storage account, and NOTHING ELSE: separate databases,
// separate Key Vaults, separate blob containers, separate identities. The demo cannot read or write
// anything of the company site's. The only cross-reach is deliberate and one-way: the company site may
// READ one demo-vault secret (`credentials`, the current demo login it advertises), which only CI writes.
//
// Everything is passwordless (HOUSE-LAW-3): managed identities reach SQL, Blob Storage and Key Vault.
// Deploy with infra/provision.ps1, which runs this and then does what Bicep cannot: seed the secrets,
// create the SQL contained users, and make the demo's first database.

targetScope = 'resourceGroup'

@description('Name base for the SHARED resources (plan, SQL server, storage, company vault). Kept at its original value because every one of those names derives from it — changing it would create a second estate.')
@minLength(3)
@maxLength(40)
param appName string = 'mindattic-ideas'

@description('The company site: <siteAppName>.azurewebsites.net.')
param siteAppName string = 'mindattic'

@description('The public demo: <demoAppName>.azurewebsites.net.')
param demoAppName string = 'mindattic-ideas-demo'

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('App Service plan SKU. B1 is the cheapest tier with Always On, which a CMS needs so the first request after idle is not a cold start.')
@allowed(['B1', 'B2', 'S1', 'P0v3'])
param appServicePlanSku string = 'B1'

@description('Object ID of the Entra principal that administers SQL (you). Get it with: az ad signed-in-user show --query id -o tsv')
param sqlAdminObjectId string

@description('UPN or display name of that same principal — shown in the portal as the SQL admin.')
param sqlAdminLogin string

@description('Object ID of the CI service principal (GitHub OIDC). It writes the demo login and empties the demo media.')
param ciPrincipalObjectId string

@description('SQL database SKU for every database. Basic is 2GB and about five dollars a month.')
param sqlDatabaseSku string = 'Basic'

@description('Cloudflare Turnstile site key (public) for the demo-login reveal. Empty keeps the reveal off.')
param turnstileSiteKey string = ''

@description('Tag applied to every resource so the whole estate can be found and costed together.')
param projectTag string = 'IdeaCms'

@description('Placeholder for the demo admin password until the first reset replaces it. Never pass one in.')
@secure()
param demoPasswordPlaceholder string = newGuid()

var suffix = uniqueString(resourceGroup().id)
// Storage account names allow no hyphens and cap at 24 characters, so the name base is squashed and
// clipped before the uniqueness suffix is appended.
var nameBase = toLower(replace(appName, '-', ''))
var shortBase = substring(nameBase, 0, min(length(nameBase), 11))
var storageName = '${shortBase}${suffix}'
var keyVaultName = 'kv-${shortBase}-${substring(suffix, 0, 6)}'
var demoKeyVaultName = 'kv-demo-${substring(suffix, 0, 6)}'
var sqlServerName = 'sql-${shortBase}-${substring(suffix, 0, 6)}'
var sqlDatabaseName = 'MindAtticIdeas'
// The demo's live database is NOT declared here: the reset workflow replaces it every hour with a copy
// of this template (schema + contained users, no content). Declaring it would let a template deploy
// fight the reset.
var demoTemplateDatabaseName = 'MindAtticIdeasDemoTemplate'
var demoDatabaseName = 'MindAtticIdeasDemo'
var mediaContainerName = 'media'
var dataProtectionContainerName = 'dp-keys'
var demoMediaContainerName = 'demo-media'
var demoDataProtectionContainerName = 'demo-dp-keys'
var dataProtectionKeyName = 'dp-protect'

var commonTags = {
  project: projectTag
  managedBy: 'infra/main.bicep'
}

// Built-in role definition IDs. These GUIDs are stable across every Azure tenant.
var roleStorageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var roleKeyVaultCryptoUser = '12338af0-0e69-4776-bea7-57ae8d297424'
var roleKeyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var roleKeyVaultCryptoOfficer = '14b46e9e-c2b7-41b4-b07b-48a6ebf60603'
var roleKeyVaultSecretsOfficer = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

func kvRef(vaultUri string, secret string) string => '@Microsoft.KeyVault(SecretUri=${vaultUri}secrets/${secret})'

// ---------------------------------------------------------------------------------------------
// Storage — one account, separate containers per deployment.
// ---------------------------------------------------------------------------------------------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: commonTags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // No anonymous access: media is served through a short-lived SAS minted by the app (M.IDEA-§4.11).
    allowBlobPublicAccess: false
    // Shared keys off — every caller authenticates with an Entra identity; a SAS is user-delegation signed.
    allowSharedKeyAccess: false
    publicNetworkAccess: 'Enabled'
  }
}

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    cors: {
      corsRules: []
    }
  }
}

resource mediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: mediaContainerName
  properties: { publicAccess: 'None' }
}

resource dataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: dataProtectionContainerName
  properties: { publicAccess: 'None' }
}

resource demoMediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: demoMediaContainerName
  properties: { publicAccess: 'None' }
}

resource demoDataProtectionContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobServices
  name: demoDataProtectionContainerName
  properties: { publicAccess: 'None' }
}

// ---------------------------------------------------------------------------------------------
// Key Vaults — the company site's, and the demo's own.
// ---------------------------------------------------------------------------------------------

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: commonTags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    // Purge protection is deliberately ON: the Data Protection KEK lives here, and losing it
    // invalidates every issued auth cookie and every protected payload at once.
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

// The demo's vault: its own pepper, its own key-ring KEK, its hourly admin password, and the
// `credentials` the company site displays. Separate so a demo identity has nothing of the company's in
// reach, and so the company site's grant on the demo is ONE secret, not a vault.
resource demoKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: demoKeyVaultName
  location: location
  tags: commonTags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// The deploying principal needs Crypto Officer to create the keys below, and Secrets Officer so
// provision.ps1 can seed the secrets afterwards — on both vaults.
resource deployerCryptoOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, sqlAdminObjectId, roleKeyVaultCryptoOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultCryptoOfficer)
    principalId: sqlAdminObjectId
    principalType: 'User'
  }
}

resource deployerSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, sqlAdminObjectId, roleKeyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsOfficer)
    principalId: sqlAdminObjectId
    principalType: 'User'
  }
}

resource deployerDemoCryptoOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoKeyVault
  name: guid(demoKeyVault.id, sqlAdminObjectId, roleKeyVaultCryptoOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultCryptoOfficer)
    principalId: sqlAdminObjectId
    principalType: 'User'
  }
}

resource deployerDemoSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoKeyVault
  name: guid(demoKeyVault.id, sqlAdminObjectId, roleKeyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsOfficer)
    principalId: sqlAdminObjectId
    principalType: 'User'
  }
}

resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: dataProtectionKeyName
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: ['wrapKey', 'unwrapKey']
  }
  dependsOn: [deployerCryptoOfficer]
}

resource demoDataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: demoKeyVault
  name: dataProtectionKeyName
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: ['wrapKey', 'unwrapKey']
  }
  dependsOn: [deployerDemoCryptoOfficer]
}

// The two demo secrets other principals are granted on by NAME. They must exist for a secret-scoped
// role assignment, so they are declared with placeholder values; the reset workflow overwrites both
// within the hour. (`existing` cannot be used: a first deployment would fail.)
resource demoAdminPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: demoKeyVault
  name: 'admin-password'
  properties: {
    // Unguessable until the reset replaces it; never shown anywhere. A template deployment therefore
    // rotates it too, which is why provision.ps1 runs a demo reset right after deploying.
    value: 'Pending-${demoPasswordPlaceholder}'
  }
}

resource demoCredentialsSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: demoKeyVault
  name: 'credentials'
  properties: {
    value: '{"status":"resetting"}'
  }
}

// ---------------------------------------------------------------------------------------------
// SQL — Entra-only authentication, so there is no server password to store or rotate.
// ---------------------------------------------------------------------------------------------

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: commonTags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlAdminLogin
      sid: sqlAdminObjectId
      tenantId: subscription().tenantId
      principalType: 'User'
      azureADOnlyAuthentication: true
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  tags: commonTags
  sku: { name: sqlDatabaseSku }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
  }
}

// The demo's pristine template: the CI migrate job keeps its schema current, provision.ps1 puts the
// demo identity's contained user in it, and it is never written to by anything else.
resource demoTemplateDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: demoTemplateDatabaseName
  location: location
  tags: commonTags
  sku: { name: sqlDatabaseSku }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
  }
}

// App Service outbound IPs are not fixed, so the apps reach SQL through the Azure-services bypass
// rather than an IP allow-list. The 0.0.0.0 start/end pair is the documented sentinel for it.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---------------------------------------------------------------------------------------------
// App Service — one plan, two sites.
// ---------------------------------------------------------------------------------------------

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${appName}'
  location: location
  tags: commonTags
  sku: { name: appServicePlanSku }
  kind: 'linux'
  properties: { reserved: true }
}

var commonSettings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  {
    // First boot discovers every citizen, seeds the CMS and installs every bundled .idea through the
    // real install path -- against a 5-DTU Basic database that is minutes of work, and the default
    // 230s start limit kills the container mid-seed.
    name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
    value: '1800'
  }
  { name: 'Media__Provider', value: 'azure' }
  { name: 'Media__Azure__BlobServiceUri', value: storage.properties.primaryEndpoints.blob }
  { name: 'Media__Azure__SignedUrlMinutes', value: '60' }
]

func connectionString(fqdn string, db string) string =>
  'Server=tcp:${fqdn},1433;Initial Catalog=${db};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'

// Every app-setting NAME is alphanumeric: App Service on Linux strips hyphens and rewrites dots in
// setting names (M.IDEA-§4.14) and now rejects hyphenated names outright. MindAttic.Authentication and
// VaultPackageSigningTrust match them back to the dotted/hyphenated config keys.
var siteSettings = concat(commonSettings, [
  { name: 'ConnectionStrings__IdeaCms', value: connectionString(sqlServer.properties.fullyQualifiedDomainName, sqlDatabaseName) }
  { name: 'DataProtection__BlobUri', value: '${storage.properties.primaryEndpoints.blob}${dataProtectionContainerName}/ideas-keys.xml' }
  { name: 'DataProtection__KeyVaultKeyId', value: dataProtectionKey.properties.keyUriWithVersion }
  { name: 'Media__Azure__ContainerName', value: mediaContainerName }
  { name: 'MindAttic__Vault__Security__pepperv1', value: kvRef(keyVault.properties.vaultUri, 'pepper-v1') }
  { name: 'MindAttic__Vault__Security__bootstraptoken', value: kvRef(keyVault.properties.vaultUri, 'bootstrap-token') }
  { name: 'MindAttic__Vault__Security__resettokenkey', value: kvRef(keyVault.properties.vaultUri, 'reset-token-key') }
  { name: 'MindAttic__Vault__Security__dpkek', value: kvRef(keyVault.properties.vaultUri, 'dp-kek') }
  // The ONE trusted package-signing certificate (public half only, M.IDEA-§4.8).
  { name: 'MindAttic__Vault__PackageSigning__signingcertpublic', value: kvRef(keyVault.properties.vaultUri, 'signing-cert-public') }
  // The demo this site advertises. Read with SecretClient (not a reference): it rotates hourly.
  { name: 'Demo__Url', value: 'https://${demoAppName}.azurewebsites.net' }
  // Absolute origin of emailed password-reset links (never derived from Request.Host).
  { name: 'MindAttic__Auth__Reset__PublicBaseUrl', value: 'https://${siteAppName}.azurewebsites.net' }
  { name: 'Demo__KeyVaultUri', value: demoKeyVault.properties.vaultUri }
  { name: 'Demo__CredentialsSecretName', value: demoCredentialsSecret.name }
], empty(turnstileSiteKey) ? [] : [
  { name: 'Demo__TurnstileSiteKey', value: turnstileSiteKey }
  { name: 'Demo__TurnstileSecretKey', value: kvRef(keyVault.properties.vaultUri, 'turnstile-secret') }
])

var demoSettings = concat(commonSettings, [
  { name: 'ConnectionStrings__IdeaCms', value: connectionString(sqlServer.properties.fullyQualifiedDomainName, demoDatabaseName) }
  { name: 'DataProtection__BlobUri', value: '${storage.properties.primaryEndpoints.blob}${demoDataProtectionContainerName}/demo-keys.xml' }
  { name: 'DataProtection__KeyVaultKeyId', value: demoDataProtectionKey.properties.keyUriWithVersion }
  { name: 'Media__Azure__ContainerName', value: demoMediaContainerName }
  // A vanilla install provisioned from its idealist: every first-party package + one hello page.
  { name: 'IdeaCms__Idealist', value: 'seed/demo.idealist' }
  // The shared admin login rotates hourly from outside; a forced change would let the first visitor
  // lock everyone else out. Sessions die within seconds of a reset and can never outlive an hour.
  { name: 'MindAttic__Auth__Bootstrap__RequirePasswordChange', value: 'false' }
  { name: 'MindAttic__Auth__Session__RevalidationInterval', value: '00:00:15' }
  { name: 'MindAttic__Auth__Session__AbsoluteTimeout', value: '01:00:00' }
  { name: 'MindAttic__Auth__Reset__PublicBaseUrl', value: 'https://${demoAppName}.azurewebsites.net' }
  { name: 'MindAttic__Vault__Security__pepperv1', value: kvRef(demoKeyVault.properties.vaultUri, 'pepper-v1') }
  { name: 'MindAttic__Vault__Security__bootstraptoken', value: kvRef(demoKeyVault.properties.vaultUri, demoAdminPasswordSecret.name) }
  { name: 'MindAttic__Vault__Security__resettokenkey', value: kvRef(demoKeyVault.properties.vaultUri, 'reset-token-key') }
  { name: 'MindAttic__Vault__Security__dpkek', value: kvRef(demoKeyVault.properties.vaultUri, 'dp-kek') }
  { name: 'MindAttic__Vault__PackageSigning__signingcertpublic', value: kvRef(demoKeyVault.properties.vaultUri, 'signing-cert-public') }
])

module site 'webapp.bicep' = {
  name: 'site-${siteAppName}'
  params: {
    name: siteAppName
    location: location
    tags: commonTags
    planId: appServicePlan.id
    alwaysOn: true
    appSettings: siteSettings
  }
}

module demo 'webapp.bicep' = {
  name: 'site-${demoAppName}'
  params: {
    name: demoAppName
    location: location
    tags: commonTags
    planId: appServicePlan.id
    alwaysOn: true
    appSettings: demoSettings
  }
}

// ---------------------------------------------------------------------------------------------
// Role assignments. Named from the site NAME (known before deployment), so they are stable.
// ---------------------------------------------------------------------------------------------

// Company site: its own storage containers, its own vault.
resource siteStorage 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, siteAppName, roleStorageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDataContributor)
    principalId: site.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource siteKeyVaultCrypto 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, siteAppName, roleKeyVaultCryptoUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultCryptoUser)
    principalId: site.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource siteKeyVaultSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, siteAppName, roleKeyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
    principalId: site.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// The ONE thing the company site may see of the demo: the published login, read-only.
resource siteReadsDemoCredentials 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoCredentialsSecret
  name: guid(demoCredentialsSecret.id, siteAppName, roleKeyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
    principalId: site.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// Demo: its two containers and its vault — nothing of the company site's.
resource demoMediaAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoMediaContainer
  name: guid(demoMediaContainer.id, demoAppName, roleStorageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDataContributor)
    principalId: demo.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource demoKeyRingAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoDataProtectionContainer
  name: guid(demoDataProtectionContainer.id, demoAppName, roleStorageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDataContributor)
    principalId: demo.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource demoKeyVaultCrypto 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoKeyVault
  name: guid(demoKeyVault.id, demoAppName, roleKeyVaultCryptoUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultCryptoUser)
    principalId: demo.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource demoKeyVaultSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoKeyVault
  name: guid(demoKeyVault.id, demoAppName, roleKeyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
    principalId: demo.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// CI (the reset workflow): writes the demo's two rotating secrets and empties its media. It already
// holds Contributor on the resource group for the database swap and the restart.
resource ciWritesDemoPassword 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoAdminPasswordSecret
  name: guid(demoAdminPasswordSecret.id, ciPrincipalObjectId, roleKeyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsOfficer)
    principalId: ciPrincipalObjectId
    principalType: 'ServicePrincipal'
  }
}

resource ciWritesDemoCredentials 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoCredentialsSecret
  name: guid(demoCredentialsSecret.id, ciPrincipalObjectId, roleKeyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsOfficer)
    principalId: ciPrincipalObjectId
    principalType: 'ServicePrincipal'
  }
}

resource ciEmptiesDemoMedia 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: demoMediaContainer
  name: guid(demoMediaContainer.id, ciPrincipalObjectId, roleStorageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobDataContributor)
    principalId: ciPrincipalObjectId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------------------------

output webAppName string = site.outputs.name
output webAppHostName string = site.outputs.hostName
output webAppPrincipalId string = site.outputs.principalId
output demoAppName string = demo.outputs.name
output demoAppHostName string = demo.outputs.hostName
output demoAppPrincipalId string = demo.outputs.principalId
output storageAccountName string = storage.name
output blobServiceUri string = storage.properties.primaryEndpoints.blob
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output demoKeyVaultName string = demoKeyVault.name
output demoKeyVaultUri string = demoKeyVault.properties.vaultUri
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabaseName
output demoTemplateDatabaseName string = demoTemplateDatabaseName
output demoDatabaseName string = demoDatabaseName
