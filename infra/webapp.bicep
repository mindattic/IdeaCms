// One IdeaCms web app: a Linux .NET 10 site with a system-assigned identity on the shared plan.
// main.bicep calls this once per deployment it hosts (the company site and the public demo) and owns
// every role assignment, so each identity's reach is visible in one place.

@description('Site name; becomes <name>.azurewebsites.net.')
param name string

param location string
param tags object
param planId string
param alwaysOn bool

@description('The complete app settings. siteConfig.appSettings is authoritative: anything set out-of-band is wiped on the next deployment.')
param appSettings array

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: planId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: alwaysOn
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      // Blazor Server holds a SignalR circuit per visitor; without affinity a reconnect can land on
      // another instance and drop the circuit.
      webSocketsEnabled: true
      healthCheckPath: '/_health'
      appSettings: appSettings
    }
  }
}

output name string = site.name
output hostName string = site.properties.defaultHostName
output principalId string = site.identity.principalId
