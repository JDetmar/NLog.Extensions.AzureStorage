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
          connectionStringKey="String"
          container="Layout" />
</targets>
```

### Parameters

_name_ - Name of the target.

_layout_ - Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_blobName_ - BlobName. [Layout](https://github.com/NLog/NLog/wiki/Layouts)  

_connectionString_ - Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_connectionStringKey_ - App key name of Azure storage connection string. Must provide either _connectionString_ or _connectionStringKey_.

_container_ - Azure blob container name. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_contentType_ - Azure blob ContentType (Default = text/plain)

### Batching Policy

_batchSize_ - Number of EventData items to send in a single batch (Default=100)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=200 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)
