# Azure BlobStorage

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureBlobStorage**  | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureBlobStorage.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureBlobStorage/) | Azure Blob Storage |

## Blob Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureBlobStorage" /> 
</extensions>

<targets>
  <target xsi:type="AzureBlobStorage"
          name="String"
          layout="Layout"
          connectionString="Layout"
          blobName="Layout"
          container="Layout">
            <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
            <tag name="mytag" layout="mytagvalue" /> <!-- Multiple allowed (Requires v2 storage accounts) -->
  </target>
</targets>
```

### Parameters

_name_ - Name of the target.

_layout_ - Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_blobName_ - BlobName. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_container_ - Azure blob container name. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required.

_contentType_ - Azure blob ContentType (Default = text/plain)

_connectionString_ - Azure storage connection string. Ex. `UseDevelopmentStorage=true;`

_serviceUri_ - Uri to reference the blob service (e.g. https://{account_name}.blob.core.windows.net). Input for `BlobServiceClient`. Required, when `connectionString` is not configured. Overrides `connectionString` when both are set.

_managedIdentityClientId_ - Sets `ManagedIdentityClientId` on `DefaultAzureCredentialOptions`. Requires `serviceUri`.

_managedIdentityResourceId_ - resourceId for `ManagedIdentityResourceId` on `DefaultAzureCredentialOptions`, do not use together with `ManagedIdentityClientId`. Requires `serviceUri`.

_tenantIdentity_ - tenantId for `DefaultAzureCredentialOptions` and `ClientSecretCredential`. Requires `serviceUri`.

_sharedAccessSignature_ - Access signature for `AzureSasCredential` authentication. Requires `serviceUri`.

_accountName_ - accountName for `StorageSharedKeyCredential` authentication. Requires `serviceUri` and `accessKey`.

_accessKey_ - accountKey for `StorageSharedKeyCredential` authentication. Requires `serviceUri` and `accountName`.

_clientAuthId_ - clientId for `ClientSecretCredential` authentication. Requires `serviceUri`, `tenantIdentity` and `clientAuthSecret`.

_clientAuthSecret_ - clientSecret for `ClientSecretCredential` authentication. Requires `serviceUri`,`tenantIdentity` and `clientAuthId`.

_useProxy_ - Enables connection through a proxy server. If no `proxyAddress` has been defined, the default proxy of the system will be used.

_proxyAddress_ - Address of the proxy server to use (e.g. http://proxyserver:8080). Requires `useProxy` set to true.

_proxyLogin_ - Login to use for the proxy server. Requires `proxyPassword` and `useProxy`.

_proxyPassword_ - Password to use for the proxy server. Requires `proxyLogin` and `useProxy`.

_useDefaultCredentialsForProxy_ - Uses the default credentials for the proxy server, overriding any values that may have been set in `proxyLogin` and `proxyPassword`. Requires `useProxy`.

### Batching Policy

_batchSize_ - Number of EventData items to send in a single batch (Default=100)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=200 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)

## Azure Blob Storage Emulator
The AzureBlobStorage-target uses Append blob operations, which is [not support by Azure Storage Emulator](https://docs.microsoft.com/en-us/azure/storage/common/storage-use-emulator#differences-for-blob-storage) from Microsoft.

It will fail with the following error:
```
Azure.RequestFailedException: This feature is not currently supported by the Storage Emulator
```

Instead one can try an alternative Azure Storage Emulator like [Azurite](https://github.com/azure/azurite)

## Azure Identity Environment
When using `ServiceUri` (Instead of ConnectionString), then `DefaultAzureCredential` is used for Azure Identity which supports environment variables:
- `AZURE_CLIENT_ID` - For ManagedIdentityClientId / WorkloadIdentityClientId
- `AZURE_TENANT_ID` - For TenantId

See also: [Set up Your Environment for Authentication](https://github.com/Azure/azure-sdk-for-go/wiki/Set-up-Your-Environment-for-Authentication)

## Azure ConnectionString

NLog Layout makes it possible to retrieve settings from [many locations](https://nlog-project.org/config/?tab=layout-renderers).

#### Lookup ConnectionString from appsettings.json
  > `connectionString="${configsetting:ConnectionStrings.AzureBlob}"`

* Example appsettings.json on .NetCore:

```json
  {
    "ConnectionStrings": {
      "AzureBlob": "UseDevelopmentStorage=true;"
    }
  }
```

#### Lookup ConnectionString from app.config

  > `connectionString="${appsetting:ConnectionStrings.AzureBlob}"`

* Example app.config on .NetFramework:

```xml
  <configuration>
    <connectionStrings>
      <add name="AzureBlob" connectionString="UseDevelopmentStorage=true;"/>
    </connectionStrings>
  </configuration>
```

#### Lookup ConnectionString from environment-variable

  > `connectionString="${environment:AZURE_STORAGE_CONNECTION_STRING}"`

#### Lookup ConnectionString from NLog GlobalDiagnosticsContext (GDC)

  > `connectionString="${gdc:AzureBlobConnectionString}"`

* Example code for setting GDC-value:

```c#
  NLog.GlobalDiagnosticsContext.Set("AzureBlobConnectionString", "UseDevelopmentStorage=true;");
```