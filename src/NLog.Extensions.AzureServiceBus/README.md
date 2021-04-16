# Azure ServiceBus

| Package Name                          | NuGet                 | Description |
| ------------------------------------- | :-------------------: | ----------- |
| **NLog.Extensions.AzureServiceBus** | [![NuGet](https://img.shields.io/nuget/v/NLog.Extensions.AzureServiceBus.svg)](https://www.nuget.org/packages/NLog.Extensions.AzureServiceBus/) | Azure Service Bus |

## ServiceBus Configuration

### Syntax
```xml
<extensions>
  <add assembly="NLog.Extensions.AzureServiceBus" /> 
</extensions>

<targets>
  <target xsi:type="AzureServiceBus"
          name="String"
          layout="Layout"
          connectionString="Layout"
          queueName="Layout"
          topicName="Layout"
          sessionId="Layout"
          partitionKey="Layout"
          contentType="Layout"
          label="Layout"
          messageId="Layout"
          correlationId="Layout">
            <userproperty name="mymeta" layout="mymetavalue" />   <!-- Multiple allowed -->
  </target>
</targets>
```

### Parameters

_name_ - Name of the target.

_connectionString_ - Azure storage connection string.  [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required.

_queueName_ - QueueName for multiple producers single consumer. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_topicName_ - TopicName for multiple producers and multiple subscribers. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_sessionId_ - SessionId-Key which Service Bus uses to generate PartitionId-hash. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_partitionKey_ - Partition-Key which Service Bus uses to generate PartitionId-hash. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_contentType_ - Service Bus Message Body ContentType to be assigned. [Layout](https://github.com/NLog/NLog/wiki/Layouts). Ex. application/json

_layout_ - Service Bus Message Body to be rendered and encoded as UTF8. [Layout](https://github.com/NLog/NLog/wiki/Layouts) Required. 

_label_ - Service Bus Message Label to be used as subject for the message. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_messageId_ - Service Bus Message MessageId. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_correlationId_ - Service Bus Message Correlationid. [Layout](https://github.com/NLog/NLog/wiki/Layouts)

_timeToLiveSeconds_ - Default Time-To-Live (TTL) for ServiceBus messages in seconds (Optional)

_timeToLiveDays_ - Default Time-To-Live (TTL) for ServiceBus messages in days (Optional)


### Batching Policy

_maxBatchSizeBytes_ - Max size of a single batch in bytes [Integer](https://github.com/NLog/NLog/wiki/Data-types) (Default=256*1024)

_batchSize_ - Number of LogEvents to send in a single batch (Default=100)

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

  > `connectionString="${configsetting:ConnectionStrings.AzureBus}"`

* Example appsettings.json on .NetCore:
```json
  {
    "ConnectionStrings": {
      "AzureBus": "UseDevelopmentStorage=true;"
    }
  }
```

#### Lookup ConnectionString from app.config

   > `connectionString="${appsetting:ConnectionStrings.AzureBus}"`

* Example app.config on .NetFramework:
```xml
   <configuration>
      <connectionStrings>
        <add name="AzureBus" connectionString="UseDevelopmentStorage=true;"/>
      </connectionStrings>
   </configuration>
```

#### Lookup ConnectionString from environment-variable

   > `connectionString="${environment:AZURE_STORAGE_CONNECTION_STRING}"`

#### Lookup ConnectionString from NLog GlobalDiagnosticsContext (GDC)
 
  > `connectionString="${gdc:AzureBusConnectionString}"`
  
* Example code for setting GDC-value:
```c#
  NLog.GlobalDiagnosticsContext.Set("AzureBusConnectionString", "UseDevelopmentStorage=true;");
```