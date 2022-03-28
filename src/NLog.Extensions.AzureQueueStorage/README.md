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
          connectionString="Layout">
            <metadata name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
  </target>
</targets>
```

### Parameters

_name_ - Name of the target.

_layout_ - Queue Message Text to be rendered. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_queueName_ - QueueName. [Layout](https://github.com/NLog/NLog/wiki/Layouts)  

_connectionString_ - Azure storage connection string. Ex. `UseDevelopmentStorage=true;`

_serviceUri_ - Alternative to ConnectionString, where Managed Identiy is acquired from AzureServiceTokenProvider for User delegation SAS.

_tenantIdentity_ - Alternative to ConnectionString. Used together with ServiceUri. Input for AzureServiceTokenProvider.

_resourceIdentity_ - Alternative to ConnectionString. Used together with ServiceUri. Input for AzureServiceTokenProvider.

_clientIdentity_ - Alternative to ConnectionString. Used together with ServiceUri. Input for AzureServiceTokenProvider (`RunAs=App;AppId={ClientIdentity}`)

_timeToLiveSeconds_ - Default Time-To-Live (TTL) for Queue messages in seconds (Optional)

_timeToLiveDays_ - Default Time-To-Live (TTL) for Queue messages in days (Optional)

### Batching Policy

_batchSize_ - Number of EventData items to send in a single batch (Default=100)

_taskDelayMilliseconds_ - Artificial delay before sending to optimize for batching (Default=200 ms)

_queueLimit_ - Number of pending LogEvents to have in memory queue, that are waiting to be sent (Default=10000)

_overflowAction_ - Action to take when reaching limit of in memory queue (Default=Discard)

### Retry Policy

_taskTimeoutSeconds_ - How many seconds a Task is allowed to run before it is cancelled (Default 150 secs)

_retryDelayMilliseconds_ - How many milliseconds to wait before next retry (Default 500ms, and will be doubled on each retry).

_retryCount_ - How many attempts to retry the same Task, before it is aborted (Default 0)


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