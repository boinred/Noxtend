// Internal-test topology for Noxtend on Azure — the PaaS equivalent of deploy/k8s.
//
//   App Service B1 (Linux container)  <- apps/backend/Dockerfile
//   Azure SQL S0                      <- mssql.yaml
//   Azure Managed Redis B0            <- redis.yaml
//   Storage account (blob)            <- azurite.yaml
//   Container Registry Basic          <- image source
//   Static Web App Free               <- apps/frontend/dist
//
// The API ships without authentication (README "인증·외부 공개 배포 ⛔ 미지원"), so the
// site is closed to every address except allowedIpAddress. Removing that rule publishes
// an unauthenticated API to the internet.

@description('Region for every resource except the Static Web App, which has its own limited region list.')
param location string = resourceGroup().location

@description('Region for the Static Web App. koreacentral is not offered; eastasia is the nearest.')
param staticWebAppLocation string = 'eastasia'

@description('Prefix for generated resource names. Letters and digits only — it feeds the registry and storage account names.')
@minLength(3)
@maxLength(11)
param namePrefix string = 'noxtend'

@description('Container image tag pushed to the registry by deploy.sh.')
param imageTag string = 'latest'

@description('Only address allowed to reach the API and the SQL server. The API has no auth of its own.')
param allowedIpAddress string

param sqlAdminLogin string = 'noxtendadmin'

@secure()
@description('SQL admin password. 12+ chars with upper, lower, digit and symbol or the server refuses to create.')
param sqlAdminPassword string

// Global-scope names (registry, storage, SQL server) must be unique across Azure.
var suffix = uniqueString(resourceGroup().id)
var acrName = toLower('${namePrefix}acr${suffix}')
// Storage account names cap at 24 chars, so the prefix is trimmed to fit the 13-char suffix.
var storageName = toLower('${take(namePrefix, 9)}st${suffix}')
var sqlServerName = toLower('${namePrefix}-sql-${suffix}')
var sqlDbName = 'Noxtend'
var redisName = toLower('${namePrefix}-redis-${suffix}')
var planName = '${namePrefix}-plan'
var apiName = toLower('${namePrefix}-api-${suffix}')
var swaName = toLower('${namePrefix}-web-${suffix}')

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    // Pulls go through the API's managed identity, so no admin credentials exist to leak.
    adminUserEnabled: false
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

// Containers "source-images" and "meshes" are created on first write by
// AzureBlobStorage/AzureMeshArtifactStorage, so they are deliberately not declared here.

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'S0'
    tier: 'Standard'
    capacity: 10
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

// The 0.0.0.0 rule is the documented "allow Azure services" marker, not an open internet
// rule — it is what lets App Service reach the database without a VNet.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlAllowOperator 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowOperator'
  properties: {
    startIpAddress: allowedIpAddress
    endIpAddress: allowedIpAddress
  }
}

// Azure Managed Redis B0. For the retiring classic tier instead, swap this pair for
// Microsoft.Cache/redis@2024-11-01 with sku Basic/C0 and use port 6380.
resource redis 'Microsoft.Cache/redisEnterprise@2024-10-01' = {
  name: redisName
  location: location
  sku: {
    name: 'Balanced_B0'
  }
}

resource redisDb 'Microsoft.Cache/redisEnterprise/databases@2024-10-01' = {
  parent: redis
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: 10000
    // EnterpriseCluster exposes one endpoint, so StackExchange.Redis needs no cluster awareness.
    clusteringPolicy: 'EnterpriseCluster'
    // Queue entries are job state, not cache. Eviction under memory pressure would drop tasks.
    evictionPolicy: 'NoEviction'
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
  }
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2023-12-01' = {
  name: apiName
  location: location
  kind: 'app,linux,container'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOCKER|${acr.properties.loginServer}/noxtend-api:${imageTag}'
      acrUseManagedIdentityCreds: true
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health/live'
      // Platform CORS is required because app.UseCors runs only in Development.
      cors: {
        allowedOrigins: [
          'https://${swa.properties.defaultHostname}'
        ]
        supportCredentials: false
      }
      // Default action turns to Deny as soon as one Allow rule exists.
      ipSecurityRestrictions: [
        {
          name: 'operator'
          ipAddress: '${allowedIpAddress}/32'
          action: 'Allow'
          priority: 100
        }
      ]
      scmIpSecurityRestrictionsUseMain: true
      appSettings: [
        {
          name: 'WEBSITES_PORT'
          value: '8080'
        }
        {
          // Mounts /home as durable storage so the Data Protection ring survives restarts.
          // Without it every stored provider API key becomes undecryptable after a restart.
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'true'
        }
        {
          // Program.cs runs MigrateAsync before Kestrel binds; the default 230s start
          // budget is shorter than a cold migration plus seed (see k8s api.yaml note).
          name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
          value: '1800'
        }
        {
          name: 'DOCKER_REGISTRY_SERVER_URL'
          value: 'https://${acr.properties.loginServer}'
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'ASPNETCORE_URLS'
          value: 'http://+:8080'
        }
        {
          name: 'DataProtection__KeysPath'
          value: '/home/keys'
        }
        {
          name: 'Jobs__LeaseSeconds'
          value: '120'
        }
        {
          name: 'Jobs__LeaseRenewSeconds'
          value: '15'
        }
        {
          name: 'Jobs__SweepIntervalSeconds'
          value: '60'
        }
        {
          name: 'Jobs__MaxAttempts'
          value: '3'
        }
        {
          name: 'Llm__UseFake'
          value: 'false'
        }
        {
          name: 'ConnectionStrings__Db'
          value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDbName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
        }
        {
          name: 'ConnectionStrings__Blob'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
        }
        {
          name: 'ConnectionStrings__Redis'
          value: '${redis.properties.hostName}:10000,password=${redisDb.listKeys().primaryKey},ssl=True,abortConnect=False'
        }
      ]
    }
  }
  dependsOn: [
    sqlDb
  ]
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, api.id, acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource swa 'Microsoft.Web/staticSites@2023-12-01' = {
  name: swaName
  location: staticWebAppLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output acrName string = acr.name
output apiName string = api.name
output apiUrl string = 'https://${api.properties.defaultHostName}'
output staticWebAppName string = swa.name
output staticWebAppUrl string = 'https://${swa.properties.defaultHostname}'
