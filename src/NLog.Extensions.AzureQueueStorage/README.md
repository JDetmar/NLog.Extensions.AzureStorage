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
          queueName="Layout"
          connectionString="String">
            <tag name="mytag" layout="mytagvalue" /> <!-- Multiple allowed -->
  </target>
</targets>
```

### Parameters

_name_ - Name of the target.

_layout_ - Queue Message Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_queueName_ - QueueName. [Layout](https://github.com/NLog/NLog/wiki/Layouts)  

_connectionString_ - Azure storage connection string. Ex. `UseDevelopmentStorage=true;`

_serviceUri_ - Alternative to ConnectionString.

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
