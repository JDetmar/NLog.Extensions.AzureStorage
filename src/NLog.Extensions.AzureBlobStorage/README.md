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
          blobName="Layout"
          connectionString="String"
          container="Layout">
            <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
            <tag name="mytag" layout="mytagvalue" /> <!-- Multiple allowed (Requires v2 storage accounts) -->
  </target>
</targets>
```

### Parameters

_name_ - Name of the target.

_layout_ - Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_blobName_ - BlobName. [Layout](https://github.com/NLog/NLog/wiki/Layouts)  

_container_ - Azure blob container name. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_contentType_ - Azure blob ContentType (Default = text/plain)

_connectionString_ - Azure storage connection string. Ex. `UseDevelopmentStorage=true;`

_serviceUri_ - Alternative to ConnectionString, where Managed Identiy is applied from AzureServiceTokenProvider.

_tenantIdentity_ - Alternative to ConnectionString. Used together with ServiceUri. Input for AzureServiceTokenProvider.

_resourceIdentity_ - Alternative to ConnectionString. Used together with ServiceUri. Input for AzureServiceTokenProvider.

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