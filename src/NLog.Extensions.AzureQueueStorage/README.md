# Azure QueueStorage

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureQueueStorage** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureQueueStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureQueueStorage/) | Azure Queue Storage |

## Queue Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureQueueStorage" /> 
</extensions>

<targets>
  <target xsi:type="AzureQueueStorage"
          name="String"
          layout="Layout"
          connectionString="Layout"
          queueName="Layout">
            <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
  </target>
</targets>
```

### General Options

_name_ - Name of the target.

_layout_ - Queue Message Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_queueName_ - QueueName. [Layout](https://github.com/NLog/NLog/wiki/Layouts)  

_connectionString_ - Azure Queue Storage connection string from your storage account. Required unless using `serviceUri`.

_serviceUri_ - Alternative to ConnectionString, where Managed Identiy is acquired from DefaultAzureCredential.

_timeToLiveSeconds_ - Default Time-To-Live (TTL) for Queue messages in seconds (Optional)

_timeToLiveDays_ - Default Time-To-Live (TTL) for Queue messages in days (Optional)

### Authentication Options

_managedIdentityClientId_ - Sets `ManagedIdentityClientId` on `DefaultAzureCredentialOptions`. Requires `serviceUri`

_managedIdentityResourceId_ - resourceId for `ManagedIdentityResourceId` on `DefaultAzureCredentialOptions`, do not use together with `ManagedIdentityClientId`. Requires `serviceUri`.

_tenantIdentity_ - tenantId for `DefaultAzureCredentialOptions`. Requires `serviceUri`.

_sharedAccessSignature_ - Access signature for `AzureSasCredential` authentication. Requires `serviceUri`.

_accountName_ - accountName for `StorageSharedKeyCredential` authentication. Requires `serviceUri` and `accessKey`.

_accessKey_ - accountKey for `StorageSharedKeyCredential` authentication. Requires `serviceUri` and `accountName`.

_clientAuthId_ - clientId for `ClientSecretCredential` authentication. Requires `serviceUri`, `tenantIdentity` and `clientAuthSecret`.

_clientAuthSecret_ - clientSecret for `ClientSecretCredential` authentication. Requires `serviceUri`,`tenantIdentity` and `clientAuthId`.

### Proxy Options

_noProxy_ - Bypasses any system proxy and proxy in `ProxyAddress` when set to `true`.

_proxyAddress_ - Address of the proxy server to use (e.g. http://proxyserver:8080).

_proxyLogin_ - Login to use for the proxy server. Requires `proxyPassword`.

_proxyPassword_ - Password to use for the proxy server. Requires `proxyLogin`.

_useDefaultCredentialsForProxy_ - Uses the default credentials (`System.Net.CredentialCache.DefaultCredentials`) for the proxy server.

### Batching Policy

_batchSize_ - Number of EventData items to send in a single batch (Default=100)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=200 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)

## Azure Identity Environment
When using `ServiceUri` (Instead of ConnectionString), then `DefaultAzureCredential` is used for Azure Identity which supports environment variables:
- `AZURE_CLIENT_ID` - For ManagedIdentityClientId / WorkloadIdentityClientId
- `AZURE_TENANT_ID` - For TenantId

See also: [Set up Your Environment for Authentication](https://github.com/Azure/azure-sdk-for-go/wiki/Set-up-Your-Environment-for-Authentication)

## Azure ConnectionString

NLog Layout makes it possible to retrieve settings from [many locations](https://nlog-project.org/config/?tab=layout-renderers).

#### Lookup ConnectionString from appsettings.json

  > `connectionString="${configsetting:ConnectionStrings.AzureQueue}"`

* Example appsettings.json on .NetCore:

```json
  {
    "ConnectionStrings": {
      "AzureQueue": "UseDevelopmentStorage=true;"
    }
  }
```

#### Lookup ConnectionString from app.config

  > `connectionString="${appsetting:ConnectionStrings.AzureQueue}"`

* Example app.config on .NetFramework:

```xml
  <configuration>
    <connectionStrings>
      <add name="AzureQueue" connectionString="UseDevelopmentStorage=true;"/>
    </connectionStrings>
  </configuration>
```

#### Lookup ConnectionString from environment-variable

  > `connectionString="${environment:AZURE_STORAGE_CONNECTION_STRING}"`

#### Lookup ConnectionString from NLog GlobalDiagnosticsContext (GDC)

  > `connectionString="${gdc:AzureQueueConnectionString}"`

* Example code for setting GDC-value:

```c#
  NLog.GlobalDiagnosticsContext.Set("AzureQueueConnectionString", "UseDevelopmentStorage=true;");
```